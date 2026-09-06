using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M03 3칸 이동 — 최대 {Range}칸 이동합니다. (옛 behaviorId `move.basic`)</summary>
    public sealed class M03_Move3Hex : BasicMoveCard
    {
        public override string Id => "M03";

        /// <summary>연마(옛 card_upgrades.csv): 3칸 이동 3→4칸(#14).</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(range: 4, amount: 4);
    }
}
