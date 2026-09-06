using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A08 증식 공격 — 덱과 손패에 있는 '증식 공격' 부적의 수({HitCount}번)만큼 선택한 적에게 피해 {Damage}를 반복합니다. 이 부적을 복사합니다. (옛 behaviorId `attack.multiplying_strike`)</summary>
    public sealed class A08_MultiplyingStrike : BasicAttackCard
    {
        public override string Id => "A08";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "복사" };

        /// <summary>연마(옛 card_upgrades.csv): 증식 반복이라 피해 2→3.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 3);

        /// <summary>맞은 뒤 자기 복사본(A09, 아지랑이)을 뽑을 더미에 넣는다. 복사본은 임시라 다시 복사하지 않는다(CombatState.ApplyPostActions).</summary>
        public override IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions { get; } = new[]
        {
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionInjectCopy, CardIds.MultiplyingStrikeCopy)
        };
    }
}
