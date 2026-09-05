using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>S04 빙고! — {Shape}를 탐색하고 드러난 적이 3명 이상일 경우 {Heal} 회복합니다.</summary>
    public sealed class S04_Bingo : ScoutCard
    {
        public override string Id => "S04";

        /// <summary>회복이 나오는 최소 적 수 — 옛 behaviorParams=threshold:3. 「3명 이상」이 규칙이라 상수다.</summary>
        public const int Threshold = 3;

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.ScoutEnemyCountHealThreshold;

        public override void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
        {
            // All-or-nothing heal, unlike S02 which scales per revealed object.
            var threshold = Threshold;
            var revealed = state.monsters
                .Count(monster => !monster.Combatant.IsDead && target.DistanceTo(monster.Coord) <= revealRadius);
            if (revealed < threshold || card.Amount <= 0)
            {
                return;
            }

            var healed = state.Player.Heal(card.Amount);
            if (healed > 0)
            {
                state.RaiseEffect(EffectKind.Heal, state.PlayerCoord, 0, healed, "player", card.Id);
            }
        }
    }
}
