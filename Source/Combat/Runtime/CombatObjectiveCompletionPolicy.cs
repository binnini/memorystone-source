using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    internal enum CombatObjectiveCompletionOutcome
    {
        AlreadyComplete,
        NoObjectiveConfigured,
        NonObjectiveTarget,
        TargetNotRevealed,
        OutOfRange,
        Completed
    }

    internal readonly struct CombatObjectiveCompletionResult
    {
        public CombatObjectiveCompletionResult(CombatObjectiveCompletionOutcome outcome)
        {
            Outcome = outcome;
        }

        public CombatObjectiveCompletionOutcome Outcome { get; }
        public bool CompletedNow => Outcome == CombatObjectiveCompletionOutcome.Completed;
    }

    internal static class CombatObjectiveCompletionPolicy
    {
        public static CombatObjectiveCompletionResult Resolve(
            bool alreadyCompleted,
            HexCoord? objectiveTargetCoord,
            HexCoord playerCoord,
            HexCoord target,
            HexCellVisibility targetVisibility,
            int range)
        {
            if (alreadyCompleted)
            {
                return new CombatObjectiveCompletionResult(CombatObjectiveCompletionOutcome.AlreadyComplete);
            }

            if (!objectiveTargetCoord.HasValue)
            {
                return new CombatObjectiveCompletionResult(CombatObjectiveCompletionOutcome.NoObjectiveConfigured);
            }

            if (target != objectiveTargetCoord.Value)
            {
                return new CombatObjectiveCompletionResult(CombatObjectiveCompletionOutcome.NonObjectiveTarget);
            }

            if (targetVisibility != HexCellVisibility.Revealed)
            {
                return new CombatObjectiveCompletionResult(CombatObjectiveCompletionOutcome.TargetNotRevealed);
            }

            if (playerCoord.DistanceTo(target) > range)
            {
                return new CombatObjectiveCompletionResult(CombatObjectiveCompletionOutcome.OutOfRange);
            }

            return new CombatObjectiveCompletionResult(CombatObjectiveCompletionOutcome.Completed);
        }
    }
}
