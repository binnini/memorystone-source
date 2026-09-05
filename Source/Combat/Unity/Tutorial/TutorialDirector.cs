using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity.Tutorial
{
    // LateUpdate must run after the HUD views (card lane cue, popups) have settled this frame's layout, so the
    // focus rects read back are current and the spotlight never lags a frame behind a moving card.
    [DefaultExecutionOrder(200)]
    public sealed class TutorialDirector : MonoBehaviour
    {
        private const string DefaultBlockedFeedback = "튜토리얼의 지시에 따라주세요.";

        [SerializeField] private TutorialScriptAsset script;
        [SerializeField] private TutorialHudView hudView;
        [SerializeField] private bool playOnStart;
        [SerializeField] private bool deactivateWhenComplete = true;
        [Tooltip("Seconds after a step appears during which click/Enter/key advances are ignored, so a double-click " +
                 "or a stray click cannot skip through steps. Gated actions (card, tile, button) are unaffected.")]
        [SerializeField, Min(0f)] private float stepInputLockSeconds = 0.5f;

        // Raised whenever a step becomes visible to the player (after its start trigger, if any). The
        // MapCombatController listens so it can drive step-scoped presentation such as a camera focus pan.
        public event System.Action<TutorialStep> StepPresented;

        // Applies the shared tutorial label font (DNFForgedBlade-Light SDF). Injected
        // by the host (MapCombatController) so the tutorial assembly need not reference the Combat.Unity
        // TooltipFontProvider — keeps SeoulPlayup.Tutorial free of a reverse dependency on the combat host.
        public System.Action<TMP_Text> LabelFontApplier { get; set; }

        // Resolves a step's Highlight / CalloutTarget to a screen-pixel rect (null = nothing to point at).
        // Injected by the host, which owns the HUD views, card lane, map camera and monster positions this
        // needs; the tutorial assembly only consumes the rect (spotlight hole, panel placement, callout ring).
        public System.Func<TutorialHighlight, TutorialFocus?> FocusRectProvider { get; set; }

        private float spotlightSuppressedUntil = -1f;
        private float stepPresentedAt = float.NegativeInfinity;

        // True while the post-transition lock is still running for the current step.
        public bool IsInputLocked => Time.unscaledTime < stepPresentedAt + stepInputLockSeconds;

        // Reused per pointer query so the UI-overlap check does not allocate every click.
        private static readonly List<RaycastResult> RaycastResultsBuffer = new List<RaycastResult>();

        private int stepIndex = -1;
        private bool isActive;
        private int lastClickHandledFrame = -1;
        private bool hoverSeenForCurrentStep;
        private bool awaitingTrigger;

        // A step that is waiting for its start trigger (startCombatEventId) is held hidden and must not
        // gate player input, so it is excluded from IsActive until the trigger fires.
        public bool IsActive => isActive && !awaitingTrigger && CurrentStep != null;

        // While a card is selected and the step still needs the player to act with it (pick a target, or
        // confirm a hand-card cost), cancelling the selection would strand the tutorial — so lock cancel.
        public bool IsCardCancelLocked
        {
            get
            {
                if (!IsActive)
                {
                    return false;
                }

                var step = CurrentStep;
                if (step == null)
                {
                    return false;
                }

                if (step.AdvanceMode == TutorialAdvanceMode.TargetSelected)
                {
                    return true;
                }

                return step.AdvanceMode == TutorialAdvanceMode.ButtonPressed
                    && step.Requirement != null
                    && step.Requirement.RequiredButton == TutorialButtonId.HandCardSelectionConfirm;
            }
        }
        public TutorialStep CurrentStep => script != null && stepIndex >= 0 && stepIndex < script.Steps.Count
            ? script.Steps[stepIndex]
            : null;

        private void Awake()
        {
            hudView = hudView != null ? hudView : GetComponentInChildren<TutorialHudView>(true);
        }

        private void Start()
        {
            if (playOnStart && script != null && script.Steps.Count > 0)
            {
                Begin();
            }
        }

        // Hides the spotlight dim for a while (e.g. while the host pans the camera to a called-out hex — the
        // thing being pointed at must not be darkened). The dim returns on its own when the window elapses.
        public void SuppressSpotlightFor(float seconds)
        {
            spotlightSuppressedUntil = Mathf.Max(spotlightSuppressedUntil, Time.unscaledTime + Mathf.Max(0f, seconds));
        }

        private void LateUpdate()
        {
            if (!IsActive || hudView == null)
            {
                return;
            }

            var step = CurrentStep;
            var focus = ResolveRect(step.Highlight);
            var callout = ResolveRect(step.CalloutTarget);
            hudView.UpdateLayout(focus, callout, Time.unscaledTime < spotlightSuppressedUntil);
            hudView.SetContinueReady(step.AdvanceMode == TutorialAdvanceMode.Click && !IsInputLocked);
        }

        private TutorialFocus? ResolveRect(TutorialHighlight highlight)
        {
            if (highlight == null || !highlight.IsSet || FocusRectProvider == null)
            {
                return null;
            }

            return FocusRectProvider(highlight);
        }

        private void Update()
        {
            if (!IsActive)
            {
                return;
            }

            if (IsLeftMousePressedThisFrame())
            {
                HandleClickInput();
            }

            if (IsEnterPressedThisFrame())
            {
                HandleContinueInput();
            }

            // Re-read after the click/Enter handlers: either may have advanced past the last step, which
            // completes the tutorial and leaves CurrentStep null.
            var step = CurrentStep;
            if (step == null || step.AdvanceMode != TutorialAdvanceMode.KeyPressed || IsInputLocked)
            {
                return;
            }

            // One accepted key per frame — NotifyKeyPressed advances the step, and a second key in the same
            // frame would be judged against (and could consume) the step that just appeared.
            foreach (var key in PolledKeys)
            {
                if (!IsKeyPressedThisFrame(key))
                {
                    continue;
                }

                NotifyKeyPressed(key);
                if (CurrentStep != step)
                {
                    break;
                }
            }
        }

        private static readonly TutorialKeyId[] PolledKeys =
        {
            TutorialKeyId.A, TutorialKeyId.D, TutorialKeyId.W, TutorialKeyId.S, TutorialKeyId.Y,
            TutorialKeyId.Space, TutorialKeyId.F, TutorialKeyId.Q, TutorialKeyId.E,
        };

        private static bool IsLeftMousePressedThisFrame()
        {
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        }

        private static bool IsEnterPressedThisFrame()
        {
            var keyboard = Keyboard.current;
            return keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
        }

        private static bool IsKeyPressedThisFrame(TutorialKeyId key)
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            switch (key)
            {
                case TutorialKeyId.A:
                    return keyboard.aKey.wasPressedThisFrame;
                case TutorialKeyId.D:
                    return keyboard.dKey.wasPressedThisFrame;
                case TutorialKeyId.W:
                    return keyboard.wKey.wasPressedThisFrame;
                case TutorialKeyId.S:
                    return keyboard.sKey.wasPressedThisFrame;
                case TutorialKeyId.Y:
                    return keyboard.yKey.wasPressedThisFrame;
                case TutorialKeyId.Space:
                    return keyboard.spaceKey.wasPressedThisFrame;
                case TutorialKeyId.F:
                    return keyboard.fKey.wasPressedThisFrame;
                case TutorialKeyId.Q:
                    return keyboard.qKey.wasPressedThisFrame;
                case TutorialKeyId.E:
                    return keyboard.eKey.wasPressedThisFrame;
                default:
                    return false;
            }
        }

        public void SetScript(TutorialScriptAsset tutorialScript)
        {
            script = tutorialScript;
            if (script == null)
            {
                Complete();
            }
        }

        private void BeginCore()
        {
            if (script == null || script.Steps.Count == 0)
            {
                isActive = false;
                hudView?.Hide();
                return;
            }

            isActive = true;
            stepIndex = 0;
            EnsureHudView();
            ShowCurrentStep();
        }

        public void Complete()
        {
            isActive = false;
            stepIndex = -1;
            // Always tear the presentation down: the spotlight dim swallows input and the panel keeps
            // blocking raycasts until Hide() runs, so leaving it up would lock the player out for good.
            // deactivateWhenComplete only decides whether the whole HUD object goes inactive as well.
            hudView?.Hide();
            if (deactivateWhenComplete && hudView != null)
            {
                hudView.gameObject.SetActive(false);
            }
        }

        public void Begin()
        {
            if (hudView != null && !hudView.gameObject.activeSelf)
            {
                hudView.gameObject.SetActive(true);
            }

            BeginCore();
        }

        public bool CanSelectCard(CombatCardSnapshot card, out string reason)
        {
            return CanPass(TutorialAdvanceMode.CardSelected, out reason)
                && (CurrentStep?.Requirement == null || CurrentStep.Requirement.MatchesCard(card));
        }

        public bool CanSelectTarget(HexCoord coord, out string reason)
        {
            return CanPass(TutorialAdvanceMode.TargetSelected, out reason)
                && (CurrentStep?.Requirement == null || CurrentStep.Requirement.MatchesTarget(coord));
        }

        public bool CanPressButton(TutorialButtonId button, out string reason)
        {
            return CanPressButton(button, -1, out reason);
        }

        public bool CanPressButton(TutorialButtonId button, int selectedHandCardCount, out string reason)
        {
            return CanPass(TutorialAdvanceMode.ButtonPressed, out reason)
                && (CurrentStep?.Requirement == null || CurrentStep.Requirement.MatchesButton(button, selectedHandCardCount));
        }

        public bool CanUseKey(TutorialKeyId key, out string reason)
        {
            return CanPass(TutorialAdvanceMode.KeyPressed, out reason)
                && (CurrentStep?.Requirement == null || CurrentStep.Requirement.MatchesKey(key));
        }

        public void NotifyCardSelected(CombatCardSnapshot card)
        {
            if (IsActive && CurrentStep.AdvanceMode == TutorialAdvanceMode.CardSelected && CurrentStep.Requirement.MatchesCard(card))
            {
                Advance();
            }
        }

        public void NotifyTargetSelected(HexCoord coord)
        {
            if (IsActive && CurrentStep.AdvanceMode == TutorialAdvanceMode.TargetSelected && CurrentStep.Requirement.MatchesTarget(coord))
            {
                Advance();
            }
        }

        public void NotifyButtonPressed(TutorialButtonId button)
        {
            if (IsActive && CurrentStep.AdvanceMode == TutorialAdvanceMode.ButtonPressed && CurrentStep.Requirement.MatchesButton(button))
            {
                Advance();
            }
        }

        public void NotifyKeyPressed(TutorialKeyId key)
        {
            if (IsActive && CurrentStep.AdvanceMode == TutorialAdvanceMode.KeyPressed && CurrentStep.Requirement.MatchesKey(key))
            {
                // Sink-and-flash the keycap so the player sees the press landed before the step moves on.
                hudView?.PlayKeyCuePressed();
                Advance();
            }
        }

        public void NotifyHoverCompleted(string targetId)
        {
            if (!IsActive)
            {
                return;
            }

            var step = CurrentStep;

            // Object hovers (monster/field/map object) only apply to steps that target a hover id. Card
            // hovers are handled by NotifyCardHover (matched against the card requirement instead).
            if (string.IsNullOrWhiteSpace(step.Requirement.RequiredHoverTargetId))
            {
                return;
            }

            if (step.AdvanceMode == TutorialAdvanceMode.HoverCompleted && step.Requirement.MatchesHover(targetId))
            {
                Advance();
            }
            else if (step.AdvanceMode == TutorialAdvanceMode.HoverReleased && step.Requirement.MatchesHover(targetId))
            {
                // Remember the hover started so the step can advance when the pointer leaves the target.
                hoverSeenForCurrentStep = true;
            }
        }

        public void NotifyCardHover(CombatCardSnapshot card)
        {
            if (!IsActive)
            {
                return;
            }

            var step = CurrentStep;
            if (step.AdvanceMode == TutorialAdvanceMode.HoverCompleted
                && !string.IsNullOrWhiteSpace(step.Requirement.RequiredCardId)
                && step.Requirement.MatchesCard(card))
            {
                Advance();
            }
        }

        public void NotifyHoverEnded(string targetId)
        {
            if (!IsActive)
            {
                return;
            }

            var step = CurrentStep;
            if (step.AdvanceMode == TutorialAdvanceMode.HoverReleased
                && hoverSeenForCurrentStep
                && step.Requirement.MatchesHover(targetId))
            {
                Advance();
            }
        }

        public bool CanSelectReward(string cardId, out string reason)
        {
            reason = string.Empty;
            if (!IsActive)
            {
                return true;
            }

            if (CurrentStep?.AdvanceMode != TutorialAdvanceMode.RewardSelected)
            {
                reason = CurrentStep?.BlockedFeedback ?? DefaultBlockedFeedback;
                return false;
            }

            var requiredCardId = CurrentStep.Requirement?.RequiredCardId;
            return string.IsNullOrWhiteSpace(requiredCardId)
                || string.Equals(requiredCardId.Trim(), cardId?.Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        public void NotifyRewardSelected(string cardId)
        {
            if (!IsActive || CurrentStep.AdvanceMode != TutorialAdvanceMode.RewardSelected)
            {
                return;
            }

            var requiredCardId = CurrentStep.Requirement?.RequiredCardId;
            if (string.IsNullOrWhiteSpace(requiredCardId)
                || string.Equals(requiredCardId.Trim(), cardId?.Trim(), System.StringComparison.OrdinalIgnoreCase))
            {
                Advance();
            }
        }

        public void NotifyCombatEvent(string eventId)
        {
            if (!isActive)
            {
                return;
            }

            var step = CurrentStep;
            if (step == null)
            {
                return;
            }

            if (awaitingTrigger)
            {
                if (MatchesStartTrigger(step, eventId))
                {
                    awaitingTrigger = false;
                    PresentCurrentStep();
                }

                return;
            }

            if (step.AdvanceMode == TutorialAdvanceMode.CombatEvent && step.Requirement.MatchesCombatEvent(eventId))
            {
                Advance();
            }
        }

        private static bool MatchesStartTrigger(TutorialStep step, string eventId)
        {
            return !string.IsNullOrWhiteSpace(step.StartCombatEventId)
                && string.Equals(step.StartCombatEventId.Trim(), eventId?.Trim(), System.StringComparison.OrdinalIgnoreCase);
        }

        public void ShowBlockedFeedback(string reason = null)
        {
            var message = string.IsNullOrWhiteSpace(reason) ? CurrentStep?.BlockedFeedback ?? DefaultBlockedFeedback : reason;
            EnsureHudView();
            hudView?.ShowFeedback(message);
        }

        public void Advance()
        {
            if (!isActive)
            {
                return;
            }

            stepIndex++;
            if (script == null || stepIndex >= script.Steps.Count)
            {
                Complete();
                return;
            }

            ShowCurrentStep();
        }

        private bool CanPass(TutorialAdvanceMode inputMode, out string reason)
        {
            reason = string.Empty;
            if (!IsActive)
            {
                return true;
            }

            var step = CurrentStep;
            if (step == null || step.AdvanceMode == inputMode)
            {
                return true;
            }

            reason = step.BlockedFeedback;
            return false;
        }

        // Left click advances a narration (Click) step (per QA feedback: the previous Enter-only flow was
        // unintuitive). Frame-gated so a single click can't consume multiple steps; gameplay steps are gated
        // and handled elsewhere.
        private void HandleClickInput()
        {
            if (!IsActive || IsInputLocked)
            {
                return;
            }

            if (lastClickHandledFrame == Time.frameCount)
            {
                return;
            }

            // A click only advances narration when it lands on empty space (the game world) or on the
            // tutorial panel itself. If it overlaps other gameplay UI — the sidebar, deck/discard piles,
            // or hand cards — the click belongs to that UI and must not consume the step.
            if (CurrentStep.AdvanceMode == TutorialAdvanceMode.Click && IsPointerOverBlockingUi())
            {
                return;
            }

            lastClickHandledFrame = Time.frameCount;
            if (CurrentStep.AdvanceMode == TutorialAdvanceMode.Click)
            {
                Advance();
            }
        }

        // True when the pointer is over a UI element that is not part of the tutorial HUD. Such a click is
        // meant for that UI (sidebar button, card pile, hand card, …), so it must not advance the tutorial.
        private bool IsPointerOverBlockingUi()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var pointerDevice = Pointer.current;
            if (Mouse.current == null && pointerDevice == null)
            {
                return false;
            }

            var pointer = new PointerEventData(eventSystem)
            {
                position = Mouse.current != null
                    ? Mouse.current.position.ReadValue()
                    : pointerDevice.position.ReadValue(),
            };

            RaycastResultsBuffer.Clear();
            eventSystem.RaycastAll(pointer, RaycastResultsBuffer);

            // RaycastAll returns everything under the pointer, sorted top-most first, without occlusion —
            // the gameplay HUD beneath the full-screen spotlight dim is still in the list. Only the top-most
            // hit matters: if the dim/panel absorbed the click it is ours; if something sits above the
            // tutorial HUD (or nothing tutorial is hit at all) the click belongs to that UI.
            for (int i = 0; i < RaycastResultsBuffer.Count; i++)
            {
                var hit = RaycastResultsBuffer[i].gameObject;
                if (hit == null)
                {
                    continue;
                }

                return !IsPartOfTutorialHud(hit.transform);
            }

            return false;
        }

        private bool IsPartOfTutorialHud(Transform target)
        {
            if (target == null)
            {
                return false;
            }

            if (hudView != null && target.IsChildOf(hudView.HudRoot))
            {
                return true;
            }

            return target.IsChildOf(transform);
        }

        // Enter advances a narration (Click) step.
        private void HandleContinueInput()
        {
            if (!IsActive || IsInputLocked)
            {
                return;
            }

            if (CurrentStep.AdvanceMode == TutorialAdvanceMode.Click)
            {
                Advance();
            }
        }

        private void ShowCurrentStep()
        {
            EnsureHudView();
            hoverSeenForCurrentStep = false;

            var step = CurrentStep;
            if (step != null && !string.IsNullOrWhiteSpace(step.StartCombatEventId))
            {
                // Hold the step hidden until its start trigger (e.g. boss enters vision) fires via
                // NotifyCombatEvent. IsActive stays false meanwhile so player input is not gated.
                awaitingTrigger = true;
                hudView?.Hide();
                return;
            }

            awaitingTrigger = false;
            PresentCurrentStep();
        }

        private void PresentCurrentStep()
        {
            EnsureHudView();
            stepPresentedAt = Time.unscaledTime;
            hudView?.ShowStep(CurrentStep);
            StepPresented?.Invoke(CurrentStep);
            if (CurrentStep != null && CurrentStep.AdvanceMode == TutorialAdvanceMode.Auto)
            {
                Advance();
            }
        }

        private void EnsureHudView()
        {
            if (hudView != null)
            {
                return;
            }

            hudView = GetComponentInChildren<TutorialHudView>(true);
            if (hudView != null)
            {
                return;
            }

            hudView = BuildFallbackHudView();
        }

        private TutorialHudView BuildFallbackHudView()
        {
            var canvasObject = new GameObject("TutorialCanvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 4500;

            var scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            // Match the gameplay HUD reference resolution (GameplaySceneContract 2200x1238) so tutorial
            // UI shares the same scale as the HUD it overlays.
            scaler.referenceResolution = new Vector2(2200f, 1238f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            canvasObject.AddComponent<GraphicRaycaster>();
            canvasObject.AddComponent<CanvasGroup>();

            // The view builds the spotlight dim, text panel, callout ring, feedback label and key cue itself
            // (all runtime-created; no scene or prefab authoring). Fonts arrive via the host-injected applier.
            var view = canvasObject.AddComponent<TutorialHudView>();
            view.Build((RectTransform)canvasObject.transform, LabelFontApplier);
            return view;
        }
    }
}
