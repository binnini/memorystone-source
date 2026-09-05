using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Cards.Unity;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>전리품 목록의 한 줄이 무엇인가.</summary>
    public enum LootRewardRowKind
    {
        /// <summary>엽전. 누르면 지갑으로 빨려들어간다.</summary>
        Money,

        /// <summary>
        /// 되돌려받은 엽전(2026-09-05 · 소매치기). 🔑 <b>알림 줄</b>이다 — 지갑에는 처치 시점에 이미
        /// 들어갔고(전리품 목록을 건너뛰어도 원금은 잃지 않는다), 이 줄은 「돌려받았다」를 눈에 보이게
        /// 할 뿐이다. 처치 보상 엽전과 <b>같은 줄에 합치지 않는다</b>: 하나는 보상이고 하나는 원금이라,
        /// 합치면 얼마를 벌었는지 읽을 수 없다(사용자 확정 — 액수 옆에 「(반환)」 명시).
        /// ⚠️append-only.
        /// </summary>
        MoneyReturned,

        /// <summary>소모품. 누르면 가방으로 빨려들어간다(만원이면 돈 폴백 — CR-10).</summary>
        Item,

        /// <summary>유물. 누르면 유물 칸으로 빨려들어간다.</summary>
        Relic,

        /// <summary>
        /// 부적 추가. 🔑 <b>이 줄만 성질이 다르다</b> — 받는 것이 아니라 <b>부적 3택을 여는</b> 줄이고,
        /// 언제나 목록에 선다(떨어지는 것이 아니라 처치의 기본 보상이라서).
        /// </summary>
        Card
    }

    /// <summary>전리품 목록의 한 줄.</summary>
    public readonly struct LootRewardRow
    {
        public LootRewardRow(LootRewardRowKind kind, string id, string label, Sprite icon = null, int amount = 0)
        {
            Kind = kind;
            Id = id ?? string.Empty;
            Label = label ?? string.Empty;
            Icon = icon;
            Amount = Math.Max(0, amount);
        }

        public LootRewardRowKind Kind { get; }
        public string Id { get; }
        public string Label { get; }
        public Sprite Icon { get; }

        /// <summary><see cref="LootRewardRowKind.Money"/>일 때의 금액.</summary>
        public int Amount { get; }
    }

    /// <summary>
    /// 처치 전리품 목록(DEC-2026-08-31-03 — DEC-2026-08-31-02 D-2 재개정, 사용자 확정 2026-08-31).
    ///
    /// <para>
    /// 🔑 <b>이 화면이 처치 보상의 입구다.</b> 종전에는 부적 3택이 화면이고 전리품이 그 아래 띠로
    /// 붙었는데, 뒤집었다 — 목록이 화면이고 <b>부적은 그 안의 한 줄</b>이다. 「부적 추가」를 누르면
    /// 기존 3택 팝업이 열리고, 고르고 나면 이 목록으로 돌아온다.
    /// </para>
    ///
    /// <para>
    /// 🔑 <b>멈춤은 여전히 한 번이다.</b> D-2가 지키려던 것은 「리듬」이고, 화면이 하나라는 점은
    /// 그대로다 — 달라진 것은 부적 선택이 클릭 뒤로 갔다는 것뿐이다. 그 대신 일반 몬스터
    /// 대부분에서 목록이 한 줄짜리가 되어 <b>덜 붐빈다</b>.
    /// </para>
    ///
    /// <para>
    /// 🔴 <b>「넘기기」는 남은 전리품을 버린다</b>(사용자 확정). 목록에 무엇이 남았는지 눈에 보이는
    /// 상태에서 누르는 것이라 함정이 아니고, 그래야 넘기기가 「전부 그만두기」라는 뜻으로 일관된다.
    /// </para>
    ///
    /// <para>
    /// <see cref="ShopPopupView"/>·<see cref="DeckPileListOverlayView"/>와 같은 절차 생성형
    /// 오버레이다 — 루트가 씬에 저작돼 있으면 그걸 쓰고, 없으면 런타임에 만든다. 뷰는 표시와
    /// 입력만 담당하고 무엇이 떨어졌는지·지급 성패는 전부 컨트롤러가 정한다.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LootRewardPopupView : MonoBehaviour
    {
        public const string RootName = "Loot Reward Overlay Root";

        // 규격(사용자 확정 2026-08-31 2차 — 보상뽑기 상자 UI를 기준으로 1.5배). 세로는 ContentSizeFitter가 잡는다.
        private static readonly Vector2 PanelSize = new Vector2(840f, 0f);
        private const float RowHeight = 100f;
        private const float IconSize = 66f;
        private const float RowFontSize = 27f;
        private const float TitleFontSize = 44f;

        /// <summary>
        /// 뒷배경을 얼마나 눌러 두는가. 🔑 보상뽑기 상자(부적 3택)와 <b>같은 값</b>이다 —
        /// 두 화면이 같은 순간에 번갈아 뜨므로 어둠의 깊이가 다르면 화면이 튄다.
        /// </summary>
        private const float BackdropAlpha = 0.72f;

        private static readonly Color PanelColor = new Color(0.11f, 0.12f, 0.19f, 0.97f);
        private static readonly Color AccentColor = new Color(0.898f, 0.769f, 0.404f, 1f);
        private static readonly Color RowColor = new Color(0.17f, 0.19f, 0.28f, 1f);
        private static readonly Color TextColor = new Color(0.94f, 0.95f, 0.99f, 1f);
        private static readonly Color MutedColor = new Color(0.68f, 0.71f, 0.85f, 1f);

        [SerializeField] private UiThemeAsset theme;

        private RectTransform panel;
        // The visible loot panel, or null while closed/hidden — the tutorial spotlight targets it.
        public RectTransform PanelRect => panel != null && panel.gameObject.activeInHierarchy ? panel : null;
        private RectTransform rowsRoot;
        private TMP_Text titleText;
        private Button skipButton;
        private Button hideButton;
        private Button restoreButton;
        private CanvasGroup rootCanvasGroup;

        private IReadOnlyList<LootRewardRow> rows = Array.Empty<LootRewardRow>();
        private Func<LootRewardRow, bool> onClaim;
        private Action onCardRowChosen;
        private Action onSkip;
        private Func<LootRewardRowKind, RectTransform> flyTargetResolver;

        public bool IsOpen => gameObject.activeSelf;

        public static LootRewardPopupView FindOrCreate(RectTransform gameplayLayers)
        {
            if (gameplayLayers == null)
            {
                return null;
            }

            var existing = gameplayLayers.GetComponentsInChildren<LootRewardPopupView>(includeInactive: true)
                .FirstOrDefault(view => view.name == RootName);
            if (existing != null)
            {
                return existing;
            }

            var root = gameplayLayers.GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == RootName);
            if (root == null)
            {
                var created = new GameObject(RootName, typeof(RectTransform));
                root = (RectTransform)created.transform;
                root.SetParent(gameplayLayers, worldPositionStays: false);
                root.anchorMin = Vector2.zero;
                root.anchorMax = Vector2.one;
                root.offsetMin = Vector2.zero;
                root.offsetMax = Vector2.zero;
            }

            return root.GetComponent<LootRewardPopupView>() ?? root.gameObject.AddComponent<LootRewardPopupView>();
        }

        /// <param name="claim">
        /// 돈·소모품·유물 줄을 눌렀을 때. <c>true</c>면 지급 성공이라 줄이 사라지고 연출이 난다.
        /// </param>
        /// <param name="cardRowChosen">「부적 추가」를 눌렀을 때 — 컨트롤러가 3택 팝업을 연다.</param>
        /// <param name="skip">「넘기기」 — 남은 전리품은 버려진다.</param>
        /// <param name="flyTarget">
        /// 종류별로 전리품이 빨려들어갈 사이드바 버튼. <see langword="null"/>을 돌려주면 연출 없이
        /// 줄만 사라진다(배선이 빠져도 지급은 이미 끝나 있다).
        /// </param>
        public void Show(
            IReadOnlyList<LootRewardRow> lootRows,
            Func<LootRewardRow, bool> claim,
            Action cardRowChosen,
            Action skip,
            Func<LootRewardRowKind, RectTransform> flyTarget = null)
        {
            rows = lootRows ?? Array.Empty<LootRewardRow>();
            onClaim = claim;
            onCardRowChosen = cardRowChosen;
            onSkip = skip;
            flyTargetResolver = flyTarget;

            // 🔴 <b>켜고 나서 짓는다.</b> UiProceduralPanel.Configure는 비활성 오브젝트에서 아무것도
            //    하지 않고(isActiveAndEnabled 가드) 복구를 OnEnable에 기대는데, 그 컴포넌트에는
            //    [ExecuteAlways]가 없어 <b>에디트 모드에서는 OnEnable이 뜨지 않는다</b> — 그래서
            //    끄고 지으면 판정 캡처에서 패널 판이 통째로 안 그려진다(실측). 순서만 지키면
            //    플레이·에디트 양쪽에서 같은 화면이 나온다.
            gameObject.SetActive(true);
            EnsureLayout();
            // 지난 처치에서 숨겨 둔 채 닫혔을 수 있다 — 열 때는 언제나 보이는 상태로 되돌린다.
            RestoreFromHidden();
            transform.SetAsLastSibling();
            RefreshRows();
        }

        /// <summary>컨트롤러가 목록을 갈아끼운다(부적 3택을 마치고 돌아왔을 때 등).</summary>
        public void SetRows(IReadOnlyList<LootRewardRow> lootRows)
        {
            rows = lootRows ?? Array.Empty<LootRewardRow>();
            if (IsOpen)
            {
                RefreshRows();
            }
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        // ── 레이아웃 ─────────────────────────────────────────────────────

        private void EnsureLayout()
        {
            if (panel != null && rowsRoot != null)
            {
                return;
            }

            var self = (RectTransform)transform;

            // 🔑 <b>가운데는 화면 가운데가 아니다.</b> 왼쪽 사이드바를 뺀 놀이판의 가운데라야
            //    보상뽑기 상자·잡화점·덱 목록과 같은 자리에 선다(같은 계약: ApplySidebarExclusionLayout).
            ApplySidebarExclusionLayout(self);

            var backdrop = self.GetComponent<Image>() ?? self.gameObject.AddComponent<Image>();
            backdrop.color = new Color(0f, 0f, 0f, BackdropAlpha);
            backdrop.raycastTarget = true;

            var panelObject = new GameObject(
                "Loot Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            panel = (RectTransform)panelObject.transform;
            panel.SetParent(self, worldPositionStays: false);
            panel.anchorMin = new Vector2(0.5f, 0.5f);
            panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = PanelSize;

            UiProceduralPanel.Attach(
                panelObject.GetComponent<Image>(),
                theme != null ? theme.PopupFillColor : PanelColor,
                theme != null ? theme.PopupBorderColor : AccentColor,
                2f,
                14f);

            var panelLayout = panelObject.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(26, 26, 22, 24);
            panelLayout.spacing = 16f;
            panelLayout.childAlignment = TextAnchor.UpperCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            var fitter = panelObject.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            titleText = CreateText(panel, "전리품!", TitleFontSize, AccentColor, bold: true, align: TextAlignmentOptions.Center);
            titleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 62f;

            var rowsObject = new GameObject("Loot Rows", typeof(RectTransform), typeof(VerticalLayoutGroup));
            rowsRoot = (RectTransform)rowsObject.transform;
            rowsRoot.SetParent(panel, worldPositionStays: false);
            var rowsLayout = rowsObject.GetComponent<VerticalLayoutGroup>();
            rowsLayout.spacing = 10f;
            rowsLayout.childControlWidth = true;
            rowsLayout.childControlHeight = true;
            rowsLayout.childForceExpandWidth = true;
            rowsLayout.childForceExpandHeight = false;

            // 보상뽑기 상자와 같은 버튼 줄 — 넘기기 | 숨기기. 「숨기기」는 목록을 잠시 치워
            // 사이드바·덱 목록을 들여다보게 해 주고, 떠 있는 「전리품 보기」로 되돌아온다.
            var actionsObject = new GameObject("Loot Actions", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var actions = (RectTransform)actionsObject.transform;
            actions.SetParent(panel, worldPositionStays: false);
            actionsObject.GetComponent<LayoutElement>().preferredHeight = 62f;
            var actionsLayout = actionsObject.GetComponent<HorizontalLayoutGroup>();
            actionsLayout.spacing = 16f;
            actionsLayout.childAlignment = TextAnchor.MiddleCenter;
            actionsLayout.childControlWidth = true;
            actionsLayout.childControlHeight = true;
            actionsLayout.childForceExpandWidth = true;
            actionsLayout.childForceExpandHeight = true;

            skipButton = CreateActionButton(actions, "Loot Skip Button", "넘기기");
            skipButton.onClick.RemoveAllListeners();
            skipButton.onClick.AddListener(() => onSkip?.Invoke());

            hideButton = CreateActionButton(actions, "Loot Hide Button", "숨기기");
            hideButton.onClick.RemoveAllListeners();
            hideButton.onClick.AddListener(HideTemporarily);
        }

        private Button CreateActionButton(RectTransform parent, string name, string label)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, worldPositionStays: false);
            buttonObject.GetComponent<LayoutElement>().preferredHeight = 62f;
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            UiButtonSkin.Apply(button, theme);
            CreateText(
                (RectTransform)buttonObject.transform, label, 24f, TextColor,
                bold: true, align: TextAlignmentOptions.Center, stretch: true);
            return button;
        }

        // ── 숨기기 / 전리품 보기 ─────────────────────────────────────────
        // 보상뽑기 상자(CardRewardPopupView)와 같은 계약이다: 루트 CanvasGroup을 투명·비차단으로
        // 두고, 부모 그룹을 무시하는 떠 있는 버튼 하나만 남겨 되돌아올 길을 만든다.
        // 🔴 <b>목록을 닫는 것이 아니다</b> — 닫으면 남은 전리품이 버려지고, 그건 「넘기기」의 몫이다.

        private void HideTemporarily()
        {
            // 🔴 `GetComponent<T>() ?? AddComponent<T>()`를 쓰지 말 것 — Unity의 가짜 null은 C#의
            //    `??`를 속인다(파괴된 컴포넌트가 non-null로 통과해 접근에서 터진다, 실측).
            if (rootCanvasGroup == null && !TryGetComponent(out rootCanvasGroup))
            {
                rootCanvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            rootCanvasGroup.alpha = 0f;
            rootCanvasGroup.interactable = false;
            rootCanvasGroup.blocksRaycasts = false;

            EnsureRestoreButton();
            restoreButton.gameObject.SetActive(true);
            restoreButton.transform.SetAsLastSibling();
        }

        private void RestoreFromHidden()
        {
            if (rootCanvasGroup != null)
            {
                rootCanvasGroup.alpha = 1f;
                rootCanvasGroup.interactable = true;
                rootCanvasGroup.blocksRaycasts = true;
            }

            if (restoreButton != null)
            {
                restoreButton.gameObject.SetActive(false);
            }
        }

        private void EnsureRestoreButton()
        {
            if (restoreButton != null)
            {
                return;
            }

            var buttonObject = new GameObject(
                "Loot Restore Button", typeof(RectTransform), typeof(CanvasGroup), typeof(Image), typeof(Button));
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(transform, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.sizeDelta = new Vector2(220f, 64f);
            rect.anchoredPosition = new Vector2(0f, 36f);

            // 루트가 알파 0·비차단이 되어도 이 버튼만은 보이고 눌려야 한다.
            var group = buttonObject.GetComponent<CanvasGroup>();
            group.ignoreParentGroups = true;
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            restoreButton = buttonObject.GetComponent<Button>();
            restoreButton.targetGraphic = buttonObject.GetComponent<Image>();
            UiButtonSkin.Apply(restoreButton, theme);
            CreateText(rect, "전리품 보기", 22f, TextColor, bold: true, align: TextAlignmentOptions.Center, stretch: true);
            restoreButton.onClick.AddListener(RestoreFromHidden);
            buttonObject.SetActive(false);
        }

        private void ApplySidebarExclusionLayout(RectTransform root)
        {
            var marker = FindFirstObjectByType<SidebarRootMarker>(FindObjectsInactive.Include);
            var sidebar = marker != null ? marker.transform as RectTransform : null;
            var sidebarWidth = sidebar != null ? Mathf.Max(sidebar.rect.width, sidebar.sizeDelta.x) : 0f;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = new Vector2(sidebarWidth, 0f);
            root.offsetMax = Vector2.zero;
        }

        private void RefreshRows()
        {
            EnsureLayout();
            ClearChildren(rowsRoot);

            foreach (var row in rows)
            {
                CreateRow(row);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);

            // 🔴 크기가 확정된 <b>뒤에</b> SDF 스킨을 다시 맞춘다 — 에디트 모드에는 크기 변경 콜백이
            //    오지 않아, 이게 없으면 판정 캡처에서 패널이 안 그려지고 줄이 알약이 된다.
            foreach (var skin in GetComponentsInChildren<UiProceduralPanel>(includeInactive: true))
            {
                skin.ResyncRectSize();
            }
        }

        private void CreateRow(LootRewardRow row)
        {
            var rowObject = new GameObject(
                $"Loot Row {row.Kind}", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var rect = (RectTransform)rowObject.transform;
            rect.SetParent(rowsRoot, worldPositionStays: false);
            rowObject.GetComponent<LayoutElement>().preferredHeight = RowHeight;

            var plate = rowObject.GetComponent<Image>();
            UiProceduralPanel.Attach(plate, RowColor, new Color(0f, 0f, 0f, 0f), 0f, 8f);

            var layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(18, 18, 0, 0);
            layout.spacing = 15f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            // 🔑 「부적 추가」 줄은 물건이 아니라 <b>부적 한 장</b>을 가리키므로 뒷면 그림을 쓴다.
            //    호출부가 따로 넘기지 않아도 되도록 여기서 해소한다 — 줄마다 갈릴 값이 아니다.
            var icon = row.Icon;
            if (icon == null && row.Kind == LootRewardRowKind.Card)
            {
                var catalog = RuntimeUiAssetCatalog.LoadDefault();
                icon = catalog != null ? catalog.CardBackSprite : null;
            }

            if (icon != null)
            {
                var iconObject = new GameObject("Loot Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                iconObject.transform.SetParent(rect, worldPositionStays: false);
                var element = iconObject.GetComponent<LayoutElement>();
                element.preferredWidth = IconSize;
                element.preferredHeight = IconSize;
                var image = iconObject.GetComponent<Image>();
                image.sprite = icon;
                image.preserveAspect = true;
                image.raycastTarget = false;
            }

            var label = CreateText(rect, RowLabel(row), RowFontSize, TextColor, bold: false, align: TextAlignmentOptions.MidlineLeft);
            var labelElement = label.gameObject.AddComponent<LayoutElement>();
            labelElement.flexibleWidth = 1f;

            // 🔑 「부적 추가」만 <b>여는</b> 줄이라 표식을 단다 — 받는 줄과 여는 줄이 눌렀을 때
            //    일어나는 일이 다르므로, 그 차이가 누르기 전에 보여야 한다.
            // 🔴 글자로 그리지 않는다: 출하 폰트(DNF)에 「›」가 없어 두부(□)로 깨졌다(실측).
            //    삼각형은 그려서 만들면 폰트 커버리지와 무관하다.
            if (row.Kind == LootRewardRowKind.Card)
            {
                CreateChevron(rect);
            }

            var button = rowObject.GetComponent<Button>();
            button.targetGraphic = plate;
            var captured = row;
            button.onClick.AddListener(() => OnRowClicked(captured, rect));

            BindRowHover(rect, row);
        }

        /// <summary>
        /// 소모품·유물 줄에 커서를 올리면 <b>이름과 효과</b>를 커서 옆에 띄운다(2026-09-02 #13).
        ///
        /// <para>🔑 사이드바 가방·유물 칸과 <b>같은 부품</b>(<see cref="SidebarRelicSlotHover"/> +
        /// <see cref="SidebarSlotTooltip"/>)을 쓴다. 같은 종류의 정보를 화면마다 다른 창으로 띄우면
        /// 플레이어는 다른 종류의 정보로 읽고, 규격도 두 벌이 되어 갈라진다 — 실제로 2026-08-10에
        /// 호버 툴팁 셋이 <c>ConstantPixelSize</c>로 각자 만들어져 해상도마다 크기가 갈린 적이 있다.
        /// 이 부품은 <c>ObjectInfoTooltipHudPresenter</c>를 공유하므로 그 사고가 구조적으로 없다.</para>
        ///
        /// <para>엽전·부적 줄에는 붙이지 않는다 — 글자가 이미 전부를 말하고 있고(「120 엽전」),
        /// 부적은 <b>누르면 열리는</b> 3택 화면이 설명 자리다.</para>
        /// </summary>
        private static void BindRowHover(RectTransform rowRect, LootRewardRow row)
        {
            if (row.Kind != LootRewardRowKind.Item && row.Kind != LootRewardRowKind.Relic)
            {
                return;
            }

            string title;
            string description;
            string category;
            if (row.Kind == LootRewardRowKind.Item)
            {
                category = "소모품";
                ConsumableItemCatalog.TryGet(row.Id, out var item);
                title = item != null && !string.IsNullOrWhiteSpace(item.DisplayName) ? item.DisplayName : row.Label;
                description = item != null && !string.IsNullOrWhiteSpace(item.Description) ? item.Description : "효과 정보 없음";
            }
            else
            {
                category = "유물";
                PlayerPermanentItemCatalog.TryGet(row.Id, out var relic);
                title = relic != null && !string.IsNullOrWhiteSpace(relic.DisplayName) ? relic.DisplayName : row.Label;
                description = relic != null && !string.IsNullOrWhiteSpace(relic.Description) ? relic.Description : "효과 정보 없음";
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = row.Id;
            }

            var lines = new List<ObjectInfoTooltipHudPresenter.Line>(3)
            {
                new ObjectInfoTooltipHudPresenter.Line(category, SidebarSlotTooltip.CategoryColor),
                new ObjectInfoTooltipHudPresenter.Line(description, SidebarSlotTooltip.BodyColor),
                new ObjectInfoTooltipHudPresenter.Line("클릭해 받기", SidebarSlotTooltip.CategoryColor)
            };

            var hover = rowRect.GetComponent<SidebarRelicSlotHover>()
                        ?? rowRect.gameObject.AddComponent<SidebarRelicSlotHover>();
            var capturedTitle = title;
            hover.Bind(() => SidebarSlotTooltip.Show(capturedTitle, lines), SidebarSlotTooltip.Hide);
        }

        /// <summary>「여는 줄」 표식. 폰트에 기대지 않으려고 사각형 두 장을 겹쳐 꺾쇠 모양을 만든다.</summary>
        private static void CreateChevron(RectTransform parent)
        {
            var holder = new GameObject("Loot Chevron", typeof(RectTransform), typeof(LayoutElement));
            var rect = (RectTransform)holder.transform;
            rect.SetParent(parent, worldPositionStays: false);
            var element = holder.GetComponent<LayoutElement>();
            element.preferredWidth = 16f;
            element.preferredHeight = 20f;

            for (var i = 0; i < 2; i++)
            {
                var barObject = new GameObject("Chevron Bar", typeof(RectTransform), typeof(Image));
                var bar = (RectTransform)barObject.transform;
                bar.SetParent(rect, worldPositionStays: false);
                bar.sizeDelta = new Vector2(13f, 3f);
                bar.anchoredPosition = new Vector2(0f, i == 0 ? 4f : -4f);
                bar.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? -38f : 38f);
                var image = barObject.GetComponent<Image>();
                image.color = MutedColor;
                image.raycastTarget = false;
            }
        }

        private static string RowLabel(LootRewardRow row)
        {
            switch (row.Kind)
            {
                case LootRewardRowKind.Money:
                    return $"{row.Amount} 엽전";
                case LootRewardRowKind.Card:
                    return "부적 추가";
                default:
                    return row.Label;
            }
        }

        // ── 입력 ─────────────────────────────────────────────────────────

        private void OnRowClicked(LootRewardRow row, RectTransform rowRect)
        {
            if (row.Kind == LootRewardRowKind.Card)
            {
                onCardRowChosen?.Invoke();
                return;
            }

            if (onClaim == null || !onClaim(row))
            {
                return;
            }

            // 지급은 이미 끝났다 — 연출은 그 사실을 <b>보여줄</b> 뿐이고 실패할 수 있는 자리가 아니다.
            PlayFlyToSidebar(row, rowRect);

            var remaining = rows.Where(r => !(r.Kind == row.Kind && r.Id == row.Id && r.Amount == row.Amount)).ToArray();
            rows = remaining;
            RefreshRows();
        }

        /// <summary>
        /// 받은 전리품이 그것이 사는 사이드바 칸으로 빨려들어간다. 줄을 지운 자리에서 임시
        /// 아이콘 하나가 날아갈 뿐이라, 연출이 끊겨도 게임 상태에는 아무 영향이 없다.
        ///
        /// <para>🔴 <b>연출의 수명은 이 뷰가 아니라 날아가는 아이콘 자신이 쥔다</b>(2026-09-02 #1).
        /// 날아가는 아이콘은 <b>캔버스 아래</b>에 서는데(뷰의 자식이 아니다) 종전에는 그것을 지우는 일이
        /// <b>이 뷰에서 돌던 코루틴</b>의 마지막 줄 하나뿐이었다. 그런데 연출을 시작한 바로 다음 줄이
        /// 목록 갱신이라 <b>마지막 전리품이면 그 자리에서 뷰가 닫힌다</b> — 코루틴만 죽고 아이콘은
        /// 캔버스에 그대로 남았다(받을 때마다 하나씩, 알파도 안 깎인 채). 그래서 코루틴을
        /// <see cref="LootFlyAnimator"/>에 옮겨 <b>아이콘이 자기 자신을 지우게</b> 했다:
        /// 수명이 다른 두 물건을 한 코루틴에 묶으면 호스트가 먼저 죽는다.</para>
        /// </summary>
        private void PlayFlyToSidebar(LootRewardRow row, RectTransform rowRect)
        {
            var target = flyTargetResolver?.Invoke(row.Kind);
            if (target == null || rowRect == null || row.Icon == null || !isActiveAndEnabled)
            {
                return;
            }

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            var flyObject = new GameObject("Loot Fly", typeof(RectTransform), typeof(Image), typeof(LootFlyAnimator));
            var fly = (RectTransform)flyObject.transform;
            fly.SetParent(canvas.transform, worldPositionStays: false);
            fly.sizeDelta = new Vector2(IconSize, IconSize);
            fly.position = rowRect.position;
            var image = flyObject.GetComponent<Image>();
            image.sprite = row.Icon;
            image.preserveAspect = true;
            image.raycastTarget = false;

            flyObject.GetComponent<LootFlyAnimator>().Begin(target);
        }

        // ── 도우미 ───────────────────────────────────────────────────────

        private static TMP_Text CreateText(
            RectTransform parent,
            string value,
            float size,
            Color color,
            bool bold = false,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft,
            bool stretch = false)
        {
            var textObject = new GameObject("Loot Text", typeof(RectTransform));
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, worldPositionStays: false);
            if (stretch)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }

            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value ?? string.Empty;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.raycastTarget = false;
            if (bold)
            {
                text.fontStyle = FontStyles.Bold;
            }

            // 한글 폰트 정본은 KoreanFontProvider다 — 뷰마다 폰트를 나르면 화면마다 글꼴이 갈린다.
            if (bold)
            {
                KoreanFontProvider.ApplyTitle(text);
            }
            else
            {
                KoreanFontProvider.Apply(text);
            }

            return text;
        }

        /// <summary>
        /// 🔴 <c>Destroy</c>만 쓰면 에디트 모드(판정 캡처)에서 동작하지 않고, 플레이 모드에서도
        /// 프레임 끝까지 지연돼 새 줄과 옛 줄이 한 프레임 겹친다 — 먼저 레이아웃에서 떼어낸다
        /// (<see cref="CardRewardPopupView"/>·<see cref="ShopPopupView"/>와 같은 결).
        /// </summary>
        private static void ClearChildren(RectTransform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.transform.SetParent(null, worldPositionStays: false);
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }
    }

    /// <summary>
    /// 사이드바로 빨려들어가는 전리품 아이콘 <b>한 장</b>의 연출. 자기 자신을 지우는 것까지가 이 컴포넌트의
    /// 일이다 — 그래서 띄운 화면이 먼저 닫혀도 아이콘이 캔버스에 남지 않는다(2026-09-02 #1).
    ///
    /// <para>🔑 종전 결함의 요점은 「연출 오브젝트는 캔버스 아래, 그것을 지우는 코루틴은 팝업 뷰에」였다.
    /// 팝업이 닫히면 코루틴만 죽고 오브젝트가 남는다. <b>지우는 코드를 지워질 물건 위에 올려 두면</b>
    /// 그 갈래가 아예 없어진다.</para>
    /// </summary>
    internal sealed class LootFlyAnimator : MonoBehaviour
    {
        private const float DurationSeconds = 0.42f;

        private RectTransform target;

        public void Begin(RectTransform flyTarget)
        {
            target = flyTarget;
            StartCoroutine(FlyRoutine());
        }

        private IEnumerator FlyRoutine()
        {
            var fly = (RectTransform)transform;
            var image = GetComponent<Image>();
            var from = fly.position;
            var elapsed = 0f;

            while (elapsed < DurationSeconds)
            {
                // 대상이 사라지면(패널 교체 등) 연출만 접는다 — 아래의 자기 파괴는 그대로 지난다.
                if (target == null)
                {
                    break;
                }

                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / DurationSeconds);
                var eased = 1f - ((1f - t) * (1f - t));

                fly.position = Vector3.Lerp(from, target.position, eased);
                fly.localScale = Vector3.one * Mathf.Lerp(1f, 0.35f, eased);
                if (image != null)
                {
                    var color = image.color;
                    color.a = Mathf.Lerp(1f, 0f, t * t);
                    image.color = color;
                }

                yield return null;
            }

            Destroy(gameObject);
        }
    }
}
