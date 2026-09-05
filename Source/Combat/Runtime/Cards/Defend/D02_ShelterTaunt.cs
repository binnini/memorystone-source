using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D02 보호구역 안에서 도발 — 이번 턴 무적이 됩니다. 다음 턴 모든 적이 강화됩니다.</summary>
    public sealed class D02_ShelterTaunt : BasicBlockCard
    {
        public override string Id => "D02";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.DefendZeroThenDouble;

        public override bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            // Nullify incoming damage this monster action, and provoke every monster: they gain
            // 강화 (Strength) from the start of the NEXT turn (booked like the delayed self-속박) —
            // applying immediately made this turn's monster action already hit harder, contradicting
            // the card text (WS-I I-14, DEC-2026-08-19-08). Magnitude and duration stay authored
            // (buff_debuff=Strength:n, duration=n).
            state.pending.NullifyIncomingDamageThisMonsterAction();
            state.ApplyInvincibleStatusThisTurn(CardEffectRefs.DefendZeroThenDouble, card.Id);
            state.ScheduleProvokeStrength(
                CardBehaviorMetadata.GetEffectAmount(card.BuffDebuff, nameof(StatusEffectKind.Strength), 0),
                card.DurationTurns);
            blockGranted = 0;
            return true;
        }
    }
}
