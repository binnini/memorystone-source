using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A08 증식 공격 — 덱과 손패에 있는 '증식 공격' 부적의 수({HitCount}번)만큼 선택한 적에게 피해 {Damage}를 반복합니다. 이 부적을 복사합니다. (옛 behaviorId `attack.multiplying_strike`)</summary>
    public sealed class A08_MultiplyingStrike : BasicAttackCard
    {
        public override string Id => "A08";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackMultiplyingStrike;

        /// <summary>맞은 뒤 자기 복사본(A09, 아지랑이)을 뽑을 더미에 넣는다. 복사본은 임시라 다시 복사하지 않는다(CombatState.ApplyPostActions).</summary>
        public override IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions { get; } = new[]
        {
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionInjectCopy, CardIds.MultiplyingStrikeCopy)
        };
    }
}
