using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D02 보호구역 안에서 도발 — 이번 턴 무적이 됩니다. 다음 턴 모든 적이 강화됩니다.</summary>
    public sealed class D02_ShelterTaunt : BasicBlockCard
    {
        public override string Id => "D02";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "무적", "강화" };

        /// <summary>연마 가능 선언(효과 연마 2차, DEC-2026-09-06-08): 「다음 턴 모든 적 강화」를 뺀다. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        public override bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            // Nullify incoming damage this monster action, and provoke every monster: they gain
            // 강화 (Strength) from the start of the NEXT turn (booked like the delayed self-속박) —
            // applying immediately made this turn's monster action already hit harder, contradicting
            // the card text (WS-I I-14, DEC-2026-08-19-08). Magnitude and duration stay authored
            // (buff_debuff=Strength:n, duration=n).
            state.pending.NullifyIncomingDamageThisMonsterAction();
            state.ApplyInvincibleStatusThisTurn(card.Id);
            if (card.UpgradeLevel < 1)
            {
                // 연마(+)는 도발의 대가를 없앤다 — 무적만 남는다.
                state.ScheduleProvokeStrength(
                    CardBehaviorMetadata.GetEffectAmount(card.BuffDebuff, nameof(StatusEffectKind.Strength), 0),
                    card.DurationTurns);
            }
            blockGranted = 0;
            return true;
        }
    }
}
