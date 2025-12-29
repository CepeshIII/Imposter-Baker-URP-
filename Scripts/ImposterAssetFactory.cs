using ImposterBaker.Data;
using ImposterBaker.Geometry;
using ImposterBaker.Rendering;
using System.IO;
using UnityEditor;
using UnityEngine;


namespace ImposterBaker.Editor.Assets
{
    public static class ImposterAssetFactory
    {
        public static void Create(GameObject target,
            ImposterBakeSettings settings,
            ImposterBakeData data,
            RenderTexture albedoPackRT,
            RenderTexture normalPackRT)
        {
            // newMeshFilter
            CreateImposterMeshRendererAndFilter(target, settings, data, out MeshFilter meshFilter, out MeshRenderer meshRenderer);

            // textures
            Texture2D albedoMapTexture = albedoPackRT.ToTexture2D(true, true);
            albedoMapTexture.name = $"{target.name}_ImposterAlbedoMap";
            string albedoTexPath = TextureAssetUtils.WriteTexture(albedoMapTexture, settings.directoryPath);

            Texture2D normalMapTexture = normalPackRT.ToTexture2D(true, true);
            normalMapTexture.name = $"{target.name}_ImposterNormalMap";
            string normalTexPath = TextureAssetUtils.WriteTexture(normalMapTexture, settings.directoryPath);

            TextureImporter albedoTexImporter = CreateTextureImporter(albedoTexPath, settings.atlasResolution);
            albedoMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoTexPath);

            TextureImporter normalTexImporter = CreateTextureImporter(normalTexPath, settings.atlasResolution);
            normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalTexPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Shader imposterShader = Shader.Find("Shader Graphs/Imposter");
            var material = CreateImposterMaterial(meshRenderer, imposterShader, target.name, albedoTexPath, normalTexPath, settings, data);

            CreatePrefab(meshFilter, meshRenderer, target.name, settings.directoryPath);

            EditorUtility.SetDirty(target);
            AssetDatabase.SaveAssets();
        }


        private static TextureImporter CreateTextureImporter(string texPath, int resolution)
        {
            TextureImporter texImporter = AssetImporter.GetAtPath(texPath) as TextureImporter;
            if (texImporter != null)
            {
                texImporter.textureType = TextureImporterType.Default;
                texImporter.maxTextureSize = resolution;
                texImporter.alphaSource = TextureImporterAlphaSource.FromInput;
                texImporter.alphaIsTransparency = false;
                texImporter.sRGBTexture = false;
                texImporter.SaveAndReimport();
            }
            return texImporter;
        }


        private static void CreateImposterMeshRendererAndFilter(GameObject target, ImposterBakeSettings settings,
            ImposterBakeData data, out MeshFilter meshFilter, out MeshRenderer meshRenderer)
        {
            if (!target.TryGetComponent<MeshRenderer>(out meshRenderer))
                meshRenderer = target.AddComponent<MeshRenderer>();
            if (!target.TryGetComponent<MeshFilter>(out meshFilter))
                meshFilter = target.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = ImposterMeshFactory.CreateImposterMesh(data.boundsOffset, data.boundsRadius);

            string meshName = $"{target.name}_Mesh";
            string meshPath = $"{settings.directoryPath}/{meshName}.asset";

            // Try to load existing newMeshFilter asset
            Mesh meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            // Create new newMeshFilter if it doesn't exist
            if (meshAsset == null)
            {
                meshAsset = ImposterMeshFactory.CreateImposterMesh(data.boundsOffset, data.boundsRadius);
                meshAsset.name = meshName;
                AssetDatabase.CreateAsset(meshAsset, meshPath);
            }
            else
            {
                // Overwrite existing newMeshFilter with new geometry
                Mesh newMesh = ImposterMeshFactory.CreateImposterMesh(data.boundsOffset, data.boundsRadius);
                newMesh.name = meshName;
                EditorUtility.CopySerialized(newMesh, meshAsset);
                EditorUtility.SetDirty(meshAsset); // Mark asset as dirty so it saves changes
                AssetDatabase.SaveAssets();
            }

            meshFilter.sharedMesh = meshAsset; // Assign newMeshFilter to MeshFilter
        }


        private static Material CreateImposterMaterial(MeshRenderer meshRenderer, Shader shader, string name,
            string albedoTexPath, string normalTexPath, ImposterBakeSettings settings, ImposterBakeData data)
        {
            // create newMeshRenderer
            string materialName = $"{name}_ImposterMaterial";
            Material imposterMaterial = AssetDatabase.LoadAssetAtPath<Material>(settings.directoryPath + "/" + materialName + ".asset");
            if (imposterMaterial == null)
            {
                imposterMaterial = new Material(shader);
                imposterMaterial.name = materialName;
                AssetDatabase.CreateAsset(imposterMaterial, settings.directoryPath + "/" + imposterMaterial.name + ".asset");
            }
            imposterMaterial.SetTexture("_ImposterAlbedoMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoTexPath));
            imposterMaterial.SetTexture("_ImposterNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalTexPath));
            imposterMaterial.SetFloat("_ImposterIsHalfSphere", settings.useHalfSphere ? 1 : 0);
            imposterMaterial.SetFloat("_ImposterFrames", settings.frames);
            imposterMaterial.SetFloat("_ImposterSize", data.boundsRadius);
            imposterMaterial.SetVector("_ImposterOffset", data.boundsOffset);
            meshRenderer.sharedMaterial = imposterMaterial;
            return imposterMaterial;
        }


        private static void CreatePrefab(
            MeshFilter sourceMeshFilter,
            MeshRenderer sourceMeshRenderer,
            string name,
            string path)
        {
            string prefabPath = Path.Combine(path, $"{name}_Imposter.prefab");

            GameObject prefabRoot;

            // ---------- CREATE ----------
            if (!File.Exists(prefabPath))
            {
                prefabRoot = new GameObject(name);

                var mf = prefabRoot.AddComponent<MeshFilter>();
                mf.sharedMesh = sourceMeshFilter.sharedMesh;

                var mr = prefabRoot.AddComponent<MeshRenderer>();
                mr.sharedMaterial = sourceMeshRenderer.sharedMaterial;

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
                Object.DestroyImmediate(prefabRoot);

                return;
            }

            // ---------- UPDATE ----------
            prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);

            var meshFilter = prefabRoot.GetComponent<MeshFilter>();
            if (meshFilter == null)
                meshFilter = prefabRoot.AddComponent<MeshFilter>();

            meshFilter.sharedMesh = sourceMeshFilter.sharedMesh;

            var meshRenderer = prefabRoot.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                meshRenderer = prefabRoot.AddComponent<MeshRenderer>();

            meshRenderer.sharedMaterial = sourceMeshRenderer.sharedMaterial;

            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }

    }

}
