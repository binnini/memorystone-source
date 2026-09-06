using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>U03 정화 뽑기 — 모든 상태이상을 정화합니다. 제거된 상태이상 수만큼 행동 부적을 뽑습니다.</summary>
    public sealed class U03_CleanseDraw : UtilityCard
    {
        public override string Id => "U03";

        /// <summary>기절 면제 — 기절 뒤에 잠긴 정화는 기절을 풀 수 없다(2026-07-25, 정화 카드 2장만).</summary>
        public override bool UsableWhileStunned => true;

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "정화" };

        /// <summary>연마 시 제거 수에 더해 뽑는 장수(효과 연마 2차, DEC-2026-09-06-08) — 깨끗한 상태에서도 1장은 뽑는다.</summary>
        public const int UpgradedExtraDraw = 1;

        /// <summary>연마 가능 선언 — 규칙(추가 드로우)이 UpgradeLevel로 갈린다. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        public override bool HasUtilityEffect(CombatState state, CardDefinition card) => true;

        public override bool TryApplyUtility(CombatState state, CardDefinition card)
        {
            // D8: playing this on a clean player is legal and simply draws nothing. The draw count is
            // the cleanse's own return value, so it needs no card data of its own.
            var removed = state.CleanseStatusEffects(CombatState.PlayerUnitId, card.Id);
            state.ActionDeck.Draw(removed + (card.UpgradeLevel >= 1 ? UpgradedExtraDraw : 0));
            return true;
        }
    }
}
