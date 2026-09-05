using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Generic mouse-following info tooltip for non-combatant map entities that are not monsters or
    /// placed field objects — currently scout-revealed traps and treasure chests. Mirrors
    /// <see cref="MonsterTooltipHudPresenter"/> / <see cref="FieldObjectTooltipHudPresenter"/>
    /// (screen-space overlay canvas, Korean font, mouse follow + clamp) but takes an arbitrary
    /// title + body-line list so a single presenter can describe several object kinds. Rows are
    /// rebuilt only when the content signature changes so per-frame allocation stays bounded.
    /// </summary>
    public sealed class ObjectInfoTooltipHudPresenter : MonoBehaviour
    {
        // KoreanFontApplier (mirrors BottomCardHudView.KoreanFontApplier from the Stage 3 font
        // delegate inversion): lets the host inject the resolved Korean font without this presenter
        // depending on TooltipFontProvider (which stays in Combat.Unity after the Cards.Unity split).
        public System.Action<TMP_Text> KoreanFontApplier { get; set; }

        /// <summary>
        /// 이 인스턴스가 화면 우상단 고정 패널로 뜨는가(§28.6 채택안 — T2 실험을 A/B 판정으로 닫으며
        /// 스위치 없이 확정). 기본 false — 맵 호버 툴팁(MapCombatController 소유)은 켜고, 카드 키워드
        /// 툴팁처럼 커서 옆에 떠야 읽히는 맥락 인스턴스는 커서 추종을 유지한다.
        /// </summary>
        public bool FixedTopRightPlacement { get; set; }

        /// <summary>고정 패널의 화면 우상단 여백(px). 맵 호버 프레젠터 3종이 같은 슬롯을 공유한다
        /// (서로 배타 표시라 겹치지 않는다).</summary>
        public const float FixedPanelMargin = HoverTooltipStyle.PanelMargin;


        public readonly struct Line
        {
            public Line(string text, Color color)
            {
                Text = text;
                Color = color;
            }

            public string Text { get; }
            public Color Color { get; }
        }

        private const float PanelWidth = 340f;
        private const float PaddingH = 14f;
        private const float PaddingV = 12f;
        private const float TitleRowHeight = 30f;
        private const float RowHeight = 28f;
        private const float RowGap = 4f;
        private const float MouseOffsetX = 14f;
        private const float MouseOffsetY = 14f;

        // 우상단 고정 모드 메트릭(§28.6 채택 — A/B 판정으로 확정). 위치만이 아니라 가독성도 채택안의
        // 일부다: 폰트 한 단 상향(15→17 · 제목 18→21) · 행간 4→8 · 패널 최소 높이 180으로 내용에 따라
        // 패널이 들썩이지 않게 한다("고정 크기" 요청의 실질은 위치·크기 안정성이다).
        // 이 한 벌이 맵 호버 정보창 3종의 공용 정본이 됐다(HoverTooltipStyle) — 몬스터·필드 오브젝트
        // 툴팁도 같은 수치를 쓴다. 여기 상수는 그 정본을 가리키는 별칭이다.
        private const float FixedPanelWidth = HoverTooltipStyle.PanelWidth;
        private const float FixedPaddingH = HoverTooltipStyle.PaddingH;
        private const float FixedPaddingV = HoverTooltipStyle.PaddingV;
        private const float FixedRowGap = HoverTooltipStyle.RowGap;
        private const int FixedTitleFontSize = HoverTooltipStyle.TitleFontSize;
        private const int FixedRowFontSize = HoverTooltipStyle.RowFontSize;
        private const float FixedMinPanelHeight = HoverTooltipStyle.MinPanelHeight;
        private const int TitleFontSize = 18;
        private const int RowFontSize = 15;

        private static readonly Color BackgroundColor = HoverTooltipStyle.BackgroundColor;

        private Canvas tooltipCanvas;
        private RectTransform panelRect;
        private Image panelBackground;

        private float cursorY;
        private string lastSignature;
        private int sortingOrder = 4552; // above the tutorial spotlight dim (4500)
        private bool isVisible;

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            var wasInitialized = panelRect != null;
            BuildCanvas();
            BuildPanel();
            if (!wasInitialized)
            {
                SetPanelVisible(false);
            }
        }

        public void Show(string title, Color titleColor, IReadOnlyList<Line> lines)
        {
            EnsureInitialized();

            // 배치 모드가 시그니처에 들어간다 — 모드가 바뀐 채 같은 내용을 다시 보여도 그 모드의
            // 메트릭(폰트·행간·폭)으로 재조판되게 한다(호스트가 표시 중 모드를 바꿔도 낡지 않는다).
            var signature = (FixedTopRightPlacement ? "fixed|" : "cursor|") + BuildSignature(title, lines);
            if (!isVisible || signature != lastSignature)
            {
                lastSignature = signature;
                Rebuild(title, titleColor, lines);
            }

            SetPanelVisible(true);
            isVisible = true;
            if (FixedTopRightPlacement)
            {
                MoveToFixedTopRight();
            }
        }

        public void Hide()
        {
            isVisible = false;
            lastSignature = null;
            SetPanelVisible(false);
        }

        private void Update()
        {
            if (!isVisible)
            {
                return;
            }

            Vector2 mousePos;
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            mousePos = mouse.position.ReadValue();
#else
            mousePos = Input.mousePosition;
#endif
            if (FixedTopRightPlacement)
            {
                MoveToFixedTopRight();
            }
            else
            {
                MoveToMousePosition(mousePos);
            }
        }

        /// <summary>커서를 따라가지 않고 화면 우상단에 붙는다(§28.6 채택안) — 커서 추종이 카드 레인
        /// 등과 겹쳐 가려지던 문제의 해법. 매 프레임 다시 놓는 이유는 패널 높이가 내용에 따라
        /// Rebuild에서 바뀌기 때문이다(위치 계산이 sizeDelta에 의존한다).</summary>
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
                halfCanvas.x - panelSize.x - FixedPanelMargin,
                halfCanvas.y - FixedPanelMargin);
        }

        private void MoveToMousePosition(Vector2 screenPos)
        {
            if (tooltipCanvas == null || panelRect == null)
            {
                return;
            }

            var canvasRect = tooltipCanvas.GetComponent<RectTransform>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                canvasRect,
                screenPos,
                tooltipCanvas.worldCamera,
                out var localPoint);

            var panelSize = panelRect.sizeDelta;
            var canvasSize = canvasRect.sizeDelta;
            var halfCanvas = canvasSize * 0.5f;

            // Panel pivot is top-left. Prefer growing to the right of the cursor; if that would overflow the
            // right edge, flip to the left of the cursor so the panel always grows toward screen centre and
            // never gets clamped under edge HUD (e.g. the left menu bar occluding a status-effect tooltip).
            var rightX = localPoint.x + MouseOffsetX;
            var leftX = localPoint.x - MouseOffsetX - panelSize.x;
            localPoint.x = (rightX + panelSize.x <= halfCanvas.x) ? rightX : leftX;
            localPoint.y += MouseOffsetY;

            localPoint.x = Mathf.Clamp(localPoint.x, -halfCanvas.x, halfCanvas.x - panelSize.x);
            localPoint.y = Mathf.Clamp(localPoint.y, -halfCanvas.y + panelSize.y, halfCanvas.y);

            panelRect.anchoredPosition = localPoint;
        }

        /// <summary>Overrides the tooltip canvas sorting order so a hover tooltip can be raised above other
        /// HUD layers (e.g. the keyword tooltip binder lifts it above the card lane).</summary>
        public void SetSortingOrder(int order)
        {
            sortingOrder = order;
            if (tooltipCanvas != null)
            {
                tooltipCanvas.sortingOrder = sortingOrder;
            }
        }

        private void BuildCanvas()
        {
            if (tooltipCanvas != null)
            {
                return;
            }

            var canvasGo = new GameObject("ObjectInfoTooltipCanvas");
            canvasGo.transform.SetParent(transform, false);

            tooltipCanvas = canvasGo.AddComponent<Canvas>();
            tooltipCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // overrideSorting is required because the presenter host is often parented under another HUD
            // canvas (e.g. the CardLane status dock). Without it a nested Canvas ignores its sortingOrder
            // and inherits the parent draw order, so the tooltip gets occluded by the sidebar / card lane.
            tooltipCanvas.overrideSorting = true;
            tooltipCanvas.sortingOrder = sortingOrder;

            HoverTooltipStyle.ApplyCanvasScaler(canvasGo.AddComponent<CanvasScaler>());

            canvasGo.AddComponent<GraphicRaycaster>();
        }

        private void BuildPanel()
        {
            if (panelRect != null)
            {
                return;
            }

            var canvasRect = tooltipCanvas.GetComponent<RectTransform>();

            var panelGo = new GameObject("TooltipPanel", typeof(RectTransform), typeof(Image));
            panelGo.transform.SetParent(canvasRect, false);

            panelRect = panelGo.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.sizeDelta = new Vector2(PanelWidth, 100f);
            panelRect.anchoredPosition = Vector2.zero;

            panelBackground = panelGo.GetComponent<Image>();
            panelBackground.color = BackgroundColor;
            panelBackground.raycastTarget = false;
        }

        private void Rebuild(string title, Color titleColor, IReadOnlyList<Line> lines)
        {
            ClearRows();
            var fixedMode = FixedTopRightPlacement;
            var width = fixedMode ? FixedPanelWidth : PanelWidth;
            var paddingV = fixedMode ? FixedPaddingV : PaddingV;
            cursorY = -paddingV;

            AddRow(title, TitleRowHeight, fixedMode ? FixedTitleFontSize : TitleFontSize, FontStyles.Bold, titleColor, width);

            if (lines != null)
            {
                for (var i = 0; i < lines.Count; i++)
                {
                    AddRow(lines[i].Text, RowHeight, fixedMode ? FixedRowFontSize : RowFontSize, FontStyles.Normal, lines[i].Color, width);
                }
            }

            cursorY -= paddingV; // bottom padding
            var height = fixedMode ? Mathf.Max(FixedMinPanelHeight, -cursorY) : -cursorY;
            panelRect.sizeDelta = new Vector2(width, height);
        }

        private void ClearRows()
        {
            for (var i = panelRect.childCount - 1; i >= 0; i--)
            {
                var child = panelRect.GetChild(i).gameObject;
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

        private void AddRow(string text, float minHeight, int fontSize, FontStyles style, Color color, float panelWidth)
        {
            var fixedMode = FixedTopRightPlacement;
            var paddingH = fixedMode ? FixedPaddingH : PaddingH;
            var contentWidth = panelWidth - paddingH * 2f;
            var label = CreateText("Row", panelRect, fontSize, style, color,
                new Vector2(paddingH, cursorY), new Vector2(contentWidth, minHeight));
            label.text = text;
            label.ForceMeshUpdate();

            var preferredHeight = Mathf.Ceil(label.preferredHeight);
            var resolvedHeight = Mathf.Max(minHeight, preferredHeight);
            label.rectTransform.sizeDelta = new Vector2(contentWidth, resolvedHeight);
            cursorY -= resolvedHeight + (fixedMode ? FixedRowGap : RowGap);
        }

        private static string BuildSignature(string title, IReadOnlyList<Line> lines)
        {
            var sb = new System.Text.StringBuilder(96);
            sb.Append(title).Append('|');
            if (lines != null)
            {
                for (var i = 0; i < lines.Count; i++)
                {
                    sb.Append(lines[i].Text).Append('\n');
                }
            }

            return sb.ToString();
        }

        private TMP_Text CreateText(string name, RectTransform parent, int fontSize, FontStyles style, Color color, Vector2 anchoredPos, Vector2 size)
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

        private void ApplyKoreanFont(TMP_Text label)
        {
            // DNFForgedBlade-Light SDF — covers Latin, numerics and the full Hangul syllable block.
            // Host-injected (see KoreanFontApplier) so this type doesn't depend on TooltipFontProvider.
            KoreanFontApplier?.Invoke(label);
        }

        private void SetPanelVisible(bool visible)
        {
            if (panelRect != null)
            {
                panelRect.gameObject.SetActive(visible);
            }
        }
    }
}
