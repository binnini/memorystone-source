using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A11 얼어버려라 — 선택한 적에게 피해 {Damage}를 주고 2턴 동안 속박시킵니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A11_TargetShot : BasicAttackCard
    {
        public override string Id => "A11";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "속박" };

        /// <summary>연마(옛 card_upgrades.csv): 원거리 2칸 유지·피해 2→4.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 4);

        /// <summary>맞은 적을 표식(다음 공격 우선 대상)하고 2턴 속박. 속박 턴수는 규칙이라 여기 상수다.</summary>
        public const int ImmobilizeTurns = 2;

        public override IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions { get; } = new[]
        {
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionApplyMark, string.Empty),
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionApplyImmobilize, ImmobilizeTurns.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
    }
}
