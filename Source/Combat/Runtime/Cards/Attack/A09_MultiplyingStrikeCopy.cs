using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A09 증식 공격 복사본 — 덱과 손패에 있는 '증식 공격' 부적의 수({HitCount}번)만큼 선택한 적에게 피해 {Damage}를 반복합니다. 아지랑이. (옛 behaviorId `attack.multiplying_strike`)</summary>
    public sealed class A09_MultiplyingStrikeCopy : BasicAttackCard
    {
        public override string Id => "A09";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "아지랑이" };

        /// <summary>원본과 같은 후속 규칙을 선언하지만 임시 카드(아지랑이)라 실제 복사는 일어나지 않는다 — 규칙 문면을 원본과 같게 둔다.</summary>
        public override IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions { get; } = new[]
        {
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionInjectCopy, CardIds.MultiplyingStrikeCopy)
        };
    }
}
