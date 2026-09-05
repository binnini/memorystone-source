#if UNITY_EDITOR
using System;
using System.Linq;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Editor.UI
{
    public static class PrototypeTestUiLayoutCapture
    {
        private const string SidebarName = "Sidebar";
        private const string SidebarContentName = "Sidebar Content";

        [MenuItem("Seoul Playup/UI/Capture PrototypeTest UI Layout From Scene")]
        public static void CaptureFromOpenScene()
        {
            var contract = UnityEngine.Object.FindFirstObjectByType<GameplaySceneContract>(FindObjectsInactive.Include);
            if (contract == null)
            {
                throw new InvalidOperationException("Open scene does not contain GameplaySceneContract. Open PrototypeTest before capturing UI layout.");
            }

            var changed = 0;
            changed += CaptureSidebarLayout();
            changed += CaptureSidebarCalloutPanels();
            changed += CaptureCardFanLayout(contract);

            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(contract.gameObject.scene);
                EditorSceneManager.SaveScene(contract.gameObject.scene);
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"Captured PrototypeTest UI layout from scene into serialized UI settings. Updated components: {changed}.");
        }

        private static int CaptureSidebarLayout()
        {
            var sidebar = FindRect(SidebarName);
            if (sidebar == null)
            {
                return 0;
            }

            var settings = sidebar.GetComponent<SidebarLayoutSettings>();
            if (settings == null)
            {
                return 0;
            }

            Undo.RecordObject(settings, "Capture Sidebar Layout");
            var so = new SerializedObject(settings);
            SetFloat(so, "sidebarWidth", Mathf.Max(80f, sidebar.sizeDelta.x));

            var content = FindDirectChild(sidebar, SidebarContentName);
            if (content != null)
            {
                SetVector2(so, "contentPadding", new Vector2(Mathf.Max(0f, -content.offsetMax.y), Mathf.Max(0f, content.offsetMin.y)));

                var verticalLayout = content.GetComponent<VerticalLayoutGroup>();
                if (verticalLayout != null)
                {
                    SetFloat(so, "verticalSpacing", Mathf.Max(0f, verticalLayout.spacing));
                }
            }

            var firstButton = sidebar.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name.StartsWith("Sidebar Button ", StringComparison.Ordinal));
            if (firstButton != null)
            {
                SetVector2(so, "buttonSize", firstButton.sizeDelta);
            }

            var firstIcon = sidebar.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == "Icon");
            if (firstIcon != null)
            {
                SetVector2(so, "iconSize", firstIcon.sizeDelta);
            }

            var labels = sidebar.GetComponentsInChildren<TMP_Text>(includeInactive: true);
            var regularLabel = labels.FirstOrDefault(text => text != null && !IsCurrencyLabel(text));
            var currencyLabel = labels.FirstOrDefault(IsCurrencyLabel);
            if (regularLabel != null)
            {
                SetVector2(so, "labelSize", regularLabel.rectTransform.sizeDelta);
                SetFloat(so, "labelFontSize", Mathf.Max(1f, regularLabel.fontSize));
                SetColor(so, "labelColor", regularLabel.color);
            }

            if (currencyLabel != null)
            {
                SetFloat(so, "currencyFontSize", Mathf.Max(1f, currencyLabel.fontSize));
            }

            var image = sidebar.GetComponent<Image>();
            if (image != null)
            {
                SetColor(so, "sidebarColor", image.color);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return 1;
        }

        private static int CaptureSidebarCalloutPanels()
        {
            var sidebar = FindRect(SidebarName);
            if (sidebar == null)
            {
                return 0;
            }

            var settings = sidebar.GetComponent<SidebarCalloutPanelSettings>();
            var controller = sidebar.GetComponent<SidebarCalloutPanelController>();
            if (settings == null || controller == null || controller.PanelLayer == null)
            {
                return 0;
            }

            Undo.RecordObject(settings, "Capture Sidebar Callout Layout");
            settings.EnsureDefaultPanelOverrides();
            var so = new SerializedObject(settings);
            var overrides = so.FindProperty("panelOverrides");
            if (overrides == null || !overrides.isArray)
            {
                return 0;
            }

            for (var i = 0; i < controller.Panels.Count; i++)
            {
                var entry = controller.Panels[i];
                if (entry?.Panel == null || string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                var panelOverride = FindPanelOverride(overrides, entry.Key);
                if (panelOverride == null)
                {
                    continue;
                }

                var panel = entry.Panel;
                SetRelativeBool(panelOverride, "useCustomPanelSize", true);
                SetRelativeVector2(panelOverride, "panelSize", panel.sizeDelta);
                SetRelativeBool(panelOverride, "useCustomHorizontalGap", true);
                SetRelativeFloat(panelOverride, "horizontalGap", Mathf.Max(0f, panel.anchoredPosition.x - sidebar.rect.width));
                SetRelativeVector2(panelOverride, "positionOffset", new Vector2(0f, ResolvePanelOffsetY(settings, controller, entry, panel)));
            }

            var firstPanel = controller.Panels.Select(entry => entry?.Panel).FirstOrDefault(panel => panel != null);
            if (firstPanel != null)
            {
                var image = firstPanel.GetComponent<Image>();
                if (image != null)
                {
                    SetColor(so, "panelColor", image.color);
                }

                var outline = firstPanel.GetComponent<Outline>();
                if (outline != null)
                {
                    SetColor(so, "outlineColor", outline.effectColor);
                    SetFloat(so, "outlineWidth", Mathf.Max(0f, Mathf.Abs(outline.effectDistance.x)));
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            return 1;
        }

        private static int CaptureCardFanLayout(GameplaySceneContract contract)
        {
            var view = contract.GetComponentsInChildren<GameplayCardLaneView>(includeInactive: true).FirstOrDefault();
            if (view == null)
            {
                return 0;
            }

            Undo.RecordObject(view, "Capture Card Fan Layout");
            var so = new SerializedObject(view);

            var firstSlot = view.GetComponentsInChildren<HandCardInteraction>(includeInactive: true)
                .Select(slot => slot.transform as RectTransform)
                .FirstOrDefault(rect => rect != null);
            if (firstSlot != null)
            {
                SetVector2(so, "cardFanSize", firstSlot.sizeDelta);
            }

            var restRoot = view.MoveCardsRoot != null ? view.MoveCardsRoot : view.ActionCardsRoot;
            if (restRoot != null)
            {
                SetFloat(so, "cardFanRestOffsetY", restRoot.anchoredPosition.y);
            }

            if (view.MoveCardsRoot != null || view.ActionCardsRoot != null)
            {
                SetBool(so, "useSeparateCardRootPositions", true);
                if (view.MoveCardsRoot != null)
                {
                    SetVector2(so, "moveCardsRootPosition", view.MoveCardsRoot.anchoredPosition);
                }

                if (view.ActionCardsRoot != null)
                {
                    SetVector2(so, "actionCardsRootPosition", view.ActionCardsRoot.anchoredPosition);
                }
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(view);
            return 1;
        }

        private static float ResolvePanelOffsetY(
            SidebarCalloutPanelSettings settings,
            SidebarCalloutPanelController controller,
            SidebarCalloutPanelController.PanelEntry entry,
            RectTransform panel)
        {
            if (!settings.AlignPanelsToButtons || controller.PanelLayer == null || entry.Button == null)
            {
                return panel.anchoredPosition.y;
            }

            var buttonRect = entry.Button.transform as RectTransform;
            if (buttonRect == null)
            {
                return panel.anchoredPosition.y;
            }

            var parentRect = controller.PanelLayer.rect;
            var buttonCenterWorld = buttonRect.TransformPoint(buttonRect.rect.center);
            var localButtonCenter = controller.PanelLayer.InverseTransformPoint(buttonCenterWorld);
            var panelSize = panel.sizeDelta;
            var topLimit = -settings.ViewportMargin.y;
            var bottomLimit = -Mathf.Max(settings.ViewportMargin.y, parentRect.height - panelSize.y - settings.ViewportMargin.y);
            var baseY = localButtonCenter.y - parentRect.yMax + (panelSize.y * 0.5f);
            baseY = Mathf.Clamp(baseY, bottomLimit, topLimit);
            return panel.anchoredPosition.y - baseY;
        }

        private static RectTransform FindRect(string objectName)
        {
            return UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(rect => rect.name == objectName);
        }

        private static RectTransform FindDirectChild(RectTransform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i) is RectTransform child && child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool IsCurrencyLabel(TMP_Text text)
        {
            return text != null
                && text.transform.parent != null
                && text.transform.parent.name.IndexOf("currency", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TMP_Text FindDockText(RectTransform dock, string objectName)
        {
            return dock == null
                ? null
                : dock.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    .FirstOrDefault(text => text.name == objectName);
        }

        private static SerializedProperty FindPanelOverride(SerializedProperty overrides, string key)
        {
            for (var i = 0; i < overrides.arraySize; i++)
            {
                var element = overrides.GetArrayElementAtIndex(i);
                var keyProperty = element.FindPropertyRelative("key");
                if (keyProperty != null && keyProperty.stringValue == key)
                {
                    return element;
                }
            }

            return null;
        }

        private static void SetFloat(SerializedObject so, string fieldName, float value)
        {
            var property = so.FindProperty(fieldName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetBool(SerializedObject so, string fieldName, bool value)
        {
            var property = so.FindProperty(fieldName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetVector2(SerializedObject so, string fieldName, Vector2 value)
        {
            var property = so.FindProperty(fieldName);
            if (property != null)
            {
                property.vector2Value = value;
            }
        }

        private static void SetColor(SerializedObject so, string fieldName, Color value)
        {
            var property = so.FindProperty(fieldName);
            if (property != null)
            {
                property.colorValue = value;
            }
        }

        private static void SetRelativeBool(SerializedProperty parent, string fieldName, bool value)
        {
            var property = parent.FindPropertyRelative(fieldName);
            if (property != null)
            {
                property.boolValue = value;
            }
        }

        private static void SetRelativeFloat(SerializedProperty parent, string fieldName, float value)
        {
            var property = parent.FindPropertyRelative(fieldName);
            if (property != null)
            {
                property.floatValue = value;
            }
        }

        private static void SetRelativeVector2(SerializedProperty parent, string fieldName, Vector2 value)
        {
            var property = parent.FindPropertyRelative(fieldName);
            if (property != null)
            {
                property.vector2Value = value;
            }
        }
    }
}
#endif
