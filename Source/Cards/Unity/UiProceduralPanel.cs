using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Applies the procedural SDF panel skin (shader <c>UI/ProceduralPanel</c>) to a UGUI Image: a
    /// rounded-rect fill with a thin uniform border, drawn per-fragment so it stays crisp at any size and
    /// aspect ratio — the P4 replacement for flat-colour popup rectangles, with no bitmap 9-slice.
    ///
    /// Mirrors the TutorialUiGlow driving pattern: the material loads build-safe from
    /// <c>Resources/UI/Panel</c>, each panel gets its own material instance, and <c>_RectSize</c> is kept in
    /// sync with the RectTransform so the SDF is evaluated in pixel space. The skin is optional and
    /// non-breaking: if the material is missing the component does nothing, leaving the Image's authored
    /// sprite/colour as the fallback look.
    ///
    /// The shader multiplies the vertex colour (Image.color / CanvasGroup alpha) into the skin, so
    /// Selectable ColorTint transitions and group fades keep working on skinned graphics.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(Image))]
    public sealed class UiProceduralPanel : MonoBehaviour
    {
        private const string PanelMaterialResourcePath = "UI/Panel/UI_ProceduralPanel";

        // Default popup look: dark-indigo fill (Slate Night #1B2438, the art-bible dial-in target for panel
        // surfaces) + thin muted-gold border. Serialized so prefab/scene-authored panels can tune per use.
        [SerializeField] private Color fillColor = new Color32(0x1B, 0x24, 0x38, 0xF7);
        [SerializeField] private Color borderColor = new Color32(0xC9, 0xA5, 0x5A, 0xE6);
        [SerializeField] private float borderThickness = 2f;
        [SerializeField] private float cornerRadius = 18f;

        // Width of the shader's antialiased outer edge, in panel pixels. The shader insets the rounded box by
        // this much so the smoothed edge never clips against the quad boundary — right for a floating popup,
        // but it leaves a translucent sliver on a panel that is meant to sit flush against the screen edge
        // (the sidebar's left edge showed the 3D scene through it). Set 0 on flush surfaces for a hard edge;
        // the 1.25 default reproduces the shipped popup look.
        [SerializeField, Min(0f)] private float edgeSoftness = 1.25f;

        // P6 T1 texture pass: a gentle vertical fill gradient + a thin lit top rim, giving the flat P4 plate a
        // hint of rounded depth. Defaults are deliberately subtle so every skinned popup/panel gains a little
        // dimension without being noisy; UiButtonSkin pushes a stronger set for buttons, and the full-bleed deck
        // overlay flattens these back to zero (no top-rim seam on a radius-0 panel). All four map straight to the
        // shader's additive params, which are zero-effect by default, so leaving them at 0 reproduces the P4 look.
        [Header("Texture (P6 T1)")]
        [SerializeField, Range(0f, 1f)] private float topLift = 0.10f;
        [SerializeField] private Color highlightColor = new Color(1f, 0.96f, 0.88f, 1f);
        [SerializeField, Range(0f, 2f)] private float highlightStrength = 0.5f;
        [SerializeField] private float highlightWidth = 2.2f;

        private Image image;
        private Material materialInstance;
        private static bool warnedMissingMaterial;

        /// <summary>Adds and configures a panel skin on a code-generated popup graphic.</summary>
        public static UiProceduralPanel Attach(
            Image target, Color fill, Color border, float borderThicknessPx, float cornerRadiusPx)
        {
            var panel = target.gameObject.AddComponent<UiProceduralPanel>();
            panel.Configure(fill, border, borderThicknessPx, cornerRadiusPx);
            return panel;
        }

        /// <summary>
        /// Routes the skin through the theme's popup tokens. A null theme keeps the component's serialized
        /// defaults (same optional/non-breaking contract as the other theme slots).
        /// </summary>
        public void Configure(UiThemeAsset theme)
        {
            if (theme == null)
            {
                return;
            }

            Configure(theme.PopupFillColor, theme.PopupBorderColor,
                theme.PopupBorderThickness, theme.PopupCornerRadius);
        }

        public void Configure(Color fill, Color border, float borderThicknessPx, float cornerRadiusPx)
        {
            fillColor = fill;
            borderColor = border;
            borderThickness = borderThicknessPx;
            cornerRadius = cornerRadiusPx;
            if (isActiveAndEnabled)
            {
                Apply();
            }
        }

        /// <summary>
        /// Overrides the P6 texture params (vertical lift + top rim). Orthogonal to the fill/border Configure
        /// overloads, so a caller can dial the depth without re-specifying colours. Passing zero lift/strength
        /// flattens the skin back to the P4 look (used by the full-bleed deck overlay to avoid a top-rim seam).
        /// </summary>
        public void ConfigureTexture(float lift, Color hlColor, float hlStrength, float hlWidth)
        {
            topLift = lift;
            highlightColor = hlColor;
            highlightStrength = hlStrength;
            highlightWidth = hlWidth;
            if (isActiveAndEnabled)
            {
                Apply();
            }
        }

        private void OnEnable()
        {
            Apply();
        }

        private void OnRectTransformDimensionsChange()
        {
            SyncRectSize();
        }

        private void OnDestroy()
        {
            if (materialInstance != null)
            {
                Destroy(materialInstance);
                materialInstance = null;
            }
        }

        private void Apply()
        {
            image = GetComponent<Image>();
            if (materialInstance == null)
            {
                var baseMaterial = Resources.Load<Material>(PanelMaterialResourcePath);
                if (baseMaterial == null)
                {
                    if (!warnedMissingMaterial)
                    {
                        warnedMissingMaterial = true;
                        Debug.LogWarning(
                            $"UiProceduralPanel material missing at Resources/{PanelMaterialResourcePath}; " +
                            "leaving the Image's authored look as fallback.", this);
                    }
                    return;
                }

                materialInstance = new Material(baseMaterial);
            }

            materialInstance.SetColor("_FillColor", fillColor);
            materialInstance.SetColor("_BorderColor", borderColor);
            materialInstance.SetFloat("_BorderThickness", borderThickness);
            materialInstance.SetFloat("_Radius", cornerRadius);
            materialInstance.SetFloat("_Softness", edgeSoftness);
            materialInstance.SetFloat("_TopLift", topLift);
            materialInstance.SetColor("_HighlightColor", highlightColor);
            materialInstance.SetFloat("_HighlightStrength", highlightStrength);
            materialInstance.SetFloat("_HighlightWidth", highlightWidth);

            // Procedural skin: no sprite (a null-sprite Image draws a full-rect quad with uv 0..1, which the
            // shader needs). Neutral vertex tint — fill/border colours come from the material.
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.material = materialInstance;
            image.color = Color.white;
            SyncRectSize();
        }

        /// <summary>
        /// 레이아웃이 확정된 뒤 <c>_RectSize</c>를 다시 맞춘다.
        ///
        /// <para>
        /// 🔴 <b>에디트 모드에는 <see cref="OnRectTransformDimensionsChange"/>가 오지 않는다</b>
        /// (이 컴포넌트에 <c>[ExecuteAlways]</c>가 없다). 그래서 런타임 생성 패널을 판정 캡처로 찍으면
        /// <b>크기가 0이던 때의 SDF가 그대로 남아 판이 통째로 안 그려지고</b>, 크기가 어긋난 채 남은
        /// 패널은 모서리가 과하게 둥근 알약으로 보인다(2026-08-31 실측 — 전리품 목록 패널이 사라졌고
        /// 줄들이 알약이 됐다). 플레이 모드에서는 콜백이 오므로 이 재호출은 무해하다.
        /// </para>
        ///
        /// <para>런타임에 패널을 짓고 <c>LayoutRebuilder</c>로 크기를 확정하는 뷰는 그 <b>뒤에</b> 이걸 부른다.</para>
        /// </summary>
        public void ResyncRectSize()
        {
            SyncRectSize();
        }

        private void SyncRectSize()
        {
            if (materialInstance == null)
            {
                return;
            }

            var size = ((RectTransform)transform).rect.size;
            materialInstance.SetVector("_RectSize", new Vector4(size.x, size.y, 0f, 0f));
        }
    }
}
