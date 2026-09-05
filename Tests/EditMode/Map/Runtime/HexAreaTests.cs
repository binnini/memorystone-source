using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    public sealed class HexAreaTests
    {
        [Test]
        public void RadiusZeroYieldsOnlyCenter()
        {
            var center = new HexCoord(2, -1);

            var cells = HexArea.CellsWithin(center, 0).ToList();

            Assert.That(cells, Has.Count.EqualTo(1));
            Assert.That(cells[0], Is.EqualTo(center));
        }

        [Test]
        public void NegativeRadiusYieldsNothing()
        {
            Assert.That(HexArea.CellsWithin(new HexCoord(0, 0), -1), Is.Empty);
        }

        [Test]
        public void DiskCountMatchesHexFormula()
        {
            // A filled hex disk of radius r has 3r^2 + 3r + 1 cells.
            for (var r = 0; r <= 4; r++)
            {
                var expected = 3 * r * r + 3 * r + 1;
                Assert.That(HexArea.CellsWithin(new HexCoord(0, 0), r).Count(), Is.EqualTo(expected), $"radius {r}");
            }
        }

        [Test]
        public void EveryCellIsWithinRadiusAndUnique()
        {
            var center = new HexCoord(-3, 5);
            const int radius = 3;

            var cells = HexArea.CellsWithin(center, radius).ToList();

            Assert.That(cells.All(cell => center.DistanceTo(cell) <= radius), Is.True);
            Assert.That(cells.Distinct().Count(), Is.EqualTo(cells.Count));
            // The disk must include the full ring at exactly the radius distance.
            Assert.That(cells.Any(cell => center.DistanceTo(cell) == radius), Is.True);
        }
    }
}

