using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using System.Linq;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatOverlayMeshBuilderTests
    {
        [Test]
        public void SingleHexFillBuildsFanMesh()
        {
            var builder = new CombatOverlayMeshBuilder();
            var mesh = builder.BuildFillMesh(new[] { new HexCoord(0, 0) }, new FakeProjector());
            try
            {
                // 13-vertex fan: shared center + 2 un-shared corners per side (so each side carries a
                // per-edge outer flag). Six triangles → 18 indices.
                Assert.That(mesh.vertexCount, Is.EqualTo(13));
                Assert.That(mesh.triangles.Length, Is.EqualTo(18));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void SingleHexBoundaryBuildsSixEdgeQuads()
        {
            var builder = new CombatOverlayMeshBuilder();
            var mesh = builder.BuildBoundaryMesh(new[] { new HexCoord(0, 0) }, new FakeProjector(), 0.1f);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(24));
                Assert.That(mesh.triangles.Length, Is.EqualTo(36));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AdjacentHexesOmitSharedBoundaryEdge()
        {
            var builder = new CombatOverlayMeshBuilder();
            var mesh = builder.BuildBoundaryMesh(new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, new FakeProjector(), 0.1f);
            try
            {
                Assert.That(mesh.vertexCount, Is.EqualTo(40));
                Assert.That(mesh.triangles.Length, Is.EqualTo(60));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }


        [Test]
        public void AdjacentHexesRemoveSharedBoundaryForEveryDirection()
        {
            var builder = new CombatOverlayMeshBuilder();
            var projector = new FakeProjector();
            var center = new HexCoord(0, 0);

            for (var direction = 0; direction < HexCoord.Directions.Count; direction++)
            {
                var neighbor = center.Neighbor((HexDirection)direction);
                var mesh = builder.BuildBoundaryMesh(new[] { center, neighbor }, projector, 0.1f);
                try
                {
                    Assert.That(mesh.vertexCount, Is.EqualTo(40), $"Direction {(HexDirection)direction} should emit ten outer edge quads.");
                    AssertSharedEdgeIsAbsent(mesh, projector, center, neighbor, (HexDirection)direction);
                }
                finally
                {
                    Object.DestroyImmediate(mesh);
                }
            }
        }

        [Test]
        public void UsesOverlaySurfaceProjectorWhenAvailable()
        {
            var builder = new CombatOverlayMeshBuilder();
            var projector = new FakeOverlaySurfaceProjector();
            var mesh = builder.BuildBoundaryMesh(new[] { new HexCoord(0, 0) }, projector, 0.1f);
            try
            {
                Assert.That(mesh.vertexCount, Is.GreaterThan(0));
                Assert.That(mesh.bounds.center.y, Is.EqualTo(0.5f + HexOverlayRenderOrder.CombatBoundaryLift).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }


        [Test]
        public void DefaultFillAndBoundaryLiftsFollowSharedRenderOrder()
        {
            var builder = new CombatOverlayMeshBuilder();
            var projector = new FakeOverlaySurfaceProjector();
            var fill = builder.BuildFillMesh(new[] { new HexCoord(0, 0) }, projector);
            var boundary = builder.BuildBoundaryMesh(new[] { new HexCoord(0, 0) }, projector, 0.1f);
            try
            {
                Assert.That(fill.bounds.center.y, Is.EqualTo(0.5f + HexOverlayRenderOrder.CombatFillLift).Within(0.001f));
                Assert.That(boundary.bounds.center.y, Is.EqualTo(0.5f + HexOverlayRenderOrder.CombatBoundaryLift).Within(0.001f));
                Assert.That(fill.bounds.center.y, Is.GreaterThan(0.5f + HexOverlayRenderOrder.VisibilityFogLift));
                Assert.That(boundary.bounds.center.y, Is.GreaterThan(fill.bounds.center.y));
            }
            finally
            {
                Object.DestroyImmediate(fill);
                Object.DestroyImmediate(boundary);
            }
        }

        [Test]
        public void SingleHexFillGlowsOnEveryEdgeAndKeepsCenterDark()
        {
            var builder = new CombatOverlayMeshBuilder();
            var mesh = builder.BuildFillMesh(new[] { new HexCoord(0, 0) }, new FakeProjector());
            try
            {
                var colors = mesh.colors;
                Assert.That(colors.Length, Is.EqualTo(13));
                // Per-edge outer flag (.r): an isolated tile has all six edges outer → 12 corner verts flagged.
                Assert.That(colors.Count(c => c.r > 0.5f), Is.EqualTo(12), "All six outer edges flag both their corners.");
                Assert.That(colors.Count(c => c.r < 0.5f), Is.EqualTo(1), "Only the center vertex stays dark.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void AdjacentHexFillFlagsOnlyOuterEdgesNotTheSharedEdge()
        {
            var builder = new CombatOverlayMeshBuilder();
            var mesh = builder.BuildFillMesh(new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, new FakeProjector());
            try
            {
                var colors = mesh.colors;
                Assert.That(colors.Length, Is.EqualTo(26));
                // Per-edge (not per-corner): each tile has 5 outer edges (10 flagged corner verts) and 1 shared
                // inner edge (2 unflagged) plus its center. The shared edge stays dark so glow never leaks
                // across it into the region interior.
                Assert.That(colors.Count(c => c.r > 0.5f), Is.EqualTo(20), "Five outer edges per tile → ten flagged corners each.");
                Assert.That(colors.Count(c => c.r < 0.5f), Is.EqualTo(6), "Two centers + the shared-edge corners of each tile.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void InteriorTileHasNoOuterEdgeSoGlowHugsThePerimeter()
        {
            // A center tile fully surrounded by its six neighbors has no outer edge, so its entire fan must
            // be unflagged (glow only hugs the region's outer ring, never an interior tile).
            var builder = new CombatOverlayMeshBuilder();
            var center = new HexCoord(0, 0);
            var coords = new System.Collections.Generic.List<HexCoord> { center };
            for (var d = 0; d < HexCoord.Directions.Count; d++)
            {
                coords.Add(center.Neighbor((HexDirection)d));
            }

            var mesh = builder.BuildFillMesh(coords, new FakeProjector());
            try
            {
                // Each tile is a 13-vertex fan (center + 12 corners). Exactly one fan — the surrounded center —
                // has every corner unflagged. Order-independent by scanning fans.
                var colors = mesh.colors;
                Assert.That(colors.Length, Is.EqualTo(coords.Count * 13));
                var fullyInteriorFans = 0;
                for (var fan = 0; fan < coords.Count; fan++)
                {
                    var allCornersDark = true;
                    for (var corner = 1; corner <= 12; corner++)
                    {
                        if (colors[fan * 13 + corner].r >= 0.5f)
                        {
                            allCornersDark = false;
                            break;
                        }
                    }

                    if (allCornersDark)
                    {
                        fullyInteriorFans++;
                    }
                }

                Assert.That(fullyInteriorFans, Is.EqualTo(1), "Only the fully-surrounded center tile stays glow-free.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FillRadialChannelIsZeroAtCenterAndOneAtCorners()
        {
            // Vertex color .g = radialHex01 (0 at each tile center, 1 at every corner) drives the shader's
            // feather + smooth glow band. Each tile is a 13-vertex fan: index 0 center, 1..12 corners.
            var builder = new CombatOverlayMeshBuilder();
            var mesh = builder.BuildFillMesh(new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, new FakeProjector());
            try
            {
                var colors = mesh.colors;
                Assert.That(colors.Length, Is.EqualTo(26));
                for (var fan = 0; fan < 2; fan++)
                {
                    Assert.That(colors[fan * 13].g, Is.EqualTo(0f).Within(0.001f), "Center radial must be 0.");
                    for (var corner = 1; corner <= 12; corner++)
                    {
                        Assert.That(colors[fan * 13 + corner].g, Is.EqualTo(1f).Within(0.001f), "Corner radial must be 1.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void BoundaryUvProgressesMonotonicallyAndSpansThickness()
        {
            var builder = new CombatOverlayMeshBuilder();
            var mesh = builder.BuildBoundaryMesh(new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, new FakeProjector(), 0.1f);
            try
            {
                var uvs = mesh.uv;
                Assert.That(uvs.Length, Is.EqualTo(40));
                for (var i = 1; i < uvs.Length; i++)
                {
                    Assert.That(uvs[i].x, Is.GreaterThanOrEqualTo(uvs[i - 1].x - 0.0001f),
                        $"UV.u must not decrease (index {i}).");
                }

                Assert.That(uvs[uvs.Length - 1].x, Is.GreaterThan(uvs[0].x), "UV.u accumulates length overall.");
                Assert.That(uvs.All(uv => Mathf.Approximately(uv.y, 0f) || Mathf.Approximately(uv.y, 1f)), Is.True,
                    "UV.v spans the thickness as 0/1.");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void FillRadiusScaleControlsCornerDistanceFromCenter()
        {
            var builder = new CombatOverlayMeshBuilder();
            var projector = new FakeProjector();

            var merged = builder.BuildFillMesh(new[] { new HexCoord(0, 0) }, projector,
                HexOverlayRenderOrder.CombatFillLift, fillRadiusScale: 1.0f);
            var tight = builder.BuildFillMesh(new[] { new HexCoord(0, 0) }, projector,
                HexOverlayRenderOrder.CombatFillLift, fillRadiusScale: CombatOverlayMeshBuilder.DefaultFillRadiusScale);
            try
            {
                AssertCornerDistance(merged, projector.TileRadius * 1.0f);
                AssertCornerDistance(tight, projector.TileRadius * CombatOverlayMeshBuilder.DefaultFillRadiusScale);
            }
            finally
            {
                Object.DestroyImmediate(merged);
                Object.DestroyImmediate(tight);
            }
        }

        private static void AssertCornerDistance(Mesh fillMesh, float expectedRadius)
        {
            var vertices = fillMesh.vertices;
            var center = vertices[0];
            // 13-vertex fan: index 0 center, indices 1..12 are the (un-shared) corner vertices.
            for (var corner = 1; corner <= 12; corner++)
            {
                var planar = new Vector2(vertices[corner].x - center.x, vertices[corner].z - center.z);
                Assert.That(planar.magnitude, Is.EqualTo(expectedRadius).Within(0.001f),
                    $"Corner {corner} should sit at radius {expectedRadius}.");
            }
        }

        private static void AssertSharedEdgeIsAbsent(Mesh mesh, FakeProjector projector, HexCoord coord, HexCoord neighbor, HexDirection direction)
        {
            var edge = ResolveSharedEdge(projector, coord, direction);
            var midpoint = (edge.a + edge.b) * 0.5f;
            var verticesNearSharedMidpoint = mesh.vertices.Count(vertex =>
                Mathf.Abs(vertex.x - midpoint.x) < 0.12f &&
                Mathf.Abs(vertex.z - midpoint.z) < 0.12f);

            Assert.That(verticesNearSharedMidpoint, Is.Zero, $"Shared boundary {coord}->{neighbor} ({direction}) should be culled.");
        }

        private static (Vector3 a, Vector3 b) ResolveSharedEdge(FakeProjector projector, HexCoord coord, HexDirection direction)
        {
            var edgeIndex = direction switch
            {
                HexDirection.SouthEast => 0,
                HexDirection.SouthWest => 1,
                HexDirection.West => 2,
                HexDirection.NorthWest => 3,
                HexDirection.NorthEast => 4,
                HexDirection.East => 5,
                _ => 0
            };
            var center = projector.ProjectTop(coord) + Vector3.up * 0.02f;
            return (center + CornerOffset(edgeIndex), center + CornerOffset((edgeIndex + 1) % 6));
        }

        private static Vector3 CornerOffset(int side)
        {
            var angle = Mathf.Deg2Rad * (30f + side * 60f);
            return new Vector3(Mathf.Cos(angle) * 0.84f, 0f, -Mathf.Sin(angle) * 0.84f);
        }

        private sealed class FakeProjector : IHexMapWorldProjector
        {
            public float TileRadius => 1f;

            public Vector3 Project(HexCoord coord)
            {
                return new Vector3(Mathf.Sqrt(3f) * (coord.Q + coord.R * 0.5f), 0f, -1.5f * coord.R);
            }

            public Vector3 ProjectTop(HexCoord coord)
            {
                return Project(coord) + Vector3.up * 0.25f;
            }
        }

        private sealed class FakeOverlaySurfaceProjector : IHexMapWorldProjector, IHexMapOverlaySurfaceProjector
        {
            public float TileRadius => 1f;

            public Vector3 Project(HexCoord coord)
            {
                return Vector3.zero;
            }

            public Vector3 ProjectTop(HexCoord coord)
            {
                return Vector3.up * 0.25f;
            }

            public Vector3 ProjectOverlaySurface(HexCoord coord)
            {
                return Vector3.up * 0.5f;
            }
        }
    }
}

