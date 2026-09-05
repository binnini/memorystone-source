namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>U01 다시 뽑기 — 모든 손패를 버리고 같은 수만큼 다시 뽑습니다. (옛 behaviorId `utility.redraw`)</summary>
    public sealed class U01_Redraw : UtilityCard
    {
        public override string Id => "U01";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.UtilityRedraw;
    }
}
