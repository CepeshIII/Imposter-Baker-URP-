using ImposterBaker.Math;
using UnityEngine;


namespace ImposterBaker.Geometry
{
    public struct Snapshot
    {
        public Vector3 position;
        public Vector3 direction;
    }


    public static class SnapshotBuilder
    {
        public static Snapshot[] Build(int frames, float radius, 
            Vector3 origin, Quaternion samplingUpRotation,  bool isHalf = true)
        {
            Snapshot[] snapshots = new Snapshot[frames * frames];

            float framesMinusOne = frames - 1;

            int i = 0;
            for (int y = 0; y < frames; y++)
            {
                for (int x = 0; x < frames; x++)
                {
                    Vector2 vec = new Vector2(
                        x / framesMinusOne * 2f - 1f,
                        y / framesMinusOne * 2f - 1f
                    );
                    Vector3 ray = isHalf ? OctahedralMapping.HemisphereToVector(vec) : OctahedralMapping.SphereToVector(vec);

                    ray = samplingUpRotation * ray.normalized;

                    snapshots[i].position = origin + ray * radius;
                    snapshots[i].direction = -ray;
                    i++;
                }
            }

            return snapshots;
        }
    }
}
