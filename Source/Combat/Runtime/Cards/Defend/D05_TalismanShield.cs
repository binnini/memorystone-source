using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D05 부적 방패 — 손의 임의의 행동 부적 한 장을 소멸시키고 이번 턴 무적이 됩니다.</summary>
    public sealed class D05_TalismanShield : BasicBlockCard
    {
        public override string Id => "D05";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.DefendExileRandomNegate;

        public override bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
        {
            // Same damage nullification as D02, without D02's 강화 backlash — the cost is paid up front by
            // exiling a card instead. The exile is random (not player-picked), so it needs no selection UI:
            // A10's 제물 flow is the player-picked variant of the same idea.
            state.ExileRandomActionHandCard(card);
            state.pending.NullifyIncomingDamageThisMonsterAction();
            state.ApplyInvincibleStatusThisTurn(CardEffectRefs.DefendExileRandomNegate, card.Id);
            blockGranted = 0;
            return true;
        }
    }
}
