using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 절차 패널(<see cref="UiProceduralPanel"/>) 위에 얹는 공용 장식 — 상단 타이틀 밴드 +
    /// 금색 모서리 틱 4개. 스타일 보드 67fe1d93 ⑩ Q-H 판정(ⓐ 절차 확장, 2026-08-19): 에셋
    /// 0으로 민무늬 라운드 팝업에 위계를 준다. 장식은 전부 <c>LayoutElement.ignoreLayout</c>이라
    /// 패널의 VerticalLayoutGroup 배치에 끼어들지 않고, 첫 sibling으로 넣어 내용 뒤에 깔린다.
    /// </summary>
    public static class UiPanelChrome
    {
        public static void Attach(RectTransform panel, Color accent, float titleBandHeight = 64f)
        {
            if (panel == null)
            {
                return;
            }

            var band = new GameObject("Panel Title Band", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            var bandRect = (RectTransform)band.transform;
            bandRect.SetParent(panel, worldPositionStays: false);
            bandRect.SetAsFirstSibling();
            bandRect.anchorMin = new Vector2(0f, 1f);
            bandRect.anchorMax = Vector2.one;
            bandRect.pivot = new Vector2(0.5f, 1f);
            bandRect.offsetMin = new Vector2(3f, -titleBandHeight);
            bandRect.offsetMax = new Vector2(-3f, -3f);
            band.GetComponent<LayoutElement>().ignoreLayout = true;
            var bandImage = band.GetComponent<Image>();
            bandImage.raycastTarget = false;
            UiProceduralPanel.Attach(
                bandImage,
                new Color(0f, 0f, 0f, 0.30f),
                new Color(accent.r, accent.g, accent.b, 0.45f),
                1f,
                12f);

            CreateCornerTick(panel, accent, left: true, top: true);
            CreateCornerTick(panel, accent, left: false, top: true);
            CreateCornerTick(panel, accent, left: true, top: false);
            CreateCornerTick(panel, accent, left: false, top: false);
        }

        private static void CreateCornerTick(RectTransform panel, Color accent, bool left, bool top)
        {
            var anchor = new Vector2(left ? 0f : 1f, top ? 1f : 0f);
            var tick = new GameObject("Panel Corner Tick", typeof(RectTransform), typeof(LayoutElement));
            var rect = (RectTransform)tick.transform;
            rect.SetParent(panel, worldPositionStays: false);
            rect.SetAsFirstSibling();
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = new Vector2(left ? 8f : -8f, top ? -8f : 8f);
            rect.sizeDelta = new Vector2(18f, 18f);
            tick.GetComponent<LayoutElement>().ignoreLayout = true;

            CreateTickBar(rect, accent, anchor, horizontal: true);
            CreateTickBar(rect, accent, anchor, horizontal: false);
        }

        private static void CreateTickBar(RectTransform tick, Color accent, Vector2 anchor, bool horizontal)
        {
            var bar = new GameObject(horizontal ? "H" : "V", typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)bar.transform;
            rect.SetParent(tick, worldPositionStays: false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = horizontal ? new Vector2(18f, 2.5f) : new Vector2(2.5f, 18f);
            var image = bar.GetComponent<Image>();
            image.color = accent;
            image.raycastTarget = false;
        }
    }
}
