using System;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SidebarRelicCursePanelView : MonoBehaviour
    {
        [SerializeField] private SidebarSlotBinding[] slots = Array.Empty<SidebarSlotBinding>();

        /// <summary>
        /// 칸을 가져온 격자. 🔴 <b>이 격자의 자식이 아닌 것에는 절대 그리지 않는다</b>는 계약의 근거다 —
        /// 배선이 어긋난 순간에 그리면 anchors 0~1이 부모 전체로 늘어나 유물 그림 한 장이 화면을 덮는다
        /// (플레이 로드 직후 실제로 재현됐다). 순서가 아니라 <b>구조</b>가 막게 한다.
        /// </summary>
        [SerializeField] private RectTransform slotGrid;
        // 칩은 더 이상 solid 금색 사각형이 아니다: 어두운 남색 판 + 종류색 테두리로 바뀌었다. 아이콘이
        // 판을 꽉 채우므로 금색 바탕은 그림과 채도가 부딪혀 아이콘이 안 읽혔다(리뉴얼 판정).
        [SerializeField] private Color relicColor = new Color(0.16f, 0.18f, 0.31f, 0.96f);
        [SerializeField] private Color curseColor = new Color(0.23f, 0.15f, 0.25f, 0.96f);
        [SerializeField] private Color relicRimColor = new Color(0.86f, 0.75f, 0.48f, 0.95f);
        [SerializeField] private Color curseRimColor = new Color(0.95f, 0.48f, 0.72f, 0.95f);
        // 판이 어두워졌으므로 잉크도 패널 잉크 계열(밝은 쪽)로 뒤집는다.
        [SerializeField] private Color textColor = new Color(0.92f, 0.96f, 1f, 1f);
        [SerializeField] private Color mutedTextColor = new Color(0.70f, 0.78f, 0.90f, 1f);
        [SerializeField] private Color accentColor = new Color(0.86f, 0.75f, 0.48f, 1f);
        [SerializeField] private Color emptySlotColor = new Color(0.13f, 0.15f, 0.25f, 0.6f);
        // 빈 칸은 「없다」가 아니라 「아직 비었다」로 읽혀야 한다 — 선은 남기고 밝기만 낮춘다.
        [SerializeField] private Color emptyRimColor = new Color(0.55f, 0.60f, 0.80f, 0.45f);

        // 저작된 머리글(제목 + 보유 수). 없으면 종전대로 상세 줄만으로 산다.
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text statusText;

        public void AutoBindFromHierarchy()
        {
            titleText = FindText("Relic Title");
            statusText = FindText("Relic Status");
            var grid = GetComponentsInChildren<GridLayoutGroup>(includeInactive: true)
                .Select(layout => layout.transform as RectTransform)
                .FirstOrDefault();
            slotGrid = grid;
            slots = grid == null
                ? Array.Empty<SidebarSlotBinding>()
                : Enumerable.Range(0, grid.childCount).Select(i => SidebarSlotBinding.From(grid.GetChild(i) as RectTransform)).ToArray();
        }

        public void Refresh(CombatState state, TMP_FontAsset font)
        {
            EnsureBound();
            var items = state?.PlayerInventory?.RelicsAndCurses?.Items
                .OrderBy(item => item.Kind)
                .ThenBy(item => item.DisplayName)
                .ToArray() ?? Array.Empty<PlayerPermanentItemState>();

            var isEmpty = items.Length == 0;

            var relicCount = items.Count(item => item.Kind != PlayerPermanentItemKind.Curse);
            var curseCount = items.Length - relicCount;
            var visibleSlots = VisibleSlotCount(items.Length, slots.Length);
            // Q6: 패널은 「유물」이다. 저주는 카드 계통으로 넘어갔으므로 평시에는 세지 않고,
            // 그래도 하나라도 들어와 있으면 조용히 감추지 않고 함께 센다.
            SetHeaderText(titleText, "유물", font, accentColor);
            SetHeaderText(
                statusText,
                isEmpty ? "0개" : (curseCount > 0 ? $"유물 {relicCount} · 저주 {curseCount}" : $"{relicCount}개"),
                font,
                mutedTextColor);


            for (var i = 0; i < slots.Length; i++)
            {
                if (i >= visibleSlots)
                {
                    UnbindSlotHover(slots[i].Root);
                    SetSlotRim(slots[i].Root, new Color(0f, 0f, 0f, 0f));
                    SetSlotIcon(slots[i].Root, null, 1f);
                    SetSlotBadge(slots[i].Root, string.Empty, font);
                    slots[i].SetInactive();
                    continue;
                }

                if (i < items.Length)
                {
                    var item = items[i];
                    var isCurse = item.Kind == PlayerPermanentItemKind.Curse;
                    var chipColor = isCurse ? curseColor : relicColor;
                    var rimColor = isCurse ? curseRimColor : relicRimColor;
                    var iconAlpha = 1f;
                    if (!item.IsActive)
                    {
                        // 턴 제한 만료(청사초롱, T2 페이즈 B): 삭제하지 않고 비활성으로 남긴다(사용자 확정) — 칩만 흐려진다.
                        // 판은 그대로 두고 테두리와 그림만 죽인다: 판까지 투명해지면 격자에 구멍이 뚫린 것처럼 보인다.
                        rimColor.a *= 0.35f;
                        iconAlpha = 0.4f;
                    }

                    SetSlotRim(slots[i].Root, rimColor);

                    // 아트가 있으면 칩은 그림 한 장이 되고, 없으면 종전 2글자 텍스트가 그대로 선다
                    // (P1 계약 — 아이콘 없는 유물이 생겨도 화면이 깨지지 않는다).
                    var icon = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadItemIcon(item.IconId);
                    slots[i].Set(icon != null ? string.Empty : IconLabel(item), string.Empty, chipColor, textColor, mutedTextColor, font);
                    SetSlotIcon(slots[i].Root, icon, iconAlpha);
                    SetSlotBadge(slots[i].Root, BadgeText(item, state), font);
                    BindSlotHover(slots[i].Root, item, state);
                }
                else
                {
                    // 아직 안 채운 자리: 칸은 그대로 서 있고 테두리만 죽인다.
                    UnbindSlotHover(slots[i].Root);
                    SetSlotIcon(slots[i].Root, null, 1f);
                    SetSlotBadge(slots[i].Root, string.Empty, font);
                    slots[i].Set(string.Empty, string.Empty, emptySlotColor, textColor, mutedTextColor, font);
                    SetSlotRim(slots[i].Root, emptyRimColor);
                }
            }
        }

        // 이름·효과는 커서 옆 툴팁이 말한다 — 카드 키워드·상태이상 칩과 같은 창을 쓴다(패널 안에
        // 상세 줄을 깔면 판이 세로로 길어지고, 시선이 아이콘과 판 바닥을 왕복해야 한다).
        // 인벤토리가 바뀌면 칸↔아이템 대응이 밀리므로 Refresh마다 다시 묶는다.
        private void BindSlotHover(RectTransform slot, PlayerPermanentItemState item, CombatState state)
        {
            if (slot == null)
            {
                return;
            }

            var hover = slot.GetComponent<SidebarRelicSlotHover>() ?? slot.gameObject.AddComponent<SidebarRelicSlotHover>();

            // The chip plate must accept raycasts for pointer enter/exit to fire at all.
            var image = slot.GetComponent<Image>();
            if (image != null)
            {
                image.raycastTarget = true;
            }

            var kindLabel = item.Kind == PlayerPermanentItemKind.Curse ? "저주" : "유물";
            var name = string.IsNullOrWhiteSpace(item.DisplayName) ? kindLabel : item.DisplayName;
            var summary = string.IsNullOrWhiteSpace(item.EffectSummary) ? "효과 정보 없음" : item.EffectSummary;

            var lines = new System.Collections.Generic.List<ObjectInfoTooltipHudPresenter.Line>(3)
            {
                new ObjectInfoTooltipHudPresenter.Line(kindLabel, SidebarSlotTooltip.CategoryColor),
                new ObjectInfoTooltipHudPresenter.Line(summary, SidebarSlotTooltip.BodyColor)
            };

            // 진행도·잔여 턴은 칩 배지로도 보이지만, 배지는 두 글자라 무슨 수인지 알 수 없다.
            var badge = BadgeText(item, state);
            if (!string.IsNullOrEmpty(badge))
            {
                var badgeLine = item.DurationTurns > 0 && item.RemainingActiveTurns > 0
                    ? $"남은 턴 {badge}"
                    : $"진행 {badge}";
                lines.Add(new ObjectInfoTooltipHudPresenter.Line(badgeLine, SidebarSlotTooltip.CategoryColor));
            }

            if (!item.IsActive)
            {
                lines.Add(new ObjectInfoTooltipHudPresenter.Line("지금은 효과가 멈춰 있습니다", SidebarSlotTooltip.CategoryColor));
            }

            hover.Bind(() => SidebarSlotTooltip.Show(name, lines), SidebarSlotTooltip.Hide);
        }

        private static void UnbindSlotHover(RectTransform slot)
        {
            var hover = slot != null ? slot.GetComponent<SidebarRelicSlotHover>() : null;
            hover?.Bind(null, null);
        }

        /// <summary>
        /// 빈 칸도 격자로 그린다 — 보유분이 한 줄을 못 채우면 남는 자리를 빈 칸으로 채워 넣어
        /// 격자가 <b>직사각형</b>으로 서게 한다(가방 패널과 같은 규칙). 한 줄은 언제나 다 그린다.
        /// </summary>
        private static int VisibleSlotCount(int itemCount, int authoredSlots)
        {
            const int columns = 5;
            var rows = Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)columns));
            return Mathf.Min(rows * columns, authoredSlots);
        }

        private TMP_Text FindText(string targetName)
        {
            return GetComponentsInChildren<TMP_Text>(includeInactive: true)
                .FirstOrDefault(text => text.name == targetName);
        }

        private void EnsureBound()
        {
            if (slots == null || slots.Length == 0 || slots.Any(slot => slot.Root == null)
                || slotGrid == null || slots.Any(slot => slot.Root.parent != slotGrid))
            {
                AutoBindFromHierarchy();
            }
        }

        /// <summary>
        /// 칩 우하단 소형 카운터(T2 페이즈 C, 사용자 확정 2026-08-06) — 진행/잔여가 있는 유물만 단다.
        /// 코인 세탁기 = 다음 드로우까지 진행도(N/10), 턴 제한 유물(청사초롱) = 남은 턴. 그 외는 빈 값.
        /// </summary>
        private static string BadgeText(PlayerPermanentItemState item, CombatState state)
        {
            if (item == null)
            {
                return string.Empty;
            }

            if (item.TriggerKind == RelicTriggerKind.DrawPerCardsUsed && item.TriggerParam > 0 && state != null)
            {
                return $"{state.TotalActionCardsUsed % item.TriggerParam}/{item.TriggerParam}";
            }

            if (item.DurationTurns > 0 && item.RemainingActiveTurns > 0)
            {
                return item.RemainingActiveTurns.ToString();
            }

            return string.Empty;
        }

        /// <summary>그릴 자격이 있는 칸인가 — 묶어 둔 격자의 직계 자식만 통과한다.</summary>
        private bool IsBoundSlot(RectTransform slot)
        {
            return slot != null && slotGrid != null && slot.parent == slotGrid;
        }

        private static void SetHeaderText(TMP_Text target, string value, TMP_FontAsset font, Color color)
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

        /// <summary>
        /// 칩 테두리. 종류(유물/저주)와 활성 여부를 <b>색</b>이 아니라 <b>테두리</b>가 말하게 해서,
        /// 판 자체는 어두운 채로 두고 아이콘이 화면에서 가장 밝은 것이 되도록 한다.
        /// </summary>
        private void SetSlotRim(RectTransform slot, Color rimColor)
        {
            if (!IsBoundSlot(slot))
            {
                return;
            }

            var outline = slot.GetComponent<Outline>();
            if (outline == null)
            {
                outline = slot.gameObject.AddComponent<Outline>();
                outline.effectDistance = new Vector2(1.5f, 1.5f);
                outline.useGraphicAlpha = false;
            }

            outline.effectColor = rimColor;
            outline.enabled = true;
        }

        // 진행도 배지와 같은 결의 런타임 생성 자식 — 저작 칩(58x58)에는 그림 타깃이 없다.
        // 🔴 배지는 아이콘 <b>밖에</b> 그린다(파일럿 4라운드 판정): 아이콘 39장에 여백 제약을 걸지
        // 않기로 했으므로, 배지는 이 그림 위에 겹쳐 그리고 그림은 칩을 꽉 채운다.
        private void SetSlotIcon(RectTransform slot, Sprite sprite, float alpha)
        {
            if (!IsBoundSlot(slot))
            {
                return;
            }

            const string iconName = "Relic Icon";
            var iconTransform = slot.Find(iconName) as RectTransform;
            if (iconTransform == null)
            {
                if (sprite == null)
                {
                    return;
                }

                var go = new GameObject(iconName, typeof(RectTransform));
                iconTransform = go.GetComponent<RectTransform>();
                iconTransform.SetParent(slot, false);
                iconTransform.anchorMin = Vector2.zero;
                iconTransform.anchorMax = Vector2.one;
                iconTransform.offsetMin = new Vector2(3f, 3f);
                iconTransform.offsetMax = new Vector2(-3f, -3f);
                go.AddComponent<LayoutElement>().ignoreLayout = true;

                var created = go.AddComponent<Image>();
                created.preserveAspect = true;
                // 칩 판이 호버를 받아야 detail 줄이 뜬다 — 그림이 레이캐스트를 가로채면 안 된다.
                created.raycastTarget = false;
            }

            var image = iconTransform.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            image.sprite = sprite;
            image.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
            image.gameObject.SetActive(sprite != null);
        }

        // Runtime-created badge child per chip (authored prefab has no counter target). Reused across
        // refreshes by name lookup; empty text hides it.
        private void SetSlotBadge(RectTransform slot, string text, TMP_FontAsset font)
        {
            if (!IsBoundSlot(slot))
            {
                return;
            }

            const string badgeName = "Trigger Badge";
            var badgeTransform = slot.Find(badgeName) as RectTransform;
            if (badgeTransform == null)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                var go = new GameObject(badgeName, typeof(RectTransform));
                badgeTransform = go.GetComponent<RectTransform>();
                badgeTransform.SetParent(slot, false);
                badgeTransform.anchorMin = new Vector2(1f, 0f);
                badgeTransform.anchorMax = new Vector2(1f, 0f);
                badgeTransform.pivot = new Vector2(1f, 0f);
                badgeTransform.anchoredPosition = new Vector2(-2f, 1f);
                badgeTransform.sizeDelta = new Vector2(40f, 16f);
                go.AddComponent<LayoutElement>().ignoreLayout = true;

                var label = go.AddComponent<TextMeshProUGUI>();
                label.alignment = TextAlignmentOptions.BottomRight;
                label.enableAutoSizing = false;
                label.fontSize = 11f;
                label.fontStyle = FontStyles.Bold;
                label.raycastTarget = false;
            }

            var badge = badgeTransform.GetComponent<TMP_Text>();
            if (badge == null)
            {
                return;
            }

            if (font != null)
            {
                badge.font = font;
            }

            // 칩 판이 어두워졌으므로 배지도 밝은 금색으로 뒤집는다(어두운 남색은 판에 묻혔다).
            badge.color = new Color(0.95f, 0.86f, 0.62f, 0.98f);
            badge.text = text ?? string.Empty;
            badge.gameObject.SetActive(!string.IsNullOrEmpty(text));
            badgeTransform.SetAsLastSibling();
        }

        // Two characters rather than one: a single glyph ("달", "바", "은") is near-useless for telling relics
        // apart at a glance, and the 58x58 chip has room for two at the authored font size.
        private static string IconLabel(PlayerPermanentItemState item)
        {
            var name = item?.DisplayName;
            if (!string.IsNullOrEmpty(name))
            {
                return name.Length >= 2 ? name.Substring(0, 2) : name;
            }

            return item?.Kind == PlayerPermanentItemKind.Curse ? "C" : "R";
        }
    }
}
