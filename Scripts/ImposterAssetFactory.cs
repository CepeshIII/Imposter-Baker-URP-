using ImposterBaker.Data;
using ImposterBaker.Geometry;
using ImposterBaker.Rendering;
using UnityEditor;
using UnityEngine;


namespace ImposterBaker.Editor.Assets
{
    public static class ImposterAssetFactory
    {
        public static void Create(GameObject target,
            string directoryPath,
            RenderTexture albedoPackRT,
            RenderTexture normalPackRT,
            ImposterBakeSettings settings)
        {
            // mesh
            CreateImposterMeshRendererAndFilter(target, directoryPath, settings, out MeshFilter meshFilter, out MeshRenderer meshRenderer);

            // textures
            Texture2D albedoMapTexture = albedoPackRT.ToTexture2D();
            albedoMapTexture.name = $"{target.name}_ImposterAlbedoMap";
            string albedoTexPath = TextureAssetUtils.WriteTexture(albedoMapTexture, directoryPath);

            Texture2D normalMapTexture = normalPackRT.ToTexture2D();
            normalMapTexture.name = $"{target.name}_ImposterNormalMap";
            string normalTexPath = TextureAssetUtils.WriteTexture(normalMapTexture, directoryPath);

            TextureImporter albedoTexImporter = CreateTextureImporter(albedoTexPath, settings.atlasResolution);
            albedoMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoTexPath);

            TextureImporter normalTexImporter = CreateTextureImporter(normalTexPath, settings.atlasResolution);
            normalMapTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(normalTexPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Shader imposterShader = Shader.Find("Shader Graphs/Imposter");
            CreateImposterMaterial(meshRenderer, imposterShader, target.name, directoryPath, albedoTexPath, normalTexPath, settings);

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


        private static void CreateImposterMeshRendererAndFilter(GameObject target, string directoryPath, 
            ImposterBakeSettings settings, out MeshFilter meshFilter, out MeshRenderer meshRenderer)
        {
            if (!target.TryGetComponent<MeshRenderer>(out meshRenderer))
                meshRenderer = target.AddComponent<MeshRenderer>();
            if (!target.TryGetComponent<MeshFilter>(out meshFilter))
                meshFilter = target.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = ImposterMeshFactory.CreateImposterMesh(settings.boundsOffset, settings.boundsRadius);

            string meshName = $"{target.name}_Mesh";
            string meshPath = $"{directoryPath}/{meshName}.asset";

            // Try to load existing mesh asset
            Mesh meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

            // Create new mesh if it doesn't exist
            if (meshAsset == null)
            {
                meshAsset = ImposterMeshFactory.CreateImposterMesh(settings.boundsOffset, settings.boundsRadius);
                meshAsset.name = meshName;
                AssetDatabase.CreateAsset(meshAsset, meshPath);
            }
            else
            {
                // Overwrite existing mesh with new geometry
                Mesh newMesh = ImposterMeshFactory.CreateImposterMesh(settings.boundsOffset, settings.boundsRadius);
                newMesh.name = meshName;
                EditorUtility.CopySerialized(newMesh, meshAsset);
                EditorUtility.SetDirty(meshAsset); // Mark asset as dirty so it saves changes
                AssetDatabase.SaveAssets();
            }

            meshFilter.sharedMesh = meshAsset; // Assign mesh to MeshFilter
        }


        private static void CreateImposterMaterial(MeshRenderer meshRenderer, Shader shader, string name, string directoryPath, 
            string albedoTexPath, string normalTexPath, ImposterBakeSettings settings)
        {
            // create material
            string materialName = $"{name}_ImposterMaterial";
            Material imposterMaterial = AssetDatabase.LoadAssetAtPath<Material>(directoryPath + "/" + materialName + ".asset");
            if (imposterMaterial == null)
            {
                imposterMaterial = new Material(shader);
                imposterMaterial.name = materialName;
                AssetDatabase.CreateAsset(imposterMaterial, directoryPath + "/" + imposterMaterial.name + ".asset");
            }
            imposterMaterial.SetTexture("_ImposterAlbedoMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedoTexPath));
            imposterMaterial.SetTexture("_ImposterNormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalTexPath));
            imposterMaterial.SetFloat("_ImposterIsHalfSphere", settings.useHalfSphere ? 1 : 0);
            imposterMaterial.SetFloat("_ImposterFrames", settings.frames);
            imposterMaterial.SetFloat("_ImposterSize", settings.boundsRadius);
            imposterMaterial.SetVector("_ImposterOffset", settings.boundsOffset);
            meshRenderer.sharedMaterial = imposterMaterial;
        }
    }

}
