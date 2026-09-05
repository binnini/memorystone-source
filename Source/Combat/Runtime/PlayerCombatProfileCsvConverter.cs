using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    public static class PlayerCombatProfileCsvConverter
    {
        private static readonly string[] ExpectedHeaders =
        {
            "profileId",
            "displayName",
            "maxHp",
            "movePoints",
            "attackRange",
            "attackDamage",
            "defenseBlock",
            "actionBudget",
            "movementHandSize",
            "actionHandSize",
            "visionRange",
            "status",
            "designerNote"
        };

        public static PlayerCombatProfileCatalog ConvertFile(
            string path,
            string sourceId = "player-combat-profiles-csv",
            string displayName = "Player Combat Profiles CSV")
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Player combat profile CSV path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required player combat profile CSV file is missing: {path}", path);
            }

            return ConvertText(File.ReadAllText(path), Path.GetFileName(path), sourceId, displayName);
        }

        public static PlayerCombatProfileCatalog ConvertText(
            string csvText,
            string sourceName = "player_combat_profiles.csv",
            string sourceId = "player-combat-profiles-csv",
            string displayName = "Player Combat Profiles CSV")
        {
            var table = CsvTable.Parse(csvText ?? string.Empty, sourceName);
            table.RequireHeaders(ExpectedHeaders);

            var profiles = new List<PlayerCombatProfile>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                var profile = CreateProfile(row);
                if (!ids.Add(profile.ProfileId))
                {
                    throw row.Error($"duplicate profileId '{profile.ProfileId}'.");
                }

                profiles.Add(profile);
            }

            if (profiles.Count == 0)
            {
                throw new FormatException($"{sourceName} has no player combat profiles.");
            }

            return new PlayerCombatProfileCatalog(sourceId, displayName, profiles);
        }

        private static PlayerCombatProfile CreateProfile(CsvRow row)
        {
            try
            {
                return new PlayerCombatProfile(
                    Required(row, "profileId"),
                    Required(row, "displayName"),
                    PositiveInt(row, "maxHp"),
                    NonNegativeInt(row, "movePoints"),
                    PositiveInt(row, "attackRange"),
                    NonNegativeInt(row, "attackDamage"),
                    NonNegativeInt(row, "defenseBlock"),
                    PositiveInt(row, "actionBudget"),
                    PositiveInt(row, "movementHandSize"),
                    PositiveInt(row, "actionHandSize"),
                    NonNegativeInt(row, "visionRange"),
                    Optional(row, "status"),
                    Optional(row, "designerNote"));
            }
            catch (ArgumentOutOfRangeException ex)
            {
                throw row.Error(ex.Message);
            }
            catch (ArgumentException ex)
            {
                throw row.Error(ex.Message);
            }
        }

        private static string Required(CsvRow row, string column)
        {
            var value = Optional(row, column);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw row.Error($"required column '{column}' is empty.");
            }

            return value;
        }

        private static string Optional(CsvRow row, string column) => row.TryGet(column, out var value) ? value.Trim() : string.Empty;

        private static int PositiveInt(CsvRow row, string column)
        {
            var value = Int(row, column);
            if (value <= 0)
            {
                throw row.Error($"column '{column}' must be greater than zero.");
            }

            return value;
        }

        private static int NonNegativeInt(CsvRow row, string column)
        {
            var value = Int(row, column);
            if (value < 0)
            {
                throw row.Error($"column '{column}' must be zero or greater.");
            }

            return value;
        }

        private static int Int(CsvRow row, string column)
        {
            var raw = Required(row, column);
            if (!int.TryParse(raw, out var value))
            {
                throw row.Error($"column '{column}' value '{raw}' is not a valid integer.");
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
