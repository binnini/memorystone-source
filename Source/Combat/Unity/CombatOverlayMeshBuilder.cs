using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class CombatOverlayMeshBuilder
    {
        private const int HexSides = 6;
        public const float DefaultFillRadiusScale = 0.88f;
        private static readonly int[] EdgeNeighborDirectionByCornerEdge =
        {
            (int)HexDirection.SouthEast,
            (int)HexDirection.SouthWest,
            (int)HexDirection.West,
            (int)HexDirection.NorthWest,
            (int)HexDirection.NorthEast,
            (int)HexDirection.East
        };

        // Scratch buffers reused across builds so per-rebuild meshing does not allocate. The fill and
        // boundary populate passes run sequentially (never interleaved) so they can share these safely.
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Color> colors = new List<Color>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int> triangles = new List<int>();
        private readonly HashSet<HexCoord> active = new HashSet<HexCoord>();
        private readonly List<HexCoord> ordered = new List<HexCoord>();

        /// <summary>Allocates a fresh fill mesh (test/utility path). Runtime uses <see cref="PopulateFillMesh"/>.</summary>
        public Mesh BuildFillMesh(
            IEnumerable<HexCoord> coords,
            IHexMapWorldProjector projector,
            float lift = HexOverlayRenderOrder.CombatFillLift,
            float fillRadiusScale = DefaultFillRadiusScale)
        {
            var mesh = new Mesh { name = "Combat Overlay Fill" };
            PopulateFillMesh(mesh, coords, projector, lift, fillRadiusScale);
            return mesh;
        }

        /// <summary>Rewrites <paramref name="mesh"/> in place (no Mesh allocation), reusing pooled buffers.</summary>
        public void PopulateFillMesh(
            Mesh mesh,
            IEnumerable<HexCoord> coords,
            IHexMapWorldProjector projector,
            float lift,
            float fillRadiusScale)
        {
            mesh.Clear();
            if (projector == null || coords == null)
            {
                return;
            }

            PrepareActiveSet(coords);
            var radius = projector.TileRadius * fillRadiusScale;
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            triangles.Clear();

            foreach (var coord in ordered)
            {
                // Fan of 6 triangles, one per hex side, with UN-SHARED corner vertices (13 verts: a shared
                // center + 2 corners per side) so each side can carry a PER-EDGE outer flag. Two channels:
                //  .r = this side's edge is on the region's outer boundary (constant across the triangle).
                //       Per-edge (not per-corner) so glow lights ONLY outward-facing edges and never bleeds
                //       through a shared corner into an interior-facing edge.
                //  .g = radialHex01 (0 at center, 1 at corners). Linear interpolation gives concentric-hex
                //       iso-contours — a per-tile hex distance field for the smooth glow band and feather.
                var center = ProjectOverlaySurface(projector, coord) + Vector3.up * lift;
                var start = vertices.Count;
                vertices.Add(center);
                colors.Add(new Color(0f, 0f, 0f, 1f));
                uvs.Add(Vector2.zero);
                for (var side = 0; side < HexSides; side++)
                {
                    var outerEdge = EdgeIsOuter(active, coord, side) ? 1f : 0f;
                    var a = vertices.Count;
                    vertices.Add(center + CornerOffset(radius, side));
                    colors.Add(new Color(outerEdge, 1f, 0f, 1f));
                    uvs.Add(Vector2.zero);
                    vertices.Add(center + CornerOffset(radius, (side + 1) % HexSides));
                    colors.Add(new Color(outerEdge, 1f, 0f, 1f));
                    uvs.Add(Vector2.zero);

                    triangles.Add(start);
                    triangles.Add(a);
                    triangles.Add(a + 1);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Allocates a fresh boundary mesh (test/utility path). Runtime uses <see cref="PopulateBoundaryMesh"/>.</summary>
        public Mesh BuildBoundaryMesh(
            IEnumerable<HexCoord> coords,
            IHexMapWorldProjector projector,
            float thickness,
            float lift = HexOverlayRenderOrder.CombatBoundaryLift,
            float fillRadiusScale = DefaultFillRadiusScale)
        {
            var mesh = new Mesh { name = "Combat Overlay Boundary" };
            PopulateBoundaryMesh(mesh, coords, projector, thickness, lift, fillRadiusScale);
            return mesh;
        }

        /// <summary>Rewrites <paramref name="mesh"/> in place (no Mesh allocation), reusing pooled buffers.</summary>
        public void PopulateBoundaryMesh(
            Mesh mesh,
            IEnumerable<HexCoord> coords,
            IHexMapWorldProjector projector,
            float thickness,
            float lift,
            float fillRadiusScale)
        {
            mesh.Clear();
            if (projector == null || coords == null)
            {
                return;
            }

            PrepareActiveSet(coords);
            var radius = projector.TileRadius * fillRadiusScale;
            var halfThickness = Mathf.Max(0.005f, thickness) * 0.5f;
            vertices.Clear();
            colors.Clear();
            uvs.Clear();
            triangles.Clear();
            // Running length along the emitted edge chain, exposed as UV.u for the marching-ants dashes.
            var runningLength = 0f;

            foreach (var coord in ordered)
            {
                var center = ProjectOverlaySurface(projector, coord) + Vector3.up * lift;
                for (var direction = 0; direction < HexSides; direction++)
                {
                    if (active.Contains(coord.Neighbor((HexDirection)EdgeNeighborDirectionByCornerEdge[direction])))
                    {
                        continue;
                    }

                    runningLength = AddEdgeQuad(
                        center + CornerOffset(radius, direction),
                        center + CornerOffset(radius, (direction + 1) % HexSides),
                        halfThickness,
                        runningLength);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
        }

        private void PrepareActiveSet(IEnumerable<HexCoord> coords)
        {
            active.Clear();
            foreach (var coord in coords)
            {
                active.Add(coord);
            }

            ordered.Clear();
            ordered.AddRange(active);
            ordered.Sort();
        }

        private float AddEdgeQuad(Vector3 a, Vector3 b, float halfThickness, float runningLength)
        {
            var edge = b - a;
            var normal = new Vector3(-edge.z, 0f, edge.x).normalized * halfThickness;
            var uStart = runningLength;
            var uEnd = runningLength + edge.magnitude;
            var start = vertices.Count;
            vertices.Add(a - normal);
            vertices.Add(a + normal);
            vertices.Add(b + normal);
            vertices.Add(b - normal);
            // Boundary carries no edge-glow signal; keep the color channel neutral (1) so a shared
            // shader reading vertex color for the fill glow leaves the boundary untouched.
            for (var i = 0; i < 4; i++)
            {
                colors.Add(Color.white);
            }

            uvs.Add(new Vector2(uStart, 0f));
            uvs.Add(new Vector2(uStart, 1f));
            uvs.Add(new Vector2(uEnd, 1f));
            uvs.Add(new Vector2(uEnd, 0f));
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
            return uEnd;
        }

        private static bool EdgeIsOuter(HashSet<HexCoord> active, HexCoord coord, int edge)
        {
            return !active.Contains(coord.Neighbor((HexDirection)EdgeNeighborDirectionByCornerEdge[edge]));
        }

        private static Vector3 ProjectOverlaySurface(IHexMapWorldProjector projector, HexCoord coord)
        {
            return projector is IHexMapOverlaySurfaceProjector overlaySurfaceProjector
                ? overlaySurfaceProjector.ProjectOverlaySurface(coord)
                : projector.ProjectTop(coord);
        }

        private static Vector3 CornerOffset(float radius, int side)
        {
            var angle = Mathf.Deg2Rad * (30f + side * 60f);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, -Mathf.Sin(angle) * radius);
        }
    }
}
