using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Random source for reward draws. Exists so the roll is testable without Unity — the presentation
    /// layer injects a <c>UnityEngine.Random</c>-backed implementation, tests inject a deterministic one.
    ///
    /// This is a seam for *this* component only. Unifying the game's several RNG axes (deck shuffle,
    /// reward draw, in-combat rolls) is a separate concern — see combat-rewards.md GAP-2.
    /// </summary>
    public interface IRewardRandom
    {
        /// <summary>Uniform integer in [0, maxExclusive). Callers never pass maxExclusive &lt;= 0.</summary>
        int Next(int maxExclusive);
    }

    /// <summary>A card eligible to be offered, reduced to what the draw needs.</summary>
    public readonly struct CardRewardCandidate
    {
        public CardRewardCandidate(string cardId, CardRarity rarity)
        {
            CardId = cardId ?? string.Empty;
            Rarity = rarity;
        }

        public string CardId { get; }
        public CardRarity Rarity { get; }
    }

    /// <summary>
    /// Picks the card ids for one reward offer. Pure: no Unity, no presentation types.
    ///
    /// Deliberately works on ids rather than the presentation `CardRewardOffer` (which lives in the Unity
    /// layer and carries display strings) — the caller maps ids back to offers. That keeps the whole of
    /// CR-1~CR-5 testable without standing up a controller.
    /// </summary>
    public static class CardRewardRoller
    {
        public const int DefaultSlotCount = 3;

        /// <summary>
        /// Selects up to <paramref name="slotCount"/> distinct card ids (CR-1·CR-4) using the weighted
        /// rarity draw (CR-3), restricted to rarities present in the active table (CR-2).
        /// Returns fewer than requested when the eligible pool runs dry — it never pads (CR-1).
        /// </summary>
        public static IReadOnlyList<string> SelectRewardCardIds(
            IReadOnlyList<CardRewardCandidate> candidates,
            CardRewardRarityWeights weights,
            bool eliteOnly,
            IRewardRandom random,
            int slotCount = DefaultSlotCount)
        {
            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            var table = weights.For(eliteOnly);
            if (candidates == null || candidates.Count == 0 || table.Count == 0 || slotCount <= 0)
            {
                return Array.Empty<string>();
            }

            // The weight table is the allow-list (CR-2). Basic is excluded unconditionally on top of
            // that: authoring must not be able to make Basic droppable — see CardRewardRarityWeights.
            var allowed = new HashSet<CardRarity>();
            foreach (var entry in table)
            {
                if (entry.Rarity != CardRarity.Basic)
                {
                    allowed.Add(entry.Rarity);
                }
            }

            var byRarity = new Dictionary<CardRarity, List<string>>();
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate.CardId) || !allowed.Contains(candidate.Rarity))
                {
                    continue;
                }

                if (!byRarity.TryGetValue(candidate.Rarity, out var bucket))
                {
                    bucket = new List<string>();
                    byRarity[candidate.Rarity] = bucket;
                }

                bucket.Add(candidate.CardId);
            }

            var result = new List<string>(slotCount);
            var chosen = new HashSet<string>(StringComparer.Ordinal);
            // Per slot: roll a rarity (re-normalized over rarities that still have an unused card), then
            // pick a distinct card of that rarity. Stops early if the eligible pool runs dry.
            var safety = 0;
            var safetyLimit = Math.Max(64, slotCount * 16);
            while (result.Count < slotCount && safety++ < safetyLimit)
            {
                var rarity = RollRarity(table, byRarity, chosen, random);
                if (rarity == null)
                {
                    break;
                }

                var candidatesOfRarity = byRarity[rarity.Value];
                var remaining = new List<string>(candidatesOfRarity.Count);
                foreach (var cardId in candidatesOfRarity)
                {
                    if (!chosen.Contains(cardId))
                    {
                        remaining.Add(cardId);
                    }
                }

                if (remaining.Count == 0)
                {
                    continue;
                }

                var pick = remaining[random.Next(remaining.Count)];
                result.Add(pick);
                chosen.Add(pick);
            }

            return result;
        }

        /// <summary>
        /// Weighted rarity draw over the rarities that still have an unchosen card, so exhausted buckets
        /// re-normalize the remaining weights instead of producing an empty slot (CR-3).
        /// </summary>
        private static CardRarity? RollRarity(
            IReadOnlyList<CardRewardRarityWeight> table,
            Dictionary<CardRarity, List<string>> byRarity,
            HashSet<string> chosen,
            IRewardRandom random)
        {
            var available = new List<CardRewardRarityWeight>(table.Count);
            foreach (var entry in table)
            {
                if (entry.Rarity == CardRarity.Basic || !byRarity.TryGetValue(entry.Rarity, out var bucket))
                {
                    continue;
                }

                foreach (var cardId in bucket)
                {
                    if (!chosen.Contains(cardId))
                    {
                        available.Add(entry);
                        break;
                    }
                }
            }

            if (available.Count == 0)
            {
                return null;
            }

            var total = 0;
            foreach (var entry in available)
            {
                total += entry.Weight;
            }

            // All-zero weights are authorable, so fall back to a uniform pick rather than dividing by
            // zero. Unreachable while the shipped table has positive weights.
            if (total <= 0)
            {
                return available[random.Next(available.Count)].Rarity;
            }

            var roll = random.Next(total);
            var cumulative = 0;
            foreach (var entry in available)
            {
                cumulative += entry.Weight;
                if (roll < cumulative)
                {
                    return entry.Rarity;
                }
            }

            return available[available.Count - 1].Rarity;
        }
    }
}
