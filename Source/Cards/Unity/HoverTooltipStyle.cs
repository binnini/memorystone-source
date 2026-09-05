using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 맵 호버 정보창 3종(몬스터 · 필드 오브젝트 · 오브젝트 정보)의 공통 치수와 캔버스 규약
    /// (실플레이 피드백 ④).
    ///
    /// <para>🔴<b>스케일 모드가 핵심이다.</b> 세 프리젠터는 각자 <c>ConstantPixelSize</c>로 캔버스를
    /// 세우고 있었다. 게임 UI 본체는 <c>ScaleWithScreenSize</c>(2200×1238)라서, 해상도가 바뀌면
    /// 정보창만 상대적으로 커지거나 작아졌다 — 4K에서는 좁쌀, 720p에서는 화면의 절반. 세 곳 모두
    /// 이 함수를 지나가게 해서 본체와 같은 기준으로 늘어나게 한다.</para>
    ///
    /// <para>치수도 제각각이었다(폭 280/310/340/400, 폰트 10~21, 행 높이 22/24/28). 같은 어휘의
    /// 정보창이 종류마다 다른 크기로 뜨면 "다른 종류의 정보"로 읽힌다 — 여기 상수가 정본이다.</para>
    ///
    /// <para>이 타입이 Cards.Unity에 있는 이유: <c>ObjectInfoTooltipHudPresenter</c>가 이 어셈블리에
    /// 있고, Cards.Unity는 Combat을 참조하지 않는다(반대 방향만 있다). 공통 코드는 낮은 쪽에 둬야
    /// 양쪽이 볼 수 있다.</para>
    /// </summary>
    public static class HoverTooltipStyle
    {
        /// <summary>게임 UI 본체와 <b>같은</b> 기준 해상도. GameplaySceneContract의 값과 한 쌍이다.</summary>
        public static readonly Vector2 ReferenceResolution = new Vector2(2200f, 1238f);
        public const float CanvasMatch = 0.5f;

        // 값의 출처는 §28.6 우상단 고정 패널 A/B 판정이다 — 셋 중 그 한 벌만 실제로 판정을 받았으므로,
        // 통일의 기준을 그쪽으로 잡는다(작은 쪽에 맞추면 이미 확정된 가독성 판정을 되돌리게 된다).
        public const float PanelWidth = 400f;
        public const float PaddingH = 18f;
        public const float PaddingV = 16f;
        public const float TitleRowHeight = 30f;
        public const float RowHeight = 28f;
        public const float RowGap = 8f;
        public const int TitleFontSize = 21;
        public const int RowFontSize = 17;

        /// <summary>부연 줄(키워드 정의·수치 근거)의 크기. 본문보다 한 단 아래지만 여기서만 정한다.</summary>
        public const int DetailFontSize = 15;

        /// <summary>내용에 따라 패널이 들썩이지 않게 하는 최소 높이(§28.6 채택안).</summary>
        public const float MinPanelHeight = 180f;

        /// <summary>우상단 고정 배치의 화면 여백(px).</summary>
        public const float PanelMargin = 16f;

        public static readonly Color BackgroundColor = new Color(0.06f, 0.10f, 0.14f, 0.95f);

        /// <summary>호버 정보창 캔버스의 스케일 규약. 세 프리젠터가 전부 이 함수를 지난다.</summary>
        public static void ApplyCanvasScaler(CanvasScaler scaler)
        {
            if (scaler == null)
            {
                return;
            }

            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = CanvasMatch;
        }
    }
}
