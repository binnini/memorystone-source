namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>D01 낡은 방어구 — 방어막 {Shield}를 얻습니다. (옛 behaviorId `defend.block`)</summary>
    public sealed class D01_OldArmor : BasicBlockCard
    {
        public override string Id => "D01";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.DefendBlock;
    }
}
