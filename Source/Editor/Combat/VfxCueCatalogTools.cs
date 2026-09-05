using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

public static class VfxCueCatalogTools
{
    private const string DefaultCatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";
    private const string DefaultSoundCatalogPath = "Assets/Data/Combat/Presentation/Catalogs/SoundCatalog.asset";

    [MenuItem("Tools/Seoul Playup/Combat/Rebuild VFX Catalog From Combat CSV")]
    public static void RebuildDefaultCatalogFromCombatCsvMenu()
    {
        var issues = RebuildDefaultCatalogFromCombatCsv();
        ShowIssueDialog("Rebuild VFX Catalog", issues);
    }

    [MenuItem("Tools/Seoul Playup/Combat/Validate VFX Catalog")]
    public static void ValidateDefaultCatalogMenu()
    {
        var issues = ValidateDefaultCatalog();
        ShowIssueDialog("Validate VFX Catalog", issues);
    }

    [MenuItem("Tools/Seoul Playup/Combat/Rebuild Sound Catalog From Combat CSV")]
    public static void RebuildSoundCatalogFromCombatCsvMenu()
    {
        var issues = RebuildSoundCatalogFromCombatCsv();
        ShowIssueDialog("Rebuild Sound Catalog", issues);
    }

    [MenuItem("Tools/Seoul Playup/Combat/Validate Sound Catalog")]
    public static void ValidateSoundCatalogMenu()
    {
        var issues = ValidateSoundCatalog();
        ShowIssueDialog("Validate Sound Catalog", issues);
    }

    public static void RebuildDefaultCatalogFromMonsterCsvMenu()
    {
        RebuildDefaultCatalogFromCombatCsvMenu();
    }

    public static void RebuildSoundCatalogFromMonsterCsvMenu()
    {
        RebuildSoundCatalogFromCombatCsvMenu();
    }

    public static IReadOnlyList<string> RebuildDefaultCatalogFromMonsterCsv()
    {
        return RebuildDefaultCatalogFromCombatCsv();
    }

