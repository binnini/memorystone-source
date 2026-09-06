using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A00 공격의 기초 — 선택한 적에게 피해 {Damage}를 줍니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A00_BasicStrike : BasicAttackCard
    {
        public override string Id => "A00";

        /// <summary>연마(옛 card_upgrades.csv): 피해 3→5.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 5);
    }
}
