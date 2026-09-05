using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SidebarCalloutPanelController : MonoBehaviour
    {
        [Serializable]
        public sealed class PanelEntry
        {
            [SerializeField] private string key;
            [SerializeField] private Button button;
            [SerializeField] private RectTransform panel;

            public string Key => key;
            public Button Button => button;
            public RectTransform Panel => panel;

            public void Bind(string entryKey, Button entryButton, RectTransform entryPanel)
            {
                key = entryKey;
                button = entryButton;
                panel = entryPanel;
            }
        }

        [SerializeField] private RectTransform sidebarRoot;
        [SerializeField] private RectTransform panelLayer;
        [SerializeField] private bool hidePanelsOnAwake = true;
        [SerializeField] private PanelEntry[] panels = Array.Empty<PanelEntry>();

        private string activeKey;

        public RectTransform PanelLayer => panelLayer;
        public IReadOnlyList<PanelEntry> Panels => panels;

        /// <summary>Key of the callout currently open, or null when every panel is hidden.</summary>
        public string ActiveKey => activeKey;

        private SidebarCalloutPanelSettings Settings => GetComponent<SidebarCalloutPanelSettings>();

        private void Awake()
        {
            if (Application.isPlaying && hidePanelsOnAwake)
            {
                HideAllPanels();
            }
        }

        public void Bind(RectTransform sidebar, RectTransform layer, PanelEntry[] entries)
        {
            sidebarRoot = sidebar;
            panelLayer = layer;
            panels = entries ?? Array.Empty<PanelEntry>();
            activeKey = null;
        }

        public void TogglePanel(string key)
        {
            if (!string.IsNullOrEmpty(activeKey) && activeKey == key)
            {
                HideAllPanels();
                return;
            }

            ShowPanel(key);
        }

        public void ShowPanel(string key)
        {
            activeKey = key;
            for (var i = 0; i < panels.Length; i++)
            {
                var entry = panels[i];
                if (entry?.Panel == null)
                {
                    continue;
                }

                entry.Panel.gameObject.SetActive(entry.Key == key);
            }

            AlignPanelsToButtons();
        }

        public void HideAllPanels()
        {
            activeKey = null;
            for (var i = 0; i < panels.Length; i++)
            {
                if (panels[i]?.Panel != null)
                {
                    panels[i].Panel.gameObject.SetActive(false);
                }
            }
        }

        public void ApplySettingsToPanels()
        {
            var settings = Settings;
            if (settings == null || panels == null)
            {
                return;
            }

            for (var i = 0; i < panels.Length; i++)
            {
                var panel = panels[i]?.Panel;
                if (panel == null)
                {
                    continue;
                }

                var authoredSize = settings.GetPanelSize(panels[i].Key);
                var sizeFitter = panel.GetComponent<ContentSizeFitter>();
                panel.sizeDelta = sizeFitter != null && sizeFitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained
                    ? new Vector2(authoredSize.x, panel.sizeDelta.y)
                    : authoredSize;
                var image = panel.GetComponent<Image>();
                if (image != null)
                {
                    image.color = settings.PanelColor;
                }

                var outline = panel.GetComponent<Outline>();
                if (outline != null)
                {
                    outline.effectColor = settings.OutlineColor;
                    outline.effectDistance = new Vector2(settings.OutlineWidth, -settings.OutlineWidth);
                }
            }
        }

        /// <summary>
        /// 중첩된 <see cref="ContentSizeFitter"/>는 한 번에 수렴하지 않는다. 자식부터 세우고 마지막에
        /// 자신을 세워, 판이 <b>첫 프레임부터</b> 제 크기로 뜨게 한다.
        /// </summary>
        private static void RebuildLayoutInnermostFirst(RectTransform root)
        {
            if (root == null)
            {
                return;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                if (root.GetChild(i) is RectTransform child && child.gameObject.activeSelf)
                {
                    RebuildLayoutInnermostFirst(child);
                }
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }

        public void AlignPanelsToButtons()
        {
            var settings = Settings;
            if (settings == null || !settings.AlignPanelsToButtons || panelLayer == null || sidebarRoot == null || panels == null)
            {
                return;
            }

            var parentRect = panelLayer.rect;
            var topLimit = -settings.ViewportMargin.y;

            for (var i = 0; i < panels.Length; i++)
            {
                var entry = panels[i];
                if (entry?.Panel == null || entry.Button == null)
                {
                    continue;
                }

                var buttonRect = entry.Button.transform as RectTransform;
                if (buttonRect == null)
                {
                    continue;
                }

                var buttonCenterWorld = buttonRect.TransformPoint(buttonRect.rect.center);
                var localButtonCenter = panelLayer.InverseTransformPoint(buttonCenterWorld);

                // 내용에 맞춰 줄어드는 패널(가방·유물)은 설정 표의 높이가 아니라 <b>실제 높이</b>로
                // 세로 위치를 잡아야 한다 — 안 그러면 최대 높이 기준으로 밀려 화면 위에 붙어 잘린다.
                var fitter = entry.Panel.GetComponent<ContentSizeFitter>();
                var heightIsDriven = fitter != null
                    && fitter.enabled
                    && fitter.verticalFit != ContentSizeFitter.FitMode.Unconstrained;
                var authoredSize = settings.GetPanelSize(entry.Key);
                var panelSize = authoredSize;
                if (heightIsDriven)
                {
                    // 🔴 <b>rect.height를 읽지 말 것.</b> 패널이 막 켜진 프레임에는 Fitter가 아직 돌지
                    //    않아 0으로 나오고(실측), 그 0으로 위치를 잡으면 패널이 화면 밖으로 튄다.
                    //    레이아웃 시스템에 <b>계산</b>을 시켜 그 값을 직접 쓴다.
                    //
                    // 🔴🔴 그리고 <b>안쪽부터</b> 세워야 한다. 격자 → 골격 → 판 세 층이 각자 Fitter를
                    //    들고 있어서 판만 한 번 돌리면 아직 안 줄어든 안쪽 높이를 읽는다 —
                    //    그 결과 첫 프레임만 판이 크게 뜨고 다음 프레임에 접히는 것이 눈에 보였다.
                    RebuildLayoutInnermostFirst(entry.Panel);
                    var preferred = LayoutUtility.GetPreferredHeight(entry.Panel);
                    panelSize = new Vector2(
                        authoredSize.x,
                        preferred > 1f ? Mathf.Min(preferred, authoredSize.y) : authoredSize.y);
                    entry.Panel.sizeDelta = panelSize;
                }
                var offset = settings.GetPanelPositionOffset(entry.Key);
                var x = sidebarRoot.rect.width + settings.GetPanelHorizontalGap(entry.Key) + offset.x;
                var bottomLimit = -Mathf.Max(settings.ViewportMargin.y, parentRect.height - panelSize.y - settings.ViewportMargin.y);
                var y = localButtonCenter.y - parentRect.yMax + (panelSize.y * 0.5f) + offset.y;
                y = Mathf.Clamp(y, bottomLimit, topLimit);

                entry.Panel.anchorMin = new Vector2(0f, 1f);
                entry.Panel.anchorMax = new Vector2(0f, 1f);
                entry.Panel.pivot = new Vector2(0f, 1f);
                entry.Panel.anchoredPosition = new Vector2(x, y);
                entry.Panel.sizeDelta = panelSize;
            }
        }
    }
}


