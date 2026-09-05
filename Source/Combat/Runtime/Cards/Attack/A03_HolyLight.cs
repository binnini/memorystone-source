using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A03 성스러운 빛 — 갈림길 — 자신을 {Heal} 회복하거나, 선택한 적에게 피해 {Damage}를 줍니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A03_HolyLight : BasicAttackCard
    {
        public override string Id => "A03";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;

        /// <summary>갈림길: 자신 회복(heal 축) 또는 적 공격. 문안은 cards.csv 선택지 컬럼(D-2).</summary>
        public override IReadOnlyList<CardBehaviorMetadata.ChoiceOption> Choices { get; } = new[]
        {
            new CardBehaviorMetadata.ChoiceOption("heal", CardBehaviorMetadata.ChoiceEffectHealPlayer, CardBehaviorMetadata.ChoiceTargetSelf),
            new CardBehaviorMetadata.ChoiceOption("attack", CardBehaviorMetadata.ChoiceEffectAttackDamage, "enemy")
        };
    }
}
