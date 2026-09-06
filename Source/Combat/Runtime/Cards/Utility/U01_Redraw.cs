using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>
    /// U01 다시 뽑기 — 모든 손패를 버리고 같은 수만큼 다시 뽑습니다. (옛 behaviorId `utility.redraw`)
    /// 연마(D-6, DEC-2026-09-06-05): 효과가 바뀌는 연마의 첫 사례 — 같은 수 <b>+1</b>장을 뽑는다. 수치 축이 없으므로
    /// <see cref="Upgrade"/>는 「연마 가능」만 선언하고, 규칙은 <see cref="TryApplyUtility"/>가 <c>UpgradeLevel</c>로 분기한다.
    /// </summary>
    public sealed class U01_Redraw : UtilityCard
    {
        public override string Id => "U01";

        /// <summary>연마 시 더 뽑는 행동 부적 수.</summary>
        public const int UpgradedExtraDraw = 1;

        /// <summary>연마 가능 선언 — 바뀌는 수치는 없고 규칙이 UpgradeLevel로 갈린다. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        public override bool HasUtilityEffect(CombatState state, CardDefinition card) => true;

        /// <summary>손패 전체 버림이 이 카드의 배출이다. 재드로우로 자기 자신이 다시 손에 들어오면 새 손패로 남아야 하므로 단일 배출 지점은 손대지 않는다.</summary>
        public override CardDisposal DisposeAfterPlay(CombatState state, CardDefinition card) => CardDisposal.HandledByRule;

        public override bool TryApplyUtility(CombatState state, CardDefinition card)
        {
            // 이동·행동 손패를 전부 버리고 같은 수만큼 다시 뽑는다. 이 카드 자신도 손패에 있으므로 함께 버려진다(DisposeAfterPlay = HandledByRule).
            var movementRedrawCount = state.MovementDeck.HandCount;
            var actionRedrawCount = state.ActionDeck.HandCount;
            state.MovementDeck.DiscardHand();
            state.ActionDeck.DiscardHand();
            state.MovementDeck.Draw(movementRedrawCount);
            state.DrawActionCards(actionRedrawCount + (card.UpgradeLevel >= 1 ? UpgradedExtraDraw : 0));
            return true;
        }
    }
}
