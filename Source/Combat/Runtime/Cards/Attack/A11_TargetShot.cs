using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A11 얼어버려라 — 선택한 적에게 피해 {Damage}를 주고 2턴 동안 속박시킵니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A11_TargetShot : BasicAttackCard
    {
        public override string Id => "A11";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;

        /// <summary>맞은 적을 표식(다음 공격 우선 대상)하고 2턴 속박. 속박 턴수는 규칙이라 여기 상수다.</summary>
        public const int ImmobilizeTurns = 2;

        public override IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions { get; } = new[]
        {
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionApplyMark, string.Empty),
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionApplyImmobilize, ImmobilizeTurns.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
    }
}
