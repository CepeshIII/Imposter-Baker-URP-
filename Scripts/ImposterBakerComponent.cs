using ImposterBaker.Data;
using ImposterBaker.Editor.Assets;
using ImposterBaker.Geometry;
using ImposterBaker.Math;
using ImposterBaker.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using static Unity.Burst.Intrinsics.X86.Avx;


namespace ImposterBaker.Data
{
    public struct ImposterBakeSettings
    {
        public int atlasResolution;
        public bool useHalfSphere;
        public int frames;
        public Vector3 boundsOffset;
        public float boundsRadius;
    }
}



namespace ImposterBaker.Editor
{
    #if UNITY_EDITOR

    public class ImposterBakerComponent : MonoBehaviour
    {
        private enum ImposterBakerPass : int
        {
            MinMax = 0,
            AlphaCopy = 1,
            DepthCopy = 2,
            MergeNormalsDepth = 3,
            Dilate = 4,
            MergeColorWithAlpha = 5,
            PartialDilate = 6,
            ChannelDifferenceMask = 7
        }
    
        private enum UnityShaderPass
        {
            ForwardLit,
            ShadowCaster,
            GBuffer,
            DepthOnly,
            DepthNormals,
            Meta,
        }

        private class MinMaxBakeData
        {
            public Snapshot snapshot;
            public RenderTexture texture;
            public int resolution;
        }

        private class RenderActualTilesData
        {
            public Snapshot snapshot;
            public RenderTexture[] gBuffer;
        }

        [Header("Settings")]
        [SerializeField] private int _atlasResolution = 2048;
        [SerializeField] private bool _useHalfSphere = true;
        [SerializeField] private int _frames = 12;
        [Range(0, 256)] private float _framePadding = 0;
        [Header("Baked")]
        [SerializeField]private Vector3 _boundsOffset;
        [SerializeField] private float _boundsRadius;
        [Tooltip(
        "Rotates the sampling directions used for octahedral capture.\n" +
        "Does not rotate the object itself.\n" +
        "Use this to bias detail toward a preferred axis (e.g. trees, towers)."
        )]
        [SerializeField] private Vector3 _samplingRotation;
    
        [Header("System")]
        // _materials + shaders
        [SerializeField] private Shader _imposterBakerShader;
        [SerializeField] private Material _imposterBakerMaterial;
        // rendering data
        [SerializeField] private UnityEngine.Renderer[] _renderers;
        [SerializeField] private Mesh[] _meshes;
        [SerializeField] private Material[][] _materials;
        [SerializeField] private Snapshot[] _snapshots;
        [SerializeField] private Bounds _bounds;

        [Header("Test")]
        [SerializeField] private Vector3 _from;
        [SerializeField] private Vector3 _to;
        [SerializeField] private Vector3 _UP;


        private Camera _camera;
        private MinMaxBakeData _minMaxBakeData;
        private RenderActualTilesData _renderActualTilesData;
        private string _directoryPath;
    
    
        bool CheckData()
        {
            if(_directoryPath == null)
            {
                Debug.LogError("Imposter Baker: No directory path set for saving imposter assets.");
                return false;
            }

            _imposterBakerShader = Shader.Find("IMP/ImposterBaker");
            if (_imposterBakerShader == null)
            {
                Debug.LogError("Imposter Baker: ImposterBaker shader not found.");
            }
            
            _imposterBakerMaterial = new Material(_imposterBakerShader);
            if(_imposterBakerMaterial == null)
            {
                Debug.LogError("imposterBaker Material is not valid");
                return false;
            }

            // find stuff to bake
            List<MeshRenderer> renderers = new List<MeshRenderer>(transform.GetComponentsInChildren<MeshRenderer>(true));
            renderers.Remove(gameObject.GetComponent<MeshRenderer>());

            if (renderers == null || renderers.Count == 0)
            {
                Debug.LogError("Imposter Baker: No MeshRenderers found to bake imposter from.");
                return false;
            }

            _renderers = renderers.ToArray();
            _meshes = new Mesh[_renderers.Length];
            _materials = new Material[_renderers.Length][];
    
            for (int i = 0; i < _renderers.Length; i++)
            {
                _meshes[i] = _renderers[i].GetComponent<MeshFilter>().sharedMesh;
                _materials[i] = new Material[_renderers[i].sharedMaterials.Length];
                for (int j = 0; j < _renderers[i].sharedMaterials.Length; j++)
                {
                    _materials[i][j] = new Material(_renderers[i].sharedMaterials[j]);
                }
            }
    
            // make sure frames are even
            if (_frames % 2 != 0)
                _frames -= 1;
    
            // make sure min is 2 x 2
            _frames = Mathf.Max(2, _frames);
    
            _bounds = new Bounds(_renderers[0].transform.position, Vector3.zero);
            for (int i = 0; i < _renderers.Length; i++)
            {
                Vector3[] verts = _meshes[i].vertices;
                for (int v = 0; v < verts.Length; v++)
                {
                    Vector3 meshWorldVert = _renderers[i].localToWorldMatrix.MultiplyPoint3x4(verts[v]);
                    Vector3 meshLocalToRoot = transform.worldToLocalMatrix.MultiplyPoint3x4(meshWorldVert);
                    Vector3 worldVert = transform.localToWorldMatrix.MultiplyPoint3x4(meshLocalToRoot);
                    _bounds.Encapsulate(worldVert);
                }
            }
    
            _boundsRadius = Vector3.Distance(_bounds.min, _bounds.max) * 0.5f;
            _boundsOffset = _bounds.center;
    
            _snapshots = SnapshotBuilder.Build(_frames, _boundsRadius, _boundsOffset, Quaternion.Euler(_samplingRotation), _useHalfSphere);
    
            return true;
        }


