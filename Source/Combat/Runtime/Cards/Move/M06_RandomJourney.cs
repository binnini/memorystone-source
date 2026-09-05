using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M06 도착지를 모르는 여행 — {Shape} 내의 무작위 칸으로 이동합니다.</summary>
    public sealed class M06_RandomJourney : BasicMoveCard
    {
        public override string Id => "M06";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.MoveRandomRadius2;

        public override bool TryResolveMoveDestination(CombatState state, CardDefinition card, int effectiveRange, HexCoord requested, out HexCoord destination, out string failureReason)
        {
            // The random-journey radius is declared by the card's area (shape blast-N → AreaRadius),
            // not by Range — Range stays 0 because there is no tile-targeting step. Fall back to the
            // effective move range only if no area is authored.
            var radius = System.Math.Max(0, effectiveRange);
            var randomDestination = state.ChooseRandomMovementDestination(radius);
            if (!randomDestination.HasValue)
            {
                destination = requested;
                failureReason = "No random movement destination is available.";
                return false;
            }

            destination = randomDestination.Value;
            failureReason = string.Empty;
            return true;
        }
    }
}
