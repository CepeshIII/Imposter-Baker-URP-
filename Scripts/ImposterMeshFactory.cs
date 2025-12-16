using UnityEngine;

namespace ImposterBaker.Geometry
{
    public static class ImposterMeshFactory
    {
        public static Mesh CreateImposterMesh(Vector3 boundsOffset, float boundsRadius)
        {
            var vertices = new[]
            {
                    new Vector3(0f, 0.0f, 0f),
                    new Vector3(-0.5f, 0.0f, -0.5f),
                    new Vector3(0.5f, 0.0f, -0.5f),
                    new Vector3(0.5f, 0.0f, 0.5f),
                    new Vector3(-0.5f, 0.0f, 0.5f)
                };
    
            var triangles = new[]
            {
                    2, 1, 0,
                    3, 2, 0,
                    4, 3, 0,
                    1, 4, 0
                };
    
            var uv = new[]
            {
                    //UV matched to verts
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.0f, 0.0f),
                    new Vector2(1.0f, 0.0f),
                    new Vector2(1.0f, 1.0f),
                    new Vector2(0.0f, 1.0f)
                };
    
            var normals = new[]
            {
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 1f, 0f),
                    new Vector3(0f, 1f, 0f)
                };
    
            var mesh = new Mesh
            {
                vertices = vertices,
                uv = uv,
                normals = normals,
                tangents = new Vector4[5]
            };
            mesh.SetTriangles(triangles, 0);
            mesh.bounds = new Bounds(Vector3.zero + boundsOffset, 2f * boundsRadius * Vector3.one);
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
