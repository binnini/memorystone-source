using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace SeoulPlayup.Combat.Runtime
{
    public static class StatusEffectCatalogCsv
    {
        public static StatusEffectCatalogDefinition ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Status effect CSV path is required.", nameof(path));
            }

            return ConvertText(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
        }

        public static StatusEffectCatalogDefinition ConvertText(string csvText, string sourceName = "status_effects.csv")
        {
            var table = CsvTable.Parse(csvText, string.IsNullOrWhiteSpace(sourceName) ? "status_effects.csv" : sourceName);
            var rows = new List<StatusEffectDefinition>();
            var seen = new HashSet<StatusEffectKind>();

            foreach (var row in table.Rows)
            {
                var kind = ParseEnum<StatusEffectKind>(Required(row, "effectKind"), row.FileName, row.LineNumber, "effectKind");
                if (!seen.Add(kind))
                {
                    throw new ArgumentException($"{row.FileName}:{row.LineNumber} duplicates effectKind '{kind}'.");
                }

                var valueMode = ParseEnum<StatusEffectValueMode>(Required(row, "valueMode"), row.FileName, row.LineNumber, "valueMode");
                var timing = ParseEnum<StatusEffectTiming>(Required(row, "timing"), row.FileName, row.LineNumber, "timing");
                var stackPolicy = ParseEnum<StatusEffectStackPolicy>(Required(row, "stackPolicy"), row.FileName, row.LineNumber, "stackPolicy");
                var expirePolicy = ParseEnum<StatusEffectExpirePolicy>(Required(row, "expirePolicy"), row.FileName, row.LineNumber, "expirePolicy");
                var maxStacks = Int(row, "maxStacks", 1);

                ValidateTimingMatchesValueMode(valueMode, timing, row);
                ValidateMaxStacks(stackPolicy, maxStacks, row);

                var definition = new StatusEffectDefinition(
                    kind,
                    Required(row, "displayNameKo"),
                    Optional(row, "descriptionKo"),
                    Int(row, "defaultAmount", 0),
                    Int(row, "defaultDurationTurns", 0),
                    valueMode,
                    timing,
                    stackPolicy,
                    maxStacks,
                    expirePolicy);

                rows.Add(WithPresentationColumns(definition, table, row));
            }

            return new StatusEffectCatalogDefinition(rows);
        }

        /// <summary>
        /// timing은 서술 전용이지만 <b>거짓말은 못 하게</b> 막는다: Amount를 읽는 지점은 valueMode 배선이
        /// 결정하므로, 그 짝이 맞지 않는 timing은 문서로서 틀린 것이고 읽는 사람을 오도한다.
        /// (허구였던 값을 실제로 두 개 잡았다 — 반사 `AfterIncomingDamage`, 민첩 `AfterMove`. 둘 다
        /// 그렇게 만료되지 않는다.)
        /// </summary>
        private static void ValidateTimingMatchesValueMode(
            StatusEffectValueMode valueMode,
            StatusEffectTiming timing,
            CsvRow row)
        {
            // valueMode == None은 Amount를 읽지 않으므로 "존재 여부를 어디서 보는가"를 서술한다 —
            // 값 축과 짝이 없어 두 지점 중 하나면 된다.
            if (valueMode == StatusEffectValueMode.None)
            {
                if (timing != StatusEffectTiming.BeforeMoveRangeCalc && timing != StatusEffectTiming.OnActionCheck)
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} valueMode 'None'은 존재 검사 지점만 서술할 수 있다"
                        + $" (BeforeMoveRangeCalc / OnActionCheck). 저작값: '{timing}'.");
                }

                return;
            }

            var expected = ExpectedTiming(valueMode);
            if (timing != expected)
            {
                throw new ArgumentException(
                    $"{row.FileName}:{row.LineNumber} valueMode '{valueMode}'는 '{expected}'에서 읽힌다"
                    + $" (소비 코드 위치). timing '{timing}'은 실제와 다르다 — 둘 중 하나가 틀렸다.");
            }
        }

        private static StatusEffectTiming ExpectedTiming(StatusEffectValueMode valueMode)
        {
            switch (valueMode)
            {
                case StatusEffectValueMode.DamagePerTurn:
                    return StatusEffectTiming.TurnStart;
                case StatusEffectValueMode.MoveRangeBonus:
                case StatusEffectValueMode.MoveRangePenalty:
                    return StatusEffectTiming.BeforeMoveRangeCalc;
                case StatusEffectValueMode.BlockGainPenalty:
                    return StatusEffectTiming.OnBlockGain;
                case StatusEffectValueMode.ReflectGate:
                    return StatusEffectTiming.OnIncomingDamage;
                case StatusEffectValueMode.DamageDealtBonusPercent:
                case StatusEffectValueMode.DamageDealtPenaltyPercent:
                // 힘(2026-09-04)도 나가는 피해에서 소비된다 — 배율 항이 곱해진 뒤 더해질 뿐 경계는 같다.
                case StatusEffectValueMode.DamageDealtBonusFlat:
                    return StatusEffectTiming.OnOutgoingDamage;
                case StatusEffectValueMode.IncomingDamageBonusFlat:
                    return StatusEffectTiming.OnIncomingDamage;
                case StatusEffectValueMode.SealedCardCount:
                    return StatusEffectTiming.OnActionCheck;
                case StatusEffectValueMode.VisionRangePenalty:
                case StatusEffectValueMode.VisionRangeBonus:
                    return StatusEffectTiming.BeforeVisionCalc;
                case StatusEffectValueMode.StatusNegationCharges:
                    return StatusEffectTiming.OnStatusApply;
                default:
                    throw new ArgumentOutOfRangeException(nameof(valueMode), valueMode, "소비 지점이 정의되지 않은 valueMode.");
            }
        }

        /// <summary>
        /// 현행 두 stackPolicy는 모두 인스턴스를 하나로 병합한다 — maxStacks가 1이 아니면 저작이
        /// 성립하지 않는 상태를 서술하는 것이다. (중독·파열이 99로 저작돼 있었는데, 실제로는 하나로
        /// 병합되고 Amount만 합산된다. "스택 99"는 인스턴스가 아니라 수치 누적을 뜻한 표기였다.)
        /// </summary>
        private static void ValidateMaxStacks(StatusEffectStackPolicy stackPolicy, int maxStacks, CsvRow row)
        {
            if (maxStacks != 1)
            {
                throw new ArgumentException(
                    $"{row.FileName}:{row.LineNumber} stackPolicy '{stackPolicy}'는 인스턴스를 하나로 병합하므로"
                    + $" maxStacks는 1이어야 한다. 저작값: {maxStacks}."
                    + " 수치 누적을 뜻했다면 그건 stackPolicy 'Add'가 이미 표현한다.");
            }
        }

        /// <summary>
        /// 표현 컬럼 4개(<c>glyph,badgeSortRank,floatingTextTemplate,applyAudioCueId</c>). 헤더에 컬럼이
        /// <b>없으면</b> 폴백 switch 값을 그대로 쓴다(옛 10컬럼 CSV·테스트 픽스처 호환). 헤더에 있는데
        /// 값이 비면 그 빈 값이 저작이다 — 큐 id 빈 값은 「무음」, 템플릿 빈 값은 「표시명만」이라 0/빈
        /// 문자열로 떨어지는 것이 아니라 뜻이 있다.
        /// </summary>
        private static StatusEffectDefinition WithPresentationColumns(StatusEffectDefinition definition, CsvTable table, CsvRow row)
        {
            var kind = definition.Kind;
            var glyph = Has(table, "glyph") ? Optional(row, "glyph") : definition.Glyph;
            var badgeSortRank = Has(table, "badgeSortRank") ? Int(row, "badgeSortRank", definition.BadgeSortRank) : definition.BadgeSortRank;
            var floatingTextTemplate = Has(table, "floatingTextTemplate") ? Optional(row, "floatingTextTemplate") : definition.FloatingTextTemplate;
            var applyAudioCueId = Has(table, "applyAudioCueId") ? Optional(row, "applyAudioCueId") : definition.ApplyAudioCueId;

            if (Has(table, "glyph") && string.IsNullOrWhiteSpace(glyph))
            {
                throw new ArgumentException($"{row.FileName}:{row.LineNumber} glyph is empty for '{kind}' — 스프라이트가 없을 때 빈칸이 뜬다.");
            }

            return definition.WithPresentation(glyph, badgeSortRank, floatingTextTemplate, applyAudioCueId);
        }

        private static bool Has(CsvTable table, string column)
        {
            foreach (var header in table.Headers)
            {
                if (string.Equals(header, column, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
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
