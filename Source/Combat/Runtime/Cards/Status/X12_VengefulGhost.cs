namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X12 원귀 — 사용 불가. 손에 있는 동안 허점을 1턴 간 부여받습니다. (옛 behaviorId `status.vengeful_ghost`)</summary>
    public sealed class X12_VengefulGhost : StatusCard
    {
        public override string Id => "X12";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.StatusVengefulGhost;
    }
}
