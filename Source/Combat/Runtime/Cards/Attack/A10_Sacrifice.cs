using SeoulPlayup.CardCore;
using System.Collections.Generic;
namespace SeoulPlayup.Combat.Runtime.Cards
{
    /// <summary>A10 제물을 바쳐서 — 손패의 행동 부적을 원하는 만큼 소멸시킵니다. 선택한 적에게 소멸시킨 부적 수만큼 피해 {Damage}를 반복합니다. (옛 behaviorId `attack.damage`)</summary>
    public sealed class A10_Sacrifice : BasicAttackCard
    {
        public override string Id => "A10";

        /// <summary>효과 발신 키(옛 behaviorId). VFX 큐·오디오·상태이상 sourceRef가 이 문자열에 매칭된다 — 카드 식별에는 쓰지 않는다.</summary>
        public override string EffectSourceRef => CardEffectRefs.AttackDamage;

        /// <summary>추가 비용: 손패의 행동 부적을 원하는 만큼 골라 소멸시킨다. 반복 횟수(HitCount)는 고른 장수 — CombatState의 제물 경로.</summary>
        public override string AdditionalCost => CardBehaviorMetadata.AdditionalCostExileSelectedHandCards;
    }
}
