using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Central UI design tokens (colours, font, spacing) for Seoul Playup's runtime UI. A single asset
    /// is the one place to tune the presentation defaults shared across runtime UI views.
    ///
    /// This coexists with the WYSIWYG rule "Scene owns presentation": the theme supplies the *generated
    /// default* colours a view applies to objects it creates (or when a view is explicitly told to
    /// re-apply generated style). Scene-authored objects keep their own inspector values. Assign a
    /// UiThemeAsset to a view's Theme slot to route its generated defaults through the theme; when the
    /// slot is empty each view falls back to its built-in defaults, so the theme is always optional and
    /// non-breaking.
    ///
    /// Default values here mirror the current shipped colours so adopting the theme is a no-op visually.
    /// Palette reference / dial-in target: docs/art-seoul-night-reference.md (Seoul night) and the
    /// "색상 규칙" section of docs/source/00-constitution/art-bible.md.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/UI/Theme", fileName = "UiTheme")]
    public sealed class UiThemeAsset : ScriptableObject
    {
        [Header("Surfaces")]
        [Tooltip("Modal/overlay panel background (e.g. Deck Pile List overlay panel).")]
        [SerializeField] private Color panelColor = new Color32(0x30, 0x34, 0x68, 0xF5);
        [Tooltip("Card/cell background inside a panel.")]
        [SerializeField] private Color cardPanelColor = new Color32(0x30, 0x34, 0x68, 0xFF);
        [Tooltip("Dim scrim drawn behind a modal overlay.")]
        [SerializeField] private Color backdropColor = new Color(0f, 0f, 0f, 0.42f);

        [Header("Text")]
        [SerializeField] private Color textPrimary = new Color(0.92f, 0.96f, 1f, 1f);
        [SerializeField] private Color textMuted = new Color(0.70f, 0.78f, 0.90f, 1f);
        [Tooltip("Selected / active highlight for interactive text.")]
        [SerializeField] private Color textAccent = new Color(0.27f, 0.82f, 1f, 1f);

        [Header("Semantic")]
        [Tooltip("Card cost value when modified at runtime (e.g. spend-all-Ki rules).")]
        [SerializeField] private Color costModified = new Color(0.36f, 0.62f, 1f, 1f);

        // P4 popup skin (procedural SDF panel, UiProceduralPanel): shader parameter bundle rather than a
        // material slot, so a theme edit re-tints every skinned popup without touching the shared material.
        // Defaults mirror UiProceduralPanel's built-in look, so routing a popup through the theme is a
        // visual no-op until the theme is dialled in. The dim scrim behind popups is backdropColor above.
        [Header("Popup Skin")]
        [Tooltip("Rounded-rect fill of a skinned popup panel (Slate Night dial-in target).")]
        [SerializeField] private Color popupFillColor = new Color32(0x1B, 0x24, 0x38, 0xF7);
        [Tooltip("Thin border rimming a skinned popup panel.")]
        [SerializeField] private Color popupBorderColor = new Color32(0xC9, 0xA5, 0x5A, 0xE6);
        [SerializeField] private float popupBorderThickness = 2f;
        [Tooltip("Popup corner radius; larger than the generic cornerRadius used for cells/buttons.")]
        [SerializeField] private float popupCornerRadius = 18f;

        // P4 button skin (UiButtonSkin): defaults mirror the lobby button sprites
        // (ui_btn_plate #150F22 α0.72 + ui_btn_border #C9A227 α0.75) so skinned buttons share the
        // lobby design language.
        [Header("Button Skin")]
        [SerializeField] private Color buttonFillColor = new Color32(0x30, 0x34, 0x68, 0xE6);
        [SerializeField] private Color buttonBorderColor = new Color32(0xC9, 0xA2, 0x27, 0xBF);
        [SerializeField] private float buttonBorderThickness = 2f;
        [SerializeField] private float buttonCornerRadius = 12f;

        [Header("Typography")]
        [Tooltip("Korean UI font (e.g. DNFForgedBlade-Light SDF). Optional; views keep their own font loaders when unset.")]
        [SerializeField] private TMP_FontAsset koreanFont;

        [Header("Scale")]
        [SerializeField] private float cornerRadius = 12f;
        [SerializeField] private float spacingUnit = 8f;
        [SerializeField] private float baseFontSize = 18f;

        public Color PanelColor => panelColor;
        public Color CardPanelColor => cardPanelColor;
        public Color BackdropColor => backdropColor;
        public Color TextPrimary => textPrimary;
        public Color TextMuted => textMuted;
        public Color TextAccent => textAccent;
        public Color CostModified => costModified;
        public Color PopupFillColor => popupFillColor;
        public Color PopupBorderColor => popupBorderColor;
        public float PopupBorderThickness => popupBorderThickness;
        public float PopupCornerRadius => popupCornerRadius;
        public Color ButtonFillColor => buttonFillColor;
        public Color ButtonBorderColor => buttonBorderColor;
        public float ButtonBorderThickness => buttonBorderThickness;
        public float ButtonCornerRadius => buttonCornerRadius;
        public TMP_FontAsset KoreanFont => koreanFont;
        public float CornerRadius => cornerRadius;
        public float SpacingUnit => spacingUnit;
        public float BaseFontSize => baseFontSize;
    }
}
