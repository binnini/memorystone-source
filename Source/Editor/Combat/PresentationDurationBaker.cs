using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.EditorTools;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Measures how long presentation assets actually play and writes the numbers into data
/// (docs/presentation-duration-data-plan.md P2). Read-and-write on data only: it never edits a prefab, clip,
/// scene, or animator, and it only ever writes the <c>measured*</c> fields — an authored duration or a
/// <c>lifetimeOverride</c> is left exactly as the designer left it (plan D1/D7).
///
/// ⚠️ The VFX catalog is the primary target, not the CSVs: 110 catalog entries exist but only ~63 have a CSV
/// row (status.apply.*, field.place.*, object.treasure_chest.* and the loop cues are authored straight into
/// the asset, and the CSV rebuild MERGES rather than replaces). Baking CSV-only would leave ~46 entries
/// permanently unmeasured. CSV rows are updated too, so a later rebuild round-trips instead of reverting.
/// </summary>
public static class PresentationDurationBaker
{
    private const string VfxCatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";
    private const string SoundCatalogPath = "Assets/Data/Combat/Presentation/Catalogs/SoundCatalog.asset";
    private const string AnimationSetsCsv = "Assets/Data/Combat/Monsters/Source/monster_animation_sets.csv";
    private const string AttackClipsCsv = "Assets/Data/Combat/Monsters/Source/monster_animation_attack_clips.csv";

    private const string MeasuredColumn = "measuredLengthSeconds";

    /// <summary>
    /// Unit separator between the parts of a composite row key. Explicit for two reasons: plain
    /// concatenation is ambiguous (monsterId "M0" + trigger "01Attack1" would collide with "M001" +
    /// "Attack1"), and the lookup dictionary and the row probe MUST build keys identically — building them
    /// with two separate expressions is how they drift apart.
    /// </summary>
    private const string KeySeparator = "\u001F";

    /// <summary>Single key builder for both sides of the lookup. Trims so a padded cell still matches.</summary>
    private static string BuildRowKey(IEnumerable<string> parts)
        => string.Join(KeySeparator, parts.Select(part => (part ?? string.Empty).Trim()));

    [MenuItem("Tools/Seoul Playup/Combat/Bake Presentation Durations")]
    public static void BakeMenu()
    {
        // 🔴 창을 직접 띄우지 않는다 — 에이전트가 이 메뉴를 부르면 아무도 없는 화면에 모달이 떠서
        // 에디터와 MCP 플러그인이 통째로 멈춘다(실측). 자동 호출은 EditorReportDialog가 걸러 낸다.
        EditorReportDialog.Report("Bake Presentation Durations", Bake());
    }

    public static string Bake()
    {
        var lines = new List<string>();
        lines.Add(BakeVfx());
        lines.Add(BakeSound());
        lines.Add(BakeAnimation());
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return string.Join("\n\n", lines);
    }

    // ---------------------------------------------------------------- VFX

    private static string BakeVfx()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(VfxCatalogPath);
        if (catalog == null)
        {
            return $"VFX: ERROR missing catalog {VfxCatalogPath}";
        }

        var measuredByCueId = new Dictionary<string, float>(StringComparer.Ordinal);
        var serialized = new SerializedObject(catalog);
        var entries = serialized.FindProperty("entries");

        int measured = 0, loopSkipped = 0, noParticles = 0, loopingParticles = 0, noPrefab = 0;
        var unmeasurable = new List<string>();

        for (var i = 0; i < entries.arraySize; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i);
            var cueId = entry.FindPropertyRelative("cueId").stringValue;
            var isLoop = entry.FindPropertyRelative("loop").boolValue;
            var target = entry.FindPropertyRelative(MeasuredColumn);

            if (isLoop)
            {
                // A loop cue lives until its status is removed. Leaving 0 here is not "unmeasured" sloppiness:
                // PresentationDuration reports loop cues as unmeasurable, so 0 can never read as instant.
                target.floatValue = 0f;
                loopSkipped++;
                continue;
            }

            var prefab = ResolveFirstPrefab(entry);
            if (prefab == null)
            {
                target.floatValue = 0f;
                noPrefab++;
                unmeasurable.Add($"{Label(cueId, i)}: no prefab");
                continue;
            }

            var result = PresentationDurationProbe.MeasurePrefab(prefab);
            if (result.HasNoParticles)
            {
                target.floatValue = 0f;
                noParticles++;
                unmeasurable.Add($"{Label(cueId, i)}: no ParticleSystem ({prefab.name})");
                continue;
            }

