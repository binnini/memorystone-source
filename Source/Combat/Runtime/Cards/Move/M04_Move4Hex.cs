using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M04 4칸 이동 — 최대 {Range}칸 이동합니다. (옛 behaviorId `move.basic`)</summary>
    public sealed class M04_Move4Hex : BasicMoveCard
    {
        public override string Id => "M04";

        /// <summary>연마(옛 card_upgrades.csv): 4칸 이동 4→5칸(#14).</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(range: 5, amount: 5);
    }
}
