using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 함정 배치 기믹(trap-volley · §21.5 — 결정 4)의 규칙 계약. 정적 저작 0 + 재무장 삭제 이후
    /// 함정의 <b>유일한 공급원</b>이 이 기믹이므로, 여기의 핀이 곧 "보스전에 함정이 존재하는 방식"의
    /// 전부다: 주기 배치 · 정찰 전 숨김(2026-09-03 — 종전 "배치 즉시 발견"의 번복) · 배치 턴 공격
    /// 억제 · 전멸기 시퀀스 양보(쿨다운 미소모) · 기절 저작 거부 · 서스펜드 왕복.
    /// </summary>
    public sealed class BossTrapVolleyTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 40;
        private const int TrapDamage = 3;
        private const int VolleyCount = 2;

        [Test]
        public void PlacesAHiddenVolleyOnScheduleAndSkipsTheAttackThatTurn()
        {
            var state = CreateTrapVolleyBossState();

            // 배치 턴의 예고 커밋 시점: 살포 문법과 같은 래치+순수 질의로 공격이 이미 막혀 있어야 한다.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(InvokeIsMonsterAttackBlocked(state, BossUnitId), Is.True,
                "배치할 턴에는 예고 커밋 시점부터 공격이 막혀 있어야 한다(철조각 살포와 같은 문법).");

            // 기믹 턴 예고(2026-09-03 ⑥): 공격이 대체되는 턴에는 예고 프리뷰가 그 기믹 id를 실어야
            // 한다 — 이 축이 비면 배지가 서지 않아 보스가 "아무것도 안 하는 턴"으로 읽힌다.
            var bossPreview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(preview => preview.MonsterId == BossUnitId);
            Assert.That(bossPreview.BossGimmickIds, Has.Member("trap-volley"),
                "공격 차단과 같은 술어가 기믹 id를 예고 프리뷰에 싣는다.");

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.RuntimeTrapRefs, Has.Count.EqualTo(VolleyCount),
                "쿨다운 0인 첫 결의에서 페이즈 저작 개수만큼 배치된다.");
            Assert.That(state.LastBossTrapVolleyReport, Is.Not.Empty, "배치 기록은 랩 관찰 문자열로 남는다.");

            // 🔴 배치 시점에는 발견되지 않는다(2026-09-03 사용자 확정 — 종전 "배치 즉시 발견"[§21.5
            // 결정 4]의 번복). 저작 함정과 같이 정찰이 유일한 발견 경로다.
            var revealed = state.CreateSuspendSnapshot().RevealedTrapCoords.Select(coord => coord.ToCoord()).ToList();
            foreach (var trap in state.RuntimeTrapRefs)
            {
                Assert.That(revealed, Has.No.Member(trap.Coord),
                    "보스가 심은 함정은 정찰 전까지 숨어 있어야 한다.");
            }

            // HUD 카운트다운 투영(2026-09-03 ⑥): 배치 직후에는 다음 배치까지 정확히 주기만큼 남는다.
            // 1 = 다가오는 몬스터 페이즈(쿨다운 C의 발동은 C+1페이즈 뒤). 철조각 미저작이라 살포는 -1.
            state.GetBossGimmickCountdowns(BossUnitId, out var propVolleyTurns, out var trapVolleyTurns);
            Assert.That(propVolleyTurns, Is.EqualTo(-1), "iron-scrap 미저작 보스의 살포 카운트다운은 -1이다.");
            Assert.That(trapVolleyTurns, Is.EqualTo(4), "배치 직후 남은 턴 = 주기(interval 4).");

            // 배치가 없는 다음 턴에는 평소대로 공격한다.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(InvokeIsMonsterAttackBlocked(state, BossUnitId), Is.False,
                "쿨다운 중에는 공격을 막지 않는다 — 일어나지 않을 배치가 공격을 삼키면 빈 턴이 생긴다.");
        }

        [Test]
        public void ScoutingRevealsPlacedRuntimeTraps()
        {
            // 정찰(과 랩 디버그)의 공유 배관이 런타임 함정도 발견해야 한다 —
            // HexVisibilityRuntime.RevealTrapsInArea는 맵 저작 함정만 순회하므로, 이 짝
            // (RevealRuntimeTrapsInArea)이 빠지면 숨김 전환 이후 보스 함정은 영영 발견할 수 없다.
            var state = CreateTrapVolleyBossState();
            RunFullTurn(state);
            var trap = state.RuntimeTrapRefs.First();

            var found = state.DebugRevealTrapsForTuning(trap.Coord, 0);

            Assert.That(found, Is.GreaterThanOrEqualTo(1), "정찰 배관이 런타임 함정을 발견 수에 센다.");
            var revealed = state.CreateSuspendSnapshot().RevealedTrapCoords.Select(coord => coord.ToCoord()).ToList();
            Assert.That(revealed, Does.Contain(trap.Coord), "발견된 런타임 함정은 저작 함정과 같은 기록에 남는다.");
        }

        [Test]
        public void SteppingOnAPlacedTrapTriggersItOnceAndConsumesIt()
        {
            var state = CreateTrapVolleyBossState();
            RunFullTurn(state);
            var trap = state.RuntimeTrapRefs.First();

            var hpBefore = state.Player.Hp;
            Assert.That(state.TryDebugMovePlayer(trap.Coord), Is.True, state.LastFailureReason);
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - TrapDamage),
                "런타임 함정은 저작 함정과 같은 트리거 파이프라인을 탄다 — 밟으면 효과가 적용된다.");

            // 일회성 소진은 기존 배관(consumedTrapIds) 그대로다: 다시 밟아도 터지지 않는다.
            var consumedTrapCell = state.PlayerCoord;
            Assert.That(state.TryDebugMovePlayer(new HexCoord(-2, 0)), Is.True, state.LastFailureReason);
            hpBefore = state.Player.Hp;
            Assert.That(state.TryDebugMovePlayer(consumedTrapCell), Is.True, state.LastFailureReason);
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore), "소진된 일회성 함정은 판에서 사라진 것과 같다.");
        }

        [Test]
        public void YieldsToTheAnnihilationSequenceWithoutConsumingCooldown_RegardlessOfMechanicOrder()
        {
            // §21.5 사용자 확정: 전멸기 시퀀스 중 배치 금지. 런타임 다량 배치는 저작으로 안전지대 틈을
            // 보장할 수 없으므로, 이 양보가 "반드시 도달할 수 있는 안전지대" 계약의 방어선이다.
            foreach (var mechanicIds in new[] { "annihilation|trap-volley", "trap-volley|annihilation" })
            {
                var state = CreateComboBossState(mechanicIds);

                RunFullTurn(state); // T1 점프
                RunFullTurn(state); // T2 예고
                RunFullTurn(state); // T3 폭발
                Assert.That(state.RuntimeTrapRefs, Is.Empty,
                    $"[{mechanicIds}] 전멸기 시퀀스 3턴(점프·예고·폭발) 동안은 배치하지 않는다.");
                Assert.That(GetTrack(state).TrapVolleyCooldownTurns, Is.Zero,
                    $"[{mechanicIds}] 양보한 턴의 쿨다운은 소모하지 않는다 — 소모하면 배치가 영영 밀린다.");

                RunFullTurn(state); // T4: 시퀀스가 끝난 다음 턴
                Assert.That(state.RuntimeTrapRefs, Has.Count.EqualTo(VolleyCount),
                    $"[{mechanicIds}] 시퀀스 종료 직후에 곧바로 배치된다.");
            }
        }

        [Test]
        public void PlacingAVolleyGrantsTheAuthoredGuardBlockAndItPersistsUntilBroken()
        {
            // §21.8 제안 4: 배치 턴에 몸을 굳힌다. "다음 배치 전에 깎아라"가 플레이어의 퍼즐이므로
            // 자연 감쇠가 생기면 이 계약이 깨진다 — 그래서 이 보스는 견고 특성을 저작으로 갖는다
            // (2026-08-10부터 몬스터 방어막의 기본은 턴 시작 소거이고, 견고만 예외다).
            const int GuardBlock = 6;
            var state = CreateBossState("trap-volley", TrapVolleyParams(guardBlock: GuardBlock));

            RunFullTurn(state); // 배치 턴
            Assert.That(state.RuntimeTrapRefs, Is.Not.Empty, "전제: 배치가 실제로 일어났다.");
            Assert.That(BossBlock(state), Is.EqualTo(GuardBlock), "배치가 일어난 턴에 저작량만큼 방어막을 얻는다.");

            RunFullTurn(state); // 쿨다운 턴 — 배치 없음
            Assert.That(BossBlock(state), Is.EqualTo(GuardBlock),
                "방어막은 부술 때까지 남는다 — 턴 경과로 소거되면 '깎아라' 퍼즐이 사라진다.");
        }

        [Test]
        public void StunTrapAuthoringIsRejectedAtParseTime()
        {
            // 🔴 §18.5 판정: 안전지대 도달 보장은 경로탐색이 아니라 그냥 거리(safeReach)다 — 이동을
            // 통째로 뺏는 함정은 그 계약을 조용히 깬다. 저작(CSV 한 줄)으로 되살릴 수 없어야 한다.
            Assert.That(
                () => CreateBossState("trap-volley", TrapVolleyParams(effectKind: "Stun")),
                Throws.ArgumentException.With.Message.Contains("Stun"),
                "기절 함정 저작은 파싱 시점에 거부된다.");
        }

        [Test]
        public void SuspendRoundTripPreservesPlacedTrapsAndCooldown()
        {
            var state = CreateTrapVolleyBossState();
            RunFullTurn(state);
            var placedBefore = state.RuntimeTrapRefs.Select(trap => (trap.TrapId, trap.Coord)).ToList();
            Assert.That(placedBefore, Has.Count.EqualTo(VolleyCount));

            var snapshot = state.CreateSuspendSnapshot();
            Assert.That(snapshot.RuntimeTraps, Has.Count.EqualTo(VolleyCount),
                "런타임 함정은 판에서 재유도할 수 없다 — 왕복하지 않으면 재개 후 통째로 사라진다.");

            var resumed = CreateTrapVolleyBossState();
            resumed.RestoreFromSuspend(snapshot);
            Assert.That(
                resumed.RuntimeTrapRefs.Select(trap => (trap.TrapId, trap.Coord)).ToList(),
                Is.EquivalentTo(placedBefore),
                "함정 본체(id·좌표)가 왕복 후에도 같아야 한다.");

            // 쿨다운이 함께 왕복되지 않으면 재개 후 첫 결의에서 볼리가 한 번 더 깔린다(세이브 스컴).
            RunFullTurn(resumed);
            Assert.That(resumed.RuntimeTrapRefs, Has.Count.EqualTo(VolleyCount),
                "재개 직후 턴에 추가 배치가 없어야 한다 — 쿨다운 왕복의 증거다.");

            // 복원된 함정도 밟으면 터진다(효과 페이로드까지 왕복됐다는 증거).
            var hpBefore = resumed.Player.Hp;
            Assert.That(resumed.TryDebugMovePlayer(resumed.RuntimeTrapRefs.First().Coord), Is.True, resumed.LastFailureReason);
            Assert.That(resumed.Player.Hp, Is.EqualTo(hpBefore - TrapDamage));
        }

        // --- helpers ------------------------------------------------------------------------------

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static int BossBlock(CombatState state)
        {
            return state.Monsters.Single(monster => monster.Id == BossUnitId).Block;
        }

        private static BossPhaseTrack GetTrack(CombatState state)
        {
            // 3-B: 트랙은 BossEncounterState 소유 — 리플렉션 문자열 대신 internal 이음새로 집는다.
            return state.BossPhaseTracksForTests.Single();
        }

        private static bool InvokeIsMonsterAttackBlocked(CombatState state, string monsterId)
        {
            var runtimeMonsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            var monster = runtimeMonsters.Single(candidate => candidate.Id == monsterId);
            var method = typeof(CombatState).GetMethod(
                "IsMonsterAttackBlocked", BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)method.Invoke(state, new object[] { monster });
        }

        private static CombatState CreateTrapVolleyBossState()
        {
            return CreateBossState("trap-volley", TrapVolleyParams());
        }

        /// <summary>전멸기(주기 4·예고 2·아레나 없음 → 제자리 점프)와 함정 배치를 함께 가진 보스.</summary>
        private static CombatState CreateComboBossState(string mechanicIds)
        {
            var annihilation =
                "annihilationPhaseMin=1;annihilationIntervalTurns=6;annihilationTelegraphTurns=2;" +
                "annihilationDamage=5;annihilationCandidateCells=3;annihilationRealSafeCells=2;" +
                "annihilationCandidateSpacing=3;annihilationSafeReach=2;annihilationFallbackRadius=3;" +
                "annihilationLandingBlastRadius=1;annihilationLandingBlastDamage=0";
            return CreateBossState(mechanicIds, annihilation + ";" + TrapVolleyParams());
        }

        private static string TrapVolleyParams(string effectKind = "Damage", int guardBlock = 0)
        {
            return $"trapVolleyPhaseMin=1;trapVolleyIntervalTurns=4;trapVolleyByPhase={VolleyCount};" +
                   "trapVolleyRingRadius=2;trapVolleyMinSpacing=2;" +
                   $"trapVolleyEffectKind={effectKind};trapVolleyEffectAmount={TrapDamage};" +
                   $"trapVolleyEffectDurationTurns=0;trapVolleyMaxArmed=8;trapVolleyGuardBlock={guardBlock}";
        }

        private static CombatState CreateBossState(string mechanicIds, string mechanicParams)
        {
            var playerCoord = new HexCoord(1, 0);
            var bossCoord = new HexCoord(3, 0);
            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + $",테스트 보스,TurnCount,{mechanicIds},{mechanicParams},,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n";

            return new CombatState(
                CombatState.CreateDemoMap(7),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "boss-trap-volley-test-catalog",
                    "Boss Trap Volley Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            BossDefinitionId,
                            "테스트 보스",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: BossBaseHp,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("A100", "기본", 1, 0, 0, cooldownTurns: 0, phaseMin: 0)
                            },
                            // 견고(2026-08-10): 몬스터 방어막은 이제 전체 턴 시작에 소거되는 것이 기본이고,
                            // 이 특성만 예외다. 출하 저작(monster_catalog.csv M002 불가살 sturdyBlock=TRUE)과
                            // 같은 값을 픽스처에도 준다 — 안 주면 trap-volley 방어막이 다음 턴에 증발해
                            // "깎아라" 퍼즐이 성립하지 않는다.
                            sturdyBlock: true)
                    }),
                bossCatalog: BossCatalogCsvConverter.Convert(
                    new BossCatalogCsvSource(profiles, phases, "boss-trap-volley-test", "Boss Trap Volley Test")));
        }
    }
}
