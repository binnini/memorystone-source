namespace SeoulPlayup.Flow.Unity.Story
{
    /// <summary>
    /// 스토리 컷씬(비주얼 노벨식 인트로/아웃트로) 기능 스위치.
    /// <para>
    /// 2026-09-01 사용자 확정: <b>이 게임은 스토리 컷씬을 쓰지 않는다.</b> 게임을 어떻게 시작하고
    /// 무엇을 하는 게임인지는 로비의 소개 페이지가 맡는다.
    /// </para>
    /// <para>
    /// 🔑 <b>끄는 자리가 여기 하나인 이유.</b> 컷씬을 재생할지 묻는 코드는 다섯 군데지만
    /// (<see cref="SceneFlowController.StartStageWithIntro"/>,
    /// <see cref="SceneFlowController.PlayStageOutroOrAdvance"/>, 로비의 스테이지 시작,
    /// 디버그 패널의 미리보기 둘) 다섯 곳이 전부 <b>같은 질문</b>을 한다 —
    /// "이 스테이지에 스토리가 붙어 있나?". 그래서 <see cref="StageDefinition.IntroStory"/> ·
    /// <see cref="StageDefinition.OutroStory"/>가 이 스위치가 꺼져 있을 때 null을 돌려주면,
    /// 다섯 곳 모두 <b>이미 구현돼 있고 이미 돌던</b> "스토리 없음" 갈래로 떨어진다.
    /// 새 분기를 만들지 않았으므로 빠뜨릴 호출부도 없다.
    /// </para>
    /// <para>
    /// ⚠️ <b>저작물은 지우지 않았다.</b> <c>Story_*.asset</c>·<c>Assets/Art/Story/</c>·
    /// <c>StoryCutscene.unity</c>(빌드 설정 포함)·<see cref="StoryCutsceneController"/>는 전부 그대로다.
    /// 되살리려면 <see cref="Enabled"/>를 <c>true</c>로 되돌리는 것이 전부다 — 컷씬 씬을 빌드 설정에서
    /// 빼지 않은 것도 같은 이유다(에디터에서만 멀쩡하고 빌드에서 죽는 함정을 만들지 않는다).
    /// </para>
    /// </summary>
    public static class StoryCutsceneFeature
    {
        /// <summary>
        /// 스토리 컷씬 재생 여부. <c>false</c>면 인트로·아웃트로가 모두 재생되지 않고
        /// 스테이지 진입/클리어가 컷씬 없이 곧바로 이어진다.
        /// </summary>
        public const bool Enabled = false;
    }
}
