namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X09 부정 탄 부적 — 사용 불가. 턴 종료 시 손패에 있으면 쇠약 1턴을 받습니다. (옛 behaviorId `status.cursed_charm`)</summary>
    public sealed class X09_CursedCharm : StatusCard
    {
        public override string Id => "X09";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.StatusCursedCharm;
    }
}
