using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A05 최후의 일격 — 남은 기력을 모두 소모해 선택한 적에게 피해 {Damage}를 줍니다. 소모한 기력만큼 커집니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A05_FinalBlow : BasicAttackCard
    {
        public override string Id => "A05";

        /// <summary>연마(옛 card_upgrades.csv): X코스트 기본 피해 4→6.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 6);
    }
}
