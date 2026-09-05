using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatObjectiveCompletionPolicyTests
    {
        [Test]
        public void ResolveCompletesOnlyRevealedMatchingObjectiveTargetInRange()
        {
            var objective = new HexCoord(1, 0);

            var result = CombatObjectiveCompletionPolicy.Resolve(
                alreadyCompleted: false,
                objectiveTargetCoord: objective,
                playerCoord: new HexCoord(0, 0),
                target: objective,
                targetVisibility: HexCellVisibility.Revealed,
                range: 1);

            Assert.That(result.CompletedNow, Is.True);
            Assert.That(result.Outcome, Is.EqualTo(CombatObjectiveCompletionOutcome.Completed));
        }

        [Test]
        public void ResolveReportsInvalidObjectiveConditionsWithoutCompletion()
        {
            Assert.That(
                CombatObjectiveCompletionPolicy.Resolve(false, new HexCoord(2, 0), new HexCoord(0, 0), new HexCoord(1, 0), HexCellVisibility.Revealed, 1).Outcome,
                Is.EqualTo(CombatObjectiveCompletionOutcome.NonObjectiveTarget));
            Assert.That(
                CombatObjectiveCompletionPolicy.Resolve(false, null, new HexCoord(0, 0), new HexCoord(1, 0), HexCellVisibility.Revealed, 1).Outcome,
                Is.EqualTo(CombatObjectiveCompletionOutcome.NoObjectiveConfigured));
            Assert.That(
                CombatObjectiveCompletionPolicy.Resolve(false, new HexCoord(1, 0), new HexCoord(0, 0), new HexCoord(1, 0), HexCellVisibility.Hinted, 1).Outcome,
                Is.EqualTo(CombatObjectiveCompletionOutcome.TargetNotRevealed));
            Assert.That(
                CombatObjectiveCompletionPolicy.Resolve(false, new HexCoord(2, 0), new HexCoord(0, 0), new HexCoord(2, 0), HexCellVisibility.Revealed, 1).Outcome,
                Is.EqualTo(CombatObjectiveCompletionOutcome.OutOfRange));
        }
    }
}

