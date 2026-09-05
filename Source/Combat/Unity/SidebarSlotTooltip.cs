using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 사이드바 칸(가방 소모품 · 유물 칩)에 마우스를 올렸을 때 <b>커서 옆</b>에 뜨는 설명창.
    ///
    /// <para>카드 키워드 툴팁·상태이상 칩 툴팁과 <b>같은 프리젠터</b>(<see cref="ObjectInfoTooltipHudPresenter"/>)를
    /// 쓴다 — 같은 종류의 정보가 화면마다 다른 모양으로 뜨면 플레이어는 "다른 종류의 정보"로 읽는다.
    /// 종전에는 패널 안에 상세 줄을 한 줄 깔았는데, 그 줄 때문에 판이 세로로 길어지고 시선이
    /// 아이콘에서 판 바닥까지 왕복해야 했다.</para>
    ///
    /// <para>프리젠터는 두 패널이 한 개를 나눠 쓴다(동시에 두 칸을 가리킬 수 없으므로 배타적이다).
    /// 사이드바보다 위에 그려야 왼쪽 띠에 가리지 않는다 — 상태이상 도크와 같은 정렬 순서를 쓴다.</para>
    /// </summary>
    public static class SidebarSlotTooltip
    {
        // 상태이상 도크와 같은 값: 사이드바·메뉴 바를 포함한 모든 게임플레이 HUD 층 위.
        private const int SortingOrder = 1200;

        private static ObjectInfoTooltipHudPresenter presenter;

        public static Color TitleColor { get; } = new Color(0.93f, 0.85f, 0.62f, 1f);
        public static Color CategoryColor { get; } = new Color(0.70f, 0.78f, 0.90f, 1f);
        public static Color BodyColor { get; } = new Color(0.92f, 0.96f, 1f, 1f);

        public static void Show(string title, IReadOnlyList<ObjectInfoTooltipHudPresenter.Line> lines)
        {
            var hud = Ensure();
            if (hud == null)
            {
                return;
            }

            hud.Show(title, TitleColor, lines);
        }

        public static void Hide()
        {
            // Unity의 가짜 null을 피해 명시적으로 본다 — 씬이 바뀌면 호스트가 이미 파괴돼 있다.
            if (presenter != null)
            {
                presenter.Hide();
            }
        }

        private static ObjectInfoTooltipHudPresenter Ensure()
        {
            if (presenter != null)
            {
                return presenter;
            }

            var host = new GameObject("SidebarSlotTooltipHud");
            presenter = host.AddComponent<ObjectInfoTooltipHudPresenter>();
            presenter.SetSortingOrder(SortingOrder);
            // 한글 폰트는 Combat.Unity의 제공자가 넣는다(프리젠터는 Cards.Unity라 이 타입을 못 본다).
            presenter.KoreanFontApplier ??= TooltipFontProvider.Apply;
            return presenter;
        }
    }
}
