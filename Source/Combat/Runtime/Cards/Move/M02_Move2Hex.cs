using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M02 2칸 이동 — 최대 {Range}칸 이동합니다. (옛 behaviorId `move.basic`)</summary>
    public sealed class M02_Move2Hex : BasicMoveCard
    {
        public override string Id => "M02";

        /// <summary>연마(옛 card_upgrades.csv): 2칸 이동 2→3칸(#14).</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(range: 3, amount: 3);
    }
}
