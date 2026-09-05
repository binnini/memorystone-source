using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class CombatCardVfxCueDefinition
    {
        public CombatCardVfxCueDefinition(
            string cueId,
            string cardId,
            string effectRef,
            EffectKind effectKind,
            string targetFilter,
            string prefabPath,
            float scaleMultiplier,
            bool scaleWithRadius,
            float offsetX,
            float offsetY,
            float offsetZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float lifetimeOverride,
            string sourceRef,
            bool matchSourceRefPrefix,
            string spawnAnchor = "",
            string designerNote = "",
            EffectFloatingTextMode floatingTextMode = EffectFloatingTextMode.Auto,
            string floatingTextOverride = "",
            float delaySeconds = 0f,
            string sourceCardId = "",
            float playbackSpeed = 1f,
            float scaleX = 1f,
            float scaleY = 1f,
            float scaleZ = 1f,
            // Appended last so existing positional/named call sites keep compiling unchanged.
            float measuredLengthSeconds = 0f,
            float authoredLengthSeconds = 0f)
        {
            SourceCardId = sourceCardId ?? string.Empty;
            CueId = cueId ?? string.Empty;
            CardId = cardId ?? string.Empty;
            EffectRef = effectRef ?? string.Empty;
            EffectKind = effectKind;
            TargetFilter = targetFilter ?? string.Empty;
            PrefabPath = prefabPath ?? string.Empty;
            ScaleMultiplier = scaleMultiplier <= 0f ? 1f : scaleMultiplier;
            ScaleWithRadius = scaleWithRadius;
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            LifetimeOverride = Math.Max(0f, lifetimeOverride);
            // A card-scoped cue (sourceCardId set) leaves SourceRef empty unless the author wrote one, so the
            // default cardId fallback below does not silently add a sourceRef rule the effect never emits —
            // an authored sourceRef on such a row is an extra constraint, not the selector.
            SourceRef = !string.IsNullOrWhiteSpace(sourceRef)
                ? sourceRef
                : !string.IsNullOrWhiteSpace(sourceCardId)
                    ? string.Empty
                    : BuildDefaultSourceRef(cardId, effectRef);
            MatchSourceRefPrefix = matchSourceRefPrefix;
            SpawnAnchor = spawnAnchor ?? string.Empty;
            DesignerNote = designerNote ?? string.Empty;
            FloatingTextMode = floatingTextMode;
            FloatingTextOverride = floatingTextOverride ?? string.Empty;
            DelaySeconds = Math.Max(0f, delaySeconds);
            PlaybackSpeed = playbackSpeed <= 0f ? 1f : playbackSpeed;
            ScaleX = scaleX <= 0f ? 1f : scaleX;
            ScaleY = scaleY <= 0f ? 1f : scaleY;
            ScaleZ = scaleZ <= 0f ? 1f : scaleZ;
            MeasuredLengthSeconds = Math.Max(0f, measuredLengthSeconds);
            AuthoredLengthSeconds = Math.Max(0f, authoredLengthSeconds);
        }

        public string CueId { get; }
        public string CardId { get; }
        public string EffectRef { get; }
        public EffectKind EffectKind { get; }
        public string TargetFilter { get; }
        public string PrefabPath { get; }
        public float ScaleMultiplier { get; }
        public bool ScaleWithRadius { get; }
        public float OffsetX { get; }
        public float OffsetY { get; }
        public float OffsetZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float LifetimeOverride { get; }
        public string SourceRef { get; }
        public string SourceCardId { get; }
        public bool MatchSourceRefPrefix { get; }
        public string SpawnAnchor { get; }
        public string DesignerNote { get; }
        public EffectFloatingTextMode FloatingTextMode { get; }
        public string FloatingTextOverride { get; }
        public float DelaySeconds { get; }
        public float PlaybackSpeed { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public float ScaleZ { get; }

        /// <summary>
        /// Baked natural play length at speed 1 (0 = not measured yet). A tool output, not an authoring
        /// field — see docs/presentation-duration-data-plan.md §3. Distinct from
        /// <see cref="LifetimeOverride"/>, which is when the spawned VFX object is destroyed.
        /// </summary>
        public float MeasuredLengthSeconds { get; }

        /// <summary>
        /// Designer-authored on-screen duration (0 = unset, use the measured length). The only hand-edited
        /// half of the pair. Deliberately not <see cref="LifetimeOverride"/>: that field already means
        /// "destroy the object now", so reusing it would make trimming a look silently retime every consumer.
        /// </summary>
        public float AuthoredLengthSeconds { get; }

        /// <summary>
        /// Composes the <see cref="PresentationDuration"/> for this cue. <paramref name="measurable"/> must
        /// be supplied by the caller because the CSV row cannot know it: whether a cue loops is authored on
        /// the catalog entry, and a looping cue has no natural length at all (plan §4.5). Defaulting it here
        /// would quietly claim every loop cue was measurable.
        /// </summary>
        public PresentationDuration CreateDuration(bool measurable)
            => PresentationDuration.Create(
                MeasuredLengthSeconds,
                AuthoredLengthSeconds,
                measurable,
                PresentationClock.Scaled,
                // The destroy timer caps (and for a prefab full of looping particles, solely determines) how
                // long the cue is on screen. A loop cue is never destroyed on a timer, so it passes 0.
                destroyAfterSeconds: measurable ? LifetimeOverride : 0f);

        private static string BuildDefaultSourceRef(string cardId, string effectRef)
        {
            if (!string.IsNullOrWhiteSpace(cardId))
            {
                return cardId.Trim();
            }

            return effectRef?.Trim() ?? string.Empty;
        }
    }

    public readonly struct CombatCardVfxCueTuningUpdate
    {
        public CombatCardVfxCueTuningUpdate(
            float scaleMultiplier,
            bool scaleWithRadius,
            float offsetX,
            float offsetY,
            float offsetZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float lifetimeOverride,
            string spawnAnchor,
            float delaySeconds = 0f,
            string prefabPath = null)
        {
            ScaleMultiplier = scaleMultiplier <= 0f ? 1f : scaleMultiplier;
            ScaleWithRadius = scaleWithRadius;
            OffsetX = offsetX;
            OffsetY = offsetY;
            OffsetZ = offsetZ;
            RotationX = rotationX;
            RotationY = rotationY;
            RotationZ = rotationZ;
            LifetimeOverride = Math.Max(0f, lifetimeOverride);
            SpawnAnchor = spawnAnchor ?? string.Empty;
            DelaySeconds = Math.Max(0f, delaySeconds);
            PrefabPath = prefabPath;
        }

        public float ScaleMultiplier { get; }
        public bool ScaleWithRadius { get; }
        public float OffsetX { get; }
        public float OffsetY { get; }
        public float OffsetZ { get; }
        public float RotationX { get; }
        public float RotationY { get; }
        public float RotationZ { get; }
        public float LifetimeOverride { get; }
        public string SpawnAnchor { get; }
        public float DelaySeconds { get; }

        /// <summary>Null leaves the CSV prefabPath cell untouched; a non-empty value repoints the cue.</summary>
        public string PrefabPath { get; }
    }

    public static class CombatCardVfxCueTuningCsvWriter
    {
        private static readonly string[] RequiredColumns =
        {
            "cueId",
            "scaleMultiplier",
            "scaleWithRadius",
            "offsetX",
            "offsetY",
            "offsetZ",
            "rotationX",
            "rotationY",
            "rotationZ",
            "lifetimeOverride",
            "spawnAnchor"
        };

        public static void UpdateFile(string path, string cueId, CombatCardVfxCueTuningUpdate update, bool createBackup = true)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Card VFX CSV path is required.", nameof(path));
            }

            if (string.IsNullOrWhiteSpace(cueId))
            {
                throw new ArgumentException("cueId is required.", nameof(cueId));
            }

            var text = File.ReadAllText(path, Encoding.UTF8);
            var newline = text.Contains("\r\n") ? "\r\n" : "\n";
            var hadTrailingNewline = text.EndsWith("\r\n") || text.EndsWith("\n");
            var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
            if (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
            {
                lines.RemoveAt(lines.Count - 1);
            }

            if (lines.Count == 0)
            {
                throw new ArgumentException($"{path} is empty.");
            }

            var headers = SplitCsvLine(lines[0]);
            var indices = BuildColumnIndex(path, headers);
            EnsureOptionalColumn(ref headers, ref lines, ref indices, path, "delaySeconds");
            var cueIdIndex = indices["cueId"];
            var rowIndex = -1;
            var rowValues = new List<string>();

            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var values = SplitCsvLine(lines[i]);
                if (values.Count != headers.Count)
                {
                    throw new ArgumentException($"{path}:{i + 1} has {values.Count} columns but expected {headers.Count}.");
                }

                if (string.Equals(values[cueIdIndex], cueId, StringComparison.Ordinal))
                {
                    rowIndex = i;
                    rowValues = values;
                    break;
                }
            }

            if (rowIndex < 0)
            {
                throw new ArgumentException($"{path} does not contain cueId '{cueId}'.");
            }

            rowValues[indices["scaleMultiplier"]] = FormatFloat(update.ScaleMultiplier);
            rowValues[indices["scaleWithRadius"]] = update.ScaleWithRadius ? "true" : "false";
            rowValues[indices["offsetX"]] = FormatFloat(update.OffsetX);
            rowValues[indices["offsetY"]] = FormatFloat(update.OffsetY);
            rowValues[indices["offsetZ"]] = FormatFloat(update.OffsetZ);
            rowValues[indices["rotationX"]] = FormatFloat(update.RotationX);
            rowValues[indices["rotationY"]] = FormatFloat(update.RotationY);
            rowValues[indices["rotationZ"]] = FormatFloat(update.RotationZ);
            rowValues[indices["lifetimeOverride"]] = FormatFloat(update.LifetimeOverride);
            rowValues[indices["spawnAnchor"]] = update.SpawnAnchor;
            rowValues[indices["delaySeconds"]] = FormatFloat(update.DelaySeconds);
            if (!string.IsNullOrWhiteSpace(update.PrefabPath))
            {
                if (!indices.TryGetValue("prefabPath", out var prefabPathIndex))
                {
                    throw new ArgumentException($"{path}:1 missing 'prefabPath' column required for a prefab repoint.");
                }

                rowValues[prefabPathIndex] = update.PrefabPath;
            }

            lines[rowIndex] = string.Join(",", rowValues.Select(Quote));

            if (createBackup)
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }

            File.WriteAllText(path, string.Join(newline, lines) + (hadTrailingNewline ? newline : string.Empty), new UTF8Encoding(false));
        }

        private static Dictionary<string, int> BuildColumnIndex(string path, IReadOnlyList<string> headers)
        {
            var indices = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < headers.Count; i++)
            {
                indices[headers[i].Trim().TrimStart('\uFEFF')] = i;
            }

            foreach (var column in RequiredColumns)
            {
                if (!indices.ContainsKey(column))
                {
                    throw new ArgumentException($"{path}:1 missing required column '{column}'.");
                }
            }

            return indices;
        }

        private static void EnsureOptionalColumn(
            ref List<string> headers,
            ref List<string> lines,
            ref Dictionary<string, int> indices,
            string path,
            string column)
        {
            if (indices.ContainsKey(column))
            {
                return;
            }

            headers = headers.Concat(new[] { column }).ToList();
            lines[0] = string.Join(",", headers.Select(Quote));
            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var values = SplitCsvLine(lines[i]);
                while (values.Count < headers.Count)
                {
                    values.Add(string.Empty);
                }

                lines[i] = string.Join(",", values.Select(Quote));
            }

            indices = BuildColumnIndex(path, headers);
        }

        private static List<string> SplitCsvLine(string line)
        {
            var values = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;
            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    values.Add(builder.ToString());
                    builder.Length = 0;
                }
                else
                {
                    builder.Append(c);
                }
            }

            if (inQuotes)
            {
                throw new ArgumentException("CSV contains an unterminated quoted field.");
            }

            values.Add(builder.ToString());
            return values;
        }

        private static string Quote(string value)
        {
            return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }
    }
    public static class CombatCardVfxCsvConverter
    {
        public static IReadOnlyList<CombatCardVfxCueDefinition> ConvertFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Card VFX CSV path is required.", nameof(path));
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required card VFX CSV file is missing: {path}", path);
            }

            return Convert(File.ReadAllText(path, Encoding.UTF8), Path.GetFileName(path));
        }

        public static IReadOnlyList<CombatCardVfxCueDefinition> Convert(string csvText, string name = "combat_card_vfx_cues.csv")
        {
            var table = CsvTable.Parse(csvText, name);
            var result = new List<CombatCardVfxCueDefinition>();
            var cueIds = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in table.Rows)
            {
                var cueId = Required(row, "cueId");
                if (!cueIds.Add(cueId))
                {
                    throw new ArgumentException($"{row.FileName}:{row.LineNumber} duplicates cueId '{cueId}'.");
                }

                var cardId = Optional(row, "cardId");
                var effectRef = Optional(row, "effectRef");
                var sourceRef = Optional(row, "sourceRef");
                var sourceCardId = Optional(row, "sourceCardId");
                if (string.IsNullOrWhiteSpace(cardId)
                    && string.IsNullOrWhiteSpace(effectRef)
                    && string.IsNullOrWhiteSpace(sourceRef)
                    && string.IsNullOrWhiteSpace(sourceCardId))
                {
                    throw new ArgumentException(
                        $"{row.FileName}:{row.LineNumber} needs cardId, effectRef, sourceRef, or sourceCardId.");
                }

                result.Add(new CombatCardVfxCueDefinition(
                    cueId,
                    cardId,
                    effectRef,
                    ParseEnum<EffectKind>(Required(row, "effectKind"), row.FileName, row.LineNumber, "effectKind"),
                    Required(row, "targetFilter"),
                    Required(row, "prefabPath"),
                    Float(row, "scaleMultiplier", 1f),
                    Bool(row, "scaleWithRadius", true),
                    Float(row, "offsetX", 0f),
                    Float(row, "offsetY", 0f),
                    Float(row, "offsetZ", 0f),
                    Float(row, "rotationX", 0f),
                    Float(row, "rotationY", 0f),
                    Float(row, "rotationZ", 0f),
                    Float(row, "lifetimeOverride", 0f),
                    sourceRef,
                    Bool(row, "matchSourceRefPrefix", false),
                    Optional(row, "spawnAnchor"),
                    Optional(row, "designerNote"),
                    OptionalEnum(row, "floatingTextMode", EffectFloatingTextMode.Auto),
                    Optional(row, "floatingTextOverride"),
                    Float(row, "delaySeconds", 0f),
                    sourceCardId,
                    Float(row, "playbackSpeed", 1f),
                    Float(row, "scaleX", 1f),
                    Float(row, "scaleY", 1f),
                    Float(row, "scaleZ", 1f),
                    // Both columns are optional: Float() falls back when the header is absent, so a CSV
                    // predating this track converts to "unmeasured / unauthored" instead of failing.
                    Float(row, "measuredLengthSeconds", 0f),
                    Float(row, "authoredLengthSeconds", 0f)));
            }

            return result;
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

        private static bool Bool(CsvRow row, string column, bool fallback)
        {
            var value = Optional(row, column);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (bool.TryParse(value, out var parsed))
            {
                return parsed;
            }

            if (value == "1") return true;
            if (value == "0") return false;
            throw new ArgumentException($"{row.FileName}:{row.LineNumber} column '{column}' must be true/false.");
        }

        private static T OptionalEnum<T>(CsvRow row, string column, T fallback) where T : struct
        {
            var value = Optional(row, column);
            if (string.IsNullOrWhiteSpace(value))
            {
                return fallback;
            }

            if (Enum.TryParse<T>(value, ignoreCase: true, out var parsed))
            {
                return parsed;
            }

            throw new ArgumentException($"{row.FileName}:{row.LineNumber} column '{column}' has unknown value '{value}'.");
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
