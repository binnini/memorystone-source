using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class GameplayCardLaneView : MonoBehaviour
    {
        [Header("Card Lane")]
        [SerializeField] private bool autoLayoutEnabled;
        [SerializeField] private GameObject cardSlotPrefab;
        [SerializeField] private GameObject moveCardSlotPrefab;
        [SerializeField] private GameObject actionCardSlotPrefab;
        [Tooltip("Frame sprite swapped onto Card_Frame_Overlay while the bound card is a status card (X01~X03). Leave empty to keep the shared action-card frame — the lane is fully inert until the art arrives. 발주: docs/design/status-trap-resource-requests.md R-1.")]
        [SerializeField] private Sprite statusCardFrameSprite;
        [SerializeField] private RectTransform moveCardsRoot;
        [SerializeField] private RectTransform actionCardsRoot;
        [SerializeField] private RectTransform moveSectionLabel;
        [SerializeField] private RectTransform actionSectionLabel;
        [Tooltip("Central UI theme. When assigned, the modified-cost highlight colour comes from it; leave empty for the built-in default.")]
        [SerializeField] private UiThemeAsset theme;

        [Header("Card Illustrations")]
        [SerializeField] private Sprite defaultIllustration;
        [SerializeField] private Sprite moveIllustration;
        [SerializeField] private Sprite attackIllustration;
        [SerializeField] private Sprite defendIllustration;
        [SerializeField] private Sprite scoutInvestigateIllustration;
        [SerializeField] private List<CardIllustrationBinding> cardIllustrations = new List<CardIllustrationBinding>();

        [Header("Card Fan Layout")]
        [SerializeField] private Vector2 cardFanSize = new Vector2(200f, 320f);
        [SerializeField] private float cardFanStep = 108f;
        [SerializeField] private float cardFanGroupGap = 32f;
        [SerializeField] private float cardFanMaxAngle = 10f;
        [SerializeField] private float cardFanArcDrop = 28f;
        [SerializeField] private float cardFanLabelOffsetY = 42f;
        [SerializeField] private float cardFanLabelAngleScale = 0.55f;
        /// <summary>
        /// 부채가 벌어질 수 있는 <b>최대 반폭</b>. 손패가 늘면 카드는 더 벌어지는 대신 이 값에서
        /// 멈추고 서로 더 겹친다.
        ///
        /// <para>🔴 이 값은 <b>양옆 도크와의 경합으로 정해진다</b>(2026-09-05 실플레이): 손패 9장에서
        /// 바깥 카드가 왼쪽 기력 도크와 오른쪽 이동 종료 버튼을 <b>동시에</b> 파고들었다. CardLane
        /// 로컬 기준 — 카드 뿌리 x=+134, 카드 반폭 120, 기력 도크 오른쪽 끝 −580, 종료 버튼 왼쪽 끝 805.
        /// 오른쪽이 더 빡빡해서 <c>134 + half + 120 ≤ 805</c> → <b>half ≤ 551</b>이 상한이고,
        /// 출하 저작은 여백을 둬 540이다(그때 8장까지는 종전과 사실상 동일하고 9장부터만 더 겹친다).</para>
        ///
        /// <para>⚠️ 도크를 옮기거나 카드 크기를 바꾸면 이 값도 다시 풀어야 한다 —
        /// <c>CardFanDoesNotOverlapSideDocks</c>가 그 계산을 대신 지키고 있다.</para>
        /// </summary>
        [SerializeField] private float cardFanMaxHalfSpan = 496f;
        [SerializeField] private float cardFanRestOffsetY = -158f;
        [SerializeField] private bool applyGeneratedLaneRootLayout;
        [SerializeField] private bool applyGeneratedLaneLabelLayout;
        [SerializeField] private bool useSeparateCardRootPositions;
        [SerializeField] private Vector2 moveCardsRootPosition = new Vector2(0f, -158f);
        [SerializeField] private Vector2 actionCardsRootPosition = new Vector2(0f, -158f);

        [Header("Card Lane Reveal")]
        [SerializeField] private bool revealCardLaneOnBottomHover = false;
        [SerializeField] private float cardLaneBottomHoverPixels = 132f;
        [SerializeField] private float cardLaneHideOffsetY = 328f;
        [SerializeField] private float cardLaneRevealSpeed = 8f;

        [Header("Card Hover")]
        [SerializeField] private float cardHoverLiftY = 170f;
        [SerializeField] private float cardHoverScale = 1.12f;
        [SerializeField] private float cardDragScale = 1.13f;
        [SerializeField] private float cardHoverInputVerticalPadding = 40f;
        [SerializeField] private float cardHoverInputHorizontalPadding = 16f;

        private readonly List<HandCardInteraction> moveCardSlots = new List<HandCardInteraction>();
        private readonly List<HandCardInteraction> actionCardSlots = new List<HandCardInteraction>();
        private ICombatCardHudHost lastController;
        private string tutorialCueSelectionKey;
        private readonly Dictionary<HandCardInteraction, int> stableSlotOrders = new Dictionary<HandCardInteraction, int>();
        private int nextStableSlotOrder;
        private Vector2 shownPosition;
        private bool hasShownPosition;
        private Vector2 shownMoveCardsPosition;
        private Vector2 shownActionCardsPosition;
        private Vector2 shownMoveLabelPosition;
        private Vector2 shownActionLabelPosition;
        private bool hasRevealTargetPositions;
        private bool revealInitialized;
        private bool revealVisible;
        private CardLaneHomeInputProxy homeInputProxy;
        private HandCardInteraction proxyPressedSlot;
        private HandCardInteraction proxyDraggingSlot;
        private HandCardInteraction currentHoveredSlot;
        private HandCardInteraction externallyLiftedSlot;

        public RectTransform RectTransform => transform as RectTransform;
        public RectTransform MoveCardsRoot => moveCardsRoot;
        public RectTransform ActionCardsRoot => actionCardsRoot;
        public GameObject CardSlotPrefab => cardSlotPrefab;
        public GameObject MoveCardSlotPrefab => moveCardSlotPrefab;
        public GameObject ActionCardSlotPrefab => actionCardSlotPrefab;
        public Sprite StatusCardFrameSprite => statusCardFrameSprite;
        public IReadOnlyList<HandCardInteraction> MoveCardSlots => moveCardSlots;
        public IReadOnlyList<HandCardInteraction> ActionCardSlots => actionCardSlots;

        // The hand slot currently carrying the tutorial cue glow (resolved by UpdateTutorialCardCue each frame),
        // or null. The host's tutorial focus resolver reads this so the spotlight hole lands on the same card
        // the glow does.
        public RectTransform TutorialCueSlotRect
        {
            get
            {
                if (string.IsNullOrEmpty(tutorialCueSelectionKey))
                {
                    return null;
                }

                return FindSlotRect(moveCardSlots, tutorialCueSelectionKey) ?? FindSlotRect(actionCardSlots, tutorialCueSelectionKey);
            }
        }

        private static RectTransform FindSlotRect(IReadOnlyList<HandCardInteraction> slots, string selectionKey)
        {
            if (slots == null)
            {
                return null;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.gameObject.activeSelf
                    && string.Equals(slot.CurrentCardSelectionKey, selectionKey, StringComparison.Ordinal))
                {
                    return slot.transform as RectTransform;
                }
            }

            return null;
        }

        private void Awake()
        {
            ResolveReferences();
            CaptureShownPosition();
            CaptureRevealTargetPositions();
        }

        private void LateUpdate()
        {
            UpdateCentralHover();
            UpdateReveal();
            UpdateTutorialCardCue();
        }

        // Keeps the tutorial cue glow in sync with the current tutorial step every frame. RefreshCardGroup
        // only runs on hand refreshes, but the highlighted card changes whenever the step advances, so the
        // glow must be re-resolved here too.
        private void UpdateTutorialCardCue()
        {
            var highlightCardId = lastController != null ? lastController.TutorialHighlightCardId : null;
            string cueKey = null;
            if (!string.IsNullOrEmpty(highlightCardId))
            {
                cueKey = FirstActiveMatchingSelectionKey(moveCardSlots, highlightCardId)
                    ?? FirstActiveMatchingSelectionKey(actionCardSlots, highlightCardId);
            }

            tutorialCueSelectionKey = cueKey;
            ApplyTutorialCueToSlots(moveCardSlots, cueKey);
            ApplyTutorialCueToSlots(actionCardSlots, cueKey);
        }

        private static string FirstActiveMatchingSelectionKey(IReadOnlyList<HandCardInteraction> slots, string cardId)
        {
            if (slots == null)
            {
                return null;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.gameObject.activeSelf)
                {
                    continue;
                }

                if (MatchesTutorialCardId(slot.Card, cardId))
                {
                    return slot.CurrentCardSelectionKey;
                }
            }

            return null;
        }

        private static void ApplyTutorialCueToSlots(IReadOnlyList<HandCardInteraction> slots, string cueKey)
        {
            if (slots == null)
            {
                return;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.gameObject.activeSelf)
                {
                    continue;
                }

                slot.SetTutorialCue(!string.IsNullOrEmpty(cueKey)
                    && string.Equals(slot.CurrentCardSelectionKey, cueKey, StringComparison.Ordinal));
            }
        }

        public HandCardInteraction ResolveHoveredSlot(Vector2 screenPosition)
        {
            ResolveReferences();
            var moveCandidate = ResolveHoveredSlot(moveCardSlots, moveCardsRoot, screenPosition);
            var actionCandidate = ResolveHoveredSlot(actionCardSlots, actionCardsRoot, screenPosition);
            if (moveCandidate == null)
            {
                return actionCandidate;
            }

            if (actionCandidate == null)
            {
                return moveCandidate;
            }

            return GetScreenDistanceToHome(moveCandidate, screenPosition) <= GetScreenDistanceToHome(actionCandidate, screenPosition)
                ? moveCandidate
                : actionCandidate;
        }

        public void ApplyResolvedHover(Vector2 screenPosition)
        {
            var hoveredSlot = ResolveHoveredSlot(screenPosition);
            ApplyResolvedHover(hoveredSlot);
        }

        public void Configure(
            GameObject slotPrefab,
            Vector2 fanSize,
            float fanStep,
            float fanGroupGap,
            float fanMaxAngle,
            float fanArcDrop,
            float fanLabelOffsetY,
            float fanLabelAngleScale,
            float fanMaxHalfSpan,
            bool revealOnBottomHover,
            float bottomHoverPixels,
            float hideOffsetY,
            float revealSpeed,
            Sprite defaultCardIllustration = null,
            Sprite moveCardIllustration = null,
            Sprite attackCardIllustration = null,
            Sprite defendCardIllustration = null,
            Sprite scoutInvestigateCardIllustration = null,
            GameObject moveCardPrefab = null,
            GameObject actionCardPrefab = null)
        {
            cardSlotPrefab = cardSlotPrefab != null ? cardSlotPrefab : slotPrefab;
            moveCardSlotPrefab = moveCardSlotPrefab != null ? moveCardSlotPrefab : moveCardPrefab;
            actionCardSlotPrefab = actionCardSlotPrefab != null ? actionCardSlotPrefab : actionCardPrefab;
            defaultIllustration = defaultIllustration != null ? defaultIllustration : defaultCardIllustration;
            moveIllustration = moveIllustration != null ? moveIllustration : moveCardIllustration;
            attackIllustration = attackIllustration != null ? attackIllustration : attackCardIllustration;
            defendIllustration = defendIllustration != null ? defendIllustration : defendCardIllustration;
            scoutInvestigateIllustration = scoutInvestigateIllustration != null ? scoutInvestigateIllustration : scoutInvestigateCardIllustration;
            revealCardLaneOnBottomHover = revealOnBottomHover;
            cardLaneBottomHoverPixels = bottomHoverPixels;
            cardLaneHideOffsetY = hideOffsetY;
            cardLaneRevealSpeed = revealSpeed;
            ResolveReferences(force: true);
            ConfigureLayout();
        }

        public void ConfigureLayout()
        {
            var rect = RectTransform;
            if (rect == null)
            {
                return;
            }

            shownPosition = rect.anchoredPosition;
            hasShownPosition = true;

            ResolveReferences(force: true);
            CaptureRevealTargetPositions(force: true);
            if (!autoLayoutEnabled)
            {
                return;
            }

            var m = moveCardSlots.Count(slot => slot != null && slot.gameObject.activeSelf);
            var a = actionCardSlots.Count(slot => slot != null && slot.gameObject.activeSelf);
            if (m == 0 && a == 0 && !Application.isPlaying)
            {
                m = moveCardSlots.Count;
                a = actionCardSlots.Count;
            }

            LayoutCardFan(m, a);
            CaptureRevealTargetPositions(force: true);
        }

        public void Refresh(
            CombatState state,
            ICombatCardHudHost controller,
            Action<CombatCardSnapshot> playCard)
        {
            ResolveReferences();
            var cards = state == null
                ? Array.Empty<CombatCardSnapshot>()
                : state.GetHandCards()
                    .OrderBy(card => CombatCardHandOrder.HandSortPriority(card.Kind))
                    .ThenBy(card => card.Kind)
                    .ThenBy(card => card.Id)
                    .ToArray();

            RefreshHandCards(cards, state, controller, playCard);
        }

        public void RefreshHandCards(
            IReadOnlyList<CombatCardSnapshot> handCards,
            CombatState state,
            ICombatCardHudHost controller,
            Action<CombatCardSnapshot> playCard)
        {
            ResolveReferences();
            var cards = (handCards ?? Array.Empty<CombatCardSnapshot>())
                .Where(card => !card.IsDiscarded)
                .OrderBy(card => CombatCardHandOrder.HandSortPriority(card.Kind))
                .ThenBy(card => card.Kind)
                .ThenBy(card => card.Id)
                .ToArray();

            var moveCards = cards.Where(card => card.Kind == CombatCardKind.Move).ToArray();
            var actionCards = cards.Where(card => card.Kind != CombatCardKind.Move).ToArray();

            lastController = controller;
            tutorialCueSelectionKey = ResolveTutorialCueSelectionKey(controller, cards);

            EnsureCardSlotCount(moveCardSlots, moveCardsRoot, ResolveMoveCardSlotPrefab(), moveCards.Length, "MoveCard_");
            EnsureCardSlotCount(actionCardSlots, actionCardsRoot, ResolveActionCardSlotPrefab(), actionCards.Length, "ActionCard_");

            if (autoLayoutEnabled)
            {
                LayoutCardFan(
                    Mathf.Min(moveCardSlots.Count, moveCards.Length),
                    Mathf.Min(actionCardSlots.Count, actionCards.Length));
            }

            RefreshCardGroup(moveCardSlots, moveCards, state, controller, playCard);
            RefreshCardGroup(actionCardSlots, actionCards, state, controller, playCard);
        }

        private void ResolveReferences(bool force = false)
        {
            moveCardsRoot = moveCardsRoot != null ? moveCardsRoot : FindRect("MoveCards");
            actionCardsRoot = actionCardsRoot != null ? actionCardsRoot : FindRect("ActionCards");
            moveSectionLabel = moveSectionLabel != null ? moveSectionLabel : FindRect("MoveSectionLabel");
            actionSectionLabel = actionSectionLabel != null ? actionSectionLabel : FindRect("ActionSectionLabel");
            EnsureHomeInputProxy();

            // Always rebuild the slot cache from the authored hierarchy. Refresh can run repeatedly
            // in EditMode and PlayMode, and serialized root references may already be assigned;
            // returning early in that state made the cache look empty and caused duplicate slot
            // instantiation under MoveCards/ActionCards.
            moveCardSlots.Clear();
            actionCardSlots.Clear();
            CacheCardSlots(moveCardsRoot, moveCardSlots);
            CacheCardSlots(actionCardsRoot, actionCardSlots);
        }

        internal bool IsHomeInputRaycastValid(Vector2 screenPosition)
        {
            return ResolveHoveredSlot(screenPosition) != null;
        }

        internal void HandleHomeInputPointerDown(PointerEventData eventData)
        {
            proxyPressedSlot = ResolveAndApplyHomeInputHover(eventData);
            proxyDraggingSlot = null;
            proxyPressedSlot?.OnPointerDown(eventData);
        }

        internal void HandleHomeInputBeginDrag(PointerEventData eventData)
        {
            proxyDraggingSlot = ResolveAndApplyHomeInputHover(eventData) ?? proxyPressedSlot;
            proxyDraggingSlot?.OnBeginDrag(eventData);
        }

        internal void HandleHomeInputDrag(PointerEventData eventData)
        {
            proxyDraggingSlot?.OnDrag(eventData);
        }

        internal void HandleHomeInputEndDrag(PointerEventData eventData)
        {
            proxyDraggingSlot?.OnEndDrag(eventData);
            proxyDraggingSlot = null;
            proxyPressedSlot = null;
        }

        internal void HandleHomeInputPointerClick(PointerEventData eventData)
        {
            var target = ResolveAndApplyHomeInputHover(eventData) ?? proxyPressedSlot ?? currentHoveredSlot;
            if (target != null)
            {
                ApplyResolvedHover(target);
            }

            target?.OnPointerClick(eventData);
            proxyPressedSlot = null;
        }

        private HandCardInteraction ResolveAndApplyHomeInputHover(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return null;
            }

            var slot = ResolveHoveredSlot(eventData.position);
            ApplyResolvedHover(slot);
            return slot;
        }

        private void EnsureHomeInputProxy()
        {
            if (homeInputProxy != null)
            {
                homeInputProxy.Configure(this);
                return;
            }

            var existing = transform.Find(CardLaneHomeInputProxy.ProxyName);
            var proxyObject = existing != null
                ? existing.gameObject
                : new GameObject(CardLaneHomeInputProxy.ProxyName, typeof(RectTransform));
            var proxyRect = proxyObject.GetComponent<RectTransform>();
            if (proxyRect == null)
            {
                proxyRect = proxyObject.AddComponent<RectTransform>();
            }

            if (proxyObject.transform.parent != transform)
            {
                proxyObject.transform.SetParent(transform, false);
            }

            proxyRect.anchorMin = Vector2.zero;
            proxyRect.anchorMax = Vector2.one;
            proxyRect.pivot = new Vector2(0.5f, 0.5f);
            proxyRect.offsetMin = Vector2.zero;
            proxyRect.offsetMax = Vector2.zero;
            proxyRect.localScale = Vector3.one;
            proxyObject.transform.SetAsFirstSibling();

            var image = proxyObject.GetComponent<Image>();
            if (image == null)
            {
                image = proxyObject.AddComponent<Image>();
            }

            image.color = new Color(1f, 1f, 1f, 0.001f);
            image.raycastTarget = true;

            homeInputProxy = proxyObject.GetComponent<CardLaneHomeInputProxy>();
            if (homeInputProxy == null)
            {
                homeInputProxy = proxyObject.AddComponent<CardLaneHomeInputProxy>();
            }

            homeInputProxy.Configure(this);
        }

        private void CaptureShownPosition()
        {
            var rect = RectTransform;
            if (rect == null || hasShownPosition)
            {
                return;
            }

            shownPosition = rect.anchoredPosition;
            hasShownPosition = true;
        }

        private void CaptureRevealTargetPositions(bool force = false)
        {
            if (hasRevealTargetPositions && !force)
            {
                return;
            }

            shownMoveCardsPosition = GetAnchoredPosition(moveCardsRoot);
            shownActionCardsPosition = GetAnchoredPosition(actionCardsRoot);
            shownMoveLabelPosition = GetAnchoredPosition(moveSectionLabel);
            shownActionLabelPosition = GetAnchoredPosition(actionSectionLabel);
            hasRevealTargetPositions = true;
        }

        private void UpdateReveal()
        {
            if (!Application.isPlaying || !revealCardLaneOnBottomHover)
            {
                return;
            }

            var rect = RectTransform;
            if (rect == null)
            {
                return;
            }

            CaptureShownPosition();
            CaptureRevealTargetPositions();
            rect.anchoredPosition = shownPosition;

            var pointer = Mouse.current?.position.ReadValue();
            var bottomHover = pointer.HasValue && pointer.Value.y <= Mathf.Max(1f, cardLaneBottomHoverPixels);
            var laneHover = revealVisible
                && pointer.HasValue
                && RectTransformUtility.RectangleContainsScreenPoint(rect, pointer.Value, null);
            // 대상 선택 중에는 레인을 내려 판을 비운다(2026-09-05 실플레이 #8 — §28 W2 「선택 중엔 숨기지
            // 않는다」를 뒤집었다). 핀 카드는 SetExternalLift로 내려간 만큼 되올려 제자리에 남고, 나머지
            // 손패만 화면 아래로 물러난다. 그래서 핀 카드 자신의 호버는 레인을 붙드는 근거에서 뺀다 —
            // 넣으면 선택 중 내내 pointerActive가 참이라 레인이 영영 안 내려간다.
            var pinnedSlot = FindPinnedSelectedCardSlot();
            var pointerActive = HasPointerActiveSlot(moveCardSlots, pinnedSlot) || HasPointerActiveSlot(actionCardSlots, pinnedSlot);
            var shouldShow = bottomHover || laneHover || pointerActive;
            revealVisible = shouldShow;
            var offset = shouldShow ? Vector2.zero : Vector2.down * Mathf.Max(0f, cardLaneHideOffsetY);

            if (!revealInitialized)
            {
                ApplyRevealTargetPositions(offset, immediate: true);
                revealInitialized = true;
                UpdatePinnedSlotLift(pinnedSlot);
                return;
            }

            var speed = Mathf.Max(1f, cardLaneRevealSpeed);
            ApplyRevealTargetPositions(offset, immediate: false, 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime));
            UpdatePinnedSlotLift(pinnedSlot);
        }

        /// <summary>
        /// 핀 카드를 레인이 내려간 만큼 되올린다. 매 프레임 실제 루트 위치에서 재므로 레인이 미끄러지는
        /// 중에도 카드는 한 자리에 붙어 있고, 핀이 풀리면 0으로 돌려 레인과 함께 내려온다.
        /// </summary>
        private void UpdatePinnedSlotLift(HandCardInteraction pinnedSlot)
        {
            if (externallyLiftedSlot != null && externallyLiftedSlot != pinnedSlot)
            {
                externallyLiftedSlot.SetExternalLift(0f);
            }

            externallyLiftedSlot = pinnedSlot;
            if (pinnedSlot == null)
            {
                return;
            }

            var root = pinnedSlot.transform.parent as RectTransform;
            if (root == null)
            {
                return;
            }

            var shown = root == moveCardsRoot ? shownMoveCardsPosition
                : root == actionCardsRoot ? shownActionCardsPosition
                : root.anchoredPosition;
            pinnedSlot.SetExternalLift(shown.y - root.anchoredPosition.y);
        }

        private void ApplyRevealTargetPositions(Vector2 offset, bool immediate, float t = 1f)
        {
            MoveRevealTarget(moveCardsRoot, shownMoveCardsPosition + offset, immediate, t);
            MoveRevealTarget(actionCardsRoot, shownActionCardsPosition + offset, immediate, t);
            MoveRevealTarget(moveSectionLabel, shownMoveLabelPosition + offset, immediate, t);
            MoveRevealTarget(actionSectionLabel, shownActionLabelPosition + offset, immediate, t);
        }

        private static void MoveRevealTarget(RectTransform target, Vector2 position, bool immediate, float t)
        {
            if (target == null)
            {
                return;
            }

            target.anchoredPosition = immediate
                ? position
                : Vector2.Lerp(target.anchoredPosition, position, t);
        }

        private static Vector2 GetAnchoredPosition(RectTransform target)
        {
            return target != null ? target.anchoredPosition : Vector2.zero;
        }

