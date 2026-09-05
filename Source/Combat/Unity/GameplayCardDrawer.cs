using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace SeoulPlayup.Combat.Unity
{
    public sealed class GameplayCardDrawer : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private string drawerKind;
        [SerializeField] private Vector2 collapsedAnchoredPosition;
        [SerializeField] private Vector2 expandedAnchoredPosition;
        [SerializeField] private bool hoverRevealsDrawer;
        [SerializeField] private float revealSpeed = 16f;
        private RectTransform rectTransform;
        private bool isHovered;
        private bool isPointerInside;

        public string DrawerKind => string.IsNullOrEmpty(drawerKind) ? InferDrawerKindFromName() : drawerKind;
        public Vector2 CollapsedAnchoredPosition => collapsedAnchoredPosition == Vector2.zero ? ((RectTransform)transform).anchoredPosition : collapsedAnchoredPosition;
        public Vector2 ExpandedAnchoredPosition => expandedAnchoredPosition == Vector2.zero ? CollapsedAnchoredPosition + new Vector2(0f, 160f) : expandedAnchoredPosition;
        public bool HoverRevealsDrawer => hoverRevealsDrawer || name.Contains("Drawer");

        private void Awake()
        {
            rectTransform = (RectTransform)transform;
            rectTransform.anchoredPosition = CollapsedAnchoredPosition;
        }

        private void Update()
        {
            PollMouseHover();

            var target = isHovered || isPointerInside ? ExpandedAnchoredPosition : CollapsedAnchoredPosition;
            RectTransform.anchoredPosition = Vector2.Lerp(RectTransform.anchoredPosition, target, Time.unscaledDeltaTime * revealSpeed);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (HoverRevealsDrawer)
            {
                isHovered = true;
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
        }

        public void PreviewHover(bool hovered)
        {
            isHovered = hovered;
            isPointerInside = hovered;
            RectTransform.anchoredPosition = hovered ? ExpandedAnchoredPosition : CollapsedAnchoredPosition;
        }

        public bool ContainsScreenPoint(Vector2 screenPoint)
        {
            return RectTransformUtility.RectangleContainsScreenPoint(RectTransform, screenPoint, null);
        }

        private void PollMouseHover()
        {
            var mouse = Mouse.current;
            if (mouse == null || !HoverRevealsDrawer)
            {
                isPointerInside = false;
                return;
            }

            isPointerInside = ContainsScreenPoint(mouse.position.ReadValue());
        }

        private RectTransform RectTransform => rectTransform != null ? rectTransform : rectTransform = (RectTransform)transform;

        public void Bind(string kind, Vector2 collapsedPosition, Vector2 expandedPosition, bool hoverReveals)
        {
            drawerKind = kind;
            collapsedAnchoredPosition = collapsedPosition;
            expandedAnchoredPosition = expandedPosition;
            hoverRevealsDrawer = hoverReveals;
        }

        private string InferDrawerKindFromName()
        {
            if (name.Contains("Move"))
            {
                return "이동 카드";
            }

            if (name.Contains("Action"))
            {
                return "행동 카드";
            }

            return name;
        }
    }
}
