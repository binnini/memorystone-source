using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 소모품 CSV(<see cref="CombatCsvPaths.ConsumableItemsCsv"/>)를
    /// <see cref="ConsumableItemCatalogDefinition"/>으로 변환한다. <see cref="RelicCatalogCsv"/>와
    /// 동일한 런타임 직접 파싱형(베이크 없음). effectRef는 아래 allow-list가 검증한다 —
    /// 미등록 ref가 조용히 통과하면 "사용해도 아무 일도 안 일어나는 아이템"이 출하된다.
    /// </summary>
    public static class ConsumableItemCatalogCsv
    {
        /// <summary>코드 핸들러(<c>CombatState.TryUseBagItem</c>)가 실제로 소비하는 effectRef 전집.</summary>
        private static readonly HashSet<string> KnownEffectRefs = new HashSet<string>(StringComparer.Ordinal)
        {
            ConsumableItemEffectRefs.Heal,
            ConsumableItemEffectRefs.Block,
            ConsumableItemEffectRefs.Cleanse,
            ConsumableItemEffectRefs.Guard,
            ConsumableItemEffectRefs.Ki,
            ConsumableItemEffectRefs.AttackBonus,
            ConsumableItemEffectRefs.StunNearby,
            ConsumableItemEffectRefs.Torch,
            ConsumableItemEffectRefs.BlastDamage,
            ConsumableItemEffectRefs.BlindWeaken,
            ConsumableItemEffectRefs.Stealth,
            ConsumableItemEffectRefs.SpawnObstacle,
        };

        public static ConsumableItemCatalogDefinition ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Consumable item CSV path is required.", nameof(path));
            }

            return ConvertText(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
        }

        public static ConsumableItemCatalogDefinition ConvertText(string csvText, string sourceName = "consumable_items.csv")
        {
            var table = CsvTable.Parse(csvText, string.IsNullOrWhiteSpace(sourceName) ? "consumable_items.csv" : sourceName);
            var rows = new List<ConsumableItemDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in table.Rows)
            {
                var id = Required(row, "id");
                if (!seen.Add(id))
                {
                    throw new ArgumentException($"{row.FileName}:{row.LineNumber} duplicates id '{id}'.");
                }

                var effectRef = Required(row, "effectRef");
                if (!KnownEffectRefs.Contains(effectRef))
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} effectRef '{effectRef}'는 미등록이다. "
                        + $"등록된 ref: {string.Join(", ", KnownEffectRefs)}");
                }

                var targetingRaw = Optional(row, "targeting");
                var targeting = ConsumableItemTargeting.None;
                if (!string.IsNullOrWhiteSpace(targetingRaw)
                    && !Enum.TryParse(targetingRaw, ignoreCase: true, out targeting))
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} targeting '{targetingRaw}'는 None/Enemy/Tile 중 하나여야 한다.");
                }

                var rarityRaw = Optional(row, "rarity");
                var rarity = SeoulPlayup.CardCore.CardRarity.Rare;
                if (!string.IsNullOrWhiteSpace(rarityRaw)
                    && !Enum.TryParse(rarityRaw, ignoreCase: true, out rarity))
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} rarity '{rarityRaw}'는 Rare/Epic/Legendary 중 하나여야 한다.");
                }

                if (rarity == SeoulPlayup.CardCore.CardRarity.Basic)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} rarity 'Basic'은 저작 금지 — 상점 가격표에 Basic 티어가 없다.");
                }

                rows.Add(new ConsumableItemDefinition(
                    id,
                    Optional(row, "displayNameKo"),
                    Optional(row, "descriptionKo"),
                    Optional(row, "category"),
                    targeting,
                    effectRef,
                    Int(row, "amount", 0),
                    Int(row, "durationTurns", 0),
                    Int(row, "radius", 0),
                    rarity,
                    Int(row, "priceDelta", 0),
                    Optional(row, "iconId")));
            }

            return new ConsumableItemCatalogDefinition(rows);
        }

        private static string Required(CsvRow row, string column)
        {
            var value = Optional(row, column);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{row.FileName}:{row.LineNumber} missing required column '{column}'.");
            }

            return value;
        }

        private static string Optional(CsvRow row, string column) => row.TryGet(column, out var value) ? value.Trim() : string.Empty;

        private static int Int(CsvRow row, string column, int fallback)
        {
            var value = Optional(row, column);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (!int.TryParse(value, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                throw new ArgumentException($"{row.FileName}:{row.LineNumber} column '{column}' must be an integer.");
            }

            return parsed;
        }
    }

    /// <summary>소모품 effectRef 상수 — 카드의 <see cref="CardEffectRefs"/>와 같은 결의 단일 사전.</summary>
    public static class ConsumableItemEffectRefs
    {
        public const string Heal = "use.heal";
        public const string Block = "use.block";
        public const string Cleanse = "use.cleanse";
        public const string Guard = "use.guard";
        public const string Ki = "use.ki";
        public const string AttackBonus = "use.attack_bonus";
        public const string StunNearby = "use.stun_nearby";
        public const string Torch = "use.torch";
        public const string BlastDamage = "use.blast_damage";
        public const string BlindWeaken = "use.blind_weaken";
        public const string Stealth = "use.stealth";
        public const string SpawnObstacle = "use.spawn_obstacle";
    }
}
