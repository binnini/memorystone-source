namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A13 잔혼 공격 — 선택한 적에게 피해 {Damage}를 소멸된 부적 수({HitCount}번)만큼 반복합니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A13_Remnant : BasicAttackCard
    {
        public override string Id => "A13";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;
    }
}
