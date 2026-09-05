using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Combat.Unity.Presentation;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using static SeoulPlayup.Combat.Unity.CombatCameraController;
using Cinemachine;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public sealed partial class MapCombatController
    {
        public bool BeginMoveSelection()
        {
            return BeginMoveSelection(string.Empty);
        }

        public bool BeginMoveSelection(string cardId)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null)
            {
                LastInputMessage = "Integration demo is not initialized.";
                RefreshView();
                return false;
            }

            if (State.IsTerminal)
            {
                LastInputMessage = "Combat ended. Press Restart.";
                RefreshView();
                return false;
            }

            if (State.Phase != CombatPhase.PlayerMovement)
            {
                LastInputMessage = "Move card is waiting for the next movement phase.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            var moveCard = State.GetCombatCards().FirstOrDefault(card => card.Kind == CombatCardKind.Move && !card.IsDiscarded && card.IsUsable && MatchesCardKey(card, cardId));
            if (moveCard.Kind != CombatCardKind.Move || string.IsNullOrEmpty(moveCard.Id))
            {
                LastInputMessage = "Not enough Ki remains for a Move card.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            ClearPendingChoiceIfDifferent(moveCard.SelectionKey);
            using (PathfindMarker.Auto())
            {
                reachable = movementPathQuery.GetReachablePlayerMoves(State, moveCard.SelectionKey);
            }

            SelectMove(moveCard.Kind == CombatCardKind.Move ? moveCard.Id : string.Empty, moveCard.Kind == CombatCardKind.Move ? moveCard.InstanceId : string.Empty, moveCard.Kind == CombatCardKind.Move ? moveCard.Name : "Move");
            LastTargetInfoText = string.Empty;
            var fastTurtleSuffix = State.RevealedFastTurtleDistance > 0 && moveCard.EffectRef == CardEffectRefs.MoveFastTurtle
                ? $" Revealed distance: {State.RevealedFastTurtleDistance}."
                : string.Empty;
            LastInputMessage = $"TARGETING MOVE: {SelectedCardName} selected.{fastTurtleSuffix} Click one highlighted map cell to move. Highlighted cells are valid Move targets.";
            RequestAudioCue(AudioCueIds.UiCardSelect, $"select:{moveCard.Id}");
            RefreshView();
            return reachable.Count > 0;
        }

        public bool PlayMovementSelf(string cardId)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            var used = State != null && State.TryPlayerMovementSelf(cardId);
            LastInputMessage = used
                ? "Played movement buff. Choose another Move card or press End Action."
                : State?.LastFailureReason ?? "Movement buff is unavailable.";
            if (used)
            {
                RaiseCardCastCue(CombatCardKind.Move);
            }
            else
            {
                RequestInvalidInputAudioCue(LastInputMessage);
            }

            RefreshView();
            return used;
        }

        public bool TryMoveTo(HexCoord destination)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null)
            {
                LastInputMessage = "Integration demo is not initialized.";
                RefreshView();
                return false;
            }

            if (!IsMoveSelectionActive)
            {
                LastInputMessage = "Select the Move card before clicking a map cell.";
                LastTargetInfoText = GetVisibilitySafeTooltipText(destination);
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            if (!CanSelectTargetForTutorial(destination, out var tutorialFailure))
            {
                LastTargetInfoText = GetVisibilitySafeTooltipText(destination);
                ShowTutorialBlockedFeedback(tutorialFailure);
                return false;
            }

            LastTargetInfoText = GetVisibilitySafeTooltipText(destination);
            if (!movementPathQuery.IsReachableDestination(reachable, destination))
            {
                LastInputMessage = "Selected M1 map cell is not reachable by the current Move card.";
                ShowTargetRejectedFloatingText(destination, CombatCardKind.Move, selectionState.CardKey);
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            var before = CapturePresentationSnapshot();
            bool moved;
            using (StateMoveMarker.Auto())
            {
                moved = State.TryPlayerMove(destination, selectionState.CardKey);
            }

            if (moved)
            {
                var after = CapturePresentationSnapshot();
                ClearSelection();
                reachable = new Dictionary<HexCoord, int>();
                LastInputMessage = "Played Move on the M1 map. End movement to resolve monster movement.";
                RaiseCardCastCue(CombatCardKind.Move);
                NotifyTutorialTargetSelected(destination);
                if (ShouldPlayPresentationSequence())
                {
                    StartPresentationSequence(
                        "Resolving move...",
                        RunMoveTimelineThenResolveTileInteraction(before, after, LastInputMessage));
                    return true;
                }

                ResolveTileInteractionAtPlayerCoord();
            }
            else
            {
                LastInputMessage = State.LastFailureReason;
                ShowTargetRejectedFloatingText(destination, CombatCardKind.Move, selectionState.CardKey);
                RequestInvalidInputAudioCue(LastInputMessage);
            }

            RefreshView();
            if (moved)
            {
                RecenterGameplayCameraOnPlayer(immediate: !Application.isPlaying);
            }

            return moved;
        }

        // Resolves a "travel" move card (unknown destination, CardTargetMode.RandomReachable) instantly:
        // the runtime picks a random reachable tile, so no tile-targeting step is shown to the player.
        public bool PlayMovementRandom(string cardId)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null)
            {
                LastInputMessage = "Integration demo is not initialized.";
                RefreshView();
                return false;
            }

            if (State.Phase != CombatPhase.PlayerMovement)
            {
                LastInputMessage = "Move card is waiting for the next movement phase.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            var before = CapturePresentationSnapshot();
            bool moved;
            using (StateMoveMarker.Auto())
            {
                // The random-radius move handler ignores the requested coordinate and chooses a random
                // reachable destination, so the player's current tile is only a placeholder argument.
                moved = State.TryPlayerMove(State.PlayerCoord, cardId);
            }

            if (moved)
            {
                var after = CapturePresentationSnapshot();
                ClearSelection();
                reachable = new Dictionary<HexCoord, int>();
                LastInputMessage = "\uC774\uB3D9 \uD6A8\uACFC\uAC00 \uC801\uC6A9\uB418\uC5C8\uC2B5\uB2C8\uB2E4.";
                RaiseCardCastCue(CombatCardKind.Move);
                if (ShouldPlayPresentationSequence())
                {
                    StartPresentationSequence(
                        "Resolving move...",
                        RunMoveTimelineThenResolveTileInteraction(before, after, LastInputMessage));
                    return true;
                }

                ResolveTileInteractionAtPlayerCoord();
            }
            else
            {
                LastInputMessage = State.LastFailureReason;
                RequestInvalidInputAudioCue(LastInputMessage);
            }

            RefreshView();
            if (moved)
            {
                RecenterGameplayCameraOnPlayer(immediate: !Application.isPlaying);
            }

            return moved;
        }

        public bool TryDebugMoveTo(HexCoord destination)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null)
            {
                LastInputMessage = "Integration demo is not initialized.";
                RefreshView();
                return false;
            }

            LastTargetInfoText = GetVisibilitySafeTooltipText(destination);
            var before = CapturePresentationSnapshot();
            var moved = State.TryDebugMovePlayer(destination);
            if (moved)
            {
                var after = CapturePresentationSnapshot();
                ClearSelection();
                reachable = new Dictionary<HexCoord, int>();
                LastInputMessage = $"Trap debug moved to {destination} without consuming a movement card.";
                RaiseCardCastCue(CombatCardKind.Move);
                if (ShouldPlayPresentationSequence())
                {
                    StartPresentationSequence(
                        "Resolving debug move...",
                        RunMoveTimelineThenResolveTileInteraction(before, after, LastInputMessage));
                    return true;
                }

                ResolveTileInteractionAtPlayerCoord();
            }
            else
            {
                LastInputMessage = State.LastFailureReason;
                RequestInvalidInputAudioCue(LastInputMessage);
            }

            RefreshView();
            if (moved)
            {
                RecenterGameplayCameraOnPlayer(immediate: !Application.isPlaying);
            }

            return moved;
        }

        private bool IsPointerOverDebugPanel(Vector2 screenPosition)
        {
            return (debugControlPanel != null && debugControlPanel.IsScreenPointOverGameViewPanel(screenPosition))
                || CameraLightTestPanel.IsPointerOverAnyVisiblePanel(screenPosition)
                || CombatDebugControlPanel.IsPointerOverAnyGameViewPanel(screenPosition);
        }

        private bool IsPointerOverDebugPanel(Vector3 screenPosition)
        {
            return IsPointerOverDebugPanel(new Vector2(screenPosition.x, screenPosition.y));
        }

        public bool IsPointerOverDebugPanelForTests(Vector2 screenPosition)
        {
            return IsPointerOverDebugPanel(screenPosition);
        }

        public bool TryMoveFromScreenPosition(Vector3 screenPosition)
        {
            if (Application.isPlaying && IsPointerOverDebugPanel(screenPosition))
            {
                LastInputMessage = "Debug panel hover blocked map click input.";
                RefreshHudOnly();
                return false;
            }

            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (!HasSelected3DView() || prototype3DCamera == null)
            {
                LastInputMessage = "Cannot click-move: 3D map view or camera is missing.";
                RefreshView();
                return false;
            }

            if (!TrySelectedScreenToHex(screenPosition, out var coord))
            {
                LastInputMessage = "Click missed the M1 3D map.";
                RefreshView();
                return false;
            }

            if (RejectUnselectableTile(coord))
            {
                return false;
            }

            if (clickMoveDebugModeEnabled)
            {
                return TryDebugMoveTo(coord);
            }

            return SelectedTargetCardKind.HasValue ? TryUseSelectedTargetCard(coord) : TryMoveTo(coord);
        }

        public bool BeginChoiceCardSelection(string cardId)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null)
            {
                LastInputMessage = "Integration demo is not initialized.";
                RefreshView();
                return false;
            }

            var card = State.GetCombatCards().FirstOrDefault(candidate => MatchesCardKey(candidate, cardId) && !candidate.IsDiscarded);
            ClearPendingChoiceIfDifferent(card.SelectionKey);
            if (!MatchesCardKey(card, cardId) || card.PlayMode != CardPlayMode.Choice)
            {
                LastInputMessage = "Choice card is unavailable.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            pendingChoicePanel = ChoiceCardPanelModel.ForCard(card);
            isChoicePanelVisible = pendingChoicePanel.Options.Count > 0;
            LastInputMessage = $"CHOICE CARD: {card.Name} selected. Choose one option.";
            RequestAudioCue(AudioCueIds.UiCardSelect, $"select:{card.Id}");
            RefreshView();
            return pendingChoicePanel.Options.Count > 0;
        }

        public bool CancelChoiceCardSelection()
        {
            if (string.IsNullOrEmpty(pendingChoicePanel.SourceCardId))
            {
                LastInputMessage = "No choice card is selected.";
                RefreshView();
                return false;
            }

            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            LastTargetInfoText = string.Empty;
            LastInputMessage = "Choice card cancelled. Select a card from your hand.";
            RequestAudioCue(AudioCueIds.UiCardSelect, "choice:cancel");
            RefreshView();
            return true;
        }

        public bool BeginChoiceOptionTargetSelection(string optionId)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (string.IsNullOrEmpty(pendingChoicePanel.SourceCardKey))
            {
                LastInputMessage = "No choice card is selected.";
                RefreshView();
                return false;
            }

            var option = pendingChoicePanel.Options.FirstOrDefault(candidate =>
                string.Equals(candidate.OptionId, optionId, System.StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(option.OptionId) || option.TargetMode != CardTargetMode.Enemy)
            {
                LastInputMessage = "Choice option does not require enemy targeting.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            isChoicePanelVisible = false;
            return BeginTargetCardSelection(
                CombatCardKind.Attack,
                pendingChoicePanel.SourceCardKey,
                $"TARGETING CHOICE: {option.DisplayName}. Click an in-range monster to play {pendingChoicePanel.SourceCardId}.");
        }

        public bool PlayChoiceOption(string optionId, HexCoord? target = null)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null || string.IsNullOrEmpty(pendingChoicePanel.SourceCardId))
            {
                LastInputMessage = "No choice card is selected.";
                RefreshView();
                return false;
            }

            var used = State.TryPlayerChoiceOption(pendingChoicePanel.SourceCardKey, optionId, target);
            LastInputMessage = used
                ? $"Played choice option: {optionId}."
                : State.LastFailureReason;
            if (used)
            {
                ClearSelection();
                reachable = new Dictionary<HexCoord, int>();
                LastTargetInfoText = string.Empty;
                RaiseCardCastCue(CombatCardKind.Attack);
            }
            else
            {
                RequestInvalidInputAudioCue(LastInputMessage);
            }

            RefreshView();
            return used;
        }

        public bool UseAttack()
        {
            return BeginAttackSelection();
        }

        public bool BeginAttackSelection()
        {
            var attackCard = State?.GetCombatCards().FirstOrDefault(card => card.Kind == CombatCardKind.Attack && !card.IsDiscarded && card.IsUsable);
            return BeginAttackSelection(attackCard.HasValue && attackCard.Value.Kind == CombatCardKind.Attack ? attackCard.Value.SelectionKey : string.Empty);
        }

        public bool BeginAttackSelection(string cardId)
        {
            if (State != null && State.RequiresSelectedHandCards(cardId))
            {
                return TryGetSelectableCard(CombatCardKind.Attack, cardId, out var attackCard)
                    && RequiresTargetBeforeHandCardSelection(attackCard)
                    ? BeginTargetCardSelection(CombatCardKind.Attack, cardId,
                        $"\uCD94\uAC00 \uBE44\uC6A9\uC73C\uB85C \uBC84\uB9B4 \uCE74\uB4DC\uB97C \uC120\uD0DD\uD55C \uB4A4 {attackCard.Name} \uB300\uC0C1\uC744 \uACE0\uB974\uC138\uC694. {GetHandCardSelectionCostPhrase(cardId)}")
                    : BeginHandCardSelectionForAdditionalCost(cardId);
            }

            return BeginTargetCardSelection(CombatCardKind.Attack, cardId, string.Empty);
        }

        public bool ToggleHandCardSelection(string cardKey)
        {
            if (!isHandCardSelectionActive || isHandCardSelectionAwaitingTarget)
            {
                return false;
            }

            if (!TryGetHandCardSelectionVisualState(cardKey, out var visualState))
            {
                LastInputMessage = "Select a valid hand card.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            if (visualState == HandCardSelectionVisualState.Disabled)
            {
                LastInputMessage = "That card cannot be selected for this effect.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            if (handCardSelectionSelectedKeys.Contains(cardKey))
            {
                handCardSelectionSelectedKeys.Remove(cardKey);
            }
            else
            {
                if (handCardSelectionSelectedKeys.Count >= handCardSelectionMaxCount)
                {
                    LastInputMessage = $"Select up to {handCardSelectionMaxCount} cards.";
                    RequestInvalidInputAudioCue(LastInputMessage);
                    RefreshView();
                    return false;
                }

                handCardSelectionSelectedKeys.Add(cardKey);
            }

            handCardSelectionPromptText = GetHandCardSelectionPromptText(handCardSelectionSourceCardKey);
            LastInputMessage = State != null && State.UsesExileSelectedHandCards(handCardSelectionSourceCardKey)
                ? handCardSelectionPromptText
                : $"{handCardSelectionPromptText} ({handCardSelectionSelectedKeys.Count} selected)";
            RequestAudioCue(AudioCueIds.UiCardSelect, $"hand-select:{cardKey}");
            RefreshView();
            return true;
        }

        public bool ConfirmHandCardSelection()
        {
            if (!isHandCardSelectionActive || isHandCardSelectionAwaitingTarget)
            {
                LastInputMessage = "No hand-card selection is active.";
                RefreshView();
                return false;
            }

            if (!CanPressTutorialButton(TutorialButtonId.HandCardSelectionConfirm, handCardSelectionSelectedKeys.Count, out var tutorialFailure))
            {
                ShowTutorialBlockedFeedback(tutorialFailure);
                return false;
            }

            if (handCardSelectionSelectedKeys.Count < handCardSelectionMinCount)
            {
                LastInputMessage = $"Select at least {handCardSelectionMinCount} card.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            if (hasHandCardSelectionLockedTarget)
            {
                var playedLockedTarget = PlayHandCardSelectionAtLockedTarget();
                if (playedLockedTarget)
                {
                    NotifyTutorialButtonPressed(TutorialButtonId.HandCardSelectionConfirm);
                }

                return playedLockedTarget;
            }

            isHandCardSelectionAwaitingTarget = true;
            var beganTargetSelection = BeginTargetCardSelection(
                CombatCardKind.Attack,
                handCardSelectionSourceCardKey,
                "TARGETING ATTACK: click an in-range monster to play the selected hand-card effect.");
            if (!beganTargetSelection)
            {
                isHandCardSelectionAwaitingTarget = false;
            }

            return beganTargetSelection;
        }

        public bool CancelHandCardSelection()
        {
            if (ResolveTutorialDirector()?.IsCardCancelLocked == true)
            {
                ShowTutorialBlockedFeedback("튜토리얼 진행 중에는 카드 선택을 취소할 수 없습니다.");
                return false;
            }

            if (!isHandCardSelectionActive)
            {
                LastInputMessage = "No hand-card selection is active.";
                RefreshView();
                return false;
            }

            ClearSelection();
            ClearHandCardSelection();
            LastTargetInfoText = string.Empty;
            LastInputMessage = "Card hand selection cancelled. Select a card from your hand.";
            RequestAudioCue(AudioCueIds.UiCardSelect, "hand-select:cancel");
            RefreshView();
            return true;
        }

        public bool CancelSelectedCard()
        {
            if (ResolveTutorialDirector()?.IsCardCancelLocked == true)
            {
                ShowTutorialBlockedFeedback("튜토리얼 진행 중에는 카드 선택을 취소할 수 없습니다.");
                return false;
            }

            if (!HasCancelableCardSelection())
            {
                LastInputMessage = "No selected card to cancel.";
                RefreshHudOnly();
                return false;
            }

            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            LastTargetInfoText = string.Empty;
            LastInputMessage = "Card selection cancelled. Select a card from your hand.";
            RequestAudioCue(AudioCueIds.UiCardSelect, "card:cancel");
            RefreshView();
            return true;
        }

        private bool HasCancelableCardSelection()
        {
            return selectionState.Mode != CombatSelectionMode.None || isChoicePanelVisible || isHandCardSelectionActive;
        }

        public HandCardSelectionVisualState GetHandCardSelectionVisualState(CombatCardSnapshot card)
        {
            if (!isHandCardSelectionActive || isHandCardSelectionAwaitingTarget)
            {
                return HandCardSelectionVisualState.Normal;
            }

            if (handCardSelectionSelectedKeys.Any(key => HandCardSelectionPanelModel.Matches(card, key)))
            {
                return HandCardSelectionVisualState.Picked;
            }

            return IsSelectableHandCardForSelection(card)
                ? HandCardSelectionVisualState.Candidate
                : HandCardSelectionVisualState.Disabled;
        }

        private bool BeginHandCardSelectionForAdditionalCost(string cardId)
        {
            return BeginHandCardSelectionForAdditionalCost(cardId, null);
        }

        private bool BeginHandCardSelectionForAdditionalCost(string cardId, HexCoord? lockedTarget)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            var sourceCard = State?.GetHandCards().FirstOrDefault(card => HandCardSelectionPanelModel.Matches(card, cardId));
            if (!sourceCard.HasValue || !State.RequiresSelectedHandCards(sourceCard.Value.SelectionKey))
            {
                LastInputMessage = "This card does not require hand-card selection.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            var selectableCount = State.GetHandCards().Count(card =>
                !card.IsDiscarded
                && card.Kind != CombatCardKind.Move
                && !HandCardSelectionPanelModel.Matches(card, sourceCard.Value.SelectionKey));
            if (selectableCount <= 0)
            {
                LastInputMessage = "At least one other action card is required.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            ClearSelection();
            ClearPendingChoicePanel();
            handCardSelectionSourceCardKey = sourceCard.Value.SelectionKey;
            handCardSelectionMinCount = 1;
            handCardSelectionMaxCount = selectableCount;
            handCardSelectionPromptText = GetHandCardSelectionPromptText(sourceCard.Value.SelectionKey);
            handCardSelectionSelectedKeys.Clear();
            isHandCardSelectionActive = true;
            isHandCardSelectionAwaitingTarget = false;
            hasHandCardSelectionLockedTarget = lockedTarget.HasValue;
            handCardSelectionLockedTarget = lockedTarget.GetValueOrDefault();
            reachable = new Dictionary<HexCoord, int>();
            LastTargetInfoText = lockedTarget.HasValue
                ? $"Target locked: {lockedTarget.Value.Q},{lockedTarget.Value.R}"
                : string.Empty;
            LastInputMessage = lockedTarget.HasValue
                ? $"{sourceCard.Value.Name}: target locked. {handCardSelectionPromptText}"
                : $"{sourceCard.Value.Name}: {handCardSelectionPromptText}";
            RequestAudioCue(AudioCueIds.UiCardSelect, $"select:{sourceCard.Value.Id}");
            RefreshView();
            return true;
        }

        private bool PlayHandCardSelectionAtLockedTarget()
        {
            var target = handCardSelectionLockedTarget;
            var cardKey = handCardSelectionSourceCardKey;
            var cardName = State?.GetHandCards()
                .FirstOrDefault(card => HandCardSelectionPanelModel.Matches(card, cardKey))
                .Name ?? "Attack";
            LastTargetInfoText = GetVisibilitySafeTooltipText(target);
            var before = CapturePresentationSnapshot();
            var beforeTargetMonster = ResolvePresentationAttackTarget(before, target);
            var bufferAttackEffects = EffectiveAlignImpactToAnimation && ShouldPlayPresentationSequence();
            if (bufferAttackEffects)
            {
                State.BeginEffectBuffering();
            }

            var used = State.TryPlayerSacrificeAttack(target, cardKey, handCardSelectionSelectedKeys.ToArray());
            if (used)
            {
                LastInputMessage = $"Played Attack ({cardName}) on selected monster. Spend remaining cost or press End Action.";
                var sacrificeAttackTimingId = ResolveAttackCardCatalogId(cardKey);
                ClearSelection();
                if (ShouldPlayPresentationSequence())
                {
                    var after = CapturePresentationSnapshot();
                    StartPresentationSequence(
                        "Resolving attack...",
                        RunAttackTimeline(before, after, target, beforeTargetMonster.Id, LastInputMessage, sacrificeAttackTimingId));
                    return true;
                }
            }
            else
            {
                LastInputMessage = State.LastFailureReason;
                RequestInvalidInputAudioCue(LastInputMessage);
            }

            if (bufferAttackEffects && State != null && State.IsBufferingEffects)
            {
                State.FlushBufferedEffects();
            }

            RefreshView();
            if (used)
            {
                TriggerEnemyDeadIfVisibleMonsterDied(beforeTargetMonster);
            }

            return used;
        }

        private static bool RequiresTargetBeforeHandCardSelection(CombatCardSnapshot card)
        {
            return card.TargetMode == CardTargetMode.Enemy
                || card.TargetMode == CardTargetMode.Tile
                || card.TargetMode == CardTargetMode.SelfOrEnemy
                || card.TargetMode == CardTargetMode.OptionThenTarget;
        }

        private string GetHandCardSelectionPromptText(string cardId)
        {
            return State != null && State.UsesExileSelectedHandCards(cardId)
                ? "\uC18C\uBA78\uC2DC\uD0AC \uCE74\uB4DC\uB97C \uC120\uD0DD\uD558\uC138\uC694."
                : "\uBC84\uB9B4 \uCE74\uB4DC\uB97C \uC120\uD0DD\uD558\uC138\uC694. \uC120\uD0DD\uD55C \uCE74\uB4DC\uB294 \uBC84\uB9BC \uB354\uBBF8\uB85C \uC774\uB3D9\uD569\uB2C8\uB2E4.";
        }

        private string GetHandCardSelectionCostPhrase(string cardId)
        {
            return State != null && State.UsesExileSelectedHandCards(cardId)
                ? "\uCD94\uBC29\uD560 \uCE74\uB4DC\uB97C \uC120\uD0DD\uD574\uC57C \uD569\uB2C8\uB2E4."
                : "\uBC84\uB9B4 \uCE74\uB4DC\uB97C \uC120\uD0DD\uD574\uC57C \uD569\uB2C8\uB2E4.";
        }

        public bool UseDefense(string cardId)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            var used = State != null && State.TryPlayerDefend(cardId);
            LastInputMessage = used
                ? "Played Defend. Spend remaining cost or press End Action."
                : State != null && State.IsTerminal
                    ? "Combat ended. Press Restart."
                    : State?.LastFailureReason ?? "Defend is unavailable.";
            if (used)
            {
                RaiseCardCastCue(CombatCardKind.Defend);
                atlasTilePresentationView?.TriggerPlayerShield();
            }
            else
            {
                RequestInvalidInputAudioCue(LastInputMessage);
            }
            RefreshView();
            return used;
        }

        public bool UseUtility(string cardId)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            var used = State != null && State.TryPlayerUtility(cardId);
            LastInputMessage = used
                ? "Played Utility. Spend remaining cost or press End Action."
                : State != null && State.IsTerminal
                    ? "Combat ended. Press Restart."
                    : State?.LastFailureReason ?? "Utility is unavailable.";
            if (used)
            {
                RaiseCardCastCue(CombatCardKind.Utility);
            }
            else
            {
                RequestInvalidInputAudioCue(LastInputMessage);
            }
            RefreshView();
            return used;
        }

        public bool BeginScoutSelection()
        {
            var scoutCard = State?.GetCombatCards().FirstOrDefault(card => card.Kind == CombatCardKind.Scout && !card.IsDiscarded && card.IsUsable);
            return BeginScoutSelection(scoutCard.HasValue && scoutCard.Value.Kind == CombatCardKind.Scout ? scoutCard.Value.SelectionKey : string.Empty);
        }

        public bool BeginScoutSelection(string cardId)
        {
            return BeginTargetCardSelection(CombatCardKind.Scout, cardId, string.Empty);
        }

        public bool BeginInvestigateSelection() => BeginTargetCardSelection(CombatCardKind.Investigate, string.Empty);

        public bool BeginFieldObjectSelection(string cardId)
        {
            return BeginTargetCardSelection(CombatCardKind.FieldObject, cardId, string.Empty);
        }

        // Self-targeted area attack (CardTargetMode.SelfArea): no tile-targeting step ??begin the attack-card
        // selection and immediately resolve it at the player's own tile.
        public bool PlaySelfAreaAttack(string cardId)
        {
            if (!BeginTargetCardSelection(CombatCardKind.Attack, cardId, string.Empty))
            {
                return false;
            }

            return TryUseSelectedTargetCard(State.PlayerCoord);
        }

        public bool PlayTorchAtPlayerForDev(string cardId = "")
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            var resolvedCardId = ResolveTorchCardId(cardId);
            var used = State != null && State.TryPlayerFieldObject(State.PlayerCoord, resolvedCardId);
            LastInputMessage = used
                ? "Played Campfire. Torch light reveals nearby fog for its field-object duration."
                : State != null && State.IsTerminal
                    ? "Combat ended. Press Restart."
                    : State?.LastFailureReason ?? "Campfire is unavailable.";
            if (used)
            {
                RaiseCardCastCue(CombatCardKind.FieldObject, resolvedCardId);
                atlasTilePresentationView?.TriggerPlayerField();
            }
            else
            {
                RequestInvalidInputAudioCue(LastInputMessage);
            }
            RefreshView();
            return used;
        }

        private string ResolveTorchCardId(string requestedCardId)
        {
            if (!string.IsNullOrWhiteSpace(requestedCardId))
            {
                return requestedCardId;
            }

            var torchCard = State == null
                ? default
                : State.GetCombatCards().FirstOrDefault(card => !card.IsDiscarded
                    && card.Kind == CombatCardKind.FieldObject
                    && card.PlayMode == SeoulPlayup.CardCore.CardPlayMode.Self
                    && card.FieldObjectKind == SeoulPlayup.CardCore.CardFieldObjectKind.FogReveal);
            return string.IsNullOrWhiteSpace(torchCard.Id) ? CombatCatalogFactory.CampfireCardId : torchCard.SelectionKey;
        }

        public bool TryUseSelectedTargetCard(HexCoord target)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null)
            {
                LastInputMessage = "Integration demo is not initialized.";
                RefreshView();
                return false;
            }

            if (!SelectedTargetCardKind.HasValue)
            {
                LastInputMessage = "Select Attack, Scout, or Investigate before clicking a map cell.";
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            if (!CanSelectTargetForTutorial(target, out var tutorialFailure))
            {
                LastTargetInfoText = GetVisibilitySafeTooltipText(target);
                ShowTutorialBlockedFeedback(tutorialFailure);
                return false;
            }

            var selected = SelectedTargetCardKind.Value;
            LastTargetInfoText = GetVisibilitySafeTooltipText(target);
            if (selected == CombatCardKind.Attack
                && !isHandCardSelectionActive
                && State.RequiresSelectedHandCards(SelectedTargetCardKey))
            {
                var validation = State.ValidateAttackTarget(target, SelectedTargetCardKey);
                if (!validation.IsValid)
                {
                    LastInputMessage = validation.FailureReason;
                    ShowTargetRejectedFloatingText(target, CombatCardKind.Attack, SelectedTargetCardKey);
                    RequestInvalidInputAudioCue(LastInputMessage);
                    RefreshView();
                    return false;
                }

                var beganHandSelection = BeginHandCardSelectionForAdditionalCost(SelectedTargetCardKey, target);
                if (beganHandSelection)
                {
                    NotifyTutorialTargetSelected(target);
                }

                return beganHandSelection;
            }

            var before = CapturePresentationSnapshot();
            var beforeTargetMonster = ResolvePresentationAttackTarget(before, target);
            // Resolve the played card's catalog id now, while the selection is still intact (ClearSelection
            // runs before the attack sequence starts). Used to look up this card's per-attack timing override.
            var attackCardTimingId = selected == CombatCardKind.Attack ? ResolveAttackCardCatalogId(SelectedTargetCardKey) : null;
            // When impact-sync is enabled, buffer the attack's EffectResolved dispatch so the damage
            // number/SFX/VFX/camera-shake land on the animation impact frame (flushed inside
            // ResolveAttackSequence) instead of the instant TryPlayerAttack resolves the rules.
            var bufferAttackEffects = selected == CombatCardKind.Attack
                && EffectiveAlignImpactToAnimation
                && ShouldPlayPresentationSequence();
            // Scout and field-object placements that hit several tiles buffer their effects too,
            // effects too, so the multi-target damage burst can be replayed one target at a time.
            var bufferBurstEffects = (selected == CombatCardKind.Scout || selected == CombatCardKind.FieldObject)
                && EffectiveAlignImpactToAnimation
                && ShouldPlayPresentationSequence();
            if (bufferAttackEffects || bufferBurstEffects)
            {
                State.BeginEffectBuffering();
            }

            var pendingChoiceAttack = selected == CombatCardKind.Attack && IsPendingChoiceAttackSelection();
            var pendingHandCardSelectionAttack = selected == CombatCardKind.Attack && IsPendingHandCardSelectionAttack();
            var used = selected == CombatCardKind.Attack
                ? pendingHandCardSelectionAttack
                    ? State.TryPlayerSacrificeAttack(target, SelectedTargetCardKey, handCardSelectionSelectedKeys.ToArray())
                    : pendingChoiceAttack
                    ? State.TryPlayerChoiceOption(pendingChoicePanel.SourceCardKey, "attack", target)
                    : State.TryPlayerAttack(target, SelectedTargetCardKey)
                : selected == CombatCardKind.Investigate
                    ? State.TryPlayerInvestigate(target)
                    : selected == CombatCardKind.FieldObject
                        ? State.TryPlayerFieldObject(target, SelectedTargetCardKey)
                        : State.TryPlayerScout(target, SelectedTargetCardKey);

            if (used)
            {
                RaiseCardCastCue(selected, SelectedTargetCardKey);
                LastInputMessage = selected == CombatCardKind.Attack
                    ? $"Played Attack ({SelectedCardName}) on selected monster. Spend remaining cost or press End Action."
                    : selected == CombatCardKind.Investigate
                        ? State.LastInvestigateResult
                        : selected == CombatCardKind.FieldObject
                            ? $"Played FieldObject ({SelectedCardName})."
                            : "Played Scout. Target revealed and nearby unknown hexes hinted.";
                if (selected == CombatCardKind.Investigate)
                {
                    RequestAudioCue(AudioCueIds.ObjectiveInvestigateSuccess, "objective:investigate-success");
                }
                if (selected == CombatCardKind.FieldObject)
                {
                    ApplyPlayerHexFacing(before.PlayerCoord, target);
                    atlasTilePresentationView?.TriggerPlayerField();
                    // Install the field-object marker immediately. When a burst presentation sequence takes
                    // ownership below (return true), the full RefreshView ??and thus SyncFieldObjectVisuals ??
                    // is deferred until the sequence ends, so the placed object would otherwise pop in late.
                    SyncFieldObjectVisuals();
                    Physics.SyncTransforms();
                }
                if (selected == CombatCardKind.Scout)
                {
                    ApplyPlayerHexFacing(before.PlayerCoord, target);
                    atlasTilePresentationView?.TriggerPlayerBuff();
                }
                if (pendingChoiceAttack)
                {
                    ClearPendingChoicePanel();
                }
                if (pendingHandCardSelectionAttack)
                {
                    ClearHandCardSelection();
                }
                ClearSelection();
                NotifyTutorialTargetSelected(target);
                if (selected == CombatCardKind.Attack && ShouldPlayPresentationSequence())
                {
                    var after = CapturePresentationSnapshot();
                    StartPresentationSequence(
                        "Resolving attack...",
                        RunAttackTimeline(before, after, target, beforeTargetMonster.Id, LastInputMessage, attackCardTimingId));
                    return true;
                }
                if (bufferBurstEffects)
                {
                    var after = CapturePresentationSnapshot();
                    StartPresentationSequence(
                        "Resolving effect...",
                        RunBurstTimeline(before, after, LastInputMessage));
                    return true;
                }
            }
            else
            {
                LastInputMessage = State.LastFailureReason;
                ShowTargetRejectedFloatingText(target, selected, SelectedTargetCardKey);
                RequestInvalidInputAudioCue(LastInputMessage);
            }

            // Release any buffered effects if no presentation sequence took ownership of the flush
            // (e.g. the attack failed). FlushBufferedEffects is idempotent, so this is a safe no-op
            // when a sequence already started and will flush at its impact beat.
            if ((bufferAttackEffects || bufferBurstEffects) && State != null && State.IsBufferingEffects)
            {
                State.FlushBufferedEffects();
            }

            RefreshView();
            if (used && selected == CombatCardKind.Attack)
            {
                TriggerEnemyDeadIfVisibleMonsterDied(beforeTargetMonster);
            }

            return used;
        }

        private static readonly Color TargetRejectionColor = new Color(1f, 0.55f, 0.4f, 1f);

        // #5: surface why a clicked target tile was rejected as a floating text on that tile (out of range vs
        // blocked by terrain/monster) so a no-op click isn't silent. Reason is best-effort Korean copy.
        private void ShowTargetRejectedFloatingText(HexCoord target, CombatCardKind kind, string cardKey)
        {
            var presentation = ResolveMovementEffectPresentation();
            if (presentation == null || !TryGetTileWorldPosition(target, out var worldPosition))
            {
                return;
            }

            presentation.PlayFloatingText(ResolveTargetRejectionText(kind, cardKey, target), worldPosition, TargetRejectionColor);
        }

        private string ResolveTargetRejectionText(CombatCardKind kind, string cardKey, HexCoord target)
        {
            if (IsTargetOutOfCardRange(cardKey, target))
            {
                return "사거리 밖이에요";
            }

            switch (kind)
            {
                case CombatCardKind.Move:
                    return "이동할 수 없는 위치예요";
                case CombatCardKind.FieldObject:
                    return "설치할 수 없는 위치예요";
                case CombatCardKind.Attack:
                    return "공격할 수 없는 대상이에요";
                default:
                    return "선택할 수 없는 위치예요";
            }
        }

        private bool IsTargetOutOfCardRange(string cardKey, HexCoord target)
        {
            if (State == null || string.IsNullOrWhiteSpace(cardKey))
            {
                return false;
            }

            var card = State.GetCombatCards().FirstOrDefault(candidate => !candidate.IsDiscarded
                && (string.Equals(candidate.SelectionKey, cardKey, System.StringComparison.Ordinal)
                    || string.Equals(candidate.Id, cardKey, System.StringComparison.Ordinal)));
            if (string.IsNullOrWhiteSpace(card.Id))
            {
                return false;
            }

            return card.Range > 0 && State.PlayerCoord.DistanceTo(target) > card.Range;
        }

        private bool IsPendingChoiceAttackSelection()
        {
            return !string.IsNullOrEmpty(pendingChoicePanel.SourceCardKey)
                && (string.Equals(pendingChoicePanel.SourceCardKey, SelectedTargetCardKey, System.StringComparison.Ordinal)
                    || string.Equals(pendingChoicePanel.SourceCardId, SelectedTargetCardKey, System.StringComparison.Ordinal)
                    || string.Equals(pendingChoicePanel.SourceCardId, SelectedCardKey, System.StringComparison.Ordinal));
        }

        private bool IsPendingHandCardSelectionAttack()
        {
            return isHandCardSelectionActive
                && isHandCardSelectionAwaitingTarget
                && !string.IsNullOrEmpty(handCardSelectionSourceCardKey)
                && (string.Equals(handCardSelectionSourceCardKey, SelectedTargetCardKey, System.StringComparison.Ordinal)
                    || string.Equals(handCardSelectionSourceCardKey, SelectedCardKey, System.StringComparison.Ordinal));
        }

        public void HandleMapHexClickForTests(HexCoord coord)
        {
            OnHexClicked(coord);
        }

        private void ClearSelection()
        {
            selectionState = CombatSelectionState.None;
            ClearPendingChoicePanel();
            ClearHandCardSelection();
        }

        private void ClearPendingChoicePanel()
        {
            pendingChoicePanel = new ChoiceCardPanelModel(string.Empty, System.Array.Empty<ChoiceCardOptionModel>());
            isChoicePanelVisible = false;
        }

        private void ClearPendingChoiceIfDifferent(string cardKey)
        {
            if (string.IsNullOrEmpty(pendingChoicePanel.SourceCardKey))
            {
                return;
            }

            if (string.IsNullOrEmpty(cardKey))
            {
                ClearPendingChoicePanel();
                return;
            }

            if (MatchesCardKey(pendingChoicePanel.SourceCard, cardKey))
            {
                return;
            }

            ClearPendingChoicePanel();
        }

        private void ClearHandCardSelection()
        {
            isHandCardSelectionActive = false;
            isHandCardSelectionAwaitingTarget = false;
            handCardSelectionSourceCardKey = string.Empty;
            handCardSelectionPromptText = string.Empty;
            handCardSelectionMinCount = 0;
            handCardSelectionMaxCount = 0;
            hasHandCardSelectionLockedTarget = false;
            handCardSelectionLockedTarget = default;
            handCardSelectionSelectedKeys.Clear();
        }

        private void ClearHandCardSelectionIfDifferent(string cardKey)
        {
            if (!isHandCardSelectionActive || string.IsNullOrEmpty(handCardSelectionSourceCardKey))
            {
                return;
            }

            if (!string.IsNullOrEmpty(cardKey) && string.Equals(handCardSelectionSourceCardKey, cardKey, System.StringComparison.Ordinal))
            {
                return;
            }

            ClearHandCardSelection();
        }

        private bool IsSelectableHandCardForSelection(CombatCardSnapshot card)
        {
            return isHandCardSelectionActive
                && !card.IsDiscarded
                && card.Kind != CombatCardKind.Move
                && !HandCardSelectionPanelModel.Matches(card, handCardSelectionSourceCardKey);
        }

        private bool TryGetHandCardSelectionVisualState(string cardKey, out HandCardSelectionVisualState visualState)
        {
            visualState = HandCardSelectionVisualState.Normal;
            if (State == null || string.IsNullOrEmpty(cardKey))
            {
                return false;
            }

            var card = State.GetHandCards().FirstOrDefault(candidate => HandCardSelectionPanelModel.Matches(candidate, cardKey));
            if (string.IsNullOrEmpty(card.Id))
            {
                return false;
            }

            visualState = GetHandCardSelectionVisualState(card);
            return true;
        }

        private void SelectMove(string cardId, string cardName)
        {
            CancelBagItemTargeting();
            selectionState = CombatSelectionState.Move(cardId, cardName);
        }

        private void SelectMove(string cardId, string cardInstanceId, string cardName)
        {
            CancelBagItemTargeting();
            selectionState = CombatSelectionState.Move(cardId, cardInstanceId, cardName);
        }

        private void SelectTarget(CombatCardKind kind, string cardId, string cardName)
        {
            CancelBagItemTargeting();
            selectionState = CombatSelectionState.Target(kind, cardId, cardName);
        }

        private void SelectTarget(CombatCardKind kind, string cardId, string cardInstanceId, string cardName)
        {
            CancelBagItemTargeting();
            selectionState = CombatSelectionState.Target(kind, cardId, cardInstanceId, cardName);
        }

        private bool BeginTargetCardSelection(CombatCardKind kind, string selectedMessage)
        {
            return BeginTargetCardSelection(kind, string.Empty, selectedMessage);
        }

        private bool BeginTargetCardSelection(CombatCardKind kind, string cardId, string selectedMessage)
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null)
            {
                LastInputMessage = "Integration demo is not initialized.";
                RefreshView();
                return false;
            }

            if (!CanBeginTargetCardSelection(kind, cardId, out var failureReason))
            {
                LastInputMessage = failureReason;
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            if (IsPendingChoiceTargetSelection(kind, cardId))
            {
                isChoicePanelVisible = false;
            }
            else
            {
                ClearPendingChoiceIfDifferent(cardId);
            }

            ClearHandCardSelectionIfDifferent(cardId);

            reachable = new Dictionary<HexCoord, int>();
            TryGetSelectableCard(kind, cardId, out var selectedCard);
            SelectTarget(kind, selectedCard.Id, selectedCard.InstanceId, selectedCard.Name);
            LastTargetInfoText = string.Empty;
            LastInputMessage = string.IsNullOrEmpty(selectedMessage) ? FormatTargetSelectionMessage(kind, selectedCard) : selectedMessage;
            RequestAudioCue(AudioCueIds.UiCardSelect, $"select:{selectedCard.Id}");
            RefreshView();
            return true;
        }

        private bool IsPendingChoiceTargetSelection(CombatCardKind kind, string cardKey)
        {
            return kind == CombatCardKind.Attack
                && !string.IsNullOrEmpty(cardKey)
                && !string.IsNullOrEmpty(pendingChoicePanel.SourceCardKey)
                && MatchesCardKey(pendingChoicePanel.SourceCard, cardKey);
        }

        private bool CanBeginTargetCardSelection(CombatCardKind kind, string cardId, out string failureReason)
        {
            failureReason = string.Empty;
            if (kind == CombatCardKind.Scout)
            {
                var validation = State.ValidateScoutTarget(State.PlayerCoord, cardId);
                if (!validation.IsValid)
                {
                    failureReason = validation.FailureReason;
                    return false;
                }

                return true;
            }

            if (kind == CombatCardKind.Investigate)
            {
                var validation = State.ValidateInvestigateCard();
                if (!validation.IsValid)
                {
                    failureReason = validation.FailureReason;
                    return false;
                }

                return true;
            }

            if (kind == CombatCardKind.FieldObject)
            {
                if (TryGetSelectableCard(CombatCardKind.FieldObject, cardId, out var fieldCard))
                {
                    if (fieldCard.IsUsable)
                    {
                        return true;
                    }

                    failureReason = fieldCard.Status;
                    return false;
                }

                failureReason = "No matching field object card is available.";
                return false;
            }

            if (TryGetSelectableCard(CombatCardKind.Attack, cardId, out var attackCard))
            {
                if (attackCard.IsUsable || IsAttackRangePreviewSelectable(attackCard))
                {
                    return true;
                }

                failureReason = attackCard.Status;
                return false;
            }

            failureReason = "No matching action card is available.";
            return false;
        }

        private bool TryGetSelectableCard(CombatCardKind kind, string cardId, out CombatCardSnapshot card)
        {
            card = State.GetCombatCards().FirstOrDefault(candidate =>
                candidate.Kind == kind &&
                !candidate.IsDiscarded &&
                MatchesCardKey(candidate, cardId));
            return card.Kind == kind;
        }

        private static bool MatchesCardKey(CombatCardSnapshot card, string cardKey)
        {
            return string.IsNullOrEmpty(cardKey) ||
                   string.Equals(card.InstanceId, cardKey, System.StringComparison.Ordinal) ||
                   string.Equals(card.Id, cardKey, System.StringComparison.Ordinal) ||
                   string.Equals(card.CatalogSourceId, cardKey, System.StringComparison.Ordinal);
        }

        private bool IsAttackRangePreviewSelectable(CombatCardSnapshot card)
        {
            return State != null &&
                   State.Phase == CombatPhase.PlayerAction &&
                   card.Kind == CombatCardKind.Attack &&
                   !card.IsDiscarded &&
                   CombatCardStatusText.IsAttackOutOfRange(card.Status);
        }

        private static string FormatTargetSelectionMessage(CombatCardKind kind, CombatCardSnapshot card)
        {
            var name = string.IsNullOrEmpty(card.Name) ? kind.ToString() : card.Name;
            switch (kind)
            {
                case CombatCardKind.Attack:
                    return $"TARGETING ATTACK: {name} selected. Click one highlighted monster cell. Range {card.Range}, cost {card.Cost}. Highlighted cells are valid Attack targets.";
                case CombatCardKind.Scout:
                    return $"TARGETING SCOUT: {name} selected. Click one highlighted walkable map cell. Highlighted cells are valid Scout targets.";
                case CombatCardKind.Investigate:
                    return $"TARGETING INVESTIGATE: {name} selected. Click the revealed objective in range. Range {card.Range}, Ki {card.KiCost}.";
                case CombatCardKind.FieldObject:
                    return $"TARGETING FIELD: {name} selected. Click one walkable map cell in range {card.Range}.";
                default:
                    return $"TARGETING: {name} selected. Highlighted cells are valid targets for the selected card.";
            }
        }

        /// <summary>
        /// Whether a map tile may be selected (clicked) by the player. Water and other terrain
        /// flagged unselectable by <see cref="activeTerrainTraits"/> cannot be selected or moved into.
        /// </summary>
        public bool IsTileSelectable(HexCoord coord)
        {
            if (LoadedMap == null || !LoadedMap.TryGetCell(coord, out var cell))
            {
                return false;
            }

            return (activeTerrainTraits ?? HexTerrainTraits.Default).IsSelectable(cell.TerrainTypeId);
        }

        private bool RejectUnselectableTile(HexCoord coord)
        {
            if (IsTileSelectable(coord))
            {
                return false;
            }

            LastTargetInfoText = GetVisibilitySafeTooltipText(coord);
            LastInputMessage = "This tile cannot be selected or entered (impassable terrain).";
            RequestInvalidInputAudioCue(LastInputMessage);
            RefreshView();
            return true;
        }
    }
}
