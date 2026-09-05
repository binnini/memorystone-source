using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D03 양날의 방패 — 이번 턴 자신에게 반사를 부여합니다.</summary>
    public sealed class D03_DoubleEdgedShield : BasicBlockCard
    {
        public override string Id => "D03";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.DefendHalfReflect;

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
