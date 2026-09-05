using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SidebarLayoutSettings : MonoBehaviour
    {
        [Header("Sidebar")]
        [SerializeField] private bool autoApplyLayoutInEditor;
        [SerializeField, Min(80f)] private float sidebarWidth = 150f;
        [SerializeField] private Color sidebarColor = new Color(0.18823531f, 0.20392159f, 0.40784317f, 0.98f);

        [Header("Button Layout")]
        [SerializeField] private Vector2 buttonSize = new Vector2(120f, 94f);
        [SerializeField, Min(0f)] private float verticalSpacing = 60f;
        [SerializeField] private Vector2 contentPadding = new Vector2(18f, 18f);

        [Header("Icon")]
        [SerializeField] private Vector2 iconSize = new Vector2(80f, 80f);

        [Header("Text")]
        [SerializeField] private Vector2 labelSize = new Vector2(120f, 28f);
        [SerializeField, Min(1f)] private float labelFontSize = 20f;
        [SerializeField, Min(1f)] private float currencyFontSize = 20f;
        [SerializeField] private Color labelColor = new Color(0.94f, 0.95f, 1f, 1f);

        public float SidebarWidth => sidebarWidth;
        public Color SidebarColor => sidebarColor;
        public Vector2 ButtonSize => buttonSize;
        public float VerticalSpacing => verticalSpacing;
        public Vector2 ContentPadding => contentPadding;
        public Vector2 IconSize => iconSize;
        public Vector2 LabelSize => labelSize;
        public float LabelFontSize => labelFontSize;
        public float CurrencyFontSize => currencyFontSize;
        public Color LabelColor => labelColor;

        private void OnValidate()
        {
            if (autoApplyLayoutInEditor)
            {
                ApplyToExistingHierarchy();
            }
        }

        public void ApplyToExistingHierarchy()
        {
            var rect = transform as RectTransform;
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(sidebarWidth, 0f);
            rect.localScale = Vector3.one;

            var image = GetComponent<Image>();
            if (image != null)
            {
                image.color = sidebarColor;
            }

            var content = transform.Find("Sidebar Content") as RectTransform;
            if (content != null)
            {
                content.offsetMin = new Vector2(0f, contentPadding.y);
                content.offsetMax = new Vector2(0f, -contentPadding.x);

                var layout = content.GetComponent<VerticalLayoutGroup>();
                if (layout != null)
                {
                    layout.spacing = verticalSpacing;
                }
            }

            var labels = GetComponentsInChildren<TMP_Text>(includeInactive: true);
            for (var i = 0; i < labels.Length; i++)
            {
                labels[i].fontSize = labels[i].transform.parent != null && labels[i].transform.parent.name.Contains("currency")
                    ? currencyFontSize
                    : labelFontSize;
                labels[i].color = labelColor;
                labels[i].rectTransform.sizeDelta = labelSize;
            }

            var rects = GetComponentsInChildren<RectTransform>(includeInactive: true);
            for (var i = 0; i < rects.Length; i++)
            {
                var child = rects[i];
                if (child == rect)
                {
                    continue;
                }

                if (child.name.StartsWith("Sidebar Button "))
                {
                    child.sizeDelta = buttonSize;
                    var layoutElement = child.GetComponent<LayoutElement>();
                    if (layoutElement != null)
                    {
                        layoutElement.preferredWidth = buttonSize.x;
                        layoutElement.preferredHeight = buttonSize.y;
                    }
                }
                else if (child.name == "Icon")
                {
                    child.sizeDelta = iconSize;
                }
            }

            var calloutController = GetComponent<SidebarCalloutPanelController>();
            if (calloutController != null)
            {
                calloutController.AlignPanelsToButtons();
            }
        }
    }
}