    public static IReadOnlyList<string> RebuildDefaultCatalogFromCombatCsv()
    {
        var issues = new List<string>();
        var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
        var importedEntries = new List<EffectVfxCatalog.Entry>();

        var monsterCueById = bundle.VfxCues.ToDictionary(cue => cue.VfxCueId, StringComparer.Ordinal);
        foreach (var binding in bundle.PatternVfxBindings)
        {
            if (!monsterCueById.TryGetValue(binding.VfxCueId, out var cue))
            {
                issues.Add($"Error: pattern {binding.PatternId} references missing VFX cue: {binding.VfxCueId}");
                continue;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(cue.PrefabPath);
            if (prefab == null)
            {
                issues.Add($"Error: {cue.VfxCueId} prefab not found: {cue.PrefabPath}");
                continue;
            }

            importedEntries.Add(CreateEntry(cue, prefab, $"monster.pattern.{binding.PatternId}", binding.PatternId, binding.SpawnAnchor, binding.Order, binding.DelaySeconds));
        }

        foreach (var cue in CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(cue.PrefabPath);
            if (prefab == null)
            {
                issues.Add($"Error: {cue.CueId} prefab not found: {cue.PrefabPath}");
                continue;
            }

            importedEntries.Add(CreateEntry(cue, prefab));
        }

        if (issues.Any(issue => issue.StartsWith("Error:", StringComparison.Ordinal)))
        {
            return issues;
        }

        var catalog = LoadOrCreateDefaultCatalog();
        var merged = catalog.Entries
            .Where(existing => !importedEntries.Any(imported => SameCueOrCatalogKey(existing, imported)))
            .Concat(importedEntries)
            .ToArray();

        catalog.SetEntries(merged);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        issues.Add($"OK: imported {importedEntries.Count} VFX cues from combat CSV into {DefaultCatalogPath}.");
        return issues;
    }

    public static IReadOnlyList<string> ValidateDefaultCatalog()
    {
        var issues = new List<string>();
        var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
        if (catalog == null)
        {
            return new[] { $"Error: missing catalog asset: {DefaultCatalogPath}" };
        }

        var entries = catalog.Entries;
        var cueIds = new HashSet<string>(StringComparer.Ordinal);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var validCount = 0;

        foreach (var entry in entries)
        {
            if (entry == null)
            {
                issues.Add("Error: catalog contains a null entry.");
                continue;
            }

            validCount++;
            if (!string.IsNullOrWhiteSpace(entry.CueId) && !cueIds.Add(entry.CueId))
            {
                issues.Add($"Warning: duplicate cueId '{entry.CueId}' shares tuning across multiple sourceRef entries.");
            }

            var key = CatalogKey(entry);
            if (!keys.Add(key))
            {
                issues.Add($"Warning: duplicate VFX match key '{key}'. Earlier entries win at runtime.");
            }

            if (entry.Prefabs == null || entry.Prefabs.All(prefab => prefab == null))
            {
                issues.Add($"Error: entry '{EntryLabel(entry)}' has no prefab.");
            }
            else if (entry.Prefabs.Any(prefab => prefab == null))
            {
                issues.Add($"Warning: entry '{EntryLabel(entry)}' has one or more null prefab slots.");
            }
        }

        ValidateMonsterCsvCueReferences(issues);
        ValidateCardCsvCueReferences(issues);

        if (issues.Count == 0)
        {
            issues.Add($"OK: {validCount} catalog entries validated.");
        }

        return issues;
    }

    public static IReadOnlyList<string> RebuildSoundCatalogFromMonsterCsv()
    {
        return RebuildSoundCatalogFromCombatCsv();
    }

    public static IReadOnlyList<string> RebuildSoundCatalogFromCombatCsv()
    {
        var issues = new List<string>();
        var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
        var importedEntries = new List<SoundCatalog.Entry>();

        foreach (var cue in bundle.SoundCues)
        {
            var clip = string.IsNullOrWhiteSpace(cue.ClipPath)
                ? null
                : AssetDatabase.LoadAssetAtPath<AudioClip>(cue.ClipPath);
            if (!string.IsNullOrWhiteSpace(cue.ClipPath) && clip == null)
            {
                issues.Add($"Error: {cue.SoundCueId} clip not found: {cue.ClipPath}");
                continue;
            }

            importedEntries.Add(CreateSoundEntry(cue, clip));
        }

        if (issues.Any(issue => issue.StartsWith("Error:", StringComparison.Ordinal)))
        {
            return issues;
        }

        var catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(DefaultSoundCatalogPath);
        if (catalog == null)
        {
            return new[] { $"Error: missing sound catalog asset: {DefaultSoundCatalogPath}" };
        }

        var merged = catalog.Entries
            .Where(existing => importedEntries.All(imported => !string.Equals(existing.CueId, imported.CueId, StringComparison.Ordinal)))
            .Concat(importedEntries)
            .ToArray();

        catalog.SetEntries(merged);
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        issues.Add($"OK: imported {importedEntries.Count} sound cues from {CombatCsvPaths.CombatSoundCuesCsv} into {DefaultSoundCatalogPath}.");
        return issues;
    }

    public static IReadOnlyList<string> ValidateSoundCatalog()
    {
        var issues = new List<string>();
        var catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(DefaultSoundCatalogPath);
        if (catalog == null)
        {
            return new[] { $"Error: missing sound catalog asset: {DefaultSoundCatalogPath}" };
        }

        var cueIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in catalog.Entries)
        {
            if (entry == null)
            {
                issues.Add("Error: sound catalog contains a null entry.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.CueId))
            {
                issues.Add("Error: sound catalog contains an entry without cueId.");
                continue;
            }

            if (!cueIds.Add(entry.CueId))
            {
                issues.Add($"Error: duplicate sound cueId '{entry.CueId}'.");
            }
        }

        ValidateMonsterCsvSoundReferences(issues);

        if (issues.Count == 0)
        {
            issues.Add($"OK: {cueIds.Count} sound catalog entries validated.");
        }

        return issues;
    }

    private static void ValidateMonsterCsvCueReferences(List<string> issues)
    {
        try
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
            var cueIds = new HashSet<string>(bundle.VfxCues.Select(cue => cue.VfxCueId), StringComparer.Ordinal);
            foreach (var binding in bundle.PatternVfxBindings)
            {
                if (!cueIds.Contains(binding.VfxCueId))
                {
                    issues.Add($"Error: pattern '{binding.PatternId}' references missing vfxCueId '{binding.VfxCueId}'.");
                }
            }

            foreach (var cue in bundle.VfxCues)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(cue.PrefabPath) == null)
                {
                    issues.Add($"Error: {cue.VfxCueId} prefab not found: {cue.PrefabPath}");
                }
            }
        }
        catch (Exception ex)
        {
            issues.Add($"Error: monster VFX CSV validation failed: {ex.Message}");
        }
    }

    private static void ValidateMonsterCsvSoundReferences(List<string> issues)
    {
        try
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
            var cueIds = new HashSet<string>(bundle.SoundCues.Select(cue => cue.SoundCueId), StringComparer.Ordinal);
            foreach (var presentation in bundle.PatternPresentations)
            {
                ValidateOptionalSoundRef(issues, presentation.PatternId, "soundWindupCueId", presentation.SoundWindupCueId, cueIds);
                ValidateOptionalSoundRef(issues, presentation.PatternId, "soundCastCueId", presentation.SoundCastCueId, cueIds);
                ValidateOptionalSoundRef(issues, presentation.PatternId, "soundImpactCueId", presentation.SoundImpactCueId, cueIds);
            }

            foreach (var cue in bundle.SoundCues)
            {
                if (!string.IsNullOrWhiteSpace(cue.ClipPath) && AssetDatabase.LoadAssetAtPath<AudioClip>(cue.ClipPath) == null)
                {
                    issues.Add($"Error: {cue.SoundCueId} clip not found: {cue.ClipPath}");
                }
            }
        }
        catch (Exception ex)
        {
            issues.Add($"Error: monster sound CSV validation failed: {ex.Message}");
        }
    }

    private static void ValidateCardCsvCueReferences(List<string> issues)
    {
        try
        {
            var cues = CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv);
            foreach (var cue in cues)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(cue.PrefabPath) == null)
                {
                    issues.Add($"Error: {cue.CueId} prefab not found: {cue.PrefabPath}");
                }
            }
        }
        catch (Exception ex)
        {
            issues.Add($"Error: card VFX CSV validation failed: {ex.Message}");
        }
    }

    private static void ValidateOptionalSoundRef(
        List<string> issues,
        string patternId,
        string columnName,
        string cueId,
        ISet<string> cueIds)
    {
        if (!string.IsNullOrWhiteSpace(cueId) && !cueIds.Contains(cueId))
        {
            issues.Add($"Error: pattern '{patternId}' references missing {columnName} '{cueId}'.");
        }
    }

    private static EffectVfxCatalog.Entry CreateEntry(CombatVfxCueDefinition cue, GameObject prefab, string sourceRefOverride = null, string patternId = null, string spawnAnchorOverride = null, int order = 0, float delaySeconds = 0f)
    {
        var displayName = string.IsNullOrWhiteSpace(patternId) ? cue.VfxCueId : $"{cue.VfxCueId} {patternId}";
        if (order > 0)
        {
            displayName += $" #{order}";
        }

        var spawnAnchor = string.IsNullOrWhiteSpace(spawnAnchorOverride) ? cue.SpawnAnchor : spawnAnchorOverride;
        return new EffectVfxCatalog.Entry(
            cue.EffectKind,
            new[] { prefab },
            ParseTargetFilter(cue.TargetFilter),
            string.IsNullOrWhiteSpace(sourceRefOverride) ? cue.SourceRef : sourceRefOverride,
            cue.MatchSourceRefPrefix,
            cue.ScaleMultiplier,
            cue.ScaleWithRadius,
            new Vector3(cue.OffsetX, cue.OffsetY, cue.OffsetZ),
            new Vector3(cue.RotationX, cue.RotationY, cue.RotationZ),
            cue.LifetimeOverride,
            cue.VfxCueId,
            displayName,
            "Monster Attack",
            BuildTags(cue),
            cue.DesignerNote,
            deprecated: false,
            spawnAnchor: ParseSpawnAnchor(spawnAnchor),
            playbackDelaySeconds: delaySeconds,
            playbackSpeed: cue.PlaybackSpeed,
            axisScaleMultiplier: new Vector3(cue.ScaleX, cue.ScaleY, cue.ScaleZ),
            // Carried through so a rebuild does not wipe baked lengths back to 0 for CSV-backed entries.
            measuredLengthSeconds: cue.MeasuredLengthSeconds,
            authoredLengthSeconds: cue.AuthoredLengthSeconds,
            areaSpawnMode: ParseAreaSpawnMode(cue.AreaSpawnMode),
            perTileDelaySeconds: cue.PerTileDelaySeconds,
            followSourceAnchor: cue.FollowSourceAnchor);
    }

    private static EffectVfxCatalog.Entry CreateEntry(CombatCardVfxCueDefinition cue, GameObject prefab)
    {
        return new EffectVfxCatalog.Entry(
            cue.EffectKind,
            new[] { prefab },
            ParseTargetFilter(cue.TargetFilter),
            cue.SourceRef,
            cue.MatchSourceRefPrefix,
            cue.ScaleMultiplier,
            cue.ScaleWithRadius,
            new Vector3(cue.OffsetX, cue.OffsetY, cue.OffsetZ),
            new Vector3(cue.RotationX, cue.RotationY, cue.RotationZ),
            cue.LifetimeOverride,
            cue.CueId,
            string.IsNullOrWhiteSpace(cue.CardId) ? cue.CueId : $"{cue.CardId} {cue.EffectKind}",
            "Player Card",
            BuildTags(cue),
            cue.DesignerNote,
            deprecated: false,
            spawnAnchor: ParseSpawnAnchor(cue.SpawnAnchor),
            floatingTextMode: cue.FloatingTextMode,
            floatingTextOverride: cue.FloatingTextOverride,
            playbackDelaySeconds: cue.DelaySeconds,
            sourceCardId: cue.SourceCardId,
            playbackSpeed: cue.PlaybackSpeed,
            axisScaleMultiplier: new Vector3(cue.ScaleX, cue.ScaleY, cue.ScaleZ),
            // Carried through so a rebuild does not wipe baked lengths back to 0 for CSV-backed entries.
            measuredLengthSeconds: cue.MeasuredLengthSeconds,
            authoredLengthSeconds: cue.AuthoredLengthSeconds);
    }

    private static SoundCatalog.Entry CreateSoundEntry(CombatSoundCueDefinition cue, AudioClip clip)
    {
        return new SoundCatalog.Entry(
            cue.SoundCueId,
            clip,
            ParseSoundBus(cue.Bus),
            cue.Volume,
            cue.PitchMin,
            cue.PitchMax,
            cue.CooldownSeconds,
            ParseMissingClipBehavior(cue.MissingClipBehavior),
            cue.SoundCueId,
            "Monster Attack",
            BuildSoundTags(cue),
            cue.DesignerNote,
            deprecated: false,
            // Carried through so a rebuild does not wipe baked lengths back to 0.
            measuredLengthSeconds: cue.MeasuredLengthSeconds,
            authoredLengthSeconds: cue.AuthoredLengthSeconds);
    }

    private static string[] BuildTags(CombatVfxCueDefinition cue)
    {
        return new[]
            {
                "monster",
                "attack",
                cue.EffectKind.ToString(),
                cue.TargetFilter,
                cue.SourceRef
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] BuildTags(CombatCardVfxCueDefinition cue)
    {
        return new[]
            {
                "player",
                "card",
                cue.CardId,
                cue.EffectRef,
                cue.EffectKind.ToString(),
                cue.TargetFilter,
                cue.SourceRef
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] BuildSoundTags(CombatSoundCueDefinition cue)
    {
        return new[]
            {
                "monster",
                "attack",
                cue.Bus,
                string.IsNullOrWhiteSpace(cue.ClipPath) ? "missing-clip" : "clip"
            }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static EffectVfxTargetFilter ParseTargetFilter(string value)
    {
        return Enum.TryParse(value, ignoreCase: true, out EffectVfxTargetFilter parsed)
            ? parsed
            : EffectVfxTargetFilter.Any;
    }

    private static EffectVfxSpawnAnchor ParseSpawnAnchor(string value)
    {
        return Enum.TryParse(value, ignoreCase: true, out EffectVfxSpawnAnchor parsed)
            ? parsed
            : EffectVfxSpawnAnchor.Auto;
    }

    private static EffectVfxAreaSpawnMode ParseAreaSpawnMode(string value)
    {
        return Enum.TryParse(value, ignoreCase: true, out EffectVfxAreaSpawnMode parsed)
            ? parsed
            : EffectVfxAreaSpawnMode.None;
    }

    private static SoundBus ParseSoundBus(string value)
    {
        return Enum.TryParse(value, ignoreCase: true, out SoundBus parsed)
            ? parsed
            : SoundBus.Sfx;
    }

    private static MissingClipBehavior ParseMissingClipBehavior(string value)
    {
        return Enum.TryParse(value, ignoreCase: true, out MissingClipBehavior parsed)
            ? parsed
            : MissingClipBehavior.Silent;
    }

    private static bool SameCueOrCatalogKey(EffectVfxCatalog.Entry left, EffectVfxCatalog.Entry right)
    {
        if (!string.IsNullOrWhiteSpace(left.CueId) &&
            !string.IsNullOrWhiteSpace(right.CueId) &&
            string.Equals(left.CueId, right.CueId, StringComparison.Ordinal))
        {
            return true;
        }

        return CatalogKey(left) == CatalogKey(right);
    }

    private static string CatalogKey(EffectVfxCatalog.Entry entry)
    {
        // SourceCardId is part of the identity: a card-scoped override shares its kind/target/sourceRef with
        // the shared entry it narrows, so leaving it out would make the two look like the same entry and the
        // import would silently drop one of them.
        // Loop and matchStatusKind entries resolve on their own tiers keyed by StatusKind, so that axis is
        // part of identity too — without it all status cues collapsed to one key and Validate reported 16
        // phantom duplicates for entries that never compete at runtime.
        var statusAxis = entry.Loop
            ? $"|loop:{entry.StatusKind}"
            : entry.MatchStatusKind
                ? $"|status:{entry.StatusKind}"
                : string.Empty;
        return $"{entry.Kind}|{entry.TargetFilter}|{entry.SourceRef}|{entry.MatchSourceRefPrefix}|{entry.SourceCardId}{statusAxis}";
    }

    private static string EntryLabel(EffectVfxCatalog.Entry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.CueId))
        {
            return entry.CueId;
        }

        if (!string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            return entry.DisplayName;
        }

        return CatalogKey(entry);
    }

    private static EffectVfxCatalog LoadOrCreateDefaultCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
        if (catalog != null)
        {
            return catalog;
        }

        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Combat");
        catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
        AssetDatabase.CreateAsset(catalog, DefaultCatalogPath);
        AssetDatabase.SaveAssets();
        return catalog;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        var parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(parent) || string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    // Console-only on purpose: a modal here blocks the editor main thread every rebuild/validate
    // (freezing MCP/automation runs until someone clicks OK), and every issue is already mirrored
    // to the Console with proper severity right above.
    private static void ShowIssueDialog(string title, IReadOnlyList<string> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.StartsWith("Error:", StringComparison.Ordinal))
            {
                Debug.LogError($"[{title}] {issue}");
            }
            else if (issue.StartsWith("Warning:", StringComparison.Ordinal))
            {
                Debug.LogWarning($"[{title}] {issue}");
            }
            else
            {
                Debug.Log($"[{title}] {issue}");
            }
        }
    }
}

