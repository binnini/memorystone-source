using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 수호 기믹(guard · DEC-2026-09-03-03)의 규칙 계약: 페이즈 진입마다 1회 충전 부여 ·
    /// 해로운 상태이상 1회 무효(플레이어 수호와 같은 상태·같은 관문) · 버프는 소비하지 않음 ·
    /// 페이즈별 저작 길이 검증 · 서스펜드 왕복(재개 재부여 금지).
    ///
    /// 부여 관문의 <b>유닛 일반화</b>(2026-09-03 — 종전 PlayerUnitId 하드코딩)도 여기서 잰다:
    /// 플레이어 쪽 계약은 <c>RelicTriggerTests</c>가 계속 지킨다.
    /// </summary>
    public sealed class BossGuardMechanicTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossBaseHp = 40;

        [Test]
        public void GrantsAuthoredChargesOnFirstResolveAndAgainOnPhaseEntry()
        {
            var state = CreateGuardBossState("1|2");

            RunFullTurn(state);
            Assert.That(BossGuardCharges(state), Is.EqualTo(1),
                "조우 후 첫 결의에서 현재 페이즈(1) 몫의 충전이 부여된다.");

            RunFullTurn(state);
            Assert.That(BossGuardCharges(state), Is.EqualTo(1),
                "같은 페이즈에서는 다시 부여하지 않는다 — 매 턴 재충전이면 상태이상 축이 봉인된다.");

            // TurnCount 지표(progress = OverallTurnNumber - 1) 임계 2 → 3번째 턴의 결의에서 2페이즈 진입.
            RunFullTurn(state);
            Assert.That(CurrentPhase(state), Is.EqualTo(2), "전제: 페이즈가 올랐다.");
            Assert.That(BossGuardCharges(state), Is.EqualTo(1 + 2),
                "페이즈 진입이 새 몫(2)을 부여한다 — 잔여 충전과 합산된다(부여는 진입 보상).");
        }

        [Test]
        public void HarmfulStatusIsNegatedByConsumingACharge()
        {
            var state = CreateGuardBossState("1|1");
            RunFullTurn(state);
            Assert.That(BossGuardCharges(state), Is.EqualTo(1), "전제: 충전 1 보유.");

            Assert.That(AddStatus(state, StatusEffectKind.Weaken, BossUnitId, 2, 30), Is.False,
                "해로운 상태이상은 충전이 삼킨다 — 부여되지 않는다.");
            Assert.That(BossHas(state, StatusEffectKind.Weaken), Is.False, "무효화된 상태가 붙어 있으면 안 된다.");
            Assert.That(BossGuardCharges(state), Is.EqualTo(0), "충전 1이 소비되어 0이 된다(상태 자체가 제거).");

            Assert.That(AddStatus(state, StatusEffectKind.Immobilize, BossUnitId, 2, 0), Is.True,
                "충전이 다 떨어지면 상태이상은 그대로 통한다 — 취약을 없애는 노브가 아니다.");
            Assert.That(BossHas(state, StatusEffectKind.Immobilize), Is.True);
        }

        [Test]
        public void BuffsAndPlayerStatusesDoNotConsumeTheBossCharge()
        {
            var state = CreateGuardBossState("1|1");
            RunFullTurn(state);

            Assert.That(AddStatus(state, StatusEffectKind.Strength, BossUnitId, 2, 100), Is.True,
                "버프(비정화 대상)는 관문을 그대로 지난다.");
            Assert.That(BossGuardCharges(state), Is.EqualTo(1), "버프 부여가 충전을 소비하면 안 된다.");

            // 대상 일반화의 다른 면: 플레이어에게 걸리는 해로운 상태가 보스 충전을 소비하면 안 된다.
            Assert.That(AddStatus(state, StatusEffectKind.Weaken, PlayerId, 2, 30), Is.True,
                "플레이어는 수호 미보유 — 그대로 부여된다.");
            Assert.That(BossGuardCharges(state), Is.EqualTo(1), "다른 유닛의 부여가 보스 충전을 소비하면 안 된다.");
        }

        [Test]
        public void ChargesByPhaseLengthMismatchIsRejectedAtParseTime()
        {
            Assert.That(
                () => CreateGuardBossState("1"),
                Throws.ArgumentException.With.Message.Contains(GuardMechanicParams.ChargesByPhase),
                "페이즈 수와 길이가 다른 저작은 파싱 시점에 거부된다(volleyByPhase 선례).");
        }

        [Test]
        public void SuspendRoundTripDoesNotRegrantTheCurrentPhaseCharges()
        {
            var state = CreateGuardBossState("1|2");
            RunFullTurn(state);
            Assert.That(BossGuardCharges(state), Is.EqualTo(1), "전제: 1페이즈 몫 부여됨.");

            var snapshot = state.CreateSuspendSnapshot();
            var resumed = CreateGuardBossState("1|2");
            resumed.RestoreFromSuspend(snapshot);

            RunFullTurn(resumed);
            Assert.That(BossGuardCharges(resumed), Is.EqualTo(1),
                "재개 직후 결의가 현재 페이즈 몫을 다시 부여하면 안 된다 — 래치 왕복의 증거다.");
        }

        // --- helpers ------------------------------------------------------------------------------

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        /// <summary><c>CombatState.PlayerUnitId</c>(private const)와 같은 값 — 드리프트 시 테스트가 빨개진다.</summary>
        private const string PlayerId = "player";

        private static int CurrentPhase(CombatState state)
        {
            Assert.That(state.TryGetBossPhaseState(BossUnitId, out var phase), Is.True);
            return phase.CurrentPhase;
        }

        private static int BossGuardCharges(CombatState state)
        {
            return state.ActiveEffects
                .Where(effect => effect.Kind == StatusEffectKind.Guard
                                 && effect.TargetUnitId == BossUnitId
                                 && !effect.IsExpired)
                .Sum(effect => effect.Amount);
        }

        private static bool BossHas(CombatState state, StatusEffectKind kind)
        {
            return state.ActiveEffects.Any(effect =>
                effect.Kind == kind && effect.TargetUnitId == BossUnitId && !effect.IsExpired);
        }

        /// <summary>부여 관문(AddDurationStatusEffect) 직접 호출 — 반환값 = 실제 부여 여부(수호 무효 시 false).</summary>
        private static bool AddStatus(CombatState state, StatusEffectKind kind, string targetUnitId, int turns, int amount)
        {
            var method = typeof(CombatState).GetMethod(
                "AddDurationStatusEffect",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(StatusEffectKind), typeof(string), typeof(int), typeof(int), typeof(string) },
                null);
            return (bool)method.Invoke(state, new object[] { kind, targetUnitId, turns, amount, "test" });
        }

        /// <summary>TurnCount 지표 · 2페이즈(임계 2 = 3번째 턴 진입) 보스. 아레나 저작이 없어 존재만으로 조우 상태다.</summary>
        private static CombatState CreateGuardBossState(string chargesByPhase)
        {
            var playerCoord = new HexCoord(1, 0);
            var bossCoord = new HexCoord(3, 0);
            var profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + $",테스트 보스,TurnCount,guard,guardChargesByPhase={chargesByPhase},,,music.boss.test,\n";
            var phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n" +
                BossDefinitionId + ",2,2,0,0,1,1,0,,\n";

            return new CombatState(
                CombatState.CreateDemoMap(7),
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossBaseHp, definitionId: BossDefinitionId, spawnRole: "boss") },
                new CombatConfig(200, BossBaseHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "boss-guard-test-catalog",
                    "Boss Guard Test Catalog",
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
                            })
                    }),
                bossCatalog: BossCatalogCsvConverter.Convert(
                    new BossCatalogCsvSource(profiles, phases, "boss-guard-test", "Boss Guard Test")));
        }
    }
}
