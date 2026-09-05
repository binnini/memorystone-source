namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X08 골칫거리 — 사용 불가. (옛 behaviorId `status.nuisance`)</summary>
    public sealed class X08_Nuisance : StatusCard
    {
        public override string Id => "X08";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.StatusNuisance;
    }
}
