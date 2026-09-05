namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A02 가속 타격 — 선택한 적에게 피해 {Damage}를 줍니다. 이번 턴 이동한 칸 수만큼 커집니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A02_MoveLinkedStrike : BasicAttackCard
    {
        public override string Id => "A02";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;
    }
}
