using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 형상 footprint(2026-09-03 · 삼각형 정예) 계약. 보스 원판(<see cref="BossFootprintTests"/>·<see cref="BossWeakSpotTests"/>)과
    /// 같은 술어를 지나므로 이 테스트는 「원판이 아닌 형상에서도 같은 것이 성립하는가」만 잰다.
    /// </summary>
    public sealed class MultiCellMonsterFootprintTests
    {
        private const string EliteDefinitionId = "M010";
        private const string EliteUnitId = "elite-01";
        private const int EliteHp = 400;
        private const int AttackAmount = 10;
        private const string AttackCardId = "A000";
        private const string ScoutCardId = "S000";
        private static readonly HexCoord EliteAnchor = new HexCoord(0, 0);

        [Test]
        public void TriangleMonsterOccupiesAnchorEastAndSouthEast()
        {
            var state = CreateState();
            var occupied = state.GetMonsterOccupiedCoords(EliteUnitId);
            Assert.That(occupied, Is.EquivalentTo(new[] { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1) }));
            Assert.That(state.BossFootprintCoords, Is.EquivalentTo(occupied),
                "점유 오버레이 좌표는 비보스 다중 칸 몬스터도 낸다 — 불가살과 같은 붉은 테두리로 몸을 읽힌다.");
            Assert.That(state.GetMonsterFootprintShapeOffsets(EliteUnitId), Is.EqualTo(MonsterFootprints.TriangleOffsets),
                "표현 계층이 모델을 세 칸의 무게중심(꼭짓점)에 세우는 근거.");
        }

        [Test]
        public void AnyBodyCellCountsAsTheMonsterForHitTesting()
        {
            var state = CreateState();
            var elite = Elite(state);
            var hpBefore = elite.Combatant.Hp;

            Attack(state, new HexCoord(1, 0));

            Assert.That(hpBefore - elite.Combatant.Hp, Is.EqualTo(AttackAmount), "앵커가 아닌 몸통 칸을 때려도 맞는다.");
        }

        [Test]
        public void DistanceUsesTheNearestBodyCellNotTheAnchor()
        {
            var state = CreateState();
            var elite = Elite(state);
            Assert.That(state.GetDistanceToMonster(elite, new HexCoord(3, 0)), Is.EqualTo(2), "(3,0)에서 가장 가까운 몸통 칸은 (1,0)이다.");
            Assert.That(state.GetDistanceToMonster(elite, new HexCoord(0, 1)), Is.EqualTo(0), "몸통 칸 위는 거리 0.");
        }

        [Test]
        public void TriangleMonsterIsImmuneToKnockback()
        {
            var state = CreateState();
            var before = Elite(state).Coord;

            state.ApplyDirectionalKnockback(state.PlayerCoord, EliteUnitId, distance: 2, impactDamage: 0);

            Assert.That(Elite(state).Coord, Is.EqualTo(before), "몸이 여러 칸이면 원판 보스와 같은 이유로 넉백 면역이다.");
        }

        [Test]
        public void SpawnRejectsWhenABodyCellIsOccupied()
        {
            var state = CreateState(withElite: false, playerCoord: new HexCoord(1, 0));
            Assert.That(state.DebugSandboxSpawnMonsterAt(EliteDefinitionId, EliteAnchor, 0, out _), Is.False,
                "앵커는 비어 있어도 동쪽 몸통 칸에 플레이어가 서 있으면 설 수 없다.");
            Assert.That(state.LastFailureReason, Does.Contain("body cell"));

            var open = CreateState(withElite: false, playerCoord: new HexCoord(-3, 0));
            Assert.That(open.DebugSandboxSpawnMonsterAt(EliteDefinitionId, EliteAnchor, 0, out var spawnedId), Is.True, open.LastFailureReason);
            Assert.That(open.GetMonsterOccupiedCoords(spawnedId).Count, Is.EqualTo(3));
        }

        [Test]
        public void PathfindingClearanceChecksEveryBodyCell()
        {
            var map = CombatState.CreateDemoMap(4);
            var target = new HexCoord(1, 1);
            var states = new Dictionary<HexCoord, HexCellRuntimeState>
            {
                [new HexCoord(2, 1)] = new HexCellRuntimeState(temporaryBlocked: true)
            };

            Assert.That(
                HexPathfinder.CanEnter(map, null, target, new MovementQuery(new HexCoord(0, 0), 1, unitId: "u"), states, null, out _),
                Is.True, "앵커 칸 자체는 비어 있다.");
            Assert.That(
                HexPathfinder.CanEnter(map, null, target, new MovementQuery(new HexCoord(0, 0), 1, unitId: "u", footprintOffsets: MonsterFootprints.TriangleOffsets), states, null, out _),
                Is.False, "동쪽 몸통 칸(2,1)이 막혀 있으면 삼각형은 그 자리에 설 수 없다.");
        }

        [Test]
        public void TriangleMonsterHasAWeakSpotInsideItsBodyRightAway()
        {
            var state = CreateState();
            var elite = Elite(state);
            Assert.That(state.TryGetBossWeakSpotCoord(elite, out var coord), Is.True, "소환 직후에도 취약 부위가 있다(정찰이 바로 통하도록).");
            Assert.That(state.IsMonsterOccupying(elite, coord), Is.True);
            Assert.That(state.GetBossWeakSpots(), Is.Empty, "미판명이면 표현에 아무것도 나오지 않는다.");
            Assert.That(state.MonsterHasBossWeakSpot(EliteUnitId), Is.True);
        }

        [Test]
        public void ScoutingRevealsTheWeakSpotAndDoublesDamageThere()
        {
            var state = CreateState();
            var elite = Elite(state);
            Assert.That(state.TryGetBossWeakSpotCoord(elite, out var weakSpot), Is.True);

            Scout(state, weakSpot);
            var known = state.GetBossWeakSpots();
            Assert.That(known.Select(spot => spot.Coord), Is.EqualTo(new[] { weakSpot }));
            Assert.That(known.Single().BossUnitId, Is.EqualTo(EliteUnitId));

            var hpBefore = elite.Combatant.Hp;
            Attack(state, weakSpot);
            Assert.That(hpBefore - elite.Combatant.Hp, Is.EqualTo(AttackAmount * 2), "판명된 취약 부위는 2배.");

            // 손패에 공격 카드가 한 장이라 같은 턴에 두 번 못 친다 — 판명 2턴 안에서 턴을 넘겨 다시 친다(2 → 1, 여전히 판명).
            RunMonsterPhase(state);
            var other = state.GetMonsterOccupiedCoords(EliteUnitId).First(coord => coord != weakSpot);
            hpBefore = elite.Combatant.Hp;
            Attack(state, other);
            Assert.That(hpBefore - elite.Combatant.Hp, Is.EqualTo(AttackAmount), "다른 몸통 칸은 그대로 100%.");
        }

        [Test]
        public void KnownWeakSpotLastsExactlyTwoMonsterPhases()
        {
            var state = CreateState();
            var elite = Elite(state);
            Assert.That(state.TryGetBossWeakSpotCoord(elite, out var weakSpot), Is.True);
            Scout(state, weakSpot);

            RunMonsterPhase(state); // 2 → 1
            Assert.That(state.GetBossWeakSpots().Single().Coord, Is.EqualTo(weakSpot), "판명 중에는 자리가 고정된다.");
            Assert.That(state.GetBossWeakSpots().Single().IsExpiringThisTurn, Is.True);

            RunMonsterPhase(state); // 1 → 0
            Assert.That(state.GetBossWeakSpots(), Is.Empty);
            Assert.That(state.TryGetBossWeakSpotCoord(elite, out _), Is.True, "판명이 끝나도 취약 부위 자체는 다시 뽑혀 존재한다.");
        }

        [Test]
        public void SuspendRoundTripPreservesTheEliteWeakSpot()
        {
            var state = CreateState();
            var elite = Elite(state);
            Assert.That(state.TryGetBossWeakSpotCoord(elite, out var weakSpot), Is.True);
            Scout(state, weakSpot);
            RunMonsterPhase(state); // 2 → 1

            var snapshot = state.CreateSuspendSnapshot();
            var resumed = CreateState();
            resumed.RestoreFromSuspend(snapshot);

            var spots = resumed.GetBossWeakSpots();
            Assert.That(spots.Single().Coord, Is.EqualTo(weakSpot), "재개가 자리를 다시 굴리면 세이브 스컴이 된다.");
            Assert.That(spots.Single().KnownTurnsRemaining, Is.EqualTo(1));
        }

        [Test]
        public void CatalogParsesFootprintColumnAndRejectsUnknownTokens()
        {
            const string header = "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath,footprint\n";
            var ok = MonsterCatalogCsvConverter.Convert(CsvSource(header + "M001,Tester,test-melee,B001,30,6,2,1,prototype,,tri\n"));
            Assert.That(ok.MonsterCatalog.Entries.Single().FootprintShape, Is.EqualTo(MonsterFootprintShape.Triangle));

            var single = MonsterCatalogCsvConverter.Convert(CsvSource(header + "M001,Tester,test-melee,B001,30,6,2,1,prototype,,\n"));
            Assert.That(single.MonsterCatalog.Entries.Single().FootprintShape, Is.EqualTo(MonsterFootprintShape.Single));

            var ex = Assert.Throws<System.ArgumentException>(() => MonsterCatalogCsvConverter.Convert(CsvSource(header + "M001,Tester,test-melee,B001,30,6,2,1,prototype,,hex\n")));
            Assert.That(ex.Message, Does.Contain("footprint"));
        }

        [Category("ShippingData")]
        [Test]
        public void ShippedCatalogAuthorsTriangleForGeogugwiAndDuokseokiniOnly()
        {
            var catalog = MonsterCatalogCsvConverter.Convert(MonsterCatalogCsvSource.FromDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory)).MonsterCatalog;
            var triangles = catalog.Entries.Where(entry => entry.FootprintShape == MonsterFootprintShape.Triangle).Select(entry => entry.Id).ToList();
            Assert.That(triangles, Is.EquivalentTo(new[] { "M010", "M012" }), "두억시니·거구귀만 3칸 삼각형이다.");
        }

        [Test]
        public void MonsterAttacksAPlayerStandingNextToAnyBodyCell()
        {
            // 플레이어 (2,0)은 앵커 (0,0)에서 2칸이지만 동쪽 몸통 칸 (1,0)에는 인접이다. 사거리 1 패턴이 닿아야 한다.
            var state = CreateState(playerCoord: new HexCoord(2, 0), detectionRange: 6);
            var hpBefore = state.Player.Hp;

            // FSM은 한 스텝에 한 단계만 전이한다(Patrol → Chase → Attack). 한 턴을 먼저 돌려 Chase에 올린다.
            RunMonsterPhase(state);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Monsters.Single().Intent.Type, Is.EqualTo(EnemyIntentType.Attack), "몸통 칸 기준 거리 1이면 공격 의도다.");
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.LessThan(hpBefore), "앵커 기준이면 닿지 않던 플레이어를 몸통 칸 기준으로 때린다.");
        }

        [Test]
        public void IntentPreviewRangeIsMeasuredFromTheNearestBodyCell()
        {
            var state = CreateState(playerCoord: new HexCoord(4, 0), detectionRange: 6);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single(candidate => candidate.MonsterId == EliteUnitId);
            Assert.That(preview.AttackRangeCoords, Does.Contain(new HexCoord(2, 0)), "(1,0) 몸통 칸에서 1칸.");
            Assert.That(preview.AttackRangeCoords, Does.Contain(new HexCoord(0, 2)), "(0,1) 몸통 칸에서 1칸.");
            Assert.That(preview.AttackRangeCoords, Has.No.Member(new HexCoord(3, 0)), "어느 몸통 칸에서도 2칸이면 밖이다.");
        }

        [Test]
        public void ShapeOriginIsTheBodyCellFacingThePlayer()
        {
            // line-2의 최전방은 원점 기준 2칸. 동쪽을 조준하면 원점이 앵커가 아니라 동쪽 몸통 칸 (1,0)이라 최전방은 (3,0)이다.
            var state = CreateState(
                playerCoord: new HexCoord(5, 0),
                detectionRange: 8,
                patterns: new[] { new MonsterAttackPattern("A100", "직선", 3, 0, 0, shapeId: AttackShapeLibrary.Line2) });
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single(candidate => candidate.MonsterId == EliteUnitId);
            Assert.That(preview.AttackRangeCoords, Does.Contain(new HexCoord(3, 0)), "형상 원점이 조준 방향 최전방 몸통 칸으로 옮겨져야 한다.");
        }

        // ── 픽스처 ──────────────────────────────────────────────────────────

        private static MonsterRuntime Elite(CombatState state)
        {
            var runtimeMonsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            return runtimeMonsters.Single(monster => monster.Id == EliteUnitId);
        }

        private static void Attack(CombatState state, HexCoord target)
        {
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerAttack(target, AttackCardId), Is.True, state.LastFailureReason);
        }

        private static void Scout(CombatState state, HexCoord target)
        {
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerScout(target, ScoutCardId), Is.True, state.LastFailureReason);
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            if (state.Phase == CombatPhase.PlayerMovement)
            {
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();
            }
        }

        private static void RunMonsterPhase(CombatState state)
        {
            if (state.Phase == CombatPhase.PlayerMovement)
            {
                Assert.That(state.EndAction(), Is.True);
                state.ResolveMonsterMovement();
            }

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static MonsterCatalogCsvSource CsvSource(string monsterCsv)
        {
            return new MonsterCatalogCsvSource(
                monsterCsv,
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,statusEffectDurationTurns,cooldownTurns\n" +
                "A001,Basic,1,3,attack.damage,player_in_range,V001,single,,2,0\n",
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\nM001,A001,1,true,,\n",
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv")),
                System.IO.File.ReadAllText(System.IO.Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv")));
        }

        private static CombatState CreateState(bool withElite = true, HexCoord? playerCoord = null, int detectionRange = 0, MonsterAttackPattern[] patterns = null)
        {
            var monsterConfigs = withElite
                ? new[] { new MonsterConfig(EliteUnitId, EliteAnchor, EliteHp, definitionId: EliteDefinitionId, spawnRole: "elite") }
                : new MonsterConfig[0];

            return new CombatState(
                CombatState.CreateDemoMap(8),
                playerCoord ?? new HexCoord(-3, 0),
                monsterConfigs,
                // enemyChaseRange 6: 0이면 FSM이 영원히 Patrol이라 공격 의도 테스트가 성립하지 않는다.
                new CombatConfig(200, EliteHp, 2, 1, 6, 6, 6, 1, 3, playerVisionRange: 8, actionBudget: 8, actionHandSize: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "tri-footprint-catalog",
                    "Triangle Footprint Catalog",
                    new[]
                    {
                        new MonsterCatalogEntry(
                            EliteDefinitionId,
                            "테스트 정예",
                            "test-melee",
                            "B001",
                            detectionRange: detectionRange,
                            movePerTurn: 0,
                            hp: EliteHp,
                            attackSpeed: 1,
                            attackPatterns: patterns ?? new[]
                            {
                                new MonsterAttackPattern("A100", "기본", 1, 0, 3, cooldownTurns: 0, phaseMin: 0)
                            },
                            footprintShape: MonsterFootprintShape.Triangle)
                    }),
                cardCatalog: TestCardCatalog());
        }

        private static CardCatalogDefinition TestCardCatalog()
        {
            return new CardCatalogDefinition(
                "tri-footprint-cards",
                "Triangle Footprint Cards",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move2Hex, "이동", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        AttackCardId, "공격", CardCategory.Action, CardEffectType.Attack,
                        1, 6, AttackAmount, "enemy_in_range",
                        status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        ScoutCardId, "정찰", CardCategory.Action, CardEffectType.Scout,
                        1, 6, 0, "walkable_map_cell", areaRadius: 0,
                        status: CardCatalogStatus.Approved)
                });
        }
    }
}
