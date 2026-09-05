using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// T1(2026-08-06) — 소비 코드만 있고 부여 경로가 없던 상태이상 4종(쇠약·허점·무장 해제·봉인)의
    /// 신설 부여 경로 계약: ①카드 `stateEffect` 컬럼 범용 배선(첫 소비 카드 A14 으름장)
    /// ②정찰 핸들러 `scout.enemy_vulnerable`(S06 약점 간파) ③몬스터 패턴 화이트리스트(A025 꾸중=쇠약 ·
    /// A026 홀리기=봉인) ④함정 kind 2종(쇠약 가스·금제 부적). 확정 정본:
    /// docs/design/keyword-systematization-and-sts-insights.md §4 T1.
    /// </summary>
    public sealed class StateEffectGrantPathsTests
    {
        private static readonly HexCoord PlayerCoord = new HexCoord(0, 0);
        private static readonly HexCoord MonsterCoord = new HexCoord(1, 0);

        [Test]
        public void AttackCardStateEffectColumnAppliesWeakenToTargetMonster()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var monsterId = state.Monsters.Single().Id;

            Assert.That(state.TryPlayerAttack(MonsterCoord, "A14"), Is.True, state.LastFailureReason);

            var weaken = state.ActiveEffects.Where(effect => effect.Kind == StatusEffectKind.Weaken).ToList();
            Assert.That(weaken, Has.Count.EqualTo(1), "으름장은 겨눈 몬스터에게 쇠약 하나를 걸어야 한다.");
            Assert.That(weaken[0].TargetUnitId, Is.EqualTo(monsterId));
            Assert.That(weaken[0].Amount, Is.EqualTo(30), "강도는 stateEffect 컬럼(Weaken:30)이 정본.");
            Assert.That(weaken[0].RemainingTurns, Is.EqualTo(2), "지속은 duration 컬럼이 정본.");
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(8), "피해 2도 함께 들어가야 한다 — 부여가 피해를 대체하면 안 된다.");
        }

        [Test]
        public void ScoutVulnerableHandlerAppliesToRevealedEnemies()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var monsterId = state.Monsters.Single().Id;

            Assert.That(state.TryPlayerScout(MonsterCoord, "S06"), Is.True, state.LastFailureReason);

            var vulnerable = state.ActiveEffects.Where(effect => effect.Kind == StatusEffectKind.Vulnerable).ToList();
            Assert.That(vulnerable, Has.Count.EqualTo(1), "약점 간파는 드러난 적에게 허점을 걸어야 한다.");
            Assert.That(vulnerable[0].TargetUnitId, Is.EqualTo(monsterId));
            Assert.That(vulnerable[0].Amount, Is.EqualTo(2), "강도는 stateEffect 컬럼(Vulnerable:2)이 정본.");
            Assert.That(vulnerable[0].RemainingTurns, Is.EqualTo(2));
            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(10), "정찰은 피해를 주지 않는다 — 셋업 전용.");
        }

        /// <summary>
        /// 몬스터 공격이 부여할 수 있는 kind는 화이트리스트가 제한한다(MonsterAi). T1은 꾸중(쇠약)과
        /// 홀리기(봉인)만 개방했다 — 허점의 몬스터 부여는 <b>보류가 사용자 결정</b>이므로 여기서
        /// false를 잠가, 나중에 열 때 의도적 결정을 강제한다.
        /// </summary>
        [Test]
        public void MonsterAttackWhitelistOpensWeakenAndSealOnly()
        {
            var whitelist = typeof(CombatState).GetMethod(
                "IsPersistentMonsterAttackStatusEffect",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(whitelist, Is.Not.Null);

            bool Allowed(StatusEffectKind kind) => (bool)whitelist.Invoke(null, new object[] { kind });

            Assert.That(Allowed(StatusEffectKind.Weaken), Is.True, "꾸중(A025)의 쇠약 부여.");
            Assert.That(Allowed(StatusEffectKind.Seal), Is.True, "홀리기(A026)의 봉인 부여.");
            Assert.That(Allowed(StatusEffectKind.Vulnerable), Is.False, "몬스터의 허점 부여는 보류(사용자 결정).");
            Assert.That(Allowed(StatusEffectKind.TorchLight), Is.False, "버프 오염 방지 가드.");
        }

        [Test]
        public void NewTrapKindsResolveToTheirStatusEffects()
        {
            Assert.That(CombatState.ToTrapStatusEffectKind(HexTrapEffectKind.Weaken), Is.EqualTo(StatusEffectKind.Weaken));
            Assert.That(CombatState.ToTrapStatusEffectKind(HexTrapEffectKind.Disarm), Is.EqualTo(StatusEffectKind.Disarm));
        }

        private static CombatState CreateState()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 6);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                PlayerCoord,
                MonsterCoord,
                config,
                cardCatalog: CreateCatalog());
        }

        private static CardCatalogDefinition CreateCatalog()
        {
            // cards.csv의 A14·S06 행과 같은 값 — 스키마 정합은 CardCatalogCsvImporterTests(ShippingData)가,
            // 여기서는 런타임 배선만 잠근다.
            return new CardCatalogDefinition(
                "state-effect-grant-paths-test",
                "State effect grant paths test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.Move1HexId, "Move 1", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "A14", "으름장", CardCategory.Action, CardEffectType.Attack,
                        1, 2, 2, CardEffectRefs.AttackDamage, "living_monster_in_range",
                        status: CardCatalogStatus.Approved, durationTurns: 2, stateEffect: "Weaken:30"),
                    new CardCatalogEntry(
                        "S06", "약점 간파", CardCategory.Action, CardEffectType.Scout,
                        1, 4, 0, CardEffectRefs.ScoutEnemyVulnerable, "walkable_map_cell",
                        areaRadius: 1, status: CardCatalogStatus.Approved, durationTurns: 2, stateEffect: "Vulnerable:2"),
                });
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }
    }
}
