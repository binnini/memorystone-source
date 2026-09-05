using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// monster_traits.csv 순수 임포터(<see cref="KeywordCatalogCsv"/>·<c>StatusEffectCatalogCsv</c>와 같은 문법:
    /// 저작자가 읽을 수 있는 오류, UTF-8, UnityEngine 없음).
    ///
    /// <para>🔴 미등록 <c>reachKind</c>·<c>badgeVisibility</c>는 <b>임포트 시점에 거부</b>한다 —
    /// 오타가 조용히 기본값으로 내려앉으면 「저작은 했는데 아무 일도 안 일어나는」 죽은 컬럼이 된다
    /// (뒤끝·은신 파서와 같은 규약). keywordRef 실재 검증만은 카탈로그가 필요해
    /// <see cref="MonsterTraitCatalogDefinition.ValidateKeywordRefs"/>가 따로 맡는다.</para>
    /// </summary>
    public static class MonsterTraitCatalogCsv
    {
        private static readonly string[] Header =
        {
            "traitId", "displayNameKo", "keywordRef", "glyph", "badgeColorHex",
            "badgeSortRank", "badgeVisibility", "reachKind", "announceRef", "designerNote",
        };

        public static MonsterTraitCatalogDefinition ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Monster traits CSV path is required.", nameof(path));
            }

            return ConvertText(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
        }

        public static MonsterTraitCatalogDefinition ConvertText(string csvText, string sourceName = "monster_traits.csv")
        {
            var name = string.IsNullOrWhiteSpace(sourceName) ? "monster_traits.csv" : sourceName;
            var lines = SplitLines(csvText ?? string.Empty);
            if (lines.Count == 0)
            {
                throw new ArgumentException($"{name} is empty.");
            }

            var header = lines[0].Split(',');
            for (var i = 0; i < Header.Length; i++)
            {
                if (header.Length <= i || header[i].Trim() != Header[i])
                {
                    throw new ArgumentException(
                        $"{name}:1 expected header '{string.Join(",", Header)}'.");
                }
            }

            var rows = new List<MonsterTraitDefinition>();
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenKeywords = new HashSet<string>(StringComparer.Ordinal);

            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                // designerNote가 마지막 컬럼이라 남은 쉼표를 통째로 흡수한다(효과문과 같은 규약).
                var columns = lines[i].Split(new[] { ',' }, Header.Length);
                if (columns.Length < Header.Length)
                {
                    throw new ArgumentException(
                        $"{name}:{i + 1} has {columns.Length} columns but expected {Header.Length}.");
                }

                var traitId = columns[0].Trim();
                if (traitId.Length == 0)
                {
                    throw new ArgumentException($"{name}:{i + 1} missing required column 'traitId'.");
                }

                if (!seenIds.Add(traitId))
                {
                    throw new ArgumentException($"{name}:{i + 1} duplicates traitId '{traitId}'.");
                }

                var displayName = columns[1].Trim();
                if (displayName.Length == 0)
                {
                    throw new ArgumentException($"{name}:{i + 1} trait '{traitId}' missing 'displayNameKo'.");
                }

                var keywordRef = columns[2].Trim();
                // 두 특성이 같은 설명문 행을 가리키면 한쪽을 고칠 때 다른 쪽이 조용히 따라 바뀐다.
                if (keywordRef.Length > 0 && !seenKeywords.Add(keywordRef))
                {
                    throw new ArgumentException(
                        $"{name}:{i + 1} trait '{traitId}' reuses keywordRef '{keywordRef}' — 특성마다 자기 행을 갖는다.");
                }

                rows.Add(new MonsterTraitDefinition(
                    traitId,
                    displayName,
                    keywordRef,
                    columns[3].Trim(),
                    ParseColorHex(columns[4].Trim(), name, i + 1, traitId),
                    ParseInt(columns[5].Trim(), name, i + 1, traitId, "badgeSortRank"),
                    ParseVisibility(columns[6].Trim(), name, i + 1, traitId),
                    ParseReachKind(columns[7].Trim(), name, i + 1, traitId),
                    columns[8].Trim(),
                    columns[9].Trim()));
            }

            return new MonsterTraitCatalogDefinition(rows);
        }

        private static string ParseColorHex(string raw, string name, int line, string traitId)
        {
            var hex = raw.TrimStart('#');
            if (hex.Length != 6)
            {
                throw new ArgumentException(
                    $"{name}:{line} trait '{traitId}' badgeColorHex '{raw}' must be 6 hex digits (RRGGBB).");
            }

            for (var i = 0; i < hex.Length; i++)
            {
                if (!Uri.IsHexDigit(hex[i]))
                {
                    throw new ArgumentException(
                        $"{name}:{line} trait '{traitId}' badgeColorHex '{raw}' is not hexadecimal.");
                }
            }

            return hex.ToUpperInvariant();
        }

        private static int ParseInt(string raw, string name, int line, string traitId, string column)
        {
            if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new ArgumentException($"{name}:{line} trait '{traitId}' {column} '{raw}' is not an integer.");
            }

            return value;
        }

        private static MonsterTraitBadgeVisibility ParseVisibility(string raw, string name, int line, string traitId)
        {
            switch (raw)
            {
                case "always": return MonsterTraitBadgeVisibility.Always;
                case "active": return MonsterTraitBadgeVisibility.Active;
                default:
                    throw new ArgumentException(
                        $"{name}:{line} trait '{traitId}' badgeVisibility '{raw}' is unknown (always|active).");
            }
        }

        private static MonsterTraitReachKind ParseReachKind(string raw, string name, int line, string traitId)
        {
            switch (raw)
            {
                case "none": return MonsterTraitReachKind.None;
                case "aftermath": return MonsterTraitReachKind.Aftermath;
                case "distanceStrength": return MonsterTraitReachKind.DistanceStrength;
                case "auraRadius": return MonsterTraitReachKind.AuraRadius;
                default:
                    throw new ArgumentException(
                        $"{name}:{line} trait '{traitId}' reachKind '{raw}' is unknown "
                        + "(none|aftermath|distanceStrength|auraRadius).");
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
    }
}
