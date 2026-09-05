using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// §17 도약 공격. 도약은 경로를 걷지 않고 <b>착지 지점만 검사</b>하므로, 걸어서 닿지 못하는
    /// 플레이어에게 "다가오는 압박"을 돌려준다.
    ///
    /// <para>§21.3 개정(2026-08-05): <b>이 파일의 전제가 바뀌었다.</b> 멀티셀 보스는 이제 걷는다
    /// (원판 클리어런스 경로탐색 — 옛 전제는 "고정 보스"였다). 도약 규칙 자체는 한 줄도 바뀌지 않았고
    /// (걷기 계획을 먼저 세운 뒤 그 자리에서 못 덮을 때만 뛴다), <b>발동 조건이 훨씬 좁아졌을 뿐</b>이다.
    /// 그래서 픽스처의 거리를 도약이 유일한 답인 밴드로 옮겼다 — 아래 <see cref="FarPlayerCoord"/> 주석.</para>
    ///
    /// 이 파일이 고정하는 계약 넷:
    ///   1. 걸어서 닿지 못할 때 <b>착지 지점을 계획한다</b>(계획부).
    ///   2. 그 계획이 <b>실제로 굴러</b> 보스를 옮긴다(해소부).
    ///   3. 걸어서/제자리에서 이미 덮을 수 있으면 뛰지 않는다(도약은 최후 수단).
    ///   4. 속박(rooted)은 도약을 막는다 — <b>몸이 묶인 것</b>이기 때문이다.
    ///
    /// 🔴 첫 구현이 죽었던 자리(2026-08-02 되돌림): 봉인된 멀티셀 보스는 속박과 <b>같은 경로</b>
    /// (<see cref="MonsterAiPlanner.ApplyControlStatusConstraintToPlan"/>)를 타고 제자리 공격으로
    /// collapse되는데, collapse가 계획된 이동 좌표를 현재 좌표로 되돌려 착지 지점을 지웠다.
    /// 테스트 1이 정확히 그 지점을 고정한다.
    /// </summary>
    public sealed class BossLeapAttackTests
    {
        private const string BossSpawnRefId = "boss-spawn";
        private const string ArenaId = "boss-arena-leap";
        private const string BossUnitId = "boss-01";
        private const string BossDefinitionId = "M002";

        private static readonly HexCoord ArenaCenter = new HexCoord(6, 0);
        private static readonly HexCoord PlayerStart = new HexCoord(-1, 0);

        /// <summary>
        /// 봉인 뒤 물러난 자리 — <b>도약이 유일한 답인 거리</b>다(§21.3 이후 픽스처의 핵심 수치).
        ///
        /// <para>보스 중심 (6,0)에서 거리 6. 산술은 이렇다: 걷기 2 + 커버 도달 3(몸 반경 1 + ring-full-2의 2)
        /// = 5 &lt; 6이라 <b>걸어서는 못 덮고</b>, 도약 3 + 커버 도달 3 = 6이라 <b>뛰면 정확히 덮는다.</b>
        /// 거리 5 이하면 걷기가 답이라 도약이 아예 후보에 오르지 않고(그게 §21.3 이후의 정상이다),
        /// 거리 7 이상이면 뛰어도 못 덮어 역시 안 뛴다. 이 한 칸이 도약의 창이다.</para>
        /// </summary>
        private static readonly HexCoord FarPlayerCoord = new HexCoord(0, 0);

        /// <summary>도약 없이도 ring-full-2가 닿는 자리(가장자리 거리 2) — 도약이 나오면 안 되는 대조군.</summary>
        private static readonly HexCoord NearPlayerCoord = new HexCoord(3, 0);

        /// <summary>
        /// 기대 착지: 보스 (6,0)에서 3칸 뛴 (3,0). 거기서 shape 원점이 몸통 가장자리 (2,0)으로 보정되고
        /// ring-full-2(반경 2)가 (0,0)을 정확히 덮는다. 더 가까운 착지((4,0) 등)는 아직 닿지 않는다.
        /// </summary>
        private static readonly HexCoord ExpectedLanding = new HexCoord(3, 0);

        private const int ArenaRadius = 6;
        private const int PlayerMoveRange = 3;
        private const int LeapRange = 3;

        /// <summary>보스 이동력. 도약이 "걸어서 못 갈 때만"인지 판정하는 기준이라 상수로 묶어 둔다.</summary>
        private const int BossMovePerTurn = 2;

        // -----------------------------------------------------------------------------------------
        // 1. 계획부 — 착지 지점이 실제로 잡히는가
        // -----------------------------------------------------------------------------------------

        [Test]
        public void LeapPlanCarriesTheBossTowardAPlayerItCannotWalkTo()
        {
            var state = CreateSealedState(FarPlayerCoord);
            RefreshPlans(state);
            var boss = Boss(state);

            Assert.That(boss.PlannedLeap, Is.True, "걸어서 닿지 못하면 도약을 계획해야 한다(§17).");
            Assert.That(
                boss.TurnPlan.PlannedMoveCoord,
                Is.EqualTo(ExpectedLanding),
                "착지 지점이 곧 PlannedMoveCoord여야 예고=명중 계약이 유지된다.");
            Assert.That(
                boss.CurrentAttackPattern.LeapRange,
                Is.GreaterThan(0),
                "도약으로 커밋된 패턴은 도약형이어야 한다.");
        }

        [Test]
        public void GateDescriptionShowsLeapStateForLeapPatterns()
        {
            // §28 후속 T3: 도약은 패턴이 아니라 패턴의 <b>이동 속성</b>이라 게이트 판정(거리 창·쿨다운·
            // 커버)에는 안 나타난다 — 도약형 패턴 줄에 도약 상태(실제 계획 술어의 답)와 발동 경로
            // 안내가 덧붙어야 "강제 커밋했는데 왜 안 뛰나"가 랩 화면에서 풀린다.
            var state = CreateSealedState(FarPlayerCoord);
            RefreshPlans(state);

            var bossId = state.DebugListPatternLabMonsters().First().Id;
            var lines = string.Join("\n", state.DebugDescribeAttackPatternGates(bossId));

            Assert.That(lines, Does.Contain("도약 상태:"), "도약형 패턴 줄에는 도약 상태가 붙는다.");
            Assert.That(lines, Does.Contain("도약 조건 만들기"), "발동 경로(위치 관계) 안내가 함께 붙는다.");
        }

        [Test]
        public void LeapPlanSurvivesTheMultiCellMovementConstraint()
        {
            // 회귀 고정: 이 계약이 깨지면 계획은 서지만 같은 RefreshTurnPlan 말미에서 지워진다.
            var state = CreateSealedState(FarPlayerCoord);
            var boss = Boss(state);
            Assert.That(
                state.BossFootprintCoords, Has.Count.EqualTo(7),
                "전제: 몸이 7칸인 멀티셀 보스여야 collapse 경로를 타는 이 회귀가 성립한다.");

            RefreshPlans(state);

            Assert.That(
                boss.TurnPlan.PlannedMoveCoord,
                Is.Not.EqualTo(boss.Coord),
                "멀티셀 이동 제약이 도약 계획까지 제자리로 되돌리면 안 된다.");
        }

        // -----------------------------------------------------------------------------------------
        // 2. 해소부 — 계획이 실제로 구르는가
        // -----------------------------------------------------------------------------------------

        [Test]
        public void LeapResolutionActuallyMovesTheFixedBoss()
        {
            var state = CreateSealedState(FarPlayerCoord);
            RefreshPlans(state);
            var boss = Boss(state);
            var origin = boss.Coord;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            Assert.That(boss.Coord, Is.Not.EqualTo(origin), "계획만 서고 해소부가 되돌리면 화면에선 아무 일도 없다.");
            Assert.That(boss.Coord, Is.EqualTo(ExpectedLanding));
        }

        [Test]
        public void LeapUpdatesOccupancySoTheGrownBodyBlocksTheNewCells()
        {
            // 점유는 유도값이 아니라 캐시다 — 착지 뒤 갱신하지 않으면 이동 판정만 낡은 자리를 가리킨다.
            var state = CreateSealedState(FarPlayerCoord);
            RefreshPlans(state);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            Assert.That(
                state.BossFootprintCoords,
                Does.Contain(ExpectedLanding),
                "착지 뒤 몸통 투영이 새 자리를 가리켜야 한다.");
            Assert.That(
                state.GetReachablePlayerMoves().Keys.Any(coord => state.BossFootprintCoords.Contains(coord)),
                Is.False,
                "착지한 몸이 덮은 칸은 즉시 걸어 들어갈 수 없어야 한다.");
        }

        // -----------------------------------------------------------------------------------------
        // 3. 도약은 최후 수단이다
        // -----------------------------------------------------------------------------------------

        [Test]
        public void LeapIsReachableInsideAShippingSizedArena()
        {
            // 🔴 §22.3이 "출하 아레나(반경 4)에서는 도약이 영원히 발동하지 않는다"고 적었는데 <b>틀렸다</b>
            // (§24.1 정정). 그 계산은 <b>보스가 중앙에 고정</b>이라는 전제를 썼는데, §21.3이 없앤 것이
            // 바로 그 전제다. 보스가 걷게 된 지금 중심은 아레나 중심에서 거리 3까지 나갈 수 있고
            // (몸 반경 1이 아레나 안에 있어야 하므로) 플레이어는 거리 4까지 갈 수 있으니,
            // 최대 이격은 7이고 도약 창(거리 6)은 <b>도달 가능</b>하다.
            //
            // 이 시험은 그 정정을 산술이 아니라 <b>실행</b>으로 고정한다 — 같은 종류의 계산 실수를
            // 두 번 했으므로(결계 링 방향·이 건) 이 계약은 사람의 암산이 아니라 픽스처가 답해야 한다.
            const int shippingArenaRadius = 4;
            var bossEdge = new HexCoord(ArenaCenter.Q + 3, ArenaCenter.R);
            var playerEdge = new HexCoord(ArenaCenter.Q - 3, ArenaCenter.R);
            Assert.That(bossEdge.DistanceTo(playerEdge), Is.EqualTo(6), "전제: 도약 창은 거리 6이다.");

            var state = CreateSealedState(playerEdge, arenaRadius: shippingArenaRadius);
            PlaceBossAt(state, bossEdge);
            RefreshPlans(state);
            var boss = Boss(state);

            Assert.That(
                state.BossFootprintCoords.All(coord => ArenaCenter.DistanceTo(coord) <= shippingArenaRadius),
                Is.True,
                "전제: 보스 몸이 아레나 안에 있어야 이 배치가 합법이다.");
            Assert.That(boss.PlannedLeap, Is.True, "출하 크기 아레나에서도 도약 창은 도달 가능하다(§24.1).");
        }

        [Test]
        public void BossDoesNotLeapWhenItAlreadyCoversThePlayer()
        {
            // 매 턴 뛰면 플레이어의 위치 선점이 무의미해진다 — 닿을 때는 뛰지 않는다.
            // ⚠️ §21.3 이후 "안 뛴다"는 "제자리"가 아니다. 보스는 걸어서 자리를 고칠 수 있다.
            // 관찰 대상은 <b>도약하지 않았다</b>이므로, 이동 거리가 걷기 예산 안인지로 판정한다.
            var state = CreateSealedState(NearPlayerCoord);
            RefreshPlans(state);
            var boss = Boss(state);
            var origin = boss.Coord;

            Assert.That(boss.PlannedLeap, Is.False, "이미 덮을 수 있으면 도약하지 않는다.");

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(
                origin.DistanceTo(boss.Coord),
                Is.LessThanOrEqualTo(BossMovePerTurn),
                "걷기 예산을 넘게 움직였다면 그건 걷기가 아니라 도약이다.");
            Assert.That(
                state.BossFootprintCoords.Contains(state.PlayerCoord),
                Is.False,
                "걸어서 다가와도 몸으로 플레이어를 삼키지는 않는다(§21.3).");
        }

        [Test]
        public void BossWithoutALeapPatternOnlyWalksNoMatterHowFarThePlayerIs()
        {
            // 양성 대조: 위 시험들이 "무엇이든 항상 뛴다"를 보고 통과하는 게 아님을 고정한다.
            // 같은 거리·같은 픽스처이고 유일한 차이는 저작(leapRange)뿐이다 — 그런데도 착지하지 않는다.
            var state = CreateSealedState(FarPlayerCoord, leapRange: 0);
            RefreshPlans(state);
            var boss = Boss(state);
            var origin = boss.Coord;

            Assert.That(boss.PlannedLeap, Is.False, "leapRange 0이면 도약은 저작되지 않은 것이다.");

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(
                origin.DistanceTo(boss.Coord),
                Is.LessThanOrEqualTo(BossMovePerTurn),
                "도약 저작이 없으면 걷기 예산을 넘어설 수 없다.");
            Assert.That(
                boss.Coord,
                Is.Not.EqualTo(ExpectedLanding),
                "도약으로만 닿는 자리에 서 있으면 저작 없이 뛴 것이다.");
        }

        // -----------------------------------------------------------------------------------------
        // 4. 속박(rooted)은 도약을 막는다 — 걷기 금지(멀티셀)와 축이 다르다
        // -----------------------------------------------------------------------------------------

        [Test]
        public void RootedBossCannotLeap()
        {
            var state = CreateSealedState(FarPlayerCoord);
            var boss = Boss(state);
            InjectEffect(state, StatusEffectKind.Immobilize, boss.Id, remainingTurns: 3);
            RefreshPlans(state);
            var origin = boss.Coord;

            Assert.That(boss.PlannedLeap, Is.False, "속박은 몸을 묶으므로 도약도 막는다.");

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(boss.Coord, Is.EqualTo(origin));
        }

        // -----------------------------------------------------------------------------------------
        // 픽스처
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// public 투영(<see cref="MonsterRuntimeState"/>)에는 계획 좌표도 도약 플래그도 없다 —
        /// 이 시험의 관찰 대상이 바로 그 둘이라 내부 런타임 객체를 직접 잡는다.
        /// </summary>
        private static MonsterRuntime Boss(CombatState state)
        {
            var runtimeMonsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            return runtimeMonsters.Single(monster => monster.Id == BossUnitId);
        }

        /// <summary>
        /// 보스를 특정 칸에 세운다. public 표면에는 몬스터 이동 API가 없고(전투 규칙이 정할 일이다)
        /// 도약 시험은 <b>계획 직전의 자리</b>를 만들어야 하므로 내부 런타임 객체를 직접 잡는다.
        /// </summary>
        private static void PlaceBossAt(CombatState state, HexCoord coord)
        {
            Boss(state).Coord = coord;

            // 점유는 캐시다 — 다시 깔지 않으면 계획이 낡은 자리를 본다.
            typeof(CombatState)
                .GetMethod("UpdateOccupancy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, null);
        }

        /// <summary>
        /// 계획은 턴 경계에서만 다시 서므로, 봉인 직후(=이 시험이 관찰하고 싶은 상태)의 계획을 보려면
        /// 리프레시를 직접 한 번 돌려야 한다. 전체 턴을 굴리면 봉인 전 낡은 계획으로 보스가 먼저 걸어가
        /// 관찰 대상 자체가 사라진다.
        /// </summary>
        private static void RefreshPlans(CombatState state)
        {
            typeof(CombatState)
                .GetMethod("RefreshMonsterIntentStep", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, null);
        }

        private static void InjectEffect(CombatState state, StatusEffectKind kind, string unitId, int remainingTurns)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns, 0, "test"));
        }

        /// <summary>
        /// ⚠️ 봉인은 플레이어를 진입 칸에 세워 두지 않는다 — <see cref="CombatState.ResolveSealedBossArenaBattleStart"/>가
        /// "아레나 중앙 전방"(여기서는 (3,0))으로 옮긴다. 그 자리는 몸통 가장자리에서 거리 2라 ring-full-2가
        /// 이미 닿으므로, 멀리 선 플레이어를 시험하려면 <b>봉인한 뒤 다시 물러나야</b> 한다.
        /// </summary>
        private static CombatState CreateSealedState(
            HexCoord playerDestination,
            int leapRange = LeapRange,
            int arenaRadius = ArenaRadius)
        {
            var state = CreateArenaState(leapRange, arenaRadius);
            // 진입 칸은 아레나 경계여야 결계가 닫힌다 — 반경이 바뀌면 그 칸도 따라 움직인다.
            var entry = new HexCoord(ArenaCenter.Q - arenaRadius, ArenaCenter.R);
            Assert.That(state.TryPlayerMove(entry), Is.True);
            Assert.That(state.IsBossArenaBarrierActive, Is.True, "봉인되지 않으면 고정 보스 시험이 무의미하다.");

            if (state.PlayerCoord != playerDestination)
            {
                Assert.That(state.TryPlayerMove(playerDestination), Is.True, "봉인 뒤 아레나 안에서 자리를 잡는다.");
            }

            Assert.That(state.PlayerCoord, Is.EqualTo(playerDestination));
            return state;
        }

        private static CombatState CreateArenaState(int leapRange, int arenaRadius)
        {
            var move = new CardDefinition(
                "arena-move", "Arena Move", CardCategory.Movement, CardEffectType.Move, 0, PlayerMoveRange, 0,
                effectRef: CardEffectRefs.MoveBasic, targeting: "walkable_in_range",
                status: CardCatalogStatus.Approved, instanceId: "arena-move-instance");
            var strike = new CardDefinition(
                "arena-strike", "Arena Strike", CardCategory.Action, CardEffectType.Attack, 1, 1, 0,
                effectRef: CardEffectRefs.AttackDamage, targeting: "living_monster_in_range",
                status: CardCatalogStatus.Approved, instanceId: "arena-strike-instance");
            var catalog = new CardCatalogDefinition(
                "test.boss-leap", "Boss leap test catalog",
                new[]
                {
                    new CardCatalogEntry(move.Id, move.DisplayName, move.Category, move.EffectType, move.Cost, move.Range, move.Amount, move.EffectRef, move.Targeting, status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(strike.Id, strike.DisplayName, strike.Category, strike.EffectType, strike.Cost, strike.Range, strike.Amount, strike.EffectRef, strike.Targeting, status: CardCatalogStatus.Approved, targetMode: CardTargetMode.Enemy)
                });

            var monsters = new List<MonsterConfig>
            {
                new MonsterConfig(BossUnitId, ArenaCenter, 200, definitionId: BossDefinitionId, spawnRefId: BossSpawnRefId, spawnRole: MonsterSpawnRoles.Boss)
            };

            var hand = Enumerable.Range(0, 8).Select(_ => move).ToArray();
            return new CombatState(
                CreateArenaMap(arenaRadius),
                PlayerStart,
                monsters,
                new CombatConfig(2000, 200, 2, 1, 0, 8, 12, 1, 0, playerVisionRange: 12),
                cardCatalog: catalog,
                monsterCatalog: CreateMonsterCatalog(leapRange),
                movementDeck: new CardDeckState(null, hand, null, null),
                actionDeck: new CardDeckState(null, Enumerable.Range(0, 8).Select(_ => strike).ToArray(), null, null),
                drawOpeningHands: false,
                bossCatalog: CreateLeapBossCatalog());
        }

        private static HexMapData CreateArenaMap(int arenaRadius)
        {
            var cells = HexArea.CellsWithin(new HexCoord(4, 0), 10)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList();
            return new HexMapData(
                cells,
                monsterSpawnRefs: new[]
                {
                    new HexMonsterSpawnRef(BossSpawnRefId, BossDefinitionId, ArenaCenter, MonsterSpawnRoles.Boss)
                },
                areas: new[]
                {
                    new HexMapAreaRef(
                        ArenaId,
                        HexArea.CellsWithin(ArenaCenter, arenaRadius),
                        HexMapAreaRef.BossArenaPurpose,
                        BossSpawnRefId)
                });
        }

        /// <summary>1페이즈부터 반경 1 — 걷기가 막힌 고정 보스가 이 시험의 전제다.</summary>
        private static BossCatalogDefinition CreateLeapBossCatalog()
        {
            const string profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,music.boss.test,\n";
            const string phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1.1,1,,\n";

            return BossCatalogCsvConverter.Convert(
                new BossCatalogCsvSource(profiles, phases, "boss-leap-test", "Boss Leap Test"));
        }

        /// <summary>
        /// 피해 0 — 관찰 대상은 위치뿐이라 플레이어가 죽으면 안 된다.
        /// 근접기 하나 + 도약형 원판기 하나. 도약형의 거리 창(distMax 2)은 <b>현재 좌표가 아니라
        /// 착지 지점</b>에서 재야 한다는 계약도 이 저작이 함께 시험한다 — 현재 좌표(가장자리 거리 4)로
        /// 재면 이 패턴은 후보에서 빠지고 도약은 영원히 안 나온다.
        /// </summary>
        private static MonsterCatalogDefinition CreateMonsterCatalog(int leapRange)
        {
            return new MonsterCatalogDefinition(
                "boss-leap-test-catalog",
                "Boss Leap Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        BossDefinitionId,
                        "테스트 보스",
                        "test-melee",
                        "B001",
                        detectionRange: 12,
                        movePerTurn: BossMovePerTurn,
                        hp: 200,
                        attackSpeed: 1,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("A100", "근접", 1, 0, 0, cooldownTurns: 0, phaseMin: 0),
                            new MonsterAttackPattern(
                                "A101", "내려찍기", 2, 1, 0,
                                shapeId: AttackShapeLibrary.RingFull2,
                                cooldownTurns: 0,
                                phaseMin: 0,
                                distMax: 2,
                                leapRange: leapRange)
                        })
                });
        }
    }
}