#if UNITY_INCLUDE_TESTS
        public bool HasPointerActiveCardForTests()
        {
            ResolveReferences();
            return HasPointerActiveSlot(moveCardSlots) || HasPointerActiveSlot(actionCardSlots);
        }
#endif

        private void UpdateCentralHover()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ResolveReferences();
            if (HasDraggingSlot(moveCardSlots) || HasDraggingSlot(actionCardSlots))
            {
                return;
            }

            var mouse = Mouse.current;
            if (mouse == null)
            {
                ClearCentralHover();
                return;
            }

            var hoveredSlot = ResolveHoveredSlot(mouse.position.ReadValue());
            // 대상 선택 중에는 마우스가 판으로 가도 사용한 카드를 호버(확대) 상태로 고정한다(§28 W2) —
            // 무슨 카드를 조준 중인지 효과 텍스트를 계속 읽을 수 있어야 한다. 마우스가 다른 카드 위에
            // 있으면 그쪽이 이긴다(비교 열람은 여전히 가능).
            if (hoveredSlot == null)
            {
                hoveredSlot = FindPinnedSelectedCardSlot();
            }

            ApplyResolvedHover(hoveredSlot);
        }

        /// <summary>대상 선택 중인 카드(SelectedCardKey)의 슬롯. 선택 중이 아니면 null.</summary>
        private HandCardInteraction FindPinnedSelectedCardSlot()
        {
            var selectedKey = lastController != null ? lastController.SelectedCardKey : null;
            if (string.IsNullOrEmpty(selectedKey))
            {
                return null;
            }

            return FindSlotBySelectionKey(moveCardSlots, selectedKey)
                ?? FindSlotBySelectionKey(actionCardSlots, selectedKey);
        }

        private static HandCardInteraction FindSlotBySelectionKey(IReadOnlyList<HandCardInteraction> slots, string selectionKey)
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
                    && string.Equals(slot.CurrentCardSelectionKey, selectionKey, StringComparison.Ordinal))
                {
                    return slot;
                }
            }

            return null;
        }

        private void ClearCentralHover()
        {
            ApplyResolvedHover(null);
        }

        private void EnsureCardSlotCount(
            List<HandCardInteraction> slots,
            RectTransform parent,
            GameObject slotPrefab,
            int required,
            string namePrefix)
        {
            if (slots == null || parent == null)
            {
                return;
            }

            RebuildSlotCache(parent, slots);
            slots.RemoveAll(slot => slot == null);
            if (required <= slots.Count)
            {
                return;
            }

            if (slotPrefab == null)
            {
                EnsureLaneSlotCount(slots, parent.name, required);
                return;
            }

            while (slots.Count < required)
            {
                var instance = Instantiate(slotPrefab, parent, false);
                instance.name = $"{namePrefix}{slots.Count + 1:00}";

                EnsureCardPointerRaycastTarget(instance);
                var interaction = instance.GetComponent<HandCardInteraction>();
                if (interaction == null)
                {
                    interaction = instance.AddComponent<HandCardInteraction>();
                }

                interaction.ConfigureHoverMotion(cardHoverLiftY, cardHoverScale, cardDragScale);
                interaction.SetLaneHoverControlled(true);
                RegisterStableSlot(interaction);
                slots.Add(interaction);
            }
        }

        private void EnsureLaneSlotCount(List<HandCardInteraction> slots, string containerName, int required)
        {
            if (slots.Count == 0)
            {
                slots.AddRange(GetLaneSlots(containerName));
            }

            if (slots.Count == 0 || required <= slots.Count)
            {
                return;
            }

            var template = slots[slots.Count - 1];
            if (template == null)
            {
                return;
            }

            var parent = template.transform.parent;
            var templateName = template.name;
            var prefix = templateName.Contains("_")
                ? templateName.Substring(0, templateName.LastIndexOf('_') + 1)
                : templateName + "_";

            while (slots.Count < required)
            {
                var clone = Instantiate(template.gameObject, parent, false);
                clone.name = $"{prefix}{slots.Count + 1:00}";
                var interaction = clone.GetComponent<HandCardInteraction>();
                if (interaction != null)
                {
                    interaction.ConfigureHoverMotion(cardHoverLiftY, cardHoverScale, cardDragScale);
                    interaction.SetLaneHoverControlled(true);
                    RegisterStableSlot(interaction);
                }

                EnsureCardPointerRaycastTarget(clone);

                slots.Add(interaction);
            }
        }

        private GameObject ResolveMoveCardSlotPrefab()
        {
            return moveCardSlotPrefab != null ? moveCardSlotPrefab : cardSlotPrefab;
        }

        private GameObject ResolveActionCardSlotPrefab()
        {
            return actionCardSlotPrefab != null ? actionCardSlotPrefab : cardSlotPrefab;
        }

        private void RefreshCardGroup(
            IReadOnlyList<HandCardInteraction> slots,
            IReadOnlyList<CombatCardSnapshot> cards,
            CombatState state,
            ICombatCardHudHost controller,
            Action<CombatCardSnapshot> playCard)
        {
            if (slots == null)
            {
                return;
            }

            var activeCardKeys = new HashSet<string>(
                (cards ?? Array.Empty<CombatCardSnapshot>()).Select(card => card.SelectionKey),
                StringComparer.Ordinal);

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                var active = i < cards.Count;
                if (slot.IsPointerActive && activeCardKeys.Contains(slot.CurrentCardSelectionKey))
                {
                    continue;
                }

                if (slot.IsPointerActive)
                {
                    slot.CancelPointerStateForRefresh();
                }

                slot.gameObject.SetActive(active);
                if (!active)
                {
                    slot.SetTutorialCue(false);
                    slot.SetOminousAura(false);
                    var hiddenFeedback = slot.GetComponent<CardUnavailableFeedback>();
                    hiddenFeedback?.SetUnavailable(false, string.Empty);
                    hiddenFeedback?.SetSealed(false);
                    continue;
                }

                var card = cards[i];
                var playable = IsCardInteractionEnabled(card, state);
                var unavailableReason = GetUnavailableReason(card, state);
                var selectionVisualState = controller == null
                    ? HandCardSelectionVisualState.Normal
                    : controller.GetHandCardSelectionVisualState(card);
                var interactionEnabled = selectionVisualState == HandCardSelectionVisualState.Normal
                    ? playable && state != null && !state.IsTerminal
                    : selectionVisualState == HandCardSelectionVisualState.Candidate
                        || selectionVisualState == HandCardSelectionVisualState.Picked;
                slot.Initialize(
                    (interaction, snapshot, releasePosition) =>
                    {
                        if (controller != null && controller.HandCardSelectionPanel.IsActive)
                        {
                            return;
                        }

                        if (interaction != null && releasePosition.y > interaction.HomeScreenTopY)
                        {
                            PlayOrToggleCardSelection(controller, playCard, snapshot);
                        }
                    },
                    (interaction, snapshot) => ToggleHandCardSelectionOnly(controller, snapshot),
                    (interaction, snapshot) => controller?.RequestCardHoverAudio(snapshot.Id),
                    (interaction, snapshot) =>
                    {
                        var feedback = interaction != null ? interaction.GetComponent<CardUnavailableFeedback>() : null;
                        feedback?.ShowUnavailableReason(GetUnavailableReason(snapshot, state));
                    });
                slot.SetLaneHoverControlled(true);
                // Configure 앞에 둔다: Configure가 ApplyImmediateState로 글로우 알파를 즉시 확정하므로,
                // 뒤에 켜면 아우라가 0에서 페이드인하며 한 박자 늦게 나타난다.
                slot.SetOminousAura(ShowStatusCardAura && IsStatusCard(card));
                var selected = controller != null && controller.SelectedCardKey == card.SelectionKey;
                slot.Configure(card, interactionEnabled, selected, selectionVisualState);
                slot.SetTutorialCue(!string.IsNullOrEmpty(tutorialCueSelectionKey)
                    && string.Equals(card.SelectionKey, tutorialCueSelectionKey, StringComparison.Ordinal));
                // Refresh the card art first so the grayscale wash mirrors the up-to-date illustration sprite.
                RefreshCardText(slot.transform, card, state);
                var feedback = GetOrAddCardUnavailableFeedback(slot);
                // 대상 선택 중인 카드는 dim 예외(§28 W2) — 핀 호버로 확대해 두고 검은 막을 씌우면
                // "확대된 채 새까만 카드"가 된다.
                feedback?.SetUnavailable(
                    selectionVisualState == HandCardSelectionVisualState.Normal && !interactionEnabled && !selected,
                    unavailableReason);
                // 봉인된 카드에는 빨간 X. 아우라(상태 카드)와 서로 다른 상태다 — 상태 카드는 그 카드의
                // 영구 속성이고, 봉인은 매 턴 다른 카드로 옮겨 다닌다(O-11 재선정). 그래서 둘 다
                // 손패 갱신마다 다시 계산한다. SetUnavailable 뒤에 둔다: dim이 최상단으로 올라간 다음
                // X가 그 위로 올라가야 검은 막에 묻히지 않는다.
                feedback?.SetSealed(IsSealedCard(card));
            }
        }

        // 불길한 아우라 OFF (2026-07-31 사용자 확정). 갤러리 판정에서 기각됐다 — 호버·선택·튜토리얼 큐와
        // **같은 모양의 네온 외곽선**이라 "불길함"이 아니라 "선택됨"으로 읽히고, 못 쓰는 카드가 화면에서
        // 가장 밝은 요소가 됐다. 상태 카드의 정체는 **전용 프레임**이 말하기로 결정됐고(백로그 §1-A 안 다),
        // 프레임 시안이 나온 뒤 아우라를 색을 갈라 되살릴지 아예 지울지 정한다.
        //
        // 배관은 그대로 둔다(`CardHoverGlow.SetOminousAura`) — 되살리는 비용을 이 상수 한 줄로 묶어 두려는
        // 것이다. 지우고 다시 만들면 "isDisabled보다 앞에 둬야 보인다" 같은 함정을 다시 밟는다.
        // 발주 사양: docs/design/status-trap-resource-requests.md R-1.
        private const bool ShowStatusCardAura = false;

        // 프레임 스왑·아우라·상태 문구·코스트/사거리 숨김·설명 색이 모두 이 하나를 본다.
        // 술어가 갈라지면 "프레임은 보라인데 코스트는 남아 있는" 반쪽 상태가 생긴다.
        private static bool IsStatusCard(CombatCardSnapshot card)
        {
            return CardFrontPresentationFormatting.IsStatusCard(card);
        }

        private static bool IsSealedCard(CombatCardSnapshot card)
        {
            return !card.IsDiscarded
                && string.Equals(card.Status, CombatCardStatusText.Sealed, StringComparison.Ordinal);
        }

        private static CardUnavailableFeedback GetOrAddCardUnavailableFeedback(HandCardInteraction slot)
        {
            if (slot == null)
            {
                return null;
            }

            return slot.GetComponent<CardUnavailableFeedback>() ?? slot.gameObject.AddComponent<CardUnavailableFeedback>();
        }

        private static void PlayOrToggleCardSelection(
            ICombatCardHudHost controller,
            Action<CombatCardSnapshot> playCard,
            CombatCardSnapshot snapshot)
        {
            if (controller != null
                && controller.HandCardSelectionPanel.IsActive
                && controller.GetHandCardSelectionVisualState(snapshot) != HandCardSelectionVisualState.Normal)
            {
                controller.ToggleHandCardSelection(snapshot.SelectionKey);
                return;
            }

            playCard?.Invoke(snapshot);
        }

        // 클릭은 카드를 사용하지 않는다(§28 W1 — 사용은 드래그 아웃 단일 경로). 클릭에 남는 유일한
        // 소임은 손패 선택 패널(버릴 카드 고르기)의 후보 토글이다 — 이 분기를 지우면 패널에서
        // 카드를 고를 수단이 사라진다.
        private static void ToggleHandCardSelectionOnly(ICombatCardHudHost controller, CombatCardSnapshot snapshot)
        {
            if (controller != null
                && controller.HandCardSelectionPanel.IsActive
                && controller.GetHandCardSelectionVisualState(snapshot) != HandCardSelectionVisualState.Normal)
            {
                controller.ToggleHandCardSelection(snapshot.SelectionKey);
            }
        }

        private void RefreshCardText(Transform root, CombatCardSnapshot card, CombatState state)
        {
            ApplyCardFrame(root, card);
            SetCardIllustration(root, card);
            SetText(root, "CostText_TMP", card.Cost.ToString());
            ApplyCostColor(root, card);
            CardFrontPresentationFormatting.RefreshCardCost(root, card);
            SetText(root, "CardNameText_TMP", card.Name);
            SetText(root, "TypeText_TMP", CardFrontPresentationFormatting.KindLabel(card));
            SetText(root, "DescriptionText_TMP", card.Description);
            ApplyDescriptionColor(root, card);
            KeywordHoverTooltipBinder.EnsureOnChild(root, "DescriptionText_TMP");
            RefreshCardRange(root, card);
            var status = root.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.name.Contains("Status"));
            if (status != null)
            {
                status.text = GetCardStatusText(card, state);
            }
        }

        // 상태 카드 프레임은 변형 프리팹 분기가 아니라 바인딩 시점의 스프라이트 스왑이다.
        // 행동 슬롯은 actionCardSlotPrefab 하나에서 풀링돼 상태 카드와 일반 행동 카드가 같은 슬롯을
        // 번갈아 쓰므로, 슬롯 생성 시점에는 어떤 카드가 앉을지 알 수 없다. 원래 프레임은 슬롯별로
        // 캐시해 일반 카드가 다시 앉으면 되돌린다.
        private readonly Dictionary<Image, Sprite> defaultFrameSpriteBySlotFrame = new Dictionary<Image, Sprite>();

        private void ApplyCardFrame(Transform root, CombatCardSnapshot card)
        {
            CardFrontPresentationFormatting.ApplyCardFrame(
                root, card, statusCardFrameSprite, defaultFrameSpriteBySlotFrame);
        }

        private void SetCardIllustration(Transform root, CombatCardSnapshot card)
        {
            var image = root.GetComponentsInChildren<Image>(includeInactive: true)
                .FirstOrDefault(candidate => candidate.name == "Card_Illust");
            if (image == null)
            {
                return;
            }

            var sprite = GetCardIllustration(card);
            if (sprite == null)
            {
                return;
            }

            image.sprite = sprite;
        }

        private Sprite GetCardIllustration(CombatCardSnapshot card)
        {
            var sprite = ResolveCardIllustration(card.IllustrationId);
            if (sprite != null)
            {
                return sprite;
            }

            if (!string.IsNullOrWhiteSpace(card.Id))
            {
                sprite = ResolveCardIllustration($"card_illust_{card.Id}");
                if (sprite != null)
                {
                    return sprite;
                }
            }

            // Catalog fallback: works in builds where the serialized cardIllustrations binding is missing
            // an entry (e.g. cards added after the lane prefab was last baked). See part_e healthbar note.
            sprite = ResolveCatalogIllustration(card.IllustrationId);
            if (sprite == null && !string.IsNullOrWhiteSpace(card.Id))
            {
                sprite = ResolveCatalogIllustration($"card_illust_{card.Id}");
            }
            if (sprite != null)
            {
                return sprite;
            }

            return GetKindFallbackIllustration(card.Kind);
        }

        private static Sprite ResolveCatalogIllustration(string illustrationId)
        {
            return string.IsNullOrWhiteSpace(illustrationId)
                ? null
                : CardIllustrationCatalog.ResolveDefault(illustrationId);
        }

        private Sprite ResolveCardIllustration(string illustrationId)
        {
            if (string.IsNullOrWhiteSpace(illustrationId) || cardIllustrations == null)
            {
                return null;
            }

            for (var i = 0; i < cardIllustrations.Count; i++)
            {
                var binding = cardIllustrations[i];
                if (binding != null &&
                    binding.Sprite != null &&
                    string.Equals(binding.IllustrationId, illustrationId, StringComparison.Ordinal))
                {
                    return binding.Sprite;
                }
            }

            return null;
        }

        private Sprite GetKindFallbackIllustration(CombatCardKind kind)
        {
            switch (kind)
            {
                case CombatCardKind.Move:
                    return moveIllustration != null ? moveIllustration : defaultIllustration;
                case CombatCardKind.Attack:
                    return attackIllustration != null ? attackIllustration : defaultIllustration;
                case CombatCardKind.Defend:
                    return defendIllustration != null ? defendIllustration : defaultIllustration;
                case CombatCardKind.Scout:
                case CombatCardKind.Investigate:
                case CombatCardKind.FieldObject:
                case CombatCardKind.Buff:
                case CombatCardKind.Utility:
                    return scoutInvestigateIllustration != null ? scoutInvestigateIllustration : defaultIllustration;
                default:
                    return defaultIllustration;
            }
        }

        [Serializable]
        private sealed class CardIllustrationBinding
        {
            [SerializeField] private string illustrationId;
            [SerializeField] private Sprite sprite;

            public string IllustrationId => illustrationId;
            public Sprite Sprite => sprite;
        }


        private HandCardInteraction ResolveHoveredSlot(
            IReadOnlyList<HandCardInteraction> slots,
            RectTransform root,
            Vector2 screenPosition)
        {
            if (slots == null || root == null)
            {
                return null;
            }

            var activeSlots = slots
                .Where(slot => slot != null && slot.gameObject.activeInHierarchy && !slot.IsDragging)
                .OrderBy(slot => stableSlotOrders.TryGetValue(slot, out var order) ? order : int.MaxValue)
                .ToArray();
            if (activeSlots.Length == 0)
            {
                return null;
            }

            var camera = GetEventCamera(root);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPosition, camera, out var localPoint))
            {
                return null;
            }

            var minY = float.PositiveInfinity;
            var maxY = float.NegativeInfinity;
            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            for (var i = 0; i < activeSlots.Length; i++)
            {
                var slot = activeSlots[i];
                var home = slot.HomeAnchoredPosition;
                var size = slot.HomeSizeDelta;
                minY = Mathf.Min(minY, home.y - size.y * 0.5f);
                maxY = Mathf.Max(maxY, home.y + size.y * 0.5f);
                minX = Mathf.Min(minX, home.x - size.x * 0.5f);
                maxX = Mathf.Max(maxX, home.x + size.x * 0.5f);
            }

            minY -= Mathf.Max(0f, cardHoverInputVerticalPadding);
            maxY += Mathf.Max(0f, cardHoverInputVerticalPadding);
            minX -= Mathf.Max(0f, cardHoverInputHorizontalPadding);
            maxX += Mathf.Max(0f, cardHoverInputHorizontalPadding);
            if (localPoint.y < minY || localPoint.y > maxY || localPoint.x < minX || localPoint.x > maxX)
            {
                return null;
            }

            if (activeSlots.Length == 1)
            {
                return activeSlots[0];
            }

            for (var i = 0; i < activeSlots.Length; i++)
            {
                var leftBoundary = i == 0
                    ? float.NegativeInfinity
                    : (activeSlots[i - 1].HomeAnchoredPosition.x + activeSlots[i].HomeAnchoredPosition.x) * 0.5f;
                var rightBoundary = i == activeSlots.Length - 1
                    ? float.PositiveInfinity
                    : (activeSlots[i].HomeAnchoredPosition.x + activeSlots[i + 1].HomeAnchoredPosition.x) * 0.5f;

                if (localPoint.x >= leftBoundary && localPoint.x < rightBoundary)
                {
                    return activeSlots[i];
                }
            }

            return null;
        }

        private void ApplyResolvedHover(HandCardInteraction hoveredSlot)
        {
            var resolved = hoveredSlot != null && hoveredSlot.gameObject.activeInHierarchy
                ? hoveredSlot
                : null;
            var previous = currentHoveredSlot;
            currentHoveredSlot = resolved;
            ApplyHoverToSlots(moveCardSlots, currentHoveredSlot);
            ApplyHoverToSlots(actionCardSlots, currentHoveredSlot);
            ApplyLaneRootLayerOrder(currentHoveredSlot);

            // Hand-card hover is lane-driven (not per-card pointer events), so notify the tutorial whenever
            // the hovered card changes so a "hover this card" step can advance.
            if (currentHoveredSlot != null && currentHoveredSlot != previous && lastController != null)
            {
                lastController.NotifyTutorialCardHover(currentHoveredSlot.Card);
            }
        }

        private static string ResolveTutorialCueSelectionKey(
            ICombatCardHudHost controller,
            IReadOnlyList<CombatCardSnapshot> cards)
        {
            var highlightCardId = controller?.TutorialHighlightCardId;
            if (string.IsNullOrEmpty(highlightCardId) || cards == null)
            {
                return null;
            }

            for (var i = 0; i < cards.Count; i++)
            {
                if (MatchesTutorialCardId(cards[i], highlightCardId))
                {
                    return cards[i].SelectionKey;
                }
            }

            return null;
        }

        private static bool MatchesTutorialCardId(CombatCardSnapshot card, string cardId)
        {
            var id = cardId.Trim();
            return string.Equals(card.Id?.Trim(), id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(card.CatalogSourceId?.Trim(), id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(card.SelectionKey?.Trim(), id, StringComparison.OrdinalIgnoreCase);
        }

        private static void ApplyHoverToSlots(IReadOnlyList<HandCardInteraction> slots, HandCardInteraction hoveredSlot)
        {
            if (slots == null)
            {
                return;
            }

            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.gameObject.activeInHierarchy || slot.IsDragging)
                {
                    continue;
                }

                slot.SetHoverFromLane(slot == hoveredSlot);
            }
        }

        private void ApplyLaneRootLayerOrder(HandCardInteraction hoveredSlot)
        {
            if (hoveredSlot == null || hoveredSlot.transform == null)
            {
                RestoreDefaultLaneRootLayerOrder();
                return;
            }

            var parent = hoveredSlot.transform.parent;
            if (parent != null)
            {
                parent.SetAsLastSibling();
            }

            hoveredSlot.transform.SetAsLastSibling();
        }

        private void RestoreDefaultLaneRootLayerOrder()
        {
            if (moveCardsRoot == null
                || actionCardsRoot == null
                || moveCardsRoot.parent == null
                || moveCardsRoot.parent != actionCardsRoot.parent)
            {
                return;
            }

            if (moveCardsRoot.GetSiblingIndex() > actionCardsRoot.GetSiblingIndex())
            {
                moveCardsRoot.SetSiblingIndex(actionCardsRoot.GetSiblingIndex());
            }
        }

        private static bool HasDraggingSlot(IReadOnlyList<HandCardInteraction> slots)
        {
            return slots != null && slots.Any(slot => slot != null && slot.gameObject.activeInHierarchy && slot.IsDragging);
        }

        private static bool HasPointerActiveSlot(IReadOnlyList<HandCardInteraction> slots, HandCardInteraction exclude = null)
        {
            return slots != null && slots.Any(slot =>
                slot != null && slot != exclude && slot.gameObject.activeInHierarchy && slot.IsPointerActive);
        }

        private static float GetScreenDistanceToHome(HandCardInteraction slot, Vector2 screenPosition)
        {
            var root = slot != null ? slot.transform.parent as RectTransform : null;
            if (root == null)
            {
                return float.PositiveInfinity;
            }

            var camera = GetEventCamera(root);
            var center = root.TransformPoint(slot.HomeAnchoredPosition);
            var screenCenter = RectTransformUtility.WorldToScreenPoint(camera, center);
            return Vector2.SqrMagnitude(screenPosition - screenCenter);
        }

        private static Camera GetEventCamera(RectTransform rect)
        {
            var canvas = rect != null ? rect.GetComponentInParent<Canvas>() : null;
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }

        private void RebuildSlotCache(RectTransform group, List<HandCardInteraction> target)
        {
            if (target == null)
            {
                return;
            }

            target.Clear();
            CacheCardSlots(group, target);
        }

        private void CacheCardSlots(RectTransform group, List<HandCardInteraction> target)
        {
            if (group == null || target == null)
            {
                return;
            }

            var slotRoots = group.Cast<Transform>()
                .Select(child => child as RectTransform)
                .Where(IsCardSlotRoot)
                .OrderBy(rect => rect.GetSiblingIndex())
                .ToArray();

            foreach (var cardRoot in slotRoots)
            {
                EnsureCardPointerRaycastTarget(cardRoot.gameObject);
                var interaction = cardRoot.GetComponent<HandCardInteraction>();
                if (interaction == null)
                {
                    interaction = cardRoot.gameObject.AddComponent<HandCardInteraction>();
                }

                interaction.ConfigureHoverMotion(cardHoverLiftY, cardHoverScale, cardDragScale);
                interaction.SetLaneHoverControlled(true);
                RegisterStableSlot(interaction);
            }

            foreach (var interaction in slotRoots
                .Select(root => root.GetComponent<HandCardInteraction>())
                .Where(interaction => interaction != null)
                .OrderBy(interaction => stableSlotOrders[interaction]))
            {
                target.Add(interaction);
            }
        }

        private static void EnsureCardPointerRaycastTarget(GameObject cardRoot)
        {
            if (cardRoot == null)
            {
                return;
            }

            var rootImage = cardRoot.GetComponent<Image>();
            if (rootImage != null)
            {
                rootImage.raycastTarget = true;
                return;
            }

            var overlay = cardRoot.GetComponentsInChildren<Image>(includeInactive: true)
                .FirstOrDefault(image => image != null && image.name == "Card_Frame_Overlay");
            if (overlay != null)
            {
                overlay.raycastTarget = true;
                return;
            }

            var firstGraphic = cardRoot.GetComponentInChildren<Graphic>(includeInactive: true);
            if (firstGraphic != null)
            {
                firstGraphic.raycastTarget = true;
                return;
            }

            var fallbackImage = cardRoot.AddComponent<Image>();
            fallbackImage.color = new Color(1f, 1f, 1f, 0.02f);
            fallbackImage.raycastTarget = true;
        }

        private void RegisterStableSlot(HandCardInteraction interaction)
        {
            if (interaction == null || stableSlotOrders.ContainsKey(interaction))
            {
                return;
            }

            stableSlotOrders.Add(interaction, nextStableSlotOrder++);
        }

        private void LayoutCardFan(int moveActive, int actionActive)
        {
            var moveSlots = GetLaneSlots("MoveCards");
            var actionSlots = GetLaneSlots("ActionCards");
            if (applyGeneratedLaneRootLayout)
            {
                CenterLaneContainer(moveCardsRoot ?? FindRect("MoveCards"));
                CenterLaneContainer(actionCardsRoot ?? FindRect("ActionCards"));
            }

            var m = Mathf.Clamp(moveActive, 0, moveSlots.Count);
            var a = Mathf.Clamp(actionActive, 0, actionSlots.Count);
            var n = m + a;
            var gap = (m > 0 && a > 0) ? cardFanGroupGap : 0f;
            var step = n > 1 ? Mathf.Min(cardFanStep, (2f * cardFanMaxHalfSpan - gap) / (n - 1)) : 0f;

            var fanX = new float[Mathf.Max(n, 1)];
            var mean = 0f;
            for (var g = 0; g < n; g++)
            {
                fanX[g] = g * step + (g >= m ? gap : 0f);
                mean += fanX[g];
            }

            if (n > 0)
            {
                mean /= n;
            }

            var half = 0.0001f;
            for (var g = 0; g < n; g++)
            {
                fanX[g] -= mean;
                half = Mathf.Max(half, Mathf.Abs(fanX[g]));
            }

            // 유지 카드는 부채 자리를 그대로 지킨다. 예전에는 버리기 구간에 오른쪽 끝으로 비켜세웠지만,
            // 그 판정이 "이번 갱신에 버려진 카드가 있는가"라서 카드를 <b>한 장 낼 때마다</b> 손패 전체가
            // 오른쪽으로 쏠렸다가 돌아왔다(실플레이 피드백 ①). 가로 이동 자체를 없애 문제를 뿌리째 지운다.
            for (var i = 0; i < moveSlots.Count; i++)
            {
                ApplyFanSlot(moveSlots[i], i < m, i < m ? fanX[i] : 0f, half, i);
            }
            for (var k = 0; k < actionSlots.Count; k++)
            {
                ApplyFanSlot(actionSlots[k], k < a, k < a ? fanX[m + k] : 0f, half, k);
            }

            var moveCenter = 0f;
            for (var g = 0; g < m; g++)
            {
                moveCenter += fanX[g];
            }
            if (m > 0)
            {
                moveCenter /= m;
            }

            var actionCenter = 0f;
            for (var k = 0; k < a; k++)
            {
                actionCenter += fanX[m + k];
            }
            if (a > 0)
            {
                actionCenter /= a;
            }

            PositionLaneLabel(moveSectionLabel, "MoveSectionLabel", moveCenter, half, m > 0);
            PositionLaneLabel(actionSectionLabel, "ActionSectionLabel", actionCenter, half, a > 0);
        }

        private void ApplyFanSlot(HandCardInteraction slot, bool active, float x, float half, int siblingIndex)
        {
            if (slot == null || slot.IsPointerActive)
            {
                return;
            }

            slot.gameObject.SetActive(active);
            if (!active)
            {
                return;
            }

            var rect = slot.transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            var frac = Mathf.Clamp(x / half, -1f, 1f);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = cardFanSize;
            rect.anchoredPosition = new Vector2(x, -(frac * frac) * cardFanArcDrop);
            rect.localEulerAngles = new Vector3(0f, 0f, -frac * cardFanMaxAngle);
            rect.localScale = Vector3.one;
            rect.SetSiblingIndex(siblingIndex);
            slot.ConfigureHoverMotion(cardHoverLiftY, cardHoverScale, cardDragScale);
            slot.SetLaneHoverControlled(true);
            slot.SetHomePoseFromLayout(rect.anchoredPosition, siblingIndex);
        }

        private void CenterLaneContainer(RectTransform container)
        {
            if (container == null)
            {
                return;
            }

            container.anchorMin = new Vector2(0.5f, 0.5f);
            container.anchorMax = new Vector2(0.5f, 0.5f);
            container.pivot = new Vector2(0.5f, 0.5f);
            container.anchoredPosition = ResolveLaneRootPosition(container);
            container.localScale = Vector3.one;
        }

        private Vector2 ResolveLaneRootPosition(RectTransform container)
        {
            if (!useSeparateCardRootPositions)
            {
                return new Vector2(0f, cardFanRestOffsetY);
            }

            if (container == moveCardsRoot)
            {
                return moveCardsRootPosition;
            }

            if (container == actionCardsRoot)
            {
                return actionCardsRootPosition;
            }

            return new Vector2(0f, cardFanRestOffsetY);
        }

        private void PositionLaneLabel(RectTransform cachedLabel, string objectName, float x, float half, bool show)
        {
            var label = cachedLabel != null ? cachedLabel : FindRect(objectName);
            if (label == null)
            {
                return;
            }

            label.gameObject.SetActive(show);
            if (!applyGeneratedLaneLabelLayout)
            {
                return;
            }

            label.anchorMin = new Vector2(0.5f, 0.5f);
            label.anchorMax = new Vector2(0.5f, 0.5f);
            label.pivot = new Vector2(0.5f, 0.5f);
            var frac = Mathf.Clamp(x / Mathf.Max(half, 0.0001f), -1f, 1f);
            var cardY = -(frac * frac) * cardFanArcDrop;
            label.anchoredPosition = new Vector2(x, cardY + cardFanSize.y * 0.5f + cardFanLabelOffsetY);
            label.localEulerAngles = new Vector3(0f, 0f, -frac * cardFanMaxAngle * cardFanLabelAngleScale);
        }

        private List<HandCardInteraction> GetLaneSlots(string containerName)
        {
            var result = new List<HandCardInteraction>();
            var container = FindRect(containerName);
            if (container == null)
            {
                return result;
            }

            var slots = container.Cast<Transform>()
                .Select(child => child as RectTransform)
                .Where(IsCardSlotRoot)
                .OrderBy(r => r.GetSiblingIndex())
                .Select(rect => rect.GetComponent<HandCardInteraction>())
                .Where(interaction => interaction != null)
                .ToArray();

            foreach (var interaction in slots)
            {
                RegisterStableSlot(interaction);
            }

            result.AddRange(slots.OrderBy(interaction => stableSlotOrders[interaction]));
            return result;
        }

        private RectTransform FindRect(string objectName)
        {
            return string.IsNullOrEmpty(objectName)
                ? null
                : GetComponentsInChildren<RectTransform>(includeInactive: true).FirstOrDefault(rect => rect.name == objectName);
        }

        private static bool IsCardSlotRoot(RectTransform rect)
        {
            if (rect == null)
            {
                return false;
            }

            return rect.name.StartsWith("Card_", StringComparison.Ordinal)
                || rect.name.StartsWith("MoveCard_", StringComparison.Ordinal)
                || rect.name.StartsWith("ActionCard_", StringComparison.Ordinal)
                || rect.GetComponent<HandCardInteraction>() != null;
        }

        private static bool IsCardInteractionEnabled(CombatCardSnapshot card, CombatState state)
        {
            if (state == null || card.IsDiscarded)
            {
                return false;
            }

            return card.IsUsable;
        }

        private static string GetCardStatusText(CombatCardSnapshot card, CombatState state)
        {
            // 상태 카드·봉인은 "이동 후 행동"으로 덮지 않는다. 그 카드는 행동 페이즈가 와도 못 쓰므로
            // 기다리면 풀린다고 읽히면 안 된다(GetUnavailableReason과 같은 순서).
            if (IsStatusCard(card) || IsSealedCard(card))
            {
                return card.Status;
            }

            if (!card.IsDiscarded && !card.IsUsable && state != null && state.Phase == CombatPhase.PlayerMovement && card.Kind != CombatCardKind.Move)
            {
                return "이동 후 행동";
            }

            return card.Status;
        }

        private static string GetUnavailableReason(CombatCardSnapshot card, CombatState state)
        {
            if (state == null)
            {
                return "전투가 초기화되지 않았습니다";
            }

            if (state.IsTerminal)
            {
                return "전투가 종료되었습니다";
            }

            if (card.IsDiscarded)
            {
                return "이미 사용한 카드입니다";
            }

            if (card.IsUsable)
            {
                return string.Empty;
            }

            // 상태 카드·봉인은 페이즈보다 앞선다 — CombatState.GetCardStatus가 같은 이유로 같은 순서를 쓴다.
            // 그 카드들은 어떤 페이즈에서도 못 쓰므로 "행동 페이즈에 사용할 수 있습니다"가 뜨면 기다리면
            // 풀리는 문제처럼 읽힌다. 전용 문장이 없으면 맨 아래 폴백으로 새어 라벨 문자열이 그대로 나온다.
            if (string.Equals(card.Status, CombatCardStatusText.StatusCard, StringComparison.Ordinal))
            {
                return "낼 수 없는 카드입니다";
            }

            if (string.Equals(card.Status, CombatCardStatusText.Sealed, StringComparison.Ordinal))
            {
                return "봉인되어 사용할 수 없습니다";
            }

            if (state.Phase == CombatPhase.PlayerMovement && card.Kind != CombatCardKind.Move)
            {
                return "행동 페이즈에 사용할 수 있습니다";
            }

            if (state.Phase == CombatPhase.PlayerAction && card.Kind == CombatCardKind.Move)
            {
                return "이동 페이즈에만 사용할 수 있습니다";
            }

            if (string.Equals(card.Status, CombatCardStatusText.Stunned, StringComparison.Ordinal))
            {
                return "기절 상태입니다";
            }

            if (string.Equals(card.Status, CombatCardStatusText.Immobilized, StringComparison.Ordinal))
            {
                return "속박 상태입니다";
            }

            if (string.Equals(card.Status, CombatCardStatusText.Disarmed, StringComparison.Ordinal))
            {
                return "무장 해제 상태입니다";
            }

            if (string.Equals(card.Status, CombatCardStatusText.MomentumLocked, StringComparison.Ordinal))
            {
                return "추진력 효과로 사용 불가";
            }

            if (string.Equals(card.Status, CombatCardStatusText.NotEnoughKi, StringComparison.Ordinal))
            {
                return "기력이 부족합니다";
            }

            if (CombatCardStatusText.IsAttackOutOfRange(card.Status))
            {
                return "사거리 내 대상이 없습니다";
            }

            if (!string.IsNullOrWhiteSpace(card.Status) &&
                !string.Equals(card.Status, CombatCardStatusText.Waiting, StringComparison.Ordinal))
            {
                return card.Status;
            }

            return "지금 사용할 수 없습니다";
        }

        // Highlight used when a card's Ki cost was modified at runtime (e.g. 최후의 일격 / Final Blow's
        // spend-all-Ki rule). Unmodified cards keep their prefab-authored cost color, captured per slot.
        // Built-in default; routed through the assigned UiThemeAsset when present (value kept identical
        // to the shipped colour so adopting a theme is visually a no-op).
        private static readonly Color DefaultModifiedCostColor = new Color(0.36f, 0.62f, 1f, 1f);
        private Color ModifiedCostColor => theme != null ? theme.CostModified : DefaultModifiedCostColor;
        private readonly Dictionary<TMP_Text, Color> originalCostColors = new Dictionary<TMP_Text, Color>();

        private void ApplyCostColor(Transform root, CombatCardSnapshot card)
        {
            var costText = FindText(root, "CostText_TMP");
            if (costText == null)
            {
                return;
            }

            if (!originalCostColors.ContainsKey(costText))
            {
                originalCostColors[costText] = costText.color;
            }

            costText.color = card.IsCostModified ? ModifiedCostColor : originalCostColors[costText];
        }

        // 상태 카드는 전용 프레임 때문에 배경이 짙은 오염 보라다. 프리팹의 기본 설명 색은 밝은 배경을
        // 전제한 어두운 회색이라 그 위에서 **글자가 아예 안 보인다**(2026-08-02 실플레이 판정).
        // 슬롯은 풀링돼 상태 카드와 일반 카드가 같은 TMP를 번갈아 쓰므로, 프레임 스프라이트와 똑같이
        // 원래 색을 슬롯별로 캐시해 두고 일반 카드가 다시 앉으면 되돌린다 — 캐시가 없으면 한 번
        // 상태 카드가 지나간 슬롯의 설명이 영영 흰색으로 남는다.
        private readonly Dictionary<TMP_Text, Color> originalDescriptionColors = new Dictionary<TMP_Text, Color>();
        private static readonly Color DefaultStatusCardDescriptionColor = new Color(0.92f, 0.96f, 1f, 1f);
        private Color StatusCardDescriptionColor => theme != null ? theme.TextPrimary : DefaultStatusCardDescriptionColor;

        private void ApplyDescriptionColor(Transform root, CombatCardSnapshot card)
        {
            var descriptionText = FindText(root, "DescriptionText_TMP");
            if (descriptionText == null)
            {
                return;
            }

            if (!originalDescriptionColors.ContainsKey(descriptionText))
            {
                originalDescriptionColors[descriptionText] = descriptionText.color;
            }

            descriptionText.color = CardFrontPresentationFormatting.IsStatusCard(card)
                ? StatusCardDescriptionColor
                : originalDescriptionColors[descriptionText];
        }

        private static void SetText(Transform root, string objectName, string value)
        {
            var text = FindText(root, objectName);
            if (text == null)
            {
                return;
            }

            text.text = value;
        }

        private static TMP_Text FindText(Transform root, string objectName)
        {
            return root != null
                ? root.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .FirstOrDefault(t => t.name == objectName)
                : null;
        }

        // Shows the card's range value/icon, hiding both for self-targeted cards (no tile to aim at).
        // 갈림길 카드(성스러운 빛처럼 self 분기 보유)도 패에서는 self로 취급해 사거리 아이콘을 숨긴다 — 분기별
        // 사거리는 선택 오버레이에서만 노출된다.
        private static void RefreshCardRange(Transform root, CombatCardSnapshot card)
        {
            CardFrontPresentationFormatting.RefreshCardRange(root, card);
        }

    }

    [DisallowMultipleComponent]
    internal sealed class CardLaneHomeInputProxy : MonoBehaviour, ICanvasRaycastFilter, IPointerDownHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
    {
        public const string ProxyName = "CardLaneHomeInputProxy";

        private GameplayCardLaneView owner;

        public void Configure(GameplayCardLaneView cardLaneView)
        {
            owner = cardLaneView;
        }

        public bool IsRaycastLocationValid(Vector2 screenPosition, Camera eventCamera)
        {
            return owner != null && owner.IsHomeInputRaycastValid(screenPosition);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            owner?.HandleHomeInputPointerDown(eventData);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            owner?.HandleHomeInputBeginDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            owner?.HandleHomeInputDrag(eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            owner?.HandleHomeInputEndDrag(eventData);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            owner?.HandleHomeInputPointerClick(eventData);
        }
    }
}

