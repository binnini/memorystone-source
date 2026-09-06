using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerStateSnapshotTests
    {
        [Test]
        public void SnapshotProjectsInitialCombatStateWithoutMutatingGameplay()
        {
            var state = CombatState.CreateDefaultDemo();

            var snapshot = state.CreatePlayerStateSnapshot();

            Assert.That(snapshot.Hp, Is.EqualTo(state.Player.Hp));
            Assert.That(snapshot.MaxHp, Is.EqualTo(state.Player.MaxHp));
            Assert.That(snapshot.Block, Is.EqualTo(state.Player.Block));
            Assert.That(snapshot.IsDead, Is.EqualTo(state.Player.IsDead));
            Assert.That(snapshot.Position, Is.EqualTo(state.PlayerCoord));
            Assert.That(snapshot.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(snapshot.CurrentKi, Is.EqualTo(state.ActionCostRemaining));
            Assert.That(snapshot.MaxKi, Is.EqualTo(state.Config.ActionBudget));
            Assert.That(snapshot.MoveDeck.HandCount, Is.EqualTo(state.MovementDeck.HandCount));
            Assert.That(snapshot.ActionDeck.HandCount, Is.EqualTo(state.ActionDeck.HandCount));
            Assert.That(snapshot.ObjectiveCompleted, Is.EqualTo(state.ObjectiveCompleted));
            Assert.That(snapshot.ObjectiveStatusText, Is.EqualTo(state.ObjectiveStatusText));
            Assert.That(snapshot.LastFailureReason, Is.Empty);
            Assert.That(snapshot.LastInvestigateResult, Is.Empty);
            Assert.That(snapshot.LastDiscardedCard, Is.Null);
            Assert.That(snapshot.Visibility.TotalCount, Is.EqualTo(state.Map.AllCells.Count()));
            Assert.That(snapshot.Visibility.RevealedCount, Is.EqualTo(state.VisibilityStates.Count(pair => pair.Value == HexCellVisibility.Revealed)));
        }

        [Test]
        public void SnapshotProjectsPostMoveFeedbackDeckAndKiAliases()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            // 이동 페이즈에서의 공격 시도는 실패해 LastFailureReason 피드백을 남긴다(몬스터가
            // 추격 이동한 뒤에는 광역 공격이 성공해 버리므로 실패 시도는 전환 전에 수행).
            Assert.That(state.TryPlayerAttack(new HexCoord(2, 0), CardIds.Sweep), Is.False);
            // DEC-2026-07-03-02: 액션 페이즈 진입은 EndAction → 몬스터 이동 해석을 거친다.
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            var snapshot = state.CreatePlayerStateSnapshot();

            Assert.That(snapshot.Position, Is.EqualTo(new HexCoord(0, 0)));
            Assert.That(snapshot.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(snapshot.CurrentKi, Is.EqualTo(state.ActionCostRemaining));
            Assert.That(snapshot.MaxKi, Is.EqualTo(state.Config.ActionBudget));
            Assert.That(snapshot.MoveDeck.DiscardCount, Is.EqualTo(1));
            Assert.That(snapshot.LastDiscardedCard, Is.EqualTo(CombatCardKind.Move));
            Assert.That(snapshot.LastFailureReason, Is.EqualTo(state.LastFailureReason));
            Assert.That(snapshot.Visibility.TotalCount, Is.EqualTo(state.Map.AllCells.Count()));
        }
    }
}

