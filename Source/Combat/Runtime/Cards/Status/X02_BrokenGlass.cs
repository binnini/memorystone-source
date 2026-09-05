namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X02 깨진 유리 — 사용 불가. 턴 종료 시 손패에 있으면 피해 {Damage}를 받습니다. (옛 behaviorId `status.broken_glass`)</summary>
    public sealed class X02_BrokenGlass : StatusCard
    {
        public override string Id => "X02";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.StatusBrokenGlass;
    }
}
