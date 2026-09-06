using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D04 무거운 갑옷 — 방어막 {Shield}를 얻습니다. 다음 턴 속박을 부여받습니다.</summary>
    public sealed class D04_HeavyArmor : BasicBlockCard
    {
        public override string Id => "D04";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "속박", "방어막" };

        /// <summary>연마(옛 card_upgrades.csv): 방어막 8→11 (속박 대가 유지).</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 11);

        public override bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            // Free, heavy block, and the same booked 속박 as D06.
            blockGranted = state.AddBlockWithRupture(state.Player, CombatState.PlayerUnitId, card.Amount);
            state.SchedulePlayerDelayedImmobilize(card.DurationTurns);
            return true;
        }
    }
}
