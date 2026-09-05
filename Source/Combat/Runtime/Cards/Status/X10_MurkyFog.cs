namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X10 궂은 안개 — 사용 불가. 턴 종료 시 손패에 있으면 실명 1턴을 받습니다. (옛 behaviorId `status.murky_fog`)</summary>
    public sealed class X10_MurkyFog : StatusCard
    {
        public override string Id => "X10";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.StatusMurkyFog;
    }
}
