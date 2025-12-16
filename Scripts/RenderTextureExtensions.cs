using UnityEngine;


namespace ImposterBaker.Rendering
{
    
    public static class RenderTextureExtensions
    {
        public static TextureFormat ToTextureFormat(this RenderTextureFormat renderTextureFormat)
        {
            TextureFormat format;

            switch (renderTextureFormat)
            {
                case RenderTextureFormat.ARGB32:
                    format = TextureFormat.ARGB32;
                    break;
                case RenderTextureFormat.ARGBHalf:
                    format = TextureFormat.RGBAHalf;
                    break;
                case RenderTextureFormat.ARGBFloat:
                    format = TextureFormat.RGBAFloat;
                    break;
                case RenderTextureFormat.R8:
                    format = TextureFormat.R8;
                    break;
                case RenderTextureFormat.R16:
                    format = TextureFormat.R16;
                    break;
                case RenderTextureFormat.RG16:
                    format = TextureFormat.RG16;
                    break;
                case RenderTextureFormat.RG32:
                    format = TextureFormat.RGBA32;
                    break;
                // add more as needed
                default:
                    Debug.LogWarning($"RenderTexture format {renderTextureFormat} not explicitly handled, defaulting to ARGB32.");
                    format = TextureFormat.ARGB32;
                    break;
            }

            return format;
        }
    

        public static Texture2D ToTexture2D(this RenderTexture renderTexture)
        {
            TextureFormat format = renderTexture.format.ToTextureFormat();
            Texture2D texture2D = new Texture2D(renderTexture.width, renderTexture.height, format, true, true);

            Graphics.SetRenderTarget(renderTexture);
            texture2D.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
            texture2D.Apply();

            return texture2D;
        }
    }

}