using System;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class HandCardInteraction : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private static readonly Color HoverOutlineColor = new Color(1f, 0.86f, 0.28f, 0.92f);
        private static readonly Color SelectedOutlineColor = new Color(0.36f, 0.93f, 1f, 0.96f);
        private static readonly Color DragOutlineColor = new Color(1f, 0.96f, 0.58f, 1f);
        private static readonly Color SelectionCandidateOutlineColor = new Color(1f, 0.82f, 0.22f, 0.92f);
        private static readonly Color SelectionPickedOutlineColor = new Color(1f, 0.48f, 0.08f, 1f);
        private static readonly Vector2 OutlineDistance = new Vector2(5f, -5f);
        private static HandCardInteraction activeHover;
        [SerializeField] private float hoverLiftY;
        private float externalLiftY;
        [SerializeField] private float hoverScale = 1.12f;
        [SerializeField] private float dragScale = 1.13f;
        [SerializeField] private bool promoteOnPointerInteraction = true;
        [SerializeField] private bool cardHoverGlowEnabled = true;

        [SerializeField] private CardHoverGlow hoverGlow;

        private RectTransform rectTransform;
        private Image image;
        private Outline outline;
        private Action<HandCardInteraction, CombatCardSnapshot, Vector2> releaseHandler;
        private Action<HandCardInteraction, CombatCardSnapshot> clickHandler;
        private Action<HandCardInteraction, CombatCardSnapshot> hoverHandler;
        private Action<HandCardInteraction, CombatCardSnapshot> invalidClickHandler;
        private CombatCardSnapshot card;
        private bool isPlayable;
        private bool isSelected;
        private bool isHovering;
        private bool isDragging;
        private bool laneHoverControlled;
        private HandCardSelectionVisualState handSelectionVisualState;
        private Vector3 pointerWorldOffset;
        private Vector2 homeAnchoredPosition;
        private int homeSiblingIndex;
        private float homeScreenTopY;
        private readonly Vector3[] homeWorldCorners = new Vector3[4];
        private readonly Vector3[] currentWorldCorners = new Vector3[4];
        private bool hasHomeWorldCorners;

        public bool IsPlayable => isPlayable;

        public bool IsDragging => isDragging;

        public bool IsHovering => isHovering;

        public bool IsPointerActive => isHovering || isDragging;

        public string CurrentCardSelectionKey => card.SelectionKey;

        public CombatCardSnapshot Card => card;

        // Orange tutorial cue glow pointing at the card the player must hover/select this step.
        public void SetTutorialCue(bool on)
        {
            if (hoverGlow != null)
            {
                hoverGlow.SetTutorialHighlight(on);
            }
        }

        // 상태 카드(오염된 카드)에 씌우는 불길한 아우라. 봉인은 아우라가 아니라 빨간 X이므로
        // CardUnavailableFeedback이 따로 처리한다.
        public void SetOminousAura(bool on)
        {
            // ⚠️hoverGlow는 프리팹에 직렬화돼 있지 않고 CacheComponents가 늦게 채운다. 이 호출은
            //   Configure보다 앞서 들어오므로 여기서 직접 보장하지 않으면 첫 갱신에서 조용히 무시된다.
            CacheComponents();
            if (hoverGlow != null)
            {
                hoverGlow.SetOminousAura(on);
            }
        }

        public float HomeScreenTopY => homeScreenTopY;

        public Vector2 HomeAnchoredPosition => homeAnchoredPosition;

        public Vector2 HomeSizeDelta => rectTransform != null ? rectTransform.sizeDelta : Vector2.zero;

        private void OnDisable()
        {
            if (activeHover == this)
            {
                activeHover = null;
            }

            isHovering = false;
            isDragging = false;
            RefreshGlowState(immediate: true);
        }

        private void Update()
        {
            if (laneHoverControlled || !isHovering || isDragging)
            {
                return;
            }

            if (!TryGetPointerScreenPosition(out var screenPosition)
                || !IsPointerInsideHoverHoldArea(screenPosition))
            {
                ClearHover();
            }
        }

        public void ConfigureHoverLift(float liftY)
        {
            hoverLiftY = Mathf.Max(0f, liftY);
        }

        /// <summary>
        /// 레인이 화면 아래로 물러나 있는 동안 <b>이 카드만</b> 제자리에 붙들어 두는 보정(대상 선택 중인
        /// 핀 카드 — 2026-09-05 실플레이 #8). 호버 리프트와 별개 축이라 둘이 더해진다. 홈 위치는
        /// 건드리지 않으므로 0으로 되돌리면 레인과 함께 제자리로 내려온다.
        /// </summary>
        public float ExternalLiftY => externalLiftY;

        public void SetExternalLift(float liftY)
        {
            var clamped = Mathf.Max(0f, liftY);
            if (Mathf.Approximately(externalLiftY, clamped))
            {
                return;
            }

            externalLiftY = clamped;
            RefreshVisualTransform();
        }

        public void ConfigureHoverMotion(float liftY, float hoverScaleMultiplier, float dragScaleMultiplier)
        {
            hoverLiftY = Mathf.Max(0f, liftY);
            hoverScale = Mathf.Max(0.01f, hoverScaleMultiplier);
            dragScale = Mathf.Max(hoverScale, dragScaleMultiplier);
        }

        public void ConfigurePointerLayerPromotion(bool enabled)
        {
            promoteOnPointerInteraction = enabled;
        }

        public void ConfigureCardHoverGlowEnabled(bool enabled)
        {
            cardHoverGlowEnabled = enabled;

            if (!cardHoverGlowEnabled && hoverGlow != null)
            {
                hoverGlow.SetDisabled(true);
                hoverGlow.HideGlowImmediately();
            }
        }

        public void Initialize(
            Action<HandCardInteraction, CombatCardSnapshot, Vector2> onReleased,
            Action<HandCardInteraction, CombatCardSnapshot> onClicked = null,
            Action<HandCardInteraction, CombatCardSnapshot> onHovered = null,
            Action<HandCardInteraction, CombatCardSnapshot> onInvalidClicked = null)
        {
            releaseHandler = onReleased;
            clickHandler = onClicked;
            hoverHandler = onHovered;
            invalidClickHandler = onInvalidClicked;
            CacheComponents();
            if (!isDragging && !isHovering)
            {
                CacheHomePose();
            }
        }

        public void SetLaneHoverControlled(bool controlled)
        {
            laneHoverControlled = controlled;
        }

        public void SetHomePoseFromLayout(Vector2 anchoredPosition, int siblingIndex)
        {
            CacheComponents();
            if (isDragging)
            {
                return;
            }

            homeAnchoredPosition = anchoredPosition;
            homeSiblingIndex = siblingIndex;
            if (!isHovering)
            {
                rectTransform.anchoredPosition = homeAnchoredPosition;
                transform.SetSiblingIndex(homeSiblingIndex);
            }

            CacheHomePose();
            RefreshVisualTransform();
        }

        public void SetHoverFromLane(bool hovered)
        {
            CacheComponents();
            laneHoverControlled = true;
            if (isDragging)
            {
                return;
            }

            if (hovered)
            {
                var wasHovering = isHovering;
                ClearOtherActiveHover();
                isHovering = true;
                activeHover = this;
                PromoteToLastSibling();
                if (!wasHovering)
                {
                    hoverHandler?.Invoke(this, card);
                }
            }
            else
            {
                isHovering = false;
                if (activeHover == this)
                {
                    activeHover = null;
                }
                RestoreSiblingOrder();
            }

            RefreshVisualTransform();
            RefreshOutline();
            RefreshGlowState();
        }

        public void Configure(CombatCardSnapshot snapshot, bool playable, bool selected, HandCardSelectionVisualState selectionVisualState = HandCardSelectionVisualState.Normal)
        {
            CacheComponents();
            card = snapshot;
            isPlayable = playable;
            isSelected = selected;
            handSelectionVisualState = selectionVisualState;
            if (!isDragging && !isHovering)
            {
                homeAnchoredPosition = rectTransform.anchoredPosition - Vector2.up * externalLiftY;
                homeSiblingIndex = transform.GetSiblingIndex();
                CacheHomePose();
            }

            if (image != null)
            {
                image.raycastTarget = true;
            }

            RefreshOutline();
            RefreshVisualTransform();
            RefreshGlowState(immediate: true);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (laneHoverControlled || isDragging)
            {
                return;
            }

            var wasHovering = isHovering;
            if (!wasHovering)
            {
                ClearOtherActiveHover();
                CacheHomePose();
                homeSiblingIndex = transform.GetSiblingIndex();
            }

            isHovering = true;
            activeHover = this;
            PromoteToLastSibling();
            if (!wasHovering)
            {
                hoverHandler?.Invoke(this, card);
            }
            RefreshVisualTransform();
            RefreshOutline();
            RefreshGlowState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (laneHoverControlled || isDragging)
            {
                return;
            }

            if (isHovering && IsPointerInsideHoverHoldArea(eventData.position))
            {
                return;
            }

            ClearHover();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isPlayable || (laneHoverControlled && !isHovering))
            {
                return;
            }

            if (!isHovering)
            {
                CacheHomePose();
            }

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out var pointerWorldPosition))
            {
                pointerWorldOffset = rectTransform.position - pointerWorldPosition;
            }
            else
            {
                pointerWorldOffset = Vector3.zero;
            }

            PromoteToLastSibling();
            RefreshOutline();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!isPlayable || (laneHoverControlled && !isHovering))
            {
                return;
            }

            isDragging = true;
            isHovering = false;
            PromoteToLastSibling();
            RefreshVisualTransform();
            RefreshOutline();
            RefreshGlowState();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!isPlayable || !isDragging)
            {
                return;
            }

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(rectTransform, eventData.position, eventData.pressEventCamera, out var pointerWorldPosition))
            {
                rectTransform.position = pointerWorldPosition + pointerWorldOffset;
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            CompleteDrag(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (isDragging)
            {
                return;
            }

            SnapHome();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (isDragging)
            {
                return;
            }

            if (!isPlayable)
            {
                invalidClickHandler?.Invoke(this, card);
                return;
            }

            var clickedCard = card;
            SnapHome();
            clickHandler?.Invoke(this, clickedCard);
        }

        public void CancelPointerStateForRefresh()
        {
            CacheComponents();
            isDragging = false;
            isHovering = false;
            if (activeHover == this)
            {
                activeHover = null;
            }

            rectTransform.anchoredPosition = homeAnchoredPosition;
            RestoreSiblingOrder();
            RefreshVisualTransform();
            RefreshOutline();
            RefreshGlowState();
        }

        private void CompleteDrag(PointerEventData eventData)
        {
            if (!isDragging)
            {
                return;
            }

            var releasePosition = eventData.position;
            SnapHome();
            releaseHandler?.Invoke(this, card, releasePosition);
        }

        private void SnapHome()
        {
            isDragging = false;
            isHovering = false;
            if (activeHover == this)
            {
                activeHover = null;
            }
            rectTransform.anchoredPosition = homeAnchoredPosition;
            RestoreSiblingOrder();
            RefreshVisualTransform();
            RefreshOutline();
            RefreshGlowState();
        }

        private void CacheHomePose()
        {
            CacheComponents();
            if (rectTransform == null)
            {
                return;
            }

            // 홈은 외부 리프트를 뺀 자리다 — 핀 카드가 떠 있는 동안 홈을 다시 재면 리프트가 홈에 눌어붙어
            // 호버할 때마다 한 번 더 올라간다.
            homeAnchoredPosition = rectTransform.anchoredPosition - Vector2.up * externalLiftY;
            homeSiblingIndex = transform.GetSiblingIndex();

            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            for (var i = 0; i < corners.Length; i++)
            {
                homeWorldCorners[i] = corners[i];
            }
            hasHomeWorldCorners = true;
            homeScreenTopY = Mathf.Max(
                RectTransformUtility.WorldToScreenPoint(null, corners[1]).y,
                RectTransformUtility.WorldToScreenPoint(null, corners[2]).y);
        }

        private void ClearHover()
        {
            isHovering = false;
            if (activeHover == this)
            {
                activeHover = null;
            }
            RestoreSiblingOrder();
            RefreshVisualTransform();
            RefreshOutline();
            RefreshGlowState();
        }

        private void ClearOtherActiveHover()
        {
            if (activeHover == null || activeHover == this)
            {
                return;
            }

            activeHover.ClearHover();
        }

        private void RestoreSiblingOrder()
        {
            if (transform.parent == null)
            {
                return;
            }

            var maxIndex = transform.parent.childCount - 1;
            transform.SetSiblingIndex(Mathf.Clamp(homeSiblingIndex, 0, maxIndex));
        }

        private void PromoteToLastSibling()
        {
            if (promoteOnPointerInteraction)
            {
                transform.SetAsLastSibling();
            }
        }

        private void RefreshVisualTransform()
        {
            if (rectTransform == null)
            {
                return;
            }

            if (isDragging)
            {
                rectTransform.localScale = Vector3.one * dragScale;
                return;
            }

            rectTransform.anchoredPosition = homeAnchoredPosition
                + Vector2.up * ((isHovering ? hoverLiftY : 0f) + externalLiftY);
            rectTransform.localScale = isHovering ? Vector3.one * hoverScale : Vector3.one;
        }

        private bool IsPointerInsideHoverHoldArea(Vector2 screenPosition)
        {
            if (rectTransform == null || !hasHomeWorldCorners)
            {
                return false;
            }

            var camera = GetEventCamera();
            if (RectTransformUtility.RectangleContainsScreenPoint(rectTransform, screenPosition, camera))
            {
                return true;
            }

            rectTransform.GetWorldCorners(currentWorldCorners);
            var min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            EncapsulateScreenCorners(homeWorldCorners, camera, ref min, ref max);
            EncapsulateScreenCorners(currentWorldCorners, camera, ref min, ref max);

            const float padding = 8f;
            min -= Vector2.one * padding;
            max += Vector2.one * padding;
            return screenPosition.x >= min.x
                && screenPosition.x <= max.x
                && screenPosition.y >= min.y
                && screenPosition.y <= max.y;
        }

        private void EncapsulateScreenCorners(Vector3[] corners, Camera camera, ref Vector2 min, ref Vector2 max)
        {
            for (var i = 0; i < corners.Length; i++)
            {
                var screen = RectTransformUtility.WorldToScreenPoint(camera, corners[i]);
                min = Vector2.Min(min, screen);
                max = Vector2.Max(max, screen);
            }
        }

        private Camera GetEventCamera()
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return canvas.worldCamera;
        }

        private static bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                screenPosition = mouse.position.ReadValue();
                return true;
            }

            screenPosition = default;
            return false;
        }

        private void RefreshGlowState(bool immediate = false)
        {
            if (hoverGlow == null)
            {
                return;
            }

            if (!cardHoverGlowEnabled)
            {
                hoverGlow.SetDisabled(true);
                hoverGlow.HideGlowImmediately();
                return;
            }

            hoverGlow.SetDisabled(!isPlayable);
            hoverGlow.SetSelected(isSelected);
            hoverGlow.SetHovered(isHovering && !isDragging);
            hoverGlow.SetHandCardSelectionVisualState(handSelectionVisualState);

            if (immediate)
            {
                hoverGlow.ApplyImmediateState();
            }
        }

        private void RefreshOutline()
        {
            if (outline == null)
            {
                return;
            }

            outline.enabled = isPlayable && (isDragging || isHovering || isSelected || handSelectionVisualState == HandCardSelectionVisualState.Candidate || handSelectionVisualState == HandCardSelectionVisualState.Picked);
            outline.effectColor = ResolveOutlineColor();
            outline.effectDistance = OutlineDistance;
        }

        private Color ResolveOutlineColor()
        {
            if (isDragging)
            {
                return DragOutlineColor;
            }

            if (handSelectionVisualState == HandCardSelectionVisualState.Picked)
            {
                return SelectionPickedOutlineColor;
            }

            if (handSelectionVisualState == HandCardSelectionVisualState.Candidate)
            {
                return SelectionCandidateOutlineColor;
            }

            return isSelected ? SelectedOutlineColor : HoverOutlineColor;
        }

        private void CacheComponents()
        {
            if (rectTransform == null)
            {
                rectTransform = GetComponent<RectTransform>();
            }

            if (image == null)
            {
                image = GetComponent<Image>();
            }

            if (outline == null)
            {
                outline = GetComponent<Outline>() ?? gameObject.AddComponent<Outline>();
                outline.useGraphicAlpha = false;
            }

            if (hoverGlow == null)
            {
                hoverGlow = GetComponent<CardHoverGlow>();
            }
        }
    }
}



