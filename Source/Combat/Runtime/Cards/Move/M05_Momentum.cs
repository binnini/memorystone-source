using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M05 추진력 — 사용 시 이번 턴에는 이동할 수 없습니다. 다음 턴 민첩 2를 얻습니다.</summary>
    public sealed class M05_Momentum : BasicMoveCard
    {
        public override string Id => "M05";

        /// <summary>연마(DEC-2026-09-06-08, 효과 연마 2차): 민첩 2→3 (buff_debuff 축). 문안은 토큰이 없어 descriptionUpgraded.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(buffDebuff: "Agility:3");

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "민첩" };

        public override void ApplyAfterMoveResolved(CombatState state, CardDefinition card)
        {
            // 민첩 크기·지속 모두 카드 데이터에서 온다 (buff_debuff=Agility:n, duration=n).
            var agility = CardBehaviorMetadata.GetEffectAmount(card.BuffDebuff, nameof(StatusEffectKind.Agility), 0);
            state.pending.BookAgility(agility, card.DurationTurns);
            state.RaiseStatusEffect(
                StatusEffectKind.Agility,
                state.PlayerCoord,
                0,
                agility,
                CombatState.PlayerUnitId,
                card.Id);
        }
    }
}
