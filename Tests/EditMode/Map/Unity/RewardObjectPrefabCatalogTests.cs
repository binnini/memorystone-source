using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using SeoulPlayup.MapDesign.Editor;
using UnityEngine;

namespace SeoulPlayup.Map.Tests.EditMode.Unity
{
    public sealed class RewardObjectPrefabCatalogTests
    {
        [Test]
        public void TreasureChestTmpRewardObjectIsVisibleInteractableTreasureChest()
        {
            var catalog = MapObjectCatalogSet.LoadDefault();

            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.TryFindEntry("treasureChest_tmp", out var entry), Is.True);
            Assert.That(entry.ObjectType, Is.EqualTo(HexMapObjectType.TreasureChest));
            Assert.That(entry.Interactable, Is.True);
            Assert.That(entry.BlocksMovement, Is.False);
            Assert.That(entry.BlocksVision, Is.False);
            Assert.That(entry.Prefab, Is.Not.Null);
            Assert.That(entry.Prefab.name, Is.EqualTo("treasureChest_tmp"));
        }

        [Test]
        public void TreasureChestPaletteOnlyShowsTreasureChestDefinitions()
        {
            var definitions = MapObjectPrefabCatalog.GetDefinitionsForObjectType(HexMapObjectType.TreasureChest);

            Assert.That(definitions.Select(definition => definition.ObjectRef), Does.Contain("treasureChest_tmp"));
            Assert.That(definitions.All(definition => definition.ObjectType == HexMapObjectType.TreasureChest), Is.True);
        }

        [Test]
        public void MapEditorCanPlaceTreasureChestRewardObjectAsRuntimeVisualObject()
        {
            var catalog = MapObjectCatalogSet.LoadDefault();
            Assert.That(catalog.TryFindEntry("treasureChest_tmp", out var entry), Is.True);

            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var coord = new HexCoord(0, 0);
                source.ConfigureForTests(new[] { new HexSparseMapAuthoringCell(coord, "street", "street", "road", baseWalkable: true) });

                var session = new HexSparseMapEditorSession { Source = source };
                session.ApplyObjectDefinition(entry);

                Assert.That(session.PlaceObject(coord, out var message), Is.True, message);

                var placed = source.ObjectRefs.Single();
                Assert.That(placed.ObjectType, Is.EqualTo(HexMapObjectType.TreasureChest));
                Assert.That(placed.ObjectRef, Is.EqualTo("treasureChest_tmp"));
                Assert.That(placed.Interactable, Is.True);

                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);
                var runtimeObject = map.ObjectRefs.Single();
                Assert.That(runtimeObject.IsRuntimeVisualObject, Is.True);
                Assert.That(runtimeObject.ObjectType, Is.EqualTo("TreasureChest"));
                Assert.That(runtimeObject.ObjectRef, Is.EqualTo("treasureChest_tmp"));
                Assert.That(runtimeObject.Interactable, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }
    }
}

