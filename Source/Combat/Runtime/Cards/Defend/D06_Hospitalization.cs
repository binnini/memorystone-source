using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D06 입원 — 모든 상태이상을 정화하고 방어막 {Shield}를 얻습니다. 다음 턴 속박을 부여받습니다.</summary>
    public sealed class D06_Hospitalization : BasicBlockCard
    {
        public override string Id => "D06";

        /// <summary>기절 면제 — 기절 뒤에 잠긴 정화는 기절을 풀 수 없다(2026-07-25, 정화 카드 2장만).</summary>
        public override bool UsableWhileStunned => true;

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "속박", "방어막", "정화" };

        /// <summary>연마(옛 card_upgrades.csv): 정화+방어막 5→8.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 8);

        public override bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            // Cleanse, then block (shield=n → Amount), then book next turn's 속박 (duration=n).
            state.CleanseStatusEffects(CombatState.PlayerUnitId, card.Id);
            blockGranted = state.AddBlockWithRupture(state.Player, CombatState.PlayerUnitId, card.Amount);
            state.SchedulePlayerDelayedImmobilize(card.DurationTurns);
            return true;
        }
    }
}
