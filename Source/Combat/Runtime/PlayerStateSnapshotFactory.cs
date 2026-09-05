using System;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public static class PlayerStateSnapshotFactory
    {
        public static PlayerStateSnapshot CreatePlayerStateSnapshot(this CombatState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            return new PlayerStateSnapshot(
                state.Player.Hp,
                state.Player.MaxHp,
                state.Player.Block,
                state.Player.IsDead,
                state.PlayerCoord,
                state.Phase,
                state.ActionCostRemaining,
                state.Config.ActionBudget,
                CreateDeckSummary(state.MovementDeck),
                CreateDeckSummary(state.ActionDeck),
                state.ObjectiveCompleted,
                state.ObjectiveStatusText,
                state.LastDiscardedCard,
                state.LastFailureReason,
                state.LastInvestigateResult,
                CreateVisibilitySummary(state));
        }

        public static PlayerState CreatePlayerStateProjection(this CombatState state)
        {
            var projection = PlayerState.FromSnapshot(state.CreatePlayerStateSnapshot());
            return new PlayerState(
                projection.Vitals,
                projection.Resources,
                projection.Position,
                projection.Decks,
                projection.Knowledge,
                projection.Objective,
                state.PlayerInventory?.Clone(),
                projection.Feedback);
        }

        private static PlayerDeckRuntimeSummary CreateDeckSummary(SeoulPlayup.CardCore.CardDeckState deck)
        {
            return deck == null
                ? new PlayerDeckRuntimeSummary(0, 0, 0)
                : new PlayerDeckRuntimeSummary(deck.DrawCount, deck.HandCount, deck.DiscardCount);
        }

        private static PlayerVisibilitySummary CreateVisibilitySummary(CombatState state)
        {
            var visibilityStates = state.VisibilityStates;
            var hintedCount = visibilityStates.Count(pair => pair.Value == HexCellVisibility.Hinted);
            var revealedCount = visibilityStates.Count(pair => pair.Value == HexCellVisibility.Revealed);
            var unknownCount = Math.Max(0, state.Map.AllCells.Count() - hintedCount - revealedCount);
            return new PlayerVisibilitySummary(unknownCount, hintedCount, revealedCount);
        }
    }
}
