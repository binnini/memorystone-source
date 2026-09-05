using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>U02 부적 끌어오기 — 갈림길 — 행동 부적을 2장 뽑거나, 소멸된 부적을 무작위로 1장 손패로 가져옵니다. (옛 behaviorId `utility.draw_or_recover`)</summary>
    public sealed class U02_DrawOrRecover : UtilityCard
    {
        public override string Id => "U02";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.UtilityDrawOrRecover;

        /// <summary>「행동 부적 2장 뽑기」의 2 — 옛 behaviorParams=drawCount:2.</summary>
        public const int DrawCount = 2;

        public override int ChoiceDrawCount => DrawCount;

        /// <summary>갈림길: 행동 부적 뽑기 또는 소멸된 부적 1장 회수. 둘 다 자기 대상이라 사거리 판정이 없다.</summary>
        public override IReadOnlyList<CardBehaviorMetadata.ChoiceOption> Choices { get; } = new[]
        {
            new CardBehaviorMetadata.ChoiceOption("draw", CardBehaviorMetadata.ChoiceEffectDrawActionCards, CardBehaviorMetadata.ChoiceTargetSelf),
            new CardBehaviorMetadata.ChoiceOption("recover", CardBehaviorMetadata.ChoiceEffectRecoverExiledCard, CardBehaviorMetadata.ChoiceTargetSelf)
        };
    }
}
