using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatObjectiveBindingResolverTests
    {
        [Test]
        public void ResolveUsesConfiguredObjectiveBinding()
        {
            var target = new HexCoord(1, 0);
            var map = new HexMapData(
                new[]
                {
                    Cell(new HexCoord(0, 0), string.Empty),
                    Cell(target, "custom-landmark")
                },
                new[]
                {
                    new HexObjectiveBinding("custom-objective", "custom-landmark", "Custom Landmark", "Investigate", 2)
                });

            var binding = CombatObjectiveBindingResolver.Resolve(map, out var targetCoord);

            Assert.That(targetCoord, Is.EqualTo(target));
            Assert.That(binding.ObjectiveId, Is.EqualTo("custom-objective"));
            Assert.That(binding.DisplayName, Is.EqualTo("Custom Landmark"));
            Assert.That(binding.InvestigateRange, Is.EqualTo(2));
        }

        [Test]
        public void ResolveReturnsDefaultWhenNoConfiguredBindingMatches()
        {
            var map = new HexMapData(new[]
            {
                Cell(new HexCoord(0, 0), string.Empty),
                Cell(new HexCoord(2, 0), "landmark-63")
            });

            var binding = CombatObjectiveBindingResolver.Resolve(map, out var targetCoord);

            Assert.That(targetCoord, Is.Null);
            Assert.That(binding.IsConfigured, Is.False);
        }

        [Test]
        public void ResolveFallsBackToAuthoredMemoryStoneObject()
        {
            var target = new HexCoord(2, 0);
            var map = new HexMapData(
                new[]
                {
                    Cell(new HexCoord(0, 0), string.Empty),
                    Cell(target, string.Empty)
                },
                objectRefs: new[]
                {
                    new HexMapObjectData("memory-main", "MemoryStone", "memorystone", target, role: "objective", interactable: true)
                });

            var binding = CombatObjectiveBindingResolver.Resolve(map, out var targetCoord);

            Assert.That(targetCoord, Is.EqualTo(target));
            Assert.That(binding.ObjectiveId, Is.EqualTo("memory-main"));
            Assert.That(binding.LandmarkId, Is.EqualTo("memorystone"));
            Assert.That(binding.DisplayName, Is.EqualTo("기억결"));
            Assert.That(binding.RequiredAction, Is.EqualTo("Interact"));
            Assert.That(binding.InvestigateRange, Is.EqualTo(0));
        }

        [Test]
        public void ResolvePrefersAuthoredMemoryStoneOverLegacyObjectiveBinding()
        {
            var legacyTarget = new HexCoord(1, 0);
            var memoryStoneTarget = new HexCoord(2, 0);
            var map = new HexMapData(
                new[]
                {
                    Cell(new HexCoord(0, 0), string.Empty),
                    Cell(legacyTarget, "legacy-landmark"),
                    Cell(memoryStoneTarget, string.Empty)
                },
                new[]
                {
                    new HexObjectiveBinding("legacy-objective", "legacy-landmark", "Legacy Landmark", "Investigate", 2)
                },
                objectRefs: new[]
                {
                    new HexMapObjectData("memory-main", "MemoryStone", "memorystone", memoryStoneTarget, role: "main", interactable: true)
                });

            var binding = CombatObjectiveBindingResolver.Resolve(map, out var targetCoord);

            Assert.That(targetCoord, Is.EqualTo(memoryStoneTarget));
            Assert.That(binding.ObjectiveId, Is.EqualTo("memory-main"));
            Assert.That(binding.RequiredAction, Is.EqualTo("Interact"));
        }

        [Test]
        public void ResolveReturnsDefaultWhenNoObjectiveExists()
        {
            var map = new HexMapData(new[]
            {
                Cell(new HexCoord(0, 0), string.Empty),
                Cell(new HexCoord(1, 0), "non-objective")
            });

            var binding = CombatObjectiveBindingResolver.Resolve(map, out var targetCoord);

            Assert.That(targetCoord, Is.Null);
            Assert.That(binding.IsConfigured, Is.False);
        }

        private static HexCellData Cell(HexCoord coord, string landmarkId)
        {
            return new HexCellData(coord, "tile", "street", 1, true, false, landmarkId: landmarkId);
        }
    }
}

