using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerStateProjectionBridgeTests
    {
        [Test]
        public void ProjectionReflectsInitialCombatStateResourcesAndFeedback()
        {
            var state = CombatState.CreateDefaultDemo();

            var projection = state.CreatePlayerStateProjection();

            AssertProjectionMatchesCombatState(projection, state);
            Assert.That(projection.Resources.CurrentKi, Is.EqualTo(state.Config.MaxKi));
            Assert.That(projection.Feedback.LastFailureReason, Is.Empty);
            Assert.That(projection.Feedback.LastDiscardedCard, Is.Null);
            Assert.That(projection.Feedback.LastInvestigateResult, Is.Empty);
        }

        [Test]
        public void ProjectionRefreshesAfterSuccessfulMove()
        {
            var state = CreateLineState();
            var startingKi = state.CurrentKi;

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            var projection = state.CreatePlayerStateProjection();

            AssertProjectionMatchesCombatState(projection, state);
            Assert.That(projection.Resources.CurrentKi, Is.EqualTo(startingKi - 1));
            Assert.That(projection.Feedback.LastDiscardedCard, Is.EqualTo(CombatCardKind.Move));
        }

        [Test]
        public void ProjectionRefreshesAfterSuccessfulAttack()
        {
            var state = CreateLineState();
            EnterActionPhase(state);
            var startingKi = state.CurrentKi;

            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0)), Is.True);
            var projection = state.CreatePlayerStateProjection();

            AssertProjectionMatchesCombatState(projection, state);
            Assert.That(projection.Resources.CurrentKi, Is.EqualTo(startingKi - 1));
            Assert.That(projection.Feedback.LastDiscardedCard, Is.EqualTo(CombatCardKind.Attack));
            Assert.That(projection.Feedback.LastFailureReason, Is.Empty);
            Assert.That(projection.Feedback.LastInvestigateResult, Is.Empty);
        }

        [Test]
        public void ProjectionRefreshesAfterSuccessfulDefend()
        {
            var state = CreateLineState();
            EnterActionPhase(state);
            var startingKi = state.CurrentKi;

            Assert.That(state.TryPlayerDefend(), Is.True);
            var projection = state.CreatePlayerStateProjection();

            AssertProjectionMatchesCombatState(projection, state);
            Assert.That(projection.Resources.CurrentKi, Is.EqualTo(startingKi - 1));
            Assert.That(projection.Feedback.LastDiscardedCard, Is.EqualTo(CombatCardKind.Defend));
            Assert.That(projection.Feedback.LastFailureReason, Is.Empty);
        }

        [Test]
        public void ProjectionRefreshesAfterSuccessfulScout()
        {
            var state = CreateLineState();
            EnterActionPhase(state);
            var startingKi = state.CurrentKi;

            Assert.That(state.TryPlayerScout(new HexCoord(0, 0)), Is.True);
            var projection = state.CreatePlayerStateProjection();

            AssertProjectionMatchesCombatState(projection, state);
            Assert.That(projection.Resources.CurrentKi, Is.EqualTo(startingKi - 1));
            Assert.That(projection.Feedback.LastDiscardedCard, Is.EqualTo(CombatCardKind.Scout));
            Assert.That(projection.Feedback.LastFailureReason, Is.Empty);
        }

        [Test]
        public void ProjectionRefreshesAfterSuccessfulInvestigate()
        {
            var state = new CombatState(
                CombatObjectiveMapBuilder.CreateObjectiveMap(new HexCoord(1, 0)),
                new HexCoord(0, 0),
                new HexCoord(4, 0),
                CombatConfig.Default);
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement(); // DEC-2026-07-03-02: 몬스터 이동 해석 후 PlayerAction 도달.
            var startingKi = state.CurrentKi;

            Assert.That(state.TryPlayerInvestigate(new HexCoord(1, 0)), Is.True);
            var projection = state.CreatePlayerStateProjection();

            AssertProjectionMatchesCombatState(projection, state);
            Assert.That(projection.Resources.CurrentKi, Is.EqualTo(startingKi - 1));
            Assert.That(projection.Feedback.LastDiscardedCard, Is.EqualTo(CombatCardKind.Investigate));
            Assert.That(projection.Feedback.LastFailureReason, Is.Empty);
            Assert.That(projection.Feedback.LastInvestigateResult, Does.Contain("Objective complete"));
        }

        [Test]
        public void ProjectionRefreshesAfterFailedActionWithoutSpendingKi()
        {
            var state = CreateLineState();
            EnterActionPhase(state);
            var startingKi = state.CurrentKi;

            Assert.That(state.TryPlayerAttack(new HexCoord(0, 1)), Is.False);
            var projection = state.CreatePlayerStateProjection();

            AssertProjectionMatchesCombatState(projection, state);
            Assert.That(projection.Resources.CurrentKi, Is.EqualTo(startingKi));
            Assert.That(projection.Feedback.LastFailureReason, Does.Contain("living monster"));
        }

        [Test]
        public void ProjectionRefreshesAfterEndActionAndNextTurnRefill()
        {
            var state = CreateLineState();
            EnterActionPhase(state);
            Assert.That(state.TryPlayerDefend(), Is.True);
            Assert.That(state.EndAction(), Is.True);

            var endedProjection = state.CreatePlayerStateProjection();
            AssertProjectionMatchesCombatState(endedProjection, state);
            Assert.That(endedProjection.Resources.CurrentKi, Is.EqualTo(0));

            state.ResolveMonsterAction();
            var refilledProjection = state.CreatePlayerStateProjection();
            AssertProjectionMatchesCombatState(refilledProjection, state);
            Assert.That(refilledProjection.Resources.CurrentKi, Is.EqualTo(state.Config.MaxKi));
        }

        [Test]
        public void MutatingReturnedProjectionDoesNotChangeCombatStateOrFutureProjection()
        {
            var state = CreateLineState();
            var projection = state.CreatePlayerStateProjection();

            Assert.That(projection.Resources.TrySpendKi(1), Is.True);
            projection.Feedback.Set("projection-only failure", CombatCardKind.Scout, "projection-only investigate");

            var freshProjection = state.CreatePlayerStateProjection();
            AssertProjectionMatchesCombatState(freshProjection, state);
            Assert.That(state.CurrentKi, Is.EqualTo(state.Config.MaxKi));
            Assert.That(state.LastFailureReason, Is.Empty);
            Assert.That(freshProjection.Feedback.LastFailureReason, Is.Empty);
        }

        private static CombatState CreateLineState()
        {
            return new CombatState(CombatState.CreateDemoMap(2), new HexCoord(0, 0), new HexCoord(1, 0), CombatConfig.Default);
        }

        private static void EnterActionPhase(CombatState state)
        {
            // DEC-2026-07-03-02: 확정된 턴 계약 — 이동만으로는 페이즈가 유지되고,
            // EndAction()이 MonsterMovement로 전환하며 몬스터 이동 해석 후 PlayerAction에 도달한다.
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterMovement));
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        private static void AssertProjectionMatchesCombatState(PlayerState projection, CombatState state)
        {
            Assert.That(projection.Resources.CurrentKi, Is.EqualTo(state.CurrentKi));
            Assert.That(projection.Resources.MaxKi, Is.EqualTo(state.MaxKi));
            Assert.That(projection.Position.Coord, Is.EqualTo(state.PlayerCoord));
            Assert.That(projection.Position.Phase, Is.EqualTo(state.Phase));
            Assert.That(projection.Feedback.LastFailureReason, Is.EqualTo(state.LastFailureReason));
            Assert.That(projection.Feedback.LastDiscardedCard, Is.EqualTo(state.LastDiscardedCard));
            Assert.That(projection.Feedback.LastInvestigateResult, Is.EqualTo(state.LastInvestigateResult));
        }
    }
}

