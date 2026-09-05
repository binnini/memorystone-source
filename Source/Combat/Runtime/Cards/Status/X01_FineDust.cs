namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>X01 미세먼지 — 사용 불가. 아지랑이. (옛 behaviorId `status.fine_dust`)</summary>
    public sealed class X01_FineDust : StatusCard
    {
        public override string Id => "X01";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.StatusFineDust;
    }
}
