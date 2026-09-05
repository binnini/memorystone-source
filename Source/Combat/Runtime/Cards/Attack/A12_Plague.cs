using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A12 전염병 — 선택한 적에게 피해 {Damage}를 줍니다. 대상이 상태이상을 가지고 있으면 추가 피해 {Damage}를 주고 전염시킵니다.</summary>
    public sealed class A12_Plague : BasicAttackCard
    {
        public override string Id => "A12";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackPlague;

        /// <summary>맞은 뒤 대상의 상태이상 하나를 반경 안 이웃에게 옮긴다(전염 반경).</summary>
        public const int SpreadRadius = 2;

        public override IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions { get; } = new[]
        {
            new CardBehaviorMetadata.PostAction(CardBehaviorMetadata.PostActionSpreadStatus, SpreadRadius.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        public override int GetAttackDamage(CombatState state, CardDefinition card, int attackBonus, HexCoord target)
        {
            // "상태이상을 가지고 있다면 {Damage}를 추가로". The bonus is one more copy of the authored damage —
            // the card text promises a second {Damage}, so that is what it adds, rather than doubling the
            // terrain bonus along with it.
            var monster = state.FindLivingMonsterAt(target);
            var afflicted = monster != null && state.HasCleansableDebuff(monster.Id);
            return System.Math.Max(0, card.Amount + (afflicted ? card.Amount : 0) + attackBonus);
        }
    }
}
