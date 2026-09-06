using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A07 일격이면 충분 — 선택한 적에게 피해 {Damage}를 줍니다. (옛 behaviorId `attack.one_strike_enough`)</summary>
    public sealed class A07_OneStrikeEnough : BasicAttackCard
    {
        public override string Id => "A07";

        /// <summary>연마(옛 card_upgrades.csv): 단발 대미지 컨셉 유지 8→12.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 12);
    }
}
