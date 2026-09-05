using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 힘(Might)의 계약을 고정한다 — 고정치(D-2)·배율보다 앞(D-1)·런 전체 유지(D-3)·
    /// 상태이상 독에 투영하되 ActiveEffect로 복제하지 않는다(D-9).
    /// </summary>
    public sealed class MightTests
    {
        // --- D-1 / D-2: 어디서 더해지는가 ---------------------------------------------------------

        [Test]
        public void Might_AddsFlatDamage()
        {
            var state = CombatState.CreateDefaultDemo();
            state.PlayerInventory.AddMight(2);

            Assert.That(state.GetDisplayValue(Attack(damage: 3)), Is.EqualTo(5),
                "힘은 가하는 피해에 고정치로 더해진다.");
        }

        [Test]
        public void Might_IsAddedBeforeOutgoingPercentMultiplier()
        {
            var state = CombatState.CreateDefaultDemo();
            state.PlayerInventory.AddMight(2);
            Inject(state, StatusEffectKind.Strength, state.Player.Id, amount: 100);

            // D-1: (3 + 2) × 200% = 10. 배율 뒤에 더하면 3 × 200% + 2 = 8이 된다.
            Assert.That(state.GetDisplayValue(Attack(damage: 3)), Is.EqualTo(10),
                "강화는 힘까지 함께 스케일해야 한다 — 힘을 먼저 더하고 %를 곱한다.");
        }

        [Test]
        public void Might_IsAppliedPerHit_NotPerCard()
        {
            var state = CombatState.CreateDefaultDemo();
            state.PlayerInventory.AddMight(3);

            // 표시 수치는 1타 기준이므로 힘이 그 1타에 붙는다(D-10). 집행이 타격마다 이 값을 다시
            // 계산하므로 다타 카드에서는 타격 수만큼 반복해서 붙는다 — 그것이 D-2다.
            Assert.That(state.GetDisplayValue(Attack(damage: 2)), Is.EqualTo(5),
                "1타에 힘이 통째로 붙어야 한다(2 + 3). 카드당 1회 배분이면 다타에서 값이 갈린다.");
        }

        [Test]
        public void Might_DoesNotAffectFieldObjectDamage()
        {
            var state = CombatState.CreateDefaultDemo();
            state.PlayerInventory.AddMight(5);

            var field = new CardDefinition(
                "TEST_FIELD_MIGHT",
                "테스트 장판",
                CardCategory.Action,
                CardEffectType.FieldObject,
                cost: 1,
                range: 2,
                amount: 4);

            Assert.That(state.GetDisplayValue(field), Is.EqualTo(4),
                "필드 오브젝트 피해는 공격 보정 축을 타지 않는다 — 힘도 예외가 아니다.");
        }

        [Test]
        public void Might_DoesNotAffectBlockGain()
        {
            var state = CombatState.CreateDefaultDemo();
            state.PlayerInventory.AddMight(5);

            Assert.That(state.GetDisplayValue(Defend(shield: 4)), Is.EqualTo(4),
                "힘은 공격력 축이다 — 방어도에 새면 안 된다.");
        }

        // --- D-3: 런 전체 유지 = 세이브에 실린다 ----------------------------------------------------

        [Test]
        public void Might_SurvivesSaveRoundTrip()
        {
            var inventory = new PlayerInventoryState();
            inventory.AddMight(4);

            var restored = PlayerInventorySaveData.FromState(inventory).ToState();

            Assert.That(restored.MightStacks, Is.EqualTo(4),
                "힘은 런 전체 유지되므로 저장·복원 양방향에 모두 실려야 한다. "
                + "FromState만 고치고 ToState를 빠뜨리는 것이 이 코드베이스의 전형적 직렬화 누락이다.");
        }

        [Test]
        public void Might_DefaultsToZeroForOlderSavesWithoutTheField()
        {
            // JsonUtility는 없는 필드를 0으로 채운다 — 스키마 버전을 올리지 않아도 구버전 세이브가
            // 힘 0으로 열려야 한다(올리면 IsValid가 마이그레이션 없이 전부 거부한다).
            var legacy = new PlayerInventorySaveData();

            Assert.That(legacy.ToState().MightStacks, Is.EqualTo(0));
        }

        [Test]
        public void Might_IsClonedWithTheInventory()
        {
            var inventory = new PlayerInventoryState();
            inventory.AddMight(3);

            Assert.That(inventory.Clone().MightStacks, Is.EqualTo(3),
                "CombatState가 생성자에서 인벤토리를 Clone하므로 여기서 떨어지면 전투에 힘이 안 들어온다.");
        }

        [Test]
        public void Might_NeverGoesNegative()
        {
            var inventory = new PlayerInventoryState();
            inventory.AddMight(2);

            Assert.That(inventory.AddMight(-5), Is.EqualTo(0));
        }

        // --- D-9: 독에 투영하되 ActiveEffect로 복제하지 않는다 ----------------------------------------

        [Test]
        public void Might_IsProjectedIntoTheStatusDock()
        {
            var state = CombatState.CreateDefaultDemo();
            state.PlayerInventory.AddMight(3);

            var shown = PlayerStatusEffectQuery.GetPlayerStatusEffects(state);

            var might = shown.FirstOrDefault(effect => effect.Kind == StatusEffectKind.Might);
            Assert.That(might, Is.Not.Null, "힘은 별도 UI 칸 없이 상태이상 독에 떠야 한다(D-9).");
            Assert.That(might.Amount, Is.EqualTo(3), "독은 현재 힘 수치를 보여준다.");
        }

        [Test]
        public void Might_IsNotAnActiveEffect()
        {
            var state = CombatState.CreateDefaultDemo();
            state.PlayerInventory.AddMight(3);

            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Might),
                Is.False,
                "힘을 activeEffects에 복제하면 턴 틱이 깎고, 정화가 집고, 피해 합산이 두 번 센다. "
                + "정본은 PlayerInventoryState.MightStacks 하나여야 한다.");
        }

        [Test]
        public void Might_IsAbsentFromTheDockWhenZero()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(
                PlayerStatusEffectQuery.GetPlayerStatusEffects(state)
                    .Any(effect => effect.Kind == StatusEffectKind.Might),
                Is.False,
                "힘이 0이면 빈 아이콘이 자리를 먹으면 안 된다.");
        }

        [Test]
        public void Might_TooltipSaysPermanentInsteadOfZeroTurns()
        {
            var permanent = new ActiveEffect(
                EffectType.Duration, StatusEffectKind.Might, "player", remainingTurns: 0, amount: 3, "run.permanent");

            Assert.That(StatusEffectTooltipContent.RemainingTurnsLine(permanent), Is.EqualTo("지속: 영구"),
                "만료가 없는 효과에 '남은 0턴'을 찍으면 곧 사라진다는 거짓말이 된다.");
        }

        // --- 부여 관문 ----------------------------------------------------------------------------

        [Test]
        public void GrantMight_AccumulatesAndFeedsDamage()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.GrantMight(2), Is.EqualTo(2));
            Assert.That(state.GrantMight(3), Is.EqualTo(5), "힘은 누적된다.");
            Assert.That(state.GetDisplayValue(Attack(damage: 1)), Is.EqualTo(6),
                "부여 관문을 지난 힘이 곧바로 피해에 반영돼야 한다.");
        }

        [Test]
        public void GrantMight_RaisesAPresentationEventCarryingTheRunningTotal()
        {
            var state = CombatState.CreateDefaultDemo();
            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            state.GrantMight(2);
            state.GrantMight(3);

            var raised = events
                .Where(e => e.Kind == EffectKind.StatusEffectApplied && e.StatusKind == StatusEffectKind.Might)
                .ToList();

            Assert.That(raised, Is.Not.Empty, "부여가 연출 이벤트 없이 조용히 지나가면 안 된다.");
            Assert.That(raised.Last().Amount, Is.EqualTo(5),
                "이벤트는 증가분이 아니라 누적 총량을 실어야 독 아이콘의 숫자와 일치한다.");
        }

        // --- 분류 ---------------------------------------------------------------------------------

        [Test]
        public void Might_IsABuffAndNotCleansable()
        {
            Assert.That(StatusEffectInfo.IsBuff(StatusEffectKind.Might), Is.True);
            Assert.That(StatusEffectInfo.IsCleansable(StatusEffectKind.Might), Is.False,
                "정화가 힘을 지우면 런 전체 진행이 카드 한 장에 날아간다.");
        }

        // --- 헬퍼 ---------------------------------------------------------------------------------

        private static CardDefinition Attack(int damage)
        {
            return new CardDefinition(
                "TEST_MIGHT_ATTACK",
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
                "TEST_MIGHT_DEFEND",
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
    }
}
