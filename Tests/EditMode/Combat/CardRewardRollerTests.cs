using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// CR-1~CR-5 of 10-specs/systems/combat-rewards.md. These became testable when the rarity table
    /// moved to combat_rewards.csv and the draw moved to Combat.Runtime (DEC-2026-07-28-04) — before
    /// that the logic sat in a MonoBehaviour and could not be exercised without a controller.
    /// </summary>
    public sealed class CardRewardRollerTests
    {
        /// <summary>Deterministic random source: replays a fixed script of values, then repeats the last.</summary>
        private sealed class ScriptedRandom : IRewardRandom
        {
            private readonly int[] script;
            private int index;

            public ScriptedRandom(params int[] script)
            {
                this.script = script != null && script.Length > 0 ? script : new[] { 0 };
            }

            public int Next(int maxExclusive)
            {
                var raw = script[Math.Min(index++, script.Length - 1)];
                return maxExclusive <= 0 ? 0 : ((raw % maxExclusive) + maxExclusive) % maxExclusive;
            }
        }

        private static CardRewardCandidate Candidate(string id, CardRarity rarity) => new CardRewardCandidate(id, rarity);

        private static IReadOnlyList<CardRewardCandidate> Pool(int rare = 3, int epic = 3, int legendary = 3, int basic = 3)
        {
            var pool = new List<CardRewardCandidate>();
            for (var i = 0; i < basic; i++) pool.Add(Candidate($"B{i}", CardRarity.Basic));
            for (var i = 0; i < rare; i++) pool.Add(Candidate($"R{i}", CardRarity.Rare));
            for (var i = 0; i < epic; i++) pool.Add(Candidate($"E{i}", CardRarity.Epic));
            for (var i = 0; i < legendary; i++) pool.Add(Candidate($"L{i}", CardRarity.Legendary));
            return pool;
        }

        [Test]
        public void ShippedWeightsMatchTheAuthoredDistribution()
        {
            // CR-3 — the code fallback must stay in lockstep with combat_rewards.csv.
            var normal = CardRewardRarityWeights.Default.For(eliteOnly: false);
            var elite = CardRewardRarityWeights.Default.For(eliteOnly: true);

            Assert.That(normal.Select(w => (w.Rarity, w.Weight)), Is.EqualTo(new[]
            {
                (CardRarity.Rare, 80), (CardRarity.Epic, 15), (CardRarity.Legendary, 5)
            }));
            Assert.That(elite.Select(w => (w.Rarity, w.Weight)), Is.EqualTo(new[]
            {
                (CardRarity.Epic, 80), (CardRarity.Legendary, 20)
            }));
        }

        [Test]
        public void OffersThreeDistinctCards()
        {
            // CR-1 + CR-4.
            var picked = CardRewardRoller.SelectRewardCardIds(
                Pool(), CardRewardRarityWeights.Default, eliteOnly: false, new ScriptedRandom(0, 1, 2, 3, 4, 5, 6, 7));

            Assert.That(picked.Count, Is.EqualTo(3));
            Assert.That(picked.Distinct().Count(), Is.EqualTo(3), "a reward must never offer the same card twice");
        }

        [Test]
        public void BasicCardsNeverDropEvenWhenTheyAreTheOnlyPoolLeft()
        {
            // CR-2 — the hard guard. Basic is absent from the table AND excluded at roll time.
            var basicOnly = new[] { Candidate("B0", CardRarity.Basic), Candidate("B1", CardRarity.Basic) };

            var picked = CardRewardRoller.SelectRewardCardIds(
                basicOnly, CardRewardRarityWeights.Default, eliteOnly: false, new ScriptedRandom(0));

            Assert.That(picked, Is.Empty);
        }

        [Test]
        public void EliteRewardsSkipRareEntirely()
        {
            // CR-3 — the elite table has no Rare row, and the table is the allow-list.
            var picked = CardRewardRoller.SelectRewardCardIds(
                Pool(rare: 50, epic: 1, legendary: 1),
                CardRewardRarityWeights.Default,
                eliteOnly: true,
                new ScriptedRandom(0, 1, 2, 3, 4, 5));

            Assert.That(picked, Is.Not.Empty);
            Assert.That(picked.Any(id => id.StartsWith("R", StringComparison.Ordinal)), Is.False,
                "elite rewards must never offer a Rare card");
        }

        [Test]
        public void OffersFewerThanThreeWhenThePoolRunsDryInsteadOfPadding()
        {
            // CR-1 — "모자란 채로 제시": no padding, no duplicates to fill the gap.
            var thin = new[] { Candidate("R0", CardRarity.Rare), Candidate("E0", CardRarity.Epic) };

            var picked = CardRewardRoller.SelectRewardCardIds(
                thin, CardRewardRarityWeights.Default, eliteOnly: false, new ScriptedRandom(0, 1, 2, 3));

            Assert.That(picked.Count, Is.EqualTo(2));
            Assert.That(picked, Is.EquivalentTo(new[] { "R0", "E0" }));
        }

        [Test]
        public void ExhaustedRarityRenormalizesInsteadOfEmittingAnEmptySlot()
        {
            // CR-3 — a rarity with no unchosen card left drops out of the draw and the rest re-normalize.
            var pool = new[]
            {
                Candidate("R0", CardRarity.Rare),
                Candidate("E0", CardRarity.Epic),
                Candidate("E1", CardRarity.Epic),
            };

            // Always roll 0 → would always land on Rare while it is available; after R0 is taken the
            // Rare bucket is exhausted and the draw must continue with Epic rather than stalling.
            var picked = CardRewardRoller.SelectRewardCardIds(
                pool, CardRewardRarityWeights.Default, eliteOnly: false, new ScriptedRandom(0));

            Assert.That(picked.Count, Is.EqualTo(3));
            Assert.That(picked, Is.EquivalentTo(new[] { "R0", "E0", "E1" }));
        }

        [Test]
        public void WeightsAreHonouredAcrossManyDraws()
        {
            // CR-3 — the distribution is actually weighted, not uniform. With Rare 80 / Epic 15 /
            // Legendary 5 and one slot, Rare must dominate. Deterministic sweep over the weight range.
            var pool = new[]
            {
                Candidate("R0", CardRarity.Rare),
                Candidate("E0", CardRarity.Epic),
                Candidate("L0", CardRarity.Legendary),
            };

            var counts = new Dictionary<char, int> { ['R'] = 0, ['E'] = 0, ['L'] = 0 };
            for (var roll = 0; roll < 100; roll++)
            {
                var picked = CardRewardRoller.SelectRewardCardIds(
                    pool, CardRewardRarityWeights.Default, eliteOnly: false, new ScriptedRandom(roll), slotCount: 1);
                Assert.That(picked.Count, Is.EqualTo(1));
                counts[picked[0][0]]++;
            }

            Assert.That(counts['R'], Is.EqualTo(80));
            Assert.That(counts['E'], Is.EqualTo(15));
            Assert.That(counts['L'], Is.EqualTo(5));
        }

        [Test]
        public void RerollUsesTheSameEliteDistribution()
        {
            // CR-5 — a reroll re-runs the same draw; elite-ness is not lost. Same inputs → same result.
            var pool = Pool();
            var first = CardRewardRoller.SelectRewardCardIds(
                pool, CardRewardRarityWeights.Default, eliteOnly: true, new ScriptedRandom(1, 2, 3, 4));
            var second = CardRewardRoller.SelectRewardCardIds(
                pool, CardRewardRarityWeights.Default, eliteOnly: true, new ScriptedRandom(1, 2, 3, 4));

            Assert.That(second, Is.EqualTo(first));
            Assert.That(first.Any(id => id.StartsWith("R", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void WeightsRejectBasicAndDuplicateRarities()
        {
            Assert.Throws<ArgumentException>(() => new CardRewardRarityWeights(
                new[] { new CardRewardRarityWeight(CardRarity.Basic, 10) },
                new[] { new CardRewardRarityWeight(CardRarity.Epic, 10) }));

            Assert.Throws<ArgumentException>(() => new CardRewardRarityWeights(
                new[]
                {
                    new CardRewardRarityWeight(CardRarity.Rare, 10),
                    new CardRewardRarityWeight(CardRarity.Rare, 20)
                },
                new[] { new CardRewardRarityWeight(CardRarity.Epic, 10) }));
        }

        [Test]
        public void EmptyPoolYieldsNoOffers()
        {
            Assert.That(
                CardRewardRoller.SelectRewardCardIds(
                    Array.Empty<CardRewardCandidate>(), CardRewardRarityWeights.Default, false, new ScriptedRandom(0)),
                Is.Empty);
        }
    }
}
