using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 「카드 한 장을 골랐다 → 정말 할 것인가」 확인 화면의 <b>공용 규격</b>
    /// (캠핑카 연마 확인 · 잡화점 제거 확인, 사용자 지적 2026-08-31).
    ///
    /// <para>
    /// 🔴 두 화면은 <b>다른 뷰 클래스</b>에 각각 살고 있다(<see cref="ServiceObjectPopupView"/> ·
    /// <see cref="ShopPopupView"/>) — 배경도 팝업 골격도 달라 한 컴포넌트로 합치기 어렵다. 그래서
    /// 합치는 대신 <b>숫자만 한곳에 모았다</b>: 실제로 갈렸던 것은 구조가 아니라 값이었다
    /// (패널 900×760 ↔ 760×740 → 버튼 폭 414 ↔ 344, 사용자가 「돌아가기 크기가 다르다」로 발견).
    /// </para>
    ///
    /// <para>
    /// 🔑 <b>버튼 폭을 패널 폭에서 유도하지 않는다.</b> 종전에는 두 버튼이 패널 안쪽 폭을 나눠 가져
    /// 패널이 커지면 버튼도 커졌다 — 그래서 내용에 맞춰 패널 크기를 정하는 순간 버튼이 갈렸다.
    /// 여기서는 버튼 폭을 <b>고정</b>하고 가운데로 모으므로, 패널이 카드 한 장짜리든 두 장짜리든
    /// 버튼은 언제나 같은 물건으로 보인다.
    /// </para>
    /// </summary>
    public static class CardConfirmPanelSpec
    {
        /// <summary>세로 여백·안쪽 여백 — 두 화면이 같은 리듬으로 서게 한다.</summary>
        public static readonly RectOffset Padding = new RectOffset(28, 28, 24, 24);

        public const float RowSpacing = 10f;

        /// <summary>패널 높이. 폭은 내용이 정한다(카드 한 장 ↔ 두 장 + 화살표).</summary>
        public const float PanelHeight = 760f;

        /// <summary>고른 카드를 세우는 자리 — 연마의 전/후 카드와 제거의 카드가 같은 크기다.</summary>
        public static readonly Vector2 CardHolderSize = new Vector2(320f, 470f);

        public const float TitleFontSize = 32f;
        public const float CaptionFontSize = 24f;

        // ── 버튼(사용자가 발견한 축) ──────────────────────────────────────
        // 2026-09-01 #13: 300×58이 「너무 크다」는 사용자 판정으로 200×52로 내렸다.
        // 🔑 한 곳만 고치면 두 화면이 함께 따라온다 — 한쪽만 줄이면 다시 갈린다(이 파일의 존재 이유).
        //    기준선은 카드 자리 폭(CardHolderSize.x = 320): 버튼이 카드보다 넓으면 확인 화면에서
        //    <b>고른 카드보다 버튼이 커 보인다</b>. 게이트가 그 비례를 잠근다.
        public const float ButtonWidth = 200f;
        public const float ButtonHeight = 52f;
        public const float ButtonFontSize = 22f;
        public const float ButtonSpacing = 16f;
    }
}
