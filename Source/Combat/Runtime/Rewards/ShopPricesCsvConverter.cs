using System;
using System.Collections.Generic;
using System.IO;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Parses <c>shop_prices.csv</c> into <see cref="ShopPrices"/>.
    ///
    /// <c>combat_rewards.csv</c>·<c>gacha_rewards.csv</c>와 **별도 파일**인 이유: 앞의 둘은 추첨
    /// 분포(가중치)이고 이것은 가격표라 컬럼 의미가 다르다. 한 파일에 넣으면 행마다 절반이 빈다.
    ///
    /// 몇 행짜리라 베이크 없이 런타임 직독한다(<see cref="GachaRewardWeightsCsvConverter"/>와 같은 관례).
    /// </summary>
    public static class ShopPricesCsvConverter
    {
        private static readonly string[] ExpectedHeaders =
        {
            "itemKind",
            "rarity",
            "price",
            "designerNote"
        };

        public static ShopPrices ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Shop price CSV path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required shop price CSV file is missing: {path}", path);
            }

            return ConvertText(File.ReadAllText(path), Path.GetFileName(path));
        }

        public static ShopPrices ConvertText(string csvText, string sourceName = "shop_prices.csv")
        {
            var table = CsvTable.Parse(csvText ?? string.Empty, sourceName);
            RequireHeaders(table, ExpectedHeaders);

            var entries = new List<ShopPriceEntry>();
            foreach (var row in table.Rows)
            {
                var kind = ItemKind(row);
                var price = PositiveInt(row, "price");
                var rarityRaw = row.TryGet("rarity", out var raw) ? raw.Trim() : string.Empty;

                if (ShopPrices.IsTiered(kind))
                {
                    // 카드·유물·소모품은 등급별 기준가를 갖는다(DEC-2026-08-31-01·-02).
                    // 유물·소모품의 행별 변주는 여기가 아니라 각 카탈로그 CSV의 priceDelta가 든다.
                    entries.Add(new ShopPriceEntry(kind, CardRarityValue(row, rarityRaw), price));
                    continue;
                }

                // 등급이 없는 것은 카드 제거뿐이다. 적혀 있으면 저작자가 그 값이 쓰인다고 믿고
                // 있다는 뜻이므로 조용히 무시하지 말고 거부한다.
                if (!string.IsNullOrEmpty(rarityRaw))
                {
                    throw Error(row, $"item '{kind}' must leave the rarity column empty; it has a single price.");
                }

                entries.Add(new ShopPriceEntry(kind, default, price));
            }

            if (entries.Count == 0)
            {
                throw new FormatException($"{table.Name}: at least one price row is required.");
            }

            try
            {
                return new ShopPrices(entries);
            }
            catch (ArgumentException ex)
            {
                throw new FormatException($"{table.Name}: {ex.Message}", ex);
            }
        }

        private static ShopItemKind ItemKind(CsvRow row)
        {
            var raw = Required(row, "itemKind");
            if (!Enum.TryParse<ShopItemKind>(raw, ignoreCase: true, out var kind) || !Enum.IsDefined(typeof(ShopItemKind), kind))
            {
                // 저주를 여기 적으려는 시도도 이 경로로 거부된다 — ShopItemKind에 Curse가 없다.
                throw Error(row, $"column 'itemKind' value '{raw}' is not a known shop item (Card, Relic, CardRemoval).");
            }

            return kind;
        }

        private static CardRarity CardRarityValue(CsvRow row, string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw Error(row, "item 'Card' needs a rarity; card prices are authored per rarity.");
            }

            if (!Enum.TryParse<CardRarity>(raw, ignoreCase: true, out var rarity) || !Enum.IsDefined(typeof(CardRarity), rarity))
            {
                throw Error(row, $"column 'rarity' value '{raw}' is not a known card rarity.");
            }

            if (rarity == CardRarity.Basic)
            {
                // "기본 등급은 상점에 나오지 않는다"는 저작 관례가 아니라 게임 규칙이다(CR-2 선례).
                throw Error(row, "rarity 'Basic' cannot be priced; Basic cards never appear in the shop.");
            }

            return rarity;
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

        private static int PositiveInt(CsvRow row, string column)
        {
            var raw = Required(row, column);
            if (!int.TryParse(raw, out var value))
            {
                throw Error(row, $"column '{column}' value '{raw}' is not a valid integer.");
            }

            // 0가격은 "공짜 판매"가 아니라 저작 실수다 — 지갑 차감(TrySpend)이 0 이하를 거부하므로
            // 그대로 두면 판매 시점에야 터진다. 여기서 미리 막는다.
            if (value <= 0)
            {
                throw Error(row, $"column '{column}' must be positive.");
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