        [ContextMenu("Bake")]
        void Bake()
        {
            string assetPath = EditorUtility.SaveFilePanelInProject("Save Imposter Textures", 
                gameObject.name, "", "Select textures save location");
            _directoryPath = Path.GetDirectoryName(assetPath);
    
            // fix rotation bug ?
            Vector3 oldPosition = transform.position;
            Quaternion oldRotation = transform.rotation;
            Vector3 oldScale = transform.localScale;
    
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
    
            if (CheckData())
            {
                RenderTexture albedoPackRT = null;
                RenderTexture normalPackRT = null;

                SetupCamera();

                try
                {
                    albedoPackRT = RenderTexture.GetTemporary(_atlasResolution, _atlasResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    normalPackRT = RenderTexture.GetTemporary(_atlasResolution, _atlasResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    CaptureViews(albedoPackRT, normalPackRT);

                    var settings = new ImposterBakeSettings
                    {
                        atlasResolution = _atlasResolution,
                        boundsOffset = _boundsOffset,
                        boundsRadius = _boundsRadius,
                        frames = _frames,
                        useHalfSphere = _useHalfSphere,
                    };

                    ImposterAssetFactory.Create(gameObject, _directoryPath, albedoPackRT, normalPackRT, settings);
                } catch (Exception e)
                {
                    Debug.LogError($"Imposter Bake Failed");
                    Debug.LogException(e);
                }
    
                finally
                {
                    if (albedoPackRT != null)
                        RenderTexture.ReleaseTemporary(albedoPackRT);
                    if (normalPackRT != null)
                        RenderTexture.ReleaseTemporary(normalPackRT);

                    if(_camera != null)
                        DestroyImmediate(_camera.gameObject);
                }
            }
    
            transform.position = oldPosition;
            transform.rotation = oldRotation;
            transform.localScale = oldScale;
    
            AssetDatabase.Refresh();
        }


        private void SetupCamera()
        {
            _camera = new GameObject("ImposterBakerCamera").AddComponent<Camera>();
            _camera.cameraType = CameraType.Game;
            _camera.enabled = false;
            _camera.orthographic = true;
            _camera.farClipPlane = 1000f;
            _camera.nearClipPlane = 0;
        }


        void DrawMeshesToTarget(ImposterBakerPass pass)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                for (int j = 0; j < _meshes[i].subMeshCount; j++)
                {
                    _imposterBakerMaterial.SetPass((int)pass);
                    Graphics.DrawMeshNow(_meshes[i], _renderers[i].localToWorldMatrix, j);
                }
            }
        }


