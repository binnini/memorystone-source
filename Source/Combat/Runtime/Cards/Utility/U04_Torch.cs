using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>U04 호롱불 — 등불을 밝혀 시야가 {Amount} 넓어집니다.</summary>
    public sealed class U04_Torch : UtilityCard
    {
        public override string Id => "U04";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 등불 지속 3→4턴 — 반경(Amount)은 duration에서 오므로 둘을 같이 올린다.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(durationTurns: 4, amount: 4);

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "등불" };

        /// <summary>등불 반경 = 지속 턴(C-14: 「지속시간 = 초기 반경」). 임포터가 duration을 Amount로 싣는다.</summary>
        public override bool AmountFollowsDuration => true;

        public override bool HasUtilityEffect(CombatState state, CardDefinition card) => true;

        public override bool TryApplyUtility(CombatState state, CardDefinition card)
        {
            state.GrantTorchLight(card.Amount, card.Id);
            return true;
        }
    }
}
