using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerCombatProfileCsvConverterTests
    {
        private const string CsvPath = "Assets/Data/Combat/Players/Source/player_combat_profiles.csv";
        private const string CardCatalogPath = TestAssetPaths.CardCatalogAsset;

        [Test]
        public void PlayerCombatProfilesCsvParsesBaselineAndPrototypeProfiles()
        {
            var catalog = PlayerCombatProfileCsvConverter.ConvertFile(CsvPath);

            Assert.That(catalog.TryGetProfile(PlayerCombatProfileCatalog.DefaultProfileId, out var baseline), Is.True);
            Assert.That(baseline.DisplayName, Is.EqualTo("Seorin"));
            Assert.That(baseline.MaxHp, Is.EqualTo(80));
            Assert.That(baseline.MovePoints, Is.EqualTo(2));
            Assert.That(baseline.AttackRange, Is.EqualTo(1));
            Assert.That(baseline.AttackDamage, Is.EqualTo(4));
            Assert.That(baseline.DefenseBlock, Is.EqualTo(4));
            Assert.That(baseline.ActionBudget, Is.EqualTo(4));
            Assert.That(baseline.MovementHandSize, Is.EqualTo(2));
            Assert.That(baseline.ActionHandSize, Is.EqualTo(5));
            Assert.That(baseline.VisionRange, Is.EqualTo(7));

            Assert.That(catalog.TryGetProfile(PlayerCombatProfileCatalog.PrototypeProfileId, out var prototype), Is.True);
            Assert.That(prototype.MaxHp, Is.EqualTo(80));
            Assert.That(prototype.VisionRange, Is.EqualTo(3));
        }

        [Test]
        public void PlayerCombatProfileCreatesCombatConfigForPlayerValuesAndKeepsEnemyValuesSeparate()
        {
            var profile = PlayerCombatProfileCsvConverter.ConvertFile(CsvPath)
                .GetProfileOrDefault(PlayerCombatProfileCatalog.PrototypeProfileId);

            var config = CombatConfig.FromPlayerProfile(
                profile,
                enemyMaxHp: 30,
                enemyChaseRange: 5,
                enemyAttackRange: 1,
                enemyAttackDamage: 5);

            Assert.That(config.PlayerMaxHp, Is.EqualTo(80));
            Assert.That(config.PlayerMovePoints, Is.EqualTo(2));
            Assert.That(config.AttackRange, Is.EqualTo(1));
            Assert.That(config.AttackDamage, Is.EqualTo(4));
            Assert.That(config.DefenseBlock, Is.EqualTo(4));
            Assert.That(config.ActionBudget, Is.EqualTo(4));
            Assert.That(config.MovementHandSize, Is.EqualTo(2));
            Assert.That(config.ActionHandSize, Is.EqualTo(5));
            Assert.That(config.PlayerVisionRange, Is.EqualTo(3));
            Assert.That(config.EnemyMaxHp, Is.EqualTo(30));
            Assert.That(config.EnemyAttackDamage, Is.EqualTo(5));
        }

        [Test]
        public void CardCatalogTokensResolveFromCsvBackedPlayerCombatConfig()
        {
            var profile = PlayerCombatProfileCsvConverter.ConvertFile(CsvPath)
                .GetProfileOrDefault(PlayerCombatProfileCatalog.PrototypeProfileId);
            var config = CombatConfig.FromPlayerProfile(profile, 30, 5, 1, 5);
            var asset = AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(CardCatalogPath);
            Assert.That(asset, Is.Not.Null);

            var entries = asset.ToCardCatalogDefinition(config).Entries.ToDictionary(entry => entry.Id);

            // Sweep (A01) is authored as a fixed range-0 area attack; its range/damage are literal, not profile tokens.
            Assert.That(entries[CardIds.Sweep].Range, Is.EqualTo(0));
            Assert.That(entries[CardIds.Sweep].Amount, Is.EqualTo(3));
            // Final Blow (A05) cost is authored as the {MaxKi} token and resolves from the profile action budget.
            Assert.That(entries[CardIds.FinalBlow].Cost, Is.EqualTo(config.MaxKi));
            // One Strike Enough (A07) uses a fixed Ki cost.
            Assert.That(entries[CardIds.OneStrikeEnough].Cost, Is.EqualTo(2));
        }

        [Test]
        public void InvalidPlayerCombatProfileCsvReportsDesignerFriendlyLineAndColumn()
        {
            var csv = File.ReadAllText(CsvPath).Replace("P001,Seorin,80", "P001,Seorin,0");

            var ex = Assert.Throws<FormatException>(() =>
                PlayerCombatProfileCsvConverter.ConvertText(csv, "bad_player_combat_profiles.csv"));

            Assert.That(ex.Message, Does.Contain("bad_player_combat_profiles.csv line 2"));
            Assert.That(ex.Message, Does.Contain("maxHp"));
        }
    }
}
