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



namespace ImposterBaker.Data
{
    [Serializable]
    public class ImposterBakeSettings
    {
        public int frames = 12;
        [Range(0, 256)]
        public int framePadding = 0;
        public int atlasResolution = 2048;
        public bool useHalfSphere  = true;
        [Tooltip(
            "Rotates the sampling directions used for octahedral capture.\n" +
            "Does not rotate the object itself.\n" +
            "Use this to bias detail toward a preferred axis (e.g. trees, towers)."
        )]
        public Vector3 samplingRotation;
        public string directoryPath;
    }

    [Serializable]
    public class ImposterBakeData
    {
        public Vector3 boundsOffset;
        public float boundsRadius;
        public Shader imposterBakerShader;
        public Material imposterBakerMaterial;

        public UnityEngine.Renderer[] renderers;
        public Mesh[] meshes;
        public Material[][] materials;
        public Snapshot[] snapshots;

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


        [SerializeField] private ImposterBakeSettings settings;
        [SerializeField] private ImposterBakeData data;

        private const int MIN_MAX_TEXTURE_RES = 512;


        bool TryPrepareData(ImposterBakeSettings settings, out ImposterBakeData data)
        {
            data = new();

            if (settings.directoryPath == null)
            {
                Debug.LogError("Imposter Baker: No directory path set for saving imposter assets.");
                return false;
            }

            data.imposterBakerShader = Shader.Find("IMP/ImposterBaker");
            if (data.imposterBakerShader == null)
            {
                Debug.LogError("Imposter Baker: ImposterBaker shader not found.");
            }

            data.imposterBakerMaterial = new Material(data.imposterBakerShader);
            if(data.imposterBakerMaterial == null)
            {
                Debug.LogError("imposterBaker Material is not valid");
                return false;
            }

            // find stuff to bake
            List<MeshRenderer> renderersList = new List<MeshRenderer>(transform.GetComponentsInChildren<MeshRenderer>(true));
            renderersList.Remove(gameObject.GetComponent<MeshRenderer>());

            if (renderersList == null || renderersList.Count == 0)
            {
                Debug.LogError("Imposter Baker: No MeshRenderers found to bake imposter from.");
                return false;
            }

            var renderers = renderersList.ToArray();
            var meshes = new Mesh[renderers.Length];
            var materials = new Material[renderers.Length][];
    
            for (int i = 0; i < renderers.Length; i++)
            {
                var meshFilter = renderers[i].GetComponent<MeshFilter>();
                if (meshFilter == null || meshFilter.sharedMesh == null)
                    continue;

                meshes[i] = meshFilter.sharedMesh;
                materials[i] = new Material[renderers[i].sharedMaterials.Length];
                for (int j = 0; j < renderers[i].sharedMaterials.Length; j++)
                {
                    materials[i][j] = new Material(renderers[i].sharedMaterials[j]);
                }
            }

            data.renderers = renderers;
            data.meshes = meshes;
            data.materials = materials;

            // make sure frames are even
            if (settings.frames % 2 != 0)
                settings.frames -= 1;

            // make sure min is 2 x 2
            settings.frames = Mathf.Max(2, settings.frames);

            var bounds = data.renderers[0].bounds;
            for (int i = 0; i < data.renderers.Length; i++)
            {
                Vector3[] verts = data.meshes[i].vertices;
                Matrix4x4 localToWorldMatrix = data.renderers[i].localToWorldMatrix;
                bounds.Encapsulate(GeometryUtility.CalculateBounds(verts, localToWorldMatrix));
            }

            data.boundsRadius = Vector3.Distance(bounds.min, bounds.max) * 0.5f;
            data.boundsOffset = bounds.center;

            data.snapshots = SnapshotBuilder.Build(
                settings.frames, 
                data.boundsRadius, 
                data.boundsOffset, 
                Quaternion.Euler(settings.samplingRotation), 
                settings.useHalfSphere);
    
            return true;
        }


