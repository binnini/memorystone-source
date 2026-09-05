using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// combat_rewards.csv contract (10-specs/schema/rewards.md). The shipped-file test is the one that
    /// matters most: it proves the authored table still equals the distribution CR-3 promises.
    /// </summary>
    public sealed class CardRewardWeightsCsvConverterTests
    {
        private const string Header = "rewardKind,rarity,weight,designerNote";

        private static string Csv(params string[] rows) => Header + "\n" + string.Join("\n", rows) + "\n";

        [Category("ShippingData")]
        [Test]
        public void ShippedCsvMatchesTheDistributionTheSpecPromises()
        {
            Assert.That(File.Exists(CombatCsvPaths.CombatRewardsCsv), Is.True,
                $"missing shipped CSV: {CombatCsvPaths.CombatRewardsCsv}");

            var weights = CardRewardWeightsCsvConverter.ConvertFile(CombatCsvPaths.CombatRewardsCsv);

            Assert.That(weights.Normal.Select(w => (w.Rarity, w.Weight)), Is.EqualTo(new[]
            {
                (CardRarity.Rare, 80), (CardRarity.Epic, 15), (CardRarity.Legendary, 5)
            }));
            Assert.That(weights.Elite.Select(w => (w.Rarity, w.Weight)), Is.EqualTo(new[]
            {
                (CardRarity.Epic, 80), (CardRarity.Legendary, 20)
            }));
        }

        [Category("ShippingData")]
        [Test]
        public void ShippedCsvEqualsTheCodeFallback()
        {
            // The fallback exists for unassigned TextAssets; if the two drift, behaviour silently depends
            // on whether the asset happened to be wired.
            var fromCsv = CardRewardWeightsCsvConverter.ConvertFile(CombatCsvPaths.CombatRewardsCsv);
            var fallback = CardRewardRarityWeights.Default;

            Assert.That(fromCsv.Normal.Select(w => (w.Rarity, w.Weight)),
                Is.EqualTo(fallback.Normal.Select(w => (w.Rarity, w.Weight))));
            Assert.That(fromCsv.Elite.Select(w => (w.Rarity, w.Weight)),
                Is.EqualTo(fallback.Elite.Select(w => (w.Rarity, w.Weight))));
        }

        [Test]
        public void BasicRowIsRejectedAtImport()
        {
            // CR-2 must not be defeatable by authoring — the import fails loudly instead.
            var ex = Assert.Throws<FormatException>(() => CardRewardWeightsCsvConverter.ConvertText(
                Csv("normal,Basic,50,", "normal,Rare,50,", "elite,Epic,100,")));

            Assert.That(ex.Message, Does.Contain("Basic"));
        }

        [Test]
        public void UnknownRewardKindOrRarityIsRejected()
        {
            Assert.Throws<FormatException>(() => CardRewardWeightsCsvConverter.ConvertText(
                Csv("bonus,Rare,50,", "elite,Epic,100,")));

            Assert.Throws<FormatException>(() => CardRewardWeightsCsvConverter.ConvertText(
                Csv("normal,Mythic,50,", "elite,Epic,100,")));
        }

        [Test]
        public void NegativeWeightAndDuplicateRarityAreRejected()
        {
            Assert.Throws<FormatException>(() => CardRewardWeightsCsvConverter.ConvertText(
                Csv("normal,Rare,-1,", "elite,Epic,100,")));

            Assert.Throws<FormatException>(() => CardRewardWeightsCsvConverter.ConvertText(
                Csv("normal,Rare,50,", "normal,Rare,20,", "elite,Epic,100,")));
        }

        [Test]
        public void MissingRewardKindIsRejected()
        {
            // Both kinds are required: a file with only 'normal' would silently make elite rewards empty.
            Assert.Throws<FormatException>(() => CardRewardWeightsCsvConverter.ConvertText(
                Csv("normal,Rare,80,")));
        }

        [Test]
        public void HeaderMismatchIsRejected()
        {
            Assert.Throws<FormatException>(() => CardRewardWeightsCsvConverter.ConvertText(
                "rewardKind,rarity,weight\nnormal,Rare,80\n"));
        }

        [Test]
        public void ErrorMessageCarriesTheRealFileLine()
        {
            var ex = Assert.Throws<FormatException>(() => CardRewardWeightsCsvConverter.ConvertText(
                Csv("normal,Rare,80,", "normal,Nope,15,", "elite,Epic,100,")));

            Assert.That(ex.Message, Does.Contain("line 3"), "structural errors must point at the authored line");
        }
    }
}
