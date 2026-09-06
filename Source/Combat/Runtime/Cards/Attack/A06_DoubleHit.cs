using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A06 두 번 치기 — 선택한 적에게 피해 {Damage}를 {HitCount}번 반복합니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A06_DoubleHit : BasicAttackCard
    {
        public override string Id => "A06";

        /// <summary>연마(옛 card_upgrades.csv): 2타 반복이라 피해 2→3.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 3);
    }
}
