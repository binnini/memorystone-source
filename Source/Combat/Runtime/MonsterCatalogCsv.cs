using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class MonsterCatalogCsvSource
    {
        public MonsterCatalogCsvSource(
            string monsterCatalogCsv,
            string attackPatternsCsv,
            string patternBindingsCsv,
            string vfxCuesCsv,
            string soundCuesCsv,
            string patternVfxBindingsCsv = "",
            string sourceId = "designer-monster-csv",
            string displayName = "Designer Monster CSV Catalog")
        {
            MonsterCatalogCsv = monsterCatalogCsv ?? string.Empty;
            AttackPatternsCsv = attackPatternsCsv ?? string.Empty;
            PatternBindingsCsv = patternBindingsCsv ?? string.Empty;
            VfxCuesCsv = vfxCuesCsv ?? string.Empty;
            SoundCuesCsv = soundCuesCsv ?? string.Empty;
            PatternVfxBindingsCsv = patternVfxBindingsCsv ?? string.Empty;
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? "designer-monster-csv" : sourceId;
            DisplayName = displayName ?? string.Empty;
        }

        public string MonsterCatalogCsv { get; }
        public string AttackPatternsCsv { get; }
        public string PatternBindingsCsv { get; }
        public string VfxCuesCsv { get; }
        public string SoundCuesCsv { get; }
        public string PatternVfxBindingsCsv { get; }
        public string SourceId { get; }
        public string DisplayName { get; }

        public static MonsterCatalogCsvSource FromDirectory(
            string directoryPath,
            string sourceId = "designer-monster-csv",
            string displayName = "Designer Monster CSV Catalog")
        {
            return FromDirectories(directoryPath, directoryPath, sourceId, displayName);
        }

        public static MonsterCatalogCsvSource FromDirectories(
            string monsterDirectoryPath,
            string presentationDirectoryPath,
            string sourceId = "designer-monster-csv",
            string displayName = "Designer Monster CSV Catalog")
        {
            if (string.IsNullOrWhiteSpace(monsterDirectoryPath))
            {
                throw new ArgumentException("Monster CSV directory path is required.", nameof(monsterDirectoryPath));
            }

            if (string.IsNullOrWhiteSpace(presentationDirectoryPath))
            {
                throw new ArgumentException("Presentation CSV directory path is required.", nameof(presentationDirectoryPath));
            }

            return new MonsterCatalogCsvSource(
                ReadRequired(monsterDirectoryPath, "monster_catalog.csv"),
                ReadRequired(monsterDirectoryPath, "monster_attack_patterns.csv"),
                ReadRequired(monsterDirectoryPath, "monster_pattern_bindings.csv"),
                ReadRequired(presentationDirectoryPath, "combat_vfx_cues.csv"),
                ReadRequired(presentationDirectoryPath, "combat_sound_cues.csv"),
                ReadOptional(presentationDirectoryPath, "monster_pattern_vfx_bindings.csv"),
                sourceId,
                displayName);
        }

        private static string ReadRequired(string directoryPath, string fileName)
        {
            var path = Path.Combine(directoryPath, fileName);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"Required monster CSV file is missing: {path}", path);
            }

            return File.ReadAllText(path, Encoding.UTF8);
        }

        private static string ReadOptional(string directoryPath, string fileName)
        {
            var path = Path.Combine(directoryPath, fileName);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
        }
    }

    public sealed class MonsterCatalogCsvBundle
    {
        private readonly Dictionary<string, MonsterPatternPresentationDefinition> patternPresentations;

        public MonsterCatalogCsvBundle(
            MonsterCatalogDefinition monsterCatalog,
            IEnumerable<MonsterPatternPresentationDefinition> patternPresentations,
            IEnumerable<MonsterPatternVfxBindingDefinition> patternVfxBindings,
            IEnumerable<CombatVfxCueDefinition> vfxCues,
            IEnumerable<CombatSoundCueDefinition> soundCues)
        {
            MonsterCatalog = monsterCatalog ?? throw new ArgumentNullException(nameof(monsterCatalog));
            PatternPresentations = (patternPresentations ?? Array.Empty<MonsterPatternPresentationDefinition>()).ToList();
            PatternVfxBindings = (patternVfxBindings ?? Array.Empty<MonsterPatternVfxBindingDefinition>()).ToList();
            VfxCues = (vfxCues ?? Array.Empty<CombatVfxCueDefinition>()).ToList();
            SoundCues = (soundCues ?? Array.Empty<CombatSoundCueDefinition>()).ToList();
            this.patternPresentations = PatternPresentations
                .Where(entry => !string.IsNullOrWhiteSpace(entry.PatternId))
                .GroupBy(entry => entry.PatternId)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        }

        public MonsterCatalogDefinition MonsterCatalog { get; }
        public IReadOnlyList<MonsterPatternPresentationDefinition> PatternPresentations { get; }
        public IReadOnlyList<MonsterPatternVfxBindingDefinition> PatternVfxBindings { get; }
        public IReadOnlyList<CombatVfxCueDefinition> VfxCues { get; }
        public IReadOnlyList<CombatSoundCueDefinition> SoundCues { get; }

        public bool TryGetPatternPresentation(string patternId, out MonsterPatternPresentationDefinition presentation)
        {
            if (string.IsNullOrWhiteSpace(patternId))
            {
                presentation = null;
                return false;
            }

            return patternPresentations.TryGetValue(patternId, out presentation);
        }
    }

    public sealed class MonsterPatternPresentationDefinition
    {
        public MonsterPatternPresentationDefinition(
            string patternId,
            string vfxCueId,
            string soundWindupCueId,
            string soundCastCueId,
            string soundImpactCueId,
            string animationTrigger)
        {
            PatternId = patternId ?? string.Empty;
            VfxCueId = vfxCueId ?? string.Empty;
            SoundWindupCueId = soundWindupCueId ?? string.Empty;
            SoundCastCueId = soundCastCueId ?? string.Empty;
            SoundImpactCueId = soundImpactCueId ?? string.Empty;
            AnimationTrigger = animationTrigger ?? string.Empty;
        }

        public string PatternId { get; }
        public string VfxCueId { get; }
        public string SoundWindupCueId { get; }
        public string SoundCastCueId { get; }
        public string SoundImpactCueId { get; }
        public string AnimationTrigger { get; }
    }

    public sealed class MonsterPatternVfxBindingDefinition
    {
        public MonsterPatternVfxBindingDefinition(
            string patternId,
            string vfxCueId,
            string spawnAnchor,
            int order,
            bool enabled,
            float delaySeconds = 0f,
            string designerNote = "")
        {
            PatternId = patternId ?? string.Empty;
            VfxCueId = vfxCueId ?? string.Empty;
            SpawnAnchor = spawnAnchor ?? string.Empty;
            Order = Math.Max(0, order);
            Enabled = enabled;
            DelaySeconds = Math.Max(0f, delaySeconds);
            DesignerNote = designerNote ?? string.Empty;
        }

        public string PatternId { get; }
        public string VfxCueId { get; }
        public string SpawnAnchor { get; }
        public int Order { get; }
        public bool Enabled { get; }
        public float DelaySeconds { get; }
        public string DesignerNote { get; }
    }

    public sealed class CombatVfxCueDefinition
    {
        public CombatVfxCueDefinition(
            string vfxCueId,
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
            float playbackSpeed = 1f,
            float scaleX = 1f,
            float scaleY = 1f,
            float scaleZ = 1f,
            // Appended last so existing positional/named call sites keep compiling unchanged.
            float measuredLengthSeconds = 0f,
            float authoredLengthSeconds = 0f,
            string areaSpawnMode = "",
            float perTileDelaySeconds = 0f,
            bool followSourceAnchor = false)
        {
            VfxCueId = vfxCueId ?? string.Empty;
            AreaSpawnMode = areaSpawnMode ?? string.Empty;
            PerTileDelaySeconds = Math.Max(0f, perTileDelaySeconds);
            FollowSourceAnchor = followSourceAnchor;
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
            SourceRef = sourceRef ?? string.Empty;
            MatchSourceRefPrefix = matchSourceRefPrefix;
            SpawnAnchor = spawnAnchor ?? string.Empty;
            DesignerNote = designerNote ?? string.Empty;
            PlaybackSpeed = playbackSpeed <= 0f ? 1f : playbackSpeed;
            ScaleX = scaleX <= 0f ? 1f : scaleX;
            ScaleY = scaleY <= 0f ? 1f : scaleY;
            ScaleZ = scaleZ <= 0f ? 1f : scaleZ;
            MeasuredLengthSeconds = Math.Max(0f, measuredLengthSeconds);
            AuthoredLengthSeconds = Math.Max(0f, authoredLengthSeconds);
        }

        public string VfxCueId { get; }
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
        public bool MatchSourceRefPrefix { get; }
        public string SpawnAnchor { get; }
        public string DesignerNote { get; }
        public float PlaybackSpeed { get; }
        public float ScaleX { get; }
        public float ScaleY { get; }
        public float ScaleZ { get; }

        /// <summary>Area spawn mode name ("" / "None" = single center spawn, "PerTile" = one instance per footprint tile).</summary>
        public string AreaSpawnMode { get; }

        /// <summary>Per-tile stagger seconds for PerTile mode (0 = simultaneous).</summary>
        public float PerTileDelaySeconds { get; }

        /// <summary>
        /// When true the spawned one-shot tracks the source actor's spawn anchor transform every frame
        /// (position + rotation delta), so e.g. a breath cone sweeps with the monster's head animation.
        /// </summary>
        public bool FollowSourceAnchor { get; }

        /// <summary>
        /// Baked natural play length at speed 1 (0 = not measured yet). Tool output, never hand-edited —
        /// see docs/presentation-duration-data-plan.md §3. Not the same thing as
        /// <see cref="LifetimeOverride"/>, which is the destroy timer for the spawned object.
        /// </summary>
        public float MeasuredLengthSeconds { get; }

        /// <summary>Designer-authored on-screen duration (0 = unset, use the measured length).</summary>
        public float AuthoredLengthSeconds { get; }

        /// <summary>
        /// Composes the <see cref="PresentationDuration"/> for this cue. <paramref name="measurable"/> is the
        /// caller's to supply: loop-ness lives on the catalog entry, not this row, and a looping cue has no
        /// natural length (plan §4.5).
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
    }

    public readonly struct CombatVfxCueTuningUpdate
    {
        public CombatVfxCueTuningUpdate(
            float scaleMultiplier,
            bool scaleWithRadius,
            float offsetX,
            float offsetY,
            float offsetZ,
            float rotationX,
            float rotationY,
            float rotationZ,
            float lifetimeOverride,
            string spawnAnchor = "",
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

        /// <summary>Null leaves the CSV prefabPath cell untouched; a non-empty value repoints the cue.</summary>
        public string PrefabPath { get; }
    }

    public static class CombatVfxCueTuningCsvWriter
    {
        private static readonly string[] RequiredColumns =
        {
            "vfxCueId",
            "scaleMultiplier",
            "scaleWithRadius",
            "offsetX",
            "offsetY",
            "offsetZ",
            "rotationX",
            "rotationY",
            "rotationZ",
            "lifetimeOverride"
        };

        public static void UpdateFile(string path, string vfxCueId, CombatVfxCueTuningUpdate update, bool createBackup = true)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Monster VFX CSV path is required.", nameof(path));
            }

            if (string.IsNullOrWhiteSpace(vfxCueId))
            {
                throw new ArgumentException("vfxCueId is required.", nameof(vfxCueId));
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
            var hasSpawnAnchorColumn = indices.ContainsKey("spawnAnchor");
            if (!hasSpawnAnchorColumn && !string.IsNullOrWhiteSpace(update.SpawnAnchor))
            {
                headers = headers.Concat(new[] { "spawnAnchor" }).ToList();
                lines[0] = string.Join(",", headers.Select(Quote));
                indices = BuildColumnIndex(path, headers);
                hasSpawnAnchorColumn = true;
            }

            var cueIdIndex = indices["vfxCueId"];
            var rowIndex = -1;
            var rowValues = new List<string>();

            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var values = SplitCsvLine(lines[i]);
                if (values.Count != headers.Count - (hasSpawnAnchorColumn && SplitCsvLine(lines[0]).Count != values.Count ? 1 : 0) &&
                    values.Count != headers.Count)
                {
                    throw new ArgumentException($"{path}:{i + 1} has {values.Count} columns but expected {headers.Count}.");
                }

                while (values.Count < headers.Count)
                {
                    values.Add(string.Empty);
                }

                if (string.Equals(values[cueIdIndex], vfxCueId, StringComparison.Ordinal))
                {
                    rowIndex = i;
                    rowValues = values;
                    break;
                }
            }

            if (rowIndex < 0)
            {
                throw new ArgumentException($"{path} does not contain vfxCueId '{vfxCueId}'.");
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
            if (hasSpawnAnchorColumn)
            {
                rowValues[indices["spawnAnchor"]] = update.SpawnAnchor;
            }

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

    public static class MonsterPatternVfxBindingCsvWriter
    {
        private static readonly string[] RequiredColumns = { "patternId", "vfxCueId", "spawnAnchor", "order", "enabled" };

        public static void UpdateSpawnAnchor(string path, string patternId, string vfxCueId, int order, string spawnAnchor, bool createBackup = true, float? delaySeconds = null)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Monster pattern VFX binding CSV path is required.", nameof(path));
            }

            if (string.IsNullOrWhiteSpace(patternId))
            {
                throw new ArgumentException("patternId is required.", nameof(patternId));
            }

            if (string.IsNullOrWhiteSpace(vfxCueId))
            {
                throw new ArgumentException("vfxCueId is required.", nameof(vfxCueId));
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
            if (delaySeconds.HasValue)
            {
                EnsureOptionalColumn(ref headers, ref lines, ref indices, path, "delaySeconds");
            }

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

                if (string.Equals(values[indices["patternId"]], patternId, StringComparison.Ordinal) &&
                    string.Equals(values[indices["vfxCueId"]], vfxCueId, StringComparison.Ordinal) &&
                    Int(values[indices["order"]]) == order)
                {
                    rowIndex = i;
                    rowValues = values;
                    break;
                }
            }

            if (rowIndex < 0)
            {
                throw new ArgumentException($"{path} does not contain patternId '{patternId}' vfxCueId '{vfxCueId}' order '{order}'.");
            }

            rowValues[indices["spawnAnchor"]] = spawnAnchor ?? string.Empty;
            if (delaySeconds.HasValue)
            {
                rowValues[indices["delaySeconds"]] = FormatFloat(Math.Max(0f, delaySeconds.Value));
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

        private static int Int(string value)
        {
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
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

    public sealed class CombatSoundCueDefinition
    {
        public CombatSoundCueDefinition(
            string soundCueId,
            string clipPath,
            string bus,
            float volume,
            float pitchMin,
            float pitchMax,
            float cooldownSeconds,
            string missingClipBehavior,
            string designerNote = "",
            // Appended last so existing positional/named call sites keep compiling unchanged.
            float measuredLengthSeconds = 0f,
            float authoredLengthSeconds = 0f)
        {
            SoundCueId = soundCueId ?? string.Empty;
            ClipPath = clipPath ?? string.Empty;
            Bus = bus ?? string.Empty;
            Volume = Clamp(volume, 0f, 2f);
            PitchMin = Clamp(Math.Min(pitchMin, pitchMax), 0.1f, 3f);
            PitchMax = Clamp(Math.Max(pitchMin, pitchMax), 0.1f, 3f);
            CooldownSeconds = Math.Max(0f, cooldownSeconds);
            MissingClipBehavior = missingClipBehavior ?? string.Empty;
            DesignerNote = designerNote ?? string.Empty;
            MeasuredLengthSeconds = Math.Max(0f, measuredLengthSeconds);
            AuthoredLengthSeconds = Math.Max(0f, authoredLengthSeconds);
        }

        public string SoundCueId { get; }
        public string ClipPath { get; }
        public string Bus { get; }
        public float Volume { get; }
        public float PitchMin { get; }
        public float PitchMax { get; }
        public float CooldownSeconds { get; }
        public string MissingClipBehavior { get; }
        public string DesignerNote { get; }

        /// <summary>
        /// Baked clip length in seconds (0 = not measured yet), stored as the length at the SLOWEST pitch
        /// this cue can pick. Playback sets <c>AudioSource.pitch = Random.Range(pitchMin, pitchMax)</c>, so a
        /// cue with a pitch range has no single length — this is an upper bound, not an exact duration.
        /// See docs/presentation-duration-data-plan.md §4.3.
        /// </summary>
        public float MeasuredLengthSeconds { get; }

        /// <summary>Designer-authored duration (0 = unset, use the measured bound).</summary>
        public float AuthoredLengthSeconds { get; }

        /// <summary>
        /// True when this cue's pitch is fixed, so <see cref="MeasuredLengthSeconds"/> is exact rather than
        /// an upper bound. Consumers that need a tight number (rather than "no longer than") should check this.
        /// </summary>
        public bool HasDeterministicLength => PitchMin >= PitchMax;

        /// <summary>
        /// Composes the <see cref="PresentationDuration"/> for this cue on the <b>unscaled</b> clock:
        /// <c>AudioSource</c> ignores <c>Time.timeScale</c>, so an SFX length must never be added to a
        /// scaled VFX/animation length without accounting for hit-stop (plan §2.5).
        /// <paramref name="measurable"/> is the caller's to supply — a Music/Ambience cue loops and so has no
        /// natural length, and this row does not decide that policy.
        /// </summary>
        public PresentationDuration CreateDuration(bool measurable)
            => PresentationDuration.Create(
                MeasuredLengthSeconds,
                AuthoredLengthSeconds,
                measurable,
                PresentationClock.Unscaled);

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }
    }

    public static class MonsterCatalogCsvConverter
    {
        private static readonly Regex MonsterIdPattern = new Regex("^M[0-9]{3}$", RegexOptions.Compiled);
        private static readonly Regex AttackPatternIdPattern = new Regex("^A[0-9]{3}$", RegexOptions.Compiled);
        private static readonly Regex BehaviorProfileIdPattern = new Regex("^B[0-9]{3}$", RegexOptions.Compiled);
        private static readonly Regex VfxCueIdPattern = new Regex("^V[0-9]{3}$", RegexOptions.Compiled);
        private static readonly Regex SoundCueIdPattern = new Regex("^S[0-9]{3}$", RegexOptions.Compiled);

        public static MonsterCatalogCsvBundle Convert(MonsterCatalogCsvSource source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var monsters = CsvTable.Parse(source.MonsterCatalogCsv, "monster_catalog.csv");
            var patterns = CsvTable.Parse(source.AttackPatternsCsv, "monster_attack_patterns.csv");
            var bindings = CsvTable.Parse(source.PatternBindingsCsv, "monster_pattern_bindings.csv");
            var vfxCues = CsvTable.Parse(source.VfxCuesCsv, "combat_vfx_cues.csv");
            var soundCues = CsvTable.Parse(source.SoundCuesCsv, "combat_sound_cues.csv");
            var patternVfxBindings = string.IsNullOrWhiteSpace(source.PatternVfxBindingsCsv)
                ? null
                : CsvTable.Parse(source.PatternVfxBindingsCsv, "monster_pattern_vfx_bindings.csv");

            var vfxDefinitions = CreateVfxDefinitions(vfxCues);
            var soundDefinitions = CreateSoundDefinitions(soundCues);
            var patternDefinitions = CreateAttackPatterns(patterns, vfxDefinitions, soundDefinitions, out var presentations);
            var vfxBindings = patternVfxBindings != null
                ? CreatePatternVfxBindings(patternVfxBindings, patternDefinitions, vfxDefinitions)
                : CreateLegacyPatternVfxBindings(presentations);
            var monsterEntries = CreateMonsterEntries(monsters, bindings, patternDefinitions, source);

            var catalog = new MonsterCatalogDefinition(source.SourceId, source.DisplayName, monsterEntries, presentations);
            var evidence = catalog.CreateBindingEvidence();
            if (!evidence.IsValid)
            {
                throw new ArgumentException(evidence.FailureReason, nameof(source));
            }

            return new MonsterCatalogCsvBundle(catalog, presentations, vfxBindings, vfxDefinitions.Values, soundDefinitions.Values);
        }

        public static MonsterCatalogCsvBundle ConvertDirectory(
            string directoryPath,
            string sourceId = "designer-monster-csv",
            string displayName = "Designer Monster CSV Catalog")
        {
            return Convert(MonsterCatalogCsvSource.FromDirectory(directoryPath, sourceId, displayName));
        }

        public static MonsterCatalogCsvBundle ConvertDirectories(
            string monsterDirectoryPath,
            string presentationDirectoryPath,
            string sourceId = "designer-monster-csv",
            string displayName = "Designer Monster CSV Catalog")
        {
            return Convert(MonsterCatalogCsvSource.FromDirectories(monsterDirectoryPath, presentationDirectoryPath, sourceId, displayName));
        }

        private static Dictionary<string, CombatVfxCueDefinition> CreateVfxDefinitions(CsvTable vfxCues)
        {
            var result = new Dictionary<string, CombatVfxCueDefinition>(StringComparer.Ordinal);
            foreach (var row in vfxCues.Rows)
            {
                var id = Required(row, "vfxCueId");
                EnsureId(VfxCueIdPattern, id, vfxCues.Name, row.LineNumber);
                if (result.ContainsKey(id))
                {
                    throw Duplicate(vfxCues.Name, row.LineNumber, "vfxCueId", id);
                }

                result.Add(id, new CombatVfxCueDefinition(
                    id,
                    ParseEnum<EffectKind>(Required(row, "effectKind"), vfxCues.Name, row.LineNumber, "effectKind"),
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
                    Optional(row, "sourceRef"),
                    Bool(row, "matchSourceRefPrefix", false),
                    Optional(row, "spawnAnchor"),
                    Optional(row, "designerNote"),
                    Float(row, "playbackSpeed", 1f),
                    Float(row, "scaleX", 1f),
                    Float(row, "scaleY", 1f),
                    Float(row, "scaleZ", 1f),
                    // Optional columns: absent header falls back, so a pre-track CSV still converts.
                    Float(row, "measuredLengthSeconds", 0f),
                    Float(row, "authoredLengthSeconds", 0f),
                    Optional(row, "areaSpawnMode"),
                    Float(row, "perTileDelaySeconds", 0f),
                    Bool(row, "followSourceAnchor", false)));
            }

            return result;
        }

        private static Dictionary<string, CombatSoundCueDefinition> CreateSoundDefinitions(CsvTable soundCues)
        {
            var result = new Dictionary<string, CombatSoundCueDefinition>(StringComparer.Ordinal);
            foreach (var row in soundCues.Rows)
            {
                var id = Required(row, "soundCueId");
                EnsureId(SoundCueIdPattern, id, soundCues.Name, row.LineNumber);
                if (result.ContainsKey(id))
                {
                    throw Duplicate(soundCues.Name, row.LineNumber, "soundCueId", id);
                }

                result.Add(id, new CombatSoundCueDefinition(
                    id,
                    Optional(row, "clipPath"),
                    Required(row, "bus"),
                    Float(row, "volume", 1f),
                    Float(row, "pitchMin", 1f),
                    Float(row, "pitchMax", 1f),
                    Float(row, "cooldownSeconds", 0f),
                    Optional(row, "missingClipBehavior"),
                    Optional(row, "designerNote"),
                    // Optional columns: absent header falls back, so a pre-track CSV still converts.
                    Float(row, "measuredLengthSeconds", 0f),
                    Float(row, "authoredLengthSeconds", 0f)));
            }

            return result;
        }

        private static List<MonsterPatternVfxBindingDefinition> CreatePatternVfxBindings(
            CsvTable table,
            IReadOnlyDictionary<string, MonsterAttackPattern> patternDefinitions,
            IReadOnlyDictionary<string, CombatVfxCueDefinition> vfxDefinitions)
        {
            var result = new List<MonsterPatternVfxBindingDefinition>();
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in table.Rows)
            {
                var enabled = Bool(row, "enabled", true);
                if (!enabled)
                {
                    continue;
                }

                var patternId = Required(row, "patternId");
                EnsureId(AttackPatternIdPattern, patternId, table.Name, row.LineNumber);
                RequireLookup(patternDefinitions, patternId, table.Name, row.LineNumber, "patternId");

                var vfxCueId = Required(row, "vfxCueId");
                EnsureId(VfxCueIdPattern, vfxCueId, table.Name, row.LineNumber);
                RequireLookup(vfxDefinitions, vfxCueId, table.Name, row.LineNumber, "vfxCueId");

                var order = Int(row, "order", 0);
                var key = $"{patternId}|{vfxCueId}|{order}";
                if (!keys.Add(key))
                {
                    throw Duplicate(table.Name, row.LineNumber, "patternId/vfxCueId/order", key);
                }

                result.Add(new MonsterPatternVfxBindingDefinition(
                    patternId,
                    vfxCueId,
                        Optional(row, "spawnAnchor"),
                        order,
                        enabled,
                        Float(row, "delaySeconds", 0f),
                        Optional(row, "designerNote")));
            }

            return result.OrderBy(binding => binding.PatternId, StringComparer.Ordinal)
                .ThenBy(binding => binding.Order)
                .ThenBy(binding => binding.VfxCueId, StringComparer.Ordinal)
                .ToList();
        }

        private static List<MonsterPatternVfxBindingDefinition> CreateLegacyPatternVfxBindings(IEnumerable<MonsterPatternPresentationDefinition> presentations)
        {
            return (presentations ?? Array.Empty<MonsterPatternPresentationDefinition>())
                .Select(presentation => new MonsterPatternVfxBindingDefinition(
                    presentation.PatternId,
                    presentation.VfxCueId,
                        string.Empty,
                        1,
                        true,
                        designerNote: "Legacy binding from monster_attack_patterns.csv vfxCueId."))
                .ToList();
        }

        private static Dictionary<string, MonsterAttackPattern> CreateAttackPatterns(
            CsvTable patterns,
            IReadOnlyDictionary<string, CombatVfxCueDefinition> vfxDefinitions,
            IReadOnlyDictionary<string, CombatSoundCueDefinition> soundDefinitions,
            out List<MonsterPatternPresentationDefinition> presentations)
        {
            var result = new Dictionary<string, MonsterAttackPattern>(StringComparer.Ordinal);
            presentations = new List<MonsterPatternPresentationDefinition>();
            foreach (var row in patterns.Rows)
            {
                var id = Required(row, "patternId");
                EnsureId(AttackPatternIdPattern, id, patterns.Name, row.LineNumber);
                if (result.ContainsKey(id))
                {
                    throw Duplicate(patterns.Name, row.LineNumber, "patternId", id);
                }

                var shapeId = Optional(row, "shapeId");
                if (!string.IsNullOrWhiteSpace(shapeId) && !AttackShapeLibrary.TryGet(shapeId, out _))
                {
                    throw new ArgumentException($"{patterns.Name}:{row.LineNumber} references unknown shapeId '{shapeId}'.");
                }

                var vfxCueId = Required(row, "vfxCueId");
                RequireLookup(vfxDefinitions, vfxCueId, patterns.Name, row.LineNumber, "vfxCueId");
                var windup = Optional(row, "soundWindupCueId");
                var cast = Optional(row, "soundCastCueId");
                var impact = Optional(row, "soundImpactCueId");
                RequireOptionalLookup(soundDefinitions, windup, patterns.Name, row.LineNumber, "soundWindupCueId");
                RequireOptionalLookup(soundDefinitions, cast, patterns.Name, row.LineNumber, "soundCastCueId");
                RequireOptionalLookup(soundDefinitions, impact, patterns.Name, row.LineNumber, "soundImpactCueId");

                // 턴별 피해 변주(±N flat). 컬럼 없음·빈 값 = 0(고정 수치) — 하위호환 규약.
                // 피해 0 패턴(자기부여·상태이상 전용)에의 저작은 의미가 없으므로 거부한다.
                var damageJitter = Int(row, "damageJitter", 0);
                if (damageJitter < 0)
                {
                    throw new ArgumentException($"{patterns.Name}:{row.LineNumber} damageJitter cannot be negative.");
                }

                if (damageJitter > 0 && Int(row, "damage", 0) <= 0)
                {
                    throw new ArgumentException($"{patterns.Name}:{row.LineNumber} damageJitter requires a positive damage.");
                }

                var statusEffects = ParseStatusEffects(Optional(row, "statusEffects"), patterns.Name, row.LineNumber);

                // 소매치기(2026-09-04 §2-D · 야광귀 A051). 헤더가 없는 구 CSV·픽스처는 0(안 훔침)으로 내려앉는다.
                var stealMoneyAmount = Int(row, "stealMoneyAmount", 0);
                if (stealMoneyAmount < 0)
                {
                    throw new ArgumentException(
                        $"{patterns.Name}:{row.LineNumber} stealMoneyAmount cannot be negative — 음수 절도는 뜻이 없다.");
                }

                // 상태이상 지대(요괴 §3-3). 형상 밖 저작은 여기서 죽인다 — 위험 칸 예고는 형상에서
                // 나오는데 장판이 다른 칸에 생기면 "예고=명중"이 조용히 깨진다.
                if (!MonsterStatusZone.TryParse(
                        Optional(row, "zoneEffect"), Optional(row, "zoneOffsets"), out var zone, out var zoneError))
                {
                    throw new ArgumentException($"{patterns.Name}:{row.LineNumber} {zoneError}");
                }

                if (zone.HasZone)
                {
                    if (string.IsNullOrWhiteSpace(shapeId))
                    {
                        throw new ArgumentException(
                            $"{patterns.Name}:{row.LineNumber} pattern '{id}' authors a status zone without a shapeId"
                            + " — 지대는 형상 안 일부 칸이라 형상 없이는 자리를 정할 수 없다.");
                    }

                    if (!AttackShapeLibrary.TryGet(shapeId, out var zoneShape))
                    {
                        throw new ArgumentException(
                            $"{patterns.Name}:{row.LineNumber} pattern '{id}' zone references unresolved shape '{shapeId}'.");
                    }

                    var shapeCells = new HashSet<SeoulPlayup.Map.Runtime.HexCoord>(zoneShape.Offsets);
                    if (zoneShape.IncludeAdjacentRing)
                    {
                        foreach (var adjacent in AttackShapeLibrary.AdjacentOffsets)
                        {
                            shapeCells.Add(adjacent);
                        }
                    }

                    var outside = zone.Offsets.Where(offset => !shapeCells.Contains(offset)).ToList();
                    if (outside.Count > 0)
                    {
                        throw new ArgumentException(
                            $"{patterns.Name}:{row.LineNumber} pattern '{id}' zoneOffsets"
                            + $" ({string.Join(" ", outside.Select(offset => $"{offset.Q}:{offset.R}"))})"
                            + $" are outside shape '{shapeId}' — 지대는 형상의 부분집합이어야 예고=명중이 성립한다.");
                    }
                }

                // 저주 풀(요괴 트랙 §3-3). 단일 저작과 <b>둘 중 하나만</b> — 둘 다 적히면 어느 쪽이
                // 이기는지가 저작자에게 안 보이므로 임포트에서 죽인다(조용한 우선순위 금지).
                var injectStatusCardId = Optional(row, "injectStatusCardId");
                var injectStatusCardPoolRaw = Optional(row, "injectStatusCardPool");
                if (!MonsterCurseCardPool.TryParse(injectStatusCardPoolRaw, out var injectStatusCardPool, out var poolError))
                {
                    throw new ArgumentException($"{patterns.Name}:{row.LineNumber} {poolError}");
                }

                if (injectStatusCardPool.Length > 0 && !string.IsNullOrWhiteSpace(injectStatusCardId))
                {
                    throw new ArgumentException(
                        $"{patterns.Name}:{row.LineNumber} pattern '{id}' authors both injectStatusCardId and"
                        + " injectStatusCardPool — 둘 중 하나만 저작한다(항상 그 카드 / 이 중 하나).");
                }

                if (injectStatusCardPool.Length == 1)
                {
                    throw new ArgumentException(
                        $"{patterns.Name}:{row.LineNumber} pattern '{id}' has a single-entry injectStatusCardPool"
                        + " — 뽑을 것이 하나면 injectStatusCardId로 저작한다.");
                }
                // 소환(요괴 §4-4). 자리는 형상 칸이 정하므로 형상 없이는 저작될 수 없고, 부르는 수가
                // 형상 칸보다 많으면 앉힐 자리가 없는 저작이다.
                if (!MonsterSummonSpec.TryParse(Optional(row, "summonSpec"), out var summon, out var summonError))
                {
                    throw new ArgumentException($"{patterns.Name}:{row.LineNumber} {summonError}");
                }

                if (summon.HasSummon)
                {
                    if (string.IsNullOrWhiteSpace(shapeId) || !AttackShapeLibrary.TryGet(shapeId, out var summonShape))
                    {
                        throw new ArgumentException(
                            $"{patterns.Name}:{row.LineNumber} pattern '{id}' authors a summon without a resolvable shapeId"
                            + " — 소환 자리는 형상 칸이 정한다.");
                    }

                    var seatCount = summonShape.Offsets.Length + (summonShape.IncludeAdjacentRing ? 6 : 0);
                    if (summon.Count > seatCount)
                    {
                        throw new ArgumentException(
                            $"{patterns.Name}:{row.LineNumber} pattern '{id}' summons {summon.Count} but shape"
                            + $" '{shapeId}' only seats {seatCount} — 앉힐 자리가 없는 저작이다.");
                    }
                }

                var pattern = new MonsterAttackPattern(
                    id,
                    Required(row, "displayName"),
                    Int(row, "range", 1),
                    Int(row, "areaRadius", 0),
                    Int(row, "damage", 0),
                    Required(row, "effectRef"),
                    Required(row, "targeting"),
                    Int(row, "weight", 1),
                    shapeId,
                    statusEffects,
                    Int(row, "statusEffectDurationTurns", 2),
                    ResolvePatternStatusEffectAmount(statusEffects),
                    Optional(row, "animationTrigger"),
                    Int(row, "knockbackDistance", 0),
                    Int(row, "knockbackImpactDamage", 0),
                    Int(row, "cooldownTurns", 0),
                    phaseMin: 0,
                    phaseMax: int.MaxValue,
                    distMin: 0,
                    distMax: int.MaxValue,
                    leapRange: Int(row, "leapRange", 0),
                    injectStatusCardId: injectStatusCardId,
                    hitCount: Int(row, "hitCount", 1),
                    shieldGain: Int(row, "shieldGain", 0),
                    damageJitter: damageJitter,
                    injectStatusCardPool: injectStatusCardPool,
                    zoneStatusKind: zone.StatusKind,
                    zoneDurationTurns: zone.HasZone ? zone.DurationTurns : 0,
                    zoneOffsets: zone.Offsets ?? System.Array.Empty<SeoulPlayup.Map.Runtime.HexCoord>(),
                    summonMonsterDefinitionId: summon.MonsterDefinitionId,
                    summonCount: summon.Count,
                    summonMaxAlive: summon.MaxAlive,
                    selfTeleportRadius: Int(row, "selfTeleportRadius", 0),
                    stealMoneyAmount: stealMoneyAmount);
                result.Add(id, pattern);
                presentations.Add(new MonsterPatternPresentationDefinition(id, vfxCueId, windup, cast, impact, Optional(row, "animationTrigger")));
            }

            return result;
        }

        private static int ResolvePatternStatusEffectAmount(IReadOnlyList<StatusEffectKind> statusEffects)
        {
            if (statusEffects == null || statusEffects.Count == 0)
            {
                return 0;
            }

            var amount = 0;
            foreach (var kind in statusEffects)
            {
                amount = Math.Max(amount, StatusEffectInfo.DefaultAmount(kind));
            }

            return amount;
        }

        private static List<MonsterCatalogEntry> CreateMonsterEntries(
            CsvTable monsters,
            CsvTable bindings,
            IReadOnlyDictionary<string, MonsterAttackPattern> patternDefinitions,
            MonsterCatalogCsvSource source)
        {
            var groupedBindings = bindings.Rows
                .Where(row => Bool(row, "enabled", true))
                .GroupBy(row => Required(row, "monsterId"), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.OrderBy(row => Int(row, "order", 0)).ToList(), StringComparer.Ordinal);

            var result = new List<MonsterCatalogEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in monsters.Rows)
            {
                var monsterId = Required(row, "monsterId");
                EnsureId(MonsterIdPattern, monsterId, monsters.Name, row.LineNumber);
                if (!seen.Add(monsterId))
                {
                    throw Duplicate(monsters.Name, row.LineNumber, "monsterId", monsterId);
                }

                var behaviorProfileRef = Required(row, "behaviorProfileRef");
                EnsureId(BehaviorProfileIdPattern, behaviorProfileRef, monsters.Name, row.LineNumber);

                if (!groupedBindings.TryGetValue(monsterId, out var monsterBindings) || monsterBindings.Count == 0)
                {
                    throw new ArgumentException($"{monsters.Name}:{row.LineNumber} monster '{monsterId}' has no enabled attack pattern bindings.");
                }

                var patterns = new List<MonsterAttackPattern>();
                foreach (var binding in monsterBindings)
                {
                    var patternId = Required(binding, "patternId");
                    RequireLookup(patternDefinitions, patternId, bindings.Name, binding.LineNumber, "patternId");
                    var pattern = patternDefinitions[patternId];
                    var overrideWeight = Optional(binding, "overrideWeight");
                    // phaseMin은 보스 페이즈 게이트(선택적, 기본 0=항상). 바인딩 소유 값이라 여기서 얹는다.
                    var phaseMin = Int(binding, "phaseMin", 0);
                    if (phaseMin < 0)
                    {
                        throw new ArgumentException($"{bindings.Name}:{binding.LineNumber} phaseMin cannot be negative.");
                    }

                    // phaseMax는 은퇴 게이트(선택적, 공란=은퇴 없음). phaseMin과 함께 [min, max] 창을 이룬다.
                    var phaseMax = Int(binding, "phaseMax", int.MaxValue);
                    if (phaseMax != int.MaxValue && phaseMax < phaseMin)
                    {
                        throw new ArgumentException(
                            $"{bindings.Name}:{binding.LineNumber} phaseMax ({phaseMax}) cannot be lower than phaseMin ({phaseMin}).");
                    }

                    // distMin은 거리 게이트(선택적, 기본 0=제한 없음). phaseMin과 같은 모양·같은 소유자다.
                    var distMin = Int(binding, "distMin", 0);
                    if (distMin < 0)
                    {
                        throw new ArgumentException($"{bindings.Name}:{binding.LineNumber} distMin cannot be negative.");
                    }

                    // distMax는 거리 상한(선택적, 공란=제한 없음). distMin과 함께 [min, max] 창을 이룬다.
                    var distMax = Int(binding, "distMax", int.MaxValue);
                    if (distMax != int.MaxValue && distMax < distMin)
                    {
                        throw new ArgumentException(
                            $"{bindings.Name}:{binding.LineNumber} distMax ({distMax}) cannot be lower than distMin ({distMin}).");
                    }

                    if (!string.IsNullOrWhiteSpace(overrideWeight) || phaseMin > 0 || phaseMax != int.MaxValue
                        || distMin > 0 || distMax != int.MaxValue)
                    {
                        pattern = new MonsterAttackPattern(
                            pattern.Id,
                            pattern.DisplayName,
                            pattern.Range,
                            pattern.AreaRadius,
                            pattern.Damage,
                            pattern.EffectRef,
                            pattern.Targeting,
                            Int(binding, "overrideWeight", pattern.Weight),
                            pattern.ShapeId,
                            pattern.StatusEffects.ToArray(),
                            pattern.StatusEffectDurationTurns,
                            pattern.StatusEffectAmount,
                            pattern.AnimationTrigger,
                            pattern.KnockbackDistance,
                            pattern.KnockbackImpactDamage,
                            pattern.CooldownTurns,
                            phaseMin,
                            phaseMax,
                            distMin,
                            distMax,
                            // ⚠️ 패턴 정의가 소유한 값은 여기서 <b>반드시 다시 넘겨야</b> 한다.
                            // 이 경로는 오버라이드가 하나라도 걸리면 패턴을 새로 만드는데, 빠뜨린 인자는
                            // 예외가 아니라 기본값(0)으로 조용히 내려앉아 저작이 사라진다.
                            // (hitCount가 실제로 이 함정을 밟았다 — A020 다단이 바인딩 오버라이드에서 1로 뭉개졌다.)
                            pattern.LeapRange,
                            pattern.InjectStatusCardId,
                            pattern.HitCount,
                            pattern.ShieldGain,
                            pattern.DamageJitter,
                            pattern.InjectStatusCardPool as string[]
                                ?? System.Linq.Enumerable.ToArray(pattern.InjectStatusCardPool),
                            pattern.ZoneStatusKind,
                            pattern.ZoneDurationTurns,
                            pattern.ZoneOffsets as SeoulPlayup.Map.Runtime.HexCoord[]
                                ?? System.Linq.Enumerable.ToArray(pattern.ZoneOffsets),
                            pattern.SummonMonsterDefinitionId,
                            pattern.SummonCount,
                            pattern.SummonMaxAlive,
                            pattern.SelfTeleportRadius,
                            pattern.StealMoneyAmount);
                    }

                    patterns.Add(pattern);
                }

                // Every monster must keep at least one always-available (cooldown 0) pattern so it can never
                // end up with its entire attack pool on cooldown at once (most monsters only have 2 patterns).
                // 보스 페이즈 게이트가 붙은 뒤로는 이 보장이 "1페이즈(게이트 0)에서도" 성립해야 한다:
                // phaseMin>0 패턴만 쿨다운 0이면 1페이즈 보스는 전 패턴이 쿨다운에 걸릴 수 있다.
                var alwaysUnlocked = patterns.Where(pattern => pattern.PhaseMin <= 0).ToList();
                if (alwaysUnlocked.Count == 0)
                {
                    throw new ArgumentException(
                        $"{monsters.Name}:{row.LineNumber} monster '{monsterId}' has no phaseMin=0 attack pattern; " +
                        "at least one bound pattern must be available in phase 1.");
                }

                // 쿨다운-0 보장은 "모든 게이트에서" 성립해야 한다. phaseMax(은퇴)가 생긴 뒤로 게이트별
                // 후보 집합이 줄어들 수 있으므로, 은퇴하지 않는(phaseMax 무한) 쿨다운-0 기본 패턴 하나를
                // 저작 규칙으로 강제한다 — "기본 공격은 은퇴하지 않는다". 게이트 수는 보스 카탈로그 소관이라
                // 여기서 게이트별 전수 검증은 불가능하고, 이 단일 규칙이 전 게이트를 커버한다.
                // 거리 창(distMin/distMax)이 생긴 뒤로는 같은 보장이 "모든 거리에서도" 성립해야 한다.
                // 거리 밴드만 걸린 패턴들로 풀을 채우면 어느 거리 구간에서 후보가 0이 될 수 있고, 그러면
                // 몬스터가 아무것도 못 뽑는 정지 상태가 된다. 그래서 기본 패턴 하나는 페이즈 축과 거리 축
                // <b>양쪽 모두</b>에서 무제한이어야 한다 — "기본 공격은 은퇴하지도, 거리를 가리지도 않는다".
                // (계획부에도 §2-5 폴백이 있어 이중 안전망이다.)
                if (!alwaysUnlocked.Any(pattern =>
                        pattern.CooldownTurns == 0
                        && pattern.PhaseMax == int.MaxValue
                        && pattern.DistMin == 0
                        && pattern.DistMax == int.MaxValue))
                {
                    throw new ArgumentException(
                        $"{monsters.Name}:{row.LineNumber} monster '{monsterId}' has no never-retiring cooldown-0 attack pattern; " +
                        "at least one bound pattern must have cooldownTurns=0, phaseMin=0, no phaseMax and no distMin/distMax.");
                }

                // T7-2 적 문법 3종. 헤더 자체가 없는 구 CSV/테스트 픽스처는 전부 기본값(문법 없음)으로
                // 내려앉는다(Optional/Int의 하위호환 규약). 뒤끝 ref/param은 임포트 시점에 파서를
                // 통과시켜 거부한다 — 집행이 같은 TryParse를 쓰므로 "임포트는 됐는데 안 터지는" 갭이 없다.
                var agitationMaxStacks = Int(row, "agitationMaxStacks", 0);
                if (agitationMaxStacks < 0)
                {
                    throw new ArgumentException($"{monsters.Name}:{row.LineNumber} agitationMaxStacks cannot be negative.");
                }

                var toughnessReloadTurns = Int(row, "toughnessReloadTurns", 0);
                if (toughnessReloadTurns < 0)
                {
                    throw new ArgumentException($"{monsters.Name}:{row.LineNumber} toughnessReloadTurns cannot be negative.");
                }

                var advanceAfterAttack = Bool(row, "advanceAfterAttack", false);
                // 수호 재충전(2026-09-05 결정 1). 헤더 없음·빈 값 = 0(특성 없음) — 구 CSV·픽스처 하위호환.
                var guardRechargeTurns = Int(row, "guardRechargeTurns", 0);
                if (guardRechargeTurns < 0)
                {
                    throw new ArgumentException($"{monsters.Name}:{row.LineNumber} guardRechargeTurns cannot be negative.");
                }

                // 힘 카운터 조건(2026-09-04 리워크 후 유일 등록 = 담력 시험). 임포트와 런타임이 같은 등록소를 본다.
                var agitationConditionRef = Optional(row, "agitationConditionRef");
                if (!MonsterAgitationCondition.IsRegistered(agitationConditionRef))
                {
                    throw new ArgumentException(
                        $"{monsters.Name}:{row.LineNumber} agitationConditionRef '{agitationConditionRef}' is not registered "
                        + $"(known: {MonsterAgitationCondition.StrengthDistanceRef}).");
                }

                if (!string.IsNullOrWhiteSpace(agitationConditionRef) && agitationMaxStacks <= 0)
                {
                    throw new ArgumentException(
                        $"{monsters.Name}:{row.LineNumber} agitationConditionRef는 agitationMaxStacks 없이 저작할 수 없다 "
                        + "— 조건만 있고 스택 상한이 없으면 아무것도 오르지 않는다.");
                }

                // 특성 1슬롯(hiddenTraitRef/Param) — ref가 갈래를 정한다: 은신(hidden.stealth) 또는
                // 홀림 오라 봉인(aura.seal · 2026-09-04 §2-C). 뒤끝과 같은 규약이라 임포트와 런타임이
                // 같은 TryParse를 지난다.
                var hiddenTraitRef = Optional(row, "hiddenTraitRef");
                var hiddenTraitParam = Optional(row, "hiddenTraitParam");
                var hiddenTrait = default(MonsterHiddenTraitSpec);
                if (MonsterAuraSeal.IsAuraSeal(hiddenTraitRef))
                {
                    if (!MonsterAuraSeal.TryParse(hiddenTraitRef, hiddenTraitParam, out _, out var auraError))
                    {
                        throw new ArgumentException($"{monsters.Name}:{row.LineNumber} {auraError}");
                    }
                }
                else if (!MonsterHiddenTrait.TryParse(hiddenTraitRef, hiddenTraitParam, out hiddenTrait, out var hiddenError))
                {
                    throw new ArgumentException($"{monsters.Name}:{row.LineNumber} {hiddenError}");
                }

                // 🔴 「어둠 먹기」는 약오름의 스택 저장·피해 보너스를 그대로 빌려 쓴다(요괴 §4-2).
                // 둘을 함께 저작하면 한 카운터에 기록자가 둘이 되어 수치가 조용히 엉킨다.
                if (hiddenTrait.HasGrowth && agitationMaxStacks > 0)
                {
                    throw new ArgumentException(
                        $"{monsters.Name}:{row.LineNumber} monster '{monsterId}' authors both agitation and hidden growth"
                        + " — 둘은 같은 스택 카운터를 쓴다. 하나만 저작한다.");
                }
                // 몸 형상(2026-09-03). 컬럼 없음·빈 값 = 한 칸 — 구 CSV·픽스처 하위호환.
                if (!MonsterFootprints.TryParse(Optional(row, "footprint"), out var footprintShape, out var footprintError))
                {
                    throw new ArgumentException($"{monsters.Name}:{row.LineNumber} {footprintError}");
                }

                var onDeathEffectRef = Optional(row, "onDeathEffectRef");
                var onDeathEffectParam = Optional(row, "onDeathEffectParam");
                if (!string.IsNullOrWhiteSpace(onDeathEffectRef)
                    && !MonsterDeathAftermath.TryParse(onDeathEffectRef, onDeathEffectParam, out _, out var aftermathError))
                {
                    throw new ArgumentException($"{monsters.Name}:{row.LineNumber} {aftermathError}");
                }

                // 스폰 시 체력 변주(±%). 컬럼 없음·빈 값 = 0(고정 체력) — 하위호환 규약.
                var hpVariancePct = Int(row, "hpVariancePct", 0);
                if (hpVariancePct < 0 || hpVariancePct > 50)
                {
                    throw new ArgumentException($"{monsters.Name}:{row.LineNumber} hpVariancePct must be 0..50.");
                }

                result.Add(new MonsterCatalogEntry(
                    monsterId,
                    Required(row, "displayName"),
                    Required(row, "archetype"),
                    behaviorProfileRef,
                    Int(row, "detectionRange", 1),
                    Int(row, "movePerTurn", 1),
                    Int(row, "hp", 1),
                    Optional(row, "status"),
                    Int(row, "attackSpeed", 1),
                    patterns.ToArray(),
                    Optional(row, "visualPrefabPath"),
                    agitationMaxStacks,
                    toughnessReloadTurns,
                    onDeathEffectRef,
                    onDeathEffectParam,
                    // 견고: 헤더가 없는 구 CSV·픽스처는 false(방어막 턴 소멸 = 기본 규칙)로 내려앉는다.
                    Bool(row, "sturdyBlock", false),
                    hpVariancePct,
                    // 밀어붙이기: 헤더가 없는 구 CSV·픽스처는 false(전진 없음)로 내려앉는다.
                    advanceAfterAttack,
                    hiddenTraitRef,
                    hiddenTraitParam,
                    agitationConditionRef,
                    footprintShape,
                    guardRechargeTurns));
            }

            var boundMonsterIds = groupedBindings.Keys.Where(id => !seen.Contains(id)).ToList();
            if (boundMonsterIds.Count > 0)
            {
                throw new ArgumentException($"{bindings.Name} references missing monsterId '{boundMonsterIds[0]}'.");
            }

            return result;
        }

        private static StatusEffectKind[] ParseStatusEffects(string value, string fileName, int lineNumber)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return Array.Empty<StatusEffectKind>();
            }

            return value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(part => ParseEnum<StatusEffectKind>(part.Trim(), fileName, lineNumber, "statusEffects"))
                .ToArray();
        }

        private static void RequireLookup<T>(IReadOnlyDictionary<string, T> lookup, string id, string fileName, int lineNumber, string column)
        {
            if (string.IsNullOrWhiteSpace(id) || !lookup.ContainsKey(id))
            {
                throw new ArgumentException($"{fileName}:{lineNumber} {column} references missing id '{id}'.");
            }
        }

        private static void RequireOptionalLookup<T>(IReadOnlyDictionary<string, T> lookup, string id, string fileName, int lineNumber, string column)
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                RequireLookup(lookup, id, fileName, lineNumber, column);
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

        private static T ParseEnum<T>(string value, string fileName, int lineNumber, string column) where T : struct
        {
            if (Enum.TryParse<T>(value, ignoreCase: false, out var parsed))
            {
                return parsed;
            }

            throw new ArgumentException($"{fileName}:{lineNumber} column '{column}' has unknown value '{value}'.");
        }

        private static void EnsureId(Regex pattern, string id, string fileName, int lineNumber)
        {
            if (!pattern.IsMatch(id))
            {
                throw new ArgumentException($"{fileName}:{lineNumber} id '{id}' does not match required format.");
            }
        }

        private static ArgumentException Duplicate(string fileName, int lineNumber, string column, string id)
        {
            return new ArgumentException($"{fileName}:{lineNumber} duplicates {column} '{id}'.");
        }
    }
}
