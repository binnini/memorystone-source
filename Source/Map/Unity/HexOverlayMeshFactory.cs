using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    internal static class HexOverlayMeshFactory
    {
        public static Mesh CreateHexFilledMesh(float radius)
        {
            var r = Mathf.Max(0.05f, radius);
            var vertices = new Vector3[7];
            vertices[0] = Vector3.zero;
            for (var i = 0; i < 6; i++)
            {
                var angle = Mathf.Deg2Rad * (30f + i * 60f);
                vertices[i + 1] = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * r;
            }

            var triangles = new int[18];
            for (var i = 0; i < 6; i++)
            {
                triangles[i * 3]     = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % 6 + 1;
            }

            var mesh = new Mesh
            {
                name = "Hex Filled Mesh",
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateHexOutlineMesh(float radius)
        {
            var outerRadius = Mathf.Max(0.05f, radius);
            var ringThickness = Mathf.Clamp(outerRadius * 0.11f, 0.004f, outerRadius * 0.35f);
            var innerRadius = Mathf.Max(outerRadius * 0.1f, outerRadius - ringThickness);
            var vertices = new Vector3[12];
            for (var i = 0; i < 6; i++)
            {
                var angle = Mathf.Deg2Rad * (30f + i * 60f);
                var direction = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                vertices[i * 2] = direction * outerRadius;
                vertices[i * 2 + 1] = direction * innerRadius;
            }

            var triangles = new int[36];
            var index = 0;
            for (var i = 0; i < 6; i++)
            {
                var next = (i + 1) % 6;
                var outer = i * 2;
                var inner = outer + 1;
                var nextOuter = next * 2;
                var nextInner = nextOuter + 1;

                triangles[index++] = outer;
                triangles[index++] = inner;
                triangles[index++] = nextOuter;
                triangles[index++] = inner;
                triangles[index++] = nextInner;
                triangles[index++] = nextOuter;
            }

            var mesh = new Mesh
            {
                name = "Reachable Hex Outline Mesh",
                vertices = vertices,
                triangles = triangles
            };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
