using System;
using System.Collections.Generic;
using System.IO;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// <c>kill_drop_rates.csv</c> → <see cref="KillDropRates"/>.
    ///
    /// <para>
    /// <c>combat_rewards.csv</c>와 <b>별도 파일</b>인 이유는 <see cref="ShopPricesCsvConverter"/>가
    /// 별도 파일인 이유와 같다: 저쪽은 「뽑혔을 때 어느 등급인가」의 가중치이고 이쪽은 「그것이
    /// 뽑히기는 하는가」의 독립 확률이라 컬럼 의미가 다르다. 한 파일에 넣으면 행마다 절반이 빈다.
    /// </para>
    ///
    /// <para>몇 행짜리라 베이크 없이 런타임 직독한다(가격표·뽑기 가중치와 같은 관례).</para>
    /// </summary>
    public static class KillDropRatesCsvConverter
    {
        private static readonly string[] ExpectedHeaders =
        {
            "tier",
            "dropKind",
            "percent",
            "minAmount",
            "maxAmount",
            "designerNote"
        };

        public static KillDropRates ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Kill drop rate CSV path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required kill drop rate CSV file is missing: {path}", path);
            }

            return ConvertText(File.ReadAllText(path), Path.GetFileName(path));
        }

        public static KillDropRates ConvertText(string csvText, string sourceName = "kill_drop_rates.csv")
        {
            var table = CsvTable.Parse(csvText ?? string.Empty, sourceName);
            RequireHeaders(table, ExpectedHeaders);

            var entries = new List<KillDropRateEntry>();
            foreach (var row in table.Rows)
            {
                var kind = DropKind(row);
                var min = NonNegativeInt(row, "minAmount");
                var max = NonNegativeInt(row, "maxAmount");

                if (kind == KillDropKind.Money)
                {
                    // 금액 없는 돈 행은 "떨어졌는데 0엽전"이라 저작 실수다 — 확률이 0이면 애초에
                    // 안 떨어지므로 금액을 물을 필요도 없다.
                    if (Percent(row) > 0 && max <= 0)
                    {
                        throw Error(row, "money rows with a positive percent need a maxAmount; a 0-coin drop is an authoring slip.");
                    }

                    if (max < min)
                    {
                        throw Error(row, $"maxAmount ({max}) must not be below minAmount ({min}).");
                    }
                }
                else if (min > 0 || max > 0)
                {
                    // 금액이 안 쓰이는 종류에 적혀 있으면 저작자가 그 값이 쓰인다고 믿고 있다는
                    // 뜻이므로 조용히 무시하지 말고 거부한다(ShopPricesCsvConverter의 rarity와 같은 결).
                    throw Error(row, $"drop '{kind}' must leave minAmount/maxAmount at 0; only Money carries an amount.");
                }

                entries.Add(new KillDropRateEntry(Tier(row), kind, Percent(row), min, max));
            }

            if (entries.Count == 0)
            {
                throw new FormatException($"{table.Name}: at least one drop rate row is required.");
            }

            try
            {
                return new KillDropRates(entries);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"{table.Name}: {ex.Message}", ex);
            }
        }

        private static MonsterKillTier Tier(CsvRow row)
        {
            var raw = Required(row, "tier");
            if (!Enum.TryParse<MonsterKillTier>(raw, ignoreCase: true, out var tier) || !Enum.IsDefined(typeof(MonsterKillTier), tier))
            {
                throw Error(row, $"column 'tier' value '{raw}' is not a known kill tier (normal, elite, boss).");
            }

            return tier;
        }

        private static KillDropKind DropKind(CsvRow row)
        {
            var raw = Required(row, "dropKind");
            if (!Enum.TryParse<KillDropKind>(raw, ignoreCase: true, out var kind) || !Enum.IsDefined(typeof(KillDropKind), kind))
            {
                // 부적을 여기 적으려는 시도도 이 경로로 거부된다 — 부적은 확률이 아니라 목록에 언제나 서는 줄이다.
                throw Error(row, $"column 'dropKind' value '{raw}' is not a known drop (Item, Relic, Money).");
            }

            return kind;
        }

        private static int Percent(CsvRow row)
        {
            var raw = Required(row, "percent");
            if (!int.TryParse(raw, out var value))
            {
                throw Error(row, $"column 'percent' value '{raw}' is not a valid integer.");
            }

            // 0(안 떨어진다)과 100(확정)은 둘 다 뜻이 있는 저작이다 — 범위 밖만 막는다.
            if (value < 0 || value > 100)
            {
                throw Error(row, $"column 'percent' must be between 0 and 100; got {value}.");
            }

            return value;
        }

        private static int NonNegativeInt(CsvRow row, string column)
        {
            var raw = row.TryGet(column, out var value) ? value.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0;
            }

            if (!int.TryParse(raw, out var parsed))
            {
                throw Error(row, $"column '{column}' value '{raw}' is not a valid integer.");
            }

            if (parsed < 0)
            {
                throw Error(row, $"column '{column}' must not be negative.");
            }

            return parsed;
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
