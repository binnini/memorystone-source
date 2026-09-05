using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>U04 호롱불 — 등불을 밝혀 시야가 {Amount} 넓어집니다.</summary>
    public sealed class U04_Torch : UtilityCard
    {
        public override string Id => "U04";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.UtilityTorch;

        public override bool HasUtilityEffect(CombatState state, CardDefinition card) => true;

        public override bool TryApplyUtility(CombatState state, CardDefinition card)
        {
            state.GrantTorchLight(card.Amount, CardEffectRefs.UtilityTorch);
            return true;
        }
    }
}
