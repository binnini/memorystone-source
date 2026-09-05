using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexAxialLayoutTests
    {
        [Test]
        public void CoordToWorldOriginIsZero()
        {
            var layout = new HexAxialLayout(0.5f);
            var result = layout.CoordToWorld(new HexCoord(0, 0));
            Assert.That(result, Is.EqualTo(Vector2.zero));
        }

        [Test]
        public void CoordToWorldAndWorldToCoordRoundtrip()
        {
            var layout = new HexAxialLayout(0.5f);
            var coords = new[]
            {
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                new HexCoord(0, 1),
                new HexCoord(-1, 1),
                new HexCoord(3, -2),
                new HexCoord(-2, -1),
            };

            foreach (var coord in coords)
            {
                var world = layout.CoordToWorld(coord);
                var roundtripped = layout.WorldToCoord(world);
                Assert.That(roundtripped, Is.EqualTo(coord), $"Roundtrip failed for {coord}");
            }
        }

        [Test]
        public void WorldToCoordAxialRounding()
        {
            var layout = new HexAxialLayout(0.5f);
            var center = new HexCoord(2, -1);
            var centerWorld = layout.CoordToWorld(center);

            // A point slightly offset from center should still resolve to the same cell
            var nearCenter = new Vector2(centerWorld.x + 0.05f, centerWorld.y + 0.05f);
            var result = layout.WorldToCoord(nearCenter);

            Assert.That(result, Is.EqualTo(center));
        }

        [Test]
        public void GetAxialRangeForBoundsIncludesPadding()
        {
            var layout = new HexAxialLayout(0.5f);

            // Single cell at origin: its world center is (0,0)
            var cellWorld = layout.CoordToWorld(new HexCoord(0, 0));
            var min = new Vector2(cellWorld.x - 0.01f, cellWorld.y - 0.01f);
            var max = new Vector2(cellWorld.x + 0.01f, cellWorld.y + 0.01f);

            layout.GetAxialRangeForBounds(min, max, padding: 1,
                out int minQ, out int maxQ, out int minR, out int maxR);

            // With padding=1, range should extend at least 1 beyond the tight fit
            Assert.That(minQ, Is.LessThanOrEqualTo(-1));
            Assert.That(maxQ, Is.GreaterThanOrEqualTo(1));
            Assert.That(minR, Is.LessThanOrEqualTo(-1));
            Assert.That(maxR, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void GetCornersReturnsSixDistinctPoints()
        {
            var layout = new HexAxialLayout(0.5f);
            var corners = new Vector3[6];
            layout.GetCorners(new HexCoord(0, 0), corners, y: 0f);

            for (int i = 0; i < 6; i++)
            {
                for (int j = i + 1; j < 6; j++)
                {
                    Assert.That(corners[i], Is.Not.EqualTo(corners[j]),
                        $"Corners {i} and {j} must be distinct");
                }
            }
        }
    }
}

