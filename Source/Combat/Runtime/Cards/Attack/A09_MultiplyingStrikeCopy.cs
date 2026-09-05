using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A09 증식 공격 복사본 — 덱과 손패에 있는 '증식 공격' 부적의 수({HitCount}번)만큼 선택한 적에게 피해 {Damage}를 반복합니다. 아지랑이. (옛 behaviorId `attack.multiplying_strike`)</summary>
    public sealed class A09_MultiplyingStrikeCopy : BasicAttackCard
    {
        public override string Id => "A09";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackMultiplyingStrike;

        /// <summary>원본과 같은 후속 규칙을 선언하지만 임시 카드(아지랑이)라 실제 복사는 일어나지 않는다 — 규칙 문면을 원본과 같게 둔다.</summary>
        public override IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions { get; } = new[]
        {
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionInjectCopy, CardIds.MultiplyingStrikeCopy)
        };
    }
}
