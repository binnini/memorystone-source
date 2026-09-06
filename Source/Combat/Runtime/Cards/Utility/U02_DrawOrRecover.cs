using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>U02 부적 끌어오기 — 갈림길 — 행동 부적을 2장 뽑거나, 소멸된 부적을 무작위로 1장 손패로 가져옵니다. (옛 behaviorId `utility.draw_or_recover`)</summary>
    public sealed class U02_DrawOrRecover : UtilityCard
    {
        public override string Id => "U02";

        /// <summary>문안에 걸리는 게임 키워드(P5 명시화). 문안과의 정합은 CardKeywordTextBindingTests가 감사한다.</summary>
        public override IReadOnlyList<string> Keywords { get; } = new[] { "갈림길", "소멸", "회수" };

        /// <summary>「행동 부적 2장 뽑기」의 2 — 옛 behaviorParams=drawCount:2.</summary>
        public const int DrawCount = 2;

        /// <summary>연마 시 뽑는 장수(효과 연마 2차, DEC-2026-09-06-08).</summary>
        public const int UpgradedDrawCount = 3;

        /// <summary>연마 가능 선언 — 규칙(뽑는 장수)이 UpgradeLevel로 갈린다. 문안은 cards.csv `descriptionUpgraded`.</summary>
        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With();

        public override int ChoiceDrawCount(CardDefinition card) => card.UpgradeLevel >= 1 ? UpgradedDrawCount : DrawCount;

        /// <summary>갈림길: 행동 부적 뽑기 또는 소멸된 부적 1장 회수. 둘 다 자기 대상이라 사거리 판정이 없다.</summary>
        public override IReadOnlyList<CardBehaviorMetadata.ChoiceOption> Choices { get; } = new[]
        {
            new CardBehaviorMetadata.ChoiceOption("draw", CardBehaviorMetadata.ChoiceEffectDrawActionCards, CardBehaviorMetadata.ChoiceTargetSelf),
            new CardBehaviorMetadata.ChoiceOption("recover", CardBehaviorMetadata.ChoiceEffectRecoverExiledCard, CardBehaviorMetadata.ChoiceTargetSelf)
        };
    }
}
