using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class BossCatalogCsvSource
    {
        public BossCatalogCsvSource(
            string bossProfilesCsv,
            string bossPhasesCsv,
            string sourceId = "designer-boss-csv",
            string displayName = "Designer Boss CSV Catalog")
        {
            BossProfilesCsv = bossProfilesCsv ?? string.Empty;
            BossPhasesCsv = bossPhasesCsv ?? string.Empty;
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? "designer-boss-csv" : sourceId;
            DisplayName = displayName ?? string.Empty;
        }

        public string BossProfilesCsv { get; }
        public string BossPhasesCsv { get; }
        public string SourceId { get; }
        public string DisplayName { get; }

        public static BossCatalogCsvSource FromDirectory(
            string directoryPath,
            string sourceId = "designer-boss-csv",
            string displayName = "Designer Boss CSV Catalog")
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("Boss CSV directory path is required.", nameof(directoryPath));
            }

            return new BossCatalogCsvSource(
                ReadRequired(directoryPath, "boss_profiles.csv"),
                ReadRequired(directoryPath, "boss_phases.csv"),
                sourceId,
                displayName);
        }

        private static string ReadRequired(string directoryPath, string fileName)
        {
            var path = Path.Combine(directoryPath, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required boss CSV file is missing: {path}", path);
            }

            return File.ReadAllText(path, Encoding.UTF8);
        }
    }

    /// <summary>
    /// <c>boss_profiles.csv</c> + <c>boss_phases.csv</c> → <see cref="BossCatalogDefinition"/>.
    ///
    /// 저작으로 규칙을 깨뜨릴 수 없게 하는 것이 이 파서의 존재 이유다. 거부하는 것들:
    /// 미등록 <c>mechanicId</c>, 불연속/1에서 시작하지 않는 <c>phaseIndex</c>, 오름차순이 아닌
    /// 정규화 임계값, 0이 아닌 1페이즈 임계값, 줄어드는 <c>maxHpBonus</c>(최대 체력 감소 불가),
    /// 페이즈 행이 없는 보스, 프로필 없는 페이즈 행.
    /// </summary>
    public static class BossCatalogCsvConverter
    {
        private static readonly Regex BossIdPattern = new Regex("^M[0-9]{3}$", RegexOptions.Compiled);

        public static BossCatalogDefinition Convert(BossCatalogCsvSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var profileTable = CsvTable.Parse(source.BossProfilesCsv, "boss_profiles.csv");
            var phaseTable = CsvTable.Parse(source.BossPhasesCsv, "boss_phases.csv");

            var profileRows = ReadProfileRows(profileTable);
            var phaseRowsByBoss = ReadPhaseRows(phaseTable, profileRows);

            var profiles = new List<BossProfileDefinition>();
            foreach (var profileRow in profileRows.Values)
            {
                if (!phaseRowsByBoss.TryGetValue(profileRow.BossId, out var phaseRows) || phaseRows.Count == 0)
                {
                    throw new ArgumentException(
                        $"{phaseTable.Name} has no phase rows for bossId '{profileRow.BossId}'; every boss profile needs at least one phase.");
                }

                // 키 존재 검증은 프로필 행을 읽을 때 이미 끝났고, 여기서는 페이즈 수를 알아야만 할 수 있는
                // 검증(페이즈별 리스트 길이 등)을 기믹에게 맡긴다.
                try
                {
                    BossMechanicRegistry.ValidateParams(profileRow.MechanicId, profileRow.MechanicParams, phaseRows.Count);
                }
                catch (ArgumentException error)
                {
                    throw new ArgumentException(
                        $"{profileTable.Name} boss '{profileRow.BossId}' mechanicParams is invalid: {error.Message}", error);
                }

                profiles.Add(new BossProfileDefinition(
                    profileRow.BossId,
                    profileRow.DisplayName,
                    profileRow.PhaseMetric,
                    profileRow.MechanicId,
                    profileRow.MechanicParams,
                    profileRow.IntroCinematic,
                    profileRow.DeathCinematic,
                    profileRow.BgmCueBase,
                    profileRow.DesignerNote,
                    phaseRows));
            }

            return new BossCatalogDefinition(source.SourceId, source.DisplayName, profiles);
        }

        public static BossCatalogDefinition ConvertDirectory(
            string directoryPath,
            string sourceId = "designer-boss-csv",
            string displayName = "Designer Boss CSV Catalog")
        {
            return Convert(BossCatalogCsvSource.FromDirectory(directoryPath, sourceId, displayName));
        }

        private sealed class ProfileRow
        {
            public string BossId;
            public string DisplayName;
            public BossPhaseMetricKind PhaseMetric;
            public string MechanicId;
            public Dictionary<string, string> MechanicParams;
            public string IntroCinematic;
            public string DeathCinematic;
            public string BgmCueBase;
            public string DesignerNote;
        }

        private static Dictionary<string, ProfileRow> ReadProfileRows(CsvTable table)
        {
            var result = new Dictionary<string, ProfileRow>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                var bossId = Required(row, "bossId");
                if (!BossIdPattern.IsMatch(bossId))
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} bossId '{bossId}' does not match required format (M###).");
                }

                if (result.ContainsKey(bossId))
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} duplicates bossId '{bossId}'.");
                }

                var mechanicId = Optional(row, "mechanicId");
                if (!BossMechanicRegistry.IsValidMechanicId(mechanicId))
                {
                    var registered = BossMechanicRegistry.RegisteredIds.Count == 0
                        ? "(none registered)"
                        : string.Join(", ", BossMechanicRegistry.RegisteredIds);
                    throw new ArgumentException(
                        $"{table.Name}:{row.LineNumber} mechanicId '{mechanicId}' has no registered IBossMechanic implementation. Registered: {registered}.");
                }

                var mechanicParams = ParseMechanicParams(Optional(row, "mechanicParams"), table.Name, row.LineNumber);
                foreach (var requiredKey in BossMechanicRegistry.GetRequiredParamKeys(mechanicId))
                {
                    if (!mechanicParams.ContainsKey(requiredKey))
                    {
                        throw new ArgumentException(
                            $"{table.Name}:{row.LineNumber} mechanicId '{mechanicId}' requires mechanicParams key '{requiredKey}'.");
                    }
                }

                result.Add(bossId, new ProfileRow
                {
                    BossId = bossId,
                    DisplayName = Required(row, "displayName"),
                    PhaseMetric = ParseEnum<BossPhaseMetricKind>(Required(row, "phaseMetric"), table.Name, row.LineNumber, "phaseMetric"),
                    MechanicId = mechanicId,
                    MechanicParams = mechanicParams,
                    IntroCinematic = Optional(row, "introCinematic"),
                    DeathCinematic = Optional(row, "deathCinematic"),
                    BgmCueBase = Optional(row, "bgmCueBase"),
                    DesignerNote = Optional(row, "designerNote")
                });
            }

            if (result.Count == 0)
            {
                throw new ArgumentException($"{table.Name} has no boss profile rows.");
            }

            return result;
        }

        private static Dictionary<string, List<BossPhaseDefinition>> ReadPhaseRows(
            CsvTable table,
            IReadOnlyDictionary<string, ProfileRow> profileRows)
        {
            var grouped = new Dictionary<string, List<BossPhaseDefinition>>(StringComparer.Ordinal);

            foreach (var row in table.Rows)
            {
                var bossId = Required(row, "bossId");
                if (!profileRows.TryGetValue(bossId, out var profileRow))
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} references bossId '{bossId}' with no boss_profiles.csv row.");
                }

                var phaseIndex = Int(row, "phaseIndex", 0);
                var authoredThreshold = Int(row, "threshold", 0);
                var progressThreshold = NormalizeThreshold(profileRow.PhaseMetric, authoredThreshold, table.Name, row.LineNumber);
                var visualScale = Float(row, "visualScale", 1f);
                if (visualScale <= 0f)
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} visualScale must be greater than 0.");
                }

                var footprintRadius = Int(row, "footprintRadius", 0);
                if (footprintRadius < 0 || footprintRadius > 2)
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} footprintRadius must be 0, 1 or 2.");
                }

                // 페이즈별 몸 형상(2026-09-04 §12 — 불가살 P2 tri). 컬럼 없음·빈 값 = 원판/한 칸(구 CSV·픽스처 호환).
                if (!MonsterFootprints.TryParse(Optional(row, "footprintShape"), out var footprintShape, out var footprintShapeError))
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} {footprintShapeError}");
                }

                if (footprintShape != MonsterFootprintShape.Single && footprintRadius != 0)
                {
                    throw new ArgumentException(
                        $"{table.Name}:{row.LineNumber} footprintShape '{MonsterFootprints.TriangleToken}'는 footprintRadius=0과만 저작한다"
                        + " — 두 축이 함께 적히면 어느 쪽 몸이 정본인지 못 읽는다.");
                }

                var maxHpBonus = Int(row, "maxHpBonus", 0);
                if (maxHpBonus < 0)
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} maxHpBonus cannot be negative (max HP can never decrease).");
                }

                var strengthBonusPercent = Int(row, "strengthBonusPercent", 0);
                if (strengthBonusPercent < 0)
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} strengthBonusPercent cannot be negative.");
                }

                var patternPhaseMin = Int(row, "patternPhaseMin", 0);
                if (patternPhaseMin < 0)
                {
                    throw new ArgumentException($"{table.Name}:{row.LineNumber} patternPhaseMin cannot be negative.");
                }

                var auraColumn = Optional(row, "auraStatusKind");
                StatusEffectKind? auraStatusKind = string.IsNullOrWhiteSpace(auraColumn)
                    ? (StatusEffectKind?)null
                    : ParseEnum<StatusEffectKind>(auraColumn, table.Name, row.LineNumber, "auraStatusKind");

                if (!grouped.TryGetValue(bossId, out var phases))
                {
                    phases = new List<BossPhaseDefinition>();
                    grouped.Add(bossId, phases);
                }

                if (phaseIndex != phases.Count + 1)
                {
                    throw new ArgumentException(
                        $"{table.Name}:{row.LineNumber} boss '{bossId}' phaseIndex must start at 1 and increase by 1 per row (expected {phases.Count + 1}, got {phaseIndex}).");
                }

                if (phases.Count == 0)
                {
                    if (progressThreshold != 0)
                    {
                        throw new ArgumentException(
                            $"{table.Name}:{row.LineNumber} boss '{bossId}' phase 1 must be entered from the start (normalized threshold 0, got {progressThreshold}).");
                    }
                }
                else
                {
                    var previous = phases[phases.Count - 1];
                    if (progressThreshold <= previous.ProgressThreshold)
                    {
                        throw new ArgumentException(
                            $"{table.Name}:{row.LineNumber} boss '{bossId}' phase {phaseIndex} threshold must advance past phase {previous.PhaseIndex} " +
                            $"(normalized {progressThreshold} <= {previous.ProgressThreshold}).");
                    }

                    if (maxHpBonus < previous.MaxHpBonus)
                    {
                        throw new ArgumentException(
                            $"{table.Name}:{row.LineNumber} boss '{bossId}' phase {phaseIndex} maxHpBonus ({maxHpBonus}) is lower than phase " +
                            $"{previous.PhaseIndex} ({previous.MaxHpBonus}); phase bonuses are cumulative targets and max HP can never decrease.");
                    }
                }

                phases.Add(new BossPhaseDefinition(
                    bossId,
                    phaseIndex,
                    authoredThreshold,
                    progressThreshold,
                    strengthBonusPercent,
                    maxHpBonus,
                    patternPhaseMin,
                    visualScale,
                    footprintRadius,
                    auraStatusKind,
                    Optional(row, "designerNote"),
                    footprintShape));
            }

            return grouped;
        }

        /// <summary>
        /// <c>mechanicParams</c> 컬럼 파싱: <c>key=value;key=value</c>. 빈 값은 빈 맵(기믹 없는 보스).
        /// 중복 키·<c>=</c> 없는 조각·빈 키는 에러다 — 조용히 무시하면 저작자가 오타를 알 수 없다.
        /// </summary>
        private static Dictionary<string, string> ParseMechanicParams(string value, string fileName, int lineNumber)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(value))
            {
                return result;
            }

            foreach (var part in value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var separator = part.IndexOf('=');
                if (separator <= 0)
                {
                    throw new ArgumentException(
                        $"{fileName}:{lineNumber} mechanicParams entry '{part.Trim()}' must be in key=value form.");
                }

                var key = part.Substring(0, separator).Trim();
                var entryValue = part.Substring(separator + 1).Trim();
                if (key.Length == 0)
                {
                    throw new ArgumentException($"{fileName}:{lineNumber} mechanicParams has an empty key.");
                }

                if (result.ContainsKey(key))
                {
                    throw new ArgumentException($"{fileName}:{lineNumber} mechanicParams duplicates key '{key}'.");
                }

                result.Add(key, entryValue);
            }

            return result;
        }

        /// <summary>
        /// 저작 threshold → 정규화 progress. <see cref="BossPhaseMetricKind.HpRatioBelow"/>만
        /// 방향이 반대라(체력 비율은 내려간다) 여기서 뒤집는다.
        /// </summary>
        private static int NormalizeThreshold(BossPhaseMetricKind metric, int authoredThreshold, string fileName, int lineNumber)
        {
            switch (metric)
            {
                case BossPhaseMetricKind.HpRatioBelow:
                    if (authoredThreshold < 0 || authoredThreshold > 100)
                    {
                        throw new ArgumentException($"{fileName}:{lineNumber} HpRatioBelow threshold must be a percentage between 0 and 100.");
                    }

                    return 100 - authoredThreshold;
                case BossPhaseMetricKind.AbsorbedStacks:
                case BossPhaseMetricKind.TurnCount:
                    if (authoredThreshold < 0)
                    {
                        throw new ArgumentException($"{fileName}:{lineNumber} threshold cannot be negative for metric '{metric}'.");
                    }

                    return authoredThreshold;
                default:
                    throw new ArgumentException($"{fileName}:{lineNumber} unsupported phaseMetric '{metric}'.");
            }
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

        private static float Float(CsvRow row, string column, float fallback)
        {
            var value = Optional(row, column);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                throw new ArgumentException($"{row.FileName}:{row.LineNumber} column '{column}' must be a number.");
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
