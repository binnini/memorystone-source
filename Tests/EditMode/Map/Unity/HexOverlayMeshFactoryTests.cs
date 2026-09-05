using NUnit.Framework;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexOverlayMeshFactoryTests
    {
        [Test]
        public void CreateHexOutlineMeshBuildsExpectedRingGeometry()
        {
            var mesh = HexOverlayMeshFactory.CreateHexOutlineMesh(1f);
            try
            {
                Assert.That(mesh.name, Does.Contain("Hex Outline"));
                Assert.That(mesh.vertexCount, Is.EqualTo(12));
                Assert.That(mesh.triangles.Length, Is.EqualTo(36));
                Assert.That(mesh.bounds.extents.x, Is.GreaterThan(0.8f));
                Assert.That(mesh.bounds.extents.z, Is.GreaterThan(0.8f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void CreateHexOutlineMeshClampsTinyRadiusToUsableMesh()
        {
            var mesh = HexOverlayMeshFactory.CreateHexOutlineMesh(-1f);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(12));
                Assert.That(mesh.triangles.Length, Is.EqualTo(36));
                Assert.That(mesh.bounds.size.x, Is.GreaterThan(0f));
                Assert.That(mesh.bounds.size.z, Is.GreaterThan(0f));
                Assert.That(mesh.vertices[1].magnitude, Is.LessThan(mesh.vertices[0].magnitude));
                Assert.That(FirstTriangleArea(mesh), Is.GreaterThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
        private static float FirstTriangleArea(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var a = vertices[triangles[0]];
            var b = vertices[triangles[1]];
            var c = vertices[triangles[2]];
            return Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        }
    }
}