        void DrawMeshesToTarget(ImposterBakerPass pass, CommandBuffer cmd)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                for (int j = 0; j < _meshes[i].subMeshCount; j++)
                {
                    _imposterBakerMaterial.SetPass((int)pass);
                    cmd.DrawMesh(_meshes[i], _renderers[i].localToWorldMatrix, _imposterBakerMaterial, j);
                }
            }
        }


        void DrawMeshesToTarget(UnityShaderPass pass)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                var renderer = _renderers[i];
                var mesh = _meshes[i];
                var materials = _materials[i];
    
                for (int j = 0; j < mesh.subMeshCount; j++)
                {
                    var material = materials[j];
                    int passIndex = material.FindPass(Enum.GetName(typeof(UnityShaderPass), pass));
                    if (passIndex == -1)
                        continue;
                    material.SetPass(passIndex);
                    Graphics.DrawMeshNow(mesh, renderer.localToWorldMatrix, j);
                }
            }
        }


        void DrawMeshesToTarget(UnityShaderPass pass, CommandBuffer cmd)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                Mesh mesh = _meshes[i];
                Renderer renderer = _renderers[i];
                Material[] mats = _materials[i];

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material mat = mats[s];
                    int passIndex = mat.FindPass(Enum.GetName(typeof(UnityShaderPass), pass));

                    if (pass < 0)
                        continue;

                    mat.SetPass(passIndex);
                    cmd.DrawMesh(mesh, renderer.localToWorldMatrix, mat, s, passIndex);
                }
            }
        }


        private void MinMaxBake(CommandBuffer cmd)
        {
            cmd.SetViewport(new Rect(
                0, 0,
                _minMaxBakeData.texture.width,
                _minMaxBakeData.texture.height
            ));


            _minMaxBakeData.texture.Create();

            cmd.SetRenderTarget(_minMaxBakeData.texture);
            cmd.ClearRenderTarget(true, true, Color.clear);
            
            Matrix4x4 ortho = Matrix4x4.Ortho(-_boundsRadius, _boundsRadius,
                -_boundsRadius, _boundsRadius, 0, _boundsRadius * 2);
            cmd.SetProjectionMatrix(ortho);

            Matrix4x4 cameraMatrix = Matrix4x4.TRS(_minMaxBakeData.snapshot.position, Quaternion.LookRotation(_minMaxBakeData.snapshot.direction, Vector3.up), Vector3.one).inverse;
            cmd.SetViewMatrix(cameraMatrix);

            DrawMeshesToTarget(ImposterBakerPass.MinMax, cmd);
        }


        private void RenderActualTiles(CommandBuffer cmd)
        {
            Snapshot snapshot = _renderActualTilesData.snapshot;
            RenderTexture[] gBuffer = _renderActualTilesData.gBuffer;

            int w = gBuffer[0].width;
            int h = gBuffer[0].height;

            for (int i = 0; i < gBuffer.Length; i++)
                gBuffer[i].Create();

            cmd.SetViewport(new Rect(0, 0, w, h));

            RenderTargetIdentifier[] mrt = new RenderTargetIdentifier[gBuffer.Length];
            for (int i = 0; i < gBuffer.Length; i++)
                mrt[i] = new RenderTargetIdentifier(gBuffer[i]);

            cmd.SetRenderTarget(mrt, gBuffer[0].depthBuffer);
            cmd.ClearRenderTarget(true, true, Color.clear);

            float near = 0;
            float far = _boundsRadius * 2.0f;

            Matrix4x4 ortho = Matrix4x4.Ortho(
                -_boundsRadius, _boundsRadius,
                -_boundsRadius, _boundsRadius,
                near, far
            );

            var viewMatrix =
                Matrix4x4.TRS(snapshot.position, 
                Quaternion.LookRotation(snapshot.direction), 
                new Vector3(1, 1, -1)).inverse;

            cmd.SetViewMatrix(viewMatrix);
            cmd.SetProjectionMatrix(ortho);

            DrawMeshesToTarget(UnityShaderPass.GBuffer, cmd);

            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        }


        void CaptureViews(RenderTexture albedoPackRT, RenderTexture normalPackRT)
        {
            int frameResolution = _atlasResolution / _frames;
            FindBetterFrameBoundsSize();
            RenderFrames(frameResolution, albedoPackRT, normalPackRT);
        }
    

        // find better min max frame/boundsRadius size
        private void FindBetterFrameBoundsSize()
        {
            RenderTexture minMaxTileRT = RenderTexture.GetTemporary(_atlasResolution, _atlasResolution, 0, 
                RenderTextureFormat.R8, RenderTextureReadWrite.Linear);

            for (var i = 0; i < _snapshots.Length; i++)
            {
                _minMaxBakeData = new MinMaxBakeData
                {
                    snapshot = _snapshots[i],
                    texture = minMaxTileRT,
                    resolution = _atlasResolution,
                };

                var cmd = new CommandBuffer();
                MinMaxBake(cmd);
                Graphics.ExecuteCommandBuffer(cmd);
            }

            //now read render texture
            Texture2D tempMinMaxTex = minMaxTileRT.ToTexture2D();

            Color32[] tempTexC = tempMinMaxTex.GetPixels32();

            // start with max min
            Vector2 min = Vector2.one * _atlasResolution;
            Vector2 max = Vector2.zero;

            //loop pixels get min max
            for (int c = 0; c < tempTexC.Length; c++)
            {
                if (tempTexC[c].r != 0x00)
                {
                    Vector2 texPos = GridIndexUtils.ToXY(c, _atlasResolution);
                    min.x = Mathf.Min(min.x, texPos.x);
                    min.y = Mathf.Min(min.y, texPos.y);
                    max.x = Mathf.Max(max.x, texPos.x);
                    max.y = Mathf.Max(max.y, texPos.y);
                }
            }

            // padding
            min -= Vector2.one * _framePadding;
            max += Vector2.one * _framePadding;

            //rescale radius
            Vector2 len = new Vector2(max.x - min.x, max.y - min.y);

            float maxR = Mathf.Max(len.x, len.y);

            float ratio = maxR / _atlasResolution; //assume square

            _boundsRadius = _boundsRadius * ratio;

            //recalculate snapshots
            _snapshots = SnapshotBuilder.Build(_frames, _boundsRadius, _boundsOffset, 
                Quaternion.Euler(_samplingRotation), _useHalfSphere);

            RenderTexture.ReleaseTemporary(minMaxTileRT);
        }


        private void RenderFrames(int frameResolution, RenderTexture albedoPackRT, RenderTexture normalPackRT)
        {
            Shader.EnableKeyword("_RENDER_PASS_ENABLED");
            for (int frameIndex = 0; frameIndex < _snapshots.Length; frameIndex++)
            {
                // current snapshot
                Snapshot currentSnapshot = _snapshots[frameIndex];

                // set buffers
                RenderTexture[] gBuffer = new RenderTexture[5];
                gBuffer[0] = RenderTexture.GetTemporary(frameResolution, frameResolution, 32, 
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                gBuffer[1] = RenderTexture.GetTemporary(frameResolution, frameResolution, 0,  
                    RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
                gBuffer[2] = RenderTexture.GetTemporary(frameResolution, frameResolution, 32, 
                    RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                gBuffer[3] = RenderTexture.GetTemporary(frameResolution, frameResolution, 32, 
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                gBuffer[4] = RenderTexture.GetTemporary(frameResolution, frameResolution, 32, 
                    RenderTextureFormat.R16, RenderTextureReadWrite.Linear);

                _renderActualTilesData = new RenderActualTilesData
                {
                    snapshot = currentSnapshot,
                    gBuffer = gBuffer,
                };

                var cmd = new CommandBuffer();
                RenderActualTiles(cmd);
                Graphics.ExecuteCommandBuffer(cmd);

                // get frame rts
                RenderTexture depthFrameRT = RenderTexture.GetTemporary(frameResolution, frameResolution, 32, 
                    RenderTextureFormat.R16, RenderTextureReadWrite.Linear);
                RenderTexture albedoFrameRT = RenderTexture.GetTemporary(frameResolution, frameResolution, 0, 
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                RenderTexture normalFrameRT = RenderTexture.GetTemporary(frameResolution, frameResolution, 0, 
                    RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                RenderTexture dilateFrameRT = RenderTexture.GetTemporary(frameResolution, frameResolution, 0, 
                    RenderTextureFormat.R8, RenderTextureReadWrite.Linear);

                // clear frame targets
                Graphics.SetRenderTarget(depthFrameRT);
                GL.Clear(true, true, Color.clear);
                Graphics.SetRenderTarget(albedoFrameRT);
                GL.Clear(true, true, Color.clear);
                Graphics.SetRenderTarget(normalFrameRT);
                GL.Clear(true, true, Color.clear);
                Graphics.SetRenderTarget(dilateFrameRT);
                GL.Clear(true, true, Color.clear);

                PrepareDilateMask(gBuffer[3], dilateFrameRT);
                PrepareDepth(gBuffer[4], depthFrameRT);

                MergeAlbedoAlpha(gBuffer[0], gBuffer[3], albedoFrameRT);

                MergeNormalsDepthWithDilatePasses(frameResolution, gBuffer[2], depthFrameRT, dilateFrameRT, normalFrameRT);

                BlitToPackRts(frameIndex, albedoFrameRT, normalFrameRT, albedoPackRT, normalPackRT);

                // dispose
                RenderTexture.ReleaseTemporary(depthFrameRT);
                RenderTexture.ReleaseTemporary(albedoFrameRT);
                RenderTexture.ReleaseTemporary(normalFrameRT);
                RenderTexture.ReleaseTemporary(dilateFrameRT);

                // dispose
                for (int i = 0; i < gBuffer.Length; i++)
                    RenderTexture.ReleaseTemporary(gBuffer[i]);
            }

            Shader.DisableKeyword("_RENDER_PASS_ENABLED");
        }


        private void PrepareDilateMask(RenderTexture dilateRT, RenderTexture dilateFrameRT)
        {
            _imposterBakerMaterial.SetTexture("_AlphaMap", dilateRT);
            _imposterBakerMaterial.SetVector("_Channels", new Vector4(1, 0, 0, 0));
            _imposterBakerMaterial.SetPass((int)ImposterBakerPass.AlphaCopy);
            Graphics.Blit(dilateRT, dilateFrameRT, _imposterBakerMaterial, (int)ImposterBakerPass.AlphaCopy);
        }
    
    
        private void PrepareDepth(RenderTexture depthRT, RenderTexture depthFrameRT)
        {
            _imposterBakerMaterial.SetTexture("_DepthMap", depthRT);
            _imposterBakerMaterial.SetVector("_Channels", new Vector4(1, 0, 0, 0));
            _imposterBakerMaterial.SetPass((int)ImposterBakerPass.DepthCopy);
            Graphics.Blit(depthRT, depthFrameRT, _imposterBakerMaterial, (int)ImposterBakerPass.DepthCopy);
        }
    
    
        private void MergeAlbedoAlpha(RenderTexture albedoRT, RenderTexture alphaRT, RenderTexture albedoFrameRT)
        {
            // dilate albedo
            _imposterBakerMaterial.SetTexture("_BlitTexture", albedoRT);
            _imposterBakerMaterial.SetTexture("_AlphaMap", alphaRT);
            _imposterBakerMaterial.SetPass((int)ImposterBakerPass.MergeColorWithAlpha);
            Graphics.Blit(albedoRT, albedoFrameRT, _imposterBakerMaterial, (int)ImposterBakerPass.MergeColorWithAlpha);
            _imposterBakerMaterial.SetTexture("_BlitTexture", null);
            _imposterBakerMaterial.SetTexture("_AlphaMap", null);
        }
    
    
        private void MergeNormalsDepthWithDilatePasses(int frameResolution, RenderTexture normalsRT, 
            RenderTexture depthRT, RenderTexture dilateMaskRT, RenderTexture normalFrameRT)
        {
           // merge normals + depth
           var firstTempNormalsDepthRT = RenderTexture.GetTemporary(frameResolution, frameResolution, 0, 
               RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
           var secondTempNormalsDepthRT = RenderTexture.GetTemporary(frameResolution, frameResolution, 0, 
               RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
           var tempDilateFrameRT = RenderTexture.GetTemporary(frameResolution, frameResolution, 0, 
               RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
    
           Graphics.SetRenderTarget(firstTempNormalsDepthRT);
           GL.Clear(true, true, Color.clear);
           Graphics.SetRenderTarget(secondTempNormalsDepthRT);
           GL.Clear(true, true, Color.clear);
           Graphics.SetRenderTarget(firstTempNormalsDepthRT);
           GL.Clear(true, true, Color.clear);
           Graphics.SetRenderTarget(tempDilateFrameRT);
           GL.Clear(true, true, Color.clear);
    
           _imposterBakerMaterial.SetTexture("_NormalMap", normalsRT);
           _imposterBakerMaterial.SetTexture("_DepthMap", depthRT);
           _imposterBakerMaterial.SetPass((int)ImposterBakerPass.MergeNormalsDepth);
    
           Graphics.Blit(normalsRT, firstTempNormalsDepthRT, _imposterBakerMaterial, (int)ImposterBakerPass.MergeNormalsDepth);
           Graphics.Blit(dilateMaskRT, tempDilateFrameRT);
    
           var steps = Mathf.CeilToInt(frameResolution / 10f);
    
           for (int i = 0; i < MathF.Ceiling(steps / 2f); i++)
           {
               // First step of partial dilate
               _imposterBakerMaterial.SetTexture("_MainTex", firstTempNormalsDepthRT);
               _imposterBakerMaterial.SetTexture("_DilateMask", tempDilateFrameRT);
               _imposterBakerMaterial.SetVector("_Channels", new Vector4(1, 1, 1, 1));
               Graphics.Blit(firstTempNormalsDepthRT, secondTempNormalsDepthRT, _imposterBakerMaterial, (int)ImposterBakerPass.PartialDilate);
    
               //Generate difference mask by comparing the alpha channels of result and source
               _imposterBakerMaterial.SetTexture("_FirstTex", firstTempNormalsDepthRT);
               _imposterBakerMaterial.SetTexture("_SecondTex", secondTempNormalsDepthRT);
               _imposterBakerMaterial.SetVector("_Channels", new Vector4(0, 0, 0, 1));
               Graphics.Blit(firstTempNormalsDepthRT, tempDilateFrameRT, _imposterBakerMaterial, (int)ImposterBakerPass.ChannelDifferenceMask);
    
               // Clear first temp render target
               Graphics.SetRenderTarget(firstTempNormalsDepthRT);
               GL.Clear(true, true, Color.clear);
    
               // Second step of partial dilate
               _imposterBakerMaterial.SetTexture("_MainTex", secondTempNormalsDepthRT);
               _imposterBakerMaterial.SetTexture("_DilateMask", tempDilateFrameRT);
               _imposterBakerMaterial.SetVector("_Channels", new Vector4(1, 1, 1, 1));
               Graphics.Blit(secondTempNormalsDepthRT, firstTempNormalsDepthRT, _imposterBakerMaterial, (int)ImposterBakerPass.PartialDilate);
    
               //Generate difference mask by comparing the alpha channels of result and source
               _imposterBakerMaterial.SetTexture("_FirstTex", firstTempNormalsDepthRT);
               _imposterBakerMaterial.SetTexture("_SecondTex", secondTempNormalsDepthRT);
               _imposterBakerMaterial.SetVector("_Channels", new Vector4(0, 0, 0, 1));
               Graphics.Blit(firstTempNormalsDepthRT, tempDilateFrameRT, _imposterBakerMaterial, (int)ImposterBakerPass.ChannelDifferenceMask);
    
               // Clear second temp render target
               Graphics.SetRenderTarget(secondTempNormalsDepthRT);
               GL.Clear(true, true, Color.clear);
    
               // Clear textures properties
               _imposterBakerMaterial.SetTexture("_FirstTex", null);
               _imposterBakerMaterial.SetTexture("_SecondTex", null);
           }
    
           Graphics.Blit(firstTempNormalsDepthRT, normalFrameRT);
    
           RenderTexture.ReleaseTemporary(firstTempNormalsDepthRT);
           RenderTexture.ReleaseTemporary(secondTempNormalsDepthRT);
           RenderTexture.ReleaseTemporary(tempDilateFrameRT);
        }
    
    
        private void BlitToPackRts(int frameIndex, RenderTexture albedoFrameRT, 
            RenderTexture normalFrameRT, RenderTexture albedoPackRT, RenderTexture normalPackRT)
        {
            //convert 1D index to flattened octahedra coordinate
            int x;
            int y;
            //this is 0-(frames-1) ex, 0-(12-1) 0-11 (for 12 x 12 frames)
            GridIndexUtils.ToXY(frameIndex, _frames, out x, out y);
    
            //X Y position to write frame into atlas
            //this would be frame index * frame width, ex 2048/12 = 170.6 = 170
            //so 12 * 170 = 2040, loses 8 pixels on the right side of atlas and top of atlas
    
            x *= albedoFrameRT.width;
            y *= albedoFrameRT.height;
    
            //copy base frame into base render target
            Graphics.CopyTexture(albedoFrameRT, 0, 0, 0, 0, albedoFrameRT.width, albedoFrameRT.height, albedoPackRT, 0, 0, x, y);
    
            //copy normals frame into normals render target
            Graphics.CopyTexture(normalFrameRT, 0, 0, 0, 0, normalFrameRT.width, normalFrameRT.height, normalPackRT, 0, 0, x, y);
        }
        
    }
}
#endif