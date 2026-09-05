using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M05 추진력 — 사용 시 이번 턴에는 이동할 수 없습니다. 다음 턴 민첩 2를 얻습니다.</summary>
    public sealed class M05_Momentum : BasicMoveCard
    {
        public override string Id => "M05";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.MoveDeferredMomentum;

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
                CardEffectRefs.MoveDeferredMomentum);
        }
    }
}
