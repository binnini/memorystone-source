using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class DeckPileListOverlayView : MonoBehaviour
    {
        public const string RootName = "Deck Pile List Overlay Root";

        // Built-in generated-default colours (used when no UiThemeAsset is assigned). Kept identical to
        // the previously-shipped hardcoded values so adopting a theme is visually a no-op.
        private static readonly Color DefaultPanelColor = new Color32(0x30, 0x34, 0x68, 0xF5);
        private static readonly Color DefaultBackdropColor = new Color(0f, 0f, 0f, 0.42f);
        private static readonly Color DefaultTextColor = new Color(0.92f, 0.96f, 1f, 1f);
        private static readonly Color DefaultCardPanelColor = new Color32(0x30, 0x34, 0x68, 0xFF);
        private static readonly Color DefaultHighlightColor = new Color(0.27f, 0.82f, 1f, 1f);

        // Generated defaults route through the assigned UiThemeAsset when present; otherwise the built-ins
        // above. These drive only generated/newly-created objects — scene-authored values are untouched.
        private Color PanelColor => theme != null ? theme.PanelColor : DefaultPanelColor;
        private Color BackdropColor => theme != null ? theme.BackdropColor : DefaultBackdropColor;
        private Color TextColor => theme != null ? theme.TextPrimary : DefaultTextColor;
        private Color CardPanelColor => theme != null ? theme.CardPanelColor : DefaultCardPanelColor;
        private Color HighlightColor => theme != null ? theme.TextAccent : DefaultHighlightColor;

        [Header("Generated defaults (off = Scene/Inspector owns presentation)")]
        [Tooltip("When enabled, runtime re-applies generated RectTransform / LayoutGroup defaults to existing scene objects. Newly created objects always get defaults.")]
        [SerializeField] private bool applyGeneratedLayout;
        [Tooltip("When enabled, runtime re-applies generated TMP / Image color/font defaults to existing scene objects. Newly created objects always get defaults.")]
        [SerializeField] private bool applyGeneratedStyle;
        [Tooltip("Central UI theme. When assigned, generated-default colours come from it; leave empty to use the built-in defaults.")]
        [SerializeField] private UiThemeAsset theme;

        [Header("Generated layout mode: fill area excluding the Sidebar")]
        [Tooltip("Generated layout mode. Only adjusts the overlay root when enabled; otherwise the authored root RectTransform is left alone.")]
        [SerializeField] private bool fillAreaExcludingSidebar;
        [SerializeField] private RectTransform excludedSidebar;
        [SerializeField] private float sidebarGap = 0f;

        [Header("Card assets")]
        [SerializeField] private GameObject moveCardFrontPrefab;
        [SerializeField] private GameObject actionCardFrontPrefab;

        // 상태 카드는 프리팹 분기로 못 가른다 — 저작 EffectType이 Status여도 CombatCardKind는
        // Move로 뭉개져서(ToKind의 default 분기) 이동 프리팹을 타고 들어온다. 그래서 손패 레인과
        // 같은 방식으로 바인딩 시점에 프레임 스프라이트만 갈아 끼운다.
        [SerializeField] private Sprite statusCardFrameSprite;

        private RectTransform backdropClickTarget;
        private Button backdropButton;
        private RectTransform panel;
        private TMP_Text titleText;
        private TMP_Text bodyText;
        private RectTransform sortBar;
        private RectTransform cardScrollRoot;
        private RectTransform cardContent;
        private Button closeButton;
        private CombatState latestState;
        private PlayerStateSnapshot latestSnapshot;
        private TMP_FontAsset latestFont;
        private PileViewKind activeKind = PileViewKind.None;
        private DeckCardSortMode sortMode = DeckCardSortMode.AcquiredOrder;
        private bool sortAscending = true;
        private PileViewKind renderedKind = PileViewKind.None;
        private string renderedSignature;
        private static TMP_FontAsset koreanFontAsset;
        private static TMP_FontAsset deckFontAsset;

        // Host-injected so this view avoids a reverse dependency on Combat.Unity's
        // KoreanFontProvider/TooltipFontProvider (mirrors TutorialDirector.LabelFontApplier
        // from cs:253 and BottomCardHudView.KoreanFontApplier).
        public Func<TMP_FontAsset> KoreanFontLoader { get; set; }
        public Func<TMP_FontAsset> DeckFontLoader { get; set; }

        public PileViewKind ActiveKind => activeKind;
        public bool IsOpen => activeKind != PileViewKind.None && gameObject.activeSelf;

        // The shared close button (deck/draw/discard/exile all reuse this overlay). Resolved lazily by
        // EnsureCloseButton() when the overlay is open, so the tutorial glow can point at it. May be null
        // until the overlay has been laid out at least once.
        public Button CloseButton => closeButton;

        public enum PileViewKind
        {
            None,
            Deck,
            DrawPile,
            DiscardPile,
            ExilePile
        }

        private enum DeckCardSortMode
        {
            AcquiredOrder,
            Kind,
            Cost,
            Name
        }

        public static DeckPileListOverlayView FindOrCreate(RectTransform gameplayLayers)
        {
            if (gameplayLayers == null)
            {
                return null;
            }

            var existing = gameplayLayers.GetComponentsInChildren<DeckPileListOverlayView>(includeInactive: true)
                .FirstOrDefault(view => view.name == RootName);
            if (existing != null)
            {
                return existing;
            }

            var root = gameplayLayers.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == RootName);
            if (root == null)
            {
                return null;
            }

            var view = root.GetComponent<DeckPileListOverlayView>() ?? root.gameObject.AddComponent<DeckPileListOverlayView>();
            if (view.activeKind == PileViewKind.None && root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }

            return view;
        }

        public void Refresh(CombatState state, PlayerStateSnapshot snapshot, TMP_FontAsset font)
        {
            latestState = state;
            latestSnapshot = snapshot;
            latestFont = ResolveDeckFont(font);
            EnsureLayout();
            ApplyFont(latestFont);
            if (activeKind == PileViewKind.None)
            {
                gameObject.SetActive(false);
                return;
            }

            if (IsOpen)
            {
                RefreshContent();
            }
        }

        public void OpenDeckList()
        {
            Open(PileViewKind.Deck);
        }

        public void OpenDrawPile()
        {
            Open(PileViewKind.DrawPile);
        }

        public void OpenDiscardPile()
        {
            Open(PileViewKind.DiscardPile);
        }

        public void OpenExilePile()
        {
            Open(PileViewKind.ExilePile);
        }

        /// <summary>Raised whenever the overlay is closed (close button, backdrop, or Esc). The tutorial
        /// listens to this to advance a "close the deck panel" step.</summary>
        public event System.Action Closed;

        public void Close()
        {
            var wasOpen = activeKind != PileViewKind.None || gameObject.activeSelf;
            activeKind = PileViewKind.None;
            gameObject.SetActive(false);
            if (wasOpen)
            {
                Closed?.Invoke();
            }
        }

        private void Awake()
        {
            EnsureLayout();
            if (activeKind == PileViewKind.None)
            {
                gameObject.SetActive(false);
            }
        }

        /// <summary>Frame on which an open pile overlay consumed an ESC press to close itself. Lets other ESC
        /// listeners (e.g. the sidebar settings toggle) ignore the same press regardless of Update order.</summary>
        public static int EscConsumedFrame { get; private set; } = -1;

        private void Update()
        {
            if (IsOpen && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
            {
                EscConsumedFrame = Time.frameCount;
                Close();
            }
        }

        private void Open(PileViewKind kind)
        {
            activeKind = kind;
            HydrateLatestState(force: true);
            latestFont = ResolveDeckFont(latestFont);
            EnsureLayout();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            RefreshContent();
        }

        private void HydrateLatestState(bool force)
        {
            if (!force && latestState != null)
            {
                return;
            }

            // Searches by MonoBehaviour + OfType<ICombatCardHudHost> (not FindObjectsByType<MapCombatController>)
            // so this Cards/Hud-candidate view has no compile-time dependency on the concrete
            // MapCombatController type, mirroring the ICombatAudioHost widening pattern.
            var hosts = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OfType<ICombatCardHudHost>()
                .ToList();
            var controller = hosts.FirstOrDefault(candidate => candidate.State != null) ?? hosts.FirstOrDefault();
            if (controller == null)
            {
                return;
            }

            if (controller.State == null && Application.isPlaying)
            {
                controller.InitializeIntegration();
            }

            if (controller.State == null)
            {
                return;
            }

            latestState = controller.State;
            latestSnapshot = latestState.CreatePlayerStateSnapshot();
        }

        // Resolves authored references and wires runtime behavior. The overlay shell is expected
        // to come from the scene/prefab; runtime only instantiates data cards from CardFront.
        private void EnsureLayout()
        {
            var rect = transform as RectTransform;
            ResolveExcludedSidebarIfMissing();
            if (fillAreaExcludingSidebar || excludedSidebar != null)
            {
                ApplySidebarExclusionLayout(rect);
            }
            else if (applyGeneratedLayout)
            {
                Stretch(rect);
            }

            var rootImage = GetComponent<Image>();
            var rootImageCreated = rootImage == null;
            if (rootImageCreated)
            {
                rootImage = gameObject.AddComponent<Image>();
            }
            if (applyGeneratedStyle || rootImageCreated)
            {
                rootImage.color = BackdropColor;
            }
            rootImage.raycastTarget = false;

            EnsureBackdrop();
            if (backdropClickTarget == null)
            {
                return;
            }

            EnsurePanel();
            if (panel == null)
            {
                return;
            }

            EnsureTitle();
            EnsureCloseButton();
            EnsureSortBar();
            EnsureBody();
            EnsureScroll();
        }

        private void EnsureBackdrop()
        {
            backdropClickTarget = ResolveChildRect(transform, "Deck Pile List Overlay Backdrop Click Target", out var created);
            if (backdropClickTarget == null)
            {
                return;
            }

            if (applyGeneratedLayout || created)
            {
                Stretch(backdropClickTarget);
            }

            var backdropImage = ResolveImage(backdropClickTarget, out var imageCreated);
            if (applyGeneratedStyle || imageCreated)
            {
                backdropImage.color = Color.clear;
            }
            backdropImage.raycastTarget = true;

            backdropButton = backdropClickTarget.GetComponent<Button>() ?? backdropClickTarget.gameObject.AddComponent<Button>();
            backdropButton.targetGraphic = backdropImage;
            backdropButton.onClick.RemoveAllListeners();
            backdropButton.onClick.AddListener(Close);
        }

        private void EnsurePanel()
        {
            panel = ResolveChildRect(transform, "Deck Pile List Overlay Panel", out var created);
            if (panel == null)
            {
                return;
            }

            if (applyGeneratedLayout || created)
            {
                panel.SetAsLastSibling();
                panel.anchorMin = new Vector2(0.5f, 0.5f);
                panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.pivot = new Vector2(0.5f, 0.5f);
                panel.anchoredPosition = new Vector2(0f, 20f);
                panel.sizeDelta = new Vector2(840f, 600f);
            }

            var panelImage = ResolveImage(panel, out var imageCreated);
            if (applyGeneratedStyle || imageCreated)
            {
                panelImage.color = PanelColor;
            }
            panelImage.raycastTarget = true;

            // P4 popup skin, full-bleed variant (runtime attach; the flat PanelColor image above stays as
            // the fallback look when the skin material is missing). This overlay fills the play area flush
            // against the sidebar (2026-07-25 user feedback), so it reads as a surface rather than a
            // floating window: keep the theme-routed fill but drop the border and corner rounding.
            var panelSkin = panel.GetComponent<UiProceduralPanel>();
            if (panelSkin == null)
            {
                panelSkin = panel.gameObject.AddComponent<UiProceduralPanel>();
            }

            var skinFill = theme != null ? theme.PopupFillColor : (Color)new Color32(0x1B, 0x24, 0x38, 0xF7);
            panelSkin.Configure(skinFill, Color.clear, 0f, 0f);
            // Full-bleed, radius-0 surface: suppress the P6 texture so the top-rim highlight never draws a
            // horizontal seam across the sidebar-flush edge. Keep it as flat as the P4/P5 shipped look.
            panelSkin.ConfigureTexture(0f, Color.clear, 0f, 0f);
        }

        private void EnsureTitle()
        {
            titleText = ResolveChildText(panel, "Deck Pile List Overlay Title", 32f, FontStyles.Bold, TextAlignmentOptions.TopLeft, TextColor, out var created);
            if (titleText == null)
            {
                return;
            }

            if (applyGeneratedLayout || created)
            {
                titleText.rectTransform.anchorMin = new Vector2(0f, 1f);
                titleText.rectTransform.anchorMax = new Vector2(1f, 1f);
                titleText.rectTransform.pivot = new Vector2(0f, 1f);
                titleText.rectTransform.anchoredPosition = new Vector2(34f, -28f);
                titleText.rectTransform.sizeDelta = new Vector2(-142f, 48f);
            }
        }

        private void EnsureCloseButton()
        {
            closeButton = panel.GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(button => button.name == "Deck Pile List Overlay Close Button");
            var created = closeButton == null;
            if (created)
            {
                return;
            }

            var rect = closeButton.GetComponent<RectTransform>();
            if (applyGeneratedLayout || created)
            {
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-64f, -34f);
                rect.sizeDelta = new Vector2(96f, 44f);
            }

            var image = ResolveImage(rect, out var imageCreated);
            if (applyGeneratedStyle || imageCreated)
            {
                image.color = CardPanelColor;
            }
            image.raycastTarget = true;
            closeButton.targetGraphic = image;

            var label = ResolveChildText(rect, "Label_TMP", 18f, FontStyles.Bold, TextAlignmentOptions.Center, TextColor, out var labelCreated);
            if (applyGeneratedLayout || labelCreated)
            {
                Stretch(label.rectTransform);
            }
            label.text = "\uB2EB\uAE30";

            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Close);
            // P4 unified button skin (the flat CardPanelColor image stays as the fallback look).
            UiButtonSkin.Apply(closeButton, theme);
        }

        private void EnsureSortBar()
        {
            sortBar = ResolveChildRect(panel, "Deck Pile List Overlay Sort Bar", out var created);
            if (sortBar == null)
            {
                return;
            }

            if (applyGeneratedLayout || created)
            {
                sortBar.anchorMin = new Vector2(0f, 1f);
                sortBar.anchorMax = new Vector2(1f, 1f);
                sortBar.pivot = new Vector2(0.5f, 1f);
                sortBar.anchoredPosition = new Vector2(0f, -88f);
                sortBar.sizeDelta = new Vector2(-68f, 40f);
            }

            var sortImage = ResolveImage(sortBar, out var imageCreated);
            if (applyGeneratedStyle || imageCreated)
            {
                sortImage.color = CardPanelColor;
            }
            sortImage.raycastTarget = false;

            EnsureSortLabels();
        }

        private void EnsureBody()
        {
            bodyText = ResolveChildText(panel, "Deck Pile List Overlay Empty Body", 22f, FontStyles.Normal, TextAlignmentOptions.Center, TextColor, out var created);
            if (bodyText == null)
            {
                return;
            }

            if (applyGeneratedLayout || created)
            {
                bodyText.rectTransform.anchorMin = new Vector2(0f, 0f);
                bodyText.rectTransform.anchorMax = new Vector2(1f, 1f);
                bodyText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                bodyText.rectTransform.anchoredPosition = new Vector2(0f, -18f);
                bodyText.rectTransform.sizeDelta = new Vector2(-92f, -180f);
            }
        }

        private void EnsureScroll()
        {
            cardScrollRoot = ResolveChildRect(panel, "Deck Pile List Card Scroll", out var created);
            if (cardScrollRoot == null)
            {
                return;
            }

            if (applyGeneratedLayout || created)
            {
                cardScrollRoot.anchorMin = new Vector2(0f, 0f);
                cardScrollRoot.anchorMax = new Vector2(1f, 1f);
                cardScrollRoot.pivot = new Vector2(0.5f, 0.5f);
                cardScrollRoot.anchoredPosition = new Vector2(0f, -34f);
                cardScrollRoot.sizeDelta = new Vector2(-68f, -172f);
            }

            var viewport = ResolveChildRect(cardScrollRoot, "Viewport", out var viewportCreated);
            if (viewport == null)
            {
                return;
            }

            if (applyGeneratedLayout || viewportCreated)
            {
                Stretch(viewport);
            }
            var viewportImage = ResolveImage(viewport, out var viewportImageCreated);
            if (applyGeneratedStyle || viewportImageCreated)
            {
                viewportImage.color = Color.white;
            }
            else if (viewportImage.color.a <= 0f)
            {
                var color = viewportImage.color;
                color.a = 1f;
                viewportImage.color = color;
            }
            viewportImage.raycastTarget = true;
            var mask = viewport.GetComponent<Mask>() ?? viewport.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            cardContent = ResolveChildRect(viewport, "Content", out var contentCreated);
            if (cardContent == null)
            {
                return;
            }

            if (applyGeneratedLayout || contentCreated)
            {
                cardContent.anchorMin = new Vector2(0f, 1f);
                cardContent.anchorMax = new Vector2(1f, 1f);
                cardContent.pivot = new Vector2(0.5f, 1f);
                cardContent.anchoredPosition = Vector2.zero;
                cardContent.sizeDelta = Vector2.zero;
            }

            var grid = cardContent.GetComponent<GridLayoutGroup>();
            var gridCreated = grid == null;
            if (gridCreated)
            {
                grid = cardContent.gameObject.AddComponent<GridLayoutGroup>();
            }
            if (applyGeneratedLayout || gridCreated)
            {
                grid.padding = new RectOffset(18, 18, 18, 24);
                grid.cellSize = new Vector2(188f, 264f);
                grid.spacing = new Vector2(26f, 24f);
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.childAlignment = TextAnchor.UpperCenter;
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = 4;
            }

            var fitter = cardContent.GetComponent<ContentSizeFitter>();
            var fitterCreated = fitter == null;
            if (fitterCreated)
            {
                fitter = cardContent.gameObject.AddComponent<ContentSizeFitter>();
            }
            if (applyGeneratedLayout || fitterCreated)
            {
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            // ScrollRect wiring is runtime behavior: it must reference the viewport/content to work.
            var scroll = cardScrollRoot.GetComponent<ScrollRect>() ?? cardScrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = cardContent;
            scroll.horizontal = false;
            scroll.vertical = true;
        }

        private void ApplySidebarExclusionLayout(RectTransform root)
        {
            var sidebarWidth = excludedSidebar != null ? Mathf.Max(excludedSidebar.rect.width, excludedSidebar.sizeDelta.x) : 0f;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = new Vector2(sidebarWidth + sidebarGap, 0f);
            root.offsetMax = Vector2.zero;
        }

        private void RefreshContent()
        {
            if (titleText == null || sortBar == null || bodyText == null || cardScrollRoot == null || cardContent == null)
            {
                return;
            }

            var cards = GetCards(activeKind);
            titleText.text = GetTitle(activeKind);
            sortBar.gameObject.SetActive(activeKind == PileViewKind.Deck && cards.Count > 0);
            bodyText.gameObject.SetActive(cards.Count == 0);
            cardScrollRoot.gameObject.SetActive(cards.Count > 0);
            if (cards.Count == 0)
            {
                bodyText.text = GetEmptyBody(activeKind, latestState);
            }

            RefreshCards(cards);
            ApplyFont(latestFont);
        }

        private IReadOnlyList<CombatCardSnapshot> GetCards(PileViewKind kind)
        {
            if (latestState == null)
            {
                return Array.Empty<CombatCardSnapshot>();
            }

            switch (kind)
            {
                case PileViewKind.Deck:
                    return SortCards(latestState.GetDeckListCards());
                case PileViewKind.DrawPile:
                    return latestState.GetDrawPileCards();
                case PileViewKind.DiscardPile:
                    return latestState.GetDiscardPileCards();
                case PileViewKind.ExilePile:
                    return latestState.GetExilePileCards();
                default:
                    return Array.Empty<CombatCardSnapshot>();
            }
        }

        private IReadOnlyList<CombatCardSnapshot> SortCards(IReadOnlyList<CombatCardSnapshot> source)
        {
            var cards = (source ?? Array.Empty<CombatCardSnapshot>()).ToList();
            IOrderedEnumerable<CombatCardSnapshot> ordered = null;
            switch (sortMode)
            {
                case DeckCardSortMode.Kind:
                    ordered = sortAscending
                        ? cards.OrderBy(card => KindSortKey(card.Kind)).ThenBy(card => card.Cost).ThenBy(card => card.Name)
                        : cards.OrderByDescending(card => KindSortKey(card.Kind)).ThenBy(card => card.Cost).ThenBy(card => card.Name);
                    break;
                case DeckCardSortMode.Cost:
                    ordered = sortAscending
                        ? cards.OrderBy(card => card.Cost).ThenBy(card => card.Name)
                        : cards.OrderByDescending(card => card.Cost).ThenBy(card => card.Name);
                    break;
                case DeckCardSortMode.Name:
                    ordered = sortAscending
                        ? cards.OrderBy(card => card.Name).ThenBy(card => card.InstanceId)
                        : cards.OrderByDescending(card => card.Name).ThenBy(card => card.InstanceId);
                    break;
            }

            return ordered == null ? cards : ordered.ToList();
        }

        private static int KindSortKey(CombatCardKind kind)
        {
            switch (kind)
            {
                case CombatCardKind.Move:        return 0;
                case CombatCardKind.Attack:      return 10;
                case CombatCardKind.Defend:      return 20;
                case CombatCardKind.Scout:       return 30;
                case CombatCardKind.Investigate: return 40;
                case CombatCardKind.FieldObject: return 50;
                case CombatCardKind.Buff:        return 60;
                case CombatCardKind.Utility:     return 70;
                default:                          return 99;
            }
        }

        private void SelectSortMode(DeckCardSortMode mode)
        {
            if (sortMode == mode)
            {
                sortAscending = !sortAscending;
            }
            else
            {
                sortMode = mode;
                sortAscending = true;
            }

            EnsureSortLabels();
            if (IsOpen)
            {
                RefreshContent();
            }
        }

        private void RefreshCards(IReadOnlyList<CombatCardSnapshot> cards)
        {
            if (cardContent == null) return;

            // Refresh() runs every frame (GameplayHudBridge.refreshEveryFrame). Rebuilding the
            // card instances unconditionally destroys/recreates every child each frame, which makes
            // the hierarchy un-inspectable and wastes allocations. Only rebuild when the displayed
            // set actually changed.
            var signature = BuildCardsSignature(cards);
            if (activeKind == renderedKind
                && signature == renderedSignature
                && cardContent.childCount == cards.Count)
            {
                return;
            }

            while (cardContent.childCount > 0)
                DestroyImmediate(cardContent.GetChild(0).gameObject);

            renderedKind = activeKind;
            renderedSignature = signature;

            foreach (var card in cards)
            {
                // 세우는 절차는 CardFrontListInstance가 단일 출처다 — 도감도 같은 함수를 쓴다.
                // 여기에 다시 풀어 쓰면 두 화면이 갈라진다.
                var prefab = GetPrefabForKind(card.Kind);
                var view = CardFrontListInstance.Create(prefab, cardContent, card, ResolveStatusCardFrameSprite());
                if (view != null)
                {
                    view.gameObject.name = $"DeckPileOverlayCard_{SafeObjectName(card.Name)}";
                }
            }
        }

        private static string BuildCardsSignature(IReadOnlyList<CombatCardSnapshot> cards)
        {
            if (cards == null || cards.Count == 0)
            {
                return string.Empty;
            }

            var sb = new System.Text.StringBuilder(cards.Count * 8);
            foreach (var card in cards)
            {
                sb.Append(card.InstanceId).Append(':').Append(card.Cost).Append(':').Append(card.Name).Append('|');
            }

            return sb.ToString();
        }

        private Sprite ResolveStatusCardFrameSprite()
        {
            if (statusCardFrameSprite != null) return statusCardFrameSprite;
            // 빌드 안전 폴백(2026-08-19 #16 계열): 런타임 생성 경로에서 SerializeField가 비면
            // Resources의 RuntimeUiAssetCatalog가 정본이다. AssetDatabase는 에디터 최후 폴백.
            var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
            if (catalog != null && catalog.StatusCardFrameSprite != null) return statusCardFrameSprite = catalog.StatusCardFrameSprite;
#if UNITY_EDITOR
            return statusCardFrameSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Cards/card_frame_status.png");
#else
            return null;
#endif
        }

        private GameObject GetPrefabForKind(CombatCardKind kind)
        {
            var catalogForPrefab = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
            if (kind == CombatCardKind.Move)
            {
                if (moveCardFrontPrefab != null) return moveCardFrontPrefab;
                if (catalogForPrefab != null && catalogForPrefab.MoveCardFrontPrefab != null) return moveCardFrontPrefab = catalogForPrefab.MoveCardFrontPrefab;
#if UNITY_EDITOR
                return moveCardFrontPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Cards/CardFront_Move.prefab");
#else
                return null;
#endif
            }
            else
            {
                if (actionCardFrontPrefab != null) return actionCardFrontPrefab;
                if (catalogForPrefab != null && catalogForPrefab.ActionCardFrontPrefab != null) return actionCardFrontPrefab = catalogForPrefab.ActionCardFrontPrefab;
#if UNITY_EDITOR
                return actionCardFrontPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Cards/CardFront_Action.prefab");
#else
                return null;
#endif
            }
        }

        private void EnsureSortLabels()
        {
            if (sortBar == null)
            {
                return;
            }

            var labels = new[] { "\uD68D\uB4DD\uC21C", "\uCE74\uB4DC \uC720\uD615", "\uBE44\uC6A9\uC21C", "\uC774\uB984\uC21C" };
            var modes = new[] { DeckCardSortMode.AcquiredOrder, DeckCardSortMode.Kind, DeckCardSortMode.Cost, DeckCardSortMode.Name };
            for (var i = 0; i < labels.Length; i++)
            {
                var buttonName = $"SortLabel_{i + 1:00}";
                var mode = modes[i];
                var isActive = sortMode == mode;

                var text = FindChildText(sortBar, buttonName);
                var created = text == null;
                if (created)
                {
                    var go = new GameObject(buttonName, typeof(RectTransform), typeof(TextMeshProUGUI));
                    go.transform.SetParent(sortBar, false);
                    text = go.GetComponent<TMP_Text>();
                }

                if (latestFont != null)
                {
                    text.font = latestFont;
                }
                // 2026-07-25 user feedback: 15pt sort labels were too small to read.
                text.fontSize = 22f;
                text.fontStyle = FontStyles.Bold;
                text.alignment = TextAlignmentOptions.Center;
                text.color = isActive ? HighlightColor : TextColor;
                text.raycastTarget = true;

                if (applyGeneratedLayout || created)
                {
                    text.rectTransform.anchorMin = new Vector2(i / 4f, 0f);
                    text.rectTransform.anchorMax = new Vector2((i + 1) / 4f, 1f);
                    text.rectTransform.offsetMin = new Vector2(8f, 5f);
                    text.rectTransform.offsetMax = new Vector2(-8f, -5f);
                }

                text.text = labels[i] + (isActive ? (sortAscending ? " \u2191" : " \u2193") : string.Empty);

                var button = text.GetComponent<Button>() ?? text.gameObject.AddComponent<Button>();
                button.targetGraphic = text;
                var colorBlock = button.colors;
                colorBlock.normalColor = isActive ? HighlightColor : TextColor;
                colorBlock.highlightedColor = new Color(0.50f, 0.90f, 1f);
                colorBlock.pressedColor = new Color(0.16f, 0.56f, 0.90f);
                button.colors = colorBlock;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectSortMode(mode));
            }
        }

        private void ApplyFont(TMP_FontAsset font)
        {
            var resolvedFont = ResolveDeckFont(font);
            if (resolvedFont == null)
            {
                return;
            }

            foreach (var text in GetComponentsInChildren<TMP_Text>(includeInactive: true))
            {
                text.font = resolvedFont;
            }
        }

        private TMP_FontAsset ResolveDeckFont(TMP_FontAsset preferred)
        {
            // The deck pile list UI uses the DNFForgedBlade-Light SDF display font regardless of the
            // Korean UI font the bridge passes in. TooltipFontProvider loads it build-safely (Resources)
            // and that face covers the full Hangul syllable block, so Korean text never breaks.
            if (deckFontAsset == null)
            {
                deckFontAsset = DeckFontLoader?.Invoke();
            }
            if (deckFontAsset != null)
            {
                return deckFontAsset;
            }

            // Defensive fallbacks if the DNF asset is ever missing.
            if (preferred != null)
            {
                return preferred;
            }

            if (latestFont != null)
            {
                return latestFont;
            }

            if (koreanFontAsset == null)
            {
                koreanFontAsset = KoreanFontLoader?.Invoke();
            }
            return koreanFontAsset;
        }

        private void ResolveExcludedSidebarIfMissing()
        {
            if (excludedSidebar != null)
            {
                return;
            }

            // Resolve by SidebarRootMarker component rather than the "Sidebar" GameObject name so the planned
            // de-Prototype rename can't silently stop the overlay from excluding the sidebar from its dimming.
            // The serialized excludedSidebar still wins when present; this is the un-wired fallback.
            var parent = transform.parent;
            if (parent != null)
            {
                var localMarker = parent.GetComponentsInChildren<SidebarRootMarker>(includeInactive: true)
                    .FirstOrDefault();
                excludedSidebar = localMarker != null ? localMarker.transform as RectTransform : null;
            }

            if (excludedSidebar == null)
            {
                var marker = FindFirstObjectByType<SidebarRootMarker>(FindObjectsInactive.Include);
                excludedSidebar = marker != null ? marker.transform as RectTransform : null;
            }
        }

        // Finds an authored child TMP by name and applies style only when explicitly requested.
        private TMP_Text ResolveChildText(Transform parent, string objectName, float size, FontStyles style, TextAlignmentOptions alignment, Color color, out bool created)
        {
            var text = FindChildText(parent, objectName);
            created = false;

            if (text != null && applyGeneratedStyle)
            {
                text.font = latestFont;
                text.fontSize = size;
                text.fontStyle = style;
                text.alignment = alignment;
                text.color = color;
            }
            if (text != null)
            {
                text.raycastTarget = false;
            }
            return text;
        }

        private static RectTransform ResolveChildRect(Transform parent, string objectName, out bool created)
        {
            var rect = FindChildRect(parent, objectName);
            created = false;
            return rect;
        }

        private static Image ResolveImage(RectTransform owner, out bool created)
        {
            var image = owner.GetComponent<Image>();
            created = image == null;
            if (created)
            {
                image = owner.gameObject.AddComponent<Image>();
            }
            return image;
        }

        private static RectTransform FindChildRect(Transform parent, string name)
        {
            return parent.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == name);
        }

        private static TMP_Text FindChildText(Transform parent, string name)
        {
            return parent.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.name == name);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string GetTitle(PileViewKind kind)
        {
            switch (kind)
            {
                case PileViewKind.Deck: return "\uB371 \uBAA9\uB85D";
                case PileViewKind.DrawPile: return "\uB4DC\uB85C\uC6B0 \uB354\uBBF8";
                case PileViewKind.DiscardPile: return "\uBC84\uB9B0 \uCE74\uB4DC \uB354\uBBF8";
                case PileViewKind.ExilePile: return "\uC18C\uBA78 \uB354\uBBF8";
                default: return string.Empty;
            }
        }

        private static string GetEmptyBody(PileViewKind kind, CombatState state)
        {
            if (state == null)
            {
                return "\uCE74\uB4DC \uC815\uBCF4\uB97C \uBD88\uB7EC\uC62C \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.";
            }

            switch (kind)
            {
                case PileViewKind.Deck: return "\uD45C\uC2DC\uD560 \uB371 \uCE74\uB4DC\uAC00 \uC5C6\uC2B5\uB2C8\uB2E4.";
                case PileViewKind.DrawPile: return "\uB4DC\uB85C\uC6B0 \uB354\uBBF8\uAC00 \uBE44\uC5B4 \uC788\uC2B5\uB2C8\uB2E4.";
                case PileViewKind.DiscardPile: return "\uBC84\uB9B0 \uCE74\uB4DC \uB354\uBBF8\uAC00 \uBE44\uC5B4 \uC788\uC2B5\uB2C8\uB2E4.";
                case PileViewKind.ExilePile: return "\uC18C\uBA78 \uB354\uBBF8\uAC00 \uBE44\uC5B4 \uC788\uC2B5\uB2C8\uB2E4.";
                default: return string.Empty;
            }
        }


        private static string SafeObjectName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "Card";
            }

            var chars = value.Where(char.IsLetterOrDigit).Take(16).ToArray();
            return chars.Length == 0 ? "Card" : new string(chars);
        }
    }
}

