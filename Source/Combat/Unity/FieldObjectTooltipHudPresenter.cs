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
    /// <summary>
    /// Mouse-following tooltip for a hovered placed field object. Mirrors
    /// <see cref="MonsterTooltipHudPresenter"/> (screen-space overlay canvas, Korean font, mouse follow +
    /// clamp). Content is derived entirely from the runtime <see cref="FieldObject"/>
    /// (Kind / Value / Radius / RemainingTurns), so it needs no card-catalog lookup.
    /// </summary>
    internal sealed class FieldObjectTooltipHudPresenter : MonoBehaviour
    {
        // 치수·폰트·배경은 호버 정보창 3종 공용 정본(HoverTooltipStyle)에서 온다(실플레이 피드백 ④).
        // 종류를 가르는 것은 크기가 아니라 제목 색이다.
        private const float PanelWidth = HoverTooltipStyle.PanelWidth;
        private const float PaddingH = HoverTooltipStyle.PaddingH;
        private const float PaddingV = HoverTooltipStyle.PaddingV;
        private const float RowHeight = HoverTooltipStyle.RowHeight;
        private const float RowGap = HoverTooltipStyle.RowGap;

        private static readonly Color BackgroundColor = HoverTooltipStyle.BackgroundColor;
        private static readonly Color TitleColor = new Color(0.55f, 0.95f, 0.65f, 1f);
        private static readonly Color EffectColor = new Color(0.9f, 0.92f, 0.9f, 1f);
        private static readonly Color TurnsColor = new Color(1f, 0.9f, 0.5f, 1f);
        private static readonly Color RangeColor = new Color(0.7f, 0.85f, 0.85f, 1f);

        private Canvas tooltipCanvas;
        private RectTransform panelRect;
        private Image panelBackground;
        private TMP_Text titleText;
        private TMP_Text effectText;
        private TMP_Text turnsText;
        private TMP_Text rangeText;

        private bool isVisible;

        private void Awake()
        {
            BuildCanvas();
            BuildPanel();
            SetPanelVisible(false);
        }

        public void Show(FieldObject fieldObject, string sourceCardName = null)
        {
            // Prefer the placing card's display name (resolved by the caller from the card catalog via
            // FieldObject.VisualRef); the generic kind title remains the fallback for card-less fields.
            titleText.text = string.IsNullOrWhiteSpace(sourceCardName)
                ? ResolveTitle(fieldObject.Kind)
                : sourceCardName;
            effectText.text = ResolveEffectLine(fieldObject);
            turnsText.text = $"남은 턴: {fieldObject.RemainingTurns}";
            rangeText.text = $"범위: 반경 {fieldObject.Radius}";

            var panelHeight = PaddingV
                + RowHeight + RowGap   // title
                + RowHeight + RowGap   // effect
                + RowHeight + RowGap   // turns
                + RowHeight            // range
                + PaddingV;
            panelRect.sizeDelta = new Vector2(PanelWidth, panelHeight);

            SetPanelVisible(true);
            isVisible = true;
        }

        public void Hide()
        {
            isVisible = false;
            SetPanelVisible(false);
        }

        private void Update()
        {
            if (!isVisible)
            {
                return;
            }

            // 패널 높이가 내용에 따라 바뀌므로(Rebuild) 위치는 매 프레임 다시 놓는다.
            MoveToFixedTopRight();
        }

        /// <summary>화면 우상단 고정(§28.6 채택안 — 커서 추종은 카드 레인과 겹쳐 폐기). 맵 호버 툴팁
        /// 셋은 서로 배타로 표시되므로 같은 슬롯을 공유해도 겹치지 않는다.</summary>
        private void MoveToFixedTopRight()
        {
            if (tooltipCanvas == null || panelRect == null)
            {
                return;
            }

            var canvasRect = tooltipCanvas.GetComponent<RectTransform>();
            var halfCanvas = canvasRect.sizeDelta * 0.5f;
            var panelSize = panelRect.sizeDelta;
            panelRect.anchoredPosition = new Vector2(
                halfCanvas.x - panelSize.x - HoverTooltipStyle.PanelMargin,
                halfCanvas.y - HoverTooltipStyle.PanelMargin);
        }

        private void BuildCanvas()
        {
            var canvasGo = new GameObject("FieldObjectTooltipCanvas");
            canvasGo.transform.SetParent(transform, false);

            tooltipCanvas = canvasGo.AddComponent<Canvas>();
            tooltipCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            tooltipCanvas.sortingOrder = 4551; // above the tutorial spotlight dim (4500)

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
            panelRect.sizeDelta = new Vector2(PanelWidth, 110f);
            panelRect.anchoredPosition = Vector2.zero;

            panelBackground = panelGo.GetComponent<Image>();
            panelBackground.color = BackgroundColor;
            panelBackground.raycastTarget = false;

            float y = -PaddingV;
            float contentWidth = PanelWidth - PaddingH * 2f;

            titleText = CreateText("TitleText", panelRect, HoverTooltipStyle.TitleFontSize, FontStyles.Bold, TitleColor,
                new Vector2(PaddingH, y), new Vector2(contentWidth, RowHeight));
            y -= RowHeight + RowGap;

            effectText = CreateText("EffectText", panelRect, HoverTooltipStyle.RowFontSize, FontStyles.Normal, EffectColor,
                new Vector2(PaddingH, y), new Vector2(contentWidth, RowHeight));
            y -= RowHeight + RowGap;

            turnsText = CreateText("TurnsText", panelRect, HoverTooltipStyle.RowFontSize, FontStyles.Normal, TurnsColor,
                new Vector2(PaddingH, y), new Vector2(contentWidth, RowHeight));
            y -= RowHeight + RowGap;

            rangeText = CreateText("RangeText", panelRect, HoverTooltipStyle.RowFontSize, FontStyles.Normal, RangeColor,
                new Vector2(PaddingH, y), new Vector2(contentWidth, RowHeight));
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
            {
                panelRect.gameObject.SetActive(visible);
            }
        }

        private static string ResolveTitle(FieldObjectKind kind)
        {
            switch (kind)
            {
                case FieldObjectKind.FogReveal:
                    return "정찰 장판";
                case FieldObjectKind.FieldDamage:
                    return "피해 장판";
                case FieldObjectKind.ConditionalHeal:
                    return "회복 장판";
                case FieldObjectKind.MassImmobilize:
                    return "섬광 장판";
                case FieldObjectKind.LifestealDamage:
                    return "흡수 장판";
                default:
                    return "필드 오브젝트";
            }
        }

        // Wording mirrors the actual tick handlers (CombatState.FieldObjectCardEffects): player-placed
        // damage fields hit monsters only, ConditionalHeal heals the player standing inside, Lifesteal
        // heals by damage actually dealt, MassImmobilize re-applies 속박 each tick. The 범위 row below
        // already shows the radius, so the effect line names the target instead of repeating "범위 내".
        private static string ResolveEffectLine(FieldObject fieldObject)
        {
            switch (fieldObject.Kind)
            {
                case FieldObjectKind.FogReveal:
                    return "범위의 시야를 공개합니다";
                case FieldObjectKind.FieldDamage:
                    return fieldObject.HitsPerTick > 1
                        ? $"매 턴 몬스터에게 피해 {fieldObject.Value}를 {fieldObject.HitsPerTick}번"
                        : $"매 턴 몬스터에게 피해 {fieldObject.Value}";
                case FieldObjectKind.ConditionalHeal:
                    return $"위에 서 있으면 매 턴 {fieldObject.Value} 회복";
                case FieldObjectKind.MassImmobilize:
                    return "매 턴 몬스터를 속박";
                case FieldObjectKind.LifestealDamage:
                    return $"매 턴 몬스터에게 피해 {fieldObject.Value} · 준 만큼 회복";
                default:
                    return string.Empty;
            }
        }
    }
}


