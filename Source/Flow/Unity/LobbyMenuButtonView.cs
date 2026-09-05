using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Flow.Unity
{
    /// <summary>
    /// Drives the lobby main-menu button look: an ink plate with a gold-leaf rim that warms up on
    /// hover. Unity's built-in <see cref="Button"/> colour tint can only drive a single graphic, so
    /// the plate / rim / inner-glow / label are lerped here instead and the owning button is left on
    /// <see cref="Selectable.Transition.None"/>.
    /// </summary>
    [RequireComponent(typeof(Button))]
    public sealed class LobbyMenuButtonView : MonoBehaviour,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        [Header("Graphics")]
        [SerializeField] private Image plateImage;
        [SerializeField] private Image borderImage;
        [SerializeField] private Image glowImage;
        [SerializeField] private TMP_Text label;

        [Header("Plate")]
        [SerializeField] private Color plateNormal = new Color32(0x15, 0x0F, 0x22, 0xB8);
        [SerializeField] private Color plateHighlighted = new Color32(0x1E, 0x16, 0x30, 0xCC);
        [SerializeField] private Color platePressed = new Color32(0x0D, 0x09, 0x16, 0xD9);
        [SerializeField] private Color plateDisabled = new Color32(0x14, 0x11, 0x1B, 0x8C);

        [Header("Gold rim")]
        [SerializeField] private Color borderNormal = new Color32(0xC9, 0xA2, 0x27, 0xBF);
        [SerializeField] private Color borderHighlighted = new Color32(0xFF, 0xD9, 0x8A, 0xFF);
        [SerializeField] private Color borderPressed = new Color32(0xE8, 0xC4, 0x6A, 0xFF);
        [SerializeField] private Color borderDisabled = new Color32(0x6B, 0x5F, 0x42, 0x66);

        // The glow renders additively (Blend One One), which adds RGB to the frame and never reads
        // alpha — so intensity has to be dialled in through the RGB channels, and black means "off".
        [Header("Inner glow (additive)")]
        [SerializeField] private Color glowNormal = new Color32(0x00, 0x00, 0x00, 0xFF);
        [SerializeField] private Color glowHighlighted = new Color32(0x2E, 0x24, 0x10, 0xFF);
        [SerializeField] private Color glowPressed = new Color32(0x47, 0x38, 0x19, 0xFF);

        [Header("Label")]
        [SerializeField] private Color labelNormal = new Color32(0xF0, 0xE2, 0xC4, 0xFF);
        [SerializeField] private Color labelHighlighted = new Color32(0xFF, 0xF3, 0xD6, 0xFF);
        [SerializeField] private Color labelPressed = new Color32(0xFF, 0xE9, 0xB0, 0xFF);
        [SerializeField] private Color labelDisabled = new Color32(0x8A, 0x82, 0x72, 0x8C);
        [SerializeField] private float labelSpacingNormal = 6f;
        [SerializeField] private float labelSpacingHighlighted = 11f;

        [Header("Motion")]
        [SerializeField] private float scaleNormal = 1f;
        [SerializeField] private float scaleHighlighted = 1.015f;
        [SerializeField] private float scalePressed = 0.99f;
        [SerializeField] private float blendSpeed = 12f;

        private Button button;
        private RectTransform rect;
        private bool pointerInside;
        private bool selected;
        private bool pressed;
        private bool wasInteractable = true;
        private bool snapped;

        private void Awake()
        {
            button = GetComponent<Button>();
            rect = (RectTransform)transform;
        }

        private void OnEnable()
        {
            pointerInside = false;
            pressed = false;
            selected = false;
            snapped = false;
        }

        private void Update()
        {
            var interactable = button != null && button.IsInteractable();
            if (interactable != wasInteractable)
            {
                wasInteractable = interactable;
                if (!interactable)
                {
                    pointerInside = false;
                    pressed = false;
                    selected = false;
                }
            }

            // The first frame snaps so a freshly enabled panel never fades in from the previous state.
            var t = snapped ? 1f - Mathf.Exp(-blendSpeed * Time.unscaledDeltaTime) : 1f;
            snapped = true;
            ApplyState(interactable, t);
        }

        private void ApplyState(bool interactable, float t)
        {
            Color plate, border, glow, text;
            float spacing, scale;

            if (!interactable)
            {
                plate = plateDisabled;
                border = borderDisabled;
                glow = glowNormal;
                text = labelDisabled;
                spacing = labelSpacingNormal;
                scale = scaleNormal;
            }
            else if (pressed)
            {
                plate = platePressed;
                border = borderPressed;
                glow = glowPressed;
                text = labelPressed;
                spacing = labelSpacingHighlighted;
                scale = scalePressed;
            }
            else if (pointerInside || selected)
            {
                plate = plateHighlighted;
                border = borderHighlighted;
                glow = glowHighlighted;
                text = labelHighlighted;
                spacing = labelSpacingHighlighted;
                scale = scaleHighlighted;
            }
            else
            {
                plate = plateNormal;
                border = borderNormal;
                glow = glowNormal;
                text = labelNormal;
                spacing = labelSpacingNormal;
                scale = scaleNormal;
            }

            if (plateImage != null)
                plateImage.color = Color.Lerp(plateImage.color, plate, t);
            if (borderImage != null)
                borderImage.color = Color.Lerp(borderImage.color, border, t);
            if (glowImage != null)
                glowImage.color = Color.Lerp(glowImage.color, glow, t);
            if (label != null)
            {
                label.color = Color.Lerp(label.color, text, t);
                label.characterSpacing = Mathf.Lerp(label.characterSpacing, spacing, t);
            }

            if (rect != null)
            {
                var current = rect.localScale.x;
                var next = Mathf.Lerp(current, scale, t);
                rect.localScale = new Vector3(next, next, 1f);
            }
        }

        public void OnPointerEnter(PointerEventData eventData) => pointerInside = true;

        public void OnPointerExit(PointerEventData eventData)
        {
            pointerInside = false;
            pressed = false;
        }

        public void OnPointerDown(PointerEventData eventData) => pressed = true;

        public void OnPointerUp(PointerEventData eventData) => pressed = false;

        public void OnSelect(BaseEventData eventData) => selected = true;

        public void OnDeselect(BaseEventData eventData) => selected = false;

        /// <summary>Editor authoring hook — see <c>LobbyMenuStyleBuilder</c>.</summary>
        public void Bind(Image plate, Image border, Image glow, TMP_Text text)
        {
            plateImage = plate;
            borderImage = border;
            glowImage = glow;
            label = text;
        }
    }
}