        [ContextMenu("Bake")]
        void Bake()
        {
            string assetPath = EditorUtility.SaveFilePanelInProject("Save Imposter Textures", 
                gameObject.name, "", "Select textures save location");

            settings.directoryPath = Path.GetDirectoryName(assetPath);
    
            // fix rotation bug ?
            Vector3 oldPosition = transform.position;
            Quaternion oldRotation = transform.rotation;
            Vector3 oldScale = transform.localScale;
    
            transform.position = Vector3.zero;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            if (TryPrepareData(settings, out var data))
            {
                RenderTexture albedoPackRT = null;
                RenderTexture normalPackRT = null;

                try
                {
                    albedoPackRT = RenderTexture.GetTemporary(settings.atlasResolution, settings.atlasResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    normalPackRT = RenderTexture.GetTemporary(settings.atlasResolution, settings.atlasResolution, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                    CaptureViews(albedoPackRT, normalPackRT, settings, data);

                    ImposterAssetFactory.Create(gameObject, settings, data, albedoPackRT, normalPackRT);
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
                }
            }
    
            transform.position = oldPosition;
            transform.rotation = oldRotation;
            transform.localScale = oldScale;
    
            AssetDatabase.Refresh();
        }


        private static void DrawMeshesToTarget(ImposterBakeData data, ImposterBakerPass pass, CommandBuffer cmd)
        {
            for (int i = 0; i < data.renderers.Length; i++)
            {
                for (int j = 0; j < data.meshes[i].subMeshCount; j++)
                {
                    data.imposterBakerMaterial.SetPass((int)pass);
                    cmd.DrawMesh(data.meshes[i], data.renderers[i].localToWorldMatrix, data.imposterBakerMaterial, j);
                }
            }
        }


        private static void DrawMeshesToTarget(ImposterBakeData data, UnityShaderPass pass, CommandBuffer cmd)
        {
            for (int i = 0; i < data.renderers.Length; i++)
            {
                Mesh mesh = data.meshes[i];
                Renderer renderer = data.renderers[i];
                Material[] mats = data.materials[i];

                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    Material mat = mats[s];
                    int passIndex = mat.FindPass(Enum.GetName(typeof(UnityShaderPass), pass));

                    if (passIndex < 0)
                        continue;

                    mat.SetPass(passIndex);
                    cmd.DrawMesh(mesh, renderer.localToWorldMatrix, mat, s, passIndex);
                }
            }
        }


        private static void MinMaxBake(ImposterBakeData imposterBakeData, 
            RenderTexture texture, Snapshot snapshot, CommandBuffer cmd)
        {
            cmd.SetViewport(new Rect(
                0, 0,
                texture.width,
                texture.height
            ));

            cmd.SetRenderTarget(texture);

            float near = -imposterBakeData.boundsRadius * 2.0f;
            float far = imposterBakeData.boundsRadius * 2.0f;
            Matrix4x4 ortho = Matrix4x4.Ortho(
                -imposterBakeData.boundsRadius, imposterBakeData.boundsRadius,
                -imposterBakeData.boundsRadius, imposterBakeData.boundsRadius,
                near,
                far);
            cmd.SetProjectionMatrix(ortho);

            Matrix4x4 cameraMatrix = 
                Matrix4x4.TRS(snapshot.position, 
                Quaternion.LookRotation(snapshot.direction, Vector3.up), 
                new Vector3(1, 1, -1)).inverse;
            cmd.SetViewMatrix(cameraMatrix);

            DrawMeshesToTarget(imposterBakeData, ImposterBakerPass.MinMax, cmd);
        }


        private static void RenderActualTiles(ImposterBakeData data, Snapshot snapshot, 
            RenderTexture[] gBuffer, CommandBuffer cmd)
        {
            int w = gBuffer[0].width;
            int h = gBuffer[0].height;

            cmd.SetViewport(new Rect(0, 0, w, h));

            RenderTargetIdentifier[] mrt = new RenderTargetIdentifier[gBuffer.Length];
            for (int i = 0; i < gBuffer.Length; i++)
                mrt[i] = new RenderTargetIdentifier(gBuffer[i]);

            cmd.SetRenderTarget(mrt, gBuffer[0].depthBuffer);
            cmd.ClearRenderTarget(true, true, Color.clear);

            float near = 0;
            float far = data.boundsRadius * 2.0f;

            Matrix4x4 ortho = Matrix4x4.Ortho(
                -data.boundsRadius, data.boundsRadius,
                -data.boundsRadius, data.boundsRadius,
                near, far
            );

            var viewMatrix =
                Matrix4x4.TRS(snapshot.position, 
                Quaternion.LookRotation(snapshot.direction), 
                new Vector3(1, 1, -1)).inverse;

            cmd.SetViewMatrix(viewMatrix);
            cmd.SetProjectionMatrix(ortho);

            DrawMeshesToTarget(data, UnityShaderPass.GBuffer, cmd);

            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        }


        private static void CaptureViews(RenderTexture albedoPackRT, RenderTexture normalPackRT, 
            ImposterBakeSettings settings, ImposterBakeData imposterBakeData)
        {
            FindBetterFrameBoundsSize(settings, imposterBakeData);
            RenderFrames(settings, imposterBakeData, albedoPackRT, normalPackRT);
        }


        // find better min max frame/boundsRadius size
        private static void FindBetterFrameBoundsSize(ImposterBakeSettings settings, ImposterBakeData data)
        {
            RenderTexture minMaxTileRT = RenderTexture.GetTemporary(
                MIN_MAX_TEXTURE_RES, MIN_MAX_TEXTURE_RES, 0,
                RenderTextureFormat.R8, RenderTextureReadWrite.Linear);
            Graphics.SetRenderTarget(minMaxTileRT);
            GL.Clear(minMaxTileRT, true, Color.clear);

            // Render all snapshots to Texture
            RenderFramesByMinMaxPass(data, minMaxTileRT);

            //now read render texture
            Texture2D tempMinMaxTex = minMaxTileRT.ToTexture2D();
            Color32[] tempTexC = tempMinMaxTex.GetPixels32();

            var contentBounds = TextureAssetUtils.FindContentBounds(tempTexC, MIN_MAX_TEXTURE_RES);
            TextureAssetUtils.SaveRenderTexture(minMaxTileRT, settings.directoryPath, "MinMaxTileRT");
            var newRadius = ClampRadius(contentBounds, data.boundsRadius, MIN_MAX_TEXTURE_RES, settings.framePadding);
            data.boundsRadius = newRadius;

            //recalculate snapshots
            data.snapshots = SnapshotBuilder.Build(settings.frames, data.boundsRadius, data.boundsOffset,
                Quaternion.Euler(settings.samplingRotation), settings.useHalfSphere);
            
            RenderTexture.ReleaseTemporary(minMaxTileRT);
        }


        private static float ClampRadius(BoundsInt contentBounds, float radius, int resolution, int framePadding)
        {
            // Apply padding
            contentBounds.min -= Vector3Int.one * framePadding;
            contentBounds.max += Vector3Int.one * framePadding;

            var emptyMinBorder = (Vector2Int)contentBounds.min;
            var emptyMaxBorder = (Vector2Int.one * resolution - (Vector2Int)contentBounds.max);

            // CalculateRadius
            var minBorder = Mathf.Min(emptyMinBorder.x, emptyMaxBorder.x, emptyMinBorder.y, emptyMaxBorder.y);
            float normalizedContentSize = minBorder / (float)resolution;
            radius -= radius * normalizedContentSize * 2;

            return radius;
        }


        static private void RenderFramesByMinMaxPass(
            ImposterBakeData data, RenderTexture texture)
        {
            var cmd = new CommandBuffer();
            // Render all snapshots to Texture
            for (var i = 0; i < data.snapshots.Length; i++)
            {
                MinMaxBake(data, texture, data.snapshots[i], cmd);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Clear();
            }
            cmd.Release();
        }


        static private void RenderFrames(ImposterBakeSettings settings, ImposterBakeData data,
            RenderTexture albedoPackRT, RenderTexture normalPackRT)
        {
            int frameResolution = settings.atlasResolution / settings.frames;
            Shader.EnableKeyword("_RENDER_PASS_ENABLED");
            var cmd = new CommandBuffer();
            for (int frameIndex = 0; frameIndex < data.snapshots.Length; frameIndex++)
            {
                // current snapshot
                Snapshot currentSnapshot = data.snapshots[frameIndex];

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

                RenderActualTiles(data, currentSnapshot, gBuffer, cmd);
                Graphics.ExecuteCommandBuffer(cmd);
                cmd.Clear();

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

                PrepareDilateMask(data, gBuffer[3], dilateFrameRT);
                PrepareDepth(data, gBuffer[4], depthFrameRT);

                MergeAlbedoAlpha(data, gBuffer[0], gBuffer[3], albedoFrameRT);

                MergeNormalsDepthWithDilatePasses(data, frameResolution, gBuffer[2], depthFrameRT, dilateFrameRT, normalFrameRT);

                BlitToPackRts(settings.frames, frameIndex, albedoFrameRT, normalFrameRT, albedoPackRT, normalPackRT);

                // dispose
                RenderTexture.ReleaseTemporary(depthFrameRT);
                RenderTexture.ReleaseTemporary(albedoFrameRT);
                RenderTexture.ReleaseTemporary(normalFrameRT);
                RenderTexture.ReleaseTemporary(dilateFrameRT);

                // dispose
                for (int i = 0; i < gBuffer.Length; i++)
                    RenderTexture.ReleaseTemporary(gBuffer[i]);
            }
            cmd.Release();

            Shader.DisableKeyword("_RENDER_PASS_ENABLED");
        }


        static private void PrepareDilateMask(ImposterBakeData data, RenderTexture dilateRT, RenderTexture dilateFrameRT)
        {
            data.imposterBakerMaterial.SetTexture("_AlphaMap", dilateRT);
            data.imposterBakerMaterial.SetVector("_Channels", new Vector4(1, 0, 0, 0));
            data.imposterBakerMaterial.SetPass((int)ImposterBakerPass.AlphaCopy);
            Graphics.Blit(dilateRT, dilateFrameRT, data.imposterBakerMaterial, (int)ImposterBakerPass.AlphaCopy);
        }


        static private void PrepareDepth(ImposterBakeData data, RenderTexture depthRT, RenderTexture depthFrameRT)
        {
            data.imposterBakerMaterial.SetTexture("_DepthMap", depthRT);
            data.imposterBakerMaterial.SetVector("_Channels", new Vector4(1, 0, 0, 0));
            data.imposterBakerMaterial.SetPass((int)ImposterBakerPass.DepthCopy);
            Graphics.Blit(depthRT, depthFrameRT, data.imposterBakerMaterial, (int)ImposterBakerPass.DepthCopy);
        }


        static private void MergeAlbedoAlpha(ImposterBakeData data, RenderTexture albedoRT, RenderTexture alphaRT, RenderTexture albedoFrameRT)
        {
            // dilate albedo
            data.imposterBakerMaterial.SetTexture("_BlitTexture", albedoRT);
            data.imposterBakerMaterial.SetTexture("_AlphaMap", alphaRT);
            data.imposterBakerMaterial.SetPass((int)ImposterBakerPass.MergeColorWithAlpha);
            Graphics.Blit(albedoRT, albedoFrameRT, data.imposterBakerMaterial, (int)ImposterBakerPass.MergeColorWithAlpha);
            data.imposterBakerMaterial.SetTexture("_BlitTexture", null);
            data.imposterBakerMaterial.SetTexture("_AlphaMap", null);
        }


        static private void MergeNormalsDepthWithDilatePasses(ImposterBakeData data, int frameResolution, RenderTexture normalsRT, 
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
    
           data.imposterBakerMaterial.SetTexture("_NormalMap", normalsRT);
           data.imposterBakerMaterial.SetTexture("_DepthMap", depthRT);
           data.imposterBakerMaterial.SetPass((int)ImposterBakerPass.MergeNormalsDepth);
    
           Graphics.Blit(normalsRT, firstTempNormalsDepthRT, data.imposterBakerMaterial, (int)ImposterBakerPass.MergeNormalsDepth);
           Graphics.Blit(dilateMaskRT, tempDilateFrameRT);
    
           var steps = Mathf.CeilToInt(frameResolution / 10f);
    
           for (int i = 0; i < MathF.Ceiling(steps / 2f); i++)
           {
               // First step of partial dilate
               data.imposterBakerMaterial.SetTexture("_MainTex", firstTempNormalsDepthRT);
               data.imposterBakerMaterial.SetTexture("_DilateMask", tempDilateFrameRT);
               data.imposterBakerMaterial.SetVector("_Channels", new Vector4(1, 1, 1, 1));
               Graphics.Blit(firstTempNormalsDepthRT, secondTempNormalsDepthRT, data.imposterBakerMaterial, (int)ImposterBakerPass.PartialDilate);
    
               //Generate difference mask by comparing the alpha channels of result and source
               data.imposterBakerMaterial.SetTexture("_FirstTex", firstTempNormalsDepthRT);
               data.imposterBakerMaterial.SetTexture("_SecondTex", secondTempNormalsDepthRT);
               data.imposterBakerMaterial.SetVector("_Channels", new Vector4(0, 0, 0, 1));
               Graphics.Blit(firstTempNormalsDepthRT, tempDilateFrameRT, data.imposterBakerMaterial, (int)ImposterBakerPass.ChannelDifferenceMask);
    
               // Clear first temp render target
               Graphics.SetRenderTarget(firstTempNormalsDepthRT);
               GL.Clear(true, true, Color.clear);
    
               // Second step of partial dilate
               data.imposterBakerMaterial.SetTexture("_MainTex", secondTempNormalsDepthRT);
               data.imposterBakerMaterial.SetTexture("_DilateMask", tempDilateFrameRT);
               data.imposterBakerMaterial.SetVector("_Channels", new Vector4(1, 1, 1, 1));
               Graphics.Blit(secondTempNormalsDepthRT, firstTempNormalsDepthRT, data.imposterBakerMaterial, (int)ImposterBakerPass.PartialDilate);
    
               //Generate difference mask by comparing the alpha channels of result and source
               data.imposterBakerMaterial.SetTexture("_FirstTex", firstTempNormalsDepthRT);
               data.imposterBakerMaterial.SetTexture("_SecondTex", secondTempNormalsDepthRT);
               data.imposterBakerMaterial.SetVector("_Channels", new Vector4(0, 0, 0, 1));
               Graphics.Blit(firstTempNormalsDepthRT, tempDilateFrameRT, data.imposterBakerMaterial, (int)ImposterBakerPass.ChannelDifferenceMask);
    
               // Clear second temp render target
               Graphics.SetRenderTarget(secondTempNormalsDepthRT);
               GL.Clear(true, true, Color.clear);
    
               // Clear textures properties
               data.imposterBakerMaterial.SetTexture("_FirstTex", null);
               data.imposterBakerMaterial.SetTexture("_SecondTex", null);
           }
    
           Graphics.Blit(firstTempNormalsDepthRT, normalFrameRT);
    
           RenderTexture.ReleaseTemporary(firstTempNormalsDepthRT);
           RenderTexture.ReleaseTemporary(secondTempNormalsDepthRT);
           RenderTexture.ReleaseTemporary(tempDilateFrameRT);
        }


        static private void BlitToPackRts(int framesCount, int frameIndex, RenderTexture albedoFrameRT, 
            RenderTexture normalFrameRT, RenderTexture albedoPackRT, RenderTexture normalPackRT)
        {
            //convert 1D index to flattened octahedra coordinate
            int x;
            int y;
            //this is 0-(frames-1) ex, 0-(12-1) 0-11 (for 12 x 12 frames)
            GridIndexUtils.ToXY(frameIndex, framesCount, out x, out y);
    
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