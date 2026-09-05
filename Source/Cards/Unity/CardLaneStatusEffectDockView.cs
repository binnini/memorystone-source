using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Renders the player's active status effects inside the CardLane <c>StatusEffectDock</c>.
    /// The first <see cref="PrimaryCapacity"/> effects fill the always-visible <c>StatusLayout</c>
    /// grid. Any overflow is summarised by <c>StatusExtraCount</c> ("+N") and revealed in the
    /// hidden <c>StatusExtraPanel</c> when the player hovers anywhere on the dock plate.
    /// The extra panel is bottom-pivoted, so its height grows upward with the overflow row count.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CardLaneStatusEffectDockView : MonoBehaviour
    {
        public const int PrimaryCapacity = 5;

        // The dock's always-visible backdrop, exposed so the tutorial UI glow can point at the whole
        // status HUD box (e.g. the reflect-buff step) without reaching into private fields.
        public RectTransform StatusPanel => statusPanel;

        [SerializeField] private RectTransform primaryLayout;
        [SerializeField] private RectTransform extraLayout;
        [SerializeField] private RectTransform extraPanel;
        // The dock's always-visible backdrop. Purely decorative — nothing else in code referenced it, which is
        // why the P6 T3 frame pass originally skinned only its overflow sibling and missed the panel the
        // player actually looks at.
        [SerializeField] private RectTransform statusPanel;
        [SerializeField] private TMP_Text extraCountText;
        [SerializeField] private StatusEffectIconCatalog iconCatalog;
        [SerializeField] private Color slotColor = new Color(1f, 1f, 1f, 0.16f);
        [SerializeField] private Color iconColor = new Color(0.95f, 0.96f, 1f, 1f);
        [SerializeField] private Color turnTextColor = new Color(0.85f, 0.88f, 0.96f, 1f);
        [SerializeField] private Color tooltipTitleColor = new Color(1f, 0.92f, 0.66f, 1f);
        [SerializeField] private Color tooltipCategoryColor = new Color(0.66f, 0.72f, 0.82f, 1f);
        [SerializeField] private Color tooltipEffectColor = new Color(0.92f, 0.92f, 0.92f, 1f);
        [SerializeField] private Color tooltipTurnsColor = new Color(1f, 0.86f, 0.48f, 1f);
        [SerializeField] private float extraPanelGap = 8f;

        // P6 T3 frame pass: the status chips and the overflow panel were flat rectangles while the rest of the
        // HUD had moved to the procedural plate look. The chip *fill* stays per-kind (StatusEffectIconStyle);
        // only the rounded rim and the plate shading are shared.
        private static readonly Color SlotBorderColor = new Color32(0xC9, 0xA2, 0x27, 0x8C);
        private static readonly Color SlotHighlightColor = new Color(1f, 0.96f, 0.88f, 1f);
        private const float SlotBorderThickness = 1.5f;
        private const float SlotCornerRadius = 10f;
        private static readonly Color ExtraPanelFillColor = new Color32(0x1B, 0x24, 0x38, 0xF2);
        private static readonly Color ExtraPanelBorderColor = new Color32(0xC9, 0xA5, 0x5A, 0xC8);

        private readonly List<SlotBinding> primarySlots = new List<SlotBinding>();
        private readonly List<SlotBinding> extraSlots = new List<SlotBinding>();
        private readonly HashSet<HoverRelay> hoveredRelays = new HashSet<HoverRelay>();
        private TMP_FontAsset uiFont;
        private ObjectInfoTooltipHudPresenter tooltipPresenter;
        private HoverRelay tooltipRelay;
        private int extraCount;
        private bool expanded;

        private void OnDisable()
        {
            HideStatusTooltip(tooltipRelay);
            hoveredRelays.Clear();
            ApplyExpandedState();
        }

        private void OnDestroy()
        {
            HideStatusTooltip(tooltipRelay);
        }

        public void Refresh(IReadOnlyList<ActiveEffect> effects, TMP_FontAsset font)
        {
            uiFont = font;
            EnsureBound();

            var visible = effects ?? Array.Empty<ActiveEffect>();
            var primaryCount = Mathf.Min(visible.Count, PrimaryCapacity);
            extraCount = Mathf.Max(0, visible.Count - PrimaryCapacity);

            EnsureSlotCount(extraSlots, extraLayout, extraCount);

            for (var i = 0; i < primarySlots.Count; i++)
            {
                ApplySlot(primarySlots[i], visible, i);
            }

            for (var i = 0; i < extraSlots.Count; i++)
            {
                ApplySlot(extraSlots[i], visible, PrimaryCapacity + i);
            }

            RefreshActiveStatusTooltip();

            if (extraCountText != null)
            {
                extraCountText.font = uiFont != null ? uiFont : extraCountText.font;
                extraCountText.text = $"+{extraCount}";
                extraCountText.gameObject.SetActive(extraCount > 0);
            }

            if (extraCount == 0)
            {
                hoveredRelays.Clear();
            }

            // Hide the backdrop when nothing is active. It used to sit there as an empty flat rectangle, which
            // was easy to miss; as a rimmed plate an empty box would instead draw the eye to nothing.
            if (statusPanel != null)
            {
                statusPanel.gameObject.SetActive(visible.Count > 0);
            }

            ApplyExpandedState();
        }

        private void ApplySlot(SlotBinding slot, IReadOnlyList<ActiveEffect> effects, int index)
        {
            slot.Ensure(this, uiFont, iconColor, turnTextColor);
            if (index < effects.Count)
            {
                var effect = effects[index];
                slot.SetEffect(effect, StatusEffectIconStyle.BackgroundColor(effect.Kind, slotColor), iconCatalog);
            }
            else
            {
                slot.SetEmpty();
            }
        }

        internal void SetHover(HoverRelay relay, bool isHovered)
        {
            if (relay == null)
            {
                return;
            }

            if (relay.ExpandsOverflow)
            {
                if (isHovered)
                {
                    hoveredRelays.Add(relay);
                }
                else
                {
                    hoveredRelays.Remove(relay);
                }

                ApplyExpandedState();
            }

            if (relay.HasEffect)
            {
                if (isHovered)
                {
                    ShowStatusTooltip(relay);
                }
                else
                {
                    HideStatusTooltip(relay);
                }
            }
        }

        private void ShowStatusTooltip(HoverRelay relay)
        {
            if (relay == null || !relay.HasEffect)
            {
                return;
            }

            tooltipRelay = relay;
            EnsureTooltipPresenter().Show(
                StatusEffectTooltipContent.Title(relay.Effect.Kind),
                tooltipTitleColor,
                BuildTooltipLines(relay.Effect));
        }

        private void HideStatusTooltip(HoverRelay relay)
        {
            if (relay != null && tooltipRelay != relay)
            {
                return;
            }

            tooltipPresenter?.Hide();
            tooltipRelay = null;
        }

        private void RefreshActiveStatusTooltip()
        {
            if (tooltipRelay == null)
            {
                return;
            }

            if (tooltipRelay.HasEffect && tooltipRelay.gameObject.activeInHierarchy)
            {
                ShowStatusTooltip(tooltipRelay);
            }
            else
            {
                HideStatusTooltip(tooltipRelay);
            }
        }

        private ObjectInfoTooltipHudPresenter EnsureTooltipPresenter()
        {
            if (tooltipPresenter != null)
            {
                return tooltipPresenter;
            }

            var host = new GameObject("StatusEffectTooltipHud");
            host.transform.SetParent(transform, false);
            tooltipPresenter = host.AddComponent<ObjectInfoTooltipHudPresenter>();
            // Draw above every gameplay HUD layer (sidebar/menu bar included) so the status description is
            // never occluded by the left menu bar when the player hovers an effect chip near it.
            tooltipPresenter.SetSortingOrder(1200);
            return tooltipPresenter;
        }

        private IReadOnlyList<ObjectInfoTooltipHudPresenter.Line> BuildTooltipLines(ActiveEffect effect)
        {
            var lines = new List<ObjectInfoTooltipHudPresenter.Line>(3);
            var category = StatusEffectTooltipContent.Category(effect.Kind);
            if (!string.IsNullOrWhiteSpace(category))
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(category, tooltipCategoryColor));
            }

            var description = StatusEffectTooltipContent.Description(effect);
            if (!string.IsNullOrWhiteSpace(description))
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(description, tooltipEffectColor));
            }

            lines.Add(new ObjectInfoTooltipHudPresenter.Line(StatusEffectTooltipContent.RemainingTurnsLine(effect), tooltipTurnsColor));
            return lines;
        }

        private void ApplyExpandedState()
        {
            expanded = extraCount > 0 && hoveredRelays.Count > 0;

            // StatusExtraPanel (background) and StatusExtraLayout (overflow grid) are dock siblings.
            // Drive both with the same bottom-pivoted geometry so the reveal grows upward together;
            // re-parenting at runtime is illegal on a prefab instance, so we never restructure here.
            var metrics = ResolveGridMetrics(extraLayout);
            var rows = Mathf.CeilToInt(extraCount / (float)PrimaryCapacity);
            var size = new Vector2(ComputeExpandedWidth(metrics), ComputeExpandedHeight(rows, metrics));

            ApplyContainer(extraPanel, expanded, size);
            ApplyContainer(extraLayout, expanded, size);
        }

        private void ApplyContainer(RectTransform container, bool show, Vector2 size)
        {
            if (container == null)
            {
                return;
            }

            container.gameObject.SetActive(show);
            if (!show)
            {
                return;
            }

            container.anchorMin = new Vector2(0.5f, 1f);
            container.anchorMax = new Vector2(0.5f, 1f);
            container.pivot = new Vector2(0.5f, 0f);
            container.anchoredPosition = new Vector2(0f, extraPanelGap);
            container.sizeDelta = size;
        }

        /// <summary>Panel height for the given overflow-row count. Grows with each extra row.</summary>
        public static float ComputeExpandedHeight(int rows, GridMetrics metrics)
        {
            if (rows <= 0)
            {
                return 0f;
            }

            return rows * metrics.CellSize.y
                + (rows - 1) * metrics.Spacing.y
                + metrics.PaddingVertical;
        }

        public static float ComputeExpandedWidth(GridMetrics metrics)
        {
            return PrimaryCapacity * metrics.CellSize.x
                + (PrimaryCapacity - 1) * metrics.Spacing.x
                + metrics.PaddingHorizontal;
        }

        private void EnsureBound()
        {
            if (primaryLayout == null)
            {
                primaryLayout = FindChildRect("StatusLayout");
            }

            if (extraLayout == null)
            {
                extraLayout = FindChildRect("StatusExtraLayout");
            }

            if (extraPanel == null)
            {
                extraPanel = FindChildRect("StatusExtraPanel");
            }

            if (statusPanel == null)
            {
                statusPanel = FindChildRect("StatusPanel");
            }

            if (extraCountText == null)
            {
                var countRoot = FindChildRect("StatusExtraCount");
                extraCountText = countRoot != null
                    ? countRoot.GetComponent<TMP_Text>() ?? countRoot.GetComponentInChildren<TMP_Text>(includeInactive: true)
                    : null;
            }

            SkinPanels();

            RebuildSlotCache(primaryLayout, primarySlots);
            RebuildSlotCache(extraLayout, extraSlots);

            // 확장 트리거는 <b>도크 판 전체</b>다. "+N" 배지만 트리거였을 때는 두 가지로 막혔다 —
            // (a) 배지가 아이콘 한 칸보다 작아 겨냥이 어렵고, (b) 펼쳐진 패널이 extraPanelGap만큼 떠 있어
            // 포인터가 배지를 벗어나는 순간 닫혀 패널에 도달할 수가 없다(죽은 구간).
            // 판 전체를 트리거로 두면 포인터를 옮기지 않아도 위로 펼쳐진 나머지가 읽힌다.
            EnsureHoverRelay(statusPanel, expandsOverflow: true);
            EnsureHoverRelay(extraCountText != null ? extraCountText.rectTransform : null, expandsOverflow: true);
            EnsureHoverRelay(extraPanel, expandsOverflow: true);
        }

        // Both dock backdrops shipped as flat navy 9-slices ("Background" sprite). Route them through the same
        // plate skin as the popups so the dock reads as one surface with the chips on it.
        // Idempotent: UiProceduralPanel is DisallowMultipleComponent and Configure just re-pushes values.
        private void SkinPanels()
        {
            SkinPanel(statusPanel);
            SkinPanel(extraPanel);
        }

        private static void SkinPanel(RectTransform panel)
        {
            var image = panel != null ? panel.GetComponent<Image>() : null;
            if (image == null)
            {
                return;
            }

            var skin = image.GetComponent<UiProceduralPanel>() ?? image.gameObject.AddComponent<UiProceduralPanel>();
            skin.Configure(ExtraPanelFillColor, ExtraPanelBorderColor, 2f, 14f);
            skin.ConfigureTexture(0.10f, SlotHighlightColor, 0.45f, 2.2f);
        }

        private void RebuildSlotCache(RectTransform layout, List<SlotBinding> cache)
        {
            cache.Clear();
            if (layout == null)
            {
                return;
            }

            for (var i = 0; i < layout.childCount; i++)
            {
                if (layout.GetChild(i) is RectTransform slotRoot)
                {
                    cache.Add(new SlotBinding(slotRoot));
                }
            }
        }

        private void EnsureSlotCount(List<SlotBinding> cache, RectTransform layout, int required)
        {
            if (layout == null || cache.Count == 0 || required <= cache.Count)
            {
                return;
            }

            var template = layout.GetChild(layout.childCount - 1) as RectTransform;
            if (template == null)
            {
                return;
            }

            while (cache.Count < required)
            {
                var clone = Instantiate(template.gameObject, layout, false);
                clone.name = $"{template.name} ({cache.Count})";
                cache.Add(new SlotBinding(clone.GetComponent<RectTransform>()));
            }
        }

        private void EnsureHoverRelay(RectTransform target, bool expandsOverflow)
        {
            if (target == null)
            {
                return;
            }

            var relay = target.GetComponent<HoverRelay>();
            if (relay == null)
            {
                relay = target.gameObject.AddComponent<HoverRelay>();
            }

            relay.Owner = this;
            relay.ExpandsOverflow = expandsOverflow;

            // 포인터 이벤트는 Graphic의 raycastTarget이 켜져 있어야만 온다. 저작에서 꺼 두면 릴레이가
            // 붙어 있어도 조용히 죽으므로(디버깅이 어렵다) 여기서 구조적으로 보장한다.
            var graphic = target.GetComponent<Graphic>();
            if (graphic != null)
            {
                graphic.raycastTarget = true;
            }
        }

        private RectTransform FindChildRect(string targetName)
        {
            return GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == targetName);
        }

        private static GridMetrics ResolveGridMetrics(RectTransform layout)
        {
            var grid = layout != null ? layout.GetComponent<GridLayoutGroup>() : null;
            if (grid == null)
            {
                return new GridMetrics(new Vector2(50f, 50f), new Vector2(10f, 10f), 10f, 10f);
            }

            return new GridMetrics(
                grid.cellSize,
                grid.spacing,
                grid.padding.left + grid.padding.right,
                grid.padding.top + grid.padding.bottom);
        }

        public readonly struct GridMetrics
        {
            public GridMetrics(Vector2 cellSize, Vector2 spacing, float paddingHorizontal, float paddingVertical)
            {
                CellSize = cellSize;
                Spacing = spacing;
                PaddingHorizontal = paddingHorizontal;
                PaddingVertical = paddingVertical;
            }

            public Vector2 CellSize { get; }
            public Vector2 Spacing { get; }
            public float PaddingHorizontal { get; }
            public float PaddingVertical { get; }
        }

        private sealed class SlotBinding
        {
            private const string IconImageName = "StatusIcon";
            private const string GlyphTextName = "StatusIcon_TMP";
            private const string TurnTextName = "StatusTurn_TMP";

            private readonly RectTransform root;
            private Image background;
            private Image iconImage;
            private TMP_Text glyphText;
            private TMP_Text turnText;
            private HoverRelay statusRelay;
            private bool resolved;

            public SlotBinding(RectTransform slotRoot)
            {
                root = slotRoot;
            }

            public void Ensure(CardLaneStatusEffectDockView owner, TMP_FontAsset font, Color glyphColor, Color turnColor)
            {
                if (root == null || resolved)
                {
                    ConfigureRelay(owner);
                    SetContentStyle(font, glyphColor, turnColor);
                    return;
                }

                background = root.GetComponent<Image>() ?? root.gameObject.AddComponent<Image>();
                iconImage = ResolveChildImage(IconImageName);
                glyphText = ResolveOrCreateText(GlyphTextName, TextAlignmentOptions.Center, 26f);
                turnText = ResolveOrCreateText(TurnTextName, TextAlignmentOptions.BottomRight, 16f);
                resolved = true;
                ConfigureRelay(owner);
                SetContentStyle(font, glyphColor, turnColor);
            }

            private void ConfigureRelay(CardLaneStatusEffectDockView owner)
            {
                if (root == null)
                {
                    return;
                }

                var relay = root.GetComponent<HoverRelay>();
                if (relay == null)
                {
                    relay = root.gameObject.AddComponent<HoverRelay>();
                }

                relay.Owner = owner;
                relay.ExpandsOverflow = false;
                statusRelay = relay;
            }

            private void SetContentStyle(TMP_FontAsset font, Color glyphColor, Color turnColor)
            {
                // Content-level styling owned by the dock (font/colour). Layout-level styling
                // (alignment/size/wrapping) is only applied when a fallback child is created, so an
                // authored slot prefab keeps its own look.
                if (glyphText != null)
                {
                    if (font != null)
                    {
                        glyphText.font = font;
                    }

                    glyphText.color = glyphColor;
                }

                if (turnText != null)
                {
                    if (font != null)
                    {
                        turnText.font = font;
                    }

                    turnText.color = turnColor;
                }
            }

            public void SetEmpty()
            {
                statusRelay?.ClearEffect();
                if (root != null)
                {
                    root.gameObject.SetActive(false);
                }
            }

            public void SetEffect(ActiveEffect effect, Color backgroundColor, StatusEffectIconCatalog catalog)
            {
                statusRelay?.SetEffect(effect);
                if (root != null)
                {
                    root.gameObject.SetActive(true);
                }

                if (background != null)
                {
                    // Plain tint first so a missing skin material still leaves the kind colour on the slot
                    // (UiProceduralPanel's non-breaking contract), then push the same colour through the skin
                    // as its SDF fill. On success the skin resets the vertex tint to white and draws the
                    // rounded plate + rim, so the per-kind colour survives the frame pass (P6 T3 follow-up).
                    background.color = backgroundColor;
                    var skin = background.GetComponent<UiProceduralPanel>()
                        ?? background.gameObject.AddComponent<UiProceduralPanel>();
                    skin.Configure(backgroundColor, SlotBorderColor, SlotBorderThickness, SlotCornerRadius);
                    skin.ConfigureTexture(0.16f, SlotHighlightColor, 0.5f, 2f);
                }

                var sprite = catalog != null ? catalog.GetSprite(effect.Kind) : null;
                var hasSprite = sprite != null && iconImage != null;
                if (iconImage != null)
                {
                    iconImage.enabled = hasSprite;
                    if (hasSprite)
                    {
                        iconImage.sprite = sprite;
                    }
                }

                if (glyphText != null)
                {
                    glyphText.enabled = !hasSprite;
                    if (!hasSprite)
                    {
                        glyphText.text = StatusEffectIconStyle.Glyph(effect.Kind);
                    }
                }

                if (turnText != null)
                {
                    // 만료가 없는 효과(힘처럼 런 영구로 투영된 것, 지속 0으로 저작된 보스 기운)는
                    // 남은 턴을 말할 수 없다. 그냥 찍으면 "0"이 배지에 남아 **곧 사라진다는 거짓말**이
                    // 된다(2026-08-05 실기 캡처가 잡았다 — 툴팁만 고치고 이 배지를 놓쳤었다).
                    // 숫자를 지우면 아이콘만 남아 "지속 중"으로 읽힌다.
                    // 숫자 판정은 StatusEffectTooltipContent에 모아 둔다 — 소비형(수호)은 턴이 아니라 횟수다.
                    var badgeNumber = StatusEffectTooltipContent.BadgeNumber(effect);
                    turnText.enabled = badgeNumber.Length > 0;
                    turnText.text = badgeNumber;
                }
            }

            private Image ResolveChildImage(string childName)
            {
                var child = root.Find(childName) as RectTransform;
                if (child == null)
                {
                    child = new GameObject(childName, typeof(RectTransform)).GetComponent<RectTransform>();
                    child.SetParent(root, false);
                    child.anchorMin = Vector2.zero;
                    child.anchorMax = Vector2.one;
                    child.offsetMin = new Vector2(6f, 6f);
                    child.offsetMax = new Vector2(-6f, -6f);
                }

                var image = child.GetComponent<Image>() ?? child.gameObject.AddComponent<Image>();
                image.raycastTarget = false;
                image.preserveAspect = true;
                return image;
            }

            private TMP_Text ResolveOrCreateText(string childName, TextAlignmentOptions alignment, float fontSize)
            {
                var existing = root.Find(childName) as RectTransform;
                if (existing != null)
                {
                    var found = existing.GetComponent<TMP_Text>();
                    if (found != null)
                    {
                        return found;
                    }

                    return AddDefaultText(existing, alignment, fontSize);
                }

                var created = new GameObject(childName, typeof(RectTransform)).GetComponent<RectTransform>();
                created.SetParent(root, false);
                created.anchorMin = Vector2.zero;
                created.anchorMax = Vector2.one;
                created.offsetMin = Vector2.zero;
                created.offsetMax = Vector2.zero;
                return AddDefaultText(created, alignment, fontSize);
            }

            private static TMP_Text AddDefaultText(RectTransform target, TextAlignmentOptions alignment, float fontSize)
            {
                var text = target.gameObject.AddComponent<TextMeshProUGUI>();
                text.alignment = alignment;
                text.fontSize = fontSize;
                text.raycastTarget = false;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                return text;
            }
        }

        internal sealed class HoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public CardLaneStatusEffectDockView Owner { get; set; }
            public bool ExpandsOverflow { get; set; }
            public ActiveEffect Effect { get; private set; }
            public bool HasEffect { get; private set; }

            public void SetEffect(ActiveEffect effect)
            {
                Effect = effect;
                HasEffect = true;
            }

            public void ClearEffect()
            {
                HasEffect = false;
                Effect = default;
            }

            public void OnPointerEnter(PointerEventData eventData)
            {
                Owner?.SetHover(this, true);
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                Owner?.SetHover(this, false);
            }
        }

