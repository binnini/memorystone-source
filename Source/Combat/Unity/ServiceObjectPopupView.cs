using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>어느 서비스 스크린인가 — 배경 일러스트·존 배치가 이 값으로 갈린다.</summary>
    public enum ServiceObjectScreen
    {
        CamperVan,
        Workshop,
    }

    /// <summary>선택지의 진행 방식 — 즉시 적용 / 카드 1장 선택 / 카드 선택 후 전·후 비교 확인.</summary>
    public enum ServiceOptionFlow
    {
        Instant,
        CardPick,
        CardPickCompare,
    }

    /// <summary>서비스 스크린의 선택지 버튼 하나(라벨·상세·활성 여부는 컨트롤러가 주입).</summary>
    public sealed class ServiceObjectOptionModel
    {
        public ServiceObjectOptionModel(
            string id,
            string title,
            string detail,
            ServiceOptionFlow flow,
            bool enabled = true,
            string disabledDetail = "")
        {
            Id = id ?? string.Empty;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
            Flow = flow;
            Enabled = enabled;
            DisabledDetail = disabledDetail ?? string.Empty;
        }

        public string Id { get; }
        public string Title { get; }
        public string Detail { get; }
        public ServiceOptionFlow Flow { get; }
        public bool Enabled { get; }
        public string DisabledDetail { get; }
    }

    /// <summary>카드 선택 목록의 한 행: 실행 API에 넘길 키(instance id)와 표시 라벨.</summary>
    public readonly struct ServiceCardCandidate
    {
        public ServiceCardCandidate(string key, string label, CombatCardSnapshot? snapshot = null)
        {
            Key = key ?? string.Empty;
            Label = label ?? string.Empty;
            Snapshot = snapshot;
        }

        public string Key { get; }
        public string Label { get; }

        /// <summary>
        /// 실물 카드를 그릴 스냅샷. 🔑 <b>비어 있어도 화면은 선다</b> — 종전 텍스트 행으로 물러난다
        /// (잡화점 카드 제거와 같은 계약: <see cref="ShopRemovalCandidate"/>).
        /// </summary>
        public CombatCardSnapshot? Snapshot { get; }
    }

    /// <summary>전/후 비교 확인 화면에 세울 카드 스냅샷 쌍.</summary>
    public readonly struct ServiceCardComparePair
    {
        public ServiceCardComparePair(CombatCardSnapshot before, CombatCardSnapshot after)
            : this(before, after, null, null)
        {
        }

        /// <summary>정의 쌍까지 실으면 요약이 스냅샷에 없는 축(타수·버프/디버프·상태이상)도 말한다(효과 연마 2차).</summary>
        public ServiceCardComparePair(
            CombatCardSnapshot before,
            CombatCardSnapshot after,
            SeoulPlayup.CardCore.CardDefinition beforeDefinition,
            SeoulPlayup.CardCore.CardDefinition afterDefinition)
        {
            Before = before;
            After = after;
            BeforeDefinition = beforeDefinition;
            AfterDefinition = afterDefinition;
            IsValid = !string.IsNullOrWhiteSpace(before.Id) && !string.IsNullOrWhiteSpace(after.Id);
        }

        public CombatCardSnapshot Before { get; }
        public CombatCardSnapshot After { get; }
        public SeoulPlayup.CardCore.CardDefinition BeforeDefinition { get; }
        public SeoulPlayup.CardCore.CardDefinition AfterDefinition { get; }
        public bool IsValid { get; }
    }

    /// <summary>
    /// 캠핑카·공작소가 공유하는 「택1 서비스」 스크린(camper-workshop-plan.md P1 → §7.5 U-4에서
    /// 풀스크린으로 개편, 2026-08-18). 덱 목록과 같은 **사이드바 제외 꽉 채움** 레이아웃 위에
    /// imagegen 배경(스타일 보드 67fe1d93 채택본)을 깔고, 선택지·버튼은 배경의 quiet zone에
    /// 앉는다. 뷰는 표시·입력만 — 판단은 전부 컨트롤러 콜백(ShopPopupView 규약).
    ///
    /// 연마 플로우(2단): 선택지 → 카드 목록 패널(후보는 컨트롤러가 CanRefine으로 거른다) →
    /// 전/후 비교 확인(실제 카드 프레임 2장) → 확정/돌아가기. 취소는 목록으로 복귀(Q-3).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ServiceObjectPopupView : MonoBehaviour
    {
        public const string RootName = "Service Object Overlay Root";
        private const float BackdropAspect = 1920f / 1080f;

        /// <summary>고르는 격자의 한 칸 — 잡화점 카드 제거 타일과 같은 규격이라 두 화면이 같아 보인다.</summary>
        /// <summary>고르는 격자의 칸 크기 — 잡화점 제거 격자와 <b>같은 값</b>이다(2026-09-02 #7).
        /// 두 화면이 같은 문법으로 지어졌으므로 한쪽만 키우면 그때부터 갈린다.</summary>
        private static readonly Vector2 PickCellSize = new Vector2(296f, 340f);

        /// <summary>격자 열 수 — 잡화점과 같은 4열.</summary>
        private const int PickColumnCount = 4;

        private static readonly Color DefaultBaseColor = new Color(0.06f, 0.07f, 0.12f, 1f);
        private static readonly Color DefaultPanelColor = new Color32(0x30, 0x34, 0x68, 0xF5);
        private static readonly Color DefaultTextColor = new Color(0.92f, 0.96f, 1f, 1f);
        private static readonly Color DefaultMutedColor = new Color(0.72f, 0.76f, 0.84f, 1f);
        private static readonly Color DefaultAccentColor = new Color32(0xC9, 0xA2, 0x27, 0xFF);

        /// <summary>
        /// 연마로 <b>좋아진 수치</b>의 색(2026-09-02 #9 · 사용자 확정: "변하는 수치를 초록색으로").
        ///
        /// <para>강조색(금색)을 쓰지 않는 이유: 이 화면에서 금색은 이미 <b>엽전·가격</b>의 어휘라
        /// 「달라진 값」과 「돈」이 같은 색으로 읽힌다. 초록은 이 판에서 다른 뜻으로 쓰이지 않아
        /// 「나아졌다」 하나만 말한다. 어두운 패널 위에서 읽히도록 채도를 낮추고 밝기를 올린 잎색이다.</para>
        /// </summary>
        private static readonly Color ImprovedColor = new Color32(0x6F, 0xD1, 0x7A, 0xFF);

        [Tooltip("Central UI theme. When assigned, generated-default colours come from it; leave empty to use the built-in defaults.")]
        [SerializeField] private UiThemeAsset theme;

        [Header("Fullscreen layout (사이드바 제외 — 덱 목록과 같은 계약)")]
        [SerializeField] private RectTransform excludedSidebar;
        [SerializeField] private float sidebarGap = 0f;

        // 배경 일러스트(스타일 보드 채택본). 🔴직렬화 참조가 정본, AssetDatabase 폴백은 에디터
        // 전용(DeckPileListOverlayView와 같은 계약) — 빌드에 실을 때 저작 루트에 참조를 채운다.
        [SerializeField] private Sprite camperBackdropSprite;
        [SerializeField] private Sprite workshopBackdropSprite;

        // 선택지 일러(보드 ⑩ Q-E: StS식 미니 일러 카드 — 그림이 면적 전부, 라벨은 아래 밴드).
        // 제거는 공용 향로 1종(Q-F) — 잡화점 배경의 향로와 같은 모티프. 같은 직렬화 계약.
        [SerializeField] private Sprite healOptionSprite;
        [SerializeField] private Sprite healthIconSprite;
        [SerializeField] private Sprite refineOptionSprite;
        [SerializeField] private Sprite removeOptionSprite;

        // 비교 화면의 실제 카드 프레임(같은 계약).
        [SerializeField] private GameObject moveCardFrontPrefab;
        [SerializeField] private GameObject actionCardFrontPrefab;
        [SerializeField] private Sprite statusCardFrameSprite;

        private Color PanelColor => theme != null ? theme.PanelColor : DefaultPanelColor;
        private Color TextColor => theme != null ? theme.TextPrimary : DefaultTextColor;
        private Color MutedColor => theme != null ? theme.TextMuted : DefaultMutedColor;
        private Color AccentColor => theme != null ? theme.TextAccent : DefaultAccentColor;

        private Image backdropImage;
        private AspectRatioFitter backdropFitter;
        private RectTransform optionListRoot;
        private RectTransform pickPanel;
        private RectTransform pickContent;
        private TMP_Text pickTitleText;
        private TMP_Text pickSubtitleText;
        private RectTransform comparePanel;
        private RectTransform compareCardsRoot;
        private TMP_Text compareDiffText;
        private TMP_Text titleText;
        private TMP_Text subtitleText;
        private TMP_Text messageText;
        private bool layoutBuilt;

        private ServiceObjectScreen activeScreen;
        private IReadOnlyList<ServiceObjectOptionModel> options = Array.Empty<ServiceObjectOptionModel>();
        private Func<ServiceObjectOptionModel, bool> onInstantRequested;
        private Func<ServiceObjectOptionModel, IReadOnlyList<ServiceCardCandidate>> cardCandidatesProvider;
        private Func<ServiceObjectOptionModel, ServiceCardCandidate, ServiceCardComparePair> comparePairProvider;
        private Func<ServiceObjectOptionModel, ServiceCardCandidate, bool> onCardConfirmed;
        private Action onLeaveRequested;

        private ServiceObjectOptionModel activeOption;
        private ServiceCardCandidate pendingCandidate;
        private bool hasPendingCandidate;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>ESC를 이 뷰가 소비한 프레임(<see cref="ShopPopupView.EscConsumedFrame"/>과 같은 프로토콜).</summary>
        public static int EscConsumedFrame { get; private set; } = -1;

        public static ServiceObjectPopupView FindOrCreate(RectTransform gameplayLayers)
        {
            if (gameplayLayers == null)
            {
                return null;
            }

            var existing = gameplayLayers.GetComponentsInChildren<ServiceObjectPopupView>(includeInactive: true)
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
                Stretch(root);
            }

            var view = root.GetComponent<ServiceObjectPopupView>() ?? root.gameObject.AddComponent<ServiceObjectPopupView>();
            if (root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }

            return view;
        }

        public void Show(
            ServiceObjectScreen screen,
            string title,
            string subtitle,
            IReadOnlyList<ServiceObjectOptionModel> serviceOptions,
            Func<ServiceObjectOptionModel, bool> instantRequested,
            Func<ServiceObjectOptionModel, IReadOnlyList<ServiceCardCandidate>> candidatesProvider,
            Func<ServiceObjectOptionModel, ServiceCardCandidate, ServiceCardComparePair> comparePairs,
            Func<ServiceObjectOptionModel, ServiceCardCandidate, bool> cardConfirmed,
            Action leaveRequested)
        {
            activeScreen = screen;
            options = serviceOptions ?? Array.Empty<ServiceObjectOptionModel>();
            onInstantRequested = instantRequested;
            cardCandidatesProvider = candidatesProvider;
            comparePairProvider = comparePairs;
            onCardConfirmed = cardConfirmed;
            onLeaveRequested = leaveRequested;
            activeOption = null;
            hasPendingCandidate = false;

            EnsureLayout();
            ApplyScreen(screen);
            if (titleText != null)
            {
                titleText.text = title ?? string.Empty;
            }

            if (subtitleText != null)
            {
                subtitleText.text = subtitle ?? string.Empty;
            }

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            // 사이드바를 비워 둔 모달은 사이드바 콜아웃 판(가방·유물)까지 덮어선 안 된다 — 같은 부모의
            // 형제인 사이드바 시스템을 이 모달 위로 되올린다(SidebarExclusionOrdering 참조).
            ResolveExcludedSidebarIfMissing();
            SidebarExclusionOrdering.RaiseSidebarAbove(transform, excludedSidebar);
            HidePickPanel();
            HideComparePanel();
            RefreshOptions();
            SetMessage(string.Empty);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void SetMessage(string message)
        {
            if (messageText != null)
            {
                messageText.text = message ?? string.Empty;
            }
        }

        private void Update()
        {
            if (!IsOpen || Keyboard.current?.escapeKey.wasPressedThisFrame != true)
            {
                return;
            }

            EscConsumedFrame = Time.frameCount;
            if (comparePanel != null && comparePanel.gameObject.activeSelf)
            {
                // Q-3: 비교 확인의 취소는 스크린 닫기가 아니라 목록으로 복귀.
                HideComparePanel();
                ShowPickPanel(activeOption);
                return;
            }

            if (pickPanel != null && pickPanel.gameObject.activeSelf)
            {
                HidePickPanel();
                return;
            }

            onLeaveRequested?.Invoke();
        }

        private void RefreshOptions()
        {
            if (optionListRoot == null)
            {
                return;
            }

            ClearChildren(optionListRoot);
            foreach (var option in options)
            {
                var current = option;
                var illust = ResolveOptionSprite(current.Id);
                if (illust != null)
                {
                    CreateOptionCard(optionListRoot, current, illust);
                    continue;
                }

                // 일러가 없는 선택지(신규 id·빌드 미저작)는 종전 텍스트 플레이트로 폴백 —
                // 화면이 죽는 것보다 낡은 모습이 낫다.
                var detail = current.Enabled ? current.Detail : current.DisabledDetail;
                var label = string.IsNullOrEmpty(detail)
                    ? current.Title
                    : $"{current.Title}\n<size=70%><color=#B8BCD0>{detail}</color></size>";
                var button = CreateListButton(optionListRoot, label, 28f, () => OnOptionClicked(current));
                var element = button.GetComponent<LayoutElement>();
                element.preferredHeight = 96f;
                element.preferredWidth = 540f;
                button.interactable = current.Enabled;
            }
        }

        /// <summary>
        /// 선택지 설명 줄 앞에 세울 작은 아이콘(2026-09-01 #11). 일러 해소(<see cref="ResolveOptionSprite"/>)와
        /// <b>같은 문법</b>이다 — 모델에 스프라이트를 실어 나르면 규칙 쪽이 그림을 들게 된다.
        /// </summary>
        private Sprite ResolveDetailIcon(string optionId)
        {
            return string.Equals(optionId, "heal", StringComparison.Ordinal) ? HealthIconSprite : null;
        }

        /// <summary>
        /// 설명. <b>줄바꿈(<c>\n</c>)이 있으면 줄마다 따로 세운다</b>(2026-09-01 #11 사용자 지정) —
        /// 「체력 54/80」과 「최대 체력의 30%를 회복합니다 (+24)」는 다른 종류의 말이라
        /// 한 문단으로 흘리면 폭에 밀려 아무 데서나 접힌다. 줄바꿈을 <b>뜻</b>으로 정해 두면
        /// 「(+24)」만 홀로 남는 사고가 구조적으로 안 난다.
        ///
        /// <para>🔑 아이콘은 <b>첫 줄에만</b> 붙는다 — 하트가 말하는 것은 「체력」 한 줄이지 설명 전체가 아니다.</para>
        /// </summary>
        private void CreateDetailLine(RectTransform parent, string detail, Sprite icon)
        {
            var lines = detail.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                CreateSingleDetailLine(parent, line, i == 0 ? icon : null);
            }
        }

        private void CreateSingleDetailLine(RectTransform parent, string detail, Sprite icon)
        {
            if (icon == null)
            {
                CreateText(parent, detail, 15f, MutedColor);
                return;
            }

            var rowObject = new GameObject("Detail Row", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var row = (RectTransform)rowObject.transform;
            row.SetParent(parent, worldPositionStays: false);
            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            var iconObject = new GameObject("Detail Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(row, worldPositionStays: false);
            var iconElement = iconObject.GetComponent<LayoutElement>();
            iconElement.preferredWidth = 18f;
            iconElement.preferredHeight = 18f;
            var iconImage = iconObject.GetComponent<Image>();
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;

            CreateText(row, detail, 15f, MutedColor);
        }

        private Sprite ResolveOptionSprite(string optionId)
        {
            switch (optionId)
            {
                case "heal":
                    return HealOptionSprite;
                case "refine":
                    return RefineOptionSprite;
                case "remove":
                    return RemoveOptionSprite;
                default:
                    return null;
            }
        }

        /// <summary>
        /// ⑩ Q-E A안: 미니 일러 카드 버튼 — 일러(340×240)가 면적 전부, 아래 라벨 밴드.
        /// 테두리·밴드는 코드가 그려 3종 세트의 프레임이 항상 같다(생성물에는 테두리 금지).
        /// </summary>
        private Button CreateOptionCard(RectTransform parent, ServiceObjectOptionModel option, Sprite illust)
        {
            var buttonObject = new GameObject(
                "Service Button", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(LayoutElement), typeof(VerticalLayoutGroup));
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, worldPositionStays: false);
            buttonObject.GetComponent<LayoutElement>().preferredWidth = 340f;

            var layout = buttonObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 10);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var plate = buttonObject.GetComponent<Image>();
            UiProceduralPanel.Attach(
                plate,
                theme != null ? theme.PopupFillColor : PanelColor,
                theme != null ? theme.PopupBorderColor : AccentColor,
                2f,
                12f);

            var illustObject = new GameObject("Option Illust", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var illustRect = (RectTransform)illustObject.transform;
            illustRect.SetParent(rect, worldPositionStays: false);
            var illustElement = illustObject.GetComponent<LayoutElement>();
            illustElement.preferredWidth = 324f;
            illustElement.preferredHeight = 229f;
            var illustImage = illustObject.GetComponent<Image>();
            illustImage.sprite = illust;
            illustImage.preserveAspect = true;
            illustImage.raycastTarget = false;
            if (!option.Enabled)
            {
                illustImage.color = new Color(0.42f, 0.44f, 0.52f, 1f);
            }

            var title = CreateText(rect, option.Title, 27f, TextColor, bold: true);
            KoreanFontProvider.ApplyTitle(title);
            var detail = option.Enabled ? option.Detail : option.DisabledDetail;
            if (!string.IsNullOrEmpty(detail))
            {
                CreateDetailLine(rect, detail, ResolveDetailIcon(option.Id));
            }

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = plate;
            button.interactable = option.Enabled;
            button.onClick.AddListener(() => OnOptionClicked(option));
            return button;
        }

        private void OnOptionClicked(ServiceObjectOptionModel option)
        {
            if (option == null || !option.Enabled)
            {
                return;
            }

            switch (option.Flow)
            {
                case ServiceOptionFlow.Instant:
                    onInstantRequested?.Invoke(option);
                    return;
                case ServiceOptionFlow.CardPick:
                case ServiceOptionFlow.CardPickCompare:
                    ShowPickPanel(option);
                    return;
            }
        }

        private void ShowPickPanel(ServiceObjectOptionModel option)
        {
            EnsureLayout();
            if (pickPanel == null || option == null)
            {
                return;
            }

            activeOption = option;
            pickPanel.gameObject.SetActive(true);
            pickPanel.SetAsLastSibling();
            if (pickTitleText != null)
            {
                pickTitleText.text = option.Title;
            }

            if (pickSubtitleText != null)
            {
                pickSubtitleText.text = option.Detail;
            }

            ClearChildren(pickContent);
            var candidates = cardCandidatesProvider?.Invoke(option) ?? Array.Empty<ServiceCardCandidate>();
            foreach (var candidate in candidates)
            {
                CreatePickTile(candidate);
            }

            if (candidates.Count == 0)
            {
                CreateText(pickContent, "대상 카드가 없습니다.", 24f, MutedColor);
            }
        }

        /// <summary>
        /// 고르는 격자의 한 칸 — 실물 카드 + 어느 더미에 있는지. 누르면 곧바로 확인 화면으로 간다
        /// (잡화점 제거와 달리 <b>중간 선택 상태가 없다</b>: 무르는 자리는 확인 화면의 「돌아가기」다).
        /// </summary>
        private void CreatePickTile(ServiceCardCandidate candidate)
        {
            var tileObject = new GameObject(
                "Service Pick Tile", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var tile = (RectTransform)tileObject.transform;
            tile.SetParent(pickContent, worldPositionStays: false);
            var layout = tileObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 6);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var plate = tileObject.GetComponent<Image>();
            UiProceduralPanel.Attach(plate, new Color(0f, 0f, 0f, 0.28f), new Color(0f, 0f, 0f, 0f), 2f, 8f);

            var snapshot = candidate.Snapshot;
            var drewCard = false;
            if (snapshot.HasValue && MoveCardFrontPrefab != null && ActionCardFrontPrefab != null)
            {
                var holderObject = new GameObject("Card Holder", typeof(RectTransform), typeof(LayoutElement));
                var holder = (RectTransform)holderObject.transform;
                holder.SetParent(tile, worldPositionStays: false);
                var holderElement = holderObject.GetComponent<LayoutElement>();
                // 라벨이 빠졌으므로 세로도 폭과 같은 여백만 남긴다(종전 62는 라벨 자리였다).
                holderElement.preferredWidth = PickCellSize.x - 20f;
                holderElement.preferredHeight = PickCellSize.y - 20f;

                var prefab = CardFrontListInstance.ChoosePrefab(snapshot.Value.Kind, MoveCardFrontPrefab, ActionCardFrontPrefab);
                var cardView = CardFrontListInstance.Create(prefab, holder, snapshot.Value, StatusCardFrameSprite);
                if (cardView != null)
                {
                    var cardRect = (RectTransform)cardView.transform;
                    cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                    cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                    cardRect.pivot = new Vector2(0.5f, 0.5f);
                    cardRect.anchoredPosition = Vector2.zero;
                    var cardSize = cardRect.rect.size;
                    if (cardSize.x > 1f && cardSize.y > 1f)
                    {
                        var fit = Mathf.Min(1f, holderElement.preferredWidth / cardSize.x, holderElement.preferredHeight / cardSize.y);
                        cardRect.localScale = Vector3.one * fit;
                    }

                    drewCard = true;
                }
            }

            // 🔑 <b>카드를 그렸으면 글자를 붙이지 않는다</b>(2026-09-02 #7 · 사용자 확정 — 잡화점과 같은 규칙).
            //    스냅샷이나 프리팹이 없을 때만 종전 텍스트 행으로 물러난다 — 화면이 죽지 않는 것이 우선.
            if (!drewCard)
            {
                CreateText(tile, candidate.Label, 22f, MutedColor).alignment = TextAlignmentOptions.Center;
            }

            var pick = candidate;
            var button = tileObject.GetComponent<Button>();
            button.targetGraphic = plate;
            button.onClick.AddListener(() => OnCandidateClicked(pick));
        }

        private void OnCandidateClicked(ServiceCardCandidate candidate)
        {
            if (activeOption == null)
            {
                return;
            }

            if (activeOption.Flow == ServiceOptionFlow.CardPickCompare)
            {
                ShowComparePanel(candidate);
                return;
            }

            if (onCardConfirmed != null && onCardConfirmed(activeOption, candidate))
            {
                HidePickPanel();
            }
        }

        /// <summary>비교 화면의 두 표제. 「연마 후」는 색 판정에도 쓰이므로 상수로 둔다(#9).</summary>
        private const string CurrentCaption = "현재";
        private const string RefinedCaption = "연마 후";

        private void ShowComparePanel(ServiceCardCandidate candidate)
        {
            EnsureLayout();
            if (comparePanel == null || activeOption == null || comparePairProvider == null)
            {
                return;
            }

            var pair = comparePairProvider(activeOption, candidate);
            if (!pair.IsValid)
            {
                SetMessage("카드를 미리 볼 수 없습니다.");
                return;
            }

            pendingCandidate = candidate;
            hasPendingCandidate = true;
            HidePickPanel();
            comparePanel.gameObject.SetActive(true);
            comparePanel.SetAsLastSibling();

            ClearChildren(compareCardsRoot);
            CreateCompareCard(pair.Before, CurrentCaption);
            CreateCompareArrow();
            CreateCompareCard(pair.After, RefinedCaption);
            if (compareDiffText != null)
            {
                compareDiffText.text = BuildDiffSummary(pair);
            }
        }

        private void OnCompareConfirmed()
        {
            if (activeOption == null || !hasPendingCandidate)
            {
                return;
            }

            // 🔴 <b>날아갈 카드의 자리를 미리 잡아 둔다.</b> 확정 콜백은 연마를 집행한 뒤 서비스 화면을
            //    통째로 닫으므로(CloseServiceObjectAndConsume), 그 뒤에는 읽을 위치도 복제할 카드도 없다.
            var refinedCard = FindRefinedCompareCard();
            var flightOrigin = refinedCard != null ? refinedCard.position : (Vector3?)null;

            if (onCardConfirmed != null && onCardConfirmed(activeOption, pendingCandidate))
            {
                hasPendingCandidate = false;
                PlayRefineCompleteFlight(refinedCard, flightOrigin);
                HideComparePanel();
            }
        }

        /// <summary>비교 화면의 <b>오른쪽</b>(연마 후) 카드. 없으면 null.</summary>
        private RectTransform FindRefinedCompareCard()
        {
            if (compareCardsRoot == null || compareCardsRoot.childCount == 0)
            {
                return null;
            }

            // 자식 차례: [현재][화살표][연마 후] — 마지막이 결과물이다.
            return compareCardsRoot.GetChild(compareCardsRoot.childCount - 1) as RectTransform;
        }

        /// <summary>
        /// 연마 완료 연출(2026-09-02 #8): 연마된 카드가 <b>덱 쪽으로 날아간다</b>.
        ///
        /// <para>🔑 <b>연출 오브젝트가 자기 수명을 쥔다</b> — cs:1285에서 전리품 회수 연출을
        /// <c>LootFlyAnimator</c>로 바꾸며 세운 문법을 그대로 쓴다. 이 화면은 확정 직후 통째로 닫히므로
        /// 연출을 팝업 <b>안</b>에 두면 첫 프레임에 함께 사라진다(코루틴 호스트와 오브젝트 수명을
        /// 나누면 반대로 유령이 남는다 — 둘 다 같은 오브젝트에 둔다).</para>
        ///
        /// <para>소리는 여기서 내지 않는다 — 규칙층 확정 시점에 <c>reward.card.acquire</c>가 이미
        /// 울린다(<c>MapCombatController.Services</c>의 "service:refine"). 연출이 자기 소리를 또 내면
        /// 한 사건이 두 번 들린다.</para>
        ///
        /// <para>목적지는 <see cref="excludedSidebar"/>다 — 연마한 카드가 「내 덱으로 돌아간다」는 뜻이고,
        /// 이 화면이 이미 비워 두고 있는 자리라 새 배선이 필요 없다. 사이드바를 못 찾으면 제자리에서
        /// 잦아든다(연출이 없느니만 못한 자리로 날아가지 않는다).</para>
        /// </summary>
        private void PlayRefineCompleteFlight(RectTransform refinedCard, Vector3? origin)
        {
            if (refinedCard == null || !origin.HasValue)
            {
                return;
            }

            // 팝업 밖(오버레이 루트의 부모)에 세운다 — 팝업이 닫혀도 연출은 살아 있어야 한다.
            var host = transform.parent as RectTransform ?? (RectTransform)transform;
            var flyObject = Instantiate(refinedCard.gameObject, host, worldPositionStays: true);
            flyObject.name = "Refine Flight";

            // 복제본은 그림일 뿐이다 — 레이아웃·입력·판정이 붙으면 뒤에 남아 화면을 잡는다.
            foreach (var behaviour in flyObject.GetComponentsInChildren<Behaviour>(includeInactive: true))
            {
                if (behaviour is LayoutGroup || behaviour is ContentSizeFitter || behaviour is LayoutElement)
                {
                    behaviour.enabled = false;
                }
            }

            foreach (var graphic in flyObject.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                graphic.raycastTarget = false;
            }

            var flyRect = (RectTransform)flyObject.transform;
            flyRect.position = origin.Value;
            flyRect.SetAsLastSibling();

            var animator = flyObject.AddComponent<RefineFlightAnimator>();
            animator.Begin(excludedSidebar);
        }

        private void OnCompareBack()
        {
            hasPendingCandidate = false;
            HideComparePanel();
            ShowPickPanel(activeOption);
        }

        private void HidePickPanel()
        {
            if (pickPanel != null)
            {
                pickPanel.gameObject.SetActive(false);
            }
        }

        private void HideComparePanel()
        {
            if (comparePanel != null)
            {
                comparePanel.gameObject.SetActive(false);
            }
        }

        // ── 비교 화면 ────────────────────────────────────────────────────────────────

        private void CreateCompareCard(CombatCardSnapshot snapshot, string caption)
        {
            var container = new GameObject("Compare Card", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var rect = (RectTransform)container.transform;
            rect.SetParent(compareCardsRoot, worldPositionStays: false);
            var layout = container.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            var element = container.GetComponent<LayoutElement>();
            element.preferredWidth = 330f;
            element.preferredHeight = 520f;

            // 「연마 후」 쪽 표제도 초록으로 세운다 — 어느 장이 결과인지가 색으로 먼저 읽힌다(#9).
            var captionText = CreateText(
                rect, caption, 24f,
                string.Equals(caption, RefinedCaption, System.StringComparison.Ordinal) ? ImprovedColor : MutedColor,
                bold: true);
            ((RectTransform)captionText.transform).sizeDelta = new Vector2(320f, 34f);

            var cardHolder = new GameObject("Card Holder", typeof(RectTransform));
            var holderRect = (RectTransform)cardHolder.transform;
            holderRect.SetParent(rect, worldPositionStays: false);
            holderRect.sizeDelta = new Vector2(320f, 470f);

            var prefab = CardFrontListInstance.ChoosePrefab(snapshot.Kind, MoveCardFrontPrefab, ActionCardFrontPrefab);
            var view = CardFrontListInstance.Create(prefab, holderRect, snapshot, StatusCardFrameSprite);
            if (view != null)
            {
                var cardRect = (RectTransform)view.transform;
                cardRect.anchorMin = new Vector2(0.5f, 0.5f);
                cardRect.anchorMax = new Vector2(0.5f, 0.5f);
                cardRect.pivot = new Vector2(0.5f, 0.5f);
                cardRect.anchoredPosition = Vector2.zero;
                // 프레임 원본 크기가 홀더보다 크면 맞춰 줄인다(현행 CardFront 200×320은 1.0 유지).
                var cardSize = cardRect.rect.size;
                if (cardSize.x > 1f && cardSize.y > 1f)
                {
                    var fit = Mathf.Min(1f, holderRect.sizeDelta.x / cardSize.x, holderRect.sizeDelta.y / cardSize.y);
                    cardRect.localScale = Vector3.one * fit;
                }
            }
            else
            {
                CreateText(holderRect, snapshot.Name, 26f, TextColor, bold: true);
            }
        }

        private void CreateCompareArrow()
        {
            // 화살표도 초록이다 — 「나아지는 방향」을 가리키는 것이라 달라진 수치와 같은 어휘여야 한다(#9).
            var arrow = CreateText(compareCardsRoot, "→", 44f, ImprovedColor, bold: true);
            var rect = (RectTransform)arrow.transform;
            rect.sizeDelta = new Vector2(60f, 60f);
            var element = arrow.gameObject.AddComponent<LayoutElement>();
            element.preferredWidth = 60f;
        }

        /// <summary>D-6: 변경 수치는 <b>초록</b>(2026-09-02 #9), 전 값은 흐리게. 축 비교와 문안 구절 diff는
        /// <see cref="RefineDiffSummary"/>(순수)가 만들고 뷰는 색만 넘긴다 — 효과 연마 카드도 「무엇이 달라졌나」가 보인다.</summary>
        private string BuildDiffSummary(ServiceCardComparePair pair)
        {
            return RefineDiffSummary.Build(
                pair.Before, pair.After, pair.BeforeDefinition, pair.AfterDefinition,
                ColorUtility.ToHtmlStringRGB(ImprovedColor), ColorUtility.ToHtmlStringRGB(MutedColor));
        }

        // 🔴 이 뷰는 런타임 생성(FindOrCreate/AddComponent)이라 SerializeField가 채워질 표면이
        // 없다 — 빌드 안전 정본은 RuntimeUiAssetCatalog(Resources)다(2026-08-19 #16: 예전
        // AssetDatabase-전용 폴백은 빌드에서 전부 null이라 배경·일러가 통째로 사라졌다).
        // AssetDatabase는 카탈로그 미저작 시의 에디터 최후 폴백으로만 남긴다.
        private GameObject MoveCardFrontPrefab
        {
            get
            {
                if (moveCardFrontPrefab != null) return moveCardFrontPrefab;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.MoveCardFrontPrefab != null) return moveCardFrontPrefab = catalog.MoveCardFrontPrefab;
#if UNITY_EDITOR
                return moveCardFrontPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Cards/CardFront_Move.prefab");
#else
                return null;
#endif
            }
        }

        private GameObject ActionCardFrontPrefab
        {
            get
            {
                if (actionCardFrontPrefab != null) return actionCardFrontPrefab;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.ActionCardFrontPrefab != null) return actionCardFrontPrefab = catalog.ActionCardFrontPrefab;
#if UNITY_EDITOR
                return actionCardFrontPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Cards/CardFront_Action.prefab");
#else
                return null;
#endif
            }
        }

        private Sprite StatusCardFrameSprite
        {
            get
            {
                if (statusCardFrameSprite != null) return statusCardFrameSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.StatusCardFrameSprite != null) return statusCardFrameSprite = catalog.StatusCardFrameSprite;
#if UNITY_EDITOR
                return statusCardFrameSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Cards/card_frame_status.png");
#else
                return null;
#endif
            }
        }

        private Sprite HealOptionSprite
        {
            get
            {
                if (healOptionSprite != null) return healOptionSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.HealOptionSprite != null) return healOptionSprite = catalog.HealOptionSprite;
#if UNITY_EDITOR
                return healOptionSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/ServiceIcons/service_icon_heal.png");
#else
                return null;
#endif
            }
        }

        /// <summary>
        /// 체력 하트 — <b>하단 HUD 체력 칸이 쓰는 그 그림</b>이다(2026-09-01 #11 사용자 지정).
        /// 같은 그림을 써야 「체력」이 화면마다 다른 물건으로 읽히지 않는다.
        /// 정본은 <c>RuntimeUiAssetCatalog</c>다 — <c>AssetDatabase</c> 폴백은 에디터에서만 살아 있어
        /// 빌드에서 조용히 null이 된다(2026-08-19 #C의 전례).
        /// </summary>
        private Sprite HealthIconSprite
        {
            get
            {
                if (healthIconSprite != null) return healthIconSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.HealthIconSprite != null) return healthIconSprite = catalog.HealthIconSprite;
#if UNITY_EDITOR
                return healthIconSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Icons/ui_health.png");
#else
                return null;
#endif
            }
        }

        private Sprite RefineOptionSprite
        {
            get
            {
                if (refineOptionSprite != null) return refineOptionSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.RefineOptionSprite != null) return refineOptionSprite = catalog.RefineOptionSprite;
#if UNITY_EDITOR
                return refineOptionSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/ServiceIcons/service_icon_refine.png");
#else
                return null;
#endif
            }
        }

        private Sprite RemoveOptionSprite
        {
            get
            {
                if (removeOptionSprite != null) return removeOptionSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.RemoveOptionSprite != null) return removeOptionSprite = catalog.RemoveOptionSprite;
#if UNITY_EDITOR
                return removeOptionSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/ServiceIcons/service_icon_remove.png");
#else
                return null;
#endif
            }
        }

        private Sprite CamperBackdropSprite
        {
            get
            {
                if (camperBackdropSprite != null) return camperBackdropSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.CamperBackdropSprite != null) return camperBackdropSprite = catalog.CamperBackdropSprite;
#if UNITY_EDITOR
                return camperBackdropSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Backdrops/service_backdrop_camper_van.png");
#else
                return null;
#endif
            }
        }

        private Sprite WorkshopBackdropSprite
        {
            get
            {
                if (workshopBackdropSprite != null) return workshopBackdropSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.WorkshopBackdropSprite != null) return workshopBackdropSprite = catalog.WorkshopBackdropSprite;
#if UNITY_EDITOR
                return workshopBackdropSprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Backdrops/service_backdrop_workshop.png");
#else
                return null;
#endif
            }
        }

        // ── 레이아웃 생성 (풀스크린) ──────────────────────────────────────────────────

        private void EnsureLayout()
        {
            var rect = (RectTransform)transform;
            if (layoutBuilt)
            {
                ApplySidebarExclusionLayout(rect);
                return;
            }

            layoutBuilt = true;
            ApplySidebarExclusionLayout(rect);

            // 배경이 못 깔린 경우(빌드에서 참조 미저작 등)에도 화면이 뚫리지 않는 바닥색.
            var baseImage = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            baseImage.color = DefaultBaseColor;
            baseImage.raycastTarget = true;

            // 배경 일러스트: 마스크 안에서 16:9 원본비를 유지한 채 영역을 덮는다(EnvelopeParent).
            // 사이드바를 제외한 영역은 16:9보다 좁으므로 넘치는 폭은 마스크가 자른다.
            var maskObject = new GameObject("Service Backdrop Mask", typeof(RectTransform), typeof(RectMask2D));
            var maskRect = (RectTransform)maskObject.transform;
            maskRect.SetParent(rect, worldPositionStays: false);
            Stretch(maskRect);

            var backdropObject = new GameObject("Service Backdrop", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            var backdropRect = (RectTransform)backdropObject.transform;
            backdropRect.SetParent(maskRect, worldPositionStays: false);
            Stretch(backdropRect);
            backdropImage = backdropObject.GetComponent<Image>();
            backdropImage.raycastTarget = false;
            backdropImage.preserveAspect = false;
            backdropFitter = backdropObject.GetComponent<AspectRatioFitter>();
            backdropFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            backdropFitter.aspectRatio = BackdropAspect;

            titleText = CreateText(rect, string.Empty, 46f, TextColor, bold: true);
            KoreanFontProvider.ApplyTitle(titleText);
            var titleRect = (RectTransform)titleText.transform;
            titleRect.anchorMin = new Vector2(0.5f, 1f);
            titleRect.anchorMax = new Vector2(0.5f, 1f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -34f);
            titleRect.sizeDelta = new Vector2(900f, 60f);

            subtitleText = CreateText(rect, string.Empty, 22f, MutedColor);
            var subtitleRect = (RectTransform)subtitleText.transform;
            subtitleRect.anchorMin = new Vector2(0.5f, 1f);
            subtitleRect.anchorMax = new Vector2(0.5f, 1f);
            subtitleRect.pivot = new Vector2(0.5f, 1f);
            subtitleRect.anchoredPosition = new Vector2(0f, -98f);
            subtitleRect.sizeDelta = new Vector2(900f, 32f);

            // 선택지 컨테이너 — 스크린별 quiet zone에 앉는다(ApplyScreen이 앵커를 정한다).
            // ⑩ Q-E: 일러 카드 2택이 가로로 놓인다(StS 모닥불 문법). 크기는 카드가 정하고
            // 컨테이너는 내용에 맞춰 줄어든다.
            var optionListObject = new GameObject("Service Option List", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            optionListRoot = (RectTransform)optionListObject.transform;
            optionListRoot.SetParent(rect, worldPositionStays: false);
            optionListRoot.sizeDelta = Vector2.zero;
            var optionLayout = optionListObject.GetComponent<HorizontalLayoutGroup>();
            optionLayout.spacing = 26f;
            optionLayout.childAlignment = TextAnchor.MiddleCenter;
            optionLayout.childControlWidth = true;
            optionLayout.childControlHeight = true;
            optionLayout.childForceExpandWidth = false;
            optionLayout.childForceExpandHeight = false;
            var optionFitter = optionListObject.GetComponent<ContentSizeFitter>();
            optionFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            optionFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            messageText = CreateText(rect, string.Empty, 22f, MutedColor);
            var messageRect = (RectTransform)messageText.transform;
            messageRect.anchorMin = new Vector2(0.5f, 0f);
            messageRect.anchorMax = new Vector2(0.5f, 0f);
            messageRect.pivot = new Vector2(0.5f, 0f);
            messageRect.anchoredPosition = new Vector2(0f, 108f);
            messageRect.sizeDelta = new Vector2(900f, 30f);

            var leaveDock = new GameObject("Service Leave Dock", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var leaveRect = (RectTransform)leaveDock.transform;
            leaveRect.SetParent(rect, worldPositionStays: false);
            leaveRect.anchorMin = new Vector2(0.5f, 0f);
            leaveRect.anchorMax = new Vector2(0.5f, 0f);
            leaveRect.pivot = new Vector2(0.5f, 0f);
            leaveRect.anchoredPosition = new Vector2(0f, 30f);
            leaveRect.sizeDelta = new Vector2(620f, 64f);
            var leaveLayout = leaveDock.GetComponent<VerticalLayoutGroup>();
            leaveLayout.childControlWidth = true;
            leaveLayout.childControlHeight = true;
            leaveLayout.childForceExpandWidth = true;
            leaveLayout.childForceExpandHeight = true;
            CreateListButton(leaveRect, "떠나기 (다시 이용할 수 없습니다)", 26f, () => onLeaveRequested?.Invoke());

            BuildPickPanel(rect);
            BuildComparePanel(rect);
        }

        /// <summary>스크린별 배경·존 배치(스타일 보드 채택본의 quiet zone 실측 기준).</summary>
        private void ApplyScreen(ServiceObjectScreen screen)
        {
            var sprite = screen == ServiceObjectScreen.CamperVan ? CamperBackdropSprite : WorkshopBackdropSprite;
            if (backdropImage != null)
            {
                backdropImage.sprite = sprite;
                backdropImage.enabled = sprite != null;
            }

            if (backdropFitter != null)
            {
                // 캠핑카는 좌측의 밴이 그림의 정체성이라 왼쪽을 보존하고 오른쪽 잔디를 자른다.
                // 공작소는 대칭이라 중앙 유지.
                var backdropRect = (RectTransform)backdropImage.transform;
                var pivotX = screen == ServiceObjectScreen.CamperVan ? 0f : 0.5f;
                backdropRect.anchorMin = new Vector2(pivotX, 0.5f);
                backdropRect.anchorMax = new Vector2(pivotX, 0.5f);
                backdropRect.pivot = new Vector2(pivotX, 0.5f);
                backdropRect.anchoredPosition = Vector2.zero;
            }

            if (optionListRoot != null)
            {
                // 캠핑카 quiet zone = 잔디 무대 중우측 / 공작소 = 빈 벽 중앙.
                // 🔑 캠핑카의 세로는 <b>밴과 나란히</b>가 기준이다(2026-08-31 사용자 확정) — 잔디에
                //    내려앉아 있던 0.37을 밴 몸통 높이로 올렸다. 좌우(0.66)는 밴을 가리지 않는 값이라 유지.
                var anchor = screen == ServiceObjectScreen.CamperVan
                    ? new Vector2(0.66f, 0.50f)
                    : new Vector2(0.5f, 0.52f);
                optionListRoot.anchorMin = anchor;
                optionListRoot.anchorMax = anchor;
                optionListRoot.pivot = new Vector2(0.5f, 0.5f);
                optionListRoot.anchoredPosition = Vector2.zero;
            }

            // 공작소 배경은 최상단이 시안 형광등 밴드라 타이틀이 씻긴다 — 등 아래로 내린다.
            if (titleText != null)
            {
                ((RectTransform)titleText.transform).anchoredPosition =
                    screen == ServiceObjectScreen.Workshop ? new Vector2(0f, -118f) : new Vector2(0f, -34f);
            }

            if (subtitleText != null)
            {
                ((RectTransform)subtitleText.transform).anchoredPosition =
                    screen == ServiceObjectScreen.Workshop ? new Vector2(0f, -182f) : new Vector2(0f, -98f);
                // 공작소 벽은 밝은 하늘색이라 회백 부제가 씻긴다 — 잉크색으로 뒤집는다.
                subtitleText.color = screen == ServiceObjectScreen.Workshop
                    ? new Color32(0x2A, 0x30, 0x55, 0xFF)
                    : MutedColor;
            }
        }

        private void ApplySidebarExclusionLayout(RectTransform root)
        {
            ResolveExcludedSidebarIfMissing();
            var sidebarWidth = excludedSidebar != null
                ? Mathf.Max(excludedSidebar.rect.width, excludedSidebar.sizeDelta.x)
                : 0f;
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.one;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.offsetMin = new Vector2(sidebarWidth + sidebarGap, 0f);
            root.offsetMax = Vector2.zero;
        }

        private void ResolveExcludedSidebarIfMissing()
        {
            if (excludedSidebar != null)
            {
                return;
            }

            // 덱 목록과 같은 계약 — 이름이 아니라 SidebarRootMarker로 찾는다.
            var marker = FindFirstObjectByType<SidebarRootMarker>(FindObjectsInactive.Include);
            excludedSidebar = marker != null ? marker.transform as RectTransform : null;
        }

        /// <summary>
        /// 카드를 고르는 화면. 🔑 <b>덱 목록·잡화점 카드 제거와 같은 문법</b>이다(사용자 확정
        /// 2026-08-31): 이름만 적힌 행이 아니라 <b>실물 카드가 격자로 서고</b>, 한 장을 누르면
        /// 곧바로 연마 확인으로 넘어간다. 고르는 일이 「목록에서 이름 찾기」가 아니라
        /// 「카드를 보고 고르기」가 되어야 값의 변화(확인 화면)와 한 흐름으로 읽힌다.
        /// </summary>
        private void BuildPickPanel(RectTransform rootRect)
        {
            // 실물 카드 5열이 서려면 종전 680폭으로는 부족하다(잡화점 제거 화면과 같은 규격).
            pickPanel = CreatePanelRect("Service Card Pick Panel", rootRect, new Vector2(1320f, 940f));
            var image = pickPanel.gameObject.AddComponent<Image>();
            image.color = PanelColor;
            UiProceduralPanel.Attach(
                image,
                theme != null ? theme.PopupFillColor : PanelColor,
                theme != null ? theme.PopupBorderColor : AccentColor,
                theme != null ? theme.PopupBorderThickness : 2f,
                theme != null ? theme.PopupCornerRadius : 16f);

            var layout = pickPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            UiPanelChrome.Attach(pickPanel, theme != null ? theme.PopupBorderColor : AccentColor);

            pickTitleText = CreateText(pickPanel, string.Empty, 32f, TextColor, bold: true);
            KoreanFontProvider.ApplyTitle(pickTitleText);
            pickSubtitleText = CreateText(pickPanel, string.Empty, 20f, MutedColor);

            var scrollObject = new GameObject("Service Pick Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            var scrollRect = (RectTransform)scrollObject.transform;
            scrollRect.SetParent(pickPanel, worldPositionStays: false);
            scrollObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.24f);
            scrollObject.GetComponent<LayoutElement>().flexibleHeight = 1f;

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(scrollRect, worldPositionStays: false);
            Stretch(viewport);
            viewportObject.GetComponent<Image>().color = Color.white;
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            pickContent = (RectTransform)contentObject.transform;
            pickContent.SetParent(viewport, worldPositionStays: false);
            pickContent.anchorMin = new Vector2(0f, 1f);
            pickContent.anchorMax = new Vector2(1f, 1f);
            pickContent.pivot = new Vector2(0.5f, 1f);
            pickContent.offsetMin = Vector2.zero;
            pickContent.offsetMax = Vector2.zero;
            var contentLayout = contentObject.GetComponent<GridLayoutGroup>();
            contentLayout.padding = new RectOffset(14, 14, 14, 14);
            contentLayout.cellSize = PickCellSize;
            contentLayout.spacing = new Vector2(16f, 16f);
            contentLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            contentLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            contentLayout.constraintCount = PickColumnCount;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = pickContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            CreateListButton(pickPanel, "돌아가기", 24f, HidePickPanel);
            pickPanel.gameObject.SetActive(false);
        }

        private void BuildComparePanel(RectTransform rootRect)
        {
            // 규격은 제거 확인과 공유한다(CardConfirmPanelSpec) — 폭만 내용이 정한다(카드 두 장 + 화살표).
            comparePanel = CreatePanelRect(
                "Service Compare Panel", rootRect, new Vector2(900f, CardConfirmPanelSpec.PanelHeight));
            var image = comparePanel.gameObject.AddComponent<Image>();
            image.color = PanelColor;
            UiProceduralPanel.Attach(
                image,
                theme != null ? theme.PopupFillColor : PanelColor,
                theme != null ? theme.PopupBorderColor : AccentColor,
                theme != null ? theme.PopupBorderThickness : 2f,
                theme != null ? theme.PopupCornerRadius : 16f);

            var layout = comparePanel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = CardConfirmPanelSpec.Padding;
            layout.spacing = CardConfirmPanelSpec.RowSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            UiPanelChrome.Attach(comparePanel, theme != null ? theme.PopupBorderColor : AccentColor);

            var compareTitle = CreateText(comparePanel, "연마 확인", CardConfirmPanelSpec.TitleFontSize, TextColor, bold: true);
            KoreanFontProvider.ApplyTitle(compareTitle);

            var cardsObject = new GameObject("Compare Cards", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            compareCardsRoot = (RectTransform)cardsObject.transform;
            compareCardsRoot.SetParent(comparePanel, worldPositionStays: false);
            var cardsLayout = cardsObject.GetComponent<HorizontalLayoutGroup>();
            cardsLayout.spacing = 12f;
            cardsLayout.childAlignment = TextAnchor.MiddleCenter;
            // 컨테이너 크기는 LayoutElement preferred가 정한다 — childControl이 꺼져 있으면
            // sizeDelta 0짜리 컨테이너들이 전부 중앙에 겹쳐 쌓인다(실측 결함, 2026-08-18).
            cardsLayout.childControlWidth = true;
            cardsLayout.childControlHeight = true;
            cardsLayout.childForceExpandWidth = false;
            cardsLayout.childForceExpandHeight = false;
            cardsObject.GetComponent<LayoutElement>().flexibleHeight = 1f;

            compareDiffText = CreateText(comparePanel, string.Empty, CardConfirmPanelSpec.CaptionFontSize, TextColor);

            var buttonsObject = new GameObject("Compare Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var buttonsRect = (RectTransform)buttonsObject.transform;
            buttonsRect.SetParent(comparePanel, worldPositionStays: false);
            var buttonsLayout = buttonsObject.GetComponent<HorizontalLayoutGroup>();
            buttonsLayout.spacing = CardConfirmPanelSpec.ButtonSpacing;
            buttonsLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            // 🔑 늘리지 않는다 — 늘리면 버튼 폭이 <b>패널 폭을 따라가</b> 제거 확인과 갈린다.
            buttonsLayout.childForceExpandWidth = false;
            buttonsLayout.childForceExpandHeight = false;

            SizeConfirmButton(CreateListButton(buttonsRect, "연마 확정", CardConfirmPanelSpec.ButtonFontSize, OnCompareConfirmed));
            SizeConfirmButton(CreateListButton(buttonsRect, "돌아가기", CardConfirmPanelSpec.ButtonFontSize, OnCompareBack));
            comparePanel.gameObject.SetActive(false);
        }

        /// <summary>확인 화면의 버튼을 공용 규격으로 못 박는다 — 두 화면이 같은 크기로 보이는 이유.</summary>
        private static void SizeConfirmButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            var element = button.GetComponent<LayoutElement>();
            if (element == null)
            {
                return;
            }

            element.preferredWidth = CardConfirmPanelSpec.ButtonWidth;
            element.preferredHeight = CardConfirmPanelSpec.ButtonHeight;
            element.flexibleWidth = 0f;
        }

        private Button CreateListButton(RectTransform parent, string label, float fontSize, Action onClick)
        {
            var buttonObject = new GameObject("Service Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            var rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, worldPositionStays: false);
            var text = CreateText(rect, label, fontSize, TextColor);
            Stretch((RectTransform)text.transform);
            text.alignment = TextAlignmentOptions.Center;
            text.margin = new Vector4(14f, 8f, 14f, 8f);
            var layoutElement = buttonObject.GetComponent<LayoutElement>();
            layoutElement.preferredHeight = string.IsNullOrEmpty(label) || !label.Contains("\n") ? 58f : 78f;
            var button = buttonObject.GetComponent<Button>();
            button.onClick.AddListener(() => onClick?.Invoke());
            UiButtonSkin.Apply(button, theme);
            return button;
        }

        private TMP_Text CreateText(RectTransform parent, string content, float fontSize, Color color, bool bold = false)
        {
            var textObject = new GameObject("Service Text", typeof(RectTransform));
            var rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, worldPositionStays: false);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = content;
            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.alignment = TextAlignmentOptions.Center;
            text.richText = true;
            text.raycastTarget = false;
            KoreanFontProvider.Apply(text);
            return text;
        }

        private static RectTransform CreatePanelRect(string name, RectTransform parent, Vector2 size)
        {
            var panelObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)panelObject.transform;
            rect.SetParent(parent, worldPositionStays: false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = size;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void ClearChildren(RectTransform parent)
        {
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    // EditMode 테스트가 패널 전환을 그대로 돌릴 수 있게 한다(Destroy는 에디트 모드 금지).
                    DestroyImmediate(child);
                }
            }
        }
    }

    /// <summary>
    /// 연마된 카드가 덱 쪽으로 날아가며 잦아드는 연출(2026-09-02 #8).
    ///
    /// <para>🔑 <b>자기 수명을 자기가 쥔다</b> — 코루틴 호스트와 움직이는 오브젝트가 같은
    /// GameObject라, 화면이 닫혀도 연출은 끝까지 가고 끝나면 스스로 사라진다.
    /// (전리품 회수의 <c>LootFlyAnimator</c>와 같은 문법이며, 값만 카드 크기에 맞게 다르다.)</para>
    ///
    /// <para>목적지가 없으면 <b>제자리에서</b> 커졌다 잦아든다 — 화면 밖이나 뜻 없는 자리로 날아가는
    /// 것보다 「무언가 완성됐다」만 말하는 편이 정직하다.</para>
    /// </summary>
    internal sealed class RefineFlightAnimator : MonoBehaviour
    {
        private const float DurationSeconds = 0.55f;

        /// <summary>날아가기 전 잠깐 부풀어 오르는 구간(전체의 앞 22%) — "완성됐다"의 박자.</summary>
        private const float SwellFraction = 0.22f;

        private RectTransform target;

        public void Begin(RectTransform flyTarget)
        {
            target = flyTarget;
            StartCoroutine(FlyRoutine());
        }

        private IEnumerator FlyRoutine()
        {
            var fly = (RectTransform)transform;
            // 🔴🔴 <c>GetComponent&lt;T&gt;() ?? AddComponent&lt;T&gt;()</c>를 쓰지 말 것 — Unity의 <b>가짜 null</b>은
            //    `??`를 통과하므로 없는 컴포넌트가 "있는 것"으로 반환되고, 다음 줄에서
            //    MissingComponentException이 난다(이 저장소가 이미 한 번 밟은 함정).
            var group = gameObject.GetComponent<CanvasGroup>();
            if (group == null)
            {
                group = gameObject.AddComponent<CanvasGroup>();
            }

            group.blocksRaycasts = false;
            group.interactable = false;

            var from = fly.position;
            var baseScale = fly.localScale;
            var to = target != null ? target.position : from;
            var elapsed = 0f;

            while (elapsed < DurationSeconds)
            {
                // 서비스 화면은 시간 배율을 건드릴 수 있으므로 unscaled로 센다(팝업 연출의 공통 규약).
                elapsed += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(elapsed / DurationSeconds);

                if (t < SwellFraction)
                {
                    var swell = t / SwellFraction;
                    fly.localScale = baseScale * Mathf.Lerp(1f, 1.12f, swell);
                    group.alpha = 1f;
                }
                else
                {
                    var travel = (t - SwellFraction) / (1f - SwellFraction);
                    var eased = 1f - ((1f - travel) * (1f - travel));
                    fly.position = Vector3.Lerp(from, to, eased);
                    fly.localScale = baseScale * Mathf.Lerp(1.12f, 0.28f, eased);
                    group.alpha = Mathf.Lerp(1f, 0f, travel * travel);
                }

                yield return null;
            }

            Destroy(gameObject);
        }
    }

}
