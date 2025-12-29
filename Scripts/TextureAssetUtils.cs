using ImposterBaker.Math;
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
            Texture2D tex = rt.ToTexture2D(true, true);
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


        static public BoundsInt FindContentBounds(Color32[] colors, int resolution)
        {
            // start with max min
            Vector2Int min = Vector2Int.one * resolution;
            Vector2Int max = Vector2Int.zero;

            //loop pixels get min max
            for (int c = 0; c < colors.Length; c++)
            {
                if (colors[c].r != 0x00)
                {
                    var texPos = GridIndexUtils.ToXY(c, resolution);
                    min.x = Mathf.Min(min.x, texPos.x);
                    min.y = Mathf.Min(min.y, texPos.y);
                    max.x = Mathf.Max(max.x, texPos.x);
                    max.y = Mathf.Max(max.y, texPos.y);
                }
            }
            var size = max - min;
            return new BoundsInt(min.x, min.y, 0, size.x, size.y, 0);
        }
    }
    #endif
}
