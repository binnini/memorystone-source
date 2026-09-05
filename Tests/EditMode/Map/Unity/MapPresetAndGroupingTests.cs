using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using SeoulPlayup.MapDesign.Editor;
using UnityEngine;

namespace SeoulPlayup.Map.Tests.EditMode.Unity
{
    public sealed class MapPresetAndGroupingTests
    {
        [Test]
        public void BuildingObjectPaletteSeparatesPropCategory()
        {
            var definitions = MapObjectPrefabCatalog.GetDefinitionsForObjectType(HexMapObjectType.Building);
            var categories = definitions.Select(definition => definition.Category).Distinct().ToArray();

            Assert.That(categories, Does.Contain("Building"));
            Assert.That(categories, Does.Contain("Prop"));
            // Prop-grouped entries still serialize as the Building object type (no enum change).
            Assert.That(definitions.All(definition => definition.ObjectType == HexMapObjectType.Building), Is.True);
        }

        [Test]
        public void TrapPresetOverridesInlineValuesAtBuild()
        {
            var catalog = TrapPresetCatalog.LoadDefault();
            Assert.That(catalog, Is.Not.Null);
            // fire_field는 D-1로 삭제됐다(유령 HexTrapEffectKind.Burn 참조 → 밟으면 크래시).
            // 같은 자리를 blind_smoke(반경 1 재사용형 실명)가 대신한다.
            Assert.That(catalog.TryGet("blind_smoke", out var preset), Is.True);
            Assert.That(catalog.TryGet("fire_field", out _), Is.False, "fire_field는 D-1로 제거됐다.");

            // Inline values intentionally differ from the preset; the preset must win when linked.
            var trapRef = new HexTrapRef(
                "trap-a", 0, 0, radius: 5,
                effects: new[] { new HexTrapEffectRef(HexTrapEffectKind.Damage, 99) },
                affectsPlayer: false, affectsMonsters: true, oneShot: true, triggerOnEnter: true,
                presetId: "blind_smoke");

            var data = trapRef.ToRuntimeTrapData(catalog);

            Assert.That(data.Radius, Is.EqualTo(preset.Radius));
            Assert.That(data.AffectsMonsters, Is.EqualTo(preset.AffectsMonsters));
            Assert.That(data.Effects.Single().Kind, Is.EqualTo(preset.EffectKind));
            Assert.That(data.Effects.Single().Amount, Is.EqualTo(preset.EffectAmount));
        }

        [Test]
        public void TrapWithoutPresetUsesInlineValues()
        {
            var catalog = TrapPresetCatalog.LoadDefault();
            var trapRef = new HexTrapRef(
                "trap-b", 0, 0, radius: 3,
                effects: new[] { new HexTrapEffectRef(HexTrapEffectKind.Damage, 7) });

            var data = trapRef.ToRuntimeTrapData(catalog);

            Assert.That(data.Radius, Is.EqualTo(3));
            Assert.That(data.Effects.Single().Kind, Is.EqualTo(HexTrapEffectKind.Damage));
            Assert.That(data.Effects.Single().Amount, Is.EqualTo(7));
        }

        [Test]
        public void MonsterSpawnStoresAndPreservesSpawnerPresetId()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                var coord = new HexCoord(0, 0);
                source.ConfigureForTests(new[] { new HexSparseMapAuthoringCell(coord, "street", "street", "road", baseWalkable: true) });
                var session = new HexSparseMapEditorSession
                {
                    Source = source,
                    ActiveObjectType = HexMapObjectType.MonsterSpawn,
                    ActiveObjectRef = "M001",
                    ActiveObjectRole = "primary_pressure",
                    ActiveSpawnerPresetId = "spawn_M001",
                };

                Assert.That(session.PlaceObject(coord, out var message), Is.True, message);
                var placed = source.ObjectRefs.Single(objectRef => objectRef.IsMonsterSpawn);
                Assert.That(placed.SpawnerPresetId, Is.EqualTo("spawn_M001"));

                // Editing the spawn (rotation) keeps the preset link.
                Assert.That(session.SetObjectRotation(placed.ObjectId, 2, out _), Is.True);
                var rotated = source.ObjectRefs.Single(objectRef => objectRef.IsMonsterSpawn);
                Assert.That(rotated.SpawnerPresetId, Is.EqualTo("spawn_M001"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }
    }
}

