using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class MonsterCatalogSpawnBindingTests
    {
        [Test]
        public void BoardSpawnRefsJoinToCatalogDefinitionsWithRuntimeEvidence()
        {
            var config = CombatConfig.Default;
            var map = CreateMapWithSpawn(new HexMonsterSpawnRef("spawn-primary", "M001", new HexCoord(2, 0), "primary_pressure"));
            var catalog = CombatState.CreateMonsterCatalog(config);

            var monsterConfigs = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, catalog);
            var state = new CombatState(map, new HexCoord(0, 0), monsterConfigs, config, monsterCatalog: catalog);

            Assert.That(state.MonsterCatalog.SourceId, Is.EqualTo("designer-monster-csv"));
            Assert.That(state.ActiveMonsterDefinitionIds, Is.EquivalentTo(new[] { "M001" }));
            Assert.That(state.ActiveMonsterSpawnRefIds, Is.EquivalentTo(new[] { "spawn-primary" }));
            Assert.That(state.Monsters.Single().DefinitionId, Is.EqualTo("M001"));
            Assert.That(state.Monsters.Single().SpawnRefId, Is.EqualTo("spawn-primary"));
            Assert.That(state.MonsterCatalogEvidenceText, Does.Contain("ActiveDefinitions=[M001]"));
            Assert.That(state.MonsterCatalogEvidenceText, Does.Contain("SpawnRefs=[spawn-primary]"));
        }


        [Test]
        public void ObjectDerivedSpawnRefsJoinToCatalogDefinitionsWithRuntimeEvidence()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    Enumerable.Range(0, 4).Select(q => new HexSparseMapAuthoringCell(new HexCoord(q, 0), $"cell-{q}", "street", "atlas-street")),
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("object-spawn", HexMapObjectType.MonsterSpawn, "M001", 2, 0, "primary_pressure")
                    });
                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);

                var config = CombatConfig.Default;
                var catalog = CombatState.CreateMonsterCatalog(config);
                var monsterConfigs = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, catalog);
                var state = new CombatState(map, new HexCoord(0, 0), monsterConfigs, config, monsterCatalog: catalog);

                Assert.That(state.ActiveMonsterSpawnRefIds, Is.EquivalentTo(new[] { "object-spawn" }));
                Assert.That(state.MonsterCatalogEvidenceText, Does.Contain("SpawnRefs=[object-spawn]"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void BoardSpawnRefsCreateAllConfiguredMonstersWithoutCountCap()
        {
            var config = CombatConfig.Default;
            var map = CreateMapWithSpawn(
                new HexMonsterSpawnRef("spawn-0", "M001", new HexCoord(0, 0), "pressure-a"),
                new HexMonsterSpawnRef("spawn-1", "M001", new HexCoord(1, 0), "pressure-b"),
                new HexMonsterSpawnRef("spawn-2", "M001", new HexCoord(2, 0), "pressure-c"),
                new HexMonsterSpawnRef("spawn-3", "M001", new HexCoord(3, 0), "pressure-d"));

            var monsterConfigs = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, CombatState.CreateMonsterCatalog(config));
            var state = new CombatState(map, new HexCoord(4, 0), monsterConfigs, config);

            Assert.That(monsterConfigs.Select(monster => monster.SpawnRefId), Is.EquivalentTo(new[] { "spawn-0", "spawn-1", "spawn-2", "spawn-3" }));
            Assert.That(state.Monsters.Count, Is.EqualTo(4));
            Assert.That(state.ActiveMonsterSpawnRefIds, Is.EquivalentTo(new[] { "spawn-0", "spawn-1", "spawn-2", "spawn-3" }));
        }

        [Test]
        public void CombatStateRejectsPlayerSpawnOnMovementBlockingObject()
        {
            var blocked = new HexCoord(0, 0);
            var map = CreateMapWithBlockingObject(blocked);

            var ex = Assert.Throws<ArgumentException>(() => new CombatState(
                map,
                blocked,
                new[] { new MonsterConfig("monster-a", new HexCoord(2, 0), CombatConfig.Default.EnemyMaxHp) },
                CombatConfig.Default));

            Assert.That(ex.Message, Does.Contain("Player spawn"));
            Assert.That(ex.Message, Does.Contain("movement-blocked"));
        }

        [Test]
        public void CombatStateRejectsExplicitMonsterSpawnOnMovementBlockingObject()
        {
            var blocked = new HexCoord(2, 0);
            var map = CreateMapWithBlockingObject(blocked);

            var ex = Assert.Throws<ArgumentException>(() => new CombatState(
                map,
                new HexCoord(0, 0),
                new[] { new MonsterConfig("monster-a", blocked, CombatConfig.Default.EnemyMaxHp) },
                CombatConfig.Default));

            Assert.That(ex.Message, Does.Contain("monster-a"));
            Assert.That(ex.Message, Does.Contain("movement-blocked"));
        }

        [Test]
        public void MissingSpawnConfigReportsClearValidationEvidence()
        {
            var config = CombatConfig.Default;
            var ex = Assert.Throws<ArgumentException>(() => CombatState.ResolveMonsterConfigsFromBoardSpawns(CreateMapWithSpawn(), config, CombatState.CreateMonsterCatalog(config)));

            Assert.That(ex.Message, Does.Contain("no configured monster spawn refs"));
        }


        [Test]
        public void ObjectDerivedMissingCatalogDefinitionReportsSpawnRefAndMonsterId()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    Enumerable.Range(0, 4).Select(q => new HexSparseMapAuthoringCell(new HexCoord(q, 0), $"cell-{q}", "street", "atlas-street")),
                    objectRefs: new[]
                    {
                        new HexMapObjectRef("object-missing", HexMapObjectType.MonsterSpawn, "missing-monster", 2, 0, "primary_pressure")
                    });
                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);

                var ex = Assert.Throws<ArgumentException>(() => CombatState.ResolveMonsterConfigsFromBoardSpawns(map, CombatConfig.Default, CombatState.CreateMonsterCatalog(CombatConfig.Default)));

                Assert.That(ex.Message, Does.Contain("object-missing"));
                Assert.That(ex.Message, Does.Contain("missing-monster"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void MissingCatalogDefinitionReportsSpawnRefAndMonsterId()
        {
            var config = CombatConfig.Default;
            var map = CreateMapWithSpawn(new HexMonsterSpawnRef("spawn-missing", "missing-monster", new HexCoord(2, 0), "primary_pressure"));
            var ex = Assert.Throws<ArgumentException>(() => CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, CombatState.CreateMonsterCatalog(config)));

            Assert.That(ex.Message, Does.Contain("spawn-missing"));
            Assert.That(ex.Message, Does.Contain("missing-monster"));
        }

        [Test]
        public void CatalogBoundMonsterPressureBehaviorPreviewsChaseBeforePlayerAction()
        {
            var config = CombatConfig.Default;
            var map = CreateMapWithSpawn(new HexMonsterSpawnRef("spawn-primary", "M001", new HexCoord(3, 0), "primary_pressure"));
            var monsterConfigs = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, CombatState.CreateMonsterCatalog(config));
            var state = new CombatState(map, new HexCoord(0, 0), monsterConfigs, config);
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(3, 0)));
            Assert.That(state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().PredictedMoveCoord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(state.Monsters.Single().Intent.Type, Is.EqualTo(EnemyIntentType.Chase));
        }

        [Test]
        public void CatalogBoundMonsterPressureDoesNotCancelInvestigateObjectiveCompletion()
        {
            var config = CombatConfig.Default;
            var cells = new[]
            {
                new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "objective", "street", 1, true, false, landmarkId: "landmark-63"),
                new HexCellData(new HexCoord(2, 0), "monster", "street", 1, true, false)
            };
            var map = new HexMapData(
                cells,
                new[] { new HexObjectiveBinding("first-play-investigate-63", "landmark-63", "63 landmark") },
                new[] { new HexMonsterSpawnRef("spawn-primary", "M001", new HexCoord(2, 0), "primary_pressure") });
            var monsterConfigs = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, CombatState.CreateMonsterCatalog(config));
            var state = new CombatState(map, new HexCoord(0, 0), monsterConfigs, config);

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            Assert.That(state.ObjectiveCompleted, Is.False);
            // DEC-2026-07-03-02: 액션 카드는 EndAction + 몬스터 이동 해석 후에야 사용 가능.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            Assert.That(state.TryPlayerInvestigate(new HexCoord(1, 0)), Is.True);
            Assert.That(state.ObjectiveCompleted, Is.True);
            Assert.That(state.LastInvestigateResult, Does.Contain("Objective complete"));
        }

        private static HexMapData CreateMapWithSpawn(params HexMonsterSpawnRef[] spawnRefs)
        {
            var cells = Enumerable.Range(0, 5)
                .Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false))
                .ToArray();
            return new HexMapData(cells, monsterSpawnRefs: spawnRefs);
        }

        private static HexMapData CreateMapWithBlockingObject(HexCoord blocked)
        {
            var cells = Enumerable.Range(0, 5)
                .Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false))
                .ToArray();
            return new HexMapData(
                cells,
                objectRefs: new[] { new HexMapObjectData("blocker", "Prop", "building", blocked, blocksMovement: true) });
        }
    }
}

