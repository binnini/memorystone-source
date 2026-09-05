namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>F05 콩콩탄탄 — 턴 시작 시 {Shape}에 {Duration}턴 동안 매 턴 피해 {Damage}를 {HitCount}번 주는 장판을 배치합니다. (옛 behaviorId `field.damage`)</summary>
    public sealed class F05_BounceBomb : FieldObjectCard
    {
        public override string Id => "F05";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.FieldDamage;
    }
}
