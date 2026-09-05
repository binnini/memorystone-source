using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 엘리트 스탯 강화(2026-08-20 #12)의 계약. 「엘리트」가 분류일 뿐 스탯 차별화가 없던 것을
    /// <c>stage_randomization.csv</c>의 배율 두 컬럼(eliteHpPercent·eliteDamagePercent)으로 연다.
    ///
    /// <para>🔑 규칙층은 <b>role만</b> 본다 — 랜덤화로 뽑힌 엘리트와 손저작 엘리트가 같은 대우를
    /// 받아야 한다. 랜덤화 경로에만 심으면 저작 엘리트가 조용히 약해진다.</para>
    /// </summary>
    public sealed class EliteMonsterStatsTests
    {
        private const string DefinitionId = "M900";
        private const int BaseHp = 20;
        private const int PatternDamage = 6;

        [Test]
        public void EliteSpawnsScaleTheirHpByTheAuthoredPercent()
        {
            var catalog = CreateCatalog();
            var map = CreateMapWithSpawns();

            var plain = ResolveHp(map, catalog, eliteHpPercent: 100);
            var boosted = ResolveHp(map, catalog, eliteHpPercent: 160);

            Assert.That(plain["normal-1"], Is.EqualTo(BaseHp));
            Assert.That(plain["elite-1"], Is.EqualTo(BaseHp), "배율 100이면 엘리트도 기본 체력 그대로다.");
            Assert.That(boosted["normal-1"], Is.EqualTo(BaseHp), "일반 몬스터는 배율의 영향을 받지 않는다.");
            Assert.That(boosted["elite-1"], Is.EqualTo(32), "20 × 160% = 32.");
        }

        [Test]
        public void EliteAttacksScaleTheirDamageByTheAuthoredPercent()
        {
            var plain = RunEliteHit(eliteDamagePercent: 100);
            var boosted = RunEliteHit(eliteDamagePercent: 150);

            Assert.That(plain, Is.EqualTo(PatternDamage), "배율 100이면 저작 피해 그대로다.");
            Assert.That(boosted, Is.EqualTo(PatternDamage * 150 / 100), "엘리트 피해 배율은 강화와 같은 % 축에 얹힌다.");
        }

        [Test]
        public void NonEliteAttacksIgnoreTheEliteDamageMultiplier()
        {
            Assert.That(RunHit(MonsterSpawnRoles.NormalEnemy, eliteDamagePercent: 150), Is.EqualTo(PatternDamage));
        }

        // --- helpers ------------------------------------------------------------------------------

        private static Dictionary<string, int> ResolveHp(HexMapData map, MonsterCatalogDefinition catalog, int eliteHpPercent)
        {
            var config = CreateConfig(eliteHpPercent: eliteHpPercent);
            return CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, catalog)
                .ToDictionary(spawn => spawn.Id, spawn => spawn.MaxHp);
        }

        private static int RunEliteHit(int eliteDamagePercent)
        {
            return RunHit(MonsterSpawnRoles.Elite, eliteDamagePercent);
        }

        private static int RunHit(string spawnRole, int eliteDamagePercent)
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(6),
                new HexCoord(1, 0),
                new[] { new MonsterConfig("attacker", new HexCoord(2, 0), 30, definitionId: DefinitionId, spawnRole: spawnRole) },
                CreateConfig(eliteDamagePercent: eliteDamagePercent),
                monsterCatalog: CreateCatalog());

            var before = state.Player.Hp;
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            return before - state.Player.Hp;
        }

        private static CombatConfig CreateConfig(int eliteHpPercent = 100, int eliteDamagePercent = 100)
        {
            return new CombatConfig(
                200, 30, 2, 1, 4, 0, 6, 1, 3,
                actionBudget: 4, movementHandSize: 1, actionHandSize: 5, playerVisionRange: 6,
                enemyDisengageRange: 4, eliteHpPercent: eliteHpPercent, eliteDamagePercent: eliteDamagePercent);
        }

        private static MonsterCatalogDefinition CreateCatalog()
        {
            return new MonsterCatalogDefinition(
                "elite-stats-test-catalog",
                "Elite Stats Test Catalog",
                new List<MonsterCatalogEntry>
                {
                    new MonsterCatalogEntry(
                        DefinitionId,
                        "실험체",
                        "test-melee",
                        "B001",
                        detectionRange: 8,
                        movePerTurn: 0,
                        hp: BaseHp,
                        attackSpeed: 1,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("AT90", "물기", range: 1, areaRadius: 0, damage: PatternDamage, cooldownTurns: 0)
                        })
                });
        }

        private static HexMapData CreateMapWithSpawns()
        {
            var cells = new List<HexCellData>();
            for (var q = -4; q <= 4; q++)
            {
                for (var r = -4; r <= 4; r++)
                {
                    var coord = new HexCoord(q, r);
                    if (System.Math.Abs(coord.S) <= 4)
                    {
                        cells.Add(new HexCellData(coord, "m2-ground", "demo", 1, true, false));
                    }
                }
            }

            return new HexMapData(
                cells,
                monsterSpawnRefs: new[]
                {
                    new HexMonsterSpawnRef("normal-1", DefinitionId, new HexCoord(2, 0), MonsterSpawnRoles.NormalEnemy),
                    new HexMonsterSpawnRef("elite-1", DefinitionId, new HexCoord(3, 0), MonsterSpawnRoles.Elite)
                });
        }
    }
}