            if (result.HasLoopingParticles)
            {
                // The prefab contains a system that never stops emitting, so it has no end of its own — only
                // the destroy timer ends it. Storing the cycle length would be a lie.
                target.floatValue = 0f;
                loopingParticles++;
                unmeasurable.Add(
                    $"{Label(cueId, i)}: {result.LoopingCount}/{result.ParticleCount} looping ParticleSystems ({prefab.name})");
                continue;
            }

            target.floatValue = result.NaturalSeconds;
            measured++;
            if (!string.IsNullOrWhiteSpace(cueId))
            {
                measuredByCueId[cueId] = result.NaturalSeconds;
            }
        }

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);

        var csvWrites = UpsertColumn(CombatCsvPaths.CombatVfxCuesCsv, "vfxCueId", MeasuredColumn, measuredByCueId)
            + UpsertColumn(CombatCsvPaths.CardVfxCuesCsv, "cueId", MeasuredColumn, measuredByCueId);

        var builder = new StringBuilder();
        builder.AppendLine($"VFX ({entries.arraySize} catalog entries): measured {measured}, "
            + $"loop cues {loopSkipped}, no ParticleSystem {noParticles}, looping particles {loopingParticles}, "
            + $"no prefab {noPrefab}. CSV rows updated: {csvWrites}.");
        if (unmeasurable.Count > 0)
        {
            builder.AppendLine("  Needs an authored length (measurement impossible):");
            foreach (var note in unmeasurable)
            {
                builder.AppendLine($"    {note}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static GameObject ResolveFirstPrefab(SerializedProperty entry)
    {
        var prefabs = entry.FindPropertyRelative("prefabs");
        for (var i = 0; i < prefabs.arraySize; i++)
        {
            if (prefabs.GetArrayElementAtIndex(i).objectReferenceValue is GameObject prefab)
            {
                return prefab;
            }
        }

        return null;
    }


    // -------------------------------------------------------------- Sound

    private static string BakeSound()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(SoundCatalogPath);
        if (catalog == null)
        {
            return $"Sound: ERROR missing catalog {SoundCatalogPath}";
        }

        var measuredByCueId = new Dictionary<string, float>(StringComparer.Ordinal);
        var serialized = new SerializedObject(catalog);
        var entries = serialized.FindProperty("entries");

        int measured = 0, looping = 0, noClip = 0, inexact = 0;

        for (var i = 0; i < entries.arraySize; i++)
        {
            var entry = entries.GetArrayElementAtIndex(i);
            var cueId = entry.FindPropertyRelative("cueId").stringValue;
            var bus = (SoundBus)entry.FindPropertyRelative("bus").enumValueIndex;
            var target = entry.FindPropertyRelative(MeasuredColumn);

            if (bus == SoundBus.Music || bus == SoundBus.Ambience)
            {
                // Looping beds: a clip length is not a play length.
                target.floatValue = 0f;
                looping++;
                continue;
            }

            if (!(entry.FindPropertyRelative("clip").objectReferenceValue is AudioClip clip))
            {
                target.floatValue = 0f;
                noClip++;
                continue;
            }

            var pitchMin = entry.FindPropertyRelative("pitchMin").floatValue;
            var pitchMax = entry.FindPropertyRelative("pitchMax").floatValue;
            var bound = PresentationDurationProbe.MeasureAudioUpperBound(clip, pitchMin, pitchMax);

            target.floatValue = bound;
            measured++;
            if (pitchMin < pitchMax)
            {
                inexact++;
            }

            if (!string.IsNullOrWhiteSpace(cueId))
            {
                measuredByCueId[cueId] = bound;
            }
        }

        serialized.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);

        var csvWrites = UpsertColumn(CombatCsvPaths.CombatSoundCuesCsv, "soundCueId", MeasuredColumn, measuredByCueId);

        return $"Sound ({entries.arraySize} catalog entries): measured {measured} "
            + $"(of which {inexact} are upper bounds only — randomized pitch), "
            + $"looping buses {looping}, no clip {noClip}. CSV rows updated: {csvWrites}.";
    }

    // ---------------------------------------------------------- Animation

    private static string BakeAnimation()
    {
        if (!File.Exists(AnimationSetsCsv))
        {
            return $"Animation: ERROR missing {AnimationSetsCsv}";
        }

        // clipName -> length, across every monster FBX referenced by the animation sets. Clip names are
        // unique per monster in practice (Dog_attack1, bulgasal_die, ...), and a collision would mean two
        // monsters share a clip name, which the report surfaces rather than silently resolving.
        var lengthsByClipName = new Dictionary<string, float>(StringComparer.Ordinal);
        var collisions = new List<string>();
        var rows = ReadCsvRows(AnimationSetsCsv);

        foreach (var row in rows)
        {
            if (!row.TryGetValue("modelAssetPath", out var modelPath) || string.IsNullOrWhiteSpace(modelPath))
            {
                continue;
            }

            foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>())
            {
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }

                if (lengthsByClipName.TryGetValue(clip.name, out var existing))
                {
                    if (Math.Abs(existing - clip.length) > 0.0001f)
                    {
                        collisions.Add($"{clip.name} ({existing:0.###} vs {clip.length:0.###})");
                    }

                    continue;
                }

                lengthsByClipName[clip.name] = clip.length;
            }
        }

        // One-shot clips only. idle/move loop, so a clip length is not a play length for them (plan §4.4) —
        // they are intentionally left without a column rather than given a misleading number.
        var oneShotColumns = new[]
        {
            ("hitClip", "hitClipLengthSeconds"),
            ("knockbackClip", "knockbackClipLengthSeconds"),
            ("deathClip", "deathClipLengthSeconds")
        };

        var setWrites = 0;
        foreach (var (clipColumn, lengthColumn) in oneShotColumns)
        {
            var byKey = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                if (!row.TryGetValue("monsterId", out var monsterId) || string.IsNullOrWhiteSpace(monsterId))
                {
                    continue;
                }

                if (row.TryGetValue(clipColumn, out var clipName)
                    && !string.IsNullOrWhiteSpace(clipName)
                    && lengthsByClipName.TryGetValue(clipName.Trim(), out var length))
                {
                    byKey[BuildRowKey(new[] { monsterId })] = length;
                }
            }

            setWrites += UpsertColumn(AnimationSetsCsv, "monsterId", lengthColumn, byKey);
        }

        var attackWrites = 0;
        var missingAttackClips = new List<string>();
        if (File.Exists(AttackClipsCsv))
        {
            // Keyed by monsterId+animationTrigger: clipName alone is not unique across rows, and monsterId
            // alone repeats (one row per attack).
            var byKey = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (var row in ReadCsvRows(AttackClipsCsv))
            {
                row.TryGetValue("monsterId", out var monsterId);
                row.TryGetValue("animationTrigger", out var trigger);
                row.TryGetValue("clipName", out var clipName);
                if (string.IsNullOrWhiteSpace(clipName))
                {
                    continue;
                }

                if (lengthsByClipName.TryGetValue(clipName.Trim(), out var length))
                {
                    byKey[BuildRowKey(new[] { monsterId, trigger })] = length;
                }
                else
                {
                    missingAttackClips.Add($"{monsterId}/{trigger} -> {clipName}");
                }
            }

            attackWrites = UpsertCompositeKeyColumn(
                AttackClipsCsv,
                new[] { "monsterId", "animationTrigger" },
                MeasuredColumn,
                byKey);
        }

        var builder = new StringBuilder();
        builder.AppendLine($"Animation: {lengthsByClipName.Count} clips measured from FBX. "
            + $"animation_sets rows updated: {setWrites} (hit/knockback/death only — idle/move loop), "
            + $"attack_clips rows updated: {attackWrites}.");
        if (missingAttackClips.Count > 0)
        {
            builder.AppendLine("  Attack rows whose clipName was not found in any FBX:");
            foreach (var note in missingAttackClips)
            {
                builder.AppendLine($"    {note}");
            }
        }

        if (collisions.Count > 0)
        {
            builder.AppendLine("  WARNING clip name collisions across FBX (first one wins):");
            foreach (var note in collisions)
            {
                builder.AppendLine($"    {note}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    // ---------------------------------------------------------- CSV write

    private static int UpsertColumn(
        string path,
        string keyColumn,
        string valueColumn,
        IReadOnlyDictionary<string, float> valuesByKey)
        => UpsertCompositeKeyColumn(path, new[] { keyColumn }, valueColumn, valuesByKey);

    /// <summary>
    /// Writes <paramref name="valueColumn"/> for every row whose key is in <paramref name="valuesByKey"/>,
    /// appending the column (and padding existing rows) when it does not exist yet. Rows with no entry are
    /// left untouched rather than zeroed, so a partial bake cannot erase a previous full one.
    /// </summary>
    private static int UpsertCompositeKeyColumn(
        string path,
        IReadOnlyList<string> keyColumns,
        string valueColumn,
        IReadOnlyDictionary<string, float> valuesByKey)
    {
        if (!File.Exists(path) || valuesByKey.Count == 0)
        {
            return 0;
        }

        // The BOM has to be sniffed from the raw bytes: File.ReadAllText strips it while decoding, so testing
        // the decoded first line always says "no BOM" and the file would silently lose it on write. Three of
        // these CSVs ship with one, and rewriting them without it is a diff this tool has no business making.
        var raw = File.ReadAllBytes(path);
        var hadBom = raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF;

        var text = File.ReadAllText(path, Encoding.UTF8);
        var newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var hadTrailingNewline = text.EndsWith("\n", StringComparison.Ordinal);
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        while (lines.Count > 0 && lines[lines.Count - 1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0)
        {
            return 0;
        }

        var headers = SplitCsvLine(lines[0]);
        var quoted = lines[0].Contains("\"");

        var keyIndices = new List<int>();
        foreach (var keyColumn in keyColumns)
        {
            var index = headers.FindIndex(h => string.Equals(h.Trim(), keyColumn, StringComparison.Ordinal));
            if (index < 0)
            {
                Debug.LogWarning($"[PresentationDurationBaker] {path} has no '{keyColumn}' column; skipped.");
                return 0;
            }

            keyIndices.Add(index);
        }

        // Bail before touching the file when no row here is measurable. Without this, a CSV that shares no
        // key with the catalog (combat_sound_cues.csv ships S001..S004 while the catalog holds semantic ids,
        // so they never intersect) would still get an empty column appended and every line rewritten.
        var matchAny = false;
        for (var i = 1; i < lines.Count && !matchAny; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var probe = SplitCsvLine(lines[i]);
            if (keyIndices.All(index => index < probe.Count))
            {
                matchAny = valuesByKey.ContainsKey(BuildRowKey(keyIndices.Select(index => probe[index])));
            }
        }

        if (!matchAny)
        {
            return 0;
        }

        var valueIndex = headers.FindIndex(h => string.Equals(h.Trim(), valueColumn, StringComparison.Ordinal));
        if (valueIndex < 0)
        {
            headers.Add(valueColumn);
            valueIndex = headers.Count - 1;
            lines[0] = JoinCsvLine(headers, quoted);
        }

        var written = 0;
        for (var i = 1; i < lines.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var values = SplitCsvLine(lines[i]);
            var rowQuoted = lines[i].Contains("\"");
            while (values.Count < headers.Count)
            {
                values.Add(string.Empty);
            }

            var key = BuildRowKey(keyIndices.Select(index => values[index]));
            if (!valuesByKey.TryGetValue(key, out var value))
            {
                lines[i] = JoinCsvLine(values, rowQuoted);
                continue;
            }

            values[valueIndex] = value.ToString("0.###", CultureInfo.InvariantCulture);
            lines[i] = JoinCsvLine(values, rowQuoted);
            written++;
        }

        File.WriteAllText(
            path,
            string.Join(newline, lines) + (hadTrailingNewline ? newline : string.Empty),
            new UTF8Encoding(hadBom));
        return written;
    }

    private static List<IReadOnlyDictionary<string, string>> ReadCsvRows(string path)
    {
        var rows = new List<IReadOnlyDictionary<string, string>>();
        var lines = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
        if (lines.Length == 0)
        {
            return rows;
        }

        var headers = SplitCsvLine(lines[0].TrimStart('﻿')).Select(h => h.Trim()).ToList();
        for (var i = 1; i < lines.Length; i++)
        {
            var values = SplitCsvLine(lines[i]);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < headers.Count; c++)
            {
                row[headers[c]] = c < values.Count ? values[c] : string.Empty;
            }

            rows.Add(row);
        }

        return rows;
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

        values.Add(builder.ToString());
        return values;
    }

    // Preserves whichever quoting style the file already used: combat_card_vfx_cues.csv quotes every cell
    // while combat_sound_cues.csv quotes none, and flipping either would produce a diff of the whole file.
    private static string JoinCsvLine(IReadOnlyList<string> values, bool quoted)
        => quoted
            ? string.Join(",", values.Select(v => "\"" + (v ?? string.Empty).Replace("\"", "\"\"") + "\""))
            : string.Join(",", values.Select(v => v ?? string.Empty));

    private static string Label(string cueId, int index)
        => string.IsNullOrWhiteSpace(cueId) ? $"entry#{index}" : cueId;
}
