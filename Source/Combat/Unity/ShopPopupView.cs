using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>상점 진열대 한 칸의 표시 모델. 판매되면 <see cref="SoldOut"/>만 바뀐다.</summary>
    public sealed class ShopOfferSlotModel
    {
        public ShopOfferSlotModel(ShopItemKind kind, string itemId, string title, string detail, int price, string iconId = null)
        {
            Kind = kind;
            ItemId = itemId ?? string.Empty;
            Title = title ?? string.Empty;
            Detail = detail ?? string.Empty;
            Price = Mathf.Max(0, price);
            IconId = iconId ?? string.Empty;
        }

        public ShopItemKind Kind { get; }
        public string ItemId { get; }
        public string Title { get; }
        public string Detail { get; }
        /// <summary>
        /// 진열가. 🔴 <b>결제가와 같은 값이어야 한다</b> — 구매는 이 값을 그대로 결제에 넘긴다.
        /// 단골 도장을 그 자리에서 사면(N3) 컨트롤러가 남은 재고의 값을 여기에 다시 써 넣는다.
        /// 값을 바꾸는 길은 <see cref="SetPrice"/> 하나뿐이고, 그 출처는 언제나 재계산된 재고다.
        /// </summary>
        public int Price { get; private set; }

        /// <summary>
        /// 재계산된 재고의 값을 반영한다(N3). 뷰가 들고 있는 <b>같은 모델</b>을 고치므로
        /// 판매 완료 표시가 살아남고, 곧 이어지는 <c>RefreshSlots</c>가 새 값을 그린다.
        /// </summary>
        public void SetPrice(int price)
        {
            Price = Mathf.Max(0, price);
        }

        /// <summary>
        /// 유물·소모품 아이콘 키(<c>relic_icon_*</c>·<c>consumable_icon_*</c>). 비면 타일이 종전
        /// 복주머니 플레이스홀더로 떨어진다. 카드·카드 제거 칸은 늘 비어 있다(자기 프레임이 있다).
        /// </summary>
        public string IconId { get; }
        public bool SoldOut { get; set; }
    }

    /// <summary>카드 제거 목록의 한 행: 제거 API에 넘길 키(instance id)와 표시 라벨.</summary>
    public readonly struct ShopRemovalCandidate
    {
        public ShopRemovalCandidate(string key, string label, CombatCardSnapshot? snapshot = null)
        {
            Key = key ?? string.Empty;
            Label = label ?? string.Empty;
            Snapshot = snapshot;
        }

        public string Key { get; }
        public string Label { get; }

        /// <summary>
        /// 실물 카드 프레임용 스냅샷(2026-08-20 #8). 덱을 <b>보면서</b> 고르라는 요구라 라벨만으로는
        /// 부족하다 — 진열 카드가 쓰는 것과 같은 통로다. null이면 뷰가 종전 텍스트 행으로 물러난다.
        /// </summary>
        public CombatCardSnapshot? Snapshot { get; }
    }

    /// <summary>
    /// 상점(팝업 스토어) 모달. <see cref="DeckPileListOverlayView"/>와 같은 절차 생성형 오버레이 —
    /// 루트가 씬에 저작돼 있으면 그걸 쓰고, 없으면 런타임에 만든다. 프리팹 추출은 "UI 런타임 저작
    /// 정리" 백로그와 같은 트랙으로 미뤄져 있다.
    ///
    /// 이 뷰는 표시와 입력만 담당한다. 무엇을 파는지(재고)·거래 성패·소비는 전부 컨트롤러가
    /// 콜백으로 결정하고, 뷰는 결과를 받아 그려질 뿐이다(추첨·연출 분리 원칙과 같은 결).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ShopPopupView : MonoBehaviour
    {
        public const string RootName = "Shop Overlay Root";
        private const float BackdropAspect = 1920f / 1080f;

        // 진열대 규격(#8 배치안 B). 왼쪽 부적 880 + 간격 44 + 오른쪽 잡화 636 = 1560.
        private const float ShelfWidth = 1720f;
        private const float ShelfHeight = 620f;
        private const float ShelfColumnGap = 44f;
        private const float CardColumnWidth = 1000f;
        private const float GoodsColumnWidth = 684f;

        /// <summary>
        /// 진열대의 세로 위치. 🔴 <b>0보다 위로 올리면 배경의 전구 줄을 가린다</b>(2026-08-31 실측·사용자
        /// 지적) — 종전 +26은 전구 높이와 정확히 겹쳤다. 아래로 내려 전구를 열어 둔다.
        /// </summary>
        private const float ShelfOffsetY = -36f;
        /// <summary>카드 홀더 — 종전 176×282에서 1.48배(#8 「크기를 키우기」).</summary>
        // 카드는 진열의 주인공이라 키우고(사용자 확정 2026-08-31), 잡화 타일은 반대로 줄여
        // 3열 격자가 카드를 밀어내지 않게 한다.
        private static readonly Vector2 CardHolderSize = new Vector2(300f, 470f);
        /// <summary>잡화 격자 한 칸. 유물 2 + 소모품 2 = 2×2가 정확히 들어간다(재고 슬롯 수는 로러 상수).</summary>
        private static readonly Vector2 GoodsCellSize = new Vector2(216f, 248f);

        /// <summary>잡화 타일 바닥에 가격표를 위해 비워 두는 높이 — 이름·설명 길이와 무관해야 한다.</summary>
        private const float GoodsPriceReserve = 46f;
        /// <summary>제거 화면의 카드 타일 한 칸(#8) — 5열 × 240 + 4×16 + 좌우 14 = 1292.</summary>
        /// <summary>
        /// 고르는 격자의 칸 크기(2026-09-02 #7 — 240×268에서 확대). 🔑 <b>커진 것은 칸이 아니라
        /// 카드다</b>: 종전에는 268 중 <b>62가 아래 라벨 몫</b>이라 카드가 206 안에 <c>localScale</c>로
        /// 눌려 들어갔다. 라벨을 지우고(카드 그림이 이미 이름을 말한다) 5열을 4열로 줄여
        /// 그 자리를 전부 카드에 줬다 — 자세히 보는 일은 <b>누르면 뜨는 확인 화면</b>의 몫이다.
        /// </summary>
        private static readonly Vector2 RemovalCellSize = new Vector2(296f, 340f);

        /// <summary>격자 열 수. 폭 1320 패널에서 4열이면 칸 폭이 240 → 296으로 는다.</summary>
        private const int RemovalColumnCount = 4;
        private const string ConfirmRemovalLabel = "이 부적을 제거";

        private static readonly Color DefaultBaseColor = new Color(0.06f, 0.07f, 0.12f, 1f);
        private static readonly Color DefaultPanelColor = new Color32(0x30, 0x34, 0x68, 0xF5);
        private static readonly Color DefaultBackdropColor = new Color(0f, 0f, 0f, 0.42f);
        private static readonly Color DefaultTextColor = new Color(0.92f, 0.96f, 1f, 1f);
        private static readonly Color DefaultMutedColor = new Color(0.72f, 0.76f, 0.84f, 1f);
        private static readonly Color DefaultAccentColor = new Color32(0xC9, 0xA2, 0x27, 0xFF);

        [Tooltip("Central UI theme. When assigned, generated-default colours come from it; leave empty to use the built-in defaults.")]
        [SerializeField] private UiThemeAsset theme;

        [Header("Fullscreen layout (사이드바 제외 — 덱 목록과 같은 계약, U-4 2026-08-18)")]
        [SerializeField] private RectTransform excludedSidebar;
        [SerializeField] private float sidebarGap = 0f;

        // 잡화점 배경(스타일 보드 67fe1d93 채택본). 🔴직렬화 참조가 정본, AssetDatabase 폴백은
        // 에디터 전용 — 빌드에 실을 때 저작 루트에 참조를 채운다.
        [SerializeField] private Sprite nightMarketBackdropSprite;

        // 진열 실물화(보드 ⑩ T2·T3, 2026-08-19): 카드는 실물 CardFront 프레임, 유물은 복주머니
        // 사물, 재화는 엽전 글리프. 전부 같은 직렬화 계약(에디터 폴백은 아래 프로퍼티).
        [SerializeField] private GameObject moveCardFrontPrefab;
        [SerializeField] private GameObject actionCardFrontPrefab;
        [SerializeField] private Sprite statusCardFrameSprite;
        [SerializeField] private Sprite coinSprite;
        [SerializeField] private Sprite relicGoodsSprite;
        [SerializeField] private Sprite cardRemovalSprite;

        private Color PanelColor => theme != null ? theme.PanelColor : DefaultPanelColor;
        private Color BackdropColor => theme != null ? theme.BackdropColor : DefaultBackdropColor;
        private Color TextColor => theme != null ? theme.TextPrimary : DefaultTextColor;
        private Color MutedColor => theme != null ? theme.TextMuted : DefaultMutedColor;
        private Color AccentColor => theme != null ? theme.TextAccent : DefaultAccentColor;

        private RectTransform panel;
        private bool layoutBuilt;
        private RectTransform slotListRoot;
        private RectTransform backdropRect;
        private RectTransform incenseRoot;
        private RectTransform removalPanel;
        private RectTransform removalContent;
        private RectTransform removalConfirmPanel;
        private RectTransform removalConfirmBackdrop;
        private RectTransform removalConfirmCardRoot;
        private TMP_Text removalConfirmPriceText;
        private ShopRemovalCandidate selectedRemovalCandidate;
        private TMP_Text titleText;
        private TMP_Text balanceText;
        private TMP_Text messageText;
        private Button leaveButton;
        private static Sprite radialGlowSprite;

        private IReadOnlyList<ShopOfferSlotModel> slots = Array.Empty<ShopOfferSlotModel>();
        private Func<ShopOfferSlotModel, bool> onPurchaseRequested;
        private Func<IReadOnlyList<ShopRemovalCandidate>> removalCandidatesProvider;
        private Func<ShopRemovalCandidate, bool> onRemovalPicked;
        private Func<int> balanceProvider;
        private Action onLeaveRequested;
        private Func<string, CombatCardSnapshot?> cardSnapshotProvider;

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>ESC를 이 뷰가 소비한 프레임. 사이드바·일시정지 메뉴가 같은 입력을 무시하게 한다
        /// (<see cref="DeckPileListOverlayView.EscConsumedFrame"/>과 같은 프로토콜).</summary>
        public static int EscConsumedFrame { get; private set; } = -1;

        /// <summary>
        /// Gameplay UI Layers 아래에서 저작된 루트를 찾고, 없으면 런타임에 만든다. 저작된 루트가
        /// 있으면 그 배치를 존중한다(생성은 최초 1회만).
        /// </summary>
        public static ShopPopupView FindOrCreate(RectTransform gameplayLayers)
        {
            if (gameplayLayers == null)
            {
                return null;
            }

            var existing = gameplayLayers.GetComponentsInChildren<ShopPopupView>(includeInactive: true)
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

            var view = root.GetComponent<ShopPopupView>() ?? root.gameObject.AddComponent<ShopPopupView>();
            if (root.gameObject.activeSelf)
            {
                root.gameObject.SetActive(false);
            }

            return view;
        }

        public void Show(
            IReadOnlyList<ShopOfferSlotModel> offerSlots,
            Func<int> getBalance,
            Func<ShopOfferSlotModel, bool> purchaseRequested,
            Func<IReadOnlyList<ShopRemovalCandidate>> removalCandidates,
            Func<ShopRemovalCandidate, bool> removalPicked,
            Action leaveRequested,
            // ⑩ T2: 카드 재고(cardId)를 실물 프레임용 스냅샷으로 투영하는 컨트롤러 통로.
            // null이면 종전 텍스트 목록으로 폴백한다(빌드 미저작·구버전 호출 안전).
            Func<string, CombatCardSnapshot?> snapshotProvider = null)
        {
            slots = offerSlots ?? Array.Empty<ShopOfferSlotModel>();
            balanceProvider = getBalance;
            onPurchaseRequested = purchaseRequested;
            removalCandidatesProvider = removalCandidates;
            onRemovalPicked = removalPicked;
            onLeaveRequested = leaveRequested;
            cardSnapshotProvider = snapshotProvider;

            EnsureLayout();
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            // 사이드바를 비워 둔 모달은 사이드바 콜아웃 판(가방·유물)까지 덮어선 안 된다 — 같은 부모의
            // 형제인 사이드바 시스템을 이 모달 위로 되올린다(SidebarExclusionOrdering 참조).
            ResolveExcludedSidebarIfMissing();
            SidebarExclusionOrdering.RaiseSidebarAbove(transform, excludedSidebar);
            HideRemovalPanel();
            RefreshSlots();
            RefreshBalance();
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
            // 🔑 안쪽부터 닫는다 — 확인 화면이 떠 있으면 그것만 닫히고 고르기 화면은 남는다.
            if (removalConfirmPanel != null && removalConfirmPanel.gameObject.activeSelf)
            {
                HideRemovalConfirmPanel();
                return;
            }

            if (removalPanel != null && removalPanel.gameObject.activeSelf)
            {
                HideRemovalPanel();
                return;
            }

            onLeaveRequested?.Invoke();
        }

        private void RefreshBalance()
        {
            if (balanceText != null)
            {
                balanceText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(AccentColor)}>{balanceProvider?.Invoke() ?? 0}</color>";
            }
        }

        private void RefreshSlots()
        {
            if (slotListRoot == null)
            {
                return;
            }

            ClearChildren(slotListRoot);
            var removalSlot = slots.FirstOrDefault(slot => slot.Kind == ShopItemKind.CardRemoval);

            // ⑩ T2 실물 진열은 스냅샷 통로 + 카드 프레임 프리팹이 있어야 선다. 없으면(빌드
            // 미저작·구버전 호출) 종전 텍스트 목록으로 통째 폴백 — 화면이 죽지 않는 것이 우선.
            var realShelf = cardSnapshotProvider != null && MoveCardFrontPrefab != null && ActionCardFrontPrefab != null;
            if (!realShelf)
            {
                BuildLegacySlotList();
                UpdateIncenseTarget(null);
                return;
            }

            var cardSlots = slots.Where(slot => slot.Kind == ShopItemKind.Card).ToList();
            if (cardSlots.Count > 0)
            {
                var column = CreateShelfColumn("Shop Card Column", CardColumnWidth);
                var row = CreateSlotRow(column, "Shop Card Row", 26f);
                foreach (var slot in cardSlots)
                {
                    CreateCardStand(row, slot);
                }
            }

            // 윗줄 = 소모품, 아랫줄 = 유물 + 카드 제거. 격자가 왼쪽 위부터 채우므로 이 순서가 배치다.
            var goodsSlots = slots.Where(slot => slot.Kind == ShopItemKind.Item)
                .Concat(slots.Where(slot => slot.Kind == ShopItemKind.Relic))
                .ToList();
            if (goodsSlots.Count > 0 || removalSlot != null)
            {
                var grid = CreateGoodsGrid();
                foreach (var slot in goodsSlots)
                {
                    CreateGoodsChip(grid, slot);
                }

                if (removalSlot != null)
                {
                    CreateGoodsChip(grid, removalSlot);
                }
            }

            // 🔑 카드 제거가 격자의 한 칸이 된 뒤로 <b>향로는 클릭 타깃이 아니다</b>(사용자 확정
            //    2026-08-31, ⑩ T2 개정) — 같은 서비스에 문이 둘이면 어느 쪽이 정본인지 흐려진다.
            //    배경 그림의 향로는 그대로 있고, 기능만 격자로 옮겼다.
            UpdateIncenseTarget(null);
        }

        private void BuildLegacySlotList()
        {
            var grouped = new (ShopItemKind kind, string header)[]
            {
                (ShopItemKind.Card, "카드"),
                (ShopItemKind.Relic, "유물"),
                (ShopItemKind.Item, "소모품"),
                (ShopItemKind.CardRemoval, "서비스")
            };
            foreach (var group in grouped)
            {
                var groupSlots = slots.Where(slot => slot.Kind == group.kind).ToList();
                if (groupSlots.Count == 0)
                {
                    continue;
                }

                CreateSectionLabel(slotListRoot, group.header);
                foreach (var slot in groupSlots)
                {
                    CreateSlotButton(slotListRoot, slot);
                }
            }
        }

        /// <summary>진열대의 한 기둥(#8 B). 폭은 고정, 높이는 진열대 전체를 쓴다.</summary>
        private RectTransform CreateShelfColumn(string name, float width)
        {
            var columnObject = new GameObject(name, typeof(RectTransform), typeof(LayoutElement));
            var column = (RectTransform)columnObject.transform;
            column.SetParent(slotListRoot, worldPositionStays: false);
            var element = columnObject.GetComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = ShelfHeight;
            // 🔴childForceExpand가 켜진 LayoutGroup은 flexible을 보고해 기둥이 늘어난다(2026-08-19 실측).
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;
            return column;
        }

        /// <summary>오른쪽 잡화 격자(#8 B) — 유물·소모품이 같은 규격 타일로 선다.</summary>
        private RectTransform CreateGoodsGrid()
        {
            var gridObject = new GameObject("Shop Goods Grid", typeof(RectTransform), typeof(GridLayoutGroup), typeof(LayoutElement));
            var grid = (RectTransform)gridObject.transform;
            grid.SetParent(slotListRoot, worldPositionStays: false);
            var element = gridObject.GetComponent<LayoutElement>();
            element.preferredWidth = GoodsColumnWidth;
            element.preferredHeight = ShelfHeight;
            element.flexibleWidth = 0f;
            element.flexibleHeight = 0f;
            var layout = gridObject.GetComponent<GridLayoutGroup>();
            layout.cellSize = GoodsCellSize;
            layout.spacing = new Vector2(18f, 18f);
            layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            layout.childAlignment = TextAnchor.MiddleCenter;
            // 3열 고정(사용자 확정 2026-08-31) — 윗줄 소모품 3, 아랫줄 유물 2 + 카드 제거.
            // 🔴 채우는 <b>순서가 곧 배치</b>다: 소모품 → 유물 → 제거 순으로 넣어야 그 그림이 된다.
            // Flexible로 두면 폭 계산이 LayoutElement보다 먼저 돌아 한 줄로 눕는 프레임이 생긴다.
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 3;
            return grid;
        }

        private RectTransform CreateSlotRow(RectTransform parent, string name, float spacing)
        {
            var rowObject = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var row = (RectTransform)rowObject.transform;
            row.SetParent(parent, worldPositionStays: false);
            var rowRect = row;
            rowRect.anchorMin = Vector2.zero;
            rowRect.anchorMax = Vector2.one;
            rowRect.offsetMin = Vector2.zero;
            rowRect.offsetMax = Vector2.zero;
            var layout = rowObject.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            return row;
        }

        /// <summary>실물 카드 프레임 + 나무 팻말 가격표로 선 카드 진열 한 칸(⑩ T2).</summary>
        private void CreateCardStand(RectTransform parent, ShopOfferSlotModel slot)
        {
            var standObject = new GameObject(
                "Shop Card Stand", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var stand = (RectTransform)standObject.transform;
            stand.SetParent(parent, worldPositionStays: false);
            standObject.GetComponent<LayoutElement>().preferredWidth = CardHolderSize.x + 16f;
            var layout = standObject.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            // 스탠드 전체가 클릭 면이 되도록 투명 레이캐스트 판만 깐다(그림은 카드가 전부).
            var plate = standObject.GetComponent<Image>();
            plate.color = new Color(1f, 1f, 1f, 0.004f);
            plate.raycastTarget = true;

            var holderObject = new GameObject("Card Holder", typeof(RectTransform), typeof(LayoutElement));
            var holder = (RectTransform)holderObject.transform;
            holder.SetParent(stand, worldPositionStays: false);
            var holderElement = holderObject.GetComponent<LayoutElement>();
            holderElement.preferredWidth = CardHolderSize.x;
            holderElement.preferredHeight = CardHolderSize.y;

            var snapshot = cardSnapshotProvider?.Invoke(slot.ItemId);
            DeckPileOverlayCardView cardView = null;
            if (snapshot.HasValue)
            {
                var prefab = CardFrontListInstance.ChoosePrefab(snapshot.Value.Kind, MoveCardFrontPrefab, ActionCardFrontPrefab);
                cardView = CardFrontListInstance.Create(prefab, holder, snapshot.Value, StatusCardFrameSprite);
            }

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
            }
            else
            {
                var fallback = CreateText(holder, slot.Title, 24f, TextColor, bold: true);
                Stretch((RectTransform)fallback.transform);
            }

            var plaque = CreatePricePlaque(stand, string.Empty, slot.SoldOut ? "판매 완료" : slot.Price.ToString());

            var button = standObject.GetComponent<Button>();
            button.targetGraphic = plaque;
            button.interactable = !slot.SoldOut;
            button.onClick.AddListener(() => OnSlotClicked(slot));
            if (slot.SoldOut)
            {
                var faded = standObject.AddComponent<CanvasGroup>();
                faded.alpha = 0.45f;
            }
        }

        /// <summary>
        /// 잡화 격자의 한 칸 — 소모품·유물, 그리고 <b>카드 제거</b>도 같은 규격으로 선다
        /// (사용자 확정 2026-08-31). 셋이 같은 타일이라 「살 수 있는 것들」이 한 문법으로 읽힌다.
        /// </summary>
        private void CreateGoodsChip(RectTransform parent, ShopOfferSlotModel slot)
        {
            var chipObject = new GameObject(
                "Shop Goods Chip", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var chip = (RectTransform)chipObject.transform;
            chip.SetParent(parent, worldPositionStays: false);
            var chipElement = chipObject.GetComponent<LayoutElement>();
            chipElement.preferredWidth = GoodsCellSize.x;
            // childForceExpandWidth가 켜진 LayoutGroup은 flexibleWidth를 보고하므로 칩이
            // 진열대 폭으로 늘어난다(실측 결함, 2026-08-19) — 확장을 양쪽에서 막는다.
            chipElement.flexibleWidth = 0f;
            var layout = chipObject.GetComponent<VerticalLayoutGroup>();
            // 🔑 아래 여백은 <b>가격표 자리</b>다 — 가격표를 흐름 밖으로 빼서 바닥에 못 박으므로
            //    이름이 한 줄이든 두 줄이든 값이 언제나 같은 높이에 뜬다(사용자 지적 2026-08-31).
            layout.padding = new RectOffset(10, 10, 10, (int)GoodsPriceReserve);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var plate = chipObject.GetComponent<Image>();
            UiProceduralPanel.Attach(
                plate,
                theme != null ? theme.PopupFillColor : PanelColor,
                theme != null ? theme.PopupBorderColor : AccentColor,
                1.5f,
                10f);

            // 저작된 iconId가 있으면 그 물건의 그림이 뜨고, 없으면 종전 복주머니 플레이스홀더를
            // 함께 쓴다(2026-08-20 사용자 확정) — 빈 칸보다 「살 수 있는 물건」으로 읽히는 편이 낫다.
            // 🔴 해소는 RuntimeUiAssetCatalog 한 곳뿐이다: 여기서 AssetDatabase로 따로 읽으면
            //    사이드바 칩·도감과 갈라지고 빌드에서는 통째로 null이 된다.
            // 카드 제거는 파는 「물건」이 없어 아이콘이 따로 필요하다 — 공작소가 쓰던 향로
            // 아이콘(service_icon_remove)을 그대로 쓴다. 제거의 어휘는 이미 향로로 굳어 있다.
            var goods = slot.Kind == ShopItemKind.CardRemoval
                ? CardRemovalSprite
                : SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadItemIcon(slot.IconId) ?? RelicGoodsSprite;
            if (goods != null)
            {
                var goodsObject = new GameObject("Goods Sprite", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                var goodsRect = (RectTransform)goodsObject.transform;
                goodsRect.SetParent(chip, worldPositionStays: false);
                var goodsElement = goodsObject.GetComponent<LayoutElement>();
                goodsElement.preferredWidth = 92f;
                goodsElement.preferredHeight = 92f;
                var goodsImage = goodsObject.GetComponent<Image>();
                goodsImage.sprite = goods;
                goodsImage.preserveAspect = true;
                goodsImage.raycastTarget = false;
            }

            var title = CreateText(chip, slot.Title, 18f, TextColor, bold: true);
            ClampChipText(title, 46f);
            if (!string.IsNullOrEmpty(slot.Detail))
            {
                var detail = CreateText(chip, slot.Detail, 13f, MutedColor);
                ClampChipText(detail, 40f);
            }

            var plaque = CreatePricePlaque(
                chip, string.Empty, slot.SoldOut ? "판매 완료" : slot.Price.ToString(), pinToBottom: true);

            var button = chipObject.GetComponent<Button>();
            button.targetGraphic = plate;
            button.interactable = !slot.SoldOut;
            button.onClick.AddListener(() => OnSlotClicked(slot));
            if (slot.SoldOut)
            {
                var faded = chipObject.AddComponent<CanvasGroup>();
                faded.alpha = 0.45f;
            }
        }

        /// <summary>
        /// 나무 팻말 가격표(⑩ T2): 갈색 라운드 판 + 엽전 + 값. prefix는 향로 팻말이 쓴다.
        /// <paramref name="pinToBottom"/>이면 세로 흐름에서 빠져 부모 <b>바닥에 못 박힌다</b> —
        /// 잡화 타일에서 값의 높이가 이름 길이를 따라 오르내리지 않게 하는 장치다.
        /// </summary>
        private Image CreatePricePlaque(RectTransform parent, string prefix, string priceLabel, bool pinToBottom = false)
        {
            var plaqueObject = new GameObject("Shop Price Plaque", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            var plaque = (RectTransform)plaqueObject.transform;
            plaque.SetParent(parent, worldPositionStays: false);
            var plaqueElement = plaqueObject.GetComponent<LayoutElement>();
            plaqueElement.preferredHeight = 36f;
            if (pinToBottom)
            {
                plaqueElement.ignoreLayout = true;
                plaque.anchorMin = new Vector2(0.5f, 0f);
                plaque.anchorMax = new Vector2(0.5f, 0f);
                plaque.pivot = new Vector2(0.5f, 0f);
                plaque.anchoredPosition = new Vector2(0f, 10f);
                plaque.sizeDelta = new Vector2(0f, 36f);

                // 🔴 흐름에서 빼면 <b>폭을 아무도 정해 주지 않는다</b> — 0폭 팻말 안에서 값이 세로로
                //    쌓여 「3 / 5」로 읽혔다(실측). 내용만큼 가로로 자라게 해야 한 줄로 선다.
                var fitter = plaqueObject.AddComponent<ContentSizeFitter>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            var layout = plaqueObject.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 4, 4);
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var image = plaqueObject.GetComponent<Image>();
            UiProceduralPanel.Attach(
                image,
                new Color32(0x6E, 0x4A, 0x2A, 0xF5),
                new Color32(0x40, 0x2A, 0x14, 0xFF),
                1.5f,
                7f);

            if (!string.IsNullOrEmpty(prefix))
            {
                var prefixText = CreateText(plaque, prefix, 19f, new Color32(0xFF, 0xE9, 0xBD, 0xFF), bold: true);
                prefixText.alignment = TextAlignmentOptions.Center;
            }

            CreateCoinImage(plaque, 20f);
            var priceText = CreateText(plaque, priceLabel, 20f, new Color32(0xFF, 0xE9, 0xBD, 0xFF), bold: true);
            priceText.alignment = TextAlignmentOptions.Center;
            return image;
        }

        /// <summary>
        /// 좁아진 타일에서 이름·설명이 넘쳐 가격표 자리를 침범하지 않게 높이를 못 박고 말줄임한다.
        /// </summary>
        private static void ClampChipText(TMP_Text text, float maxHeight)
        {
            if (text == null)
            {
                return;
            }

            var element = text.gameObject.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = maxHeight;
            element.flexibleHeight = 0f;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.Center;
        }

        /// <summary>카드 제거 칸의 아이콘 — 공작소가 쓰던 향로(service_icon_remove)를 그대로 쓴다.</summary>
        private Sprite CardRemovalSprite
        {
            get
            {
                if (cardRemovalSprite != null) return cardRemovalSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                // 🔑 새 필드를 만들지 않는다 — 공작소 「카드 제거」 선택지가 쓰던 바로 그 스프라이트다.
                if (catalog != null && catalog.RemoveOptionSprite != null)
                {
                    return cardRemovalSprite = catalog.RemoveOptionSprite;
                }
#if UNITY_EDITOR
                return cardRemovalSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Art/UI/ServiceIcons/service_icon_remove.png");
#else
                return null;
#endif
            }
        }

        private void CreateCoinImage(RectTransform parent, float size)
        {
            var sprite = CoinSprite;
            if (sprite == null)
            {
                return;
            }

            var coinObject = new GameObject("Coin", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var coinRect = (RectTransform)coinObject.transform;
            coinRect.SetParent(parent, worldPositionStays: false);
            var element = coinObject.GetComponent<LayoutElement>();
            element.preferredWidth = size;
            element.preferredHeight = size;
            var image = coinObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }

        /// <summary>
        /// 카드 제거 = 배경에 이미 그려진 우하단 향로가 클릭 타깃(⑩ T2). 배경 rect의 자식으로
        /// 앵커를 걸어 EnvelopeParent 크롭·해상도와 무관하게 그림의 향로 위에 붙는다. 호버 글로우
        /// + 상시 팻말로 발견성을 만든다.
        /// </summary>
        private void UpdateIncenseTarget(ShopOfferSlotModel removalSlot)
        {
            if (incenseRoot != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(incenseRoot.gameObject);
                }
                else
                {
                    DestroyImmediate(incenseRoot.gameObject);
                }

                incenseRoot = null;
            }

            if (removalSlot == null || backdropRect == null)
            {
                return;
            }

            var targetObject = new GameObject("Shop Incense Target", typeof(RectTransform), typeof(Image), typeof(Button));
            incenseRoot = (RectTransform)targetObject.transform;
            incenseRoot.SetParent(backdropRect, worldPositionStays: false);
            // 배경 그림 실측: 향로는 우하단 x 0.83~0.97 · y 0~0.35, 연기가 그 위로 뻗는다.
            incenseRoot.anchorMin = new Vector2(0.825f, 0.01f);
            incenseRoot.anchorMax = new Vector2(0.975f, 0.40f);
            incenseRoot.offsetMin = Vector2.zero;
            incenseRoot.offsetMax = Vector2.zero;

            var glow = targetObject.GetComponent<Image>();
            glow.sprite = RadialGlowSprite;
            glow.color = new Color(1f, 0.84f, 0.45f, 1f);
            glow.raycastTarget = true;

            var button = targetObject.GetComponent<Button>();
            button.targetGraphic = glow;
            var colors = button.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0f);
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.42f);
            colors.pressedColor = new Color(1f, 1f, 1f, 0.6f);
            colors.selectedColor = colors.normalColor;
            colors.disabledColor = new Color(1f, 1f, 1f, 0f);
            button.colors = colors;
            button.interactable = !removalSlot.SoldOut;
            button.onClick.AddListener(() => OnSlotClicked(removalSlot));

            var plaqueDock = new GameObject("Incense Plaque Dock", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var dockRect = (RectTransform)plaqueDock.transform;
            dockRect.SetParent(incenseRoot, worldPositionStays: false);
            dockRect.anchorMin = new Vector2(0.5f, 1f);
            dockRect.anchorMax = new Vector2(0.5f, 1f);
            dockRect.pivot = new Vector2(0.5f, 0f);
            dockRect.anchoredPosition = new Vector2(-24f, 8f);
            var dockLayout = plaqueDock.GetComponent<HorizontalLayoutGroup>();
            dockLayout.childControlWidth = true;
            dockLayout.childControlHeight = true;
            dockLayout.childForceExpandWidth = false;
            dockLayout.childForceExpandHeight = false;
            var dockFitter = plaqueDock.GetComponent<ContentSizeFitter>();
            dockFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            dockFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            CreatePricePlaque(dockRect, "카드 제거", removalSlot.SoldOut ? "판매 완료" : removalSlot.Price.ToString());
        }

        private static Sprite RadialGlowSprite
        {
            get
            {
                if (radialGlowSprite != null)
                {
                    return radialGlowSprite;
                }

                // 향로 호버 글로우용 소프트 원 스프라이트 — 에셋 없이 런타임 생성(빌드 안전).
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false);
                texture.name = "Shop Incense Glow";
                var center = (size - 1) * 0.5f;
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center)) / center;
                        var alpha = Mathf.Clamp01(1f - distance);
                        texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha * alpha));
                    }
                }

                texture.Apply();
                radialGlowSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
                radialGlowSprite.name = "Shop Incense Glow";
                return radialGlowSprite;
            }
        }

        private void OnSlotClicked(ShopOfferSlotModel slot)
        {
            if (slot.SoldOut)
            {
                return;
            }

            if (slot.Kind == ShopItemKind.CardRemoval)
            {
                ShowRemovalPanel();
                return;
            }

            if (onPurchaseRequested != null && onPurchaseRequested(slot))
            {
                slot.SoldOut = true;
                RefreshSlots();
            }

            RefreshBalance();
        }

        private void ShowRemovalPanel()
        {
            EnsureLayout();
            if (removalPanel == null)
            {
                return;
            }

            removalPanel.gameObject.SetActive(true);
            removalPanel.SetAsLastSibling();
            HideRemovalConfirmPanel();
            ClearChildren(removalContent);
            var candidates = removalCandidatesProvider?.Invoke() ?? Array.Empty<ShopRemovalCandidate>();
            foreach (var candidate in candidates)
            {
                CreateRemovalTile(candidate);
            }

            if (candidates.Count == 0)
            {
                CreateText(removalContent, "제거할 수 있는 카드가 없습니다.", 24f, MutedColor);
            }
        }

        /// <summary>덱 목록형 제거 타일 한 칸(#8): 실물 카드 프레임 + 더미 라벨. 누르면 선택만 된다.</summary>
        private void CreateRemovalTile(ShopRemovalCandidate candidate)
        {
            var tileObject = new GameObject(
                "Shop Removal Tile", typeof(RectTransform), typeof(Image), typeof(Button),
                typeof(VerticalLayoutGroup), typeof(LayoutElement));
            var tile = (RectTransform)tileObject.transform;
            tile.SetParent(removalContent, worldPositionStays: false);
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
                holderElement.preferredWidth = RemovalCellSize.x - 20f;
                holderElement.preferredHeight = RemovalCellSize.y - 20f;

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

            // 🔑 <b>카드를 그렸으면 글자를 붙이지 않는다</b>(2026-09-02 #7 · 사용자 확정) — 어느 더미에서
            //    왔는지까지 목록에서 말할 필요가 없다. 누르면 확인 화면이 전부 다시 말한다.
            //    스냅샷이나 프리팹이 없을 때만 종전 텍스트 행으로 물러난다 — 화면이 죽지 않는 것이 우선.
            if (!drewCard)
            {
                CreateText(tile, candidate.Label, 22f, MutedColor).alignment = TextAlignmentOptions.Center;
            }

            var pick = candidate;
            var button = tileObject.GetComponent<Button>();
            button.targetGraphic = plate;
            button.onClick.AddListener(() => OnRemovalTileClicked(pick));
        }

        /// <summary>
        /// 카드를 누르면 <b>곧바로 확인 화면</b>이 뜬다(사용자 확정 2026-08-31 — 캠핑카 연마와 같은 문법).
        ///
        /// <para>
        /// 🔑 되돌릴 틈을 없앤 것이 아니다. 종전 「선택 → 결정」 2단계가 지키던 것은 <b>누르는 즉시
        /// 카드가 사라지지 않는다</b>였고, 그 안전장치는 확인 화면의 「돌아가기」로 자리만 옮겼다.
        /// 대신 고른 카드를 <b>크게 보여 주고 값까지 말하므로</b>, 무엇을 얼마에 지우는지가 더 분명하다.
        /// </para>
        /// </summary>
        private void OnRemovalTileClicked(ShopRemovalCandidate candidate)
        {
            selectedRemovalCandidate = candidate;
            ShowRemovalConfirmPanel(candidate);
        }

        private void ShowRemovalConfirmPanel(ShopRemovalCandidate candidate)
        {
            EnsureLayout();
            if (removalConfirmPanel == null)
            {
                // 확인 화면을 못 세우면 고르기가 막다른 길이 된다 — 종전 즉시 제거로 물러난다.
                OnRemovalRowClicked(candidate);
                return;
            }

            if (removalConfirmBackdrop != null)
            {
                removalConfirmBackdrop.gameObject.SetActive(true);
                removalConfirmBackdrop.SetAsLastSibling();
            }

            removalConfirmPanel.gameObject.SetActive(true);
            removalConfirmPanel.SetAsLastSibling();
            ClearChildren(removalConfirmCardRoot);

            var snapshot = candidate.Snapshot;
            if (snapshot.HasValue && MoveCardFrontPrefab != null && ActionCardFrontPrefab != null)
            {
                var holderObject = new GameObject("Card Holder", typeof(RectTransform), typeof(LayoutElement));
                var holder = (RectTransform)holderObject.transform;
                holder.SetParent(removalConfirmCardRoot, worldPositionStays: false);
                var holderElement = holderObject.GetComponent<LayoutElement>();
                holderElement.preferredWidth = CardConfirmPanelSpec.CardHolderSize.x;
                holderElement.preferredHeight = CardConfirmPanelSpec.CardHolderSize.y;

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
                }
                else
                {
                    CreateText(removalConfirmCardRoot, candidate.Label, 26f, TextColor, bold: true);
                }
            }
            else
            {
                CreateText(removalConfirmCardRoot, candidate.Label, 26f, TextColor, bold: true);
            }

            if (removalConfirmPriceText != null)
            {
                var removalSlot = slots.FirstOrDefault(slot => slot.Kind == ShopItemKind.CardRemoval);
                var price = removalSlot != null ? removalSlot.Price : 0;
                var accentHex = ColorUtility.ToHtmlStringRGB(AccentColor);
                removalConfirmPriceText.text = price > 0
                    ? $"덱에서 영구히 제거합니다  ·  <color=#{accentHex}>{price}</color>"
                    : "덱에서 영구히 제거합니다";
            }

            foreach (var skin in removalConfirmPanel.GetComponentsInChildren<UiProceduralPanel>(includeInactive: true))
            {
                skin.ResyncRectSize();
            }
        }

        private void HideRemovalConfirmPanel()
        {
            if (removalConfirmBackdrop != null)
            {
                removalConfirmBackdrop.gameObject.SetActive(false);
            }

            if (removalConfirmPanel != null)
            {
                removalConfirmPanel.gameObject.SetActive(false);
            }
        }

        private void ConfirmRemoval()
        {
            HideRemovalConfirmPanel();
            OnRemovalRowClicked(selectedRemovalCandidate);
        }

        private void OnRemovalRowClicked(ShopRemovalCandidate candidate)
        {
            if (onRemovalPicked == null || !onRemovalPicked(candidate))
            {
                RefreshBalance();
                return;
            }

            var removalSlot = slots.FirstOrDefault(slot => slot.Kind == ShopItemKind.CardRemoval);
            if (removalSlot != null)
            {
                removalSlot.SoldOut = true;
            }

            HideRemovalPanel();
            RefreshSlots();
            RefreshBalance();
        }

        private void HideRemovalPanel()
        {
            HideRemovalConfirmPanel();
            if (removalPanel != null)
            {
                removalPanel.gameObject.SetActive(false);
            }
        }

        // ── 레이아웃 생성 ─────────────────────────────────────────────────────────────

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

            // 배경이 못 깔린 경우에도 화면이 뚫리지 않는 바닥색.
            var baseImage = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
            baseImage.color = DefaultBaseColor;
            baseImage.raycastTarget = true;

            // 잡화점 배경: 마스크 안에서 16:9 원본비 유지(EnvelopeParent) — 사이드바 제외 영역은
            // 16:9보다 좁아 넘치는 폭은 마스크가 좌우 균등하게 자른다(구도가 대칭이라 안전).
            var maskObject = new GameObject("Shop Backdrop Mask", typeof(RectTransform), typeof(RectMask2D));
            var maskRect = (RectTransform)maskObject.transform;
            maskRect.SetParent(rect, worldPositionStays: false);
            Stretch(maskRect);

            var backdropObject = new GameObject("Shop Backdrop", typeof(RectTransform), typeof(Image), typeof(AspectRatioFitter));
            backdropRect = (RectTransform)backdropObject.transform;
            backdropRect.SetParent(maskRect, worldPositionStays: false);
            backdropRect.anchorMin = new Vector2(0.5f, 0.5f);
            backdropRect.anchorMax = new Vector2(0.5f, 0.5f);
            backdropRect.pivot = new Vector2(0.5f, 0.5f);
            backdropRect.anchoredPosition = Vector2.zero;
            var backdropImage = backdropObject.GetComponent<Image>();
            backdropImage.raycastTarget = false;
            var sprite = NightMarketBackdropSprite;
            backdropImage.sprite = sprite;
            backdropImage.enabled = sprite != null;
            var fitter = backdropObject.GetComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = BackdropAspect;

            // 타이틀은 좌상단 — 배경 상단 중앙의 여우탈 네온이 이 스크린의 간판이라 겹치지 않는다.
            titleText = CreateText(rect, ObjectInfoTooltipContent.ShopTitle, 44f, TextColor, bold: true);
            KoreanFontProvider.ApplyTitle(titleText);
            var titleRect = (RectTransform)titleText.transform;
            titleRect.anchorMin = new Vector2(0f, 1f);
            titleRect.anchorMax = new Vector2(0f, 1f);
            titleRect.pivot = new Vector2(0f, 1f);
            titleRect.anchoredPosition = new Vector2(56f, -30f);
            titleRect.sizeDelta = new Vector2(420f, 58f);
            titleText.alignment = TextAlignmentOptions.Left;

            // 보유 재화 = 엽전 글리프 + 숫자(⑩ T3). 어두운 배경이라 라이트 틴트 사본을 쓴다.
            var balanceDock = new GameObject("Shop Balance Dock", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(ContentSizeFitter));
            var balanceDockRect = (RectTransform)balanceDock.transform;
            balanceDockRect.SetParent(rect, worldPositionStays: false);
            balanceDockRect.anchorMin = new Vector2(1f, 1f);
            balanceDockRect.anchorMax = new Vector2(1f, 1f);
            balanceDockRect.pivot = new Vector2(1f, 1f);
            balanceDockRect.anchoredPosition = new Vector2(-56f, -34f);
            var balanceLayout = balanceDock.GetComponent<HorizontalLayoutGroup>();
            balanceLayout.spacing = 9f;
            balanceLayout.childAlignment = TextAnchor.MiddleCenter;
            balanceLayout.childControlWidth = true;
            balanceLayout.childControlHeight = true;
            balanceLayout.childForceExpandWidth = false;
            balanceLayout.childForceExpandHeight = false;
            var balanceFitter = balanceDock.GetComponent<ContentSizeFitter>();
            balanceFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            balanceFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            CreateCoinImage(balanceDockRect, 32f);
            balanceText = CreateText(balanceDockRect, string.Empty, 30f, TextColor, bold: true);
            balanceText.alignment = TextAlignmentOptions.Left;

            messageText = CreateText(rect, string.Empty, 22f, MutedColor);
            var messageRect = (RectTransform)messageText.transform;
            messageRect.anchorMin = new Vector2(0.5f, 0f);
            messageRect.anchorMax = new Vector2(0.5f, 0f);
            messageRect.pivot = new Vector2(0.5f, 0f);
            messageRect.anchoredPosition = new Vector2(0f, 108f);
            messageRect.sizeDelta = new Vector2(900f, 30f);

            // 진열대 — 배경의 보자기(quiet zone) 위. panel 참조는 이 컨테이너를 가리킨다.
            var slotListObject = new GameObject("Shop Slot List", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            slotListRoot = (RectTransform)slotListObject.transform;
            slotListRoot.SetParent(rect, worldPositionStays: false);
            slotListRoot.anchorMin = new Vector2(0.5f, 0.5f);
            slotListRoot.anchorMax = new Vector2(0.5f, 0.5f);
            slotListRoot.pivot = new Vector2(0.5f, 0.5f);
            // 좌우 2단(2026-08-20 #8 배치안 B): 왼쪽 부적 진열, 오른쪽 잡화 격자. 종전은 980폭
            // 세로 1열이라 1920 화면의 절반만 쓰고 아래가 비어 보였다. 지갑은 우상단 독에 그대로
            // 두고(사용자 확정), 우하단 향로 존(x 0.825~ · y 0.01~)은 이 컨테이너 바깥이다.
            slotListRoot.anchoredPosition = new Vector2(0f, ShelfOffsetY);
            slotListRoot.sizeDelta = new Vector2(ShelfWidth, ShelfHeight);
            var slotLayout = slotListObject.GetComponent<HorizontalLayoutGroup>();
            slotLayout.spacing = ShelfColumnGap;
            slotLayout.childAlignment = TextAnchor.MiddleCenter;
            slotLayout.childControlWidth = true;
            slotLayout.childControlHeight = true;
            slotLayout.childForceExpandWidth = false;
            slotLayout.childForceExpandHeight = false;
            panel = slotListRoot;

            var leaveDock = new GameObject("Shop Leave Dock", typeof(RectTransform), typeof(VerticalLayoutGroup));
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
            leaveButton = CreateListButton(leaveRect, "떠나기 (잡화점은 사라집니다)", 26f, () => onLeaveRequested?.Invoke());

            BuildRemovalPanel(rect);
        }

        // 🔴 런타임 생성 뷰라 SerializeField가 채워질 표면이 없다 — 빌드 안전 정본은
        // RuntimeUiAssetCatalog(Resources)다(2026-08-19 #16: AssetDatabase-전용 폴백은 빌드에서
        // 전부 null — 잡화점 배경·엽전·진열 카드가 통째로 사라졌다). AssetDatabase는 에디터 최후 폴백.
        private Sprite NightMarketBackdropSprite
        {
            get
            {
                if (nightMarketBackdropSprite != null) return nightMarketBackdropSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.ShopBackdropSprite != null) return nightMarketBackdropSprite = catalog.ShopBackdropSprite;
#if UNITY_EDITOR
                return nightMarketBackdropSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Backdrops/service_backdrop_popup_shop.png");
#else
                return null;
#endif
            }
        }

        private GameObject MoveCardFrontPrefab
        {
            get
            {
                if (moveCardFrontPrefab != null) return moveCardFrontPrefab;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.MoveCardFrontPrefab != null) return moveCardFrontPrefab = catalog.MoveCardFrontPrefab;
#if UNITY_EDITOR
                return moveCardFrontPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Cards/CardFront_Move.prefab");
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
                return actionCardFrontPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/Cards/CardFront_Action.prefab");
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
                return statusCardFrameSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Cards/card_frame_status.png");
#else
                return null;
#endif
            }
        }

        private Sprite CoinSprite
        {
            get
            {
                if (coinSprite != null) return coinSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.CoinLightSprite != null) return coinSprite = catalog.CoinLightSprite;
#if UNITY_EDITOR
                return coinSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/ServiceIcons/service_coin_light.png");
#else
                return null;
#endif
            }
        }

        private Sprite RelicGoodsSprite
        {
            get
            {
                if (relicGoodsSprite != null) return relicGoodsSprite;
                var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
                if (catalog != null && catalog.RelicGoodsSprite != null) return relicGoodsSprite = catalog.RelicGoodsSprite;
#if UNITY_EDITOR
                return relicGoodsSprite = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/ServiceIcons/service_goods_relic_pouch.png");
#else
                return null;
#endif
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

        private void BuildRemovalPanel(RectTransform rootRect)
        {
            // 덱을 보면서 고르는 화면(#8) — 실물 카드 5열이 서려면 종전 680폭으로는 부족하다.
            removalPanel = CreatePanelRect("Shop Removal Panel", rootRect, new Vector2(1320f, 940f));
            var image = removalPanel.gameObject.AddComponent<Image>();
            image.color = PanelColor;
            UiProceduralPanel.Attach(
                image,
                theme != null ? theme.PopupFillColor : PanelColor,
                theme != null ? theme.PopupBorderColor : AccentColor,
                theme != null ? theme.PopupBorderThickness : 2f,
                theme != null ? theme.PopupCornerRadius : 16f);

            var layout = removalPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(28, 28, 24, 24);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            UiPanelChrome.Attach(removalPanel, theme != null ? theme.PopupBorderColor : AccentColor);

            var removalTitle = CreateText(removalPanel, "제거할 카드 선택", 32f, TextColor, bold: true);
            KoreanFontProvider.ApplyTitle(removalTitle);
            CreateText(removalPanel, "손패·뽑을 더미·버림 더미의 카드 한 장을 영구히 제거합니다.", 20f, MutedColor);

            var scrollObject = new GameObject("Shop Removal Scroll", typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            var scrollRect = (RectTransform)scrollObject.transform;
            scrollRect.SetParent(removalPanel, worldPositionStays: false);
            scrollObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.24f);
            scrollObject.GetComponent<LayoutElement>().flexibleHeight = 1f;

            var viewportObject = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            var viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(scrollRect, worldPositionStays: false);
            Stretch(viewport);
            viewportObject.GetComponent<Image>().color = Color.white;
            viewportObject.GetComponent<Mask>().showMaskGraphic = false;

            var contentObject = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            removalContent = (RectTransform)contentObject.transform;
            removalContent.SetParent(viewport, worldPositionStays: false);
            removalContent.anchorMin = new Vector2(0f, 1f);
            removalContent.anchorMax = new Vector2(1f, 1f);
            removalContent.pivot = new Vector2(0.5f, 1f);
            removalContent.offsetMin = Vector2.zero;
            removalContent.offsetMax = Vector2.zero;
            var contentLayout = contentObject.GetComponent<GridLayoutGroup>();
            contentLayout.padding = new RectOffset(14, 14, 14, 14);
            contentLayout.cellSize = RemovalCellSize;
            contentLayout.spacing = new Vector2(16f, 16f);
            contentLayout.startCorner = GridLayoutGroup.Corner.UpperLeft;
            contentLayout.startAxis = GridLayoutGroup.Axis.Horizontal;
            contentLayout.childAlignment = TextAnchor.UpperCenter;
            contentLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            contentLayout.constraintCount = RemovalColumnCount;
            contentObject.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollObject.GetComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = removalContent;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 24f;

            // 되돌릴 틈은 <b>확인 화면</b>이 만든다(2026-08-31 개정) — 여기 아래에는 나가는 문 하나뿐이다.
            //
            // 🔴🔴 <b>버튼을 감싸는 푸터를 다시 만들지 말 것</b>(2026-09-02 #2). 종전에는 이 버튼이
            //    HorizontalLayoutGroup 푸터 안에 있었는데, <b>강제 확장이 켜진 레이아웃 그룹은 그 축의
            //    자기 flexible 값을 1로 보고한다</b> — 푸터의 LayoutElement가 preferredHeight 64를
            //    적었어도 flexibleHeight를 0으로 못 박지 않아, 부모가 남는 높이를 스크롤(1)과
            //    푸터(1)에게 <b>반씩</b> 나눠 줬다. 940 패널에서 그 반이 「버튼이 화면 절반」의 정체다.
            //    (같은 함정을 2026-08-18에 <b>폭</b>에서 물었다 — 이번은 세로 축의 같은 사고다.)
            //    캠핑카 연마의 「돌아가기」는 처음부터 패널의 <b>직계 자식</b>이라 멀쩡했다. 두 화면을
            //    같은 구조로 맞춰 그 갈래 자체를 없앤다.
            CreateListButton(removalPanel, "돌아가기", 24f, HideRemovalPanel);
            removalPanel.gameObject.SetActive(false);

            BuildRemovalConfirmPanel(rootRect);
        }

        /// <summary>
        /// 제거 확인 화면 — 캠핑카 연마 확인과 같은 골격(고른 카드를 크게 + 무슨 일이 일어나는지 + 두 버튼).
        /// </summary>
        private void BuildRemovalConfirmPanel(RectTransform rootRect)
        {
            // 🔴 확인 화면 뒤에는 <b>고르기 격자가 그대로 깔려 있다</b> — 딤 없이 띄우면 카드와 푸터
            //    글자가 확인 패널 위로 비쳐 읽힌다(실측). 전리품 목록·보상뽑기 상자와 같은 값으로 누른다.
            var backdropObject = new GameObject("Shop Removal Confirm Backdrop", typeof(RectTransform), typeof(Image));
            removalConfirmBackdrop = (RectTransform)backdropObject.transform;
            removalConfirmBackdrop.SetParent(rootRect, worldPositionStays: false);
            Stretch(removalConfirmBackdrop);
            var backdropImage = backdropObject.GetComponent<Image>();
            backdropImage.color = new Color(0f, 0f, 0f, 0.72f);
            backdropImage.raycastTarget = true;
            backdropObject.SetActive(false);

            // 규격은 연마 확인과 공유한다(CardConfirmPanelSpec) — 폭만 내용이 정한다(카드 한 장).
            removalConfirmPanel = CreatePanelRect(
                "Shop Removal Confirm Panel", rootRect, new Vector2(760f, CardConfirmPanelSpec.PanelHeight));
            var image = removalConfirmPanel.gameObject.AddComponent<Image>();
            image.color = PanelColor;

            // 🔴 이 패널만 <b>불투명</b>이다 — 다른 팝업은 배경 그림 위에 서지만 이것은 고르기 격자
            //    위에 선다. 테마 기본 알파(반투명)를 그대로 쓰면 뒤 패널의 「돌아가기」가 비쳐 읽힌다(실측).
            var confirmFill = theme != null ? theme.PopupFillColor : PanelColor;
            confirmFill.a = 1f;
            UiProceduralPanel.Attach(
                image,
                confirmFill,
                theme != null ? theme.PopupBorderColor : AccentColor,
                theme != null ? theme.PopupBorderThickness : 2f,
                theme != null ? theme.PopupCornerRadius : 16f);

            var layout = removalConfirmPanel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = CardConfirmPanelSpec.Padding;
            layout.spacing = CardConfirmPanelSpec.RowSpacing;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            UiPanelChrome.Attach(removalConfirmPanel, theme != null ? theme.PopupBorderColor : AccentColor);

            var title = CreateText(removalConfirmPanel, "제거 확인", CardConfirmPanelSpec.TitleFontSize, TextColor, bold: true);
            KoreanFontProvider.ApplyTitle(title);

            var cardsObject = new GameObject("Removal Confirm Card", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            removalConfirmCardRoot = (RectTransform)cardsObject.transform;
            removalConfirmCardRoot.SetParent(removalConfirmPanel, worldPositionStays: false);
            var cardsLayout = cardsObject.GetComponent<HorizontalLayoutGroup>();
            cardsLayout.childAlignment = TextAnchor.MiddleCenter;
            cardsLayout.childControlWidth = true;
            cardsLayout.childControlHeight = true;
            cardsLayout.childForceExpandWidth = false;
            cardsLayout.childForceExpandHeight = false;
            cardsObject.GetComponent<LayoutElement>().flexibleHeight = 1f;

            removalConfirmPriceText = CreateText(
                removalConfirmPanel, string.Empty, CardConfirmPanelSpec.CaptionFontSize, TextColor);
            removalConfirmPriceText.alignment = TextAlignmentOptions.Center;

            var buttonsObject = new GameObject("Removal Confirm Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            var buttonsRect = (RectTransform)buttonsObject.transform;
            buttonsRect.SetParent(removalConfirmPanel, worldPositionStays: false);
            var buttonsLayout = buttonsObject.GetComponent<HorizontalLayoutGroup>();
            buttonsLayout.spacing = CardConfirmPanelSpec.ButtonSpacing;
            buttonsLayout.childAlignment = TextAnchor.MiddleCenter;
            buttonsLayout.childControlWidth = true;
            buttonsLayout.childControlHeight = true;
            // 🔑 늘리지 않는다 — 늘리면 버튼 폭이 <b>패널 폭을 따라가</b> 연마 확인과 갈린다.
            buttonsLayout.childForceExpandWidth = false;
            buttonsLayout.childForceExpandHeight = false;

            SizeConfirmButton(CreateListButton(buttonsRect, ConfirmRemovalLabel, CardConfirmPanelSpec.ButtonFontSize, ConfirmRemoval));
            SizeConfirmButton(CreateListButton(buttonsRect, "돌아가기", CardConfirmPanelSpec.ButtonFontSize, HideRemovalConfirmPanel));
            removalConfirmPanel.gameObject.SetActive(false);
        }

        /// <summary>확인 화면의 버튼을 공용 규격으로 못 박는다 — 연마 확인과 같은 크기로 보이는 이유.</summary>
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

        private void CreateSectionLabel(RectTransform parent, string label)
        {
            var text = CreateText(parent, $"— {label} —", 24f, MutedColor, bold: true);
            text.alignment = TextAlignmentOptions.Center;
        }

        private void CreateSlotButton(RectTransform parent, ShopOfferSlotModel slot)
        {
            var accentHex = ColorUtility.ToHtmlStringRGB(AccentColor);
            var priceLabel = slot.SoldOut ? "판매 완료" : $"{slot.Price}";
            var label = string.IsNullOrEmpty(slot.Detail)
                ? $"{slot.Title}   <color=#{accentHex}>{priceLabel}</color>"
                : $"{slot.Title}   <color=#{accentHex}>{priceLabel}</color>\n<size=70%><color=#B8BCD0>{slot.Detail}</color></size>";
            var button = CreateListButton(parent, label, 26f, () => OnSlotClicked(slot));
            button.interactable = !slot.SoldOut;
        }

        private Button CreateListButton(RectTransform parent, string label, float fontSize, Action onClick)
        {
            var buttonObject = new GameObject("Shop Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
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
            var textObject = new GameObject("Shop Text", typeof(RectTransform));
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
                    // 에디트 모드 실기 캡처(⑩ 검증 절차)가 Show를 반복 호출할 수 있게 한다.
                    DestroyImmediate(child);
                }
            }
        }
    }
}
