namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X07 지각 — 사용 불가. 손에 있는 동안 이동력이 1 줄어듭니다. (옛 behaviorId `status.tardiness`)</summary>
    public sealed class X07_Tardiness : StatusCard
    {
        public override string Id => "X07";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.StatusTardiness;
    }
}
