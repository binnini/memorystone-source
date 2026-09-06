using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M06 도착지를 모르는 여행 — {Shape} 내의 무작위 칸으로 이동합니다.</summary>
    public sealed class M06_RandomJourney : BasicMoveCard
    {
        public override string Id => "M06";

        /// <summary>연마 가능 선언(효과 연마 2차, DEC-2026-09-06-08): 무작위 후보에서 적 인접 칸을 뺀다. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        public override bool TryResolveMoveDestination(CombatState state, CardDefinition card, int effectiveRange, HexCoord requested, out HexCoord destination, out string failureReason)
        {
            // The random-journey radius is declared by the card's area (shape blast-N → AreaRadius),
            // not by Range — Range stays 0 because there is no tile-targeting step. Fall back to the
            // effective move range only if no area is authored.
            var radius = System.Math.Max(0, effectiveRange);
            var randomDestination = state.ChooseRandomMovementDestination(radius, avoidEnemyAdjacent: card.UpgradeLevel >= 1);
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
