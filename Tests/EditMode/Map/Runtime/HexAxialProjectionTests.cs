using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    public sealed class HexAxialProjectionTests
    {
        private static Func<HexCoord, int?> Lookup(params (HexCoord Coord, int Height)[] cells)
        {
            var map = new Dictionary<HexCoord, int>();
            foreach (var cell in cells)
            {
                map[cell.Coord] = cell.Height;
            }

            return coord => map.TryGetValue(coord, out var height) ? height : (int?)null;
        }

        [Test]
        public void CoordToWorldRoundTripsThroughWorldToCoord()
        {
            foreach (var radius in new[] { 0.7f, 1f, 2.3f })
            {
                var projection = new HexAxialProjection(radius);
                for (var q = -3; q <= 3; q++)
                {
                    for (var r = -3; r <= 3; r++)
                    {
                        var coord = new HexCoord(q, r);
                        projection.CoordToWorld(coord, out var x, out var z);
                        Assert.That(projection.WorldToCoord(x, z), Is.EqualTo(coord),
                            $"radius {radius}, coord {coord}");
                    }
                }
            }
        }

        [Test]
        public void WorldToCoordResolvesOffCenterPointsToOwningCell()
        {
            var projection = new HexAxialProjection(1f);
            var coord = new HexCoord(2, -1);
            projection.CoordToWorld(coord, out var x, out var z);

            // Sample inside the hex: the in-radius is √3/2 ≈ 0.866, so offsets of
            // 0.6 along the axes and 0.4 diagonally stay strictly inside the cell.
            foreach (var (dx, dz) in new[] { (0.6f, 0f), (-0.6f, 0f), (0f, 0.6f), (0f, -0.6f), (0.4f, 0.4f), (-0.4f, -0.4f) })
            {
                Assert.That(projection.WorldToCoord(x + dx, z + dz), Is.EqualTo(coord), $"offset ({dx}, {dz})");
            }
        }

        [Test]
        public void WorldToCoordCrossesToNeighborBeyondSharedEdge()
        {
            var projection = new HexAxialProjection(1f);
            var origin = new HexCoord(0, 0);
            var east = new HexCoord(1, 0);
            projection.CoordToWorld(origin, out var originX, out var originZ);
            projection.CoordToWorld(east, out var eastX, out _);

            // The shared edge sits at the in-radius midpoint between both centers.
            var midX = (originX + eastX) * 0.5f;
            Assert.That(projection.WorldToCoord(midX - 0.05f, originZ), Is.EqualTo(origin));
            Assert.That(projection.WorldToCoord(midX + 0.05f, originZ), Is.EqualTo(east));
        }

        [Test]
        public void RaycastPicksCellUnderVerticalRay()
        {
            var projection = new HexAxialProjection(1f, verticalOffset: 0f, heightStep: 0.5f);
            var flat = new HexCoord(0, 0);
            var raised = new HexCoord(1, 0);
            var lookup = Lookup((flat, 0), (raised, 2));

            projection.CoordToWorld(raised, out var raisedX, out var raisedZ);
            Assert.That(projection.TryRaycastHeightPlanes(raisedX, 5f, raisedZ, 0f, -1f, 0f, lookup, out var hit), Is.True);
            Assert.That(hit, Is.EqualTo(raised));

            projection.CoordToWorld(flat, out var flatX, out var flatZ);
            Assert.That(projection.TryRaycastHeightPlanes(flatX, 5f, flatZ, 0f, -1f, 0f, lookup, out hit), Is.True);
            Assert.That(hit, Is.EqualTo(flat));
        }

        [Test]
        public void RaycastPrefersRaisedTopPlaneOverGroundBehindIt()
        {
            var projection = new HexAxialProjection(1f, verticalOffset: 0f, heightStep: 0.5f);
            var flat = new HexCoord(0, 0);
            var raised = new HexCoord(1, 0);
            var lookup = Lookup((flat, 0), (raised, 2));

            // Slanted ray from above the flat cell toward the raised one: the
            // height-2 plane intersection (x = 1.6) already lands inside the raised
            // cell's footprint, so the sweep resolves it before any lower plane.
            Assert.That(projection.TryRaycastHeightPlanes(0f, 5f, 0f, 0.4f, -1f, 0f, lookup, out var hit), Is.True);
            Assert.That(hit, Is.EqualTo(raised));
        }

        [Test]
        public void RaycastAgainstRaisedSideWallFails()
        {
            var projection = new HexAxialProjection(1f, verticalOffset: 0f, heightStep: 0.5f);
            var flat = new HexCoord(0, 0);
            var raised = new HexCoord(1, 0);
            var lookup = Lookup((flat, 0), (raised, 2));

            // This ray passes below the raised top plane while inside the raised
            // footprint (its plane-0 crossing at x = 0.9 is past the shared edge at
            // ≈0.866): a physical side-wall hit. Top-surface-only picking rejects it
            // — the confirmed P6 accuracy contract.
            Assert.That(projection.TryRaycastHeightPlanes(0f, 5f, 0f, 0.18f, -1f, 0f, lookup, out _), Is.False);
        }

        [Test]
        public void RaycastHonorsVerticalOffsetAndNegativeHeight()
        {
            var projection = new HexAxialProjection(1f, verticalOffset: 10f, heightStep: 0.5f);
            var sunken = new HexCoord(0, 1);
            var lookup = Lookup((sunken, -1));

            projection.CoordToWorld(sunken, out var x, out var z);
            Assert.That(projection.TryRaycastHeightPlanes(x, 20f, z, 0f, -1f, 0f, lookup, out var hit), Is.True);
            Assert.That(hit, Is.EqualTo(sunken));
            Assert.That(projection.HeightPlaneY(-1), Is.EqualTo(9.5f));
        }

        [Test]
        public void RaycastClampsOutOfRangeHeightLevels()
        {
            var projection = new HexAxialProjection(1f, verticalOffset: 0f, heightStep: 0.5f);
            var cell = new HexCoord(0, 0);
            var lookup = Lookup((cell, 7));

            // Raw level 7 clamps to MaxHeightLevel (3), so the top plane matches.
            Assert.That(projection.TryRaycastHeightPlanes(0f, 5f, 0f, 0f, -1f, 0f, lookup, out var hit), Is.True);
            Assert.That(hit, Is.EqualTo(cell));
        }

        [Test]
        public void RaycastFailsWithoutMatchingCellOrUsableRay()
        {
            var projection = new HexAxialProjection(1f, verticalOffset: 0f, heightStep: 0.5f);
            var lookup = Lookup((new HexCoord(0, 0), 0));

            // Outside the map.
            Assert.That(projection.TryRaycastHeightPlanes(30f, 5f, 30f, 0f, -1f, 0f, lookup, out _), Is.False);
            // Parallel to the planes.
            Assert.That(projection.TryRaycastHeightPlanes(0f, 5f, 0f, 1f, 0f, 0f, lookup, out _), Is.False);
            // All planes behind the origin.
            Assert.That(projection.TryRaycastHeightPlanes(0f, -5f, 0f, 0f, -1f, 0f, lookup, out _), Is.False);
            // No lookup.
            Assert.That(projection.TryRaycastHeightPlanes(0f, 5f, 0f, 0f, -1f, 0f, null, out _), Is.False);
        }
    }
}
