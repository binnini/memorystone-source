namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A00 공격의 기초 — 선택한 적에게 피해 {Damage}를 줍니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A00_BasicStrike : BasicAttackCard
    {
        public override string Id => "A00";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;
    }
}
