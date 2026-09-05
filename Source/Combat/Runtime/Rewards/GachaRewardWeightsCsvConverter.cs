using System;
using System.Collections.Generic;
using System.IO;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Parses <c>gacha_rewards.csv</c> into <see cref="GachaRewardWeights"/>.
    ///
    /// 카드 등급 분포(<c>combat_rewards.csv</c>)와 **별도 파일**인 이유: 그쪽 스키마의 `rarity`는
    /// `CardRarity` enum이라 카드팩/돈/유물 같은 결과 종류를 표현할 수 없다. 두 표를 한 파일에 넣으면
    /// 컬럼 절반이 행마다 비게 된다.
    ///
    /// 3행짜리라 베이크 없이 런타임 직독한다(<see cref="CardRewardWeightsCsvConverter"/>와 같은 관례).
    /// </summary>
    public static class GachaRewardWeightsCsvConverter
    {
        private static readonly string[] ExpectedHeaders =
        {
            "outcomeKind",
            "weight",
            "minAmount",
            "maxAmount",
            "designerNote"
        };

        public static GachaRewardWeights ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Gacha reward CSV path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required gacha reward CSV file is missing: {path}", path);
            }

            return ConvertText(File.ReadAllText(path), Path.GetFileName(path));
        }

        public static GachaRewardWeights ConvertText(string csvText, string sourceName = "gacha_rewards.csv")
        {
            var table = CsvTable.Parse(csvText ?? string.Empty, sourceName);
            RequireHeaders(table, ExpectedHeaders);

            var entries = new List<GachaRewardWeight>();
            foreach (var row in table.Rows)
            {
                var kind = OutcomeKind(row);
                var weight = NonNegativeInt(row, "weight");
                var min = NonNegativeInt(row, "minAmount");
                var max = NonNegativeInt(row, "maxAmount");

                if (max < min)
                {
                    throw Error(row, $"column 'maxAmount' ({max}) is smaller than 'minAmount' ({min}).");
                }

                // 금액형만 지급 범위를 갖는다. 카드팩/유물 행에 금액이 적혀 있으면 저작자가 그 값이
                // 쓰인다고 믿고 있다는 뜻이므로 조용히 무시하지 말고 거부한다.
                if (kind != GachaRewardKind.Money && (min > 0 || max > 0))
                {
                    throw Error(row, $"outcome '{kind}' must leave minAmount/maxAmount at 0; only Money pays an amount.");
                }

                if (kind == GachaRewardKind.Money && max <= 0)
                {
                    throw Error(row, "outcome 'Money' needs a positive maxAmount, otherwise it always pays 0.");
                }

                entries.Add(new GachaRewardWeight(kind, weight, min, max));
            }

            if (entries.Count == 0)
            {
                throw new FormatException($"{table.Name}: at least one outcome row is required.");
            }

            try
            {
                return new GachaRewardWeights(entries);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"{table.Name}: {ex.Message}", ex);
            }
        }

        private static GachaRewardKind OutcomeKind(CsvRow row)
        {
            var raw = Required(row, "outcomeKind");
            if (!Enum.TryParse<GachaRewardKind>(raw, ignoreCase: true, out var kind) || !Enum.IsDefined(typeof(GachaRewardKind), kind))
            {
                // 저주를 여기 적으려는 시도도 이 경로로 거부된다 — GachaRewardKind에 Curse가 없다.
                throw Error(row, $"column 'outcomeKind' value '{raw}' is not a known gacha outcome (CardPack, Money, Relic).");
            }

            return kind;
        }

        private static string Required(CsvRow row, string column)
        {
            var value = row.TryGet(column, out var raw) ? raw.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                throw Error(row, $"required column '{column}' is empty.");
            }

            return value;
        }

        private static int NonNegativeInt(CsvRow row, string column)
        {
            var raw = Required(row, column);
            if (!int.TryParse(raw, out var value))
            {
                throw Error(row, $"column '{column}' value '{raw}' is not a valid integer.");
            }

            if (value < 0)
            {
                throw Error(row, $"column '{column}' must be zero or greater.");
            }

            return value;
        }

        private static void RequireHeaders(CsvTable table, IReadOnlyList<string> expected)
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

        private static FormatException Error(CsvRow row, string message)
            => new FormatException($"{row.FileName} line {row.LineNumber}: {message}");
    }
}