public sealed class VfxCatalogBrowserWindow : EditorWindow
{
    private const string DefaultCatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";

    private EffectVfxCatalog catalog;
    private SerializedObject serializedCatalog;
    private SerializedProperty entriesProperty;
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private string search = string.Empty;
    private int selectedIndex;
    private int previewRadius = 1;
    private string bulkCategory = string.Empty;
    private string bulkTag = string.Empty;
    private GameObject previewRoot;

    [MenuItem("Tools/Seoul Playup/Combat/VFX Catalog Browser")]
    public static void Open()
    {
        GetWindow<VfxCatalogBrowserWindow>("VFX Catalog");
    }

    private void OnEnable()
    {
        LoadDefaultCatalog();
    }

    private void OnGUI()
    {
        DrawToolbar();
        if (catalog == null || serializedCatalog == null || entriesProperty == null)
        {
            EditorGUILayout.HelpBox($"No VFX catalog found at {DefaultCatalogPath}.", MessageType.Warning);
            return;
        }

        serializedCatalog.Update();
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawEntryList();
            DrawEntryDetail();
        }

        serializedCatalog.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            catalog = (EffectVfxCatalog)EditorGUILayout.ObjectField(catalog, typeof(EffectVfxCatalog), false, GUILayout.Width(260f));
            if (GUILayout.Button("Load Default", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                LoadDefaultCatalog();
            }

            if (GUILayout.Button("Rebuild From CSV", EditorStyles.toolbarButton, GUILayout.Width(120f)))
            {
                VfxCueCatalogTools.RebuildDefaultCatalogFromMonsterCsvMenu();
                LoadDefaultCatalog();
            }

            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                VfxCueCatalogTools.ValidateDefaultCatalogMenu();
            }

