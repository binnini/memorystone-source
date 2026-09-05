using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    // Refactoring stage 4-5 (card/HUD presentation seam), relocated to SeoulPlayup.Combat.Contracts
    // during Stage 3 asmdef extraction (2026-07-01) so Cards.Unity views can depend on the seam
    // without depending on Combat.Unity. See ICombatCardHudPresentationViews.cs for the sibling
    // ICardRewardPopupView interface, which remains in Combat.Unity.

    /// <summary>
    /// Seam interface the HUD views depend on instead of the concrete
    /// <c>MapCombatController</c>. Exposes only the members those views actually call.
    /// </summary>
    public interface ICombatCardHudHost
    {
        CombatState State { get; }
        bool IsSequencePlaying { get; }
        void InitializeIntegration();
        string StatusText { get; }
        string MonsterStatusText { get; }
        string ObjectiveStatusText { get; }
        string EnemyIntentDisplayText { get; }
        string LastInputMessage { get; }
        string SelectedCardId { get; }
        string SelectedCardName { get; }
        string SelectedCardKey { get; }
        CombatCardKind? SelectedTargetCardKind { get; }
        string LastTargetInfoText { get; }
        string DeckStatusText { get; }
        bool IsMoveSelectionActive { get; }
        bool IsAttackSelectionActive { get; }
        bool IsScoutSelectionActive { get; }
        bool ShowOverlayDebugControls { get; }
        bool IsFogDebugVisible { get; }
        bool IsClickMoveDebugModeEnabled { get; }
        string OverlayRendererStatusText { get; }
        string TutorialHighlightCardId { get; }
        HandCardSelectionPanelModel HandCardSelectionPanel { get; }
        CombatTimingProfile TimingProfile { get; }

        bool EndAction();
        void RestartDemo();
        void ToggleOverlayDebugLayer(CombatOverlayDebugLayer layer);
        bool IsOverlayDebugLayerVisible(CombatOverlayDebugLayer layer);
        void ToggleClickMoveDebugMode();
        void ToggleFogDebugVisibility();
        void RequestCardHoverAudio(string cardId);
        HandCardSelectionVisualState GetHandCardSelectionVisualState(CombatCardSnapshot card);
        bool ToggleHandCardSelection(string cardKey);
        bool ConfirmHandCardSelection();
        bool CancelHandCardSelection();
        void NotifyTutorialCardHover(CombatCardSnapshot card);
        void NotifyTutorialCardSelected(CombatCardSnapshot card);
        bool CanSelectCardForTutorial(CombatCardSnapshot card, out string reason);
        void ShowTutorialBlockedFeedback(string reason);

        bool PlayMovementSelf(string cardId);
        bool PlayMovementRandom(string cardId);
        bool BeginMoveSelection(string cardId);
        bool BeginChoiceCardSelection(string cardId);
        bool PlaySelfAreaAttack(string cardId);
        bool BeginAttackSelection(string cardId);
        bool UseDefense(string cardId);
        bool BeginScoutSelection(string cardId);
        bool BeginInvestigateSelection();
        bool PlayTorchAtPlayerForDev(string cardId);
        bool BeginFieldObjectSelection(string cardId);
        bool UseUtility(string cardId);
    }
}
