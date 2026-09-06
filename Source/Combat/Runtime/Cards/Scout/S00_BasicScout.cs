using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S00 정찰의 기초 — {Shape}를 탐색합니다. (옛 behaviorId `scout.reveal`)</summary>
    public sealed class S00_BasicScout : ScoutCard
    {
        public override string Id => "S00";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "탐색" };

        /// <summary>연마 시 탐색 뒤 뽑는 행동 부적 수(효과 연마 2차, DEC-2026-09-06-08).</summary>
        public const int UpgradedDrawCount = 1;

        /// <summary>연마 가능 선언 — 규칙(탐색 뒤 드로우)이 UpgradeLevel로 갈린다. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            if (card.UpgradeLevel >= 1)
            {
                // 드로우 이음새(미련 X06 등)를 타도록 CombatState의 단일 입구를 쓴다 — U01과 같은 자리.
                state.DrawActionCards(UpgradedDrawCount);
            }
        }
    }
}
