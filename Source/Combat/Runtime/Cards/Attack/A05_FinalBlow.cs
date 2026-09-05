namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A05 최후의 일격 — 남은 기력을 모두 소모해 선택한 적에게 피해 {Damage}를 줍니다. 소모한 기력만큼 커집니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A05_FinalBlow : BasicAttackCard
    {
        public override string Id => "A05";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;
    }
}
