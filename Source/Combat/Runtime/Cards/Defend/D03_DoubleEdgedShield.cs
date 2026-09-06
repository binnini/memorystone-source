using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D03 양날의 방패 — 이번 턴 자신에게 반사를 부여합니다.</summary>
    public sealed class D03_DoubleEdgedShield : BasicBlockCard
    {
        public override string Id => "D03";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "반사" };

        /// <summary>연마(옛 card_upgrades.csv): 반사 50%→75%.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(buffDebuff: "Reflect:75");

        public override bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            // Grant the player a Reflect (반사) status for the upcoming monster action. buff_debuff=Reflect:n is
            // the authored value; it used to be ignored in favour of a hardcoded importer fallback.
            state.ApplyReflectToPlayer(
                CardBehaviorMetadata.GetEffectAmount(card.BuffDebuff, nameof(StatusEffectKind.Reflect), 0),
                card.DurationTurns,
                card.Id);
            blockGranted = 0;
            return true;
        }
    }
}
