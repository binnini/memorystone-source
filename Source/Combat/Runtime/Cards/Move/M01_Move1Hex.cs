using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M01 1칸 이동 — 최대 {Range}칸 이동합니다. (옛 behaviorId `move.basic`)</summary>
    public sealed class M01_Move1Hex : BasicMoveCard
    {
        public override string Id => "M01";

        /// <summary>연마(옛 card_upgrades.csv): 1칸 이동 1→2칸(2026-09-02 #14). 이동 카드 연마 축은 range 하나다 — amount는 range 파생.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(range: 2, amount: 2);
    }
}
