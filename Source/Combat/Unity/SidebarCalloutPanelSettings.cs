using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SidebarCalloutPanelSettings : MonoBehaviour
    {
        private static readonly PanelDefinition[] DefaultPanelDefinitions =
        {
            new PanelDefinition("currency", "재화", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f)),
            new PanelDefinition("bag", "가방", new Vector2(324f, 224f), new Vector2(14f, 10f), new Vector2(68f, 68f)),
            new PanelDefinition("deck", "덱", new Vector2(330f, 260f), new Vector2(16f, 14f), new Vector2(286f, 30f)),
            // Q6(DEC-2026-08-31-01): 저주는 카드 계통에 완전히 넘겼으므로 패널은 「유물」이다.
            // 키 "relic_curse"와 에셋 파일명은 유지한다 — 바꾸면 .meta GUID 재배선이 따라온다.
            new PanelDefinition("relic_curse", "유물", new Vector2(350f, 300f), new Vector2(14f, 10f), new Vector2(58f, 58f)),
            new PanelDefinition("settings", "설정", new Vector2(420f, 300f), new Vector2(18f, 14f), new Vector2(376f, 30f))
        };

        [Serializable]
        public sealed class PanelOverride
        {
            [SerializeField] private string key;
            [SerializeField] private string inspectorLabel;

            [Header("Panel Frame")]
            [SerializeField] private bool useCustomPanelSize;
            [SerializeField] private Vector2 panelSize = new Vector2(330f, 185f);
            [SerializeField] private bool useCustomHorizontalGap;
            [SerializeField, Min(0f)] private float horizontalGap = 28f;
            [SerializeField] private Vector2 positionOffset;

            [Header("Panel Content")]
            [SerializeField] private bool useCustomContentLayout;
            [SerializeField] private Vector2 contentPadding = new Vector2(18f, 14f);
            [SerializeField, Min(1f)] private float bodyFontSize = 16f;
            [SerializeField, Min(1f)] private float smallFontSize = 13f;
            [SerializeField] private Vector2 iconCellSize = new Vector2(58f, 42f);
            [SerializeField] private Vector2 rowSize = new Vector2(286f, 24f);
            [SerializeField, Min(0f)] private float contentSpacing = 8f;

            public string Key => key;
            public string InspectorLabel => inspectorLabel;
            public Vector2 PositionOffset => positionOffset;

            public PanelOverride(string panelKey, string label, SidebarCalloutPanelSettings defaults)
            {
                key = panelKey;
                inspectorLabel = label;
                ResetDefaults(defaults, panelKey);
            }

            public void EnsureDefaults(string panelKey, string label, SidebarCalloutPanelSettings defaults)
            {
                if (string.IsNullOrEmpty(key))
                {
                    key = panelKey;
                }

                inspectorLabel = label;
                if (panelSize == Vector2.zero)
                {
                    panelSize = defaults.GetDefaultPanelSize(panelKey);
                }

                if (horizontalGap <= 0f)
                {
                    horizontalGap = defaults.HorizontalGap;
                }

                if (contentPadding == Vector2.zero)
                {
                    contentPadding = defaults.GetDefaultContentPadding(panelKey);
                }

                if (bodyFontSize <= 0f)
                {
                    bodyFontSize = defaults.BodyFontSize;
                }

                if (smallFontSize <= 0f)
                {
                    smallFontSize = defaults.SmallFontSize;
                }

                if (iconCellSize == Vector2.zero)
                {
                    iconCellSize = defaults.GetDefaultIconCellSize(panelKey);
                }

                if (rowSize == Vector2.zero)
                {
                    rowSize = defaults.GetDefaultRowSize(panelKey);
                }

                if (contentSpacing < 0f)
                {
                    contentSpacing = defaults.ContentSpacing;
                }
            }

            public Vector2 ResolvePanelSize(Vector2 defaultValue) => useCustomPanelSize ? panelSize : defaultValue;

            public float ResolveHorizontalGap(float defaultValue) => useCustomHorizontalGap ? horizontalGap : defaultValue;

            public Vector2 ResolveContentPadding(Vector2 defaultValue) => useCustomContentLayout ? contentPadding : defaultValue;

            public float ResolveBodyFontSize(float defaultValue) => useCustomContentLayout ? bodyFontSize : defaultValue;

            public float ResolveSmallFontSize(float defaultValue) => useCustomContentLayout ? smallFontSize : defaultValue;

            public Vector2 ResolveIconCellSize(Vector2 defaultValue) => useCustomContentLayout ? iconCellSize : defaultValue;

            public Vector2 ResolveRowSize(Vector2 defaultValue) => useCustomContentLayout ? rowSize : defaultValue;

            public float ResolveContentSpacing(float defaultValue) => useCustomContentLayout ? contentSpacing : defaultValue;

            private void ResetDefaults(SidebarCalloutPanelSettings defaults, string panelKey)
            {
                panelSize = defaults.GetDefaultPanelSize(panelKey);
                horizontalGap = defaults.HorizontalGap;
                contentPadding = defaults.GetDefaultContentPadding(panelKey);
                bodyFontSize = defaults.BodyFontSize;
                smallFontSize = defaults.SmallFontSize;
                iconCellSize = defaults.GetDefaultIconCellSize(panelKey);
                rowSize = defaults.GetDefaultRowSize(panelKey);
                contentSpacing = defaults.ContentSpacing;
            }
        }

        private readonly struct PanelDefinition
        {
            public readonly string Key;
            public readonly string Label;
            public readonly Vector2 PanelSize;
            public readonly Vector2 ContentPadding;
            public readonly Vector2 RowSize;

            public PanelDefinition(string key, string label, Vector2 panelSize, Vector2 contentPadding, Vector2 rowSize)
            {
                Key = key;
                Label = label;
                PanelSize = panelSize;
                ContentPadding = contentPadding;
                RowSize = rowSize;
            }
        }

        [Header("Panel Layout Defaults")]
        [SerializeField] private bool autoApplyLayoutInEditor;
        [SerializeField] private Vector2 panelSize = new Vector2(330f, 185f);
        [SerializeField, Min(0f)] private float horizontalGap = 28f;
        [SerializeField] private Vector2 viewportMargin = new Vector2(18f, 18f);
        [SerializeField] private bool alignPanelsToButtons = true;

        // P6 T2: the callouts shipped as white cards with dark ink, the last light surface left in an
        // otherwise dark-indigo UI. These tokens are the single source the panel generator
        // (PrototypeTestSidebarApplier) and ApplySettingsToPanels draw every callout colour from, so
        // flipping them here keeps a future regeneration on the dark skin. The panel *surface* itself is
        // drawn by UiProceduralPanel (SDF fill + gold border); panelColor stays as the authored-Image
        // fallback for edit mode, where the skin does not run, and outlineColor is now transparent because
        // an Outline mesh effect would duplicate the procedural quad.
        [Header("Panel Style")]
        [SerializeField] private Color panelColor = new Color32(0x1B, 0x24, 0x38, 0xF7);
        [SerializeField] private Color outlineColor = new Color(0.28f, 0.30f, 0.56f, 0f);
        [SerializeField, Min(0f)] private float outlineWidth = 0f;
        [SerializeField] private Color textColor = new Color(0.92f, 0.96f, 1f, 1f);
        [SerializeField] private Color mutedTextColor = new Color(0.70f, 0.78f, 0.90f, 1f);
        [SerializeField] private Color accentColor = new Color(0.86f, 0.75f, 0.48f, 1f);

        [Header("Panel Content Defaults")]
        [SerializeField] private Vector2 contentPadding = new Vector2(18f, 14f);
        [SerializeField, Min(1f)] private float bodyFontSize = 16f;
        [SerializeField, Min(1f)] private float smallFontSize = 13f;
        [SerializeField] private Vector2 iconCellSize = new Vector2(58f, 42f);
        [SerializeField] private Vector2 rowSize = new Vector2(286f, 24f);
        [SerializeField, Min(0f)] private float contentSpacing = 8f;

        [Header("Per Panel Inspector Overrides")]
        [SerializeField] private PanelOverride[] panelOverrides = Array.Empty<PanelOverride>();

        public Vector2 PanelSize => panelSize;
        public float HorizontalGap => horizontalGap;
        public Vector2 ViewportMargin => viewportMargin;
        public bool AlignPanelsToButtons => alignPanelsToButtons;
        public Color PanelColor => panelColor;
        public Color OutlineColor => outlineColor;
        public float OutlineWidth => outlineWidth;
        public Color TextColor => textColor;
        public Color MutedTextColor => mutedTextColor;
        public Color AccentColor => accentColor;
        public Vector2 ContentPadding => contentPadding;
        public float BodyFontSize => bodyFontSize;
        public float SmallFontSize => smallFontSize;
        public Vector2 IconCellSize => iconCellSize;
        public Vector2 RowSize => rowSize;
        public float ContentSpacing => contentSpacing;
        public IReadOnlyList<PanelOverride> PanelOverrides => panelOverrides;

        private void OnValidate()
        {
            EnsureDefaultPanelOverrides();
            if (!autoApplyLayoutInEditor)
            {
                return;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        ApplyToExistingHierarchy();
                    }
                };
                return;
            }
