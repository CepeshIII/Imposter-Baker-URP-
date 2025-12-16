using ImposterBaker.Rendering;
using System.IO;
using UnityEditor;
using UnityEngine;


namespace ImposterBaker.Editor
{
    #if UNITY_EDITOR
    public static class TextureAssetUtils
    {
        public static string SaveRenderTexture(
            RenderTexture rt,
            string directoryPath,
            string name)
        {
            Texture2D tex = rt.ToTexture2D();
            tex.name = name;
            return WriteTexture(tex, directoryPath);
        }
        
        public static string WriteTexture(Texture2D tex, string path)
        {
            byte[] bytes = tex.EncodeToPNG();
            string fullPath = Path.Combine(path, tex.name + ".png");
            File.WriteAllBytes(fullPath, bytes);
        
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        
            return fullPath;
        }
    }
    #endif
}
