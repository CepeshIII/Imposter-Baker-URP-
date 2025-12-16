using UnityEngine;


namespace ImposterBaker.Math
{
    public static class OctahedralMapping
    {
        public static Vector3 HemisphereToVector(Vector2 coord)
        {
            coord = new Vector2(coord.x + coord.y, coord.x - coord.y) * 0.5f;
            Vector3 vec = new Vector3(
                coord.x,
                1.0f - Vector2.Dot(Vector2.one,
                    new Vector2(Mathf.Abs(coord.x), Mathf.Abs(coord.y))
                ),
                coord.y
            );
            return Vector3.Normalize(vec);
        }

        public static Vector3 SphereToVector(Vector2 coord)
        {
            Vector3 n = new Vector3(coord.x, 1f - Mathf.Abs(coord.x) - Mathf.Abs(coord.y), coord.y);
            float t = Mathf.Clamp01(-n.y);
            n.x += n.x >= 0f ? -t : t;
            n.z += n.z >= 0f ? -t : t;
            return n;
        }
    }
}