            GUILayout.Label("Search", GUILayout.Width(46f));
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField);
        }

        if (serializedCatalog == null || serializedCatalog.targetObject != catalog)
        {
            BindCatalog();
        }
    }

    private void DrawEntryList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(380f)))
        {
            EditorGUILayout.LabelField($"Entries ({entriesProperty.arraySize})", EditorStyles.boldLabel);
            DrawBulkTools();
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (var i = 0; i < entriesProperty.arraySize; i++)
            {
                var entry = entriesProperty.GetArrayElementAtIndex(i);
                if (!MatchesSearch(entry))
                {
                    continue;
                }

                var label = BuildEntryLabel(entry, i);
                var previousColor = GUI.backgroundColor;
                if (i == selectedIndex)
                {
                    GUI.backgroundColor = new Color(0.55f, 0.8f, 1f, 1f);
                }

                if (GUILayout.Button(label, EditorStyles.miniButton))
                {
                    selectedIndex = i;
                    GUI.FocusControl(null);
                }

                GUI.backgroundColor = previousColor;
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawEntryDetail()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            EditorGUILayout.LabelField("Selected Cue", EditorStyles.boldLabel);
            if (entriesProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Catalog has no entries.", MessageType.Info);
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, entriesProperty.arraySize - 1);
            var entry = entriesProperty.GetArrayElementAtIndex(selectedIndex);
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            DrawPatternPresentationHelp(entry);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("cueId"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("displayName"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("category"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("tags"), includeChildren: true);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("designerNote"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("deprecated"));
            EditorGUILayout.Space(6f);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("kind"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("targetFilter"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("spawnAnchor"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("sourceRef"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("matchSourceRefPrefix"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("prefabs"), includeChildren: true);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("scaleMultiplier"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("scaleWithRadius"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("positionOffset"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("rotationEulerOffset"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("lifetimeOverride"));
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Persistent Status Loop", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("loop"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("attachToActor"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("statusKind"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("loopAnchor"));
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Spawn Preview"))
                {
                    SpawnPreview(entry);
                }

                if (GUILayout.Button("Clear Preview"))
                {
                    ClearPreview();
                }

                if (GUILayout.Button("Ping First Prefab"))
                {
                    PingFirstPrefab(entry);
                }

                if (GUILayout.Button("Select Catalog Asset"))
                {
                    Selection.activeObject = catalog;
                    EditorGUIUtility.PingObject(catalog);
                }
            }

            previewRadius = EditorGUILayout.IntSlider("Preview Radius", previewRadius, 0, 4);
        }
    }

    private void DrawBulkTools()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Bulk Edit Matching Search Results", EditorStyles.miniBoldLabel);
            bulkCategory = EditorGUILayout.TextField("Category", bulkCategory);
            bulkTag = EditorGUILayout.TextField("Add Tag", bulkTag);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Apply Category"))
                {
                    ApplyCategoryToMatches(bulkCategory);
                }

                if (GUILayout.Button("Add Tag"))
                {
                    AddTagToMatches(bulkTag);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Empty"))
                {
                    AddEmptyEntry();
                }

                if (GUILayout.Button("Duplicate Selected"))
                {
                    DuplicateSelectedEntry();
                }

                if (GUILayout.Button("Delete Selected"))
                {
                    DeleteSelectedEntry();
                }
            }
        }
    }

    private void DrawPatternPresentationHelp(SerializedProperty entry)
    {
        var sourceRef = ReadString(entry, "sourceRef");
        if (string.IsNullOrWhiteSpace(sourceRef) || !sourceRef.StartsWith("monster.pattern.", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var patternId = sourceRef.Substring("monster.pattern.".Length);
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
            if (!bundle.TryGetPatternPresentation(patternId, out var presentation))
            {
                EditorGUILayout.HelpBox($"No monster pattern presentation row found for {patternId}.", MessageType.Warning);
                return;
            }

            EditorGUILayout.HelpBox(
                $"Pattern {patternId}\nVFX: {presentation.VfxCueId}\nSFX: windup={presentation.SoundWindupCueId}, cast={presentation.SoundCastCueId}, impact={presentation.SoundImpactCueId}\nAnimation: {presentation.AnimationTrigger}",
                MessageType.Info);
        }
        catch (Exception ex)
        {
            EditorGUILayout.HelpBox($"Could not read monster CSV presentation binding: {ex.Message}", MessageType.Warning);
        }
    }

    private void LoadDefaultCatalog()
    {
        catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultCatalogPath);
        BindCatalog();
    }

    private void BindCatalog()
    {
        if (catalog == null)
        {
            serializedCatalog = null;
            entriesProperty = null;
            return;
        }

        serializedCatalog = new SerializedObject(catalog);
        entriesProperty = serializedCatalog.FindProperty("entries");
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, entriesProperty.arraySize - 1));
    }

    private void ApplyCategoryToMatches(string category)
    {
        serializedCatalog.Update();
        foreach (var entry in MatchingEntries())
        {
            entry.FindPropertyRelative("category").stringValue = category ?? string.Empty;
        }

        serializedCatalog.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);
    }

    private void AddTagToMatches(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        serializedCatalog.Update();
        foreach (var entry in MatchingEntries())
        {
            AddTag(entry.FindPropertyRelative("tags"), tag.Trim());
        }

        serializedCatalog.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);
    }

    private IEnumerable<SerializedProperty> MatchingEntries()
    {
        for (var i = 0; i < entriesProperty.arraySize; i++)
        {
            var entry = entriesProperty.GetArrayElementAtIndex(i);
            if (MatchesSearch(entry))
            {
                yield return entry;
            }
        }
    }

    private void AddEmptyEntry()
    {
        serializedCatalog.Update();
        var index = entriesProperty.arraySize;
        entriesProperty.InsertArrayElementAtIndex(index);
        var entry = entriesProperty.GetArrayElementAtIndex(index);
        ResetEntry(entry);
        selectedIndex = index;
        serializedCatalog.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);
    }

    private void DuplicateSelectedEntry()
    {
        if (entriesProperty.arraySize == 0)
        {
            return;
        }

        serializedCatalog.Update();
        selectedIndex = Mathf.Clamp(selectedIndex, 0, entriesProperty.arraySize - 1);
        entriesProperty.InsertArrayElementAtIndex(selectedIndex);
        selectedIndex++;
        serializedCatalog.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);
    }

    private void DeleteSelectedEntry()
    {
        if (entriesProperty.arraySize == 0)
        {
            return;
        }

        serializedCatalog.Update();
        selectedIndex = Mathf.Clamp(selectedIndex, 0, entriesProperty.arraySize - 1);
        entriesProperty.DeleteArrayElementAtIndex(selectedIndex);
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, entriesProperty.arraySize - 1));
        serializedCatalog.ApplyModifiedProperties();
        EditorUtility.SetDirty(catalog);
    }

    private static void ResetEntry(SerializedProperty entry)
    {
        entry.FindPropertyRelative("cueId").stringValue = string.Empty;
        entry.FindPropertyRelative("displayName").stringValue = string.Empty;
        entry.FindPropertyRelative("category").stringValue = string.Empty;
        entry.FindPropertyRelative("tags").arraySize = 0;
        entry.FindPropertyRelative("designerNote").stringValue = string.Empty;
        entry.FindPropertyRelative("deprecated").boolValue = false;
        entry.FindPropertyRelative("kind").enumValueIndex = 0;
        entry.FindPropertyRelative("targetFilter").enumValueIndex = 0;
        entry.FindPropertyRelative("spawnAnchor").enumValueIndex = 0;
        entry.FindPropertyRelative("sourceRef").stringValue = string.Empty;
        entry.FindPropertyRelative("matchSourceRefPrefix").boolValue = false;
        entry.FindPropertyRelative("prefabs").arraySize = 0;
        entry.FindPropertyRelative("scaleMultiplier").floatValue = 1f;
        entry.FindPropertyRelative("scaleWithRadius").boolValue = true;
        entry.FindPropertyRelative("positionOffset").vector3Value = Vector3.zero;
        entry.FindPropertyRelative("rotationEulerOffset").vector3Value = Vector3.zero;
        entry.FindPropertyRelative("lifetimeOverride").floatValue = 0f;
        entry.FindPropertyRelative("loop").boolValue = false;
        entry.FindPropertyRelative("attachToActor").boolValue = true;
        entry.FindPropertyRelative("statusKind").enumValueIndex = 0;
        entry.FindPropertyRelative("loopAnchor").enumValueIndex = (int)CharacterVfxAnchorKind.Ground;
    }

    private static void AddTag(SerializedProperty tags, string tag)
    {
        for (var i = 0; i < tags.arraySize; i++)
        {
            if (string.Equals(tags.GetArrayElementAtIndex(i).stringValue, tag, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
    }

    private void SpawnPreview(SerializedProperty entry)
    {
        ClearPreview();
        previewRoot = new GameObject("VFX Catalog Preview Root");
        var controller = previewRoot.AddComponent<EffectPresentationController>();
        controller.SetVfxCatalog(catalog);
        var kind = (EffectKind)entry.FindPropertyRelative("kind").enumValueIndex;
        var sourceRef = ReadString(entry, "sourceRef");
        var targetFilter = ReadEnumName(entry, "targetFilter");
        var targetUnitId = string.Equals(targetFilter, "Player", StringComparison.OrdinalIgnoreCase)
            ? "player"
            : string.Equals(targetFilter, "Field", StringComparison.OrdinalIgnoreCase)
                ? "field"
                : "monster-preview";
        var resultEvent = new EffectResultEvent(kind, radius: previewRadius, appliedAmount: 1, targetUnitId: targetUnitId, sourceRef: sourceRef);
        controller.Play(resultEvent, Vector3.zero, Quaternion.identity);
        Selection.activeObject = previewRoot;
    }

    private void ClearPreview()
    {
        if (previewRoot != null)
        {
            DestroyImmediate(previewRoot);
            previewRoot = null;
        }
    }

    private bool MatchesSearch(SerializedProperty entry)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return BuildSearchText(entry).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildSearchText(SerializedProperty entry)
    {
        return string.Join(" ",
            ReadString(entry, "cueId"),
            ReadString(entry, "displayName"),
            ReadString(entry, "category"),
            ReadString(entry, "designerNote"),
            ReadString(entry, "sourceRef"),
            ReadEnumName(entry, "kind"),
            ReadEnumName(entry, "targetFilter"));
    }

    private static string BuildEntryLabel(SerializedProperty entry, int index)
    {
        var cueId = ReadString(entry, "cueId");
        var displayName = ReadString(entry, "displayName");
        var kind = ReadEnumName(entry, "kind");
        var target = ReadEnumName(entry, "targetFilter");
        var sourceRef = ReadString(entry, "sourceRef");
        var label = string.IsNullOrWhiteSpace(cueId) ? $"{index + 1:00}" : cueId;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            label += $" - {displayName}";
        }

        label += $" [{kind}/{target}]";
        if (!string.IsNullOrWhiteSpace(sourceRef))
        {
            label += $" <{sourceRef}>";
        }

        return label;
    }

    private static void PingFirstPrefab(SerializedProperty entry)
    {
        var prefabs = entry.FindPropertyRelative("prefabs");
        if (prefabs == null)
        {
            return;
        }

        for (var i = 0; i < prefabs.arraySize; i++)
        {
            var prefab = prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
            if (prefab != null)
            {
                Selection.activeObject = prefab;
                EditorGUIUtility.PingObject(prefab);
                return;
            }
        }
    }

    private static string ReadString(SerializedProperty parent, string propertyName)
    {
        return parent.FindPropertyRelative(propertyName)?.stringValue ?? string.Empty;
    }

    private static string ReadEnumName(SerializedProperty parent, string propertyName)
    {
        var property = parent.FindPropertyRelative(propertyName);
        return property == null ? string.Empty : property.enumDisplayNames[property.enumValueIndex];
    }
}

public sealed class SoundCatalogBrowserWindow : EditorWindow
{
    private const string DefaultSoundCatalogPath = "Assets/Data/Combat/Presentation/Catalogs/SoundCatalog.asset";

    private SoundCatalog catalog;
    private SerializedObject serializedCatalog;
    private SerializedProperty entriesProperty;
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private string search = string.Empty;
    private int selectedIndex;

    [MenuItem("Tools/Seoul Playup/Combat/Sound Catalog Browser")]
    public static void Open()
    {
        GetWindow<SoundCatalogBrowserWindow>("Sound Catalog");
    }

    private void OnEnable()
    {
        LoadDefaultCatalog();
    }

    private void OnGUI()
    {
        DrawToolbar();
        if (catalog == null || serializedCatalog == null || entriesProperty == null)
        {
            EditorGUILayout.HelpBox($"No sound catalog found at {DefaultSoundCatalogPath}.", MessageType.Warning);
            return;
        }

        serializedCatalog.Update();
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawEntryList();
            DrawEntryDetail();
        }

        serializedCatalog.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            catalog = (SoundCatalog)EditorGUILayout.ObjectField(catalog, typeof(SoundCatalog), false, GUILayout.Width(260f));
            if (GUILayout.Button("Load Default", EditorStyles.toolbarButton, GUILayout.Width(90f)))
            {
                LoadDefaultCatalog();
            }

            if (GUILayout.Button("Rebuild From CSV", EditorStyles.toolbarButton, GUILayout.Width(120f)))
            {
                VfxCueCatalogTools.RebuildSoundCatalogFromMonsterCsvMenu();
                LoadDefaultCatalog();
            }

            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(70f)))
            {
                VfxCueCatalogTools.ValidateSoundCatalogMenu();
            }

            GUILayout.Label("Search", GUILayout.Width(46f));
            search = GUILayout.TextField(search, EditorStyles.toolbarSearchField);
        }

        if (serializedCatalog == null || serializedCatalog.targetObject != catalog)
        {
            BindCatalog();
        }
    }

    private void DrawEntryList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(360f)))
        {
            EditorGUILayout.LabelField($"Entries ({entriesProperty.arraySize})", EditorStyles.boldLabel);
            listScroll = EditorGUILayout.BeginScrollView(listScroll);
            for (var i = 0; i < entriesProperty.arraySize; i++)
            {
                var entry = entriesProperty.GetArrayElementAtIndex(i);
                if (!MatchesSearch(entry))
                {
                    continue;
                }

                var previousColor = GUI.backgroundColor;
                if (i == selectedIndex)
                {
                    GUI.backgroundColor = new Color(0.55f, 0.8f, 1f, 1f);
                }

                if (GUILayout.Button(BuildEntryLabel(entry, i), EditorStyles.miniButton))
                {
                    selectedIndex = i;
                    GUI.FocusControl(null);
                }

                GUI.backgroundColor = previousColor;
            }

            EditorGUILayout.EndScrollView();
        }
    }

    private void DrawEntryDetail()
    {
        using (new EditorGUILayout.VerticalScope())
        {
            EditorGUILayout.LabelField("Selected Sound Cue", EditorStyles.boldLabel);
            if (entriesProperty.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Catalog has no entries.", MessageType.Info);
                return;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, entriesProperty.arraySize - 1);
            var entry = entriesProperty.GetArrayElementAtIndex(selectedIndex);
            detailScroll = EditorGUILayout.BeginScrollView(detailScroll);
            DrawPatternUsageHelp(entry);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("cueId"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("displayName"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("category"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("tags"), includeChildren: true);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("designerNote"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("deprecated"));
            EditorGUILayout.Space(6f);
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("clip"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("bus"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("volume"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("pitchMin"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("pitchMax"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("cooldownSeconds"));
            EditorGUILayout.PropertyField(entry.FindPropertyRelative("missingClipBehavior"));
            EditorGUILayout.EndScrollView();

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Preview Clip"))
                {
                    PreviewClip(entry);
                }

                if (GUILayout.Button("Stop Preview"))
                {
                    StopPreviewClips();
                }

                if (GUILayout.Button("Ping Clip"))
                {
                    PingClip(entry);
                }
            }
        }
    }

    private void LoadDefaultCatalog()
    {
        catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(DefaultSoundCatalogPath);
        BindCatalog();
    }

    private void BindCatalog()
    {
        if (catalog == null)
        {
            serializedCatalog = null;
            entriesProperty = null;
            return;
        }

        serializedCatalog = new SerializedObject(catalog);
        entriesProperty = serializedCatalog.FindProperty("entries");
        selectedIndex = Mathf.Clamp(selectedIndex, 0, Mathf.Max(0, entriesProperty.arraySize - 1));
    }

    private void DrawPatternUsageHelp(SerializedProperty entry)
    {
        var cueId = ReadString(entry, "cueId");
        if (string.IsNullOrWhiteSpace(cueId))
        {
            return;
        }

        try
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
            var usages = bundle.PatternPresentations
                .Where(presentation => presentation.SoundWindupCueId == cueId || presentation.SoundCastCueId == cueId || presentation.SoundImpactCueId == cueId)
                .Select(presentation => $"{presentation.PatternId}: windup={presentation.SoundWindupCueId}, cast={presentation.SoundCastCueId}, impact={presentation.SoundImpactCueId}")
                .ToArray();
            if (usages.Length > 0)
            {
                EditorGUILayout.HelpBox("Monster pattern usage:\n" + string.Join("\n", usages), MessageType.Info);
            }
        }
        catch (Exception ex)
        {
            EditorGUILayout.HelpBox($"Could not read monster CSV sound bindings: {ex.Message}", MessageType.Warning);
        }
    }

    private bool MatchesSearch(SerializedProperty entry)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return true;
        }

        return BuildSearchText(entry).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string BuildSearchText(SerializedProperty entry)
    {
        return string.Join(" ",
            ReadString(entry, "cueId"),
            ReadString(entry, "displayName"),
            ReadString(entry, "category"),
            ReadString(entry, "designerNote"),
            ReadEnumName(entry, "bus"));
    }

    private static string BuildEntryLabel(SerializedProperty entry, int index)
    {
        var cueId = ReadString(entry, "cueId");
        var displayName = ReadString(entry, "displayName");
        var bus = ReadEnumName(entry, "bus");
        var clip = entry.FindPropertyRelative("clip").objectReferenceValue;
        var label = string.IsNullOrWhiteSpace(cueId) ? $"{index + 1:00}" : cueId;
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            label += $" - {displayName}";
        }

        label += $" [{bus}]";
        if (clip == null)
        {
            label += " (no clip)";
        }

        return label;
    }

    private static void PreviewClip(SerializedProperty entry)
    {
        var clip = entry.FindPropertyRelative("clip").objectReferenceValue as AudioClip;
        if (clip == null)
        {
            return;
        }

        var audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        var play = audioUtil?.GetMethod("PlayPreviewClip", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public, null, new[] { typeof(AudioClip), typeof(int), typeof(bool) }, null);
        play?.Invoke(null, new object[] { clip, 0, false });
    }

    private static void StopPreviewClips()
    {
        var audioUtil = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        var stop = audioUtil?.GetMethod("StopAllPreviewClips", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
        stop?.Invoke(null, null);
    }

    private static void PingClip(SerializedProperty entry)
    {
        var clip = entry.FindPropertyRelative("clip").objectReferenceValue;
        if (clip == null)
        {
            return;
        }

        Selection.activeObject = clip;
        EditorGUIUtility.PingObject(clip);
    }

    private static string ReadString(SerializedProperty parent, string propertyName)
    {
        return parent.FindPropertyRelative(propertyName)?.stringValue ?? string.Empty;
    }

    private static string ReadEnumName(SerializedProperty parent, string propertyName)
    {
        var property = parent.FindPropertyRelative(propertyName);
        return property == null ? string.Empty : property.enumDisplayNames[property.enumValueIndex];
    }
}


