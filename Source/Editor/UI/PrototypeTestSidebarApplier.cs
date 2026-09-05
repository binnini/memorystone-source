#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Editor.UI
{
    public static class PrototypeTestSidebarApplier
    {
        private const string PrototypeScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";
        private const string SidebarPrefabPath = "Assets/Prefabs/UI/Prototype/SidebarSystem.prefab";
        private const string SidebarName = "Sidebar";
        private const string CalloutPanelLayerName = "Sidebar Callout Panel Layer";
        private const string SidebarSpriteRoot = "Assets/Art/UI/Sidebar";
        private static readonly Color ClearButtonColor = new Color(1f, 1f, 1f, 0f);

        private readonly struct SidebarItem
        {
            public readonly string Key;
            public readonly string Label;
            public readonly bool BottomPinned;

            public SidebarItem(string key, string label, bool bottomPinned = false)
            {
                Key = key;
                Label = label;
                BottomPinned = bottomPinned;
            }
        }

        private static readonly SidebarItem[] Items =
        {
            new SidebarItem("currency", "1234"),
            new SidebarItem("bag", "가방"),
            new SidebarItem("deck", "덱"),
            new SidebarItem("relic_curse", "유물/저주"),
            new SidebarItem("settings", "설정", bottomPinned: true)
        };

        [MenuItem("Seoul Playup/UI/Apply PrototypeTest Sidebar")]
        public static void ApplyToPrototypeTest()
        {
            var scene = EditorSceneManager.OpenScene(PrototypeScenePath, OpenSceneMode.Single);
            ApplyToOpenScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Applied {SidebarName} layout to {PrototypeScenePath}.");
        }

        /// <summary>
        /// 출하 사이드바(<see cref="SidebarPrefabPath"/>)의 가방·유물 콜아웃 두 장을 새 저작으로 다시 짓는다.
        /// MainGameplay는 이 프리팹의 인스턴스라 여기만 고치면 실게임에 그대로 간다 —
        /// 씬을 건드리지 않는 것이 요점이다.
        /// </summary>
        [MenuItem("Seoul Playup/UI/Apply SidebarSystem Prefab Item Panels")]
        public static void ApplySidebarSystemPrefabItemPanels()
        {
            var root = PrefabUtility.LoadPrefabContents(SidebarPrefabPath);
            try
            {
                var settings = root.GetComponentInChildren<SidebarCalloutPanelSettings>(includeInactive: true);
                if (settings == null)
                {
                    throw new InvalidOperationException("SidebarSystem prefab does not contain SidebarCalloutPanelSettings.");
                }

                // 프리팹에 저장된 per-panel 오버라이드가 정의 표를 이긴다 — 크기를 정의 표만 고치면
                // 프리팹은 옛 값(350x118)을 그대로 쓴다. 그래서 오버라이드도 함께 되돌린다.
                ResetPanelOverrideLayout(settings, "bag");
                ResetPanelOverrideLayout(settings, "relic_curse");

                RebuildAuthoredPanel<SidebarBagPanelView>(root, settings, "bag");
                RebuildAuthoredPanel<SidebarRelicCursePanelView>(root, settings, "relic_curse");

                PrefabUtility.SaveAsPrefabAsset(root, SidebarPrefabPath);
                Debug.Log($"Applied renewed bag/relic callout panels to {SidebarPrefabPath}.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void RebuildAuthoredPanel<TView>(GameObject root, SidebarCalloutPanelSettings settings, string key)
            where TView : Component
        {
            var panel = root.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == $"Sidebar Callout Panel {key}");
            if (panel == null)
            {
                throw new InvalidOperationException($"SidebarSystem prefab does not contain Sidebar Callout Panel {key}.");
            }

            for (var i = panel.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(panel.GetChild(i).gameObject);
            }

            panel.sizeDelta = settings.GetPanelSize(key);

            // 🔴 판에 세로 레이아웃이 없으면 preferred height가 0으로 나오고 ContentSizeFitter가 판을
            //    통째로 접는다 — 가방 판이 실제로 그 상태였다(옛 저작 경로가 그룹을 안 달았다).
            var layout = panel.GetComponent<VerticalLayoutGroup>() ?? panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = settings.GetContentSpacing(key);
            var contentPadding = settings.GetContentPadding(key);
            layout.padding = new RectOffset(
                Mathf.RoundToInt(contentPadding.x),
                Mathf.RoundToInt(contentPadding.x),
                Mathf.RoundToInt(contentPadding.y),
                Mathf.RoundToInt(contentPadding.y));

            PopulateCalloutPanel(panel, key, settings);

            // 판 자체도 내용에 맞춰 줄어든다. 설정 표의 크기는 이제 「최대 높이」 구실만 한다 —
            // 폭은 그대로 두고(Unconstrained) 높이만 따라간다.
            var panelFitter = panel.GetComponent<ContentSizeFitter>() ?? panel.gameObject.AddComponent<ContentSizeFitter>();
            panelFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // 뷰 컴포넌트는 새로 단다 — 직렬화된 옛 색(금색 칩 등)이 남아 있으면 코드 기본값을 이긴다.
            var stale = panel.GetComponent<TView>();
            if (stale != null)
            {
                UnityEngine.Object.DestroyImmediate(stale);
            }

            var view = panel.gameObject.AddComponent<TView>();
            // 뷰는 이름으로 저작 타깃을 다시 찾는다 — 방금 지은 골격에 맞춰 배선을 새로 잡는다.
            var bind = typeof(TView).GetMethod("AutoBindFromHierarchy");
            bind?.Invoke(view, null);

            EditorUtility.SetDirty(panel.gameObject);
        }

        /// <summary>
        /// 한 패널의 Inspector 오버라이드를 정의 표 기본값으로 되돌린다(크기·내용 레이아웃 모두).
        /// 오버라이드가 켜져 있으면 정의 표를 고쳐도 화면이 안 바뀐다 — 리뉴얼이 안 먹는 함정.
        /// </summary>
        private static void ResetPanelOverrideLayout(SidebarCalloutPanelSettings settings, string key)
        {
            settings.EnsureDefaultPanelOverrides();
            var serializedSettings = new SerializedObject(settings);
            var overrides = serializedSettings.FindProperty("panelOverrides");
            for (var i = 0; overrides != null && i < overrides.arraySize; i++)
            {
                var entry = overrides.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("key")?.stringValue != key)
                {
                    continue;
                }

                entry.FindPropertyRelative("useCustomPanelSize").boolValue = false;
                entry.FindPropertyRelative("useCustomHorizontalGap").boolValue = false;
                entry.FindPropertyRelative("useCustomContentLayout").boolValue = false;
                entry.FindPropertyRelative("positionOffset").vector2Value = Vector2.zero;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                return;
            }
        }

        public static void ApplyToOpenScene()
        {
            var contract = UnityEngine.Object.FindFirstObjectByType<GameplaySceneContract>(FindObjectsInactive.Include);
            if (contract == null)
            {
                throw new InvalidOperationException("PrototypeTest scene does not contain GameplaySceneContract.");
            }

            EnsureCanvasScaler(contract.Canvas);

            var gameplayLayers = FindRect(GameplaySceneContract.GameplayLayerRootName);
            if (gameplayLayers == null)
            {
                throw new InvalidOperationException("Gameplay UI Layers root was not found.");
            }

            var sidebar = FindRect(SidebarName);
            if (sidebar == null)
            {
                sidebar = new GameObject(SidebarName, typeof(RectTransform)).GetComponent<RectTransform>();
            }

            sidebar.SetParent(gameplayLayers, false);
            sidebar.SetAsFirstSibling();
            var settings = sidebar.GetComponent<SidebarLayoutSettings>();
            if (settings == null)
            {
                settings = sidebar.gameObject.AddComponent<SidebarLayoutSettings>();
            }

            ConfigureSidebarRoot(sidebar, settings);
            var buttons = RebuildSidebarChildren(sidebar, settings);
            settings.ApplyToExistingHierarchy();

            var calloutSettings = sidebar.GetComponent<SidebarCalloutPanelSettings>();
            if (calloutSettings == null)
            {
                calloutSettings = sidebar.gameObject.AddComponent<SidebarCalloutPanelSettings>();
            }

            calloutSettings.EnsureDefaultPanelOverrides();

            var controller = sidebar.GetComponent<SidebarCalloutPanelController>();
            if (controller == null)
            {
                controller = sidebar.gameObject.AddComponent<SidebarCalloutPanelController>();
            }

            var runtimeView = sidebar.GetComponent<SidebarRuntimeView>();
            if (runtimeView == null)
            {
                runtimeView = sidebar.gameObject.AddComponent<SidebarRuntimeView>();
            }

            var panelLayer = EnsureCalloutPanelLayer(gameplayLayers);
            var entries = RebuildCalloutPanels(panelLayer, calloutSettings, buttons, controller);
            controller.Bind(sidebar, panelLayer, entries);
            runtimeView.Bind(controller, calloutSettings);
            calloutSettings.ApplyToExistingHierarchy();
            controller.HideAllPanels();

            EditorUtility.SetDirty(sidebar.gameObject);
            EditorUtility.SetDirty(panelLayer.gameObject);
        }

        private static void EnsureCanvasScaler(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            var scaler = canvas.GetComponent<CanvasScaler>();
            if (scaler == null)
            {
                scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = GameplaySceneContract.GameplayUiReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = GameplaySceneContract.GameplayUiCanvasMatch;
            EditorUtility.SetDirty(scaler);
        }

        private static void ConfigureSidebarRoot(RectTransform sidebar, SidebarLayoutSettings settings)
        {
            sidebar.anchorMin = new Vector2(0f, 0f);
            sidebar.anchorMax = new Vector2(0f, 1f);
            sidebar.pivot = new Vector2(0f, 0.5f);
            sidebar.anchoredPosition = Vector2.zero;
            sidebar.sizeDelta = new Vector2(settings.SidebarWidth, 0f);
            sidebar.localScale = Vector3.one;

            var image = sidebar.GetComponent<Image>();
            if (image == null)
            {
                image = sidebar.gameObject.AddComponent<Image>();
            }

            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = settings.SidebarColor;
            image.raycastTarget = true;

            var canvasGroup = sidebar.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
            {
                canvasGroup = sidebar.gameObject.AddComponent<CanvasGroup>();
            }

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        private static Dictionary<string, Button> RebuildSidebarChildren(RectTransform sidebar, SidebarLayoutSettings settings)
        {
            for (var i = sidebar.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(sidebar.GetChild(i).gameObject);
            }

            var buttons = new Dictionary<string, Button>();

            var content = CreateRect("Sidebar Content", sidebar);
            Stretch(content, new Vector2(0f, settings.ContentPadding.y), new Vector2(0f, -settings.ContentPadding.x));
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = settings.VerticalSpacing;
            layout.padding = new RectOffset(0, 0, 6, 0);

            foreach (var item in Items.Where(item => !item.BottomPinned))
            {
                buttons[item.Key] = CreateSidebarButton(content, item, settings);
            }

            var spacer = CreateRect("Sidebar Flexible Spacer", content);
            spacer.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;

            foreach (var item in Items.Where(item => item.BottomPinned))
            {
                buttons[item.Key] = CreateSidebarButton(content, item, settings);
            }

            return buttons;
        }

        private static Button CreateSidebarButton(RectTransform parent, SidebarItem item, SidebarLayoutSettings settings)
        {
            var buttonRect = CreateRect($"Sidebar Button {item.Key}", parent);
            buttonRect.sizeDelta = settings.ButtonSize;
            var layoutElement = buttonRect.gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredWidth = settings.ButtonSize.x;
            layoutElement.preferredHeight = settings.ButtonSize.y;

            var buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = ClearButtonColor;
            buttonImage.raycastTarget = true;

            var button = buttonRect.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.SpriteSwap;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            button.interactable = item.Key != "currency";

            var vertical = buttonRect.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.childAlignment = TextAnchor.MiddleCenter;
            vertical.childControlWidth = false;
            vertical.childControlHeight = false;
            vertical.childForceExpandWidth = false;
            vertical.childForceExpandHeight = false;
            vertical.spacing = -2f;
            vertical.padding = new RectOffset(0, 0, 0, 0);

            var icon = CreateRect("Icon", buttonRect);
            icon.sizeDelta = settings.IconSize;
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.sprite = LoadSprite($"ui_sidebar_icon_{item.Key}_default.png");
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            button.targetGraphic = iconImage;

            var hover = LoadSprite($"ui_sidebar_icon_{item.Key}_hover.png");
            if (hover != null)
            {
                var state = button.spriteState;
                state.highlightedSprite = hover;
                state.pressedSprite = hover;
                state.selectedSprite = hover;
                button.spriteState = state;
            }

            var label = CreateText("Label", buttonRect, item.Label, item.Key == "currency" ? settings.CurrencyFontSize : settings.LabelFontSize, settings.LabelColor, FontStyles.Bold);
            label.rectTransform.sizeDelta = settings.LabelSize;
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            return button;
        }

        private static RectTransform EnsureCalloutPanelLayer(RectTransform gameplayLayers)
        {
            var layer = gameplayLayers.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == CalloutPanelLayerName && rect.parent == gameplayLayers);
            if (layer == null)
            {
                layer = new GameObject(CalloutPanelLayerName, typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            }

            layer.SetParent(gameplayLayers, false);
            layer.SetSiblingIndex(Mathf.Min(1, gameplayLayers.childCount - 1));
            Stretch(layer, Vector2.zero, Vector2.zero);
            var image = layer.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            return layer;
        }

        private static SidebarCalloutPanelController.PanelEntry[] RebuildCalloutPanels(
            RectTransform panelLayer,
            SidebarCalloutPanelSettings settings,
            IReadOnlyDictionary<string, Button> buttons,
            SidebarCalloutPanelController controller)
        {
            for (var i = panelLayer.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.DestroyImmediate(panelLayer.GetChild(i).gameObject);
            }

            var entries = new List<SidebarCalloutPanelController.PanelEntry>();
            foreach (var item in Items)
            {
                if (!buttons.TryGetValue(item.Key, out var button))
                {
                    continue;
                }

                if (item.Key == "currency")
                {
                    button.interactable = false;
                    continue;
                }

                var panel = CreateCalloutPanel(panelLayer, item.Key, settings);
                PopulateCalloutPanel(panel, item.Key, settings);

                var binding = button.GetComponent<SidebarPanelButton>();
                if (binding == null)
                {
                    binding = button.gameObject.AddComponent<SidebarPanelButton>();
                }

                binding.Bind(item.Key, controller);

                var entry = new SidebarCalloutPanelController.PanelEntry();
                entry.Bind(item.Key, button, panel);
                entries.Add(entry);
            }

            return entries.ToArray();
        }

        private static RectTransform CreateCalloutPanel(RectTransform parent, string key, SidebarCalloutPanelSettings settings)
        {
            var panel = CreateRect($"Sidebar Callout Panel {key}", parent);
            panel.anchorMin = new Vector2(0f, 1f);
            panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.sizeDelta = settings.GetPanelSize(key);
            panel.gameObject.SetActive(false);

            var image = panel.gameObject.AddComponent<Image>();
            image.color = settings.PanelColor;
            image.raycastTarget = true;

            var outline = panel.gameObject.AddComponent<Outline>();
            outline.effectColor = settings.OutlineColor;
            outline.effectDistance = new Vector2(settings.OutlineWidth, -settings.OutlineWidth);

            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = settings.GetContentSpacing(key);
            var contentPadding = settings.GetContentPadding(key);
            layout.padding = new RectOffset(
                Mathf.RoundToInt(contentPadding.x),
                Mathf.RoundToInt(contentPadding.x),
                Mathf.RoundToInt(contentPadding.y),
                Mathf.RoundToInt(contentPadding.y));

            return panel;
        }

        private static void PopulateCalloutPanel(RectTransform panel, string key, SidebarCalloutPanelSettings settings)
        {
            switch (key)
            {
                case "currency":
                    break;
                case "bag":
                    CreateBagAuthoredPanel(panel, key, settings);
                    break;
                case "deck":
                    CreateScrollList(panel, key, settings, new[]
                    {
                        ("공격", "보유 카드"),
                        ("수비", "보유 카드"),
                        ("대쉬", "보유 카드"),
                        ("방어 자세", "보유 카드"),
                        ("빠른 회피", "보유 카드"),
                        ("응급 처치", "보유 카드"),
                        ("집중", "보유 카드")
                    });
                    break;
                case "relic_curse":
                    CreateRelicAuthoredPanel(panel, key, settings);
                    break;
                case "settings":
                    CreateEmptyPanelSpace(panel, key, settings);
                    break;
            }
        }

        /// <summary>
        /// 가방 콜아웃 저작(리뉴얼). 골격은 <b>머리글 / 격자 / 상세 줄</b> 세 층이고 유물 패널과 같다 —
        /// 두 패널이 한 벌로 읽히게 하려고 같은 골격을 쓴다. 이름·설명은 칸에 넣지 않는다(68px에
        /// 이름을 넣으면 무조건 잘린다) — 칸은 그림 한 장, 글자는 상세 줄이 맡는다.
        /// </summary>
        private static void CreateBagAuthoredPanel(
            RectTransform parent,
            string key,
            SidebarCalloutPanelSettings settings)
        {
            var content = CreatePanelColumn(parent, key, settings, "Bag Content");
            CreatePanelHeader(content, key, settings, "Bag Title", "가방", "Bag Status", "0 / 3 슬롯");
            CreatePanelDivider(content, settings);

            var iconCellSize = settings.GetIconCellSize(key);
            var grid = CreateGrid(content, "Bag Slot Grid", iconCellSize, columns: 4);
            for (var i = 0; i < 8; i++)
            {
                CreateBagSlot(grid, i, settings);
            }

        }

        /// <summary>
        /// 유물 콜아웃 저작(리뉴얼). 종전에는 스크롤 자리표시 격자였다 — 저작 슬롯이 그대로 보여서
        /// 「빈 칸 20개」가 화면을 채웠다. 이제 뷰가 안 쓰는 칸을 끄므로 격자는 보유분만큼만 선다.
        /// </summary>
        private static void CreateRelicAuthoredPanel(
            RectTransform parent,
            string key,
            SidebarCalloutPanelSettings settings)
        {
            var content = CreatePanelColumn(parent, key, settings, "Relic Content");
            CreatePanelHeader(content, key, settings, "Relic Title", "유물", "Relic Status", "0개");
            CreatePanelDivider(content, settings);

            var iconCellSize = settings.GetIconCellSize(key);
            var grid = CreateGrid(content, "Relic Curse Grid", iconCellSize, columns: 5);
            for (var i = 0; i < 20; i++)
            {
                CreateRelicChip(grid, i, settings);
            }

        }

        /// <summary>두 패널이 공유하는 세로 골격(머리글 / 격자 / 상세 줄).</summary>
        private static RectTransform CreatePanelColumn(
            RectTransform parent,
            string key,
            SidebarCalloutPanelSettings settings,
            string name)
        {
            var content = CreateRect(name, parent);
            var panelSize = settings.GetPanelSize(key);
            var padding = settings.GetContentPadding(key);
            content.sizeDelta = new Vector2(
                Mathf.Max(10f, panelSize.x - padding.x * 2f),
                Mathf.Max(10f, panelSize.y - padding.y * 2f));

            var vertical = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vertical.childAlignment = TextAnchor.UpperLeft;
            vertical.childControlWidth = false;
            vertical.childControlHeight = false;
            vertical.childForceExpandWidth = false;
            vertical.childForceExpandHeight = false;
            vertical.spacing = 6f;

            // 격자가 보유 개수만큼만 서므로 세로 골격도 따라 줄어야 한다 — 안 그러면 패널 아래쪽에
            // 아무것도 없는 판이 남는다(리뉴얼 1차 판정에서 실제로 그랬다).
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return content;
        }

        private static void CreatePanelHeader(
            RectTransform content,
            string key,
            SidebarCalloutPanelSettings settings,
            string titleName,
            string titleValue,
            string statusName,
            string statusValue)
        {
            var header = CreateRect($"{titleName} Row", content);
            header.sizeDelta = new Vector2(content.sizeDelta.x, 22f);

            var title = CreateText(titleName, header, titleValue, settings.GetBodyFontSize(key), settings.AccentColor, FontStyles.Bold);
            title.rectTransform.anchorMin = new Vector2(0f, 0f);
            title.rectTransform.anchorMax = new Vector2(0f, 1f);
            title.rectTransform.pivot = new Vector2(0f, 0.5f);
            title.rectTransform.anchoredPosition = Vector2.zero;
            title.rectTransform.sizeDelta = new Vector2(120f, 0f);
            title.alignment = TextAlignmentOptions.Left;

            var status = CreateText(statusName, header, statusValue, settings.GetSmallFontSize(key), settings.MutedTextColor, FontStyles.Normal);
            status.rectTransform.anchorMin = new Vector2(1f, 0f);
            status.rectTransform.anchorMax = new Vector2(1f, 1f);
            status.rectTransform.pivot = new Vector2(1f, 0.5f);
            status.rectTransform.anchoredPosition = Vector2.zero;
            status.rectTransform.sizeDelta = new Vector2(Mathf.Max(10f, content.sizeDelta.x - 130f), 0f);
            status.alignment = TextAlignmentOptions.Right;
            status.textWrappingMode = TextWrappingModes.NoWrap;
            status.overflowMode = TextOverflowModes.Ellipsis;
        }

        /// <summary>머리글과 격자를 가르는 실선 한 줄 — 금색 강조를 아주 옅게 깐다.</summary>
        private static void CreatePanelDivider(RectTransform content, SidebarCalloutPanelSettings settings)
        {
            var divider = CreateRect("Panel Divider", content);
            divider.sizeDelta = new Vector2(content.sizeDelta.x, 1f);
            var image = divider.gameObject.AddComponent<Image>();
            var accent = settings.AccentColor;
            image.color = new Color(accent.r, accent.g, accent.b, 0.28f);
            image.raycastTarget = false;
        }

        private static RectTransform CreateGrid(RectTransform content, string name, Vector2 cellSize, int columns)
        {
            var grid = CreateRect(name, content);
            var rows = 2;
            grid.sizeDelta = new Vector2(
                cellSize.x * columns + 8f * (columns - 1),
                cellSize.y * rows + 8f * (rows - 1));

            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = cellSize;
            layout.spacing = new Vector2(8f, 8f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;

            // 뷰가 안 쓰는 칸을 끄므로 격자 높이는 보유 개수에 따라 달라진다 — 고정 높이로 두면
            // 칸이 넘칠 때 상세 줄 위로 흘러넘친다. 실제로 서는 줄 수에 맞춰 높이가 따라오게 한다.
            var fitter = grid.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            return grid;
        }

        /// <summary>
        /// 가방 칸. 그림 타깃을 <b>저작해 둔다</b> — 뷰에도 런타임 생성 폴백이 있지만, 저작해 두면
        /// 에디터에서 배치를 눈으로 조정할 수 있고 플레이 첫 프레임에 칸이 비지 않는다.
        /// </summary>
        private static void CreateBagSlot(RectTransform parent, int index, SidebarCalloutPanelSettings settings)
        {
            var cell = CreateRect($"Bag Slot {index}", parent);
            var image = cell.gameObject.AddComponent<Image>();
            image.color = new Color(0.16f, 0.18f, 0.30f, 0.55f);
            // 🔴 클릭·호버가 들어오는 문. 종전 저작은 이 값이 꺼져 있어서 아이템을 눌러도 아무 일도
            //    일어나지 않았다(뷰의 사용 핸들러는 처음부터 배선돼 있었다).
            image.raycastTarget = true;

            var outline = cell.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(settings.AccentColor.r, settings.AccentColor.g, settings.AccentColor.b, 0.5f);
            outline.effectDistance = new Vector2(1.5f, 1.5f);
            outline.useGraphicAlpha = false;

            var icon = CreateRect("Bag Slot Icon", cell);
            icon.anchorMin = Vector2.zero;
            icon.anchorMax = Vector2.one;
            icon.offsetMin = new Vector2(5f, 5f);
            icon.offsetMax = new Vector2(-5f, -5f);
            var iconImage = icon.gameObject.AddComponent<Image>();
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            icon.gameObject.SetActive(false);

            var label = CreateText("Bag Slot Label", cell, "+", 20f, settings.MutedTextColor, FontStyles.Bold);
            Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            var count = CreateText("Bag Slot Count", cell, string.Empty, 13f, settings.AccentColor, FontStyles.Bold);
            count.rectTransform.anchorMin = new Vector2(1f, 0f);
            count.rectTransform.anchorMax = new Vector2(1f, 0f);
            count.rectTransform.pivot = new Vector2(1f, 0f);
            count.rectTransform.anchoredPosition = new Vector2(-3f, 1f);
            count.rectTransform.sizeDelta = new Vector2(44f, 18f);
            count.alignment = TextAlignmentOptions.BottomRight;
            count.textWrappingMode = TextWrappingModes.NoWrap;
        }

        /// <summary>유물 칩. 판은 어둡게 두고 종류색은 테두리가 말한다(뷰가 색을 다시 칠한다).</summary>
        private static void CreateRelicChip(RectTransform parent, int index, SidebarCalloutPanelSettings settings)
        {
            var cell = CreateRect($"Relic Curse Slot {index}", parent);
            var image = cell.gameObject.AddComponent<Image>();
            image.color = new Color(0.16f, 0.18f, 0.31f, 0.96f);
            // 호버가 상세 줄을 띄우는 유일한 통로다.
            image.raycastTarget = true;

            var outline = cell.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(settings.AccentColor.r, settings.AccentColor.g, settings.AccentColor.b, 0.95f);
            outline.effectDistance = new Vector2(1.5f, 1.5f);
            outline.useGraphicAlpha = false;

            var label = CreateText("Icon Label", cell, string.Empty, 20f, settings.TextColor, FontStyles.Bold);
            Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
            label.alignment = TextAlignmentOptions.Center;
            label.textWrappingMode = TextWrappingModes.NoWrap;

            var meta = CreateText("Icon Meta", cell, string.Empty, 10f, settings.MutedTextColor, FontStyles.Normal);
            meta.rectTransform.anchorMin = new Vector2(0f, 0f);
            meta.rectTransform.anchorMax = new Vector2(1f, 0f);
            meta.rectTransform.pivot = new Vector2(0.5f, 0f);
            meta.rectTransform.anchoredPosition = Vector2.zero;
            meta.rectTransform.sizeDelta = new Vector2(0f, 14f);
            meta.alignment = TextAlignmentOptions.Bottom;
        }

        private static void CreatePlaceholderGrid(
            RectTransform parent,
            string key,
            SidebarCalloutPanelSettings settings,
            int columns,
            int rows)
        {
            var iconCellSize = settings.GetIconCellSize(key);
            var grid = CreateRect("Panel Placeholder Grid", parent);
            grid.sizeDelta = new Vector2(iconCellSize.x * columns + 8f * Mathf.Max(0, columns - 1), iconCellSize.y * rows);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = iconCellSize;
            layout.spacing = new Vector2(8f, 8f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;

            for (var i = 0; i < columns * rows; i++)
            {
                CreateDashedPlusCell(grid, settings);
            }
        }

        private static void CreateScrollablePlaceholderGrid(
            RectTransform parent,
            string key,
            SidebarCalloutPanelSettings settings,
            int columns,
            int totalSlots)
        {
            var viewport = CreateScrollViewport(parent, key, settings, "Panel Placeholder Scroll");
            var content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);

            var iconCellSize = settings.GetIconCellSize(key);
            var rows = Mathf.CeilToInt(totalSlots / (float)columns);
            content.sizeDelta = new Vector2(
                iconCellSize.x * columns + 8f * Mathf.Max(0, columns - 1),
                iconCellSize.y * rows + 8f * Mathf.Max(0, rows - 1));

            var layout = content.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = iconCellSize;
            layout.spacing = new Vector2(8f, 8f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = columns;

            for (var i = 0; i < totalSlots; i++)
            {
                CreateDashedPlusCell(content, settings);
            }

            var scroll = viewport.parent.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
        }

        private static RectTransform CreateScrollViewport(
            RectTransform parent,
            string key,
            SidebarCalloutPanelSettings settings,
            string name)
        {
            var scrollRoot = CreateRect(name, parent);
            var panelSize = settings.GetPanelSize(key);
            var padding = settings.GetContentPadding(key);
            scrollRoot.sizeDelta = new Vector2(
                Mathf.Max(10f, panelSize.x - padding.x * 2f),
                Mathf.Max(10f, panelSize.y - padding.y * 2f));

            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = false;

            var viewport = CreateRect("Viewport", scrollRoot);
            Stretch(viewport, Vector2.zero, Vector2.zero);
            var viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;
            var mask = viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;
            scroll.viewport = viewport;
            return viewport;
        }

        private static void CreateDashedPlusCell(RectTransform parent, SidebarCalloutPanelSettings settings)
        {
            var cell = CreateRect("Empty Grid Slot", parent);
            var image = cell.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.08f);
            image.raycastTarget = false;

            CreateDashedBorder(cell, settings.OutlineColor);
            var plus = CreateText("Plus", cell, "+", 26f, settings.MutedTextColor, FontStyles.Bold);
            Stretch(plus.rectTransform, Vector2.zero, Vector2.zero);
            plus.alignment = TextAlignmentOptions.Center;
        }

        private static void CreateDashedBorder(RectTransform parent, Color color)
        {
            const int horizontalDashCount = 5;
            const int verticalDashCount = 4;
            for (var i = 0; i < horizontalDashCount; i++)
            {
                var t = (i + 0.5f) / horizontalDashCount;
                CreateDash(parent, color, new Vector2(t, 1f), new Vector2(0.13f, 0f), new Vector2(0f, -1.5f), new Vector2(0f, 1f));
                CreateDash(parent, color, new Vector2(t, 0f), new Vector2(0.13f, 0f), new Vector2(0f, 1.5f), new Vector2(0f, 0f));
            }

            for (var i = 0; i < verticalDashCount; i++)
            {
                var t = (i + 0.5f) / verticalDashCount;
                CreateDash(parent, color, new Vector2(0f, t), new Vector2(0f, 0.16f), new Vector2(1.5f, 0f), new Vector2(0f, 0.5f));
                CreateDash(parent, color, new Vector2(1f, t), new Vector2(0f, 0.16f), new Vector2(-1.5f, 0f), new Vector2(1f, 0.5f));
            }
        }

        private static void CreateDash(RectTransform parent, Color color, Vector2 anchor, Vector2 sizePercent, Vector2 anchoredOffset, Vector2 pivot)
        {
            var dash = CreateRect("Dash", parent);
            dash.anchorMin = anchor - sizePercent * 0.5f;
            dash.anchorMax = anchor + sizePercent * 0.5f;
            dash.pivot = pivot;
            dash.anchoredPosition = anchoredOffset;
            dash.sizeDelta = new Vector2(sizePercent.x == 0f ? 2f : 0f, sizePercent.y == 0f ? 2f : 0f);
            var image = dash.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void CreateScrollList(
            RectTransform parent,
            string key,
            SidebarCalloutPanelSettings settings,
            (string Title, string Meta)[] rows)
        {
            var viewport = CreateScrollViewport(parent, key, settings, "Panel Text Scroll");
            var content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);

            var rowSize = settings.GetRowSize(key);
            content.sizeDelta = new Vector2(0f, rows.Length * (rowSize.y + 6f));
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;

            for (var i = 0; i < rows.Length; i++)
            {
                CreateTextListRow(content, key, rows[i].Title, rows[i].Meta, settings);
            }

            var scroll = viewport.parent.GetComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = viewport;
        }

        private static void CreateTextListRow(RectTransform parent, string key, string title, string meta, SidebarCalloutPanelSettings settings)
        {
            var rowSize = settings.GetRowSize(key);
            var row = CreateRect("Panel Scroll Row", parent);
            row.sizeDelta = rowSize;
            var image = row.gameObject.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.16f);
            image.raycastTarget = false;

            var horizontal = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = false;
            horizontal.childControlHeight = false;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;
            horizontal.spacing = 8f;
            horizontal.padding = new RectOffset(8, 8, 0, 0);

            var titleText = CreateText("Card Name", row, title, settings.GetBodyFontSize(key), settings.TextColor, FontStyles.Bold);
            titleText.rectTransform.sizeDelta = new Vector2(rowSize.x - 112f, rowSize.y);
            titleText.alignment = TextAlignmentOptions.Left;

            var metaText = CreateText("Card Meta", row, meta, settings.GetSmallFontSize(key), settings.MutedTextColor, FontStyles.Normal);
            metaText.rectTransform.sizeDelta = new Vector2(88f, rowSize.y);
            metaText.alignment = TextAlignmentOptions.Right;
        }

        private static void CreateEmptyPanelSpace(RectTransform parent, string key, SidebarCalloutPanelSettings settings)
        {
            var placeholder = CreateRect("Panel Empty Space", parent);
            var panelSize = settings.GetPanelSize(key);
            var padding = settings.GetContentPadding(key);
            placeholder.sizeDelta = new Vector2(Mathf.Max(10f, panelSize.x - padding.x * 2f), Mathf.Max(10f, panelSize.y - padding.y * 2f));
        }

        private static void CreateTextLine(RectTransform parent, string key, string text, float size, Color color, FontStyles style, SidebarCalloutPanelSettings settings)
        {
            var label = CreateText("Panel Text", parent, text, size, color, style);
            label.rectTransform.sizeDelta = new Vector2(settings.GetRowSize(key).x, 22f);
            label.alignment = TextAlignmentOptions.Left;
        }

        private static void CreateInfoRow(RectTransform parent, string key, string left, string right, SidebarCalloutPanelSettings settings)
        {
            var row = CreateRect("Panel Row", parent);
            var rowSize = settings.GetRowSize(key);
            row.sizeDelta = rowSize;
            var horizontal = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = false;
            horizontal.childControlHeight = false;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;
            horizontal.spacing = 8f;

            var leftText = CreateText("Left", row, left, settings.GetBodyFontSize(key), settings.TextColor, FontStyles.Normal);
            leftText.rectTransform.sizeDelta = new Vector2(rowSize.x - 68f, rowSize.y);
            leftText.alignment = TextAlignmentOptions.Left;
            var rightText = CreateText("Right", row, right, settings.GetSmallFontSize(key), settings.MutedTextColor, FontStyles.Bold);
            rightText.rectTransform.sizeDelta = new Vector2(60f, rowSize.y);
            rightText.alignment = TextAlignmentOptions.Right;
        }

        private static void CreateToggleRow(RectTransform parent, string key, string left, string right, SidebarCalloutPanelSettings settings)
        {
            var row = CreateRect("Panel Toggle Row", parent);
            var rowSize = settings.GetRowSize(key);
            row.sizeDelta = rowSize;
            var horizontal = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.childAlignment = TextAnchor.MiddleLeft;
            horizontal.childControlWidth = false;
            horizontal.childControlHeight = false;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;
            horizontal.spacing = 8f;

            var leftText = CreateText("Left", row, left, settings.GetBodyFontSize(key), settings.TextColor, FontStyles.Normal);
            leftText.rectTransform.sizeDelta = new Vector2(rowSize.x - 66f, rowSize.y);
            var pill = CreateRect("Toggle Pill", row);
            pill.sizeDelta = new Vector2(58f, 24f);
            var image = pill.gameObject.AddComponent<Image>();
            image.color = settings.AccentColor;
            image.raycastTarget = false;
            var pillText = CreateText("Toggle Text", pill, right, settings.GetSmallFontSize(key), Color.white, FontStyles.Bold);
            Stretch(pillText.rectTransform, Vector2.zero, Vector2.zero);
            pillText.alignment = TextAlignmentOptions.Center;
        }

        private static void CreateCheckRows(RectTransform parent, string key, string[] labels, SidebarCalloutPanelSettings settings)
        {
            var rowSize = settings.GetRowSize(key);
            for (var i = 0; i < labels.Length; i++)
            {
                var row = CreateRect("Panel Check Row", parent);
                row.sizeDelta = rowSize;
                var horizontal = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                horizontal.childAlignment = TextAnchor.MiddleLeft;
                horizontal.childControlWidth = false;
                horizontal.childControlHeight = false;
                horizontal.childForceExpandWidth = false;
                horizontal.childForceExpandHeight = false;
                horizontal.spacing = 8f;

                var box = CreateRect("Checkbox", row);
                box.sizeDelta = new Vector2(18f, 18f);
                var boxImage = box.gameObject.AddComponent<Image>();
                boxImage.color = new Color(1f, 1f, 1f, 0.25f);
                boxImage.raycastTarget = false;
                var boxOutline = box.gameObject.AddComponent<Outline>();
                boxOutline.effectColor = settings.OutlineColor;
                boxOutline.effectDistance = Vector2.one;

                var label = CreateText("Label", row, labels[i], settings.GetSmallFontSize(key), settings.TextColor, FontStyles.Normal);
                label.rectTransform.sizeDelta = new Vector2(rowSize.x - 70f, rowSize.y);
                var count = CreateText("Count", row, "x1", settings.GetSmallFontSize(key), settings.MutedTextColor, FontStyles.Bold);
                count.rectTransform.sizeDelta = new Vector2(34f, rowSize.y);
                count.alignment = TextAlignmentOptions.Right;
            }
        }

        private static void CreateIconGrid(RectTransform parent, string key, string[] labels, SidebarCalloutPanelSettings settings)
        {
            var rowSize = settings.GetRowSize(key);
            var iconCellSize = settings.GetIconCellSize(key);
            var grid = CreateRect("Panel Icon Grid", parent);
            grid.sizeDelta = new Vector2(rowSize.x, iconCellSize.y * 2f + 8f);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = iconCellSize;
            layout.spacing = new Vector2(8f, 8f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;

            for (var i = 0; i < labels.Length; i++)
            {
                var cell = CreateRect("Icon Cell", grid);
                var image = cell.gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.22f);
                image.raycastTarget = false;
                var outline = cell.gameObject.AddComponent<Outline>();
                outline.effectColor = settings.OutlineColor;
                outline.effectDistance = Vector2.one;

                var label = CreateText("Icon Label", cell, labels[i], settings.GetBodyFontSize(key), settings.AccentColor, FontStyles.Bold);
                Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
                label.alignment = TextAlignmentOptions.Center;
            }
        }

        private static void CreateMapRoute(RectTransform parent, string key, SidebarCalloutPanelSettings settings)
        {
            var route = CreateRect("Panel Map Route", parent);
            route.sizeDelta = new Vector2(settings.GetRowSize(key).x, 68f);
            var horizontal = route.gameObject.AddComponent<HorizontalLayoutGroup>();
            horizontal.childAlignment = TextAnchor.MiddleCenter;
            horizontal.childControlWidth = false;
            horizontal.childControlHeight = false;
            horizontal.childForceExpandWidth = false;
            horizontal.childForceExpandHeight = false;
            horizontal.spacing = 10f;

            for (var i = 0; i < 5; i++)
            {
                var node = CreateRect("Route Node", route);
                node.sizeDelta = i == 2 ? new Vector2(30f, 30f) : new Vector2(22f, 22f);
                var image = node.gameObject.AddComponent<Image>();
                image.color = i == 2 ? settings.AccentColor : new Color(1f, 1f, 1f, 0.28f);
                image.raycastTarget = false;
                var outline = node.gameObject.AddComponent<Outline>();
                outline.effectColor = settings.OutlineColor;
                outline.effectDistance = Vector2.one;
            }
        }

        private static void CreateDivider(RectTransform parent, string key, SidebarCalloutPanelSettings settings)
        {
            var divider = CreateRect("Panel Divider", parent);
            divider.sizeDelta = new Vector2(settings.GetRowSize(key).x, 2f);
            var image = divider.gameObject.AddComponent<Image>();
            image.color = settings.OutlineColor;
            image.raycastTarget = false;
        }

        private static TMP_Text CreateText(string name, RectTransform parent, string text, float size, Color color, FontStyles style)
        {
            var rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.raycastTarget = false;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GameplaySceneContract.KoreanFontAssetPath);
            if (font != null)
            {
                label.font = font;
            }

            return label;
        }

        private static RectTransform CreateRect(string name, RectTransform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.localScale = Vector3.one;
            return rect;
        }

        private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        private static Sprite LoadSprite(string filename)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>($"{SidebarSpriteRoot}/{filename}");
        }

        private static RectTransform FindRect(string name)
        {
            return UnityEngine.Object.FindObjectsByType<RectTransform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(rect => rect.name == name);
        }
    }
}
#endif
