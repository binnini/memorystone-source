using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Inert <see cref="ICombatCardHudHost"/> for the UI gallery. The HUD views take the whole combat seam,
    /// but the gallery has no combat runtime — it hands the views synthetic data directly. Every member here
    /// returns a neutral value and every command is a no-op, except the hand-card-selection surface, which is
    /// the one thing a gallery entry needs to drive: the 손패 선택 panel had no gallery coverage at all
    /// because there was no way to put it in its active state without a live combat controller.
    ///
    /// Dev-only (Assets/Scripts/Combat/Unity/Dev), same as the gallery itself.
    /// </summary>
    public sealed class UiGalleryStubHudHost : ICombatCardHudHost
    {
        private readonly HashSet<string> selectedKeys = new HashSet<string>(StringComparer.Ordinal);

        public UiGalleryStubHudHost(
            CombatState state,
            string promptText = "",
            int minSelectCount = 0,
            int maxSelectCount = 0,
            IEnumerable<string> preselectedKeys = null,
            bool selectionActive = false)
        {
            State = state;
            SelectionActive = selectionActive;
            PromptText = promptText ?? string.Empty;
            MinSelectCount = minSelectCount;
            MaxSelectCount = maxSelectCount;
            foreach (var key in preselectedKeys ?? Enumerable.Empty<string>())
            {
                selectedKeys.Add(key);
            }
        }

        public bool SelectionActive { get; private set; }
        public string PromptText { get; }
        public int MinSelectCount { get; }
        public int MaxSelectCount { get; }

        public CombatState State { get; }

        public HandCardSelectionPanelModel HandCardSelectionPanel => SelectionActive
            ? new HandCardSelectionPanelModel(true, string.Empty, PromptText, MinSelectCount, MaxSelectCount, selectedKeys.ToArray())
            : HandCardSelectionPanelModel.Empty;

        public HandCardSelectionVisualState GetHandCardSelectionVisualState(CombatCardSnapshot card)
        {
            if (!SelectionActive)
            {
                return HandCardSelectionVisualState.Normal;
            }

            return HandCardSelectionPanel.IsSelected(card)
                ? HandCardSelectionVisualState.Picked
                : HandCardSelectionVisualState.Candidate;
        }

        public bool ToggleHandCardSelection(string cardKey)
        {
            if (!SelectionActive || string.IsNullOrEmpty(cardKey))
            {
                return false;
            }

            if (!selectedKeys.Remove(cardKey) && selectedKeys.Count < Math.Max(1, MaxSelectCount))
            {
                selectedKeys.Add(cardKey);
            }

            return true;
        }

        public bool ConfirmHandCardSelection()
        {
            SelectionActive = false;
            return true;
        }

        public bool CancelHandCardSelection()
        {
            selectedKeys.Clear();
            SelectionActive = false;
            return true;
        }

        // --- Everything below is inert: the gallery drives views with synthetic data, never a real run. ---

        public bool IsSequencePlaying => false;
        public string StatusText => string.Empty;
        public string MonsterStatusText => string.Empty;
        public string ObjectiveStatusText => string.Empty;
        public string EnemyIntentDisplayText => string.Empty;
        public string LastInputMessage => string.Empty;
        public string SelectedCardId => string.Empty;
        public string SelectedCardName => string.Empty;
        public string SelectedCardKey => string.Empty;
        public CombatCardKind? SelectedTargetCardKind => null;
        public string LastTargetInfoText => string.Empty;
        public string DeckStatusText => string.Empty;
        public bool IsMoveSelectionActive => false;
        public bool IsAttackSelectionActive => false;
        public bool IsScoutSelectionActive => false;
        public bool ShowOverlayDebugControls => false;
        public bool IsFogDebugVisible => false;
        public bool IsClickMoveDebugModeEnabled => false;
        public string OverlayRendererStatusText => string.Empty;
        public string TutorialHighlightCardId => string.Empty;
        public CombatTimingProfile TimingProfile => null;

        public void InitializeIntegration() { }
        public bool EndAction() => false;
        public void RestartDemo() { }
        public void ToggleOverlayDebugLayer(CombatOverlayDebugLayer layer) { }
        public bool IsOverlayDebugLayerVisible(CombatOverlayDebugLayer layer) => false;
        public void ToggleClickMoveDebugMode() { }
        public void ToggleFogDebugVisibility() { }
        public void RequestCardHoverAudio(string cardId) { }
        public void NotifyTutorialCardHover(CombatCardSnapshot card) { }
        public void NotifyTutorialCardSelected(CombatCardSnapshot card) { }

        public bool CanSelectCardForTutorial(CombatCardSnapshot card, out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public void ShowTutorialBlockedFeedback(string reason) { }

        public bool PlayMovementSelf(string cardId) => false;
        public bool PlayMovementRandom(string cardId) => false;
        public bool BeginMoveSelection(string cardId) => false;
        public bool BeginChoiceCardSelection(string cardId) => false;
        public bool PlaySelfAreaAttack(string cardId) => false;
        public bool BeginAttackSelection(string cardId) => false;
        public bool UseDefense(string cardId) => false;
        public bool BeginScoutSelection(string cardId) => false;
        public bool BeginInvestigateSelection() => false;
        public bool PlayTorchAtPlayerForDev(string cardId) => false;
        public bool BeginFieldObjectSelection(string cardId) => false;
        public bool UseUtility(string cardId) => false;
    }
}
