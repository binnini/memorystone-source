using System;
using System.IO;
using System.Text;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.CardCore.EditorTools
{
    public static class CardCatalogCsvImporter
    {
        public const string InputPath = CombatCsvPaths.CardsCsv;
        public const string ChoiceOptionsInputPath = CombatCsvPaths.CardChoiceOptionsCsv;
        public const string UpgradesInputPath = CombatCsvPaths.CardUpgradesCsv;
        public const string OutputPath = "Assets/Data/Combat/Cards/Catalogs/CardCatalog.asset";

        [MenuItem("Tools/Cards/Import cards.csv")]
        public static void ImportDefault()
        {
            Import(InputPath, OutputPath);
        }

        public static CardCatalogAsset Import(string csvPath, string assetPath)
        {
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                throw new ArgumentException("CSV path is required.", nameof(csvPath));
            }

            if (string.IsNullOrWhiteSpace(assetPath))
            {
                throw new ArgumentException("Asset path is required.", nameof(assetPath));
            }

            if (!File.Exists(csvPath))
            {
                throw new FileNotFoundException("Card catalog CSV was not found.", csvPath);
            }

            var csvText = File.ReadAllText(csvPath, new UTF8Encoding(false, true));
            var parsedRows = CardCatalogAsset.ParseCsvText(csvText);
            var choiceOptionsPath = Path.Combine(Path.GetDirectoryName(csvPath) ?? string.Empty, Path.GetFileName(ChoiceOptionsInputPath));
            var parsedChoiceOptionRows = File.Exists(choiceOptionsPath)
                ? CardCatalogAsset.ParseChoiceOptionsCsvText(File.ReadAllText(choiceOptionsPath, new UTF8Encoding(false, true)))
                : Array.Empty<CardChoiceOptionCsvRow>();
            var upgradesPath = Path.Combine(Path.GetDirectoryName(csvPath) ?? string.Empty, Path.GetFileName(UpgradesInputPath));
            var parsedUpgradeRows = File.Exists(upgradesPath)
                ? CardCatalogAsset.ParseUpgradesCsvText(File.ReadAllText(upgradesPath, new UTF8Encoding(false, true)))
                : Array.Empty<CardUpgradeCsvRow>();

            var asset = AssetDatabase.LoadAssetAtPath<CardCatalogAsset>(assetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
                var directory = Path.GetDirectoryName(assetPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                AssetDatabase.CreateAsset(asset, assetPath);
            }

            asset.SetRows(parsedRows);
            asset.SetChoiceOptionRows(parsedChoiceOptionRows);
            asset.SetUpgradeRows(parsedUpgradeRows);
            if (!asset.ValidateRows(out var reason))
            {
                throw new InvalidOperationException($"Card catalog import validation failed: {reason}");
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CardCatalogCsvImporter] Imported {asset.Rows.Count} cards, {asset.ChoiceOptionRows.Count} choice options and {asset.UpgradeRows.Count} upgrades from {csvPath} to {assetPath}.");
            return asset;
        }
    }
}
