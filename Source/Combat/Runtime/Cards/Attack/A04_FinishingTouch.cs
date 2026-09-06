using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A04 몰아치는 공세 — 선택한 적에게 피해 {Damage}를 손의 공격 카드 수({HitCount}번)만큼 반복합니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A04_FinishingTouch : BasicAttackCard
    {
        public override string Id => "A04";

        /// <summary>연마(옛 card_upgrades.csv): 피해 3→5.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 5);
    }
}
