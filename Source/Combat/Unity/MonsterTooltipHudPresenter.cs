using System.Collections.Generic;
using System.Text;
using SeoulPlayup.Combat.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Combat.Unity
{
    // Hover tooltip for monsters. Driven by MapCombatController.UpdateMonsterTooltip which calls
    // Show() every frame while the pointer is over a monster. The panel is rebuilt only when the
    // displayed content actually changes (signature-gated) so per-frame allocation stays bounded;
    // the mouse-follow positioning in Update() runs every frame regardless.
    internal sealed class MonsterTooltipHudPresenter : MonoBehaviour
    {
        // 치수·폰트·배경은 호버 정보창 3종 공용 정본(HoverTooltipStyle)에서 온다 — 예전에는 셋이
        // 제각각이라 같은 어휘의 정보창이 종류마다 다른 크기로 떴다(실플레이 피드백 ④).
        // A안(2026-09-05 3차 · 사용자 확정): 공용 400px보다 좁은 압축 카드. 남은 내용이 이름·HP·상태 칩뿐이라
        // 폭을 줄이고, HP는 숫자 대신 막대가 먼저 읽히게 한다.
        private const float PanelWidth = 340f;
        private const float HpBarHeightPx = 8f;
        private const float HpBarGapPx = 8f;
        private const float PanelCornerRadiusPx = 6f;
        private static readonly Color HpBarTrackColor = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color PanelBorderColor = new Color(1f, 1f, 1f, 0.08f);
        private const float PaddingH = HoverTooltipStyle.PaddingH;
        private const float PaddingV = HoverTooltipStyle.PaddingV;
        private const float NameRowHeight = HoverTooltipStyle.TitleRowHeight;
        private const float RowHeight = HoverTooltipStyle.RowHeight;
        private const float DetailRowHeight = HoverTooltipStyle.RowHeight;
        private const float DividerHeight = 18f;
        // 행 아이콘(보스 기물 상태 줄)은 28px 행 안에 들어가는 최대치. 예전 16px는 40px에서 읽히도록
        // 그린 카탈로그 아이콘을 형체가 안 보일 만큼 줄였다(2026-09-05 실플레이 #3).
        private const float IconSizePx = 24f;
        private const float IconGap = 6f;

        // 의도 스트라이프(채택안 B) · 상태 칩 격자.
        private const float IntentStripeWidthPx = 3f;
        // 칩은 아이콘이 주인공이다 — 아이콘 28px에 칩 높이가 따라간다. 단위 텍스트("2턴"·"준비") 폭은
        // 글자수 근사가 아니라 TMP 실측이다: 근사(11px/자)가 실제 폭에 못 미쳐 "2턴"이 두 줄로 접혀
        // 숫자만 남고 「턴」이 잘렸다(같은 피드백 #3).
        private const float ChipIconSizePx = 28f;
        private const float ChipHeightPx = 36f;
        private const float ChipGapPx = 6f;
        private const float ChipPaddingPx = 7f;

        private static readonly Color BackgroundColor = HoverTooltipStyle.BackgroundColor;
        private static readonly Color NameColor = Color.white;
        private static readonly Color HpColor = new Color(0.7f, 0.9f, 0.7f, 1f);
        private static readonly Color HpLowColor = new Color(0.95f, 0.5f, 0.45f, 1f);
        private static readonly Color IntentColorAttack = new Color(0.95f, 0.4f, 0.4f, 1f);
        private static readonly Color IntentColorChase = new Color(0.9f, 0.85f, 0.5f, 1f);
        private static readonly Color IntentColorNeutral = new Color(0.7f, 0.8f, 0.7f, 1f);
        private static readonly Color DividerColor = new Color(0.5f, 0.55f, 0.6f, 1f);
        private static readonly Color DimColor = new Color(0.62f, 0.64f, 0.68f, 1f);
        private static readonly Color DebuffColor = new Color(0.85f, 0.6f, 0.95f, 1f);

        private Canvas tooltipCanvas;
        private RectTransform panelRect;
        private Image panelBackground;
        private UiProceduralPanel panelSkin;

        // Running layout cursor (negative-down from the top padding) reused while rebuilding rows.
        private float cursorY;
        private string lastSignature;
        private bool isVisible;
        private StatusEffectIconCatalog iconCatalog;

        private void Awake()
        {
            EnsureBuilt();
        }

        // EditMode에서는 일반 MonoBehaviour의 Awake가 AddComponent 시점에 불리지 않는다 —
        // Show()가 첫 진입에서 지연 구축해 프레젠터 단위 테스트(T7-1)가 성립한다. 플레이 모드에서는
        // Awake가 먼저 지나가므로 no-op이다.
        private void EnsureBuilt()
        {
            if (tooltipCanvas != null && panelRect != null)
            {
                return;
            }

            BuildCanvas();
            BuildPanel();
            SetPanelVisible(false);
        }

        /// <summary>
        /// 보스 기물(철조각)일 때만 채워지는 부가 정보. 성숙까지 남은 턴은 규칙 계층만 알 수 있어
        /// (소유 보스의 프로필에 저작된 값이다) 호스트가 투영해 넘긴다 — 이 프리젠터는 규칙을 모른다.
        /// </summary>
        public readonly struct BossPropInfo
        {
            public BossPropInfo(int turnsUntilAbsorb, int maturityTurns, int blastRadius, int blastDamage)
            {
                TurnsUntilAbsorb = turnsUntilAbsorb;
                MaturityTurns = maturityTurns;
                BlastRadius = blastRadius;
                BlastDamage = blastDamage;
            }

            public int TurnsUntilAbsorb { get; }
            public int MaturityTurns { get; }
            public int BlastRadius { get; }
            public int BlastDamage { get; }
        }

        public void Show(
            MonsterRuntimeState monster,
            MonsterCatalogEntry entry,
            IReadOnlyList<ActiveEffect> allActiveEffects,
            StatusEffectIconCatalog statusIconCatalog = null,
            BossPropInfo? bossPropInfo = null)
        {
            EnsureBuilt();
            iconCatalog = statusIconCatalog;
            var effects = CollectMonsterEffects(monster.Id, allActiveEffects);
            var signature = BuildSignature(monster, entry, effects) + (iconCatalog != null ? "|ic" : "|nc")
                            + (bossPropInfo.HasValue ? $"|bp{bossPropInfo.Value.TurnsUntilAbsorb},{bossPropInfo.Value.BlastDamage}" : "|nbp");
            if (!isVisible || signature != lastSignature)
            {
                lastSignature = signature;
                Rebuild(monster, entry, effects, bossPropInfo);
            }

            SetPanelVisible(true);
            isVisible = true;
        }

        public void Hide()
        {
            isVisible = false;
            lastSignature = null;
            SetPanelVisible(false);
        }

        private void Update()
        {
            if (!isVisible) return;

            // 패널 높이가 내용에 따라 바뀌므로(Rebuild) 위치는 매 프레임 다시 놓는다.
            MoveToFixedTopRight();
        }

        /// <summary>화면 우상단 고정(§28.6 채택안 — 커서 추종은 카드 레인과 겹쳐 폐기). 맵 호버 툴팁
        /// 셋은 서로 배타로 표시되므로 같은 슬롯을 공유해도 겹치지 않는다.</summary>
        private void MoveToFixedTopRight()
        {
            if (tooltipCanvas == null || panelRect == null) return;

            var canvasRect = tooltipCanvas.GetComponent<RectTransform>();
            var halfCanvas = canvasRect.sizeDelta * 0.5f;
            var panelSize = panelRect.sizeDelta;
            panelRect.anchoredPosition = new Vector2(
                halfCanvas.x - panelSize.x - HoverTooltipStyle.PanelMargin,
                halfCanvas.y - HoverTooltipStyle.PanelMargin);
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("MonsterTooltipCanvas");
            canvasGo.transform.SetParent(transform, false);

            tooltipCanvas = canvasGo.AddComponent<Canvas>();
            tooltipCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the tutorial canvas (4500) so a hover-step tooltip is not darkened by the spotlight dim.
            tooltipCanvas.sortingOrder = 4550;

            HoverTooltipStyle.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());

            canvasGo.AddComponent<GraphicRaycaster>();
        }

        private void BuildPanel()
        {
            var canvasRect = tooltipCanvas.GetComponent<RectTransform>();

            var panelGo = new GameObject("TooltipPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvasRect, false);

            panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.sizeDelta = new Vector2(PanelWidth, 120f);
            panelRect.anchoredPosition = Vector2.zero;

            panelBackground = panelGo.GetComponent<Image>();
            panelBackground.color = BackgroundColor;
            panelBackground.raycastTarget = false;
            // 둥근 6px 모서리(A안). 스킨 머티리얼이 없으면 평면 색 그대로 폴백한다.
            panelSkin = UiProceduralPanel.Attach(panelBackground, BackgroundColor, PanelBorderColor, 1f, PanelCornerRadiusPx);
        }

        // Rebuilds every text row from scratch. Cheap enough because Show() only invokes this when the
        // signature changes (monster swap, hp/state/effect change), not on every hover frame.
        //
        // 2026-09-05 실플레이 3차 #4: 이 툴팁이 말하는 것은 <b>이름·HP·방어막</b>과 <b>지금 붙은 상태</b>뿐이다.
        // 이동력·「공격 의도」 줄·의도 색 띠·「어휘」 절은 전부 걷어냈다 — 의도는 겨냥 칸 오버레이와 이름표
        // 배지가, 특성·상태이상의 뜻은 배지 호버 툴팁이 이미 말하고 있어 여기서 되풀이할 이유가 없었다.
        private void Rebuild(
            MonsterRuntimeState monster,
            MonsterCatalogEntry entry,
            IReadOnlyList<ActiveEffect> effects,
            BossPropInfo? bossPropInfo = null)
        {
            ClearRows();
            cursorY = -PaddingV;

            // --- 머리줄: 이름(왼쪽) · HP 숫자(오른쪽, 방어막 동반) ---
            var displayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? monster.DefinitionId : entry.DisplayName;
            var hp = Mathf.Max(0, monster.Hp);
            var ratio = monster.MaxHp > 0 ? Mathf.Clamp01(hp / (float)monster.MaxHp) : 0f;
            var isLow = monster.MaxHp > 0 && hp <= monster.MaxHp * 0.34f;
            var hpColor = isLow ? HpLowColor : HpColor;
            var isBossProp = MonsterSpawnRoles.IsBossProp(monster.SpawnRole);
            var contentWidth = PanelWidth - PaddingH * 2f;

            var name = CreateText("Name", panelRect, HoverTooltipStyle.TitleFontSize, FontStyles.Bold, NameColor,
                new Vector2(PaddingH, cursorY), new Vector2(contentWidth, NameRowHeight));
            name.text = displayName;
            name.alignment = TextAlignmentOptions.MidlineLeft;
            name.textWrappingMode = TextWrappingModes.NoWrap;
            name.overflowMode = TextOverflowModes.Ellipsis;

            // 방어막(§21.8 제안 4)은 HP 옆이 제자리다 — 피해가 들어가기 전에 먼저 깎이는 수치이므로
            // 같은 줄에서 읽혀야 "지금 때리면 몇이 먹히는가"가 한눈에 계산된다.
            var blockSegment = monster.Block > 0 ? $"<color=#{ToHex(DimColor)}>  ·  방어막 {monster.Block}</color>" : string.Empty;
            var hpLabel = CreateText("Hp", panelRect, HoverTooltipStyle.RowFontSize, FontStyles.Normal, hpColor,
                new Vector2(PaddingH, cursorY), new Vector2(contentWidth, NameRowHeight));
            hpLabel.text = $"<b>{hp}</b><color=#{ToHex(DimColor)}>/{monster.MaxHp}</color>{blockSegment}";
            hpLabel.alignment = TextAlignmentOptions.MidlineRight;
            hpLabel.textWrappingMode = TextWrappingModes.NoWrap;
            // 이름이 길면 HP 숫자와 겹치지 않게 이름 폭을 숫자 폭만큼 양보한다.
            var hpWidth = Mathf.Ceil(hpLabel.GetPreferredValues(hpLabel.text, 0f, 0f).x) + 4f;
            name.rectTransform.sizeDelta = new Vector2(Mathf.Max(60f, contentWidth - hpWidth - 8f), NameRowHeight);
            cursorY -= NameRowHeight;

            // --- HP 막대: 저체력(34% 이하)이면 붉게 — 「지금 잡을 수 있나」가 숫자보다 먼저 읽힌다 ---
            cursorY -= HpBarGapPx * 0.5f;
            var track = CreateBar("HpTrack", PaddingH, cursorY, contentWidth, HpBarTrackColor);
            CreateBar("HpFill", PaddingH, cursorY, Mathf.Max(0f, contentWidth * ratio), hpColor);
            cursorY -= HpBarHeightPx + HpBarGapPx;

            // 보스 기물은 스스로 움직이지도 공격하지도 않는다 — 언제 터지고 무엇을 하는지 한 줄이면 된다.
            if (isBossProp)
            {
                RebuildBossPropBody(effects, bossPropInfo);
                FinishPanel();
                return;
            }

            // --- 상태 칩 (지속 상태이상 + 상시 문법 + 버프를 한 격자에) ---
            // 축은 "이 몬스터에 지금 붙어 있는 것 전부"다. 없으면 칩 영역 자체를 접는다(A안 — 「없음」 줄 폐지).
            var chips = CollectStatusChips(monster, entry, effects);
            if (chips.Count > 0)
            {
                AddStatusChipGrid(chips);
            }
            else
            {
                cursorY += HpBarGapPx; // 막대 아래 여백만 남긴다
            }

            FinishPanel();
        }

        private void FinishPanel()
        {
            cursorY -= PaddingV; // bottom padding
            panelRect.sizeDelta = new Vector2(PanelWidth, -cursorY);
            // 절차 패널은 rect 크기를 스스로 따라가지 못하는 경우가 있다(에디트 모드) — 크기를 바꿀 때마다 맞춘다.
            if (panelSkin != null) panelSkin.ResyncRectSize();
        }

        private RectTransform CreateBar(string name, float x, float top, float width, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(panelRect, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, top);
            rect.sizeDelta = new Vector2(width, HpBarHeightPx);
            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        /// <summary>
        /// 「상태」 칩 하나. 아이콘 + 단위 텍스트가 전부이고, 정의문은 어휘 절이 맡는다.
        /// <paramref name="GlossaryKeyword"/>가 비어 있지 않으면 어휘 절이 그 정의문을 싣는다.
        /// </summary>
        private readonly struct StatusChip
        {
            private StatusChip(StatusEffectKind? kind, string glyph, string unitText, Color tint, string glossaryKeyword)
            {
                Kind = kind;
                Glyph = glyph ?? string.Empty;
                UnitText = unitText ?? string.Empty;
                Tint = tint;
                GlossaryKeyword = glossaryKeyword ?? string.Empty;
            }

            /// <summary>카탈로그 아이콘을 쓰는 칩(지속 상태이상).</summary>
            public static StatusChip FromEffect(StatusEffectKind kind, string unitText, Color tint) =>
                new StatusChip(kind, string.Empty, unitText, tint, string.Empty);

            /// <summary>
            /// 저작 아이콘이 없는 칩(적 문법). 남의 상태이상 아이콘을 빌려 쓰지 않는다 —
            /// 아이콘 카탈로그는 40px에서 읽히도록 문화적 어휘까지 맞춘 17종 어휘이고, 맷집에
            /// 수호 아이콘을 붙이면 그 어휘가 거짓말을 하기 시작한다. 전용 아트가 나오기 전까지는
            /// 글리프 한 자로 둔다(<see cref="CreateStatusIcon"/>의 글리프 폴백과 같은 규약).
            /// </summary>
            public static StatusChip FromGlyph(string glyph, string unitText, Color tint, string glossaryKeyword) =>
                new StatusChip(null, glyph, unitText, tint, glossaryKeyword);

            public StatusEffectKind? Kind { get; }
            public string Glyph { get; }
            public string UnitText { get; }
            public Color Tint { get; }
            public string GlossaryKeyword { get; }
        }

        /// <summary>
        /// 이 몬스터에 <b>지금 붙어 있는 것 전부</b>를 칩으로 모은다 — 지속 상태이상, 상시 문법
        /// (약오름·맷집), 그리고 버프까지 한 축이다. 단위는 칩마다 자연스러운 것을 쓴다(Q2 확정 = 혼합):
        /// 지속은 턴 수, 약오름은 스택, 맷집은 준비/소진.
        /// </summary>
        private static List<StatusChip> CollectStatusChips(
            MonsterRuntimeState monster,
            MonsterCatalogEntry entry,
            IReadOnlyList<ActiveEffect> effects)
        {
            var chips = new List<StatusChip>(effects.Count + 2);

            foreach (var effect in effects)
            {
                // 🔴 힘은 <b>턴이 아니라 값</b>이다(2026-09-05). 힘의 남은 턴은 투영 갱신 주기라는
                // 구현 부산물이고(카운터가 0이 될 때까지 산다), 플레이어가 알고 싶은 것은 "+몇"이다.
                // 여기서 "2턴"을 찍으면 두 턴 뒤 사라진다는 거짓말이 된다.
                var unitText = effect.Kind == StatusEffectKind.Might
                    ? (effect.Amount > 0 ? $"+{effect.Amount}" : string.Empty)
                    : effect.RemainingTurns > 0 ? $"{effect.RemainingTurns}턴" : string.Empty;
                chips.Add(StatusChip.FromEffect(effect.Kind, unitText, DebuffColor));
            }

            // 힘 칩은 스택이 0이면 아직 아무것도 아니다 — 저작만으로 칩을 띄우면 "0스택"이라는
            // 빈 칩이 항상 떠 있게 된다. 담력 시험(strength.distance·2026-09-04)은 같은 힘 카운터를
            // 재설정형으로 쓰므로 이름표만 가른다 — 표기가 「약오름」으로 새면 어둠 먹기 선례의 재발이다.
            if (entry.HasAgitation && monster.AgitationStacks > 0)
            {
                // 🔴 수치는 위 힘 칩이 말한다(2026-09-05 사용자 확정) — 여기서 또 찍으면 같은 값이
                // 두 칩에 나란히 뜬다. 이 칩은 <b>왜 힘이 붙었는가</b>(약오름 / 담력 시험)만 말한다.
                var isStrengthDistance = string.Equals(
                    entry.AgitationConditionRef, "strength.distance", System.StringComparison.Ordinal);
                chips.Add(StatusChip.FromGlyph(
                    isStrengthDistance ? "담" : "약",
                    string.Empty,
                    IntentColorAttack,
                    string.Empty));
            }

            // 맷집은 반대로 <b>소진 상태도 정보</b>다("지금 때리면 온전히 들어간다"). 그래서 저작만
            // 있으면 항상 띄우고 단위로 상태를 가른다.
            if (entry.HasToughness)
            {
                chips.Add(StatusChip.FromGlyph(
                    "맷",
                    monster.ToughnessSpent ? "소진" : "준비",
                    monster.ToughnessSpent ? DimColor : IntentColorChase,
                    string.Empty));
            }

            return chips;
        }

        /// <summary>
        /// 보스 기물(철조각) 전용 본문. 플레이어가 이 툴팁에서 얻어야 하는 판단은 하나다 —
        /// <b>지금 부술 것인가, 두고 볼 것인가.</b> 그래서 남은 턴을 가장 크게 보여주고,
        /// 그 턴이 지나면 무슨 일이 벌어지는지를 한 줄로 붙인다.
        /// </summary>
        private void RebuildBossPropBody(IReadOnlyList<ActiveEffect> effects, BossPropInfo? bossPropInfo)
        {
            // 2026-09-05 실플레이 3차 #4-6 확정 문안: 「N턴 후 폭발하며 불가살에게 흡수되고 주위 플레이어에게 N 피해를 입힙니다.」
            if (bossPropInfo.HasValue)
            {
                var info = bossPropInfo.Value;
                var when = info.TurnsUntilAbsorb <= 0 ? "다음 적 턴에" : $"<b>{info.TurnsUntilAbsorb}턴</b> 후";
                AddRow(
                    $"{when} 폭발하며 불가살에게 흡수되고 주위 플레이어에게 <b>{Mathf.Max(0, info.BlastDamage)}</b> 피해를 입힙니다.",
                    RowHeight,
                    HoverTooltipStyle.RowFontSize,
                    FontStyles.Normal,
                    info.TurnsUntilAbsorb <= 0 ? HpLowColor : IntentColorAttack,
                    TextAlignmentOptions.TopLeft);
            }

            if (effects.Count > 0)
            {
                AddDivider("상태");
                foreach (var effect in effects)
                {
                    AddStatusEffectRow(effect.Kind, FormatStatusEffect(effect));
                }
            }
        }

        private void ClearRows()
        {
            for (var i = panelRect.childCount - 1; i >= 0; i--)
            {
                var child = panelRect.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }
        }

        private void AddRow(string text, float height, int fontSize, FontStyles style, Color color, TextAlignmentOptions alignment)
        {
            var contentWidth = PanelWidth - PaddingH * 2f;
            var label = CreateText("Row", panelRect, fontSize, style, color,
                new Vector2(PaddingH, cursorY), new Vector2(contentWidth, height));
            label.text = text;
            label.alignment = alignment;
            // Long patterns (e.g. 성난 황소's 밀치기/충돌 detail) wrap to multiple lines. Advance the cursor
            // by the actual rendered height so the next row never overlaps the wrapped text.
            var measured = MeasureRowHeight(label, text, contentWidth, height);
            label.rectTransform.sizeDelta = new Vector2(contentWidth, measured);
            cursorY -= measured;
        }

        // Returns the rendered height of a wrapped row, never below its minimum line height. Rich-text
        // tags (color/bold) are respected because TMP_Text.GetPreferredValues parses them.
        private static float MeasureRowHeight(TMP_Text label, string text, float width, float minHeight)
        {
            if (string.IsNullOrEmpty(text))
            {
                return minHeight;
            }

            var preferred = label.GetPreferredValues(text, width, 0f).y;
            return Mathf.Max(minHeight, Mathf.Ceil(preferred));
        }

        private void AddDivider(string label)
        {
            var contentWidth = PanelWidth - PaddingH * 2f;
            var text = CreateText("Divider", panelRect, HoverTooltipStyle.DetailFontSize, FontStyles.Normal, DividerColor,
                new Vector2(PaddingH, cursorY), new Vector2(contentWidth, DividerHeight));
            text.text = $"── {label} ──";
            text.alignment = TextAlignmentOptions.Center;
            cursorY -= DividerHeight;
        }

        /// <summary>
        /// 「상태」 칩 격자. 한 줄에 들어갈 만큼 담고 넘치면 다음 줄로 접는다 — 개수 상한을 두지
        /// 않는 이유는, 여기가 "지금 이 몬스터에 붙은 것 전부"를 말하는 유일한 자리이기 때문이다
        /// (마커 아이콘 행은 4개 + <c>+N</c>으로 자르지만, 그건 판 위에서 읽는 요약이다).
        /// </summary>
        private void AddStatusChipGrid(IReadOnlyList<StatusChip> chips)
        {
            var contentLeft = PaddingH;
            var contentRight = PanelWidth - PaddingH;
            var x = contentLeft;
            var rowTop = cursorY;
            var usedRow = false;

            foreach (var chip in chips)
            {
                var chipRect = CreateStatusChip(chip);
                var width = chipRect.sizeDelta.x;
                if (usedRow && x + width > contentRight)
                {
                    x = contentLeft;
                    rowTop -= ChipHeightPx + ChipGapPx;
                }

                chipRect.anchoredPosition = new Vector2(x, rowTop);
                x += width + ChipGapPx;
                usedRow = true;
            }

            cursorY = rowTop - ChipHeightPx - ChipGapPx;
        }

        /// <summary>
        /// 칩 하나 = 배경 rect 하나 밑에 아이콘·단위 텍스트. 자식으로 묶어 두어 격자가 칩을 통째로
        /// 옮길 수 있고, 폭은 만들고 나서 TMP 실측으로 확정한다(위치는 호출부가 놓는다).
        /// </summary>
        private RectTransform CreateStatusChip(StatusChip chip)
        {
            var backdrop = new GameObject("StatusChip", typeof(RectTransform), typeof(Image));
            backdrop.transform.SetParent(panelRect, false);
            var backRect = backdrop.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 1f);
            backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            var backImage = backdrop.GetComponent<Image>();
            backImage.color = new Color(chip.Tint.r, chip.Tint.g, chip.Tint.b, 0.16f);
            backImage.raycastTarget = false;

            var iconY = -(ChipHeightPx - ChipIconSizePx) * 0.5f;
            if (chip.Kind.HasValue)
            {
                CreateStatusIcon(chip.Kind.Value, backRect, ChipPaddingPx, iconY, ChipIconSizePx);
            }
            else
            {
                var glyph = CreateText("ChipGlyph", backRect, HoverTooltipStyle.RowFontSize, FontStyles.Bold, chip.Tint,
                    new Vector2(ChipPaddingPx, iconY), new Vector2(ChipIconSizePx, ChipIconSizePx));
                glyph.text = chip.Glyph;
                glyph.alignment = TextAlignmentOptions.Center;
            }

            var width = ChipPaddingPx * 2f + ChipIconSizePx;
            if (chip.UnitText.Length > 0)
            {
                var textX = ChipPaddingPx + ChipIconSizePx + IconGap;
                var label = CreateText("ChipUnit", backRect, HoverTooltipStyle.RowFontSize, FontStyles.Normal, chip.Tint,
                    new Vector2(textX, 0f), new Vector2(ChipHeightPx, ChipHeightPx));
                label.text = chip.UnitText;
                label.alignment = TextAlignmentOptions.Left;
                // 단위 텍스트는 한 줄이 계약이다 — 접히면 "2턴"이 "2"로 읽힌다.
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Overflow;
                var textWidth = Mathf.Ceil(label.GetPreferredValues(chip.UnitText, 0f, 0f).x) + 2f;
                label.rectTransform.sizeDelta = new Vector2(textWidth, ChipHeightPx);
                width += IconGap + textWidth;
            }

            backRect.sizeDelta = new Vector2(width, ChipHeightPx);
            return backRect;
        }

        // A single status-effect row: status icon (sprite from the shared catalog, glyph fallback) + text.
        private void AddStatusEffectRow(StatusEffectKind kind, string text)
        {
            const float rowH = DetailRowHeight;
            var iconY = cursorY - (rowH - IconSizePx) * 0.5f;
            CreateStatusIcon(kind, PaddingH, iconY, IconSizePx);

            var textX = PaddingH + IconSizePx + IconGap;
            var contentWidth = PanelWidth - textX - PaddingH;
            var label = CreateText("Row", panelRect, HoverTooltipStyle.RowFontSize, FontStyles.Normal, DebuffColor,
                new Vector2(textX, cursorY), new Vector2(contentWidth, rowH));
            label.text = text;
            label.alignment = TextAlignmentOptions.Left;
            var measured = MeasureRowHeight(label, text, contentWidth, rowH);
            label.rectTransform.sizeDelta = new Vector2(contentWidth, measured);
            cursorY -= measured;
        }

        // Mirrors the CardLane status dock: sprite from the catalog when available, otherwise a
        // colour-coded chip with the kind's glyph so the row still reads without authored art.
        private void CreateStatusIcon(StatusEffectKind kind, float x, float y, float sizePx)
            => CreateStatusIcon(kind, panelRect, x, y, sizePx);

        private void CreateStatusIcon(StatusEffectKind kind, RectTransform parent, float x, float y, float sizePx)
        {
            var go = new GameObject("StatusIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(sizePx, sizePx);

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;

            var sprite = iconCatalog != null ? iconCatalog.GetSprite(kind) : null;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.preserveAspect = true;
                image.color = Color.white;
                return;
            }

            image.color = StatusEffectIconStyle.BackgroundColor(kind, new Color(0.3f, 0.3f, 0.35f, 0.85f));
            var glyph = CreateText("Glyph", rect, Mathf.RoundToInt(sizePx * 0.72f), FontStyles.Bold, Color.white,
                Vector2.zero, new Vector2(sizePx, sizePx));
            glyph.text = StatusEffectIconStyle.Glyph(kind);
            glyph.alignment = TextAlignmentOptions.Center;
        }

        private static string ToHex(Color color) => ColorUtility.ToHtmlStringRGB(color);

        private static List<ActiveEffect> CollectMonsterEffects(string monsterId, IReadOnlyList<ActiveEffect> all)
        {
            var result = new List<ActiveEffect>();
            if (all == null || string.IsNullOrEmpty(monsterId)) return result;
            for (var i = 0; i < all.Count; i++)
            {
                var effect = all[i];
                if (effect.IsExpired || effect.TargetUnitId != monsterId)
                    continue;

                var existingIndex = result.FindIndex(candidate => candidate.Kind == effect.Kind);
                if (existingIndex < 0)
                {
                    result.Add(effect);
                    continue;
                }

                var existing = result[existingIndex];
                result[existingIndex] = new ActiveEffect(
                    effect.Type,
                    effect.Kind,
                    effect.TargetUnitId,
                    System.Math.Max(existing.RemainingTurns, effect.RemainingTurns),
                    System.Math.Max(existing.Amount, effect.Amount),
                    existing.SourceRef);
            }
            return result;
        }

        private static string BuildSignature(MonsterRuntimeState monster, MonsterCatalogEntry entry, IReadOnlyList<ActiveEffect> effects)
        {
            var sb = new StringBuilder(96);
            sb.Append(monster.Id).Append('|')
                .Append(entry.Id).Append('|')
                .Append(monster.Hp).Append('/').Append(monster.MaxHp).Append('|')
                .Append((int)monster.FsmState).Append('|')
                .Append((int)monster.ActivityState).Append('|')
                .Append((int)monster.Intent.Type).Append('|')
                .Append(monster.Intent.Distance).Append('|')
                .Append(monster.HasAttackIntent ? 1 : 0).Append('|')
                .Append(monster.AttackPatternIndex).Append('|')
                // 적 문법(T7-2) 동적 값 — 서명에 없으면 스택이 올라도 툴팁이 다시 그려지지 않는다
                // (사이드바 배지에서 실제로 밟은 함정과 같은 구조).
                .Append(monster.AgitationStacks).Append('|')
                .Append(monster.ToughnessSpent ? 1 : 0).Append('|')
                // 🔴 방어막은 <b>체력이 그대로인 채</b> 오르내린다(자기부여 패턴이 올리고, 피격이 깎는다).
                //    서명에 없으면 "방어막 N" 줄이 옛 숫자를 붙들거나, 0→N인데 아예 나타나지 않는다
                //    (2026-09-02 #2). 위 두 줄과 정확히 같은 부류의 누락이다.
                .Append(monster.Block).Append('|');
            for (var i = 0; i < effects.Count; i++)
            {
                var e = effects[i];
                sb.Append((int)e.Kind).Append(':').Append(e.Amount).Append(':').Append(e.RemainingTurns).Append(',');
            }
            return sb.ToString();
        }


        private static string FormatStatusEffect(ActiveEffect effect)
        {
            var name = LocalizeStatusKind(effect.Kind);
            var amount = effect.Amount > 0 && effect.Kind != StatusEffectKind.Strength ? $" {effect.Amount}" : string.Empty;
            return $"{name}{amount} ({effect.RemainingTurns}턴)";
        }

        private static TMP_Text CreateText(string name, RectTransform parent, int fontSize, FontStyles style, Color color, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = size;

            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            ApplyKoreanFont(text);
            return text;
        }

        private static void ApplyKoreanFont(TMP_Text label)
        {
            // DNFForgedBlade-Light SDF — covers Latin, numerics and the full Hangul syllable block.
            TooltipFontProvider.Apply(label);
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelRect != null)
                panelRect.gameObject.SetActive(visible);
        }

        // Player-facing threat read-out: only the two states that matter to the player (공격/추격) are
        // named; everything else (순찰/탐색/경계/복귀 …) collapses to 대기 so the panel shows 3 states.
        private static string LocalizeStatusKind(StatusEffectKind kind) => StatusEffectInfo.DisplayName(kind);
    }
}

