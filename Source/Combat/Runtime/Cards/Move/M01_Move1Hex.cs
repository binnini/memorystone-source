namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>M01 1칸 이동 — 최대 {Range}칸 이동합니다. (옛 behaviorId `move.basic`)</summary>
    public sealed class M01_Move1Hex : BasicMoveCard
    {
        public override string Id => "M01";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.MoveBasic;
    }
}
