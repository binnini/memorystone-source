#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using SeoulPlayup.Combat.Runtime;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_PresentationDurationAudit
    {
        // A measured length is a float written by a tool and re-derived here, so exact equality would flag
        // float formatting noise as drift. One millisecond is far below anything a designer can perceive and
        // far above the round-trip error of "0.###" formatting in the CSVs.
        private const float ToleranceSeconds = 0.001f;

        [AiTool
        (
            "presentation-duration-audit",
            Title = "Presentation Duration / Drift Audit",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("베이크된 연출 길이 데이터가 실제 에셋과 어긋났는지 감사한다(docs/presentation-duration-data-plan.md P3). " +
            "프리팹의 ParticleSystem·AudioClip·FBX AnimationClip을 **다시 측정해** 카탈로그/CSV에 저장된 " +
            "measuredLengthSeconds와 **내용으로** 비교한다(mtime 아님 — VCS 사이에서 신뢰할 수 없다). " +
            "검출 항목: ①에셋이 바뀌었는데 베이크를 안 돌린 stale 측정값, ②측정 불가한 큐(loop·파티클 없음·루프 파티클)에 " +
            "손으로 써넣은 measured 값, ③길이를 아는 값이 아무것도 없는 큐(authored 저작이 유일한 답), " +
            "④CSV와 카탈로그 불일치(리빌드가 베이크를 되돌린다), ⑤FBX에 없는 클립을 가리키는 애니메이션 행. " +
            "③에서는 **런타임에 도달 불가한 엔트리를 제외**한다 — kind-tier는 (EffectKind, targetFilter) 키의 " +
            "첫 엔트리만 선택되므로 뒤에 가려진 엔트리는 무엇을 저작해도 발동하지 않는다(그건 " +
            "vfxUnreachableKindTierEntries로 따로 보고하며, 고칠 방법은 저작이 아니라 삭제·재키다). " +
            "**loop 큐의 authored 누락은 정보성이며 allOk에 반영하지 않는다** — loop는 상태이상이 사라질 때까지 " +
            "재생되어 말할 길이가 없고, 애초에 TryResolve/ResolveAll이 loop 큐를 배제하므로 어떤 소비자도 " +
            "길이를 물을 수 없다. " +
            "베이크 도구와 **같은 측정 경로**(PresentationDurationProbe)를 쓰므로 감사와 베이크가 갈라질 수 없다. " +
            "에셋을 쓰지 않는 읽기 전용 감사.")]
        public PresentationDurationAuditResult Audit
        (
            [Description("EffectVfxCatalog 에셋 경로. 기본은 런타임 기본 카탈로그.")]
            string vfxCatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset",
            [Description("SoundCatalog 에셋 경로.")]
            string soundCatalogPath = "Assets/Data/Combat/Presentation/Catalogs/SoundCatalog.asset"
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var result = new PresentationDurationAuditResult
                {
                    vfxCatalogPath = vfxCatalogPath,
                    soundCatalogPath = soundCatalogPath
                };

                AuditVfx(result, vfxCatalogPath);
                AuditSound(result, soundCatalogPath);
                AuditAnimation(result);

                result.allOk =
                    result.vfxStaleMeasurements.Count == 0 &&
                    result.vfxHandEditedMeasurements.Count == 0 &&
                    result.vfxWithoutAnyLength.Count == 0 &&
                    result.csvCatalogMismatches.Count == 0 &&
                    result.soundStaleMeasurements.Count == 0 &&
                    result.soundWithoutAnyLength.Count == 0 &&
                    result.animationStaleMeasurements.Count == 0 &&
                    result.animationMissingClips.Count == 0;

                result.summary =
                    $"VFX {result.vfxResolvable}/{result.vfxEntries} resolvable (measured {result.vfxMeasured}), "
                    + $"stale {result.vfxStaleMeasurements.Count}, hand-edited {result.vfxHandEditedMeasurements.Count}, "
                    + $"needsAuthoring {result.vfxWithoutAnyLength.Count}, unreachable {result.vfxUnreachableKindTierEntries.Count}, "
                    + $"loopNoAuthored(info) {result.loopCuesWithoutAuthoredLength.Count}, "
                    + $"csv mismatch {result.csvCatalogMismatches.Count} | "
                    + $"Sound {result.soundMeasured}/{result.soundEntries} measured, stale {result.soundStaleMeasurements.Count}, "
                    + $"no length {result.soundWithoutAnyLength.Count}, inexact {result.soundInexactByPitchRange.Count} | "
                    + $"Animation {result.animationClipsMeasured} clips, stale {result.animationStaleMeasurements.Count}, "
                    + $"missing {result.animationMissingClips.Count}";

                return result;
            });
        }

        /// <summary>
        /// Indices of kind-tier entries that can never be selected. Both <c>TryResolve</c> and
        /// <c>ResolveAll</c> return the FIRST entry matching an (EffectKind, targetFilter) key in that tier,
        /// so every later entry sharing a key is dead weight — authoring a length on one would change nothing.
        /// Kept separate from the duration findings because the fix is to delete or re-key the entry, not to
        /// author a number.
        /// </summary>
        private static HashSet<int> FindUnreachableKindTierEntries(
            EffectVfxCatalog.Entry[] entries,
            PresentationDurationAuditResult result)
        {
            var winnerByKey = new Dictionary<string, int>(StringComparer.Ordinal);
            var unreachable = new HashSet<int>();

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                // Mirror MatchesKind's gate: anything with a sourceRef/sourceCardId/statusKind rule or a loop
                // flag is reached through a different tier and is not shadowed by this one.
                if (entry == null
                    || entry.Deprecated
                    || entry.Loop
                    || entry.MatchStatusKind
                    || entry.HasSourceRefRule
                    || entry.HasSourceCardIdRule)
                {
                    continue;
                }

                var key = $"{entry.Kind}/{entry.TargetFilter}";
                if (winnerByKey.TryGetValue(key, out var winner))
                {
                    unreachable.Add(i);
                    result.vfxUnreachableKindTierEntries.Add(
                        $"{Label(entry, i)}: shadowed by {Label(entries[winner], winner)} on kind-tier key {key} "
                        + $"(prefab {PrefabName(entry)}) — can never be selected at runtime");
                }
                else
                {
                    winnerByKey[key] = i;
                }
            }

            return unreachable;
        }

        private static string Label(EffectVfxCatalog.Entry entry, int index)
            => entry != null && !string.IsNullOrWhiteSpace(entry.CueId) ? entry.CueId : $"entry#{index}";

        private static string PrefabName(EffectVfxCatalog.Entry entry)
        {
            var prefab = entry.Prefabs.FirstOrDefault(p => p != null);
            return prefab != null ? prefab.name : "(no prefab)";
        }

        // ------------------------------------------------------------------ VFX

        private static void AuditVfx(PresentationDurationAuditResult result, string catalogPath)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(catalogPath);
            if (catalog == null)
            {
                throw new InvalidOperationException($"No EffectVfxCatalog asset at '{catalogPath}'.");
            }

            var entries = catalog.Entries;
            result.vfxEntries = entries.Length;

            var measuredByCueId = new Dictionary<string, float>(StringComparer.Ordinal);
            var unreachable = FindUnreachableKindTierEntries(entries, result);

            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(entry.CueId) ? $"entry#{i}" : entry.CueId;
                var stored = entry.MeasuredLengthSeconds;
                var duration = entry.Duration;
                // A shadowed kind-tier entry can never play, so it needs no length. Drift and hand-edit
                // checks still apply below — a stale number is still wrong even on a dead entry.
                var needsLength = !unreachable.Contains(i);

                if (duration.IsKnown)
                {
                    result.vfxResolvable++;
                }

                if (stored > 0f)
                {
                    result.vfxMeasured++;
                }

                if (entry.Loop)
                {
                    // Loop cues are never measurable, so any stored measurement is hand-written.
                    if (stored > 0f)
                    {
                        result.vfxHandEditedMeasurements.Add(
                            $"{label}: loop cue carries measuredLengthSeconds={Fmt(stored)} (a loop has no natural length)");
                    }

                    if (entry.AuthoredLengthSeconds <= 0f)
                    {
                        result.loopCuesWithoutAuthoredLength.Add(
                            $"{label}: loop cue with no authoredLengthSeconds — nothing can say how long it plays");
                    }

                    continue;
                }

                var prefab = entry.Prefabs.FirstOrDefault(p => p != null);
                if (prefab == null)
                {
                    // Reported by combat-vfx-audit as a broken reference; here it only matters as a reason the
                    // length is unknowable.
                    if (!duration.IsKnown && needsLength)
                    {
                        result.vfxWithoutAnyLength.Add($"{label}: no prefab and no authored length");
                    }

                    continue;
                }

                var fresh = PresentationDurationProbe.MeasurePrefab(prefab);

                if (!fresh.IsMeasurable)
                {
                    var reason = fresh.HasNoParticles
                        ? "no ParticleSystem"
                        : fresh.HasLoopingParticles
                            ? $"{fresh.LoopingCount}/{fresh.ParticleCount} looping ParticleSystems"
                            : "natural length is zero";

                    if (stored > 0f)
                    {
                        result.vfxHandEditedMeasurements.Add(
                            $"{label}: measuredLengthSeconds={Fmt(stored)} but the prefab is not measurable ({reason}, {prefab.name})");
                    }

                    if (!duration.IsKnown && needsLength)
                    {
                        result.vfxWithoutAnyLength.Add(
                            $"{label}: not measurable ({reason}, {prefab.name}), no authoredLengthSeconds, no lifetimeOverride");
                    }

                    continue;
                }

                if (Mathf.Abs(fresh.NaturalSeconds - stored) > ToleranceSeconds)
                {
                    result.vfxStaleMeasurements.Add(
                        $"{label}: stored {Fmt(stored)}s but {prefab.name} now measures {Fmt(fresh.NaturalSeconds)}s "
                        + "— re-run 'Bake Presentation Durations'");
                }

                if (!string.IsNullOrWhiteSpace(entry.CueId))
                {
                    measuredByCueId[entry.CueId] = stored;
                }
            }

            CompareCsvAgainstCatalog(result, CombatCsvPaths.CombatVfxCuesCsv, "vfxCueId", measuredByCueId);
            CompareCsvAgainstCatalog(result, CombatCsvPaths.CardVfxCuesCsv, "cueId", measuredByCueId);
        }

        /// <summary>
        /// A CSV row that disagrees with the catalog is a live hazard rather than cosmetic: the
        /// "Rebuild VFX Catalog From Combat CSV" menu overwrites the catalog entry from the row, so a stale
        /// row silently reverts a good bake the next time anyone rebuilds.
        /// </summary>
        private static void CompareCsvAgainstCatalog(
            PresentationDurationAuditResult result,
            string csvPath,
            string keyColumn,
            IReadOnlyDictionary<string, float> catalogByCueId)
        {
            if (!File.Exists(csvPath))
            {
                return;
            }

            var rows = ReadCsvRows(csvPath, out var hasMeasuredColumn);
            if (!hasMeasuredColumn)
            {
                return;
            }

            var fileName = Path.GetFileName(csvPath);
            foreach (var row in rows)
            {
                if (!row.TryGetValue(keyColumn, out var cueId) || string.IsNullOrWhiteSpace(cueId))
                {
                    continue;
                }

                cueId = cueId.Trim();
                if (!catalogByCueId.TryGetValue(cueId, out var catalogValue))
                {
                    continue;
                }

                row.TryGetValue("measuredLengthSeconds", out var raw);
                var csvValue = ParseFloat(raw);
                if (Mathf.Abs(csvValue - catalogValue) > ToleranceSeconds)
                {
                    result.csvCatalogMismatches.Add(
                        $"{fileName}:{cueId} has {Fmt(csvValue)}s but the catalog has {Fmt(catalogValue)}s "
                        + "— a CSV rebuild would overwrite the catalog with the stale row");
                }
            }
        }

        // ---------------------------------------------------------------- Sound

        private static void AuditSound(PresentationDurationAuditResult result, string catalogPath)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(catalogPath);
            if (catalog == null)
            {
                throw new InvalidOperationException($"No SoundCatalog asset at '{catalogPath}'.");
            }

            var entries = catalog.Entries;
            result.soundEntries = entries.Count;

            foreach (var entry in entries)
            {
                if (entry == null)
                {
                    continue;
                }

                var label = string.IsNullOrWhiteSpace(entry.CueId) ? "(unnamed cue)" : entry.CueId;
                var stored = entry.MeasuredLengthSeconds;
                if (stored > 0f)
                {
                    result.soundMeasured++;
                }

                // Music/Ambience loop, so a clip length is not a play length; they are out of scope by design.
                if (entry.Bus == SoundBus.Music || entry.Bus == SoundBus.Ambience)
                {
                    continue;
                }

                if (entry.Clip == null)
                {
                    if (!entry.Duration.IsKnown)
                    {
                        result.soundWithoutAnyLength.Add($"{label}: no clip and no authoredLengthSeconds");
                    }

                    continue;
                }

                if (!entry.HasDeterministicLength)
                {
                    result.soundInexactByPitchRange.Add(
                        $"{label}: pitch {Fmt(entry.PitchMin)}..{Fmt(entry.PitchMax)} — stored length is an upper bound");
                }

                var fresh = PresentationDurationProbe.MeasureAudioUpperBound(entry.Clip, entry.PitchMin, entry.PitchMax);
                if (Mathf.Abs(fresh - stored) > ToleranceSeconds)
                {
                    result.soundStaleMeasurements.Add(
                        $"{label}: stored {Fmt(stored)}s but {entry.Clip.name} at pitch {Fmt(entry.PitchMin)} "
                        + $"now measures {Fmt(fresh)}s — re-run 'Bake Presentation Durations'");
                }
            }
        }

        // ------------------------------------------------------------ Animation

        private static void AuditAnimation(PresentationDurationAuditResult result)
        {
            const string setsCsv = "Assets/Data/Combat/Monsters/Source/monster_animation_sets.csv";
            const string attackClipsCsv = "Assets/Data/Combat/Monsters/Source/monster_animation_attack_clips.csv";

            if (!File.Exists(setsCsv))
            {
                return;
            }

            var setRows = ReadCsvRows(setsCsv, out _);
            var lengthsByClipName = new Dictionary<string, float>(StringComparer.Ordinal);

            foreach (var row in setRows)
            {
                if (!row.TryGetValue("modelAssetPath", out var modelPath) || string.IsNullOrWhiteSpace(modelPath))
                {
                    continue;
                }

                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(modelPath.Trim()).OfType<AnimationClip>())
                {
                    if (clip == null || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (!lengthsByClipName.ContainsKey(clip.name))
                    {
                        lengthsByClipName[clip.name] = clip.length;
                    }
                }
            }

            result.animationClipsMeasured = lengthsByClipName.Count;

            // The one-shot clips the bake writes columns for. idle/move loop and deliberately have none.
            var setColumns = new[]
            {
                ("hitClip", "hitClipLengthSeconds"),
                ("knockbackClip", "knockbackClipLengthSeconds"),
                ("deathClip", "deathClipLengthSeconds")
            };

            foreach (var row in setRows)
            {
                row.TryGetValue("monsterId", out var monsterId);
                foreach (var (clipColumn, lengthColumn) in setColumns)
                {
                    if (!row.TryGetValue(clipColumn, out var clipName) || string.IsNullOrWhiteSpace(clipName))
                    {
                        continue;
                    }

                    clipName = clipName.Trim();
                    if (!lengthsByClipName.TryGetValue(clipName, out var fresh))
                    {
                        result.animationMissingClips.Add(
                            $"monster_animation_sets.csv:{monsterId} {clipColumn}='{clipName}' not found in any FBX");
                        continue;
                    }

                    row.TryGetValue(lengthColumn, out var raw);
                    var stored = ParseFloat(raw);
                    if (Mathf.Abs(fresh - stored) > ToleranceSeconds)
                    {
                        result.animationStaleMeasurements.Add(
                            $"monster_animation_sets.csv:{monsterId} {lengthColumn} stored {Fmt(stored)}s "
                            + $"but '{clipName}' measures {Fmt(fresh)}s");
                    }
                }
            }

            if (!File.Exists(attackClipsCsv))
            {
                return;
            }

            foreach (var row in ReadCsvRows(attackClipsCsv, out _))
            {
                row.TryGetValue("monsterId", out var monsterId);
                row.TryGetValue("animationTrigger", out var trigger);
                if (!row.TryGetValue("clipName", out var clipName) || string.IsNullOrWhiteSpace(clipName))
                {
                    continue;
                }

                clipName = clipName.Trim();
                if (!lengthsByClipName.TryGetValue(clipName, out var fresh))
                {
                    result.animationMissingClips.Add(
                        $"monster_animation_attack_clips.csv:{monsterId}/{trigger} clipName='{clipName}' not found in any FBX");
                    continue;
                }

                row.TryGetValue("measuredLengthSeconds", out var raw);
                var stored = ParseFloat(raw);
                if (Mathf.Abs(fresh - stored) > ToleranceSeconds)
                {
                    result.animationStaleMeasurements.Add(
                        $"monster_animation_attack_clips.csv:{monsterId}/{trigger} stored {Fmt(stored)}s "
                        + $"but '{clipName}' measures {Fmt(fresh)}s");
                }
            }
        }

        // ------------------------------------------------------------------ CSV

        private static List<Dictionary<string, string>> ReadCsvRows(string path, out bool hasMeasuredColumn)
        {
            var rows = new List<Dictionary<string, string>>();
            hasMeasuredColumn = false;

            var lines = File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line)).ToArray();
            if (lines.Length == 0)
            {
                return rows;
            }

            var headers = SplitCsvLine(lines[0]).Select(h => h.Trim().TrimStart('﻿')).ToList();
            hasMeasuredColumn = headers.Any(h => string.Equals(h, "measuredLengthSeconds", StringComparison.Ordinal));

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

        private static float ParseFloat(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return 0f;
            }

            return float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0f;
        }

        private static string Fmt(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