#endif
            ApplyToExistingHierarchy();
        }

        public void ApplyToExistingHierarchy()
        {
            EnsureDefaultPanelOverrides();
            var controller = GetComponent<SidebarCalloutPanelController>();
            if (controller != null)
            {
                controller.ApplySettingsToPanels();
                controller.AlignPanelsToButtons();
            }
        }

        public void EnsureDefaultPanelOverrides()
        {
            var existing = panelOverrides?
                .Where(entry => entry != null && !string.IsNullOrEmpty(entry.Key))
                .GroupBy(entry => entry.Key)
                .ToDictionary(group => group.Key, group => group.First())
                ?? new Dictionary<string, PanelOverride>();

            var list = new List<PanelOverride>(DefaultPanelDefinitions.Length);
            foreach (var definition in DefaultPanelDefinitions)
            {
                if (!existing.TryGetValue(definition.Key, out var panelOverride) || panelOverride == null)
                {
                    panelOverride = new PanelOverride(definition.Key, definition.Label, this);
                }

                panelOverride.EnsureDefaults(definition.Key, definition.Label, this);
                list.Add(panelOverride);
            }

            panelOverrides = list.ToArray();
        }

        public Vector2 GetPanelSize(string key) => FindPanelOverride(key)?.ResolvePanelSize(GetDefaultPanelSize(key)) ?? GetDefaultPanelSize(key);

        public float GetPanelHorizontalGap(string key) => FindPanelOverride(key)?.ResolveHorizontalGap(HorizontalGap) ?? HorizontalGap;

        public Vector2 GetPanelPositionOffset(string key) => FindPanelOverride(key)?.PositionOffset ?? Vector2.zero;

        public Vector2 GetContentPadding(string key) => FindPanelOverride(key)?.ResolveContentPadding(GetDefaultContentPadding(key)) ?? GetDefaultContentPadding(key);

        public float GetBodyFontSize(string key) => FindPanelOverride(key)?.ResolveBodyFontSize(BodyFontSize) ?? BodyFontSize;

        public float GetSmallFontSize(string key) => FindPanelOverride(key)?.ResolveSmallFontSize(SmallFontSize) ?? SmallFontSize;

        public Vector2 GetIconCellSize(string key) => FindPanelOverride(key)?.ResolveIconCellSize(GetDefaultIconCellSize(key)) ?? GetDefaultIconCellSize(key);

        public Vector2 GetRowSize(string key) => FindPanelOverride(key)?.ResolveRowSize(GetDefaultRowSize(key)) ?? GetDefaultRowSize(key);

        public float GetContentSpacing(string key) => FindPanelOverride(key)?.ResolveContentSpacing(ContentSpacing) ?? ContentSpacing;

        private Vector2 GetDefaultPanelSize(string key) => FindDefinition(key)?.PanelSize ?? PanelSize;

        private Vector2 GetDefaultContentPadding(string key) => FindDefinition(key)?.ContentPadding ?? ContentPadding;

        private Vector2 GetDefaultRowSize(string key) => FindDefinition(key)?.RowSize ?? RowSize;

        // 가방·유물은 칸 자체가 그림 한 장이라 셀 크기가 곧 아이콘 크기다 — 정의 표의 값을 그대로 쓴다
        // (종전에는 58x58이 여기 박혀 있어서 정의 표를 고쳐도 칸이 안 커졌다).
        private Vector2 GetDefaultIconCellSize(string key) =>
            key == "bag" || key == "relic_curse" ? GetDefaultRowSize(key) : IconCellSize;

        private static PanelDefinition? FindDefinition(string key)
        {
            for (var i = 0; i < DefaultPanelDefinitions.Length; i++)
            {
                if (DefaultPanelDefinitions[i].Key == key)
                {
                    return DefaultPanelDefinitions[i];
                }
            }

            return null;
        }

        private PanelOverride FindPanelOverride(string key)
        {
            if (string.IsNullOrEmpty(key) || panelOverrides == null)
            {
                return null;
            }

            for (var i = 0; i < panelOverrides.Length; i++)
            {
                if (panelOverrides[i] != null && panelOverrides[i].Key == key)
                {
                    return panelOverrides[i];
                }
            }

            return null;
        }
    }
}


