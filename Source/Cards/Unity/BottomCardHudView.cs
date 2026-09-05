using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Phase 3 (card draw/discard flight) wiring lives in this view; see docs/card-draw-discard-animation-design.md.

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class BottomCardHudView : MonoBehaviour
    {
        private static readonly Color InvisibleHitColor = new Color(1f, 1f, 1f, 0.02f);

        // P6 T3 dock frame: same plate language as the popups/callouts, a touch lighter than the panel fill so
        // the tiles read as raised against the board.
        private const string DockFrameName = "Dock Frame";
        private const float DockFramePad = 6f;
        private static readonly Color DockFillColor = new Color32(0x24, 0x2C, 0x54, 0xE6);
        private static readonly Color DockBorderColor = new Color32(0xC9, 0xA2, 0x27, 0x9E);
        private static readonly Color DockHighlightColor = new Color(1f, 0.96f, 0.88f, 1f);
        // End-action emphasis, as a plate fill rather than a vertex tint — the old orange multiplied into the
        // dark fill and came out muddy brown, which read as *less* urgent than before the frame pass.
        private static readonly Color EndActionEmphasisFill = new Color32(0xB4, 0x54, 0x0A, 0xF0);
        private static readonly Color EndActionEmphasisBorder = new Color32(0xFF, 0xC2, 0x4A, 0xFF);

        [Header("External Gameplay Card UI")]
        [SerializeField] private GameplayCardLaneView cardLaneView;
        [SerializeField] private RectTransform drawPileDock;
        [SerializeField] private RectTransform healthDock;
        [SerializeField] private RectTransform energyDock;
        [SerializeField] private RectTransform discardPileDock;
        [SerializeField] private RectTransform exilePileDock;
        [SerializeField] private RectTransform combatControlDock;
        [SerializeField] private CardLaneStatusEffectDockView statusEffectDockView;

        // Host-injected so this view avoids a reverse dependency on Combat.Unity's
        // KoreanFontProvider (mirrors TutorialDirector.LabelFontApplier from cs:253).
        public Action<TMP_Text> KoreanFontApplier { get; set; }

        // Content targets. Optional explicit refs for rename safety: when assigned in the inspector the
        // runtime uses them directly; when left null, ResolveReferences() falls back to the child-name
        // lookups below (unchanged behaviour). Assigning these makes the HUD survive object renames.
        [Header("Content Targets (optional; name lookup is the fallback)")]
        [SerializeField] private TMP_Text drawCountText;
        [SerializeField] private TMP_Text healthText;
        [SerializeField] private TMP_Text energyText;
        [SerializeField] private TMP_Text discardCountText;
        [SerializeField] private TMP_Text exileCountText;
        [SerializeField] private RectTransform shieldIcon;
        [SerializeField] private TMP_Text shieldValueText;
        [SerializeField] private TMP_Text phaseText;
        [SerializeField] private TMP_Text endActionLabelText;
        private RectTransform handCardSelectionOverlayRoot;
        private TMP_Text handCardSelectionPromptText;
        private RectTransform handCardSelectionDimOverlay;
        private Button drawPileButton;
        private Button discardPileButton;
        private Button exilePileButton;
        private Button endActionButton;
        private UiProceduralPanel endActionSkin;
        private bool docksSkinned;
        private bool hasEndActionBaseColors;
        private ColorBlock endActionBaseColors;
        private Graphic endActionTargetGraphic;
        private bool hasEndActionBaseGraphicColor;
        private Color endActionBaseGraphicColor;
        private Button handCardSelectionConfirmButton;
        private Button handCardSelectionCancelButton;
        private Action drawPileClickAction;
        private Action discardPileClickAction;
        private Action exilePileClickAction;
        private Action endActionClickAction;
        private CombatState latestState;
        private CardFlightAnimator flightAnimator;
        private readonly Dictionary<string, CardZone> previousZones = new Dictionary<string, CardZone>(StringComparer.Ordinal);
        private static readonly Dictionary<string, CardZone> EmptyZones = new Dictionary<string, CardZone>(StringComparer.Ordinal);
        private bool hasPreviousZones;

        public GameplayCardLaneView CardLaneView => cardLaneView;
        public RectTransform DrawPileDock => drawPileDock;

        /// <summary>
        /// 카드 비행 기구. 저주 주입 연출(2026-09-01 #9)이 「버린 카드가 더미로 빨려드는」 그 비행을
        /// 그대로 쓴다 — 카드가 나는 모습이 화면마다 다르면 플레이어는 다른 종류의 일로 읽는다.
        /// 하단 HUD가 런타임에 서므로 배선 전에는 null일 수 있다(호출자가 그 경우 연출만 접는다).
        /// </summary>
        public CardFlightAnimator FlightAnimator => flightAnimator;
        public RectTransform HealthDock => healthDock;
        public RectTransform EnergyDock => energyDock;
        public RectTransform DiscardPileDock => discardPileDock;
        public RectTransform ExilePileDock => exilePileDock;
        public RectTransform CombatControlDock => combatControlDock;
        public Button EndActionButton => endActionButton;

        public void ConfigureEndAction(Action onEndActionClicked)
        {
            endActionClickAction = onEndActionClicked;
            ResolveReferences();
            ConfigureEndActionButton(latestState);
        }

        public void ConfigurePileClickActions(Action onDrawPileClicked, Action onDiscardPileClicked, Action onExilePileClicked = null)
        {
            drawPileClickAction = onDrawPileClicked;
            discardPileClickAction = onDiscardPileClicked;
            exilePileClickAction = onExilePileClicked;
            ResolveReferences();
            ConfigurePileButton(drawPileDock, ref drawPileButton, drawPileClickAction);
            ConfigurePileButton(discardPileDock, ref discardPileButton, discardPileClickAction);
            ConfigurePileButton(exilePileDock, ref exilePileButton, exilePileClickAction);
        }

        public void Refresh(
            CombatState state,
            PlayerStateSnapshot snapshot,
            ICombatCardHudHost controller,
            TMP_FontAsset uiFont,
            Action<CombatCardSnapshot> playCard)
        {
            latestState = state;
            ResolveReferences();
            // Before ConfigureAuthoredControls: it drives the end-action emphasis, which caches the button's
            // graphic colour as its baseline the first time it runs. Skinning first means it caches the
            // skinned (white) tint rather than the authored navy.
            SkinDocksOnce();
            ConfigureAuthoredControls();
            flightAnimator?.ConfigureTimingProfile(controller != null ? controller.TimingProfile : null);

            // Capture outgoing-card flight sources BEFORE the hand re-lays-out (discarded cards are gone
            // from the hand afterwards), then launch the flights once the new layout is in place.
            var flightPlan = PrepareCardFlights(state);
            cardLaneView?.Refresh(state, controller, playCard);
            LaunchCardFlights(flightPlan);

            // Safety net for the deal-in reveal: LaunchCardFlights hides each drawn slot
            // (CanvasGroup.alpha = 0) and only reveals it when the draw-flight ghost lands. If a flight is
            // interrupted before landing (e.g. the flight animator is disabled mid-deal), that reveal never
            // fires and the card stays invisible for the rest of the turn. Once no flight is in progress
            // (IsFlying == false, which CleanupGhosts also forces on an interruption), any active hand slot
            // still left hidden is stuck, so restore it. Guarded on IsFlying so an in-progress deal-in is
            // never prematurely revealed. Runs every frame via GameplayHudBridge, so recovery is immediate.
            if (flightAnimator == null || !flightAnimator.IsFlying)
            {
                RestoreStuckHiddenHandSlots();
            }

            statusEffectDockView?.Refresh(PlayerStatusEffectQuery.GetPlayerStatusEffects(state), uiFont);

            var moveDraw = state?.MovementDeck.DrawCount ?? 0;
            var actionDraw = state?.ActionDeck.DrawCount ?? 0;
            var moveDiscard = state?.MovementDeck.DiscardCount ?? 0;
            var actionDiscard = state?.ActionDeck.DiscardCount ?? 0;
            var moveExile = state?.MovementDeck.RemovedCount ?? 0;
            var actionExile = state?.ActionDeck.RemovedCount ?? 0;

            SetText(drawCountText, state == null ? "--" : (moveDraw + actionDraw).ToString());
            SetText(discardCountText, state == null ? "--" : (moveDiscard + actionDiscard).ToString());
            SetText(exileCountText, state == null ? "--" : (moveExile + actionExile).ToString());
            SetText(healthText, state == null ? "--/--" : $"{snapshot.Hp}/{snapshot.MaxHp}");
            SetText(energyText, state == null ? "--/--" : $"{snapshot.CurrentKi}/{snapshot.MaxKi}");
            RefreshShield(state, snapshot);
            SetText(phaseText, state == null ? "\uC804\uD22C \uB300\uAE30" : PhaseLabel(state.Phase));
            ConfigureEndActionButton(state);
            ConfigureHandCardSelectionControls(controller);
        }

        /// <summary>
        /// Record the current card zones without animating. Call on save-restore so a restored hand
        /// appears instantly instead of dealing in. See docs/card-draw-discard-animation-design.md (edge #1/#2).
        /// </summary>
        public void PrimeZones(CombatState state)
        {
            ResolveReferences();
            StorePreviousZones(CombatCardZoneSnapshot.Build(state));
        }

        private sealed class CardFlightPlan
        {
            public Dictionary<string, CardZone> Current;
            public bool Animate;
            public bool Reshuffled;
            public readonly List<DiscardGhostFlight> Discards = new List<DiscardGhostFlight>();
            public readonly List<DiscardGhostFlight> Exiles = new List<DiscardGhostFlight>();
            public readonly List<string> DrawInstanceIds = new List<string>();
        }

        private struct DiscardGhostFlight
        {
            public RectTransform Ghost;
            public RectTransform Dock;
        }

        // Phase 2/3 step 1 (runs BEFORE the hand re-lays-out): diff the card zones, and for outgoing cards
        // clone their current visuals into the flight overlay while the slots still show them.
        private CardFlightPlan PrepareCardFlights(CombatState state)
        {
            if (!Application.isPlaying || flightAnimator == null || cardLaneView == null || state == null)
            {
                return null;
            }

            var current = CombatCardZoneSnapshot.Build(state);
            var diff = CardZoneDiff.Compute(hasPreviousZones ? previousZones : EmptyZones, current);
            var plan = new CardFlightPlan { Current = current };

            // Don't stack batches: if a flight set is already in the air, just keep zones primed (no anim).
            if (!diff.HasAny || flightAnimator.IsFlying)
            {
                return plan;
            }

            // Self-heal: a previous flight that was interrupted before its coroutine finished (e.g. the HUD
            // GameObject was deactivated mid-flight) can leave a frozen front-face clone stranded at a hand
            // slot. With no flight currently active, drop any such orphan before capturing new ghosts.
            flightAnimator.PurgeOrphanedGhosts();

            plan.Reshuffled = diff.Reshuffled;

            for (var i = 0; i < diff.Transitions.Count; i++)
            {
                var transition = diff.Transitions[i];
                if (transition.IsDiscard || transition.IsReturnToDraw || transition.IsExile)
                {
                    // Capture now — after cardLaneView.Refresh the card is no longer in the hand.
                    var slot = FindHandSlot(transition.InstanceId);
                    var ghost = slot != null ? flightAnimator.CaptureDiscardGhost(slot.transform as RectTransform) : null;
                    if (ghost != null)
                    {
                        var flight = new DiscardGhostFlight
                        {
                            Ghost = ghost,
                            Dock = transition.IsReturnToDraw ? drawPileDock : transition.IsExile ? exilePileDock : discardPileDock,
                        };

                        if (transition.IsExile)
                        {
                            plan.Exiles.Add(flight);
                        }
                        else
                        {
                            plan.Discards.Add(flight);
                        }
                    }
                }
                else if (transition.To == CardZone.Hand)
                {
                    // Any card entering the hand flies in from the draw pile — covers normal draws, the
                    // opening deal, and post-reshuffle draws (which transition discard -> hand).
                    plan.DrawInstanceIds.Add(transition.InstanceId);
                }
            }

            plan.Animate = plan.Discards.Count > 0 || plan.Exiles.Count > 0 || plan.DrawInstanceIds.Count > 0 || plan.Reshuffled;
            return plan;
        }

        // Phase 2/3 step 2 (runs AFTER the new layout is in place): fly the captured discard ghosts into the
        // pile first, then deal the freshly drawn slots in (hidden until their ghost lands). Discards lead so
        // a turn boundary reads as "clear the hand, then draw the new one".
        private void LaunchCardFlights(CardFlightPlan plan)
        {
            if (plan == null)
            {
                return;
            }

            // Always advance the tracked zones so a transition is never replayed on the next refresh.
            StorePreviousZones(plan.Current);

            if (!plan.Animate)
            {
                return;
            }

            // Discards: left-to-right so the sweep reads cleanly.
            plan.Discards.Sort((a, b) => GetRectX(a.Ghost).CompareTo(GetRectX(b.Ghost)));
            var discardStagger = flightAnimator.DiscardStaggerSeconds;
            for (var i = 0; i < plan.Discards.Count; i++)
            {
                flightAnimator.FlyDiscardGhost(plan.Discards[i].Ghost, plan.Discards[i].Dock, i * discardStagger);
            }

            plan.Exiles.Sort((a, b) => GetRectX(a.Ghost).CompareTo(GetRectX(b.Ghost)));
            var exileStagger = flightAnimator.ExileStaggerSeconds;
            for (var i = 0; i < plan.Exiles.Count; i++)
            {
                flightAnimator.FlyExileGhost(plan.Exiles[i].Ghost, plan.Exiles[i].Dock, i * exileStagger);
            }

            var discardWindow = plan.Discards.Count > 0 ? plan.Discards.Count * discardStagger : 0f;
            var exileWindow = plan.Exiles.Count > 0 ? plan.Exiles.Count * exileStagger : 0f;
            var outgoingWindow = Mathf.Max(discardWindow, exileWindow);

            // Reshuffle cut: discard pile sweeps back into the draw pile after the hand clears, before new draws.
            var reshuffleWindow = 0f;
            if (plan.Reshuffled)
            {
                flightAnimator.PlayReshuffle(discardPileDock, drawPileDock, outgoingWindow);
                reshuffleWindow = flightAnimator.ReshuffleSeconds;
            }

            // Draws begin once discards have cleared and the reshuffle (if any) finishes; fill right-most first.
            var drawBaseDelay = outgoingWindow + reshuffleWindow;
            var drawSlots = new List<HandCardInteraction>();
            for (var i = 0; i < plan.DrawInstanceIds.Count; i++)
            {
                var slot = FindHandSlot(plan.DrawInstanceIds[i]);
                if (slot != null)
                {
                    drawSlots.Add(slot);
                }
            }

            drawSlots.Sort((a, b) => GetSlotScreenX(b).CompareTo(GetSlotScreenX(a)));

            var drawStagger = flightAnimator.DrawStaggerSeconds;
            for (var i = 0; i < drawSlots.Count; i++)
            {
                var slot = drawSlots[i];
                var target = slot.transform as RectTransform;
                var group = GetOrAddCanvasGroup(slot.gameObject);
                // Hide the real slot until the ghost (card back) lands in its place.
                group.alpha = 0f;
                group.blocksRaycasts = false;
                var captured = group;
                flightAnimator.PlayDrawFlight(drawPileDock, target, () => RevealSlot(captured), drawBaseDelay + i * drawStagger);
            }
        }

        private static float GetRectX(RectTransform rect)
        {
            return rect != null ? rect.anchoredPosition.x : 0f;
        }

        private static float GetSlotScreenX(HandCardInteraction slot)
        {
            var rect = slot != null ? slot.transform as RectTransform : null;
            return rect != null ? rect.position.x : 0f;
        }

        private void StorePreviousZones(Dictionary<string, CardZone> current)
        {
            previousZones.Clear();
            if (current != null)
            {
                foreach (var entry in current)
                {
                    previousZones[entry.Key] = entry.Value;
                }
            }

            hasPreviousZones = true;
        }

        private HandCardInteraction FindHandSlot(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId) || cardLaneView == null)
            {
                return null;
            }

            return FindActiveSlot(cardLaneView.MoveCardSlots, instanceId)
                ?? FindActiveSlot(cardLaneView.ActionCardSlots, instanceId);
        }

        private static HandCardInteraction FindActiveSlot(IReadOnlyList<HandCardInteraction> slots, string instanceId)
        {
            if (slots == null)
            {
                return null;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot != null
                    && slot.gameObject.activeInHierarchy
                    && string.Equals(slot.CurrentCardSelectionKey, instanceId, StringComparison.Ordinal))
                {
                    return slot;
                }
            }

            return null;
        }

        private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
        {
            var group = go.GetComponent<CanvasGroup>();
            return group != null ? group : go.AddComponent<CanvasGroup>();
        }

        private static void RevealSlot(CanvasGroup group)
        {
            if (group == null)
            {
                return;
            }

            group.alpha = 1f;
            group.blocksRaycasts = true;
        }

        // Reveal any active hand slot that a deal-in left hidden but whose draw flight never landed (see the
        // call site in Refresh). Only slots that actually carry a CanvasGroup were touched by a flight, so a
        // slot without one is skipped. Callers gate on !IsFlying so a legitimately in-air deal is untouched.
        private void RestoreStuckHiddenHandSlots()
        {
            if (cardLaneView == null)
            {
                return;
            }

            RevealStuckHiddenSlots(cardLaneView.MoveCardSlots);
            RevealStuckHiddenSlots(cardLaneView.ActionCardSlots);
        }

        private static void RevealStuckHiddenSlots(IReadOnlyList<HandCardInteraction> slots)
        {
            if (slots == null)
            {
                return;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.gameObject.activeInHierarchy)
                {
                    continue;
                }

                var group = slot.GetComponent<CanvasGroup>();
                if (group != null && group.alpha < 1f)
                {
                    RevealSlot(group);
                }
            }
        }

        private void ConfigureAuthoredControls()
        {
            ConfigurePileButton(drawPileDock, ref drawPileButton, drawPileClickAction);
            ConfigurePileButton(discardPileDock, ref discardPileButton, discardPileClickAction);
            ConfigurePileButton(exilePileDock, ref exilePileButton, exilePileClickAction);
            ConfigureEndActionButton(latestState);
        }

        private void ResolveReferences()
        {
            cardLaneView = cardLaneView != null ? cardLaneView : GetComponent<GameplayCardLaneView>() ?? GetComponentInChildren<GameplayCardLaneView>(true);
            flightAnimator = flightAnimator != null ? flightAnimator : GetComponent<CardFlightAnimator>() ?? GetComponentInChildren<CardFlightAnimator>(true);
            drawPileDock = drawPileDock != null ? drawPileDock : FindChildRect("DrawPileDock");
            healthDock = healthDock != null ? healthDock : FindChildRect("HealthDock");
            energyDock = energyDock != null ? energyDock : FindChildRect("EnergyDock");
            discardPileDock = discardPileDock != null ? discardPileDock : FindChildRect("DiscardPileDock");
            exilePileDock = exilePileDock != null ? exilePileDock : FindChildRect("ExilePileDock");
            combatControlDock = combatControlDock != null ? combatControlDock : FindChildRect("BottomCombatControlDock");
            statusEffectDockView = statusEffectDockView != null ? statusEffectDockView : ResolveStatusEffectDockView();
            drawCountText = drawCountText != null ? drawCountText : FindDockText(drawPileDock, "Count_TMP");
            healthText = healthText != null ? healthText : FindDockText(healthDock, "Value_TMP");
            energyText = energyText != null ? energyText : FindDockText(energyDock, "Value_TMP");
            discardCountText = discardCountText != null ? discardCountText : FindDockText(discardPileDock, "Count_TMP");
            exileCountText = exileCountText != null ? exileCountText : FindDockText(exilePileDock, "Count_TMP");
            shieldIcon = shieldIcon != null ? shieldIcon : FindChildRect("Shield_Icon");
            shieldValueText = shieldValueText != null ? shieldValueText : FindChildText("Shield_Value_TMP");
            phaseText = phaseText != null ? phaseText : FindDockText(combatControlDock, "BottomCombatPhase_TMP");
            endActionLabelText = endActionLabelText != null ? endActionLabelText : FindDockText(combatControlDock, "BottomCombatEndActionLabel_TMP");
            handCardSelectionOverlayRoot = handCardSelectionOverlayRoot != null ? handCardSelectionOverlayRoot : FindSceneRect("Card Selection Overlay Root");
            handCardSelectionPromptText = handCardSelectionPromptText != null ? handCardSelectionPromptText : FindOverlayText("DescriptionText", "HandCardSelectionPrompt_TMP", "Prompt_TMP", "PromptText_TMP");
            handCardSelectionDimOverlay = handCardSelectionDimOverlay != null ? handCardSelectionDimOverlay : FindOverlayRect("HandCardSelectionDimOverlay", "Deck Pile List Overlay Panel");
            drawPileButton = drawPileButton != null ? drawPileButton : drawPileDock != null ? drawPileDock.GetComponent<Button>() : null;
            discardPileButton = discardPileButton != null ? discardPileButton : discardPileDock != null ? discardPileDock.GetComponent<Button>() : null;
            exilePileButton = exilePileButton != null ? exilePileButton : exilePileDock != null ? exilePileDock.GetComponent<Button>() : null;
            endActionButton = endActionButton != null ? endActionButton : FindChildButton("BottomCombatEndActionButton") ?? (combatControlDock != null ? combatControlDock.GetComponentInChildren<Button>(true) : null);
            handCardSelectionConfirmButton = handCardSelectionConfirmButton != null ? handCardSelectionConfirmButton : FindFirstOverlayButton("DecisionButton", "DecisionActionButton", "DscisionActionButton", "HandCardSelectionConfirmButton");
            handCardSelectionCancelButton = handCardSelectionCancelButton != null ? handCardSelectionCancelButton : FindFirstOverlayButton("CancelButton", "CancelActionButton", "HandCardSelectionCancelButton");
        }

        private void ConfigureHandCardSelectionControls(ICombatCardHudHost controller)
        {

            var model = controller == null ? HandCardSelectionPanelModel.Empty : controller.HandCardSelectionPanel;
            var active = model.IsActive;
            if (handCardSelectionOverlayRoot != null)
            {
                handCardSelectionOverlayRoot.gameObject.SetActive(active);
            }

            ConfigureOverlayBackgroundRaycasts();

            if (handCardSelectionDimOverlay != null)
            {
                handCardSelectionDimOverlay.gameObject.SetActive(active);
            }

            if (handCardSelectionPromptText != null)
            {
                handCardSelectionPromptText.gameObject.SetActive(active);
                KoreanFontApplier?.Invoke(handCardSelectionPromptText);
                handCardSelectionPromptText.text = active
                    ? $"{model.PromptText} ({model.SelectedCount}/{model.MaxSelectCount})"
                    : string.Empty;
            }

            ConfigureSelectionButton(
                handCardSelectionConfirmButton,
                active,
                model.CanConfirm,
                "\uC0AC\uC6A9",
                () => controller?.ConfirmHandCardSelection());
            ConfigureSelectionButton(
                handCardSelectionCancelButton,
                active,
                true,
                "\uCDE8\uC18C",
                () => controller?.CancelHandCardSelection());
        }

        private void ConfigureOverlayBackgroundRaycasts()
        {
            SetGraphicRaycastTarget(handCardSelectionOverlayRoot, false);
            SetGraphicRaycastTarget(handCardSelectionDimOverlay, false);
            if (handCardSelectionPromptText != null)
            {
                handCardSelectionPromptText.raycastTarget = false;
            }
        }

        private static void SetGraphicRaycastTarget(RectTransform rect, bool raycastTarget)
        {
            var graphic = rect != null ? rect.GetComponent<Graphic>() : null;
            if (graphic != null)
            {
                graphic.raycastTarget = raycastTarget;
            }
        }


        private static void ConfigureSelectionButton(Button button, bool visible, bool interactable, string label, Action action)
        {
            if (button == null)
            {
                return;
            }

            button.gameObject.SetActive(visible);
            button.interactable = visible && interactable;
            button.onClick.RemoveAllListeners();
            if (action != null)
            {
                button.onClick.AddListener(() => action.Invoke());
            }

            var text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = label;
            }
        }

        // P6 T3 (frame pass): the bottom docks shipped as flat, unframed squares — 체력/기력 as plain navy
        // rectangles, the pile docks as bare icon art — while every popup and sidebar callout had moved to the
        // procedural plate look. Frames only; the icons themselves are a separate, still-open decision.
        //
        // Applied at runtime rather than in the scene: driving UiProceduralPanel from the editor serialises a
        // material instance. Once — Refresh runs every frame via GameplayHudBridge.
        //
        private void SkinDocksOnce()
        {
            if (docksSkinned || !Application.isPlaying)
            {
                return;
            }

            docksSkinned = true;

            // Plain colour plates: skin the dock's own graphic in place.
            SkinDockGraphic(healthDock);
            SkinDockGraphic(energyDock);

            // Pile docks: the icon art fills the whole dock, so an in-place skin would be hidden behind it.
            // Frame them with a slightly oversized plate dropped in behind the art instead.
            AddDockFrame(drawPileDock);
            AddDockFrame(discardPileDock);
            AddDockFrame(exilePileDock);

            SkinEndActionButton();
        }

        // 종료 버튼을 체력·기력 도크와 같은 문법으로 세운다: **플레이트는 도크가 지고, 버튼은
        // 아이콘만 진다.** 라벨("이동 종료"/"턴 종료"/"진행 대기")은 버튼 밖으로 나와 "체력"·"기력"
        // 라벨과 같은 줄에 선다.
        //
        // 왜 코드에서 하는가: 이 메서드는 원래도 도크 플레이트를 런타임에 지우고 있었다(구조를
        // 런타임에 정규화하는 것이 이 뷰의 기존 패턴이다). 반대 방향으로 뒤집는 것이므로 같은 자리다.
        //
        // ⚠️ 핵심은 `endActionSkin`이 가리키는 대상을 바꾸는 것 하나다. ApplyEndActionButtonEmphasis가
        //    그 필드로 강조(주황 fill/border 통째 교체)를 구동하므로, 참조만 도크 플레이트로 돌리면
        //    강조 로직 본문은 손대지 않아도 그대로 맞다. 버튼 플레이트를 투명하게 만드는 방식으로는
        //    안 된다 — 갱신마다 emphasis가 되돌린다(2026-07-26 프로토타입에서 실제로 밟았다).
        private void SkinEndActionButton()
        {
            if (combatControlDock == null || endActionButton == null)
            {
                return;
            }

            // 플레이트를 도크로. 강조도 이제 이 스킨이 진다.
            //
            // **체력·기력 도크와 같은 방식**(도크 자체 그래픽에 스킨)을 쓴다 = 100×100.
            // 한때 더미 도크들처럼 AddDockFrame(오버사이즈 112×112 자식)을 썼다가 되돌렸다 —
            // 사용자가 더미가 아니라 체력·기력 박스에 크기를 맞추기로 정했다(2026-07-26).
            // 두 방식이 공존하는 것은 원래 그렇다: 더미 도크는 아이콘 아트가 도크를 꽉 채워
            // 제자리 스킨이 가려지므로 프레임을 뒤에 깔고, 체력·기력은 그럴 필요가 없다.
            var dockImage = combatControlDock.GetComponent<Image>();
            if (dockImage == null)
            {
                return;
            }

            endActionSkin = ApplyDockSkin(dockImage);
            dockImage.color = Color.white;

            // 버튼은 아이콘만 남긴다 — 자기 플레이트를 그리던 UiProceduralPanel을 떼어낸다.
            // Configure(clear)가 아니라 컴포넌트 비활성인 이유는, 같은 Image를 아이콘 스프라이트가
            // 쓰기 때문이다. 절차 머터리얼이 살아 있으면 스프라이트를 덮어 그린다.
            var buttonImage = endActionButton.targetGraphic as Image ?? endActionButton.GetComponent<Image>();
            if (buttonImage != null)
            {
                var buttonPanel = buttonImage.GetComponent<UiProceduralPanel>();
                if (buttonPanel != null)
                {
                    buttonPanel.enabled = false;
                    buttonImage.material = null;
                }

                endActionTargetGraphic = buttonImage;
                endActionBaseGraphicColor = Color.white;
                hasEndActionBaseGraphicColor = true;

                // 호버 글로우는 자기 크기를 버튼 rect에서 잡는데, 버튼이 아이콘 크기로 줄면
                // 글로우가 버튼보다 커져 도크 밖으로 샌다. 아이콘 방식에서는 도크 플레이트가
                // 이미 테두리를 지므로 글로우를 뗀다.
                var glow = buttonImage.GetComponent<UiButtonHoverGlow>();
                if (glow != null)
                {
                    glow.enabled = false;
                }
            }
        }

        // ⚠️ 배치는 여기 없다 — CardLane.prefab에 저작돼 있다(cs:735).
        //
        // 한때 이 자리에 LayoutEndActionAsIcon()이 있었다. 버튼 rect·라벨 부모/서식·
        // 페이즈 텍스트 비활성을 전부 런타임에 잡았는데, 그러면 **에디터에서 프리팹을 열었을 때
        // 실제와 다른 모습이 보인다**. 도크 스킨(색·플레이트)이 런타임인 것은 절차 머터리얼이라
        // 어쩔 수 없지만, 크기·부모·폰트는 디자이너가 보고 만져야 하는 값이라 프리팹으로 옮겼다.
        // 저작값: 버튼 92×96 중앙, 라벨은 도크 자식 126×40 @(0,-21.5) fs30, 페이즈 텍스트 비활성.

        private static void SkinDockGraphic(RectTransform dock)
        {
            var image = dock != null ? dock.GetComponent<Image>() : null;
            if (image == null || image.sprite != null)
            {
                return; // an authored sprite plate keeps its art
            }

            ApplyDockSkin(image);
        }

        private static void AddDockFrame(RectTransform dock)
        {
            if (dock == null)
            {
                return;
            }

            var existing = dock.Find(DockFrameName) as RectTransform;
            if (existing == null)
            {
                var go = new GameObject(DockFrameName, typeof(RectTransform));
                existing = (RectTransform)go.transform;
                existing.SetParent(dock, false);
            }

            existing.anchorMin = Vector2.zero;
            existing.anchorMax = Vector2.one;
            existing.pivot = new Vector2(0.5f, 0.5f);
            existing.offsetMin = new Vector2(-DockFramePad, -DockFramePad);
            existing.offsetMax = new Vector2(DockFramePad, DockFramePad);
            existing.localScale = Vector3.one;
            existing.SetAsFirstSibling(); // behind the pile art and its count/label

            // ignoreLayout so a LayoutGroup on the dock row can't claim the oversized frame as a cell.
            var layoutElement = existing.GetComponent<LayoutElement>() ?? existing.gameObject.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            var image = existing.GetComponent<Image>() ?? existing.gameObject.AddComponent<Image>();
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.raycastTarget = false; // the dock's own Image stays the click target
            ApplyDockSkin(image);
        }

        private static UiProceduralPanel ApplyDockSkin(Image image)
        {
            var skin = image.GetComponent<UiProceduralPanel>() ?? image.gameObject.AddComponent<UiProceduralPanel>();
            skin.Configure(DockFillColor, DockBorderColor, 1.5f, 12f);
            skin.ConfigureTexture(0.16f, DockHighlightColor, 0.55f, 2.2f);
            return skin;
        }

        private void ConfigureEndActionButton(CombatState state)
        {
            if (endActionButton == null)
            {
                return;
            }

            endActionButton.onClick.RemoveAllListeners();
            if (endActionClickAction != null)
            {
                endActionButton.onClick.AddListener(() => endActionClickAction.Invoke());
            }

            var canEnd = state != null
                && !state.IsTerminal
                && endActionClickAction != null
                && (state.Phase == CombatPhase.PlayerMovement || state.Phase == CombatPhase.PlayerAction);
            endActionButton.interactable = canEnd;
            ApplyEndActionButtonEmphasis(state, canEnd);
            SetText(endActionLabelText, EndActionLabel(state));
        }

        private void ApplyEndActionButtonEmphasis(CombatState state, bool canEnd)
        {
            if (endActionButton == null)
            {
                return;
            }

            if (!hasEndActionBaseColors)
            {
                endActionBaseColors = endActionButton.colors;
                hasEndActionBaseColors = true;
            }

            endActionTargetGraphic = endActionButton.targetGraphic != null
                ? endActionButton.targetGraphic
                : endActionButton.GetComponent<Graphic>();
            if (!hasEndActionBaseGraphicColor && endActionTargetGraphic != null)
            {
                endActionBaseGraphicColor = endActionTargetGraphic.color;
                hasEndActionBaseGraphicColor = true;
            }

            var colors = endActionBaseColors;
            var graphicColor = hasEndActionBaseGraphicColor ? endActionBaseGraphicColor : Color.white;
            var emphasize = canEnd && ShouldEmphasizeEndAction(state);

            // Skinned path (P6 T3): the plate's own fill carries the emphasis, so nothing gets multiplied down.
            // The unskinned path below is untouched and still applies.
            //
            // 2026-07-26: endActionSkin이 이제 **도크 플레이트**를 가리키고, targetGraphic은 플레이트가
            // 아니라 아이콘 스프라이트다. 그래도 흰색 고정은 **그대로 필요하다** — 이유가 바뀌었을 뿐이다.
            //
            // ⚠️ 한 번 빼 봤다가 되돌렸다. 상태 디밍을 덮어쓸까 걱정했는데 그렇지 않다:
            //    UGUI의 ColorTint는 CrossFadeColor(CanvasRenderer 채널)로 들어가고 Image.color는
            //    별개 레이어라 둘이 곱해진다. 흰색을 안 넣으면 저작된 남색이 스프라이트를 곱해
            //    아이콘이 통째로 어두워지고, 디밍은 그것과 무관하게 계속 작동한다.
            if (endActionSkin != null)
            {
                endActionSkin.Configure(
                    emphasize ? EndActionEmphasisFill : DockFillColor,
                    emphasize ? EndActionEmphasisBorder : DockBorderColor,
                    1.5f,
                    12f);
                endActionSkin.ConfigureTexture(0.16f, DockHighlightColor, emphasize ? 0.8f : 0.55f, 2.2f);
                endActionButton.colors = colors;
                if (endActionTargetGraphic != null)
                {
                    endActionTargetGraphic.color = Color.white;
                }

                return;
            }

            if (emphasize)
            {
                // Turn the whole button a bold, unmistakable colour. Drive the
                // target graphic directly and keep the colour block neutral so the
                // emphasis shows identically across every Button transition mode
                // (ColorTint multiplies graphic*state, so a coloured state block
                // here would darken/shift the result; non-ColorTint modes ignore it
                // entirely). White states keep the emphasis colour stable on
                // hover/press instead of washing it out.
                var active = new Color(1f, 0.55f, 0.05f, 1f);
                colors.normalColor = Color.white;
                colors.highlightedColor = Color.white;
                colors.selectedColor = Color.white;
                colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
                colors.disabledColor = Color.white;
                colors.colorMultiplier = 1f;
                graphicColor = active;
            }

            endActionButton.colors = colors;
            if (endActionTargetGraphic != null)
            {
                endActionTargetGraphic.color = graphicColor;
            }
        }

        private static bool ShouldEmphasizeEndAction(CombatState state)
        {
            if (state == null || state.Phase != CombatPhase.PlayerAction)
            {
                return false;
            }

            return !state.GetCombatCards().Any(card =>
                !card.IsDiscarded &&
                card.Kind != CombatCardKind.Move &&
                card.IsUsable &&
                card.Cost <= state.ActionCostRemaining);
        }

        private void RefreshShield(CombatState state, PlayerStateSnapshot snapshot)
        {
            var block = state == null ? 0 : snapshot.Block;
            var show = block > 0;

            if (shieldIcon != null)
            {
                shieldIcon.gameObject.SetActive(show);
            }

            if (shieldValueText != null)
            {
                shieldValueText.gameObject.SetActive(show);
                if (show)
                {
                    shieldValueText.text = block.ToString();
                }
            }
        }

        private static string PhaseLabel(CombatPhase phase)
        {
            switch (phase)
            {
                case CombatPhase.PlayerMovement: return "\uC774\uB3D9 \uD398\uC774\uC988";
                case CombatPhase.MonsterMovement: return "\uBAAC\uC2A4\uD130 \uC774\uB3D9";
                case CombatPhase.PlayerAction: return "\uC561\uC158 \uD398\uC774\uC988";
                case CombatPhase.MonsterAction: return "\uBAAC\uC2A4\uD130 \uD589\uB3D9";
                default: return phase.ToString();
            }
        }

        private static string EndActionLabel(CombatState state)
        {
            if (state == null)
            {
                return "\uB300\uAE30";
            }

            return state.Phase == CombatPhase.PlayerMovement ? "\uC774\uB3D9 \uC885\uB8CC"
                : state.Phase == CombatPhase.PlayerAction ? "\uD134 \uC885\uB8CC"
                : "\uC9C4\uD589 \uB300\uAE30";
        }

        private static void ConfigurePileButton(RectTransform dock, ref Button button, Action action)
        {
            if (dock == null)
            {
                return;
            }

            var image = dock.GetComponent<Image>();
            if (image == null)
            {
                image = dock.gameObject.AddComponent<Image>();
                image.color = InvisibleHitColor;
            }
            image.raycastTarget = action != null;

            button = button != null ? button : dock.GetComponent<Button>() ?? dock.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.RemoveAllListeners();
            if (action != null)
            {
                button.onClick.AddListener(() => action.Invoke());
            }
            button.interactable = action != null;
        }

        private RectTransform FindSceneRect(string objectName)
        {
            var canvas = GetComponentInParent<Canvas>(includeInactive: true);
            if (canvas != null)
            {
                var rect = canvas.GetComponentsInChildren<RectTransform>(includeInactive: true)
                    .FirstOrDefault(candidate => candidate.name == objectName);
                if (rect != null)
                {
                    return rect;
                }
            }

            return FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(candidate => candidate.name == objectName);
        }

        private RectTransform FindOverlayRect(params string[] objectNames)
        {
            if (objectNames == null)
            {
                return null;
            }

            var roots = handCardSelectionOverlayRoot != null
                ? handCardSelectionOverlayRoot.GetComponentsInChildren<RectTransform>(includeInactive: true)
                : GetComponentsInChildren<RectTransform>(includeInactive: true);
            foreach (var objectName in objectNames)
            {
                var rect = roots.FirstOrDefault(candidate => candidate.name == objectName);
                if (rect != null)
                {
                    return rect;
                }
            }

            return null;
        }

        private TMP_Text FindOverlayText(params string[] objectNames)
        {
            if (objectNames == null)
            {
                return null;
            }

            var roots = handCardSelectionOverlayRoot != null
                ? handCardSelectionOverlayRoot.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                : GetComponentsInChildren<TMP_Text>(includeInactive: true);
            foreach (var objectName in objectNames)
            {
                var text = roots.FirstOrDefault(candidate => candidate.name == objectName);
                if (text != null)
                {
                    return text;
                }
            }

            return null;
        }

        private Button FindFirstOverlayButton(params string[] objectNames)
        {
            if (objectNames == null)
            {
                return null;
            }

            var buttons = handCardSelectionOverlayRoot != null
                ? handCardSelectionOverlayRoot.GetComponentsInChildren<Button>(includeInactive: true)
                : GetComponentsInChildren<Button>(includeInactive: true);
            foreach (var objectName in objectNames)
            {
                var button = buttons.FirstOrDefault(candidate => candidate.name == objectName);
                if (button != null)
                {
                    return button;
                }
            }

            return null;
        }
        private RectTransform FindChildRect(string objectName)
        {
            return GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == objectName);
        }

        private CardLaneStatusEffectDockView ResolveStatusEffectDockView()
        {
            var dock = FindChildRect("StatusEffectDock");
            if (dock == null)
            {
                return null;
            }

            return dock.GetComponent<CardLaneStatusEffectDockView>()
                ?? dock.gameObject.AddComponent<CardLaneStatusEffectDockView>();
        }

        private TMP_Text FindChildText(string objectName)
        {
            return GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.name == objectName);
        }

        private Button FindChildButton(string objectName)
        {
            return GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(button => button.name == objectName);
        }

        private static TMP_Text FindDockText(RectTransform dock, string objectName)
        {
            return dock == null
                ? null
                : dock.GetComponentsInChildren<TMP_Text>(includeInactive: true).FirstOrDefault(text => text.name == objectName);
        }

        private void SetText(TMP_Text text, string value)
        {
            if (text == null)
            {
                return;
            }

            KoreanFontApplier?.Invoke(text);
            text.text = value ?? string.Empty;
        }
    }
}
