using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    public static class RewardEventObjectUiAdapter
    {
        public static IReadOnlyList<CardRewardOffer> ToCardRewardOffers(CombatEventObjectDefinition eventObject, int fallbackCost = 1)
        {
            if (eventObject == null || eventObject.Category != CombatEventObjectCategory.Reward)
            {
                return new List<CardRewardOffer>();
            }

            return eventObject.RewardOffers
                .Where(offer => offer.Kind == RewardEventObjectOfferKind.Card)
                .Select(offer => new CardRewardOffer(
                    offer.RewardId,
                    offer.DisplayName,
                    "보상",
                    offer.Description,
                    fallbackCost,
                    offer.CardEffectType))
                .ToList();
        }
    }
}