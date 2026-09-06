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
    /// 보스의 이동 계약과, 페이즈 전환이 점유 등록을 다시 깐다는 계약(§14.1).
    ///
    /// <para>§21.3 개정(2026-08-05): <b>이 파일이 고정하던 계약이 뒤집혔다.</b> §14.3은 멀티셀 보스의
    /// 걷기를 통째로 막았고 그 근거는 "원판 클리어런스 경로탐색이 없다"였다. 이제 있다
    /// (<see cref="MovementQuery.FootprintRadius"/>) — 근거가 사라졌으므로 차단도 사라진다.
    /// 그래서 <c>BossStopsMovingOnceItGrowsToMultipleCells</c>가
    /// <c>MultiCellBossWalksOnceClearancePathfindingExists</c>로 뒤집혀 있다.</para>
    ///
    /// <para>🔑 걷기를 막던 사고들은 이제 게이트가 아니라 <b>경로탐색 자체</b>가 막는다. 그 계약은
    /// 두 층에서 고정된다: 순수 경로탐색은 <c>HexPathfinderClearanceTests</c>, 전투 통합은 여기.</para>
    /// </summary>
    public sealed class BossPhaseFootprintMobilityTests
    {
        private const string BossSpawnRefId = "boss-spawn";
        private const string ArenaId = "boss-arena-1";
        private const string BossUnitId = "boss-01";
        private const string BossDefinitionId = "M002";

        private static readonly HexCoord ArenaCenter = new HexCoord(6, 0);
        private static readonly HexCoord PlayerStart = new HexCoord(2, 0);
        private const int ArenaRadius = 3;
        // 이동 사거리를 넉넉히 잡아야 몸통 칸이 도달 집합 안에 들어와 점유 시험이 성립한다.
        private const int PlayerMoveRange = 3;

        // -----------------------------------------------------------------------------------------
        // 이동 게이트
        // -----------------------------------------------------------------------------------------

        [Test]
        public void SingleCellSealedBossStillMoves()
        {
            // 1페이즈 = 반경 0. 봉인돼 있어도 몸이 한 칸이면 일반 몬스터와 같은 경로탐색으로 움직인다.
            var state = CreateSealedArenaState();
            Assert.That(BossFootprintRadiusIsZero(state), Is.True, "이 시험의 전제는 1페이즈 반경 0이다.");

            var origin = BossCoord(state);
            var moved = false;
            for (var turn = 0; turn < 6 && !moved; turn++)
            {
                RunFullTurn(state);
                moved = BossCoord(state) != origin;
            }

            Assert.That(moved, Is.True, "반경 0 보스는 봉인 중에도 움직여야 한다(§14.3).");
        }

        [Test]
        public void MultiCellBossWalksOnceClearancePathfindingExists()
        {
            // §21.3: 2페이즈(반경 1)에서도 걷는다. 이것이 §14.3 계약의 정확한 반전이다.
            var state = CreateSealedArenaState();
            AdvanceToPhaseTwo(state);
            Assert.That(state.BossFootprintCoords, Has.Count.EqualTo(7), "2페이즈는 반경 1(7칸)이다.");

            var origin = BossCoord(state);
            var moved = false;
            for (var turn = 0; turn < 8 && !moved; turn++)
            {
                RunFullTurn(state);
                moved = BossCoord(state) != origin;
            }

            Assert.That(moved, Is.True, "멀티셀 보스도 원판 클리어런스 경로탐색으로 움직인다(§21.3).");
        }

        [Test]
        public void MultiCellBossNeverEndsAMoveCoveringThePlayer()
        {
            // 삼키기 금지: 원판이 플레이어를 덮는 자리에는 서지 않고 인접 링까지만 다가온다.
            // 인접 링은 모든 공격 shape에 항상 포함되므로 여기까지 와도 공격은 성립한다.
            var state = CreateSealedArenaState();
            AdvanceToPhaseTwo(state);

            for (var turn = 0; turn < 8; turn++)
            {
                RunFullTurn(state);
                Assert.That(
                    state.BossFootprintCoords.Contains(state.PlayerCoord),
                    Is.False,
                    "보스 몸이 플레이어를 덮은 채로 턴이 끝나면 안 된다.");
            }
        }

        [Test]
        public void WalkingBossBodyStaysInsideTheArena()
        {
            // 걷기 쪽 계약. 결계는 점유 캐시에서 TemporaryBlocked이므로 원판 클리어런스가 자동으로
            // 걸러낸다(§21.4 결정 3이 코드 0줄로 성립하는 근거).
            //
            // ⚠️ 결계는 아레나의 <b>바깥 이웃 칸</b>이다(HexMapAreaRef.EnumerateBoundaryRing이
            // `!inside.Contains(neighbor)`로 고른다) — 중심 거리 ArenaRadius가 아니라 <b>+1</b>이다.
            // 그래서 "링을 덮지 않는다"가 아니라 <b>"몸이 아레나를 벗어나지 않는다"</b>가 검사할 계약이다.
            var state = CreateSealedArenaState();
            AdvanceToPhaseTwo(state);
            var arena = new HashSet<HexCoord>(HexArea.CellsWithin(ArenaCenter, ArenaRadius));

            for (var turn = 0; turn < 8; turn++)
            {
                RunFullTurn(state);
                Assert.That(
                    state.BossFootprintCoords.Where(coord => !arena.Contains(coord)).ToList(),
                    Is.Empty,
                    "걸어 다니는 동안 몸이 아레나 밖으로 나가면 안 된다.");
            }
        }

        [Test]
        public void GrowthPullsTheBossBackWhenItsNewBodyWouldLeaveTheArena()
        {
            // 성장 쪽 계약(§22.5-1). 성장은 이동이 아니라 <b>제자리 확대</b>라 걷기에 붙은 원판
            // 클리어런스가 걸리지 않는다 — 반경 0 보스는 아레나 가장자리(중심 거리 ArenaRadius)에
            // 합법적으로 설 수 있고, 거기서 반경 1로 커지면 몸이 결계(거리 ArenaRadius+1)를 덮는다.
            // 그 순간이 유일한 발생점이므로 여기서만 재배치가 필요하다.
            var state = CreateSealedArenaState();
            // 속박으로 자리를 고정한다 — 안 그러면 페이즈가 오르기 전에 플레이어를 쫓아 안쪽으로 걸어간다.
            Assert.That(state.DebugApplyStatusToAllMonsters(StatusEffectKind.Immobilize, 20, 1), Is.EqualTo(1));
            var edge = new HexCoord(ArenaCenter.Q + ArenaRadius, ArenaCenter.R);
            Assert.That(ArenaCenter.DistanceTo(edge), Is.EqualTo(ArenaRadius), "전제: 아레나 가장자리 칸이다.");
            PlaceBossAt(state, edge);

            AdvanceToPhaseTwo(state);

            var arena = new HashSet<HexCoord>(HexArea.CellsWithin(ArenaCenter, ArenaRadius));
            Assert.That(
                state.BossFootprintCoords.Where(coord => !arena.Contains(coord)).ToList(),
                Is.Empty,
                $"커진 몸이 아레나를 벗어난 채로 남으면 안 된다. 재배치기록='{state.LastBossGrowthRepositionReport}'");
            Assert.That(
                state.LastBossGrowthRepositionReport,
                Is.Not.Empty,
                "겹침을 해소했다면 랩이 읽을 기록이 남아야 한다.");
        }

        [Test]
        public void GrowthDoesNotMoveTheBossWhenItAlreadyFits()
        {
            // 양성 대조: 재배치가 "성장할 때마다 무조건 옮긴다"가 아님을 고정한다.
            // 보스를 아레나 중앙에 묶어 두면(속박) 커져도 겹칠 것이 없으므로 제자리여야 한다.
            var state = CreateSealedArenaState();
            Assert.That(state.DebugApplyStatusToAllMonsters(StatusEffectKind.Immobilize, 20, 1), Is.EqualTo(1));
            var settled = BossCoord(state);

            AdvanceToPhaseTwo(state);

            Assert.That(BossCoord(state), Is.EqualTo(settled), "겹치지 않으면 성장은 보스를 옮기지 않는다.");
            Assert.That(
                state.LastBossGrowthRepositionReport,
                Is.Empty,
                "옮기지 않았으면 재배치 기록도 비어 있어야 한다.");
        }

        [Test]
        public void RootedMultiCellBossStillDoesNotMove()
        {
            // 양성 대조: 걷기가 열렸다고 <b>속박</b>까지 풀린 것은 아니다. 이동 차단의 유일한 축이
            // rooted로 남았음을 고정한다 — 이게 없으면 위 시험들은 "항상 움직임"으로도 통과한다.
            var state = CreateSealedArenaState();
            AdvanceToPhaseTwo(state);
            // 픽스처의 몬스터는 보스 하나뿐이라 "전체 적용"이 곧 보스 적용이다.
            Assert.That(state.DebugApplyStatusToAllMonsters(StatusEffectKind.Immobilize, 20, 1), Is.EqualTo(1));

            var settled = BossCoord(state);
            for (var turn = 0; turn < 6; turn++)
            {
                RunFullTurn(state);
                Assert.That(BossCoord(state), Is.EqualTo(settled), "속박된 보스는 제자리다.");
            }
        }

        [Test]
        public void BossBoundToAnUnsealedArenaStaysPutAndOffScreen()
        {
            // 🔴 <b>계약이 뒤집혔다</b>(2026-09-02 #4 · 사용자 확정: "보스는 아레나에 들어가기 전에는
            //    스폰되어서도 안 되고 움직여서도 안 된다"). 종전 시험은
            //    UnsealedSingleCellBossIsUnaffectedByTheGate로 그 반대를 못 박고 있었는데, 그때의
            //    「게이트」는 §21.3에서 지운 <b>멀티셀</b> 게이트를 가리켰다 — 그 계약(반경과 무관하게
            //    걷는다)은 지금도 <b>봉인된</b> 아레나 시험 둘이 그대로 지킨다.
            //
            // 🔑 아레나에 <b>묶이지 않은</b> 고정 보스는 여전히 존재만으로 조우이므로 이 게이트에
            //    걸리지 않는다 — 판정이 IsBossEncountered 하나라서 그렇다.
            var state = CreateArenaState();
            Assert.That(state.IsBossArenaBarrierActive, Is.False, "이 시험의 전제는 아직 봉인 전이다.");
            var origin = BossCoord(state);

            for (var turn = 0; turn < 6; turn++)
            {
                RunFullTurn(state);
                Assert.That(BossCoord(state), Is.EqualTo(origin), "아레나가 닫히기 전에는 보스가 제자리다.");
            }

            Assert.That(state.IsMonsterHiddenBeforeBossArena(BossUnitId), Is.True,
                "움직이지 않을 뿐 아니라 화면에도 서지 않는다.");
        }

        [Test]
        public void SealingTheArenaWakesTheBoss()
        {
            // 양성 대조: 위 시험은 "보스가 영영 안 움직인다"로도 통과한다. 봉인이 <b>깨우는</b> 것까지
            // 함께 못 박아야 게이트가 잠금이 아니라 문이 된다.
            var state = CreateSealedArenaState();
            Assert.That(state.IsMonsterHiddenBeforeBossArena(BossUnitId), Is.False, "봉인하면 보스가 드러난다.");

            var origin = BossCoord(state);
            var moved = false;
            for (var turn = 0; turn < 6 && !moved; turn++)
            {
                RunFullTurn(state);
                moved = BossCoord(state) != origin;
            }

            Assert.That(moved, Is.True, "봉인된 뒤에는 평소대로 움직인다.");
        }

        // -----------------------------------------------------------------------------------------
        // 페이즈 전환 시 점유 갱신
        // -----------------------------------------------------------------------------------------

        [Test]
        public void PhaseEntryRefreshesOccupancySoTheGrownBodyBlocksMovement()
        {
            // 반경이 0→1로 커지면 새로 덮은 6칸이 <b>즉시</b> 이동 차단에 반영되어야 한다.
            // 점유(runtimeStates)는 유도값이 아니라 캐시라, 전환에서 다시 깔지 않으면 오버레이·히트테스트는
            // 멀쩡한데 이동만 낡는 조용한 버그가 된다.
            var state = CreateSealedArenaState();
            AdvanceToPhaseTwo(state);

            var reachable = state.GetReachablePlayerMoves();
            // 플레이어가 선 칸은 제외한다: §16.2가 겹침을 해소해 플레이어를 몸통 밖으로 밀어내지만,
            // 밀 자리가 없으면 겹친 채로 남을 수 있다. 이 시험의 관찰 대상은 <b>점유 갱신</b>이지
            // 겹침 해소가 아니므로(그쪽은 BossPullAndOverlapTests가 고정한다) 그 칸은 빼고 본다.
            var footprint = state.BossFootprintCoords.Where(coord => coord != state.PlayerCoord).ToList();

            // 전제: 몸통 칸 중 적어도 하나가 이동 사거리 안에 있어야 시험이 성립한다(밖이면 무엇이든 통과한다).
            Assert.That(
                footprint.Any(coord => state.PlayerCoord.DistanceTo(coord) <= PlayerMoveRange),
                Is.True,
                "몸통이 이동 사거리 밖이면 이 시험은 아무것도 관찰하지 못한다 — 픽스처를 고칠 것.");

            Assert.That(
                footprint.Where(coord => reachable.ContainsKey(coord)).ToList(),
                Is.Empty,
                "전환 직후 커진 몸이 덮은 칸은 걸어 들어갈 수 없어야 한다.");
        }

        [Test]
        public void PhaseOneLeavesTheBossNeighboursWalkableBecauseItIsOnlyOneCell()
        {
            // 양성 대조: 1페이즈(반경 0)에는 보스 <b>이웃 칸</b>이 걸어 들어갈 수 있다.
            // 위 시험이 "무엇이든 항상 막힘"을 보고 통과하는 게 아님을 고정한다.
            var state = CreateSealedArenaState();
            var reachable = state.GetReachablePlayerMoves();
            var bossCoord = BossCoord(state);

            Assert.That(reachable.ContainsKey(bossCoord), Is.False, "보스가 선 칸 자체는 반경 0이어도 막힌다.");
            Assert.That(
                bossCoord.NeighborsInDirectionOrder().Any(coord => reachable.ContainsKey(coord)),
                Is.True,
                "반경 0 보스의 이웃 칸은 비어 있어야 한다.");
        }

        // -----------------------------------------------------------------------------------------
        // 픽스처
        // -----------------------------------------------------------------------------------------

        // 반경 0 = 몸이 중심 한 칸. §18 전까지는 "표시 자체가 없음"이 판별식이었지만, 이제 1페이즈에도
        // 중심 한 칸이 그려지므로 칸 수로 읽는다(2페이즈 이상은 7칸).
        private static bool BossFootprintRadiusIsZero(CombatState state) => state.BossFootprintCoords.Count == 1;

        private static HexCoord BossCoord(CombatState state) =>
            state.Monsters.Single(monster => monster.Id == BossUnitId).Coord;

        /// <summary>
        /// 보스를 특정 칸에 세운다. public 표면에는 몬스터 이동 API가 없고(전투 규칙이 정할 일이다)
        /// 이 시험은 <b>성장 직전의 자리</b>를 만들어야 하므로 내부 런타임 객체를 직접 잡는다
        /// (<c>BossLeapAttackTests</c>의 선례와 같은 방식이다).
        /// </summary>
        private static void PlaceBossAt(CombatState state, HexCoord coord)
        {
            var runtimeMonsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            runtimeMonsters.Single(monster => monster.Id == BossUnitId).Coord = coord;

            // 점유는 캐시다 — 다시 깔지 않으면 성장 판정이 낡은 자리를 본다.
            typeof(CombatState)
                .GetMethod("UpdateOccupancy", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(state, null);
        }

        private static void RunFullTurn(CombatState state)
        {
            state.EndAction();
            state.ResolveMonsterMovement();
            state.EndAction();
            state.ResolveMonsterAction();
        }

        /// <summary>TurnCount 지표라 턴을 돌리면 2페이즈(임계 1)에 들어간다.</summary>
        private static void AdvanceToPhaseTwo(CombatState state)
        {
            for (var turn = 0; turn < 4 && state.BossPhases.Single().CurrentPhase < 2; turn++)
            {
                RunFullTurn(state);
            }

            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(2), "2페이즈 진입이 이 시험의 전제다.");
        }

        private static CombatState CreateSealedArenaState()
        {
            var state = CreateArenaState();
            Assert.That(state.TryPlayerMove(new HexCoord(3, 0)), Is.True);
            Assert.That(state.IsBossArenaBarrierActive, Is.True, "봉인되지 않으면 게이트 시험이 무의미하다.");
            return state;
        }

        private static CombatState CreateArenaState()
        {
            var move = new CardDefinition(
                "arena-move", "Arena Move", CardCategory.Movement, CardEffectType.Move, 0, PlayerMoveRange, 0, targeting: "walkable_in_range",
                status: CardCatalogStatus.Approved, instanceId: "arena-move-instance");
            var strike = new CardDefinition(
                "arena-strike", "Arena Strike", CardCategory.Action, CardEffectType.Attack, 1, 1, 0, targeting: "living_monster_in_range",
                status: CardCatalogStatus.Approved, instanceId: "arena-strike-instance");
            var catalog = new CardCatalogDefinition(
                "test.boss-mobility", "Boss mobility test catalog",
                new[]
                {
                    new CardCatalogEntry(move.Id, move.DisplayName, move.Category, move.EffectType, move.Cost, move.Range, move.Amount, move.Targeting, status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(strike.Id, strike.DisplayName, strike.Category, strike.EffectType, strike.Cost, strike.Range, strike.Amount, strike.Targeting, status: CardCatalogStatus.Approved, targetMode: CardTargetMode.Enemy)
                });

            var monsters = new List<MonsterConfig>
            {
                new MonsterConfig(BossUnitId, ArenaCenter, 200, definitionId: BossDefinitionId, spawnRefId: BossSpawnRefId, spawnRole: MonsterSpawnRoles.Boss)
            };

            var hand = Enumerable.Range(0, 8).Select(_ => move).ToArray();
            return new CombatState(
                CreateArenaMap(),
                PlayerStart,
                monsters,
                new CombatConfig(2000, 200, 2, 1, 0, 8, 12, 1, 0, playerVisionRange: 12),
                cardCatalog: catalog,
                monsterCatalog: CreateMonsterCatalog(),
                movementDeck: new CardDeckState(null, hand, null, null),
                actionDeck: new CardDeckState(null, Enumerable.Range(0, 8).Select(_ => strike).ToArray(), null, null),
                drawOpeningHands: false,
                bossCatalog: CreateMobilityBossCatalog());
        }

        private static HexMapData CreateArenaMap()
        {
            var cells = HexArea.CellsWithin(new HexCoord(4, 0), 9)
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
                        HexArea.CellsWithin(ArenaCenter, ArenaRadius),
                        HexMapAreaRef.BossArenaPurpose,
                        BossSpawnRefId)
                });
        }

        /// <summary>출하와 같은 모양의 저작: 1페이즈 반경 0(작고 움직임) → 2페이즈 반경 1(크고 고정).</summary>
        private static BossCatalogDefinition CreateMobilityBossCatalog()
        {
            const string profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + ",테스트 보스,TurnCount,,,,,music.boss.test,\n";
            const string phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,0.6,0,,\n" +
                BossDefinitionId + ",2,1,0,0,0,1.1,1,,\n";

            return BossCatalogCsvConverter.Convert(
                new BossCatalogCsvSource(profiles, phases, "boss-mobility-test", "Boss Mobility Test"));
        }

        /// <summary>피해 0 근접 패턴 하나 — 관찰 대상은 위치뿐이라 플레이어가 죽으면 안 된다.</summary>
        private static MonsterCatalogDefinition CreateMonsterCatalog()
        {
            return new MonsterCatalogDefinition(
                "boss-mobility-test-catalog",
                "Boss Mobility Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        BossDefinitionId,
                        "테스트 보스",
                        "test-melee",
                        "B001",
                        detectionRange: 12,
                        movePerTurn: 2,
                        hp: 200,
                        attackSpeed: 1,
                        attackPatterns: new[]
                        {
                            new MonsterAttackPattern("A100", "근접", 1, 0, 0, cooldownTurns: 0, phaseMin: 0)
                        })
                });
        }
    }
}
