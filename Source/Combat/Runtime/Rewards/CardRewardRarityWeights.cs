using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>One authored row: how likely a rarity is for a given reward kind.</summary>
    public readonly struct CardRewardRarityWeight
    {
        public CardRewardRarityWeight(CardRarity rarity, int weight)
        {
            Rarity = rarity;
            Weight = weight < 0 ? 0 : weight;
        }

        public CardRarity Rarity { get; }
        public int Weight { get; }
    }

    /// <summary>
    /// The reward rarity distribution, authored in <c>combat_rewards.csv</c>.
    ///
    /// The table doubles as an **allow-list**: a rarity absent from the active table never appears as a
    /// reward (CR-2). That is why elite rewards skip Rare — the elite table simply has no Rare row.
    ///
    /// ⚠️ <see cref="CardRarity.Basic"/> is rejected at import and again at roll time. "Basic never
    /// drops" is a game rule (CR-2), not an authoring convention, so it stays enforced in code even
    /// though the rest of the distribution became data. See DEC-2026-07-28-04.
    /// </summary>
    public sealed class CardRewardRarityWeights
    {
        private readonly CardRewardRarityWeight[] normal;
        private readonly CardRewardRarityWeight[] elite;

        public CardRewardRarityWeights(
            IReadOnlyList<CardRewardRarityWeight> normalWeights,
            IReadOnlyList<CardRewardRarityWeight> eliteWeights)
        {
            normal = Copy(normalWeights, nameof(normalWeights));
            elite = Copy(eliteWeights, nameof(eliteWeights));
        }

        /// <summary>
        /// Code fallback matching <c>combat_rewards.csv</c>. Used when the CSV TextAsset is not assigned,
        /// mirroring how <see cref="PlayerCombatProfile"/> keeps a fallback for its CSV.
        /// </summary>
        public static CardRewardRarityWeights Default { get; } = new CardRewardRarityWeights(
            new[]
            {
                new CardRewardRarityWeight(CardRarity.Rare, 80),
                new CardRewardRarityWeight(CardRarity.Epic, 15),
                new CardRewardRarityWeight(CardRarity.Legendary, 5)
            },
            new[]
            {
                new CardRewardRarityWeight(CardRarity.Epic, 80),
                new CardRewardRarityWeight(CardRarity.Legendary, 20)
            });

        public IReadOnlyList<CardRewardRarityWeight> Normal => normal;

        public IReadOnlyList<CardRewardRarityWeight> Elite => elite;

        /// <summary>The active table for this reward. Elite kills use a different distribution (CR-3).</summary>
        public IReadOnlyList<CardRewardRarityWeight> For(bool eliteOnly) => eliteOnly ? elite : normal;

        private static CardRewardRarityWeight[] Copy(IReadOnlyList<CardRewardRarityWeight> source, string parameterName)
        {
            if (source == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            var copy = new CardRewardRarityWeight[source.Count];
            var seen = new HashSet<CardRarity>();
            for (var i = 0; i < source.Count; i++)
            {
                var entry = source[i];
                if (entry.Rarity == CardRarity.Basic)
                {
                    throw new ArgumentException(
                        "Basic cards never drop as rewards (CR-2). Remove the Basic row from combat_rewards.csv.",
                        parameterName);
                }

                if (!seen.Add(entry.Rarity))
                {
                    throw new ArgumentException(
                        $"Duplicate rarity '{entry.Rarity}' in reward weights.",
                        parameterName);
                }

                copy[i] = entry;
            }

            return copy;
        }
    }
}
