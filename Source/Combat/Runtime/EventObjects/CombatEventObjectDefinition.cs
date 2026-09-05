using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    public enum CombatEventObjectCategory
    {
        Reward,
        Debuff,
        Knockback
    }

    public enum RewardEventObjectKind
    {
        CardDrawMachine,
        RelicDrawMachine,
        MoneyDrawMachine,

        /// <summary>소모품 지급기(T4-3 뽑기 Item 결과). 직렬화 안정을 위해 끝에 추가.</summary>
        ItemDrawMachine
    }

    public enum DebuffEventObjectKind
    {
        Poison,
        Bind,
        Slow,
        Rupture
    }

    public enum KnockbackEventObjectKind
    {
        BoxingGloveMachine
    }

    public enum RewardEventObjectOfferKind
    {
        Card,
        PermanentItem,
        Money,

        /// <summary>소모품(가방 아이템, T4-3). 직렬화 안정을 위해 끝에 추가.</summary>
        BagItem
    }

    public readonly struct RewardEventObjectOffer : IEquatable<RewardEventObjectOffer>
    {
        public RewardEventObjectOffer(
            RewardEventObjectOfferKind kind,
            string rewardId,
            string displayName = "",
            string description = "",
            CardEffectType cardEffectType = CardEffectType.Utility,
            PlayerPermanentItemKind permanentItemKind = PlayerPermanentItemKind.Relic,
            int amount = 0)
        {
            Kind = kind;
            RewardId = string.IsNullOrWhiteSpace(rewardId)
                ? throw new ArgumentException("Reward id is required.", nameof(rewardId))
                : rewardId.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? RewardId : displayName;
            Description = description ?? string.Empty;
            CardEffectType = cardEffectType;
            PermanentItemKind = permanentItemKind;
            Amount = Math.Max(0, amount);
        }

        public RewardEventObjectOfferKind Kind { get; }
        public string RewardId { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public CardEffectType CardEffectType { get; }
        public PlayerPermanentItemKind PermanentItemKind { get; }

        /// <summary>지급 금액. <see cref="RewardEventObjectOfferKind.Money"/>일 때만 의미가 있다.</summary>
        public int Amount { get; }

        public static RewardEventObjectOffer Card(string cardId, string displayName = "", string description = "", CardEffectType effectType = CardEffectType.Utility)
        {
            return new RewardEventObjectOffer(RewardEventObjectOfferKind.Card, cardId, displayName, description, effectType);
        }

        public static RewardEventObjectOffer PermanentItem(string definitionId, string displayName = "", string description = "", PlayerPermanentItemKind itemKind = PlayerPermanentItemKind.Relic)
        {
            return new RewardEventObjectOffer(RewardEventObjectOfferKind.PermanentItem, definitionId, displayName, description, permanentItemKind: itemKind);
        }

        /// <summary>
        /// 스테이지 한정 재화 지급. 뽑기가 금액을 이미 정해서 넘기므로 여기서 다시 뽑지 않는다.
        /// <paramref name="amount"/>가 0이면 지급 시점에 거부된다 — 0원 꾸러미를 크게 띄우고
        /// 아무 일도 없는 것보다 저작 실수로 드러나는 편이 낫다.
        /// </summary>
        /// <summary>소모품 지급(T4-3). 가방 만원 판정은 지급 지점(TryAddBagItem)이 한다.</summary>
        public static RewardEventObjectOffer BagItem(string itemId, string displayName = "", string description = "")
        {
            return new RewardEventObjectOffer(RewardEventObjectOfferKind.BagItem, itemId, displayName, description);
        }

        public static RewardEventObjectOffer Money(int amount, string displayName = "", string description = "")
        {
            return new RewardEventObjectOffer(
                RewardEventObjectOfferKind.Money,
                "money",
                displayName,
                description,
                amount: amount);
        }

        public bool Equals(RewardEventObjectOffer other)
        {
            // 금액도 동일성에 넣는다: RewardId가 모두 "money"라 금액이 빠지면 서로 다른 액수의
            // 꾸러미가 같은 오퍼로 취급되고, TryClaim의 "이 오퍼가 이 오브젝트의 것인가" 검사가
            // 엉뚱한 금액을 통과시킨다.
            return Kind == other.Kind
                && string.Equals(RewardId, other.RewardId, StringComparison.Ordinal)
                && Amount == other.Amount;
        }

        public override bool Equals(object obj)
        {
            return obj is RewardEventObjectOffer other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = ((int)Kind * 397) ^ (RewardId == null ? 0 : RewardId.GetHashCode());
                return (hash * 397) ^ Amount;
            }
        }
    }

    public sealed class CombatEventObjectDefinition
    {
        private readonly RewardEventObjectOffer[] rewardOffers;

        private CombatEventObjectDefinition(
            string objectId,
            CombatEventObjectCategory category,
            RewardEventObjectKind rewardKind,
            IEnumerable<RewardEventObjectOffer> rewardOffers,
            DebuffEventObjectKind debuffKind,
            int debuffAmount,
            int debuffDurationTurns,
            KnockbackEventObjectKind knockbackKind,
            int knockbackDistance,
            int knockbackImpactDamage)
        {
            ObjectId = string.IsNullOrWhiteSpace(objectId)
                ? throw new ArgumentException("Event object id is required.", nameof(objectId))
                : objectId.Trim();
            Category = category;
            RewardKind = rewardKind;
            this.rewardOffers = rewardOffers == null
                ? Array.Empty<RewardEventObjectOffer>()
                : rewardOffers.Where(offer => !string.IsNullOrWhiteSpace(offer.RewardId)).Distinct().ToArray();
            DebuffKind = debuffKind;
            DebuffAmount = Math.Max(0, debuffAmount);
            DebuffDurationTurns = Math.Max(0, debuffDurationTurns);
            KnockbackKind = knockbackKind;
            KnockbackDistance = Math.Max(0, knockbackDistance);
            KnockbackImpactDamage = Math.Max(0, knockbackImpactDamage);
        }

        public string ObjectId { get; }
        public CombatEventObjectCategory Category { get; }
        public RewardEventObjectKind RewardKind { get; }
        public IReadOnlyList<RewardEventObjectOffer> RewardOffers => rewardOffers;
        public DebuffEventObjectKind DebuffKind { get; }
        public int DebuffAmount { get; }
        public int DebuffDurationTurns { get; }
        public KnockbackEventObjectKind KnockbackKind { get; }
        public int KnockbackDistance { get; }
        public int KnockbackImpactDamage { get; }

        public static CombatEventObjectDefinition CardRewardMachine(string objectId, IEnumerable<RewardEventObjectOffer> cardOffers)
        {
            return new CombatEventObjectDefinition(
                objectId,
                CombatEventObjectCategory.Reward,
                RewardEventObjectKind.CardDrawMachine,
                cardOffers,
                DebuffEventObjectKind.Poison,
                0,
                0,
                KnockbackEventObjectKind.BoxingGloveMachine,
                0,
                0);
        }

        public static CombatEventObjectDefinition RelicRewardMachine(string objectId, IEnumerable<RewardEventObjectOffer> permanentItemOffers)
        {
            return new CombatEventObjectDefinition(
                objectId,
                CombatEventObjectCategory.Reward,
                RewardEventObjectKind.RelicDrawMachine,
                permanentItemOffers,
                DebuffEventObjectKind.Poison,
                0,
                0,
                KnockbackEventObjectKind.BoxingGloveMachine,
                0,
                0);
        }

        public static CombatEventObjectDefinition MoneyRewardMachine(string objectId, IEnumerable<RewardEventObjectOffer> moneyOffers)
        {
            return new CombatEventObjectDefinition(
                objectId,
                CombatEventObjectCategory.Reward,
                RewardEventObjectKind.MoneyDrawMachine,
                moneyOffers,
                DebuffEventObjectKind.Poison,
                0,
                0,
                KnockbackEventObjectKind.BoxingGloveMachine,
                0,
                0);
        }

        public static CombatEventObjectDefinition ItemRewardMachine(string objectId, IEnumerable<RewardEventObjectOffer> itemOffers)
        {
            return new CombatEventObjectDefinition(
                objectId,
                CombatEventObjectCategory.Reward,
                RewardEventObjectKind.ItemDrawMachine,
                itemOffers,
                DebuffEventObjectKind.Poison,
                0,
                0,
                KnockbackEventObjectKind.BoxingGloveMachine,
                0,
                0);
        }

        public static CombatEventObjectDefinition Debuff(string objectId, DebuffEventObjectKind debuffKind, int amount, int durationTurns)
        {
            return new CombatEventObjectDefinition(
                objectId,
                CombatEventObjectCategory.Debuff,
                RewardEventObjectKind.CardDrawMachine,
                Array.Empty<RewardEventObjectOffer>(),
                debuffKind,
                amount,
                durationTurns,
                KnockbackEventObjectKind.BoxingGloveMachine,
                0,
                0);
        }

        public static CombatEventObjectDefinition BoxingGloveMachine(string objectId, int distance, int impactDamage)
        {
            return new CombatEventObjectDefinition(
                objectId,
                CombatEventObjectCategory.Knockback,
                RewardEventObjectKind.CardDrawMachine,
                Array.Empty<RewardEventObjectOffer>(),
                DebuffEventObjectKind.Poison,
                0,
                0,
                KnockbackEventObjectKind.BoxingGloveMachine,
                distance,
                impactDamage);
        }
    }
}
