namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X06 미련 — 사용 불가. 이 부적을 뽑을 시 사용 가능한 기력이 1 줄어듭니다. (옛 behaviorId `status.lingering`)</summary>
    public sealed class X06_Lingering : StatusCard
    {
        public override string Id => "X06";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.StatusLingering;
    }
}
