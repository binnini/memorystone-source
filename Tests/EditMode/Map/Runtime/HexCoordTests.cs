using System.Collections.Generic;
using NUnit.Framework;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    public sealed class HexCoordTests
    {
        [Test]
        public void NeighborsUseStableSixDirectionOrder()
        {
            var origin = new HexCoord(0, 0);
            CollectionAssert.AreEqual(
                new[]
                {
                    new HexCoord(1, 0),
                    new HexCoord(1, -1),
                    new HexCoord(0, -1),
                    new HexCoord(-1, 0),
                    new HexCoord(-1, 1),
                    new HexCoord(0, 1)
                },
                new List<HexCoord>(origin.NeighborsInDirectionOrder()));
        }

        [Test]
        public void DistanceEqualityAndHashMatchAxialExpectations()
        {
            var a = new HexCoord(1, -2);
            var same = new HexCoord(1, -2);
            var other = new HexCoord(-2, 1);

            Assert.That(a, Is.EqualTo(same));
            Assert.That(a.GetHashCode(), Is.EqualTo(same.GetHashCode()));
            Assert.That(a.DistanceTo(other), Is.EqualTo(3));
        }
    }
}

