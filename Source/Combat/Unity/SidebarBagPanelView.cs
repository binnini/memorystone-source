using System;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 사이드바 「가방」 콜아웃. 슬롯은 <b>그림 한 장</b>이고, 이름·설명은 슬롯 위에 욱여넣지 않고
    /// 커서 옆 툴팁(<see cref="SidebarSlotTooltip"/>)이 말한다 — 68px 칸에 이름을 넣으면 어떤 이름이든
    /// 「생기…」처럼 잘려서 무슨 아이템인지 못 읽는다.
    ///
    /// <para>빈 칸도 <b>격자로 그린다</b>. 상한이 3칸이라 몇 칸을 더 담을 수 있는지가 그 자체로 정보이고,
    /// 안 그리면 칸이 몇 개인지 보이지 않는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SidebarBagPanelView : MonoBehaviour
    {
        private const string IconName = "Bag Slot Icon";

        [Serializable]
        private sealed class SlotBinding
        {
            [SerializeField] private RectTransform root;
            [SerializeField] private Image background;
            [SerializeField] private Image icon;
            [SerializeField] private TMP_Text label;
            [SerializeField] private TMP_Text count;

            public RectTransform Root => root;

            public void Bind(RectTransform slotRoot)
            {
                root = slotRoot;
                background = slotRoot != null ? slotRoot.GetComponent<Image>() : null;
                icon = slotRoot != null ? FindIcon(slotRoot) : null;

                var texts = slotRoot != null
                    ? slotRoot.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                    : Array.Empty<TMP_Text>();
                label = texts.FirstOrDefault(text => text.name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? texts.FirstOrDefault();
                count = texts.FirstOrDefault(text => text.name.IndexOf("Count", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? texts.Skip(1).FirstOrDefault();
            }

            /// <summary>
            /// 그림 타깃은 저작돼 있으면 그것을 쓰고, 없으면 만든다 — 유물 칩이 쓰는 것과 같은 계약이라
            /// 옛 저작(그림 칸이 없는 프리팹)에서도 화면이 깨지지 않는다.
            /// </summary>
            private static Image FindIcon(RectTransform slotRoot)
            {
                var existing = slotRoot.Find(IconName) as RectTransform;
                if (existing != null)
                {
                    return existing.GetComponent<Image>();
                }

                var go = new GameObject(IconName, typeof(RectTransform));
                var rect = go.GetComponent<RectTransform>();
                rect.SetParent(slotRoot, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = new Vector2(5f, 5f);
                rect.offsetMax = new Vector2(-5f, -5f);
                go.AddComponent<LayoutElement>().ignoreLayout = true;

                var image = go.AddComponent<Image>();
                image.preserveAspect = true;
                // 칩 판이 호버·클릭을 받아야 한다 — 그림이 레이캐스트를 가로채면 안 된다.
                image.raycastTarget = false;
                rect.SetAsFirstSibling();
                return image;
            }

            public void SetFont(TMP_FontAsset font)
            {
                if (font == null)
                {
                    return;
                }

                if (label != null)
                {
                    label.font = font;
                }

                if (count != null)
                {
                    count.font = font;
                }
            }

            public void SetEmpty(Color emptyColor, Color emptyRimColor, Color mutedTextColor)
            {
                Show(emptyColor);
                SetRim(emptyRimColor);
                SetIcon(null);

                if (label != null)
                {
                    label.text = "+";
                    label.color = mutedTextColor;
                }

                if (count != null)
                {
                    count.text = string.Empty;
                    count.color = mutedTextColor;
                }
            }

            public void SetStack(
                PlayerBagItemStack stack,
                ConsumableItemDefinition definition,
                Sprite iconSprite,
                Color filledColor,
                Color filledRimColor,
                Color textColor,
                Color countColor)
            {
                Show(filledColor);
                SetRim(filledRimColor);
                SetIcon(iconSprite);

                if (label != null)
                {
                    // 그림이 있으면 글자는 비운다 — 없을 때만 이름 두 글자가 대신 선다(그림 없는
                    // 소모품이 새로 들어와도 칸이 빈 채로 남지 않는다).
                    label.text = iconSprite != null ? string.Empty : ShortLabel(stack, definition);
                    label.color = textColor;
                }

                if (count != null)
                {
                    // 1개짜리는 숫자를 달지 않는다 — 「x1」은 정보가 없는데 그림만 가린다.
                    count.text = stack.Count > 1 ? $"×{stack.Count}" : string.Empty;
                    count.color = countColor;
                }
            }

            private void Show(Color backgroundColor)
            {
                if (root != null)
                {
                    root.gameObject.SetActive(true);
                }

                if (background != null)
                {
                    background.color = backgroundColor;
                    // 🔴 클릭·호버가 들어오는 유일한 문이다. 저작 시 raycastTarget이 꺼져 있으면
                    //    아이템을 눌러도 아무 일도 일어나지 않는다(출하 저작이 실제로 꺼져 있었다).
                    background.raycastTarget = true;
                }
            }

            /// <summary>담긴 칸과 빈 칸을 <b>테두리</b>로 가른다 — 판 색만으로는 격자가 안 읽힌다.</summary>
            private void SetRim(Color rimColor)
            {
                if (root == null)
                {
                    return;
                }

                var outline = root.GetComponent<Outline>();
                if (outline == null)
                {
                    outline = root.gameObject.AddComponent<Outline>();
                    outline.effectDistance = new Vector2(1.5f, 1.5f);
                    outline.useGraphicAlpha = false;
                }

                outline.effectColor = rimColor;
                outline.enabled = true;
            }

            private void SetIcon(Sprite sprite)
            {
                if (icon == null)
                {
                    return;
                }

                icon.sprite = sprite;
                icon.color = Color.white;
                icon.gameObject.SetActive(sprite != null);
            }

            private static string ShortLabel(PlayerBagItemStack stack, ConsumableItemDefinition definition)
            {
                var name = definition != null ? definition.DisplayName : stack.ItemId;
                if (string.IsNullOrWhiteSpace(name))
                {
                    return "?";
                }

                return name.Length >= 2 ? name.Substring(0, 2) : name;
            }
        }

        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private RectTransform slotGrid;
        [SerializeField] private SlotBinding[] slots = Array.Empty<SlotBinding>();
        [SerializeField] private Color filledSlotColor = new Color(0.22f, 0.25f, 0.44f, 0.92f);
        [SerializeField] private Color emptySlotColor = new Color(0.16f, 0.18f, 0.30f, 0.55f);
        // P6 T2: light ink — the slot plates are dark indigo and the callout behind them is now the dark
        // procedural panel skin, so the previous near-black ink was unreadable on both.
        [SerializeField] private Color textColor = new Color(0.92f, 0.96f, 1f, 1f);
        [SerializeField] private Color mutedTextColor = new Color(0.70f, 0.78f, 0.90f, 1f);
        [SerializeField] private Color accentColor = new Color(0.86f, 0.75f, 0.48f, 1f);
        [SerializeField] private Color filledRimColor = new Color(0.86f, 0.75f, 0.48f, 0.9f);
        // 빈 칸은 「없다」가 아니라 「아직 비었다」로 읽혀야 한다 — 선은 남기고 밝기만 낮춘다.
        [SerializeField] private Color emptyRimColor = new Color(0.55f, 0.60f, 0.80f, 0.45f);

        // T4-1 클릭 사용: 슬롯 인덱스 → 마지막 Refresh의 itemId. 핸들러는 교체 방식(SetUseItemHandler)
        // 이라 라우팅 재구성이 중복 구독을 쌓지 않는다(GameplayHudBridge의 BindExternalClickHandler 결).
        private string[] slotItemIds = Array.Empty<string>();
        private Func<string, bool> useItemHandler;

        /// <summary>슬롯 클릭 시 아이템 사용을 위임할 핸들러를 설정한다(교체 — 중복 구독 없음).</summary>
        public void SetUseItemHandler(Func<string, bool> handler)
        {
            useItemHandler = handler;
        }

        private void HandleSlotClicked(int slotIndex)
        {
            if (useItemHandler == null || slotItemIds == null
                || slotIndex < 0 || slotIndex >= slotItemIds.Length
                || string.IsNullOrEmpty(slotItemIds[slotIndex]))
            {
                return;
            }

            useItemHandler(slotItemIds[slotIndex]);
        }

        public void AutoBindFromHierarchy()
        {
            contentRoot = FindRect("Bag Content")
                ?? FindRect("Sidebar Runtime Content")
                ?? transform as RectTransform;
            titleText = FindText("Bag Title") ?? FindText("Runtime Title");
            statusText = FindText("Bag Status") ?? FindText("Runtime Subtitle");
            slotGrid = FindRect("Bag Slot Grid") ?? FindRect("Bag Stack Grid");

            slots = slotGrid == null
                ? Array.Empty<SlotBinding>()
                : Enumerable.Range(0, slotGrid.childCount)
                    .Select(i =>
                    {
                        var binding = new SlotBinding();
                        binding.Bind(slotGrid.GetChild(i) as RectTransform);
                        return binding;
                    })
                    .ToArray();
        }

        public void Refresh(PlayerBagState bag, TMP_FontAsset font, int slotLimit = -1)
        {
            EnsureBound();
            // 멜빵 유물(T4-3): 슬롯 상한이 저작된 슬롯 수를 넘으면 마지막 저작 슬롯을 복제해 늘린다 —
            // 복제라 프리팹 저작 모습과 어긋나지 않고, 상한이 다시 줄면 남는 복제는 숨긴다.
            if (slotLimit > 0)
            {
                EnsureSlotCapacity(slotLimit);
            }

            if (contentRoot != null)
            {
                contentRoot.gameObject.SetActive(true);
            }

            var stacks = bag?.Stacks ?? Array.Empty<PlayerBagItemStack>();
            var visibleSlots = slotLimit > 0 ? Math.Min(slotLimit, slots.Length) : slots.Length;

            SetText(titleText, "가방", font, accentColor);
            SetText(
                statusText,
                bag == null ? "가방 정보 없음" : $"{Math.Min(stacks.Count, visibleSlots)} / {visibleSlots} 슬롯",
                font,
                mutedTextColor);

            slotItemIds = new string[slots.Length];
            for (var i = 0; i < slots.Length; i++)
            {
                if (i >= visibleSlots)
                {
                    slotItemIds[i] = string.Empty;
                    UnbindSlotHover(slots[i].Root);
                    if (slots[i].Root != null)
                    {
                        slots[i].Root.gameObject.SetActive(false);
                    }

                    continue;
                }

                slots[i].SetFont(font);
                EnsureSlotClickRelay(i);
                if (i < stacks.Count)
                {
                    var stack = stacks[i];
                    slotItemIds[i] = stack.ItemId;
                    // T4-1: 표시명은 카탈로그가 정본 — 미등록 id(갤러리 placeholder 토큰 등)는 raw id 폴백.
                    var definition = ConsumableItemCatalog.TryGet(stack.ItemId, out var found) ? found : null;
                    // 🔑 아이콘 해소는 이 한 곳으로만 간다(RuntimeUiAssetCatalog) — 카드·상점·전리품이
                    //    쓰는 그 통로라 새 소모품 아트가 들어오면 네 화면이 함께 산다.
                    var icon = definition != null
                        ? SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadItemIcon(definition.IconId)
                        : null;
                    slots[i].SetStack(stack, definition, icon, filledSlotColor, filledRimColor, textColor, accentColor);
                    BindSlotHover(slots[i].Root, stack, definition);
                }
                else
                {
                    slotItemIds[i] = string.Empty;
                    slots[i].SetEmpty(emptySlotColor, emptyRimColor, mutedTextColor);
                    UnbindSlotHover(slots[i].Root);
                }
            }
        }

        // 이름과 설명은 커서 옆 툴팁이 말한다 — 카드 키워드·상태이상 칩과 같은 창을 쓴다.
        private void BindSlotHover(RectTransform slot, PlayerBagItemStack stack, ConsumableItemDefinition definition)
        {
            if (slot == null)
            {
                return;
            }

            var hover = slot.GetComponent<SidebarRelicSlotHover>() ?? slot.gameObject.AddComponent<SidebarRelicSlotHover>();
            var name = definition != null ? definition.DisplayName : stack.ItemId;
            if (string.IsNullOrWhiteSpace(name))
            {
                name = "알 수 없는 물건";
            }

            var description = definition != null && !string.IsNullOrWhiteSpace(definition.Description)
                ? definition.Description
                : "효과 정보 없음";
            var countLine = stack.Count > 1 ? $"{stack.Count}개 보유 · 클릭해 사용" : "클릭해 사용";

            var lines = new System.Collections.Generic.List<ObjectInfoTooltipHudPresenter.Line>(3)
            {
                new ObjectInfoTooltipHudPresenter.Line("소모품", SidebarSlotTooltip.CategoryColor),
                new ObjectInfoTooltipHudPresenter.Line(description, SidebarSlotTooltip.BodyColor),
                new ObjectInfoTooltipHudPresenter.Line(countLine, SidebarSlotTooltip.CategoryColor)
            };
            hover.Bind(() => SidebarSlotTooltip.Show(name, lines), SidebarSlotTooltip.Hide);
        }

        private static void UnbindSlotHover(RectTransform slot)
        {
            var hover = slot != null ? slot.GetComponent<SidebarRelicSlotHover>() : null;
            hover?.Bind(null, null);
        }

        private static void SetText(TMP_Text target, string value, TMP_FontAsset font, Color color)
        {
            if (target == null)
            {
                return;
            }

            target.text = value ?? string.Empty;
            target.color = color;
            if (font != null)
            {
                target.font = font;
            }
        }

        private void EnsureSlotCapacity(int slotLimit)
        {
            if (slots == null || slots.Length == 0 || slots.Length >= slotLimit)
            {
                return;
            }

            var template = slots[slots.Length - 1].Root;
            if (template == null || template.parent == null)
            {
                return;
            }

            var expanded = new System.Collections.Generic.List<SlotBinding>(slots);
            while (expanded.Count < slotLimit)
            {
                var clone = UnityEngine.Object.Instantiate(template.gameObject, template.parent);
                clone.name = $"{template.name} (Extra {expanded.Count + 1})";
                var binding = new SlotBinding();
                binding.Bind(clone.transform as RectTransform);
                expanded.Add(binding);
            }

            slots = expanded.ToArray();
        }

        // 클릭 릴레이는 시각을 바꾸지 않는 런타임 동작 컴포넌트라 프리팹 저작 원칙(배치=프리팹)과
        // 충돌하지 않는다 — 버튼 스킨을 씌우지 않고 IPointerClickHandler만 단다.
        private void EnsureSlotClickRelay(int slotIndex)
        {
            var root = slots[slotIndex].Root;
            if (root == null)
            {
                return;
            }

            var relay = root.GetComponent<SidebarBagSlotClickRelay>();
            if (relay == null)
            {
                relay = root.gameObject.AddComponent<SidebarBagSlotClickRelay>();
            }

            relay.Configure(slotIndex, HandleSlotClicked);
        }

        private void EnsureBound()
        {
            if (contentRoot == null || slotGrid == null || slots == null || slots.Length == 0 || slots.Any(slot => slot.Root == null))
            {
                AutoBindFromHierarchy();
            }
        }

        private RectTransform FindRect(string targetName)
        {
            return GetComponentsInChildren<RectTransform>(includeInactive: true)
                .FirstOrDefault(rect => rect.name == targetName);
        }

        private TMP_Text FindText(string targetName)
        {
            return GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.name == targetName);
        }
    }
}
