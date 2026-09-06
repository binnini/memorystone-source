using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A02 가속 타격 — 선택한 적에게 피해 {Damage}를 줍니다. 이번 턴 이동한 칸 수만큼 커집니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A02_MoveLinkedStrike : BasicAttackCard
    {
        public override string Id => "A02";

        /// <summary>연마(옛 card_upgrades.csv): 피해 3→5.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 5);
    }
}
