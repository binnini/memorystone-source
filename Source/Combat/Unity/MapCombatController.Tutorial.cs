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
        public void SetTutorialScript(TutorialScriptAsset tutorialScript)
        {
            if (tutorialScript == null)
            {
                ResolveTutorialDirector()?.SetScript(null);
                return;
            }

            ResolveTutorialDirector(createIfMissing: true)?.SetScript(tutorialScript);
        }

        public void BeginTutorial()
        {
            var director = ResolveTutorialDirector();
            if (director != null && !tutorialDirectorStepHooked)
            {
                director.StepPresented += OnTutorialStepPresented;
                tutorialDirectorStepHooked = true;
            }

            director?.Begin();
            RefreshMapVisibilityAndHighlights();
        }

        // When a step asks to highlight a goal hex (e.g. the memory stone), briefly pan the gameplay
        // camera there and back so the tutorial can point it out without taking control away for long.
        private void OnTutorialStepPresented(TutorialStep step)
        {
            if (step == null)
            {
                return;
            }

            EnsureTutorialHandCards(step.InjectHandCardIds);

            // Briefly lift the fog around a called-out objective (tile AND monster marker) while the camera
            // points at it — e.g. so the boss the tutorial is describing is actually visible — then let it
            // fade back to fog once the beat ends.
            var reveal = step.RevealAreaCoord;
            if (reveal != null && reveal.Enabled)
            {
                PlayTutorialAreaReveal(reveal.ToHexCoord(), step.RevealAreaRadius, step.CameraFocusHoldSeconds);
            }

            var focus = step.CameraFocusCoord;
            if (focus != null && focus.Enabled)
            {
                PlayTutorialCameraFocus(focus.ToHexCoord(), step.CameraFocusHoldSeconds);
                // The spotlight would darken the very hex the camera is pointing at — lift it for the pan out,
                // the dwell and the pan back (pan length mirrors CombatCameraController's tutorial focus).
                const float panSeconds = 1.1f;
                ResolveTutorialDirector()?.SuppressSpotlightFor(panSeconds * 2f + Mathf.Max(0f, step.CameraFocusHoldSeconds));
            }
        }

        // Guarantees scripted cards are in the player's hand for a step (e.g. a two-move puzzle that needs
        // both a 3-tile and a 1-tile move), since the opening hand is otherwise drawn at random.
        private void EnsureTutorialHandCards(IReadOnlyList<string> cardIds)
        {
            if (!Application.isPlaying || State == null || cardIds == null || cardIds.Count == 0)
            {
                return;
            }

            var injectedAny = false;
            foreach (var cardId in cardIds)
            {
                if (!string.IsNullOrWhiteSpace(cardId) && State.DebugInjectCardIntoHand(cardId.Trim()))
                {
                    injectedAny = true;
                }
            }

            if (injectedAny)
            {
                RefreshHudOnly();
            }
        }

        public void PlayTutorialCameraFocus(HexCoord coord, float holdSeconds)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            CameraController.StopTutorialCameraFocusRoutine();

            if (!TryGetTileWorldPosition(coord, out var tileWorld))
            {
                return;
            }

            var binder = ResolveCinemachineCombatCameraBinder();
            if (binder == null)
            {
                return;
            }

            CameraController.PlayTutorialCameraFocusCinemachine(binder, tileWorld, holdSeconds);
        }

        // Accompanies a tutorial camera focus with a temporary "here it is" fog lift over the objective's
        // surroundings, so a distant target the player has not walked up to (e.g. the boss) is shown — tile
        // and monster marker both — while the camera dwells on it, then fades back to fog. Uses the
        // presentation-only forced-reveal set the intro/victory cinematics use; it never mutates CombatState
        // visibility, so clearing it simply restores the real fog.
        private Coroutine tutorialAreaRevealRoutine;

        private void PlayTutorialAreaReveal(HexCoord center, int radius, float holdSeconds)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (tutorialAreaRevealRoutine != null)
            {
                StopCoroutine(tutorialAreaRevealRoutine);
                tutorialAreaRevealRoutine = null;
            }

            tutorialAreaRevealRoutine = StartCoroutine(TutorialAreaRevealRoutine(center, radius, holdSeconds));
        }

        private IEnumerator TutorialAreaRevealRoutine(HexCoord center, int radius, float holdSeconds)
        {
            cinematicForcedRevealCells = new HashSet<HexCoord>(HexArea.CellsWithin(center, System.Math.Max(0, radius)));
            RefreshMapVisibilityAndHighlights();
            UpdateEnemyMarker();

            // Hold the reveal for the camera's pan-out plus its dwell so the target is visible exactly while
            // the camera is on it. panSeconds mirrors CombatCameraController's tutorial-focus pan duration.
            const float panSeconds = 1.1f;
            yield return new WaitForSecondsRealtime(panSeconds + Mathf.Max(0f, holdSeconds));

            cinematicForcedRevealCells = null;
            RefreshMapVisibilityAndHighlights();
            UpdateEnemyMarker();
            tutorialAreaRevealRoutine = null;
        }

        public bool CanSelectCardForTutorial(CombatCardSnapshot card, out string reason)
        {
            var director = ResolveTutorialDirector();
            if (director == null || !director.IsActive)
            {
                reason = string.Empty;
                return true;
            }

            return director.CanSelectCard(card, out reason);
        }

        public bool CanSelectTargetForTutorial(HexCoord coord, out string reason)
        {
            var director = ResolveTutorialDirector();
            if (director == null || !director.IsActive)
            {
                reason = string.Empty;
                return true;
            }

            // Monster-attack steps accept the tile a living monster currently occupies (it may have moved
            // during its movement phase), instead of a fixed coord.
            var step = director.CurrentStep;
            if (step != null && step.Requirement != null && step.Requirement.RequiredTargetIsMonster)
            {
                if (step.AdvanceMode == TutorialAdvanceMode.TargetSelected && HasLivingMonsterAt(coord))
                {
                    reason = string.Empty;
                    return true;
                }

                reason = step.BlockedFeedback;
                return false;
            }

            return director.CanSelectTarget(coord, out reason);
        }

        private bool HasLivingMonsterAt(HexCoord coord)
        {
            if (State == null)
            {
                return false;
            }

            foreach (var monster in State.Monsters)
            {
                if (!monster.IsDead && monster.Coord.Q == coord.Q && monster.Coord.R == coord.R)
                {
                    return true;
                }
            }

            return false;
        }

        public bool CanPressTutorialButton(TutorialButtonId button, out string reason)
        {
            return CanPressTutorialButton(button, -1, out reason);
        }

        public bool CanPressTutorialButton(TutorialButtonId button, int selectedHandCardCount, out string reason)
        {
            var director = ResolveTutorialDirector();
            if (director == null || !director.IsActive)
            {
                reason = string.Empty;
                return true;
            }

            return director.CanPressButton(button, selectedHandCardCount, out reason);
        }

        public bool CanSelectRewardForTutorial(string cardId, out string reason)
        {
            var director = ResolveTutorialDirector();
            if (director == null || !director.IsActive)
            {
                reason = string.Empty;
                return true;
            }

            return director.CanSelectReward(cardId, out reason);
        }

        public void ShowTutorialBlockedFeedback(string reason = null)
        {
            var director = ResolveTutorialDirector();
            var message = string.IsNullOrWhiteSpace(reason)
                ? "\uD29C\uD1A0\uB9AC\uC5BC \uC21C\uC11C\uC5D0 \uB9DE\uB294 \uC870\uC791\uC744 \uBA3C\uC800 \uC9C4\uD589\uD558\uC138\uC694."
                : reason;
            LastInputMessage = message;
            director?.ShowBlockedFeedback(message);
            RequestInvalidInputAudioCue(message);
            RefreshHudOnly();
        }

        public void NotifyTutorialCardSelected(CombatCardSnapshot card)
        {
            ResolveTutorialDirector()?.NotifyCardSelected(card);
            RefreshMapVisibilityAndHighlights();
        }

        public void NotifyTutorialTargetSelected(HexCoord coord)
        {
            var director = ResolveTutorialDirector();
            if (director != null && director.IsActive)
            {
                var step = director.CurrentStep;
                if (step != null && step.Requirement != null && step.Requirement.RequiredTargetIsMonster
                    && step.AdvanceMode == TutorialAdvanceMode.TargetSelected && HasLivingMonsterAt(coord))
                {
                    director.Advance();
                    RefreshMapVisibilityAndHighlights();
                    return;
                }
            }

            director?.NotifyTargetSelected(coord);
            RefreshMapVisibilityAndHighlights();
        }

        public void NotifyTutorialButtonPressed(TutorialButtonId button)
        {
            ResolveTutorialDirector()?.NotifyButtonPressed(button);
            RefreshMapVisibilityAndHighlights();
        }

        // The card the player just took from a reward pick. A Card highlight authored as "$reward" resolves to
        // it, so a "it went straight into your hand" step can point at the actual card without knowing its id.
        private string lastTutorialRewardCardId;
        private const string TutorialRewardCardToken = "$reward";

        public void NotifyTutorialRewardSelected(string cardId)
        {
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                lastTutorialRewardCardId = cardId.Trim();
            }

            ResolveTutorialDirector()?.NotifyRewardSelected(cardId);
            RefreshMapVisibilityAndHighlights();
        }

        // Hover/UI-event notifications for tutorial steps that complete on hovering a tooltip target
        // (monster/field object/map object) or opening a sidebar panel. Called from the per-frame
        // hover updates and the sidebar panel routing; the director ignores them unless the active
        // step's advance mode and required target match.
        public void NotifyTutorialHover(string targetId)
        {
            ResolveTutorialDirector()?.NotifyHoverCompleted(targetId);
        }

        public void NotifyTutorialCombatEvent(string eventId)
        {
            ResolveTutorialDirector()?.NotifyCombatEvent(eventId);
        }

        public void NotifyTutorialHoverEnded(string targetId)
        {
            ResolveTutorialDirector()?.NotifyHoverEnded(targetId);
        }

        public void NotifyTutorialCardHover(CombatCardSnapshot card)
        {
            ResolveTutorialDirector()?.NotifyCardHover(card);
        }

        // Card id the current tutorial step wants highlighted in hand (TargetType.Card), or null. The card
        // lane reads this to glow the matching card.
        public string TutorialHighlightCardId
        {
            get
            {
                var director = ResolveTutorialDirector();
                if (director == null || !director.IsActive)
                {
                    return null;
                }

                var highlight = director.CurrentStep?.Highlight;
                if (highlight == null || highlight.TargetType != TutorialHighlightTargetType.Card)
                {
                    return null;
                }

                return string.Equals(highlight.TargetId?.Trim(), TutorialRewardCardToken, System.StringComparison.Ordinal)
                    ? lastTutorialRewardCardId
                    : highlight.TargetId;
            }
        }

        // UI element id the current tutorial step wants highlighted (TargetType.UiElement), or null.
        // TutorialUiGlow polls this each frame to glow the matching HUD button. Mirrors TutorialHighlightCardId
        // above so step transitions, tutorial end (IsActive=false), and runtime-created overlay buttons are all
        // handled by the same polling contract.
        public string TutorialHighlightUiTargetId
        {
            get
            {
                var director = ResolveTutorialDirector();
                if (director != null && director.IsActive)
                {
                    var highlight = director.CurrentStep?.Highlight;
                    return highlight != null && highlight.TargetType == TutorialHighlightTargetType.UiElement
                        ? highlight.TargetId
                        : null;
                }

                // No scripted tutorial running (e.g. Stage1+): fall back to a contextual gameplay hint so the
                // same button glow gently guides the player without any input gating.
                return ComputeContextualUiHintTargetId();
            }
        }

        [Tooltip("Outside a scripted tutorial, glow the end-phase button when the player has nothing playable left this phase (contextual hint for Stage1+). Turn off to disable all non-tutorial guidance glow.")]
        [SerializeField] private bool enableContextualHints = true;

        // Contextual, non-scripted UI hint for stages that carry no TutorialScript. When the player has nothing
        // left to do in the current phase (no affordable, usable card of the relevant kind), point the glow at the
        // end-phase button — the clearest "what do I do now?" moment. Mirrors BottomCardHudView.ShouldEmphasizeEnd
        // so the glow reinforces the button's existing colour emphasis. Both ids resolve to the same end button.
        private string ComputeContextualUiHintTargetId()
        {
            if (!enableContextualHints || State == null || IsSequencePlaying || State.IsTerminal)
            {
                return null;
            }

            switch (State.Phase)
            {
                case CombatPhase.PlayerAction:
                    return HasAffordableUsableCard(kind => kind != CombatCardKind.Move) ? null : "end_turn_button";
                case CombatPhase.PlayerMovement:
                    return HasAffordableUsableCard(kind => kind == CombatCardKind.Move) ? null : "end_move_phase_button";
                default:
                    return null;
            }
        }

        private bool HasAffordableUsableCard(System.Func<CombatCardKind, bool> kindFilter)
        {
            var remaining = State.ActionCostRemaining;
            foreach (var card in State.GetCombatCards())
            {
                if (!card.IsDiscarded && card.IsUsable && kindFilter(card.Kind) && card.Cost <= remaining)
                {
                    return true;
                }
            }

            return false;
        }

        private bool tutorialBossSightedFired;

        // Fires the 'boss.sighted' tutorial event the first time a tutorial-boss monster becomes
        // visible (Revealed) to the player, so a tutorial step gated on that trigger can start.
        private void CheckTutorialBossSighted()
        {
            if (tutorialBossSightedFired || State == null)
            {
                return;
            }

            var director = ResolveTutorialDirector();
            if (director == null)
            {
                return;
            }

            foreach (var monster in State.Monsters)
            {
                if (monster.IsDead || !IsTutorialBossMonster(monster.Id, monster.SpawnRole))
                {
                    continue;
                }

                if (State.GetVisibility(monster.Coord) != HexCellVisibility.Revealed)
                {
                    continue;
                }

                tutorialBossSightedFired = true;
                NotifyTutorialCombatEvent("boss.sighted");
                break;
            }
        }

        private bool IsTutorialBossMonster(string monsterId, string spawnRole)
        {
            // An explicit inspector id pins the boss exactly; otherwise fall back to the spawn role
            // (authoring marks the boss with role "boss") or an id that mentions "boss".
            if (!string.IsNullOrWhiteSpace(tutorialBossMonsterId))
            {
                return string.Equals(monsterId?.Trim(), tutorialBossMonsterId.Trim(), System.StringComparison.OrdinalIgnoreCase);
            }

            if (IsBossRole(spawnRole))
            {
                return true;
            }

            return !string.IsNullOrEmpty(monsterId)
                && monsterId.IndexOf("boss", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private TutorialDirector ResolveTutorialDirector(bool createIfMissing = false)
        {
            if (tutorialDirector == null)
            {
                tutorialDirector = GetComponent<TutorialDirector>() ?? GetComponentInChildren<TutorialDirector>(true);
                if (tutorialDirector == null && createIfMissing)
                {
                    tutorialDirector = gameObject.AddComponent<TutorialDirector>();
                }
            }

            if (tutorialDirector != null)
            {
                // Inject the shared tutorial-label font applier so the Tutorial assembly need not depend on
                // the Combat.Unity TooltipFontProvider. Idempotent; covers both serialized and created directors.
                tutorialDirector.LabelFontApplier ??= TooltipFontProvider.Apply;
                // Same seam for the spotlight: the host resolves where a step's target is on screen.
                tutorialDirector.FocusRectProvider ??= ResolveTutorialFocusScreenRect;
            }

            return tutorialDirector;
        }

        // ---- tutorial focus (spotlight hole / panel anchor) -------------------------------------------------

        private TutorialUiGlow tutorialUiGlowForFocus;
        private GameplayCardLaneView cardLaneViewForFocus;

        // Where a step's Highlight / CalloutTarget is on screen and how the spotlight should treat it, or null
        // when there is nothing to point at (unknown id, monster dead, popup closed, camera missing). All eight
        // TargetTypes resolve here — the spotlight, panel placement and callout ring share this one answer.
        //   UiElement/StatusIcon/RewardCard → rounded-rect hole over the RectTransform.
        //   Card → no hole: the slot is lifted above the dim instead (see ElevateTutorialCard), so the card's
        //          fan tilt and hover/draw motion are cut out exactly, with no bounding-box slop or lag.
        //   Tile/FieldObject → no hole, lighter dim: the hex already carries the gold tutorial-tile overlay.
        //   Monster → elliptical hole from the model's rendered bounds (falls back to a hex-sized rect).
        // Card elevation is a side effect of the *Highlight* only (isPrimary). The CalloutTarget is resolved
        // through the same table for its ring rect but must leave the elevation alone: 「방어의 기초」 step pairs
        // a Card highlight with an energy_bar callout, and resolving the callout as if it were the highlight
        // dropped the lifted card again every LateUpdate, so the card sat under the input-blocking dim.
        private TutorialFocus? ResolveTutorialFocusScreenRect(TutorialHighlight highlight, bool isPrimary)
        {
            if (highlight == null)
            {
                if (isPrimary)
                {
                    ElevateTutorialCard(null);
                }

                return null;
            }

            if (isPrimary && highlight.TargetType != TutorialHighlightTargetType.Card)
            {
                ElevateTutorialCard(null);
            }

            switch (highlight.TargetType)
            {
                case TutorialHighlightTargetType.UiElement:
                    return RectFocus(ScreenRectOf(ResolveTutorialUiTargetRect(highlight.TargetId)), "ui:" + highlight.TargetId);
                case TutorialHighlightTargetType.StatusIcon:
                    return RectFocus(ScreenRectOf(ResolveTutorialUiTargetRect("player_status_panel")), "ui:player_status_panel");
                case TutorialHighlightTargetType.Card:
                {
                    var slot = ResolveTutorialCardLaneView()?.TutorialCueSlotRect;
                    if (isPrimary)
                    {
                        ElevateTutorialCard(slot);
                    }

                    var rect = ScreenRectOf(slot);
                    return rect.HasValue
                        ? new TutorialFocus(rect.Value, TutorialFocusShape.NoHole, 1f, "card:" + highlight.TargetId)
                        : (TutorialFocus?)null;
                }
                case TutorialHighlightTargetType.RewardCard:
                {
                    // A kill opens the loot list first (엽전 · 아이템 · 「부적 추가」 row); the card 3-pick only
                    // appears after that row is chosen. Spotlight whichever of the two is up.
                    var lootRect = ScreenRectOf(RewardFlow.LootPanelRect);
                    if (lootRect.HasValue)
                    {
                        return new TutorialFocus(lootRect.Value, TutorialFocusShape.Rect, 1f, "reward:loot");
                    }

                    return cardRewardPopupView != null && cardRewardPopupView.TryGetOfferCardsScreenBounds(out var offers)
                        ? new TutorialFocus(offers, TutorialFocusShape.Rect, 1f, "reward:cards")
                        : (TutorialFocus?)null;
                }
                case TutorialHighlightTargetType.Tile:
                case TutorialHighlightTargetType.FieldObject:
                {
                    if (highlight.Coord == null || !highlight.Coord.Enabled)
                    {
                        return null;
                    }

                    var coord = highlight.Coord.ToHexCoord();
                    var rect = TileScreenRect(coord, heightScale: 0.9f, lift: 0f);
                    // Passthrough: HexMapInputController drops clicks while the pointer is over any UI, so the
                    // tile dim must not be a raycast target at all (the step's own gating still rejects wrong tiles).
                    return rect.HasValue
                        ? new TutorialFocus(rect.Value, TutorialFocusShape.NoHole, TileDimStrength, "tile:" + coord, blocksInput: false)
                        : (TutorialFocus?)null;
                }
                case TutorialHighlightTargetType.Monster:
                {
                    if (!TryGetTutorialMonster(highlight, out var monsterId, out var monsterCoord))
                    {
                        return null;
                    }

                    var bounds = MonsterVisualScreenRect(monsterId);
                    var rect = bounds ?? TileScreenRect(monsterCoord, heightScale: 1.7f, lift: 0.35f);
                    return rect.HasValue
                        ? new TutorialFocus(rect.Value, TutorialFocusShape.Ellipse, 1f, "monster:" + monsterId)
                        : (TutorialFocus?)null;
                }
                case TutorialHighlightTargetType.MonsterNameplate:
                {
                    // Body + nameplate + badge row in one ellipse, for steps that talk about the intent badge.
                    if (!TryGetTutorialMonster(highlight, out var monsterId, out var monsterCoord))
                    {
                        return null;
                    }

                    var body = MonsterVisualScreenRect(monsterId) ?? TileScreenRect(monsterCoord, heightScale: 1.7f, lift: 0.35f);
                    var plate = MonsterNameplateScreenRect(monsterId);
                    Rect? rect = body.HasValue && plate.HasValue ? Union(body.Value, plate.Value) : (body ?? plate);
                    return rect.HasValue
                        ? new TutorialFocus(rect.Value, TutorialFocusShape.Ellipse, 1f, "monster-nameplate:" + monsterId)
                        : (TutorialFocus?)null;
                }
                default:
                    return null;
            }
        }

        // Tiles keep their own gold overlay; a full-strength dim would bury it, so the board is only lightly
        // darkened on those steps.
        private const float TileDimStrength = 0.6f;
        private const int TutorialElevatedCardSortingOrder = 4501;

        private static TutorialFocus? RectFocus(Rect? rect, string key)
        {
            return rect.HasValue ? new TutorialFocus(rect.Value, TutorialFocusShape.Rect, 1f, key) : (TutorialFocus?)null;
        }

        private RectTransform tutorialElevatedCard;

        // Lifts one hand-card slot above the spotlight dim by giving it its own sorting canvas (plus a raycaster,
        // since a nested override-sorting canvas is not swept by the root raycaster). Removed again the moment
        // the highlighted card changes or the step moves on, so the lane's own hierarchy is untouched afterwards.
        private void ElevateTutorialCard(RectTransform slot)
        {
            if (tutorialElevatedCard == slot)
            {
                return;
            }

            if (tutorialElevatedCard != null)
            {
                // Immediate, not deferred: a Destroy()ed component lingers as a fake-null until end of frame,
                // and re-elevating the same slot in that window would resurrect it (MissingComponentException).
                var raycaster = tutorialElevatedCard.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    DestroyImmediate(raycaster);
                }

                var canvas = tutorialElevatedCard.GetComponent<Canvas>();
                if (canvas != null)
                {
                    DestroyImmediate(canvas);
                }
            }

            tutorialElevatedCard = slot;
            if (slot == null)
            {
                return;
            }

            // Explicit null checks — `GetComponent ?? AddComponent` is fooled by Unity's fake null.
            var lift = slot.GetComponent<Canvas>();
            if (lift == null)
            {
                lift = slot.gameObject.AddComponent<Canvas>();
            }

            lift.overrideSorting = true;
            lift.sortingOrder = TutorialElevatedCardSortingOrder;
            if (slot.GetComponent<GraphicRaycaster>() == null)
            {
                slot.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        // The monster's rendered body projected to a screen rect (null when the marker has no mesh yet or is
        // behind the camera). Sized from the real silhouette so the hole follows body scale and animation.
        private Rect? MonsterVisualScreenRect(string monsterId)
        {
            if (prototype3DCamera == null || !actorMarkerPresenter.TryGetMarkerVisualBounds(monsterId, out var bounds))
            {
                return null;
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? bounds.min.x : bounds.max.x,
                    (i & 2) == 0 ? bounds.min.y : bounds.max.y,
                    (i & 4) == 0 ? bounds.min.z : bounds.max.z);
                var p = prototype3DCamera.WorldToScreenPoint(corner);
                if (p.z <= 0f)
                {
                    return null;
                }

                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private readonly List<Vector3> tutorialNameplateCorners = new List<Vector3>(8);

        // The monster's nameplate and badge row projected to a screen rect (null when there is no nameplate
        // or any corner is behind the camera). Same projection as MonsterVisualScreenRect so the two union cleanly.
        private Rect? MonsterNameplateScreenRect(string monsterId)
        {
            tutorialNameplateCorners.Clear();
            if (prototype3DCamera == null || !actorMarkerPresenter.TryGetMarkerNameplateWorldCorners(monsterId, tutorialNameplateCorners))
            {
                return null;
            }

            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            foreach (var corner in tutorialNameplateCorners)
            {
                var p = prototype3DCamera.WorldToScreenPoint(corner);
                if (p.z <= 0f)
                {
                    return null;
                }

                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private static Rect Union(Rect a, Rect b)
        {
            return Rect.MinMaxRect(
                Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin),
                Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
        }

        private GameplayCardLaneView ResolveTutorialCardLaneView()
        {
            if (cardLaneViewForFocus == null)
            {
                cardLaneViewForFocus = FindFirstObjectByType<GameplayCardLaneView>(FindObjectsInactive.Include);
            }

            return cardLaneViewForFocus;
        }

        private RectTransform ResolveTutorialUiTargetRect(string targetId)
        {
            if (tutorialUiGlowForFocus == null)
            {
                tutorialUiGlowForFocus = FindFirstObjectByType<TutorialUiGlow>(FindObjectsInactive.Include);
            }

            return tutorialUiGlowForFocus != null ? tutorialUiGlowForFocus.ResolveTargetRect(targetId) : null;
        }

        // A monster highlight follows the live monster (it moves during its own phase), matched by spawn ref id
        // or definition id; failing that the nearest living monster (to the authored coord, else to the player).
        private bool TryGetTutorialMonster(TutorialHighlight highlight, out string monsterId, out HexCoord coord)
        {
            monsterId = null;
            coord = default;
            if (State == null)
            {
                return false;
            }

            var hasAuthoredCoord = highlight.Coord != null && highlight.Coord.Enabled;
            var reference = hasAuthoredCoord ? highlight.Coord.ToHexCoord() : State.PlayerCoord;
            var id = highlight.TargetId?.Trim();
            var nearestDistance = int.MaxValue;
            foreach (var monster in State.Monsters)
            {
                if (monster.IsDead)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(id)
                    && (string.Equals(monster.SpawnRefId, id, System.StringComparison.OrdinalIgnoreCase)
                        || string.Equals(monster.DefinitionId, id, System.StringComparison.OrdinalIgnoreCase)))
                {
                    monsterId = monster.Id;
                    coord = monster.Coord;
                    return true;
                }

                var distance = monster.Coord.DistanceTo(reference);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    monsterId = monster.Id;
                    coord = monster.Coord;
                }
            }

            return monsterId != null;
        }

        // Projects a hex to a screen rect sized from the on-screen distance to its neighbour, so the hole scales
        // with zoom. heightScale stretches the rect vertically (a standing monster is taller than its tile);
        // lift shifts the rect up by that fraction of the neighbour distance.
        private Rect? TileScreenRect(HexCoord coord, float heightScale, float lift)
        {
            if (prototype3DCamera == null || !TryGetTileWorldPosition(coord, out var world))
            {
                return null;
            }

            var center = prototype3DCamera.WorldToScreenPoint(world);
            if (center.z <= 0f)
            {
                return null;
            }

            var neighbourDistance = 48f;
            if (TryGetTileWorldPosition(new HexCoord(coord.Q + 1, coord.R), out var neighbourWorld))
            {
                var neighbour = prototype3DCamera.WorldToScreenPoint(neighbourWorld);
                if (neighbour.z > 0f)
                {
                    neighbourDistance = Mathf.Max(24f, Vector2.Distance(center, neighbour));
                }
            }

            var width = neighbourDistance * 1.15f;
            var height = neighbourDistance * heightScale;
            var cy = center.y + neighbourDistance * lift;
            return new Rect(center.x - width * 0.5f, cy - height * 0.5f, width, height);
        }

        private static readonly Vector3[] TutorialCornerBuffer = new Vector3[4];

        private static Rect? ScreenRectOf(RectTransform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                return null;
            }

            var canvas = target.GetComponentInParent<Canvas>();
            var camera = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.rootCanvas.worldCamera
                : null;

            target.GetWorldCorners(TutorialCornerBuffer);
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < 4; i++)
            {
                var p = RectTransformUtility.WorldToScreenPoint(camera, TutorialCornerBuffer[i]);
                min = Vector2.Min(min, p);
                max = Vector2.Max(max, p);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        private void ApplyTutorialTileHighlight(CombatMapOverlayPresenter presenter)
        {
            if (presenter == null)
            {
                return;
            }

            if (TryGetTutorialTileHighlight(out var coord))
            {
                presenter.ShowTutorialTileHighlight(new[] { coord });
                isTutorialTileHighlightActive = true;
                return;
            }

            if (isTutorialTileHighlightActive)
            {
                presenter.ClearTutorialTileHighlight();
                isTutorialTileHighlightActive = false;
            }
        }

        private bool TryGetTutorialTileHighlight(out HexCoord coord)
        {
            coord = default;
            var step = ResolveTutorialDirector()?.CurrentStep;
            var highlight = step?.Highlight;
            var highlightCoord = highlight?.Coord;
            if (highlight == null ||
                highlight.TargetType != TutorialHighlightTargetType.Tile ||
                highlightCoord == null ||
                !highlightCoord.Enabled)
            {
                return false;
            }

            coord = highlightCoord.ToHexCoord();
            return true;
        }
    }
}
