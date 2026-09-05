using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Pure importer for game_keywords.csv (분류,키워드,효과). Mirrors the StatusEffectCatalogCsv style:
    /// designer-friendly errors, UTF-8, no UnityEngine. The 효과 column never contains commas in the
    /// authored data, but we still keep it as the trailing remainder so future commas survive.
    /// </summary>
    public static class KeywordCatalogCsv
    {
        private const string CategoryHeader = "분류";
        private const string KeywordHeader = "키워드";
        private const string ValueHeader = "값";
        private const string ValueKindHeader = "값종류";
        private const string EffectHeader = "효과";

        public static KeywordCatalogDefinition ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Game keywords CSV path is required.", nameof(path));
            }

            return ConvertText(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
        }

        public static KeywordCatalogDefinition ConvertText(string csvText, string sourceName = "game_keywords.csv")
        {
            var name = string.IsNullOrWhiteSpace(sourceName) ? "game_keywords.csv" : sourceName;
            var lines = SplitLines(csvText ?? string.Empty);
            if (lines.Count == 0)
            {
                throw new ArgumentException($"{name} is empty.");
            }

            // Two authored layouts are accepted: the legacy 3-column (분류,키워드,효과) and the extended
            // 5-column (분류,키워드,값,값종류,효과). 효과 is always the trailing remainder column.
            var header = SplitColumns(lines[0], int.MaxValue);
            var extended = HeaderIsExtended(header);
            var columnCount = extended ? 5 : 3;
            if (!HeaderIsLegacy(header) && !extended)
            {
                throw new ArgumentException(
                    $"{name}:1 expected header '{CategoryHeader},{KeywordHeader},{EffectHeader}' or " +
                    $"'{CategoryHeader},{KeywordHeader},{ValueHeader},{ValueKindHeader},{EffectHeader}'.");
            }

            var rows = new List<KeywordDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 1; i < lines.Count; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var columns = SplitColumns(line, columnCount);
                if (columns.Length < columnCount)
                {
                    throw new ArgumentException($"{name}:{i + 1} has {columns.Length} columns but expected {columnCount}.");
                }

                var category = columns[0].Trim();
                var keyword = columns[1].Trim();
                var value = extended ? columns[2].Trim() : string.Empty;
                var valueKind = extended ? ParseValueKind(columns[3].Trim()) : KeywordValueKind.None;
                var effect = (extended ? columns[4] : columns[2]).Trim();

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    throw new ArgumentException($"{name}:{i + 1} missing required column '{KeywordHeader}'.");
                }

                if (!seen.Add(keyword))
                {
                    throw new ArgumentException($"{name}:{i + 1} duplicates keyword '{keyword}'.");
                }

                rows.Add(new KeywordDefinition(category, keyword, effect, valueKind, value));
            }

            return new KeywordCatalogDefinition(rows);
        }

        private static bool HeaderIsLegacy(string[] header)
            => header.Length >= 3
               && header[0].Trim() == CategoryHeader
               && header[1].Trim() == KeywordHeader
               && header[2].Trim() == EffectHeader;

        private static bool HeaderIsExtended(string[] header)
            => header.Length >= 5
               && header[0].Trim() == CategoryHeader
               && header[1].Trim() == KeywordHeader
               && header[2].Trim() == ValueHeader
               && header[3].Trim() == ValueKindHeader
               && header[4].Trim() == EffectHeader;

        private static KeywordValueKind ParseValueKind(string raw)
        {
            switch (raw)
            {
                case "고정":
                case "Fixed":
                case "fixed":
                    return KeywordValueKind.Fixed;
                case "유동":
                case "동적":
                case "Dynamic":
                case "dynamic":
                    return KeywordValueKind.Dynamic;
                default:
                    return KeywordValueKind.None;
            }
        }

        private static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            var sb = new StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    lines.Add(sb.ToString());
                    sb.Length = 0;
                }
                else if (c == '\n')
                {
                    lines.Add(sb.ToString());
                    sb.Length = 0;
                }
                else
                {
                    sb.Append(c);
                }
            }

            if (sb.Length > 0)
            {
                lines.Add(sb.ToString());
            }

            return lines;
        }

        // Leading commas split the fixed-width columns; the final column (효과) keeps any remaining commas
        // verbatim. 분류/키워드/값/값종류 never contain commas in the authored data.
        private static string[] SplitColumns(string line, int maxColumns)
            => maxColumns == int.MaxValue ? line.Split(',') : line.Split(new[] { ',' }, maxColumns);
    }
}
