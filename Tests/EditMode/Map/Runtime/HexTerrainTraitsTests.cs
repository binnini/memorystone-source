using NUnit.Framework;

namespace SeoulPlayup.Map.Runtime.Tests.EditMode
{
    public sealed class HexTerrainTraitsTests
    {
        [Test]
        public void DefaultBlocksWaterFamilyForMovementAndSelection()
        {
            var traits = HexTerrainTraits.Default;

            foreach (var waterId in new[] { "hanriver-water", "river", "lake", "water", "RIVER" })
            {
                Assert.That(traits.IsWalkable(waterId), Is.False, $"{waterId} should be impassable.");
                Assert.That(traits.IsSelectable(waterId), Is.False, $"{waterId} should be unselectable.");
            }
        }

        [Test]
        public void DefaultAllowsBridgesRoutesAndUnknownTerrain()
        {
            var traits = HexTerrainTraits.Default;

            foreach (var id in new[] { "hanriver-bridge", "hanriver-primary-route", "riverside-bypass", "seoul-street", "", null })
            {
                Assert.That(traits.IsWalkable(id), Is.True);
                Assert.That(traits.IsSelectable(id), Is.True);
            }
        }

        [Test]
        public void HeightTraversalAllowsSingleLevelAndBlocksTwoOrMore()
        {
            var traits = HexTerrainTraits.Default;

            Assert.That(traits.IsHeightTraversable(0, 0), Is.True);
            Assert.That(traits.IsHeightTraversable(0, 1), Is.True);
            Assert.That(traits.IsHeightTraversable(1, 0), Is.True);
            Assert.That(traits.IsHeightTraversable(0, 2), Is.False);
            Assert.That(traits.IsHeightTraversable(3, 1), Is.False);
            Assert.That(traits.IsHeightTraversable(-1, 1), Is.False);
            Assert.That(traits.IsHeightTraversable(-1, 0), Is.True);
        }

        [Test]
        public void CustomTraitsRestrictExtraTerrainIds()
        {
            var traits = new HexTerrainTraits(
                impassableTerrainIds: new[] { "lava" },
                unselectableTerrainIds: new[] { "lava", "fog" });

            Assert.That(traits.IsWalkable("lava"), Is.False);
            Assert.That(traits.IsSelectable("fog"), Is.False);
            Assert.That(traits.IsWalkable("fog"), Is.True, "fog is unselectable but still walkable.");
            Assert.That(traits.IsWalkable("hanriver-water"), Is.True, "Custom set replaces defaults when supplied explicitly.");
        }
    }
}

