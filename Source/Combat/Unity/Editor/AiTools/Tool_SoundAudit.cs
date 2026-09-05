#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_SoundAudit
    {
        // How a catalog cue was found in source. Constant/MonsterFamily are exact (resolved off
        // AudioCueIds); Literal is a bare string in code that happens to equal a cue id.
        private const string RaiseKindNone = "None";
        private const string RaiseKindConstant = "Constant";
        private const string RaiseKindMonsterFamily = "MonsterFamily";
        private const string RaiseKindLiteral = "Literal";
        // Data-driven: the cue is named by a shipping CSV row (monster_attack_patterns.csv soundImpactCueId),
        // which CombatAudioPresenter resolves at runtime off EffectResultEvent.SourcePatternId. No code literal
        // ever spells these ids (S005~), so without this kind the whole pattern-sound axis reads as "never raised".
        private const string RaiseKindPatternData = "PatternData";

        private const int MaxSitesPerCue = 12;

        // `AudioCueIds.Something` — the exact, unambiguous half of the raise scan.
        private static readonly Regex ConstantReference =
            new Regex(@"AudioCueIds\s*\.\s*([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);

        // A bare dotted lowercase string literal. Only counted when it equals a catalog cue id, because
        // the same shape is also used for effect source refs (`field.damage`, `monster.pattern.A001`).
        private static readonly Regex CueLikeLiteral =
            new Regex("\"([a-z][a-z0-9_]*(?:\\.[a-z0-9_]+)+)\"", RegexOptions.Compiled);

        // A monster voice cue id, so a family raise can be attributed to the concrete catalog entries.
        private static readonly Regex MonsterCueId =
            new Regex(@"^monster\.(M[0-9]{3})\.(attack|hit|death)$", RegexOptions.Compiled);

        private static readonly Regex SerializedGuid =
            new Regex(@"guid:\s*([0-9a-f]{32})", RegexOptions.Compiled);

        [AiTool
        (
            "sound-audit",
            Title = "Sound / Audit",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("SoundCatalog의 큐 ↔ 발화지점 ↔ 클립 3자 대조 감사. 카탈로그에 있는데 코드 어디서도 " +
            "발화되지 않는 큐, 발화되는데 카탈로그에 없어 조용히 MissingCue가 되는 큐, placeholder 클립인 채 " +
            "발화되는 큐, 어디서도 참조되지 않는 오디오 에셋(GUID·CSV 경로 전수 대조), 오디오 폴더에 섞인 " +
            "비오디오 파일을 보고한다. 런타임은 미등록 큐와 빈 클립을 설계상 조용히 넘기므로 이 대조가 없으면 " +
            "둘 다 에러로 드러나지 않는다. 발화지점 수집은 AudioCueIds 상수 참조 + monster.* 헬퍼 패밀리 + " +
            "카탈로그 큐 id와 일치하는 문자열 리터럴 세 갈래이며, '발화 문맥' 판정은 줄 창(window) 키워드 " +
            "휴리스틱이라 raiseSites 증거를 함께 노출한다. 읽기 전용 — 게임/씬 상태를 바꾸지 않는다.")]
        public SoundAuditResult Audit
        (
            [Description("SoundCatalog 에셋 경로. 기본은 출하 카탈로그.")]
            string catalogPath = "Assets/Data/Combat/Presentation/Catalogs/SoundCatalog.asset",
            [Description("발화지점을 훑을 소스 루트(쉼표 구분).")]
            string scriptRoots = "Assets/Scripts",
            [Description("오디오 에셋 루트(쉼표 구분). 미참조 에셋 판정 대상.")]
            string audioRoots = "Assets/Sounds,Assets/Data/Audio",
            [Description("placeholder 클립으로 간주할 경로 조각.")]
            string placeholderPathFragment = "Assets/Data/Audio/Clips/Placeholders",
            [Description("발화지점 스캔에서 제외할 경로 조각(쉼표 구분). 큐 id 선언부와 런타임 폴백 카탈로그 " +
                "빌더는 모든 큐를 발화 없이 언급하므로 기본으로 제외한다.")]
            string excludePathFragments = "Assets/Scripts/Audio/AudioCueIds.cs,Assets/Scripts/Audio/SoundCatalog.cs",
            [Description("해당 줄 창에 나타나면 '발화 문맥'으로 보는 키워드(쉼표 구분).")]
            string emissionKeywords = "RequestCue,RequestAudioCue,RequestCueWithVoiceDelay,PlayCastCue,PlayCue,cues,AudioCueRequested,TryResolveLobbySound,PlayLobby,return,SerializeField,cueId"
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(catalogPath);
                if (catalog == null)
                {
                    throw new InvalidOperationException($"No SoundCatalog asset at '{catalogPath}'.");
                }

                var result = new SoundAuditResult { catalogPath = catalogPath };
                var scriptRootList = SplitList(scriptRoots);
                var audioRootList = SplitList(audioRoots);
                var excludeList = SplitList(excludePathFragments);
                var emissionKeywordList = SplitList(emissionKeywords);
                result.scannedScriptRoots.AddRange(scriptRootList);
                result.scannedAudioRoots.AddRange(audioRootList);
                result.excludedFromRaiseScan.AddRange(excludeList);

                var catalogCueIds = new HashSet<string>(
                    catalog.Entries.Where(entry => entry != null).Select(entry => entry.CueId),
                    StringComparer.Ordinal);

                var scan = ScanRaiseSites(
                    scriptRootList, excludeList, emissionKeywordList, catalogCueIds, result);
                AppendPatternDataRaiseSites(scan, result);

                AppendCatalogFindings(result, catalog, scan, placeholderPathFragment);
                AppendMissingFromCatalog(result, scan, catalogCueIds);
                AppendAssetFindings(result, catalog, audioRootList, placeholderPathFragment);

                return result;
            });
        }

        // --- Raise-site scan -------------------------------------------------------------------

        private sealed class RaiseScan
        {
            // cueId (or family key "monster.*.attack") -> sites
            public readonly Dictionary<string, List<RaiseSite>> ByCueId = new(StringComparer.Ordinal);
            // Cue ids resolved off AudioCueIds (constants + concrete monster ids seen in helper args).
            public readonly HashSet<string> CodeKnownCueIds = new(StringComparer.Ordinal);
            // Monster helper actions actually called ("attack"/"hit"/"death"), covering the whole family.
            public readonly HashSet<string> MonsterFamilyActions = new(StringComparer.Ordinal);
        }

        private readonly struct RaiseSite
        {
            public RaiseSite(string path, int line, string text, bool emission, string kind)
            {
                Path = path;
                Line = line;
                Text = text;
                Emission = emission;
                Kind = kind;
            }

            public string Path { get; }
            public int Line { get; }
            public string Text { get; }
            public bool Emission { get; }
            public string Kind { get; }

            public string Describe() => $"{Path}:{Line}  {Text}{(Emission ? string.Empty : "   [reference-only]")}";
        }

        private static RaiseScan ScanRaiseSites(
            IReadOnlyList<string> scriptRoots,
            IReadOnlyList<string> excludeFragments,
            IReadOnlyList<string> emissionKeywords,
            HashSet<string> catalogCueIds,
            SoundAuditResult result)
        {
            var scan = new RaiseScan();
            var constants = ResolveCueConstants();
            var monsterHelpers = new HashSet<string>(
                new[] { "MonsterAttack", "MonsterHit", "MonsterDeath" }, StringComparer.Ordinal);

            foreach (var file in EnumerateScriptFiles(scriptRoots, excludeFragments))
            {
                result.scannedScriptFileCount++;
                var lines = File.ReadAllLines(file);
                var relative = ToProjectRelative(file);

                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (line.IndexOf("AudioCueIds", StringComparison.Ordinal) < 0 && line.IndexOf('"') < 0)
                    {
                        continue;
                    }

                    var emission = IsEmissionContext(lines, i, emissionKeywords);
                    var text = line.Trim();

                    foreach (Match match in ConstantReference.Matches(line))
                    {
                        var member = match.Groups[1].Value;
                        if (constants.TryGetValue(member, out var cueId))
                        {
                            scan.CodeKnownCueIds.Add(cueId);
                            Record(scan, cueId, new RaiseSite(relative, i + 1, text, emission, RaiseKindConstant));
                            continue;
                        }

                        if (!monsterHelpers.Contains(member))
                        {
                            continue;
                        }

                        // monster.* cues are built at runtime from a definition id. A literal argument
                        // pins the exact cue; otherwise the call covers the whole action family.
                        var action = member.Substring("Monster".Length).ToLowerInvariant();
                        scan.MonsterFamilyActions.Add(action);

                        var literalArg = Regex.Match(
                            line.Substring(match.Index), @"^Monster(?:Attack|Hit|Death)\s*\(\s*""([^""]+)""");
                        var familyKey = literalArg.Success
                            ? $"monster.{literalArg.Groups[1].Value}.{action}"
                            : $"monster.*.{action}";
                        if (literalArg.Success)
                        {
                            scan.CodeKnownCueIds.Add(familyKey);
                        }

                        Record(scan, familyKey, new RaiseSite(relative, i + 1, text, emission, RaiseKindMonsterFamily));
                    }

                    foreach (Match match in CueLikeLiteral.Matches(line))
                    {
                        var literal = match.Groups[1].Value;
                        if (catalogCueIds.Contains(literal))
                        {
                            Record(scan, literal, new RaiseSite(relative, i + 1, text, emission, RaiseKindLiteral));
                        }
                    }
                }
            }

            return scan;
        }

        private static void Record(RaiseScan scan, string key, RaiseSite site)
        {
            if (!scan.ByCueId.TryGetValue(key, out var sites))
            {
                sites = new List<RaiseSite>();
                scan.ByCueId[key] = sites;
            }

            sites.Add(site);
        }

        // The emission call can sit either side of the cue reference: above it when an argument list wraps,
        // below it when the cue is first assigned to a local and raised on the next statement.
        private static bool IsEmissionContext(string[] lines, int index, IReadOnlyList<string> keywords)
        {
            var from = Math.Max(0, index - 2);
            var to = Math.Min(lines.Length - 1, index + 2);
            for (var i = from; i <= to; i++)
            {
                foreach (var keyword in keywords)
                {
                    if (lines[i].IndexOf(keyword, StringComparison.Ordinal) >= 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // Public const string fields on AudioCueIds, by member name -> cue id value.
        private static Dictionary<string, string> ResolveCueConstants()
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in typeof(AudioCueIds).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string)
                    && field.GetRawConstantValue() is string value && !string.IsNullOrWhiteSpace(value))
                {
                    map[field.Name] = value;
                }
            }

            return map;
        }

        private static IEnumerable<string> EnumerateScriptFiles(
            IReadOnlyList<string> roots, IReadOnlyList<string> excludeFragments)
        {
            foreach (var root in roots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
                {
                    var normalized = file.Replace('\\', '/');
                    if (excludeFragments.Any(fragment =>
                            normalized.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        continue;
                    }

                    yield return file;
                }
            }
        }

        // --- Findings --------------------------------------------------------------------------

        private static void AppendCatalogFindings(
            SoundAuditResult result, SoundCatalog catalog, RaiseScan scan, string placeholderPathFragment)
        {
            var cueIdCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var entry in catalog.Entries)
            {
                if (entry == null)
                {
                    continue;
                }

                result.totalEntries++;
                cueIdCounts.TryGetValue(entry.CueId, out var count);
                cueIdCounts[entry.CueId] = count + 1;

                var clipPath = entry.Clip != null ? AssetDatabase.GetAssetPath(entry.Clip) : string.Empty;
                var isPlaceholder = !string.IsNullOrEmpty(clipPath)
                    && clipPath.IndexOf(placeholderPathFragment, StringComparison.OrdinalIgnoreCase) >= 0;

                var sites = ResolveSitesForCue(scan, entry.CueId);
                var row = new SoundCueAuditRow
                {
                    cueId = entry.CueId,
                    displayName = entry.DisplayName,
                    bus = entry.Bus.ToString(),
                    deprecated = entry.Deprecated,
                    clipPath = clipPath,
                    clipIsPlaceholder = isPlaceholder,
                    volume = entry.Volume,
                    pitchMin = entry.PitchMin,
                    pitchMax = entry.PitchMax,
                    cooldownSeconds = entry.CooldownSeconds,
                    raiseSiteCount = sites.Count,
                    emissionSiteCount = sites.Count(site => site.Emission),
                    raiseKind = sites.Count == 0 ? RaiseKindNone : PreferredKind(sites)
                };
                row.raiseSites.AddRange(sites.Take(MaxSitesPerCue).Select(site => site.Describe()));
                if (sites.Count > MaxSitesPerCue)
                {
                    row.raiseSites.Add($"... and {sites.Count - MaxSitesPerCue} more site(s)");
                }

                result.cues.Add(row);

                if (entry.Deprecated)
                {
                    result.deprecatedCount++;
                    continue;
                }

                if (entry.Clip == null)
                {
                    result.entriesWithoutClip.Add($"{entry.CueId} (bus={entry.Bus})");
                }

                if (row.raiseSiteCount == 0)
                {
                    result.cuesNeverRaised.Add(
                        $"{entry.CueId} (bus={entry.Bus}, clip={(string.IsNullOrEmpty(clipPath) ? "<none>" : Path.GetFileName(clipPath))})");
                }
                else if (row.emissionSiteCount == 0)
                {
                    result.cuesReferencedButNotEmitted.Add($"{entry.CueId} → {row.raiseSites.FirstOrDefault()}");
                }

                if (isPlaceholder && row.raiseSiteCount > 0)
                {
                    result.placeholderCuesInUse.Add($"{entry.CueId} → {Path.GetFileName(clipPath)}");
                }
            }

            foreach (var pair in cueIdCounts
                         .Where(pair => pair.Value > 1)
                         .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                result.duplicateCueIds.Add($"{pair.Key} (x{pair.Value})");
            }

            foreach (var group in result.cues
                         .Where(cue => !cue.deprecated && !string.IsNullOrEmpty(cue.clipPath))
                         .GroupBy(cue => cue.clipPath, StringComparer.Ordinal)
                         .Where(group => group.Count() > 1)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                result.clipsSharedByMultipleCues.Add(
                    $"{Path.GetFileName(group.Key)} ← {string.Join(", ", group.Select(cue => cue.cueId))}");
            }
        }

        // A monster cue is covered either by a literal-argument call or by any call to its action helper.
        // 2026-09-05: monster pattern impact cues (combat_sound_cues.csv S-ids) are raised through data —
        // monster_attack_patterns.csv → MonsterCatalogDefinition.TryGetPatternPresentation → presenter. Count each
        // referencing pattern row as an emission site so the audit tracks the axis instead of listing it as dead.
        private static void AppendPatternDataRaiseSites(RaiseScan scan, SoundAuditResult result)
        {
            try
            {
                var bundle = SeoulPlayup.Combat.Runtime.MonsterCatalogCsvConverter.ConvertDirectories(
                    SeoulPlayup.Combat.Runtime.CombatCsvPaths.MonsterDirectory,
                    SeoulPlayup.Combat.Runtime.CombatCsvPaths.PresentationDirectory);
                var csvPath = SeoulPlayup.Combat.Runtime.CombatCsvPaths.MonsterDirectory + "/monster_attack_patterns.csv";
                foreach (var presentation in bundle.PatternPresentations)
                {
                    var cueId = presentation.SoundImpactCueId;
                    if (string.IsNullOrWhiteSpace(cueId))
                    {
                        continue;
                    }

                    if (!scan.ByCueId.TryGetValue(cueId, out var sites))
                    {
                        sites = new List<RaiseSite>();
                        scan.ByCueId[cueId] = sites;
                    }

                    sites.Add(new RaiseSite(
                        csvPath, 0, $"{presentation.PatternId} soundImpactCueId={cueId}", emission: true, RaiseKindPatternData));
                    scan.CodeKnownCueIds.Add(cueId);
                }
            }
            catch (Exception ex)
            {
                result.scannedScriptRoots.Add($"[pattern-data scan failed: {ex.Message}]");
            }
        }

        private static List<RaiseSite> ResolveSitesForCue(RaiseScan scan, string cueId)
        {
            var sites = new List<RaiseSite>();
            if (scan.ByCueId.TryGetValue(cueId, out var direct))
            {
                sites.AddRange(direct);
            }

            var monster = MonsterCueId.Match(cueId);
            if (monster.Success
                && scan.ByCueId.TryGetValue($"monster.*.{monster.Groups[2].Value}", out var family))
            {
                sites.AddRange(family);
            }

            return sites;
        }

        private static string PreferredKind(IReadOnlyList<RaiseSite> sites)
        {
            if (sites.Any(site => site.Kind == RaiseKindConstant))
            {
                return RaiseKindConstant;
            }

            if (sites.Any(site => site.Kind == RaiseKindMonsterFamily))
            {
                return RaiseKindMonsterFamily;
            }

            return sites.Any(site => site.Kind == RaiseKindPatternData) ? RaiseKindPatternData : RaiseKindLiteral;
        }

        // The reverse direction: cue ids code can raise that the catalog does not answer. Restricted to
        // ids resolved off AudioCueIds — bare literals are excluded on purpose, since a dotted literal is
        // just as likely to be an effect source ref, and guessing there would manufacture false findings.
        private static void AppendMissingFromCatalog(
            SoundAuditResult result, RaiseScan scan, HashSet<string> catalogCueIds)
        {
            foreach (var cueId in scan.CodeKnownCueIds.OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!catalogCueIds.Contains(cueId))
                {
                    var sites = scan.ByCueId.TryGetValue(cueId, out var found) ? found : new List<RaiseSite>();
                    result.raisedCuesMissingFromCatalog.Add(
                        $"{cueId} → {sites.FirstOrDefault().Describe()}");
                }
            }

            // Family calls with no literal argument: flag an action whose catalog family is entirely absent.
            foreach (var action in scan.MonsterFamilyActions.OrderBy(a => a, StringComparer.Ordinal))
            {
                var covered = catalogCueIds.Any(id =>
                {
                    var match = MonsterCueId.Match(id);
                    return match.Success && match.Groups[2].Value == action;
                });

                if (!covered)
                {
                    result.raisedCuesMissingFromCatalog.Add(
                        $"monster.*.{action} (AudioCueIds.Monster{char.ToUpperInvariant(action[0])}{action.Substring(1)}) → no catalog entry for this action");
                }
            }
        }

        // --- Asset findings --------------------------------------------------------------------

        private static void AppendAssetFindings(
            SoundAuditResult result,
            SoundCatalog catalog,
            IReadOnlyList<string> audioRoots,
            string placeholderPathFragment)
        {
            var existingRoots = audioRoots.Where(AssetDatabase.IsValidFolder).ToArray();
            if (existingRoots.Length == 0)
            {
                return;
            }

            var catalogClipPaths = new HashSet<string>(
                catalog.Entries
                    .Where(entry => entry != null && entry.Clip != null)
                    .Select(entry => AssetDatabase.GetAssetPath(entry.Clip)),
                StringComparer.Ordinal);

            var referencedGuids = CollectSerializedGuidReferences();
            var csvText = ReadDataCsvText();

            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", existingRoots))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                result.scannedAudioAssetCount++;
                var isPlaceholder = path.IndexOf(placeholderPathFragment, StringComparison.OrdinalIgnoreCase) >= 0;
                var referenced = catalogClipPaths.Contains(path)
                    || referencedGuids.Contains(guid)
                    || csvText.IndexOf(path, StringComparison.OrdinalIgnoreCase) >= 0;

                if (referenced)
                {
                    continue;
                }

                if (isPlaceholder)
                {
                    result.unreferencedPlaceholderClips.Add(path);
                }
                else
                {
                    result.unreferencedAudioAssets.Add(path);
                }
            }

            AppendForeignFiles(result, audioRoots);
        }

        // Every guid mentioned by a serialized Unity file. A clip assigned straight onto a scene component
        // (the lobby BGM and button SFX are) is referenced without ever appearing in the catalog, so
        // without this pass it would be reported as an orphan.
        private static HashSet<string> CollectSerializedGuidReferences()
        {
            var guids = new HashSet<string>(StringComparer.Ordinal);
            var patterns = new[] { "*.unity", "*.prefab", "*.asset", "*.controller", "*.mat", "*.playable" };

            foreach (var pattern in patterns)
            {
                foreach (var file in Directory.EnumerateFiles("Assets", pattern, SearchOption.AllDirectories))
                {
                    foreach (var line in File.ReadLines(file))
                    {
                        if (line.IndexOf("guid:", StringComparison.Ordinal) < 0)
                        {
                            continue;
                        }

                        foreach (Match match in SerializedGuid.Matches(line))
                        {
                            guids.Add(match.Groups[1].Value);
                        }
                    }
                }
            }

            return guids;
        }

        // CSV pipelines reference clips by path, not guid (combat_sound_cues.csv, sound_manifest.csv).
        private static string ReadDataCsvText()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var file in Directory.EnumerateFiles("Assets", "*.csv", SearchOption.AllDirectories))
            {
                try
                {
                    builder.Append(File.ReadAllText(file)).Append('\n');
                }
                catch (IOException)
                {
                    // A CSV we cannot read simply contributes no references.
                }
            }

            return builder.ToString().Replace('\\', '/');
        }

        private static void AppendForeignFiles(SoundAuditResult result, IReadOnlyList<string> audioRoots)
        {
            var audioExtensions = new HashSet<string>(
                new[] { ".wav", ".mp3", ".ogg", ".aiff", ".aif", ".mod", ".it", ".s3m", ".xm" },
                StringComparer.OrdinalIgnoreCase);

            foreach (var root in audioRoots)
            {
                if (!Directory.Exists(root))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var extension = Path.GetExtension(file);
                    if (audioExtensions.Contains(extension)
                        || string.Equals(extension, ".meta", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    result.foreignFilesInAudioRoots.Add(ToProjectRelative(file));
                }
            }
        }

        // --- Helpers ---------------------------------------------------------------------------

        private static List<string> SplitList(string value)
        {
            return (value ?? string.Empty)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(item => item.Trim())
                .Where(item => item.Length > 0)
                .ToList();
        }

        private static string ToProjectRelative(string path)
        {
            var normalized = path.Replace('\\', '/');
            var index = normalized.IndexOf("Assets/", StringComparison.Ordinal);
            return index >= 0 ? normalized.Substring(index) : normalized;
        }
    }
}
