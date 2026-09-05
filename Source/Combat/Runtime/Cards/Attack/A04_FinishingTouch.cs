namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A04 몰아치는 공세 — 선택한 적에게 피해 {Damage}를 손의 공격 카드 수({HitCount}번)만큼 반복합니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A04_FinishingTouch : BasicAttackCard
    {
        public override string Id => "A04";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;
    }
}
