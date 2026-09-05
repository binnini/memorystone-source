using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatSelectionStateTests
    {
        [Test]
        public void NoneProjectsEmptyCompatibilityValues()
        {
            var selection = CombatSelectionState.None;

            Assert.That(selection.Mode, Is.EqualTo(CombatSelectionMode.None));
            Assert.That(selection.IsMove, Is.False);
            Assert.That(selection.HasTargetCard, Is.False);
            Assert.That(selection.TargetCardKind, Is.Null);
            Assert.That(selection.TargetCardId, Is.Empty);
            Assert.That(selection.CardId, Is.Empty);
            Assert.That(selection.CardName, Is.Empty);
        }

        [Test]
        public void MoveProjectsMoveSelectionWithoutTargetCard()
        {
            var selection = CombatSelectionState.Move("m2-step", "Step");

            Assert.That(selection.Mode, Is.EqualTo(CombatSelectionMode.Move));
            Assert.That(selection.IsMove, Is.True);
            Assert.That(selection.HasTargetCard, Is.False);
            Assert.That(selection.TargetCardKind, Is.Null);
            Assert.That(selection.TargetCardId, Is.Empty);
            Assert.That(selection.CardId, Is.EqualTo("m2-step"));
            Assert.That(selection.CardName, Is.EqualTo("Step"));
        }

        [Test]
        public void TargetProjectsKindAndCardMetadata()
        {
            var selection = CombatSelectionState.Target(CombatCardKind.Attack, "m2-heavy-strike", "Heavy Strike");

            Assert.That(selection.Mode, Is.EqualTo(CombatSelectionMode.Attack));
            Assert.That(selection.IsMove, Is.False);
            Assert.That(selection.HasTargetCard, Is.True);
            Assert.That(selection.TargetCardKind, Is.EqualTo(CombatCardKind.Attack));
            Assert.That(selection.TargetCardId, Is.EqualTo("m2-heavy-strike"));
            Assert.That(selection.CardId, Is.EqualTo("m2-heavy-strike"));
            Assert.That(selection.CardName, Is.EqualTo("Heavy Strike"));
        }

        [Test]
        public void InvestigateProjectsTargetKindAndMode()
        {
            var selection = CombatSelectionState.Target(CombatCardKind.Investigate, "m2-investigate", "Investigate");

            Assert.That(selection.Mode, Is.EqualTo(CombatSelectionMode.Investigate));
            Assert.That(selection.HasTargetCard, Is.True);
            Assert.That(selection.TargetCardKind, Is.EqualTo(CombatCardKind.Investigate));
            Assert.That(selection.TargetCardId, Is.EqualTo("m2-investigate"));
        }

        [Test]
        public void EmptyNamesUseModeFallbacksAndNullIdsBecomeEmpty()
        {
            var move = CombatSelectionState.Move(null, string.Empty);
            var scout = CombatSelectionState.Target(CombatCardKind.Scout, null, string.Empty);
            var investigate = CombatSelectionState.Target(CombatCardKind.Investigate, null, string.Empty);

            Assert.That(move.CardId, Is.Empty);
            Assert.That(move.CardName, Is.EqualTo("Move"));
            Assert.That(scout.TargetCardId, Is.Empty);
            Assert.That(scout.CardName, Is.EqualTo("Scout"));
            Assert.That(investigate.TargetCardId, Is.Empty);
            Assert.That(investigate.CardName, Is.EqualTo("Investigate"));
        }
    }
}

