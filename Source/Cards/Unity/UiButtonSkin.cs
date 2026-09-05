using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// One-call button skin for the P4 unified button look: the procedural SDF rounded plate + thin gold
    /// border (<see cref="UiProceduralPanel"/>) at button scale, mirroring the lobby's bitmap button style
    /// (ui_btn_plate #150F22 α0.72 + ui_btn_border #C9A227 α0.75) so runtime-created and popup buttons
    /// share the lobby design language without bitmaps.
    ///
    /// State feedback rides UGUI's ColorTint transition: the skin shader multiplies the vertex colour into
    /// both fill and border, so the standard Selectable tinting (hover/pressed/disabled) works unchanged.
    /// Same non-breaking contract as the panel skin: if the skin material is missing the button keeps its
    /// authored graphic, and only the tint transition is normalised.
    /// </summary>
    public static class UiButtonSkin
    {
        // 2026-07-25 user feedback: the near-black plate read too dark over dimmed popups — lifted to the
        // shipped indigo panel tone so the plate stays visible on dark backdrops.
        private static readonly Color FillColor = new Color32(0x30, 0x34, 0x68, 0xE6);
        private static readonly Color BorderColor = new Color32(0xC9, 0xA2, 0x27, 0xBF);
        private const float BorderThickness = 2f;
        private const float CornerRadius = 12f;

        // P6 T1 button depth: a slightly stronger vertical lift + warm top rim than the generic panel default,
        // so buttons read as raised, tactile plates (approaching the lobby's 3-layer bitmap look) procedurally.
        private static readonly Color HighlightColor = new Color(1f, 0.96f, 0.88f, 1f);
        private const float TopLift = 0.18f;
        private const float HighlightStrength = 0.9f;
        private const float HighlightWidth = 2.6f;

        public static UiProceduralPanel Apply(Button button, UiThemeAsset theme = null)
        {
            if (button == null)
            {
                return null;
            }

            var image = button.targetGraphic as Image;
            if (image == null)
            {
                image = button.GetComponent<Image>();
            }

            if (image == null)
            {
                return null;
            }

            var skin = ApplyToGraphic(image, theme);

            // Unify the label ink with the skin: buttons being re-skinned may carry authored dark label
            // text (the reward popup's original blue plates used near-black labels), unreadable on the
            // dark plate. Only runtime-applied buttons pass through here; prefab-authored skins keep
            // their authored labels.
            var labelColor = theme != null ? theme.TextPrimary : new Color(0.92f, 0.96f, 1f, 1f);
            foreach (var label in image.GetComponentsInChildren<TMPro.TMP_Text>(true))
            {
                label.color = labelColor;
            }

            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = ColorBlock.defaultColorBlock;
            colors.normalColor = new Color(0.94f, 0.94f, 0.94f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = new Color(0.72f, 0.72f, 0.78f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.60f, 0.55f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            // Hover halo (self-building, non-breaking). DisallowMultipleComponent + the component's own child
            // guard make re-application on runtime-cloned buttons idempotent.
            if (button.GetComponent<UiButtonHoverGlow>() == null)
            {
                button.gameObject.AddComponent<UiButtonHoverGlow>();
            }

            return skin;
        }

        public static UiProceduralPanel ApplyToGraphic(Image image, UiThemeAsset theme = null)
        {
            if (image == null)
            {
                return null;
            }

            var skin = image.GetComponent<UiProceduralPanel>();
            if (skin == null)
            {
                skin = image.gameObject.AddComponent<UiProceduralPanel>();
            }

            if (theme != null)
            {
                skin.Configure(theme.ButtonFillColor, theme.ButtonBorderColor,
                    theme.ButtonBorderThickness, theme.ButtonCornerRadius);
            }
            else
            {
                skin.Configure(FillColor, BorderColor, BorderThickness, CornerRadius);
            }

            // Depth is orthogonal to the fill/border tokens above, so always push the button texture regardless
            // of whether a theme drove the colours. (Theme texture tokens are a deferred follow-up.)
            skin.ConfigureTexture(TopLift, HighlightColor, HighlightStrength, HighlightWidth);

            return skin;
        }
    }
}
