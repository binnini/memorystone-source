using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>U03 정화 뽑기 — 모든 상태이상을 정화합니다. 제거된 상태이상 수만큼 행동 부적을 뽑습니다.</summary>
    public sealed class U03_CleanseDraw : UtilityCard
    {
        public override string Id => "U03";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.UtilityCleanseDraw;

        public override bool HasUtilityEffect(CombatState state, CardDefinition card) => true;

        public override bool TryApplyUtility(CombatState state, CardDefinition card)
        {
            // D8: playing this on a clean player is legal and simply draws nothing. The draw count is
            // the cleanse's own return value, so it needs no card data of its own.
            var removed = state.CleanseStatusEffects(CombatState.PlayerUnitId, CardEffectRefs.UtilityCleanseDraw);
            state.ActionDeck.Draw(removed);
            return true;
        }
    }
}
