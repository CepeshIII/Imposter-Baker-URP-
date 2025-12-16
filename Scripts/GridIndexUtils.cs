using UnityEngine;



namespace ImposterBaker.Math
{
    public static class GridIndexUtils
    {
        public static Vector2Int ToXY(int index, int width)
        {
            int x = index % width;
            int y = (index - x) / width;
            return new Vector2Int(x, y);
        }
    
        public static void ToXY(int index, int width, out int x, out int y)
        {
            x = index % width;
            y = (index - x) / width;
        }
    }
}
