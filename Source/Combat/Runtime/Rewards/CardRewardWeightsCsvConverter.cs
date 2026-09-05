using System;
using System.Collections.Generic;
using System.IO;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Parses <c>combat_rewards.csv</c> into <see cref="CardRewardRarityWeights"/>.
    ///
    /// Small enough to read directly at runtime (5 rows) — no baked asset, matching how monsters /
    /// players / status-effects are handled. Schema: <c>10-specs/schema/rewards.md</c>.
    ///
    /// Structural errors surface as <see cref="FormatException"/> with the real file line, matching the
    /// player-profile converter's contract.
    /// </summary>
    public static class CardRewardWeightsCsvConverter
    {
        private const string NormalKind = "normal";
        private const string EliteKind = "elite";

        private static readonly string[] ExpectedHeaders =
        {
            "rewardKind",
            "rarity",
            "weight",
            "designerNote"
        };

        public static CardRewardRarityWeights ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Combat reward CSV path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required combat reward CSV file is missing: {path}", path);
            }

            return ConvertText(File.ReadAllText(path), Path.GetFileName(path));
        }

        public static CardRewardRarityWeights ConvertText(string csvText, string sourceName = "combat_rewards.csv")
        {
            var table = CsvTable.Parse(csvText ?? string.Empty, sourceName);
            table.RequireHeaders(ExpectedHeaders);

            var normal = new List<CardRewardRarityWeight>();
            var elite = new List<CardRewardRarityWeight>();
            foreach (var row in table.Rows)
            {
                var kind = Required(row, "rewardKind");
                var rarity = Rarity(row);
                var weight = NonNegativeInt(row, "weight");
                var entry = new CardRewardRarityWeight(rarity, weight);

                if (string.Equals(kind, NormalKind, StringComparison.OrdinalIgnoreCase))
                {
                    normal.Add(entry);
                }
                else if (string.Equals(kind, EliteKind, StringComparison.OrdinalIgnoreCase))
                {
                    elite.Add(entry);
                }
                else
                {
                    throw row.Error($"column 'rewardKind' value '{kind}' is not '{NormalKind}' or '{EliteKind}'.");
                }
            }

            if (normal.Count == 0 || elite.Count == 0)
            {
                throw new FormatException(
                    $"{table.Name}: both '{NormalKind}' and '{EliteKind}' reward kinds must have at least one rarity row.");
            }

            try
            {
                return new CardRewardRarityWeights(normal, elite);
            }
            catch (ArgumentException ex)
            {
                // Duplicate-rarity / Basic guards live on the type; surface them as a CSV format error so
                // the authoring mistake reads like every other CSV failure.
                throw new FormatException($"{table.Name}: {ex.Message}", ex);
            }
        }

        private static CardRarity Rarity(CsvRow row)
        {
            var raw = Required(row, "rarity");
            if (!Enum.TryParse<CardRarity>(raw, ignoreCase: true, out var rarity) || !Enum.IsDefined(typeof(CardRarity), rarity))
            {
                throw row.Error($"column 'rarity' value '{raw}' is not a known card rarity.");
            }

            // CR-2 is a game rule, not an authoring convention: refuse the row rather than let a typo
            // quietly make Basic cards droppable.
            if (rarity == CardRarity.Basic)
            {
                throw row.Error("column 'rarity' is 'Basic'; Basic cards never drop as rewards (CR-2). Remove this row.");
            }

            return rarity;
        }

        private static string Required(CsvRow row, string column)
        {
            var value = row.TryGet(column, out var raw) ? raw.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw row.Error($"required column '{column}' is empty.");
            }

            return value;
        }

        private static int NonNegativeInt(CsvRow row, string column)
        {
            var raw = Required(row, column);
            if (!int.TryParse(raw, out var value))
            {
                throw row.Error($"column '{column}' value '{raw}' is not a valid integer.");
            }

            if (value < 0)
            {
                throw row.Error($"column '{column}' must be zero or greater.");
            }

            return value;
        }

        private static void RequireHeaders(this CsvTable table, IReadOnlyList<string> expected)
        {
            if (table.Headers.Count != expected.Count)
            {
                throw new FormatException($"{table.Name} header has {table.Headers.Count} columns; expected {expected.Count}.");
            }

            for (var i = 0; i < expected.Count; i++)
            {
                if (!string.Equals(table.Headers[i], expected[i], StringComparison.Ordinal))
                {
                    throw new FormatException($"{table.Name} header column {i + 1} is '{table.Headers[i]}'; expected '{expected[i]}'.");
                }
            }
        }

        private static FormatException Error(this CsvRow row, string message)
            => new FormatException($"{row.FileName} line {row.LineNumber}: {message}");
    }
}
