using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S02 보물찾기 — {Shape}를 탐색하고 드러난 보물 1개당 {Heal} 회복합니다.</summary>
    public sealed class S02_Treasurefinder : ScoutCard
    {
        public override string Id => "S02";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.ScoutTreasureCountHeal;

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            var heal = state.CountTreasureObjects(target, revealRadius) * card.Amount;
            if (heal <= 0)
            {
                return;
            }

            var healed = state.Player.Heal(heal);
            if (healed > 0)
            {
                state.RaiseEffect(EffectKind.Heal, state.PlayerCoord, 0, healed, "player", card.Id);
            }
        }
    }
}