#if UNITY_INCLUDE_TESTS
        internal void SetIconCatalogForTests(StatusEffectIconCatalog catalog)
        {
            iconCatalog = catalog;
        }

        internal int ExtraCountForTests => extraCount;
        internal bool IsExpandedForTests => expanded;
        internal bool IsExtraCountVisibleForTests => extraCountText != null && extraCountText.gameObject.activeSelf;
        internal string ExtraCountTextForTests => extraCountText != null ? extraCountText.text : string.Empty;

        internal int CountActivePrimarySlotsForTests()
        {
            return primaryLayout == null
                ? 0
                : Enumerable.Range(0, primaryLayout.childCount)
                    .Count(i => primaryLayout.GetChild(i).gameObject.activeSelf);
        }

        internal int CountActiveExtraSlotsForTests()
        {
            return extraLayout == null
                ? 0
                : Enumerable.Range(0, extraLayout.childCount)
                    .Count(i => extraLayout.GetChild(i).gameObject.activeSelf);
        }

        internal void SetHoverForTests(bool hovered)
        {
            EnsureBound();
            hoveredRelays.Clear();
            if (hovered)
            {
                var relay = extraCountText != null ? extraCountText.GetComponent<HoverRelay>() : null;
                if (relay != null)
                {
                    hoveredRelays.Add(relay);
                }
            }

            ApplyExpandedState();
        }

        internal float ExpandedPanelHeightForTests => extraPanel != null ? extraPanel.sizeDelta.y : 0f;
#endif
    }
}
