using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S05 돌 다리 두드리기 — 범위 1을 탐색하고 발견된 함정을 해체합니다.</summary>
    public sealed class S05_StoneBridgeTap : ScoutCard
    {
        public override string Id => "S05";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.ScoutTrapDisarm;

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            // 정찰의 공유 배관(RevealTrapsInArea)이 먼저 돌아 범위 안 함정을 발견 상태로 만들고, 이 후처리가 그것들을 해체한다.
            state.DisarmRevealedTrapsInArea(target, revealRadius, card.Id);
        }
    }
}
