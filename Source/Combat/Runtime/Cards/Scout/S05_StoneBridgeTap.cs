using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S05 돌 다리 두드리기 — 범위 1을 탐색하고 발견된 함정을 해체합니다.</summary>
    public sealed class S05_StoneBridgeTap : ScoutCard
    {
        public override string Id => "S05";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 탐색 범위 1→2 (shape blast-1의 AreaRadius 축). 문안은 리터럴이라 descriptionUpgraded.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(areaRadius: 2);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "탐색", "해체" };

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            // 정찰의 공유 배관(RevealTrapsInArea)이 먼저 돌아 범위 안 함정을 발견 상태로 만들고, 이 후처리가 그것들을 해체한다.
            state.DisarmRevealedTrapsInArea(target, revealRadius, card.Id);
        }
    }
}
