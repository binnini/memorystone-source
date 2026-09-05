using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Map.Tests.EditMode.Unity
{
    public sealed class MapObjectCatalogSetTests
    {
        [Test]
        public void DefaultCatalogSetLoadsWithEntries()
        {
            var set = MapObjectCatalogSet.LoadDefault();

            Assert.That(set, Is.Not.Null);
            Assert.That(set.Entries.Count, Is.GreaterThan(0));
            Assert.That(set.Catalogs.Count, Is.GreaterThan(0));
        }

        [Test]
        public void DefaultCatalogSetHasNoDuplicateObjectRefs()
        {
            var set = MapObjectCatalogSet.LoadDefault();

            Assert.That(set.FindDuplicateObjectRefs(), Is.Empty);
        }

        [Test]
        public void DefaultCatalogSetResolvesTreasureChestTmp()
        {
            var set = MapObjectCatalogSet.LoadDefault();

            Assert.That(set.TryFindEntry("treasureChest_tmp", out var entry), Is.True);
            Assert.That(entry.ObjectType, Is.EqualTo(HexMapObjectType.TreasureChest));
            Assert.That(entry.Interactable, Is.True);
            Assert.That(entry.Prefab, Is.Not.Null);
            Assert.That(entry.Prefab.name, Is.EqualTo("treasureChest_tmp"));
        }

        [Test]
        public void GetEntriesForTypeFiltersByObjectType()
        {
            var set = MapObjectCatalogSet.LoadDefault();

            var treasure = set.GetEntriesForType(HexMapObjectType.TreasureChest);
            Assert.That(treasure.Select(entry => entry.ObjectRef), Does.Contain("treasureChest_tmp"));
            Assert.That(treasure.All(entry => entry.ObjectType == HexMapObjectType.TreasureChest), Is.True);
        }

        [Test]
        public void DefaultSpawnerPresetCatalogCoversImportedMonsterCatalogIds()
        {
            var catalog = SpawnerPresetCatalog.LoadDefault();

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.Presets.Select(preset => preset.MonsterRef), Is.SupersetOf(new[] { "M001", "M002", "M003", "M004", "M005", "M006" }));
            Assert.That(catalog.Presets.Select(preset => preset.PresetId), Is.SupersetOf(new[] { "spawn_M001", "spawn_M002", "spawn_M003", "spawn_M004", "spawn_M005", "spawn_M006" }));
        }
    }
}

