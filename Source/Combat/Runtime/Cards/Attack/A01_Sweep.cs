using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A01 휘둘러치기 — 플레이어 주변 {Shape} 내의 적 모두에게 피해 {Damage}를 줍니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A01_Sweep : BasicAttackCard
    {
        public override string Id => "A01";

        /// <summary>연마(옛 card_upgrades.csv): 자기 주변 blast라 형상 유지·피해 3→5.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 5);
    }
}
