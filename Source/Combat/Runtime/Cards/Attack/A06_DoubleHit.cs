namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A06 두 번 치기 — 선택한 적에게 피해 {Damage}를 {HitCount}번 반복합니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A06_DoubleHit : BasicAttackCard
    {
        public override string Id => "A06";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;
    }
}
