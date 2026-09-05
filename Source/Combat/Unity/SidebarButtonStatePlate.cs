using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Draws a rounded state plate behind a sidebar icon button and fades it in on pointer hover, holding it
    /// lit while that button's callout is the open one (P6 T2). The sidebar buttons shipped with an
    /// invisible (alpha 0) background Image and a SpriteSwap transition on the icon alone, so the column
    /// gave no hover affordance and no "this panel is open" state at all.
    ///
    /// Self-building like <see cref="UiButtonHoverGlow"/>: callers only AddComponent it. The plate reuses the
    /// shared procedural panel skin (<see cref="UiProceduralPanel"/>), so it inherits the same fill/border
    /// language as the callouts it opens, with no bitmap. Non-breaking: if the skin material is missing the
    /// plate keeps a plain tinted Image, and the button itself is untouched — its icon sprite swap still runs.
    ///
    /// Fades on <see cref="Time.unscaledDeltaTime"/> so the plate still animates while the game is paused.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SidebarPanelButton))]
    public sealed class SidebarButtonStatePlate : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const string PlateName = "Sidebar Button Plate";

        // Lighter than the column (#303468) so the plate reads as a raised cell, rimmed with the same gold
        // as the column's edge rule and the callout borders.
        private static readonly Color PlateFill = new Color32(0x45, 0x4B, 0x96, 0xD9);
        private static readonly Color PlateBorder = new Color32(0xC9, 0xA2, 0x27, 0xB0);

        [SerializeField] private float hoverAlpha = 0.55f;
        [SerializeField] private float selectedAlpha = 1f;
        [SerializeField] private float fadeSpeed = 14f;
        [Tooltip("Inset of the plate from the button rect, in px (x = horizontal, y = vertical).")]
        [SerializeField] private Vector2 inset = new Vector2(8f, 3f);

        private SidebarPanelButton panelButton;
        private Selectable selectable;
        private Image plateImage;
        private bool isHovered;
        private bool built;
        private bool snapNextFrame;

        private void Awake() => Build();

        private void OnEnable()
        {
            Build();
            isHovered = false;
            snapNextFrame = true;
            SetPlateAlpha(0f);
        }

        private void OnDisable()
        {
            isHovered = false;
            SetPlateAlpha(0f);
        }

        public void OnPointerEnter(PointerEventData eventData) => isHovered = true;

        public void OnPointerExit(PointerEventData eventData) => isHovered = false;

        private void Update()
        {
            if (plateImage == null)
            {
                return;
            }

            var interactable = selectable == null || selectable.IsInteractable();
            float target;
            if (!interactable)
            {
                target = 0f;
            }
            else if (IsSelected())
            {
                target = selectedAlpha;
            }
            else
            {
                target = isHovered ? hoverAlpha : 0f;
            }

            // Snap on the first frame after enable: UiProceduralPanel.Apply resets the Image tint to opaque
            // white when it re-applies on enable, and lerping down from there would flash the plate.
            var current = snapNextFrame ? target : plateImage.color.a;
            snapNextFrame = false;
            SetPlateAlpha(Mathf.Lerp(current, target, Time.unscaledDeltaTime * fadeSpeed));
        }

        // Alpha rides the Image's vertex colour, which the panel shader multiplies into both fill and border.
        // Deliberately not a CanvasGroup: a CanvasGroup added to the plate during Awake does not survive.
        private void SetPlateAlpha(float alpha)
        {
            if (plateImage == null)
            {
                return;
            }

            var c = plateImage.color;
            plateImage.color = new Color(c.r, c.g, c.b, alpha);
        }

        // The settings button routes its click to the pause menu through SidebarPanelButton's external
        // handler, so no callout is ever marked active for it; it simply keeps the hover-only state.
        private bool IsSelected()
        {
            var controller = panelButton != null ? panelButton.Controller : null;
            return controller != null
                && !string.IsNullOrEmpty(panelButton.PanelKey)
                && controller.ActiveKey == panelButton.PanelKey;
        }

        // Builds (or reuses) the single plate child. Safe to call repeatedly.
        private void Build()
        {
            if (built)
            {
                return;
            }

            built = true;
            panelButton = GetComponent<SidebarPanelButton>();
            selectable = GetComponent<Selectable>();

            var existing = transform.Find(PlateName) as RectTransform;
            if (existing == null)
            {
                var go = new GameObject(PlateName, typeof(RectTransform));
                existing = (RectTransform)go.transform;
                existing.SetParent(transform, false);
            }

            existing.anchorMin = Vector2.zero;
            existing.anchorMax = Vector2.one;
            existing.pivot = new Vector2(0.5f, 0.5f);
            existing.offsetMin = new Vector2(inset.x, inset.y);
            existing.offsetMax = new Vector2(-inset.x, -inset.y);
            existing.localScale = Vector3.one;
            existing.SetAsFirstSibling(); // behind the icon and label

            // A LayoutGroup on the button would otherwise resize/reposition the plate.
            var layoutElement = existing.GetComponent<LayoutElement>() ?? existing.gameObject.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;

            plateImage = existing.GetComponent<Image>() ?? existing.gameObject.AddComponent<Image>();
            plateImage.sprite = null;
            plateImage.type = Image.Type.Simple;
            plateImage.raycastTarget = false;

            var skin = existing.GetComponent<UiProceduralPanel>() ?? existing.gameObject.AddComponent<UiProceduralPanel>();
            skin.Configure(PlateFill, PlateBorder, 1.5f, 10f);
            skin.ConfigureTexture(0.14f, new Color(1f, 0.96f, 0.88f, 1f), 0.6f, 2f);

            SetPlateAlpha(0f);
        }
    }
}
