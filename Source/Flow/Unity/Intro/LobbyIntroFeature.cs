namespace SeoulPlayup.Flow.Unity.Intro
{
    /// <summary>
    /// 로비 「게임 소개」 버튼 기능 스위치.
    /// <para>
    /// 2026-09-05 사용자 확정: <b>로비에서 「게임 소개」 버튼을 보이지 않게 한다.</b>
    /// 스토리 컷씬(<see cref="Story.StoryCutsceneFeature"/>)과 같은 문법 — 끄는 자리는 여기 하나이고,
    /// <see cref="LobbyController.WireIntroButton"/>이 이 값을 읽어 버튼을 감춘다(저작이 없을 때와 같은
    /// 「버튼 없음」 갈래라 새 분기가 없다). 버튼은 VerticalLayoutGroup 안이라 감추면 빈칸 없이 당겨진다.
    /// </para>
    /// <para>
    /// ⚠️ <b>저작물은 지우지 않았다.</b> <see cref="LobbyIntroPageAsset"/>·<see cref="LobbyIntroOverlayView"/>·
    /// 로비 프리팹의 <c>IntroButton</c>·<c>Assets/Data/Intro/</c>는 전부 그대로다. 되살리려면
    /// <see cref="Enabled"/>를 <c>true</c>로 되돌리는 것이 전부다.
    /// </para>
    /// </summary>
    public static class LobbyIntroFeature
    {
        /// <summary>「게임 소개」 버튼 노출 여부. <c>false</c>면 로비가 버튼을 감춘다.</summary>
        public const bool Enabled = false;
    }
}
