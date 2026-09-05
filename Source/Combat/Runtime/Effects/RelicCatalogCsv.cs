using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 유물·저주 CSV(<see cref="CombatCsvPaths.RelicsCsv"/>)를 <see cref="RelicCatalogDefinition"/>로
    /// 변환한다. <c>StatusEffectCatalogCsv</c>와 동일한 런타임 직접 파싱형이며, 베이크 산출물은 없다.
    /// 컬럼: id, kind, displayNameKo, descriptionKo, effectKind, effectAmount, extraEffects(kind:amount;…),
    /// durationTurns, triggerRef, triggerParam, rarity, priceDelta, iconId.
    /// rarity는 상점 기준가를 고르는 내부 축이고 <b>플레이어에게 노출되지 않는다</b>
    /// (DEC-2026-08-31-01 Q1) — Basic은 상점에 서지 않으므로 저작도 거부한다.
    /// kind 'Curse'는 저작 금지(T2, 2026-08-06) — 저주는 카드(cards.csv X행)로만 존재한다.
    /// 트리거 유물(T2 페이즈 C)은 triggerRef가 <see cref="RelicTriggerRegistry"/>에 등록돼 있어야 하며
    /// (미등록 ref는 임포트 거부 — BossMechanicRegistry 선례), effectKind는 None으로 저작해
    /// 수치(effectAmount)가 스칼라 합산(SumEffect)에 새지 않게 한다.
    /// </summary>
    public static class RelicCatalogCsv
    {
        public static RelicCatalogDefinition ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Relic CSV path is required.", nameof(path));
            }

            return ConvertText(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
        }

        public static RelicCatalogDefinition ConvertText(string csvText, string sourceName = "relics.csv")
        {
            var table = CsvTable.Parse(csvText, string.IsNullOrWhiteSpace(sourceName) ? "relics.csv" : sourceName);
            var rows = new List<PlayerPermanentItemDefinition>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in table.Rows)
            {
                var id = Required(row, "id");
                if (!seen.Add(id))
                {
                    throw new ArgumentException($"{row.FileName}:{row.LineNumber} duplicates id '{id}'.");
                }

                var kind = ParseEnum<PlayerPermanentItemKind>(Required(row, "kind"), row.FileName, row.LineNumber, "kind");
                if (kind == PlayerPermanentItemKind.Curse)
                {
                    // T2(2026-08-06): 저주 유물 전면 폐기 — enum 값은 세이브 호환 때문에 남지만 신규 저작은
                    // 임포트 시점에 막는다(Burn 유령 값과 같은 처방).
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} kind 'Curse'는 저작 금지 — 저주는 카드(cards.csv X행)로만 저작한다.");
                }
                var effectKind = ParseEnum<PlayerPermanentItemEffectKind>(
                    Optional(row, "effectKind") is var raw && string.IsNullOrWhiteSpace(raw) ? nameof(PlayerPermanentItemEffectKind.None) : raw,
                    row.FileName,
                    row.LineNumber,
                    "effectKind");

                var triggerRef = Optional(row, "triggerRef");
                var triggerKind = RelicTriggerKind.None;
                if (!string.IsNullOrWhiteSpace(triggerRef) && !RelicTriggerRegistry.TryGet(triggerRef, out triggerKind))
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} triggerRef '{triggerRef}'는 미등록 트리거다. "
                        + $"등록된 ref: {string.Join(", ", RelicTriggerRegistry.RegisteredRefs)}");
                }

                var rarityRaw = Optional(row, "rarity");
                var rarity = string.IsNullOrWhiteSpace(rarityRaw)
                    ? SeoulPlayup.CardCore.CardRarity.Rare
                    : ParseEnum<SeoulPlayup.CardCore.CardRarity>(rarityRaw, row.FileName, row.LineNumber, "rarity");
                if (rarity == SeoulPlayup.CardCore.CardRarity.Basic)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} rarity 'Basic'은 저작 금지 — 상점 가격표에 Basic 티어가 없다.");
                }

                var triggerParam = Int(row, "triggerParam", 0);
                if (RelicTriggerRegistry.RequiresParam(triggerKind) && triggerParam <= 0)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} 트리거 '{triggerRef}'는 양수 triggerParam(주기/문턱)이 필요하다.");
                }

                if (triggerKind != RelicTriggerKind.None && effectKind != PlayerPermanentItemEffectKind.None)
                {
                    // 트리거 수치는 effectAmount가 들되 effectKind는 None이어야 한다 — 아니면 같은 수치가
                    // 트리거 1회분과 상시 스칼라 합산에 이중으로 적용된다.
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} 트리거 유물은 effectKind를 None으로 저작한다"
                        + $" (저작값: '{effectKind}' — SumEffect 이중 적용 방지).");
                }

                rows.Add(new PlayerPermanentItemDefinition(
                    id,
                    kind,
                    Optional(row, "displayNameKo"),
                    Optional(row, "descriptionKo"),
                    effectKind,
                    Int(row, "effectAmount", 0),
                    ParseExtraEffects(Optional(row, "extraEffects"), row.FileName, row.LineNumber),
                    Int(row, "durationTurns", 0),
                    triggerKind,
                    triggerParam,
                    rarity,
                    Int(row, "priceDelta", 0),
                    Optional(row, "iconId")));
            }

            return new RelicCatalogDefinition(rows);
        }

        /// <summary>양날 유물(T2 페이즈 B)의 부가 효과 목록 — `kind:amount;…`.</summary>
        private static IReadOnlyList<PlayerPermanentItemEffect> ParseExtraEffects(string raw, string fileName, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return Array.Empty<PlayerPermanentItemEffect>();
            }

            var effects = new List<PlayerPermanentItemEffect>();
            foreach (var token in raw.Split(';'))
            {
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var separator = token.IndexOf(':');
                if (separator <= 0 || separator >= token.Length - 1)
                {
                    throw new ArgumentException($"{fileName}:{lineNumber} extraEffects 항목 '{token}'은 kind:amount 형식이어야 한다.");
                }

                var kind = ParseEnum<PlayerPermanentItemEffectKind>(token.Substring(0, separator).Trim(), fileName, lineNumber, "extraEffects");
                if (!int.TryParse(token.Substring(separator + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var amount))
                {
                    throw new ArgumentException($"{fileName}:{lineNumber} extraEffects amount '{token}'은 정수여야 한다.");
                }

                effects.Add(new PlayerPermanentItemEffect(kind, amount));
            }

            return effects;
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

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new ArgumentException($"{row.FileName}:{row.LineNumber} column '{column}' must be an integer.");
            }

            return parsed;
        }

        private static T ParseEnum<T>(string value, string fileName, int lineNumber, string column) where T : struct
        {
            if (Enum.TryParse<T>(value, ignoreCase: false, out var parsed))
            {
                return parsed;
            }

            throw new ArgumentException($"{fileName}:{lineNumber} column '{column}' has unknown value '{value}'.");
        }
    }
}
