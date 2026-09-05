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
    /// 카드가 화면에 찍는 수치는 저작값이 아니라 <b>실효값</b>이라는 계약을 고정한다.
    /// 몬스터 공격 예고가 실제로 들어올 피해를 보여주는 것(R-8)의 플레이어 방향 대칭이며,
    /// 표시와 집행이 같은 함수를 부르므로 갈라질 수 없다는 것이 이 테스트들이 지키는 성질이다.
    ///
    /// <para>대상 의존분(허점·표식)은 겨눈 칸이 있을 때만 합류한다(D-7) — 손에 들고만 있을 때
    /// 허점을 미리 더해 버리면 아직 고르지도 않은 대상의 사정을 카드가 아는 셈이 된다.</para>
    /// </summary>
    public sealed class CardDisplayValueTests
    {
        // --- 대상 무관분 --------------------------------------------------------------------------

        [Test]
        public void AttackDisplayValue_IsAuthoredAmount_WhenNoModifiersActive()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.GetDisplayValue(Attack(damage: 6)), Is.EqualTo(6),
                "보정이 없으면 실효값은 저작값 그대로여야 한다.");
        }

        [Test]
        public void AttackDisplayValue_AppliesOutgoingPercentModifiers()
        {
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Strength, state.Player.Id, amount: 100);

            Assert.That(state.GetDisplayValue(Attack(damage: 6)), Is.EqualTo(12),
                "강화(+100%)는 대상과 무관하므로 겨누기 전에도 카드 수치에 반영돼야 한다.");
        }

        [Test]
        public void AttackDisplayValue_AppliesWeakenPenalty()
        {
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Weaken, state.Player.Id, amount: 30);

            Assert.That(state.GetDisplayValue(Attack(damage: 10)), Is.EqualTo(7),
                "쇠약(−30%)도 같은 배율 지점에서 합산된다(10 × 70% = 7).");
        }

        // --- 손패 vs 목록: 쓰임새가 다르다 -----------------------------------------------------

        /// <summary>
        /// 🔑 <b>손패는 실시간, 목록은 기본값</b>(2026-09-02 #7 · 사용자 확정).
        ///
        /// <para>손패 카드는 "지금 쓰면 얼마인가"를 답하므로 강화·쇠약을 실시간으로 반영해야 하고,
        /// 덱 목록·잡화점·카드 제거·카드 연마 같은 <b>목록류</b>는 "이 카드가 무엇인가"를 답하는
        /// 자리라 저작 기본값을 보여줘야 한다. 전투 상황에 따라 목록 숫자가 흔들리면 무엇을 사고
        /// 무엇을 연마할지 판단할 기준선이 사라진다.</para>
        ///
        /// <para>종전에는 두 쓰임새가 <b>같은 스냅샷 하나</b>를 나눠 썼다. 두 표면을 <b>한 시험에서
        /// 나란히</b> 보는 이유는, 한쪽만 재면 둘 다 기본값으로 만들어도(=손패가 죽어도) 통과하기 때문이다.</para>
        /// </summary>
        [Test]
        public void HandShowsLiveValueWhileDeckListShowsAuthoredValue()
        {
            var state = CombatState.CreateDefaultDemo();
            var handValues = state.GetHandCards().Select(snapshot => snapshot.Value).ToList();
            Assume.That(handValues, Is.Not.Empty, "픽스처: 손패에 카드가 있어야 시험이 성립한다.");

            Inject(state, StatusEffectKind.Strength, state.Player.Id, amount: 100);

            var handAfter = state.GetHandCards().ToList();
            var deckList = state.GetDeckListCards().ToList();

            Assert.That(handAfter.Any(snapshot => snapshot.Value > 0), Is.True,
                "픽스처: 값이 있는 손패 카드가 하나는 있어야 한다.");
            Assert.That(
                handAfter.Where(snapshot => snapshot.BaseValue > 0)
                    .All(snapshot => snapshot.Value >= snapshot.BaseValue),
                Is.True,
                "손패는 강화를 실시간으로 반영해야 한다 — 이 축이 죽으면 손에 든 카드가 거짓말을 한다.");
            Assert.That(handAfter.Any(snapshot => snapshot.Value > snapshot.BaseValue), Is.True,
                "강화 +100%인데 손패 수치가 하나도 오르지 않았다.");

            Assert.That(
                deckList.All(snapshot => snapshot.Value == snapshot.BaseValue),
                Is.True,
                "목록류 수치는 저작 기본값이어야 한다 — 전투 보정이 새어 들어왔다(#7).");
        }

        // --- 대상 의존분 --------------------------------------------------------------------------

        [Test]
        public void AttackDisplayValue_ExcludesVulnerable_WhenNoTargetIsAimed()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();
            Inject(state, StatusEffectKind.Vulnerable, monster.Id, amount: 2);

            Assert.That(state.GetDisplayValue(Attack(damage: 6)), Is.EqualTo(6),
                "겨눈 대상이 없으면 허점은 합류하지 않는다 — 아직 누구를 때릴지 정해지지 않았다(D-7).");
        }

        [Test]
        public void AttackDisplayValue_IncludesVulnerable_WhenTargetIsAimed()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();
            Inject(state, StatusEffectKind.Vulnerable, monster.Id, amount: 2);

            Assert.That(state.GetDisplayValue(Attack(damage: 6), monster.Coord), Is.EqualTo(8),
                "그 몬스터를 겨누면 허점(타격당 +2)이 합류해야 한다.");
        }

        [Test]
        public void AttackDisplayValue_AppliesPercentBeforeVulnerableFlat()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();
            Inject(state, StatusEffectKind.Strength, state.Player.Id, amount: 100);
            Inject(state, StatusEffectKind.Vulnerable, monster.Id, amount: 2);

            // 배율 먼저, 고정치 나중: (6 × 200%) + 2 = 14. 순서가 뒤집히면 (6+2) × 200% = 16이 된다.
            Assert.That(state.GetDisplayValue(Attack(damage: 6), monster.Coord), Is.EqualTo(14),
                "허점은 퍼센트가 아니라 타격당 고정치이므로 배율 뒤에 더해져야 한다(D-10).");
        }

        // --- 방어 -------------------------------------------------------------------------------

        [Test]
        public void DefendDisplayValue_AppliesRupture()
        {
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Rupture, state.Player.Id, amount: 2);

            Assert.That(state.GetDisplayValue(Defend(shield: 5)), Is.EqualTo(3),
                "파열은 획득 방어도를 깎으므로 카드가 약속하는 방어 수치도 함께 줄어야 한다.");
        }

        // --- 스냅샷 3축 ---------------------------------------------------------------------------

        [Test]
        public void Snapshot_CarriesAuthoredAndEffectiveValueSeparately()
        {
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Strength, state.Player.Id, amount: 100);

            var card = state.GetHandCards().FirstOrDefault(candidate => candidate.Kind == CombatCardKind.Attack);
            if (card.Id == null)
            {
                Assert.Ignore("이 손패에 공격 카드가 없다 — 드로우에 의존하지 않는 판정은 위의 테스트들이 한다.");
            }

            Assert.That(card.Value, Is.GreaterThan(card.BaseValue),
                "강화 중에는 실효값이 저작값보다 커야 한다.");
            Assert.That(card.IsValueModified, Is.True,
                "저작값과 다르면 강조 대상으로 표시돼야 한다(D-6).");
        }

        // --- scaling 카드는 총합을 찍는다 (P4) ------------------------------------------------------

        [Test]
        public void SpentKiScalingCard_ShowsTheTotalItActuallyDeals()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var ki = state.ActionCostRemaining;
            Assert.That(ki, Is.GreaterThan(1), "이 판정은 기력이 2 이상 남아 있어야 의미가 있다.");

            var card = new CardDefinition(
                "TEST_SPENDALL",
                "테스트 전력",
                CardCategory.Action,
                CardEffectType.Attack,
                cost: 1,
                range: 3,
                amount: 4,
                costMode: CardCostMode.SpendAll,
                scalingMode: CardScalingMode.SpentKi);

            // 규칙이 hitCount 1로 총합을 한 번에 때리므로(GetAttackHitCount가 이 모드를 다루지 않는다)
            // 1타분을 찍으면 카드가 실제 피해를 과소 보고한다.
            Assert.That(state.GetDisplayValue(card), Is.EqualTo(4 * ki),
                "소모할 기력만큼 커진 총합이 그대로 보여야 한다.");
        }

        // --- 필드 오브젝트 격리 (회귀 핀) -----------------------------------------------------------

        [Test]
        public void FieldObjectDisplayValue_IgnoresDamageModifiers()
        {
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Strength, state.Player.Id, amount: 100);

            var field = new CardDefinition(
                "TEST_FIELD",
                "테스트 장판",
                CardCategory.Action,
                CardEffectType.FieldObject,
                cost: 1,
                range: 2,
                amount: 4);

            // 필드 피해는 배치 시점의 저작값이 그대로 들어가고(FieldObjectCardEffects가 ApplyDamage를
            // 직접 부른다) 강화·쇠약·허점·유물이 개입하지 않는다. 표시가 이를 앞서가면 화면만 거짓이 된다.
            Assert.That(state.GetDisplayValue(field), Is.EqualTo(4),
                "필드 오브젝트 피해는 공격 보정 축을 타지 않는다 — 표시도 보정하면 안 된다.");
        }

        // --- 미리보기의 순수성 ---------------------------------------------------------------------

        [Test]
        public void DisplayValue_DoesNotConsumeMark()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();
            SetMarkedMonster(state, monster.Id);

            var first = state.GetDisplayValue(Attack(damage: 3), monster.Coord);
            var second = state.GetDisplayValue(Attack(damage: 3), monster.Coord);

            Assert.That(first, Is.EqualTo(6), "표식이 걸린 대상을 겨누면 배율 2가 반영돼야 한다.");
            Assert.That(second, Is.EqualTo(first),
                "미리보기를 두 번 그려도 표식이 소모되면 안 된다 — 카드를 쓰기도 전에 배율이 사라진다.");
        }

        [Test]
        public void DisplayValue_DoesNotSpendKi()
        {
            var state = CombatState.CreateDefaultDemo();
            var before = state.ActionCostRemaining;

            state.GetDisplayValue(Attack(damage: 3));

            Assert.That(state.ActionCostRemaining, Is.EqualTo(before),
                "수치를 그리는 것만으로 기력이 줄면 안 된다.");
        }

        // --- 헬퍼 -------------------------------------------------------------------------------

        // --- 사거리 축(2026-08-20 #13) ------------------------------------------------------------

        /// <summary>
        /// #13 감사에서 나온 실제 구멍: 이동 카드의 거리는 민첩·둔화·지각(X07)·유물이 실제로 바꾸는데
        /// 카드 문안에는 저작 거리가 리터럴("최대 3칸")로 박혀 있어 아무리 빨라져도 숫자가 안 변했다.
        /// 문안을 {Range} 토큰으로 바꾸고 토큰이 규칙과 같은 함수를 부르게 해 잠근다.
        /// </summary>
        [Test]
        public void MoveCardText_ShowsTheEffectiveRange_WhenAgilityIsActive()
        {
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Agility, state.Player.Id, amount: 2);

            var text = DescribeInState(state, Move(range: 3));

            Assert.That(text, Does.Contain("5"), $"민첩 +2면 실효 5칸이어야 한다: {text}");
            Assert.That(text, Does.Not.Contain(">3<"), "저작값 3이 그대로 찍히면 안 된다.");
        }

        [Test]
        public void MoveCardText_ShowsTheEffectiveRange_WhenSlowIsActive()
        {
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Slow, state.Player.Id, amount: 1);

            Assert.That(DescribeInState(state, Move(range: 3)), Does.Contain("2"), "둔화 −1이면 실효 2칸이다.");
        }

        [Test]
        public void MoveCardText_KeepsTheAuthoredRange_WhenNothingModifiesIt()
        {
            var state = CombatState.CreateDefaultDemo();
            var text = DescribeInState(state, Move(range: 3));

            Assert.That(text, Does.Contain("3"));
            Assert.That(text, Does.Not.Contain("<color="),
                "보정이 없으면 강조 색이 붙지 않는다 — 색은 '저작값과 달라졌다'는 신호다.");
        }

        [Test]
        public void AttackCardRange_HasNoModifierAxisAndStaysAuthored()
        {
            // 감사 결과: 공격 사거리에는 보정 축이 없다. 이동과 같은 취급을 하면 없는 규칙을 표시가
            // 발명하게 되므로 저작값 그대로가 맞다.
            var state = CombatState.CreateDefaultDemo();
            Inject(state, StatusEffectKind.Agility, state.Player.Id, amount: 2);

            Assert.That(DescribeInState(state, Attack(damage: 6)), Does.Not.Contain("<color="));
        }

        private static string DescribeInState(CombatState state, CardDefinition card)
        {
            var method = typeof(CombatState).GetMethod("Describe", BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(CardDefinition), typeof(int?), typeof(int?), typeof(int?), typeof(int?) },
                null);
            var rangeSeam = typeof(CombatState).GetMethod("GetDisplayRange", BindingFlags.Instance | BindingFlags.NonPublic);
            var areaSeam = typeof(CombatState).GetMethod("GetDisplayAreaRadius", BindingFlags.Instance | BindingFlags.NonPublic);
            return (string)method.Invoke(null, new object[]
            {
                card,
                null,
                state.GetDisplayValue(card),
                (int?)rangeSeam.Invoke(state, new object[] { card }),
                (int?)areaSeam.Invoke(state, new object[] { card })
            });
        }

        private static CardDefinition Move(int range)
        {
            return new CardDefinition(
                "TEST_MOVE",
                "테스트 이동",
                CardCategory.Movement,
                CardEffectType.Move,
                cost: 1,
                range: range,
                amount: 0,
                description: "최대 {Range}칸 이동합니다.");
        }

        private static CardDefinition Attack(int damage)
        {
            return new CardDefinition(
                "TEST_ATTACK",
                "테스트 공격",
                CardCategory.Action,
                CardEffectType.Attack,
                cost: 1,
                range: 3,
                amount: damage);
        }

        private static CardDefinition Defend(int shield)
        {
            return new CardDefinition(
                "TEST_DEFEND",
                "테스트 방어",
                CardCategory.Action,
                CardEffectType.Defend,
                cost: 1,
                range: 0,
                amount: shield);
        }

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId, int amount)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns: 2, amount, "test"));
        }

        // markedMonster는 MonsterRuntime을 들지만 공개 표면(state.Monsters)은 MonsterRuntimeState를 준다.
        // 런타임 인스턴스는 private 컬렉션에만 있으므로 id로 찾아 넣는다.
        private static void SetMarkedMonster(CombatState state, string monsterId)
        {
            var monsters = (System.Collections.IEnumerable)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);

            object target = null;
            foreach (var monster in monsters)
            {
                var id = (string)monster.GetType().GetProperty("Id").GetValue(monster);
                if (string.Equals(id, monsterId, System.StringComparison.Ordinal))
                {
                    target = monster;
                    break;
                }
            }

            Assert.That(target, Is.Not.Null, "표식을 걸 몬스터를 런타임 컬렉션에서 찾지 못했다.");
            typeof(CombatState)
                .GetField("markedMonster", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(state, target);
        }
    }
}
