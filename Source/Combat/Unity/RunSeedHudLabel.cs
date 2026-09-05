
using TMPro;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 화면 구석의 런 시드 표시(seed-determinism-handoff P1-2). 재현 신고를 받으려면 플레이어가
    /// 시드를 볼 수 있어야 한다. 자기 오버레이 캔버스를 세우므로 씬 저작이 없고, 레이캐스트를
    /// 받지 않아 입력에 끼어들지 않는다. 일시정지 메뉴 안에 넣지 않은 이유는 그 파일이 다른
    /// 트랙과 동시 편집 중이라서다 — 그쪽이 안정되면 옮겨도 된다.
    /// </summary>
    public sealed class RunSeedHudLabel : MonoBehaviour
    {
        private const string ObjectName = "RunSeedHudLabel";
        // 일시정지 메뉴(CombatPauseMenuController.CanvasSortingOrder) 아래, 일반 HUD 위.
        private const int CanvasSortingOrder = 900;

        private TMP_Text label;

        /// <summary>시드 라벨을 만들거나 갱신한다. 게임플레이 씬 수명과 함께 사라진다.</summary>
        public static RunSeedHudLabel Show(int seed)
        {
            var existing = FindFirstObjectByType<RunSeedHudLabel>(FindObjectsInactive.Include);
            var instance = existing != null ? existing : Create();
            instance.label.text = $"시드 {seed}";
            return instance;
        }

        private static RunSeedHudLabel Create()
        {
            var root = new GameObject(ObjectName, typeof(RectTransform));
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = CanvasSortingOrder;
            var instance = root.AddComponent<RunSeedHudLabel>();

            var rect = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = new Vector2(-12f, 8f);
            rect.sizeDelta = new Vector2(320f, 24f);

            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            var font = KoreanFontProvider.Load();
            if (font != null)
            {
                text.font = font;
            }

            text.fontSize = 16f;
            text.alignment = TextAlignmentOptions.BottomRight;
            text.color = new Color(1f, 1f, 1f, 0.55f);
            text.raycastTarget = false;
            instance.label = text;
            return instance;
        }
    }
}
