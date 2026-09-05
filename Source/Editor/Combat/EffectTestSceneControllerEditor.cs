using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
[CustomEditor(typeof(EffectTestSceneController))]
public sealed class EffectTestSceneControllerEditor : Editor
{
    private const string DefaultEffectVfxCatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";
    private const string PendingSnapshotJsonKey = "SeoulPlayup.EffectTest.PendingAssignments.Json";
    private const string PendingSnapshotSceneKey = "SeoulPlayup.EffectTest.PendingAssignments.Scene";

    private static readonly string[] VfxSearchRoots =
    {
        "Assets/ThirdParty/JMO Assets",
        "Assets/ThirdParty/Matthew Guz",
        "Assets/ThirdParty/NamuFX",
        "Assets/ThirdParty/Travis Game Assets",
        "Assets/ThirdParty/Hovl Studio"
    };

    private static readonly Dictionary<string, string[]> SuggestedKeywords = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        { "Damage", new[] { "Basic Hit", "Hit", "Impact", "Blood" } },
        { "Attack", new[] { "Slash", "Hit" } },
        { "Block", new[] { "Buff", "Aura", "Shield" } },
        { "Heal", new[] { "Healing", "Aura" } },
        { "FogReveal", new[] { "Aura Lightning", "Light", "Smoke" } },
        { "Reflect", new[] { "Magic", "Lightning", "Aura" } },
        { "Immobilize", new[] { "StatusAilment", "Debuff", "Stun" } },
        { "Agility", new[] { "Buff", "Aura" } },
        { "DamageMultiplier", new[] { "Buff", "Aura" } },
        { "Push", new[] { "Ground Hit", "Impact", "Hit" } },
        { "Burn", new[] { "Burn", "Fire" } },
        { "Poison", new[] { "Smell", "Debuff", "Skull" } },
        { "Stun", new[] { "Stun", "Electric", "Lightning" } },
        { "Slow", new[] { "Ice", "StatusAilment" } },
        { "VisionDown", new[] { "Smoke", "Shadow" } },
        { "Rupture", new[] { "Slash_Ink", "Blood", "Debuff" } },
    };

    private readonly Dictionary<string, bool> rootFoldouts = new Dictionary<string, bool>(StringComparer.Ordinal);
    private readonly List<PrefabOption> prefabOptions = new List<PrefabOption>();

    private SerializedProperty vfxAssignmentsProperty;
    private int selectedAssignmentIndex;
    private string search = string.Empty;
    private Vector2 assignmentScroll;
    private Vector2 prefabScroll;
    private bool showDefaultInspector;
    private bool onlySuggested = true;

    static EffectTestSceneControllerEditor()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnEnable()
    {
        vfxAssignmentsProperty = serializedObject.FindProperty("vfxAssignments");
        RefreshPrefabOptions();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox(
            "EffectTest 전용 VFX 할당 UI입니다. 왼쪽에서 Effect 항목을 고르고, 아래 prefab palette에서 Add/Replace 버튼으로 바로 할당하세요.",
            MessageType.Info);

        DrawToolbar();
        DrawAssignmentSelector();
        EditorGUILayout.Space(8f);
        DrawSelectedAssignment();
        EditorGUILayout.Space(8f);
        DrawPrefabPalette();
        EditorGUILayout.Space(8f);
        DrawDefaultInspectorFoldout();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawToolbar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Refresh Prefabs", GUILayout.Width(120f)))
            {
                RefreshPrefabOptions();
            }

            if (GUILayout.Button("Reset Assignments", GUILayout.Width(130f)))
            {
                foreach (var targetObject in targets)
                {
                    ((EffectTestSceneController)targetObject).ResetVfxAssignments();
                    EditorUtility.SetDirty(targetObject);
                }

                serializedObject.Update();
            }

            onlySuggested = GUILayout.Toggle(onlySuggested, "Suggested only", EditorStyles.toolbarButton, GUILayout.Width(120f));
        }
    }

    private void DrawAssignmentSelector()
    {
        if (vfxAssignmentsProperty == null || !vfxAssignmentsProperty.isArray)
        {
            EditorGUILayout.HelpBox("vfxAssignments serialized property was not found.", MessageType.Warning);
            return;
        }

        selectedAssignmentIndex = Mathf.Clamp(selectedAssignmentIndex, 0, Mathf.Max(0, vfxAssignmentsProperty.arraySize - 1));
        EditorGUILayout.LabelField($"VFX Assignments ({vfxAssignmentsProperty.arraySize})", EditorStyles.boldLabel);

        assignmentScroll = EditorGUILayout.BeginScrollView(assignmentScroll, GUILayout.Height(190f));
        for (var i = 0; i < vfxAssignmentsProperty.arraySize; i++)
        {
            var assignment = vfxAssignmentsProperty.GetArrayElementAtIndex(i);
            var label = ReadString(assignment, "label");
            var kind = ReadEnumName(assignment, "kind");
            var sourceRef = ReadString(assignment, "sourceRef");
            var prefabCount = CountAssignedPrefabs(assignment);
            var buttonLabel = $"{i + 1:00}. {label}  [{kind}]";
            if (!string.IsNullOrWhiteSpace(sourceRef))
            {
                buttonLabel += $"  <{sourceRef}>";
            }

            buttonLabel += $"  prefabs:{prefabCount}";

            var previousColor = GUI.backgroundColor;
            if (i == selectedAssignmentIndex)
            {
                GUI.backgroundColor = new Color(0.55f, 0.8f, 1f, 1f);
            }

            if (GUILayout.Button(buttonLabel, EditorStyles.miniButton))
            {
                selectedAssignmentIndex = i;
                search = BuildDefaultSearch(assignment);
                GUI.FocusControl(null);
            }

            GUI.backgroundColor = previousColor;
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawSelectedAssignment()
    {
        if (vfxAssignmentsProperty == null || vfxAssignmentsProperty.arraySize == 0)
        {
            return;
        }

        var assignment = vfxAssignmentsProperty.GetArrayElementAtIndex(selectedAssignmentIndex);
            EditorGUILayout.LabelField("Selected Assignment", EditorStyles.boldLabel);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Designer Metadata", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("cueId"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("label"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("category"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("tags"), includeChildren: true);
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("designerNote"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("deprecated"));
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Runtime Match", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("kind"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("targetFilter"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("sourceRef"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("matchSourceRefPrefix"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("amount"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("appliedAmount"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("radius"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("isArea"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("targetAnchor"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("targetUnitId"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("prefabs"), includeChildren: true);
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("scaleMultiplier"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("scaleWithRadius"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("positionOffset"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("rotationEulerOffset"));
            EditorGUILayout.PropertyField(assignment.FindPropertyRelative("lifetimeOverride"));

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Clear Prefabs"))
                {
                    assignment.FindPropertyRelative("prefabs").ClearArray();
                }

                if (GUILayout.Button("Search Suggested"))
                {
                    search = BuildDefaultSearch(assignment);
                    onlySuggested = true;
                    GUI.FocusControl(null);
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Scale x1"))
                {
                    assignment.FindPropertyRelative("scaleMultiplier").floatValue = 1f;
                }

                if (GUILayout.Button("Scale x3"))
                {
                    assignment.FindPropertyRelative("scaleMultiplier").floatValue = 3f;
                }

                if (GUILayout.Button("Scale x5"))
                {
                    assignment.FindPropertyRelative("scaleMultiplier").floatValue = 5f;
                }

                if (GUILayout.Button("JMO Preset"))
                {
                    ApplyVisibilityPreset(assignment, 4f, Vector3.zero);
                }
            }

            EditorGUILayout.LabelField("Orientation Presets", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Default"))
                {
                    assignment.FindPropertyRelative("rotationEulerOffset").vector3Value = Vector3.zero;
                }

                if (GUILayout.Button("Ground X90"))
                {
                    assignment.FindPropertyRelative("rotationEulerOffset").vector3Value = new Vector3(90f, 0f, 0f);
                }

                if (GUILayout.Button("Face Cam Y0"))
                {
                    assignment.FindPropertyRelative("rotationEulerOffset").vector3Value = Vector3.zero;
                    assignment.FindPropertyRelative("positionOffset").vector3Value = new Vector3(0f, 0.6f, 0f);
                }

                if (GUILayout.Button("Air +Y"))
                {
                    assignment.FindPropertyRelative("positionOffset").vector3Value = new Vector3(0f, 0.8f, 0f);
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Persist / Catalog", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                EditorApplication.isPlaying
                    ? "Play Mode에서 조정한 값은 버튼을 누른 뒤 Play Mode를 종료하면 씬에 자동 저장됩니다."
                    : "현재 Inspector 값을 씬에 저장하거나 DefaultEffectVfxCatalog.asset에 반영합니다.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(EditorApplication.isPlaying ? "Save Play Values To Scene" : "Save Values To Scene"))
                {
                    SaveCurrentValuesToScene(showDialog: true);
                }

                if (GUILayout.Button("Apply Selected To Default Catalog"))
                {
                    ApplySelectedAssignmentToDefaultCatalog();
                }
            }

            if (GUILayout.Button("Apply All Assignments To Default Catalog"))
            {
                ApplyAllAssignmentsToDefaultCatalog();
            }
        }
    }

    private void DrawPrefabPalette()
    {
        if (vfxAssignmentsProperty == null || vfxAssignmentsProperty.arraySize == 0)
        {
            return;
        }

        var assignment = vfxAssignmentsProperty.GetArrayElementAtIndex(selectedAssignmentIndex);
        EditorGUILayout.LabelField("Target Asset Prefab Palette", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Search", GUILayout.Width(48f));
            search = EditorGUILayout.TextField(search);
            if (GUILayout.Button("X", GUILayout.Width(24f)))
            {
                search = string.Empty;
                GUI.FocusControl(null);
            }
        }

        var filtered = FilterOptions(assignment).ToList();
        EditorGUILayout.LabelField($"{filtered.Count} / {prefabOptions.Count} prefabs");

        prefabScroll = EditorGUILayout.BeginScrollView(prefabScroll, GUILayout.Height(330f));
        foreach (var group in filtered.GroupBy(option => option.Root))
        {
            if (!rootFoldouts.ContainsKey(group.Key))
            {
                rootFoldouts[group.Key] = true;
            }

            rootFoldouts[group.Key] = EditorGUILayout.Foldout(rootFoldouts[group.Key], $"{group.Key} ({group.Count()})", true);
            if (!rootFoldouts[group.Key])
            {
                continue;
            }

            foreach (var option in group)
            {
                DrawPrefabOption(assignment, option);
            }
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawPrefabOption(SerializedProperty assignment, PrefabOption option)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.ObjectField(option.Prefab, typeof(GameObject), false, GUILayout.Width(80f), GUILayout.Height(36f));
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField(option.Name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(option.RelativePath, EditorStyles.miniLabel);
            }

            if (GUILayout.Button("Replace", GUILayout.Width(68f)))
            {
                ReplacePrefabs(assignment, option.Prefab);
                ApplyPresetForOption(assignment, option);
            }

            if (GUILayout.Button("Add", GUILayout.Width(44f)))
            {
                AddPrefab(assignment, option.Prefab);
                ApplyPresetForOption(assignment, option);
            }

            if (GUILayout.Button("Ping", GUILayout.Width(44f)))
            {
                EditorGUIUtility.PingObject(option.Prefab);
            }
        }
    }

    private void DrawDefaultInspectorFoldout()
    {
        showDefaultInspector = EditorGUILayout.Foldout(showDefaultInspector, "Advanced: Default Inspector", true);
        if (!showDefaultInspector)
        {
            return;
        }

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("Script", MonoScript.FromMonoBehaviour((EffectTestSceneController)target), typeof(MonoScript), false);
        }

        DrawPropertiesExcluding(serializedObject, "m_Script", "vfxAssignments");
    }

    private IEnumerable<PrefabOption> FilterOptions(SerializedProperty assignment)
    {
        var tokens = SplitTokens(search);
        var suggested = GetSuggestedTokens(assignment);
        foreach (var option in prefabOptions)
        {
            if (onlySuggested && suggested.Length > 0 && !suggested.Any(option.Matches))
            {
                continue;
            }

            if (tokens.Length > 0 && !tokens.All(option.Matches))
            {
                continue;
            }

            yield return option;
        }
    }

    private string BuildDefaultSearch(SerializedProperty assignment)
    {
        var suggested = GetSuggestedTokens(assignment);
        return suggested.Length > 0 ? suggested[0] : ReadEnumName(assignment, "kind");
    }

    private static string[] GetSuggestedTokens(SerializedProperty assignment)
    {
        var label = ReadString(assignment, "label");
        var kind = ReadEnumName(assignment, "kind");
        var sourceRef = ReadString(assignment, "sourceRef");

        if (label.Contains("Attack", StringComparison.OrdinalIgnoreCase) ||
            sourceRef.StartsWith("attack", StringComparison.OrdinalIgnoreCase))
        {
            return SuggestedKeywords["Attack"];
        }

        foreach (var pair in SuggestedKeywords)
        {
            if (kind.Contains(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                label.Contains(pair.Key, StringComparison.OrdinalIgnoreCase) ||
                sourceRef.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return Array.Empty<string>();
    }

    private void RefreshPrefabOptions()
    {
        prefabOptions.Clear();
        foreach (var root in VfxSearchRoots)
        {
            if (!AssetDatabase.IsValidFolder(root))
            {
                continue;
            }

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { root });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || !LooksLikeVfxPrefab(prefab))
                {
                    continue;
                }

                prefabOptions.Add(new PrefabOption(root, path, prefab));
            }
        }

        prefabOptions.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeVfxPrefab(GameObject prefab)
    {
        return prefab.GetComponentInChildren<ParticleSystem>(true) != null ||
               prefab.GetComponentsInChildren<Renderer>(true).Length > 0;
    }

    private static string[] SplitTokens(string value)
    {
        return (value ?? string.Empty)
            .Split(new[] { ' ', ',', ';', '/', '\\', '_' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToArray();
    }

    private static void ReplacePrefabs(SerializedProperty assignment, GameObject prefab)
    {
        var prefabs = assignment.FindPropertyRelative("prefabs");
        prefabs.ClearArray();
        prefabs.InsertArrayElementAtIndex(0);
        prefabs.GetArrayElementAtIndex(0).objectReferenceValue = prefab;
    }

    private static void AddPrefab(SerializedProperty assignment, GameObject prefab)
    {
        var prefabs = assignment.FindPropertyRelative("prefabs");
        for (var i = 0; i < prefabs.arraySize; i++)
        {
            if (prefabs.GetArrayElementAtIndex(i).objectReferenceValue == prefab)
            {
                return;
            }
        }

        var index = prefabs.arraySize;
        prefabs.InsertArrayElementAtIndex(index);
        prefabs.GetArrayElementAtIndex(index).objectReferenceValue = prefab;
    }

    private static void ApplyPresetForOption(SerializedProperty assignment, PrefabOption option)
    {
        if (!option.Path.StartsWith("Assets/ThirdParty/JMO Assets", StringComparison.Ordinal))
        {
            return;
        }

        var scale = option.Path.Contains("Ground", StringComparison.OrdinalIgnoreCase) ? 5f : 3.5f;
        var offset = option.Path.Contains("(Air)", StringComparison.OrdinalIgnoreCase)
            ? new Vector3(0f, 0.8f, 0f)
            : Vector3.zero;
        ApplyVisibilityPreset(assignment, scale, offset);
    }

    private static void ApplyVisibilityPreset(SerializedProperty assignment, float minimumScale, Vector3 offset)
    {
        var scaleProperty = assignment.FindPropertyRelative("scaleMultiplier");
        if (scaleProperty.floatValue < minimumScale)
        {
            scaleProperty.floatValue = minimumScale;
        }

        assignment.FindPropertyRelative("positionOffset").vector3Value = offset;
    }

    private void SaveCurrentValuesToScene(bool showDialog)
    {
        serializedObject.ApplyModifiedProperties();
        var controller = (EffectTestSceneController)target;
        if (EditorApplication.isPlaying)
        {
            var snapshot = CaptureAssignments(vfxAssignmentsProperty);
            EditorPrefs.SetString(PendingSnapshotJsonKey, JsonUtility.ToJson(snapshot));
            EditorPrefs.SetString(PendingSnapshotSceneKey, controller.gameObject.scene.path);
            Debug.Log("[EffectTest] Play Mode VFX assignment values captured. They will be applied to the scene after exiting Play Mode.");
            if (showDialog)
            {
                EditorUtility.DisplayDialog(
                    "EffectTest",
                    "Play Mode 값이 임시 저장되었습니다.\nPlay Mode를 종료하면 EffectTest 씬에 자동 반영/저장됩니다.",
                    "OK");
            }

            return;
        }

        MarkAndSaveScene(controller);
        Debug.Log("[EffectTest] VFX assignment values saved to scene.");
    }

    private void ApplySelectedAssignmentToDefaultCatalog()
    {
        serializedObject.ApplyModifiedProperties();
        if (vfxAssignmentsProperty == null || vfxAssignmentsProperty.arraySize == 0)
        {
            return;
        }

        var assignment = vfxAssignmentsProperty.GetArrayElementAtIndex(selectedAssignmentIndex);
        var entry = CreateCatalogEntry(assignment);
        if (entry == null)
        {
            EditorUtility.DisplayDialog("EffectTest", "선택한 항목에 할당된 Prefab이 없습니다.", "OK");
            return;
        }

        var catalog = LoadOrCreateDefaultCatalog();
        var entries = catalog.Entries
            .Where(existing => !SameCatalogKey(existing, entry))
            .Concat(new[] { entry })
            .ToArray();
        catalog.SetEntries(entries);
        SaveCatalog(catalog);
        SaveCurrentValuesToScene(showDialog: false);
        EditorUtility.DisplayDialog("EffectTest", "선택한 VFX 할당을 DefaultEffectVfxCatalog.asset에 반영했습니다.", "OK");
    }

    private void ApplyAllAssignmentsToDefaultCatalog()
    {
        serializedObject.ApplyModifiedProperties();
        if (vfxAssignmentsProperty == null || vfxAssignmentsProperty.arraySize == 0)
        {
            return;
        }

        var newEntries = new List<EffectVfxCatalog.Entry>();
        for (var i = 0; i < vfxAssignmentsProperty.arraySize; i++)
        {
            var entry = CreateCatalogEntry(vfxAssignmentsProperty.GetArrayElementAtIndex(i));
            if (entry != null)
            {
                newEntries.Add(entry);
            }
        }

        if (newEntries.Count == 0)
        {
            EditorUtility.DisplayDialog("EffectTest", "카탈로그에 반영할 Prefab 할당이 없습니다.", "OK");
            return;
        }

        var catalog = LoadOrCreateDefaultCatalog();
        var entries = catalog.Entries
            .Where(existing => !newEntries.Any(newEntry => SameCatalogKey(existing, newEntry)))
            .Concat(newEntries)
            .ToArray();
        catalog.SetEntries(entries);
        SaveCatalog(catalog);
        SaveCurrentValuesToScene(showDialog: false);
        EditorUtility.DisplayDialog("EffectTest", $"{newEntries.Count}개 VFX 할당을 DefaultEffectVfxCatalog.asset에 반영했습니다.", "OK");
    }

    private static EffectVfxCatalog.Entry CreateCatalogEntry(SerializedProperty assignment)
    {
        var prefabsProperty = assignment.FindPropertyRelative("prefabs");
        var prefabs = new List<GameObject>();
        for (var i = 0; i < prefabsProperty.arraySize; i++)
        {
            if (prefabsProperty.GetArrayElementAtIndex(i).objectReferenceValue is GameObject prefab)
            {
                prefabs.Add(prefab);
            }
        }

        if (prefabs.Count == 0)
        {
            return null;
        }

        return new EffectVfxCatalog.Entry(
            (EffectKind)assignment.FindPropertyRelative("kind").enumValueIndex,
            prefabs.ToArray(),
            (EffectVfxTargetFilter)assignment.FindPropertyRelative("targetFilter").enumValueIndex,
            ReadString(assignment, "sourceRef"),
            assignment.FindPropertyRelative("matchSourceRefPrefix").boolValue,
            assignment.FindPropertyRelative("scaleMultiplier").floatValue,
            assignment.FindPropertyRelative("scaleWithRadius").boolValue,
            assignment.FindPropertyRelative("positionOffset").vector3Value,
            assignment.FindPropertyRelative("rotationEulerOffset").vector3Value,
            assignment.FindPropertyRelative("lifetimeOverride").floatValue,
            ReadString(assignment, "cueId"),
            ReadString(assignment, "label"),
            ReadString(assignment, "category"),
            ReadStringArray(assignment, "tags"),
            ReadString(assignment, "designerNote"),
            assignment.FindPropertyRelative("deprecated").boolValue);
    }

    private static bool SameCatalogKey(EffectVfxCatalog.Entry left, EffectVfxCatalog.Entry right)
    {
        return left.Kind == right.Kind &&
               left.TargetFilter == right.TargetFilter &&
               string.Equals(left.SourceRef ?? string.Empty, right.SourceRef ?? string.Empty, StringComparison.Ordinal) &&
               left.MatchSourceRefPrefix == right.MatchSourceRefPrefix;
    }

    private static EffectVfxCatalog LoadOrCreateDefaultCatalog()
    {
        var catalog = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(DefaultEffectVfxCatalogPath);
        if (catalog != null)
        {
            return catalog;
        }

        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Combat");
        catalog = CreateInstance<EffectVfxCatalog>();
        AssetDatabase.CreateAsset(catalog, DefaultEffectVfxCatalogPath);
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

    private static void SaveCatalog(EffectVfxCatalog catalog)
    {
        EditorUtility.SetDirty(catalog);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[EffectTest] Default VFX catalog saved: {DefaultEffectVfxCatalogPath}");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.EnteredEditMode)
        {
            ApplyPendingPlayModeSnapshot();
        }
    }

    private static void ApplyPendingPlayModeSnapshot()
    {
        if (!EditorPrefs.HasKey(PendingSnapshotJsonKey))
        {
            return;
        }

        var json = EditorPrefs.GetString(PendingSnapshotJsonKey, string.Empty);
        var scenePath = EditorPrefs.GetString(PendingSnapshotSceneKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            ClearPendingSnapshot();
            return;
        }

        if (!string.IsNullOrWhiteSpace(scenePath))
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene.path != scenePath)
            {
                EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            }
        }

        var controller = UnityEngine.Object.FindFirstObjectByType<EffectTestSceneController>();
        if (controller == null)
        {
            Debug.LogWarning("[EffectTest] Could not apply saved Play Mode VFX values because no EffectTestSceneController was found.");
            return;
        }

        var snapshot = JsonUtility.FromJson<AssignmentSnapshotList>(json);
        var serializedController = new SerializedObject(controller);
        var assignments = serializedController.FindProperty("vfxAssignments");
        ApplyAssignmentsSnapshot(assignments, snapshot);
        serializedController.ApplyModifiedPropertiesWithoutUndo();
        MarkAndSaveScene(controller);
        ClearPendingSnapshot();
        Debug.Log("[EffectTest] Applied and saved Play Mode VFX assignment values to the scene.");
    }

    private static void ClearPendingSnapshot()
    {
        EditorPrefs.DeleteKey(PendingSnapshotJsonKey);
        EditorPrefs.DeleteKey(PendingSnapshotSceneKey);
    }

    private static void MarkAndSaveScene(EffectTestSceneController controller)
    {
        EditorUtility.SetDirty(controller);
        var scene = controller.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    private static AssignmentSnapshotList CaptureAssignments(SerializedProperty assignments)
    {
        var list = new AssignmentSnapshotList
        {
            assignments = new AssignmentSnapshot[assignments.arraySize]
        };

        for (var i = 0; i < assignments.arraySize; i++)
        {
            list.assignments[i] = CaptureAssignment(assignments.GetArrayElementAtIndex(i));
        }

        return list;
    }

    private static AssignmentSnapshot CaptureAssignment(SerializedProperty assignment)
    {
        var prefabGuids = new List<string>();
        var prefabs = assignment.FindPropertyRelative("prefabs");
        for (var i = 0; i < prefabs.arraySize; i++)
        {
            var prefab = prefabs.GetArrayElementAtIndex(i).objectReferenceValue;
            if (prefab == null)
            {
                prefabGuids.Add(string.Empty);
                continue;
            }

            prefabGuids.Add(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab)));
        }

        return new AssignmentSnapshot
        {
            cueId = ReadString(assignment, "cueId"),
            label = ReadString(assignment, "label"),
            category = ReadString(assignment, "category"),
            tags = ReadStringArray(assignment, "tags"),
            designerNote = ReadString(assignment, "designerNote"),
            deprecated = assignment.FindPropertyRelative("deprecated").boolValue,
            kind = assignment.FindPropertyRelative("kind").enumValueIndex,
            targetFilter = assignment.FindPropertyRelative("targetFilter").enumValueIndex,
            sourceRef = ReadString(assignment, "sourceRef"),
            matchSourceRefPrefix = assignment.FindPropertyRelative("matchSourceRefPrefix").boolValue,
            amount = assignment.FindPropertyRelative("amount").intValue,
            appliedAmount = assignment.FindPropertyRelative("appliedAmount").intValue,
            radius = assignment.FindPropertyRelative("radius").intValue,
            isArea = assignment.FindPropertyRelative("isArea").boolValue,
            targetAnchor = assignment.FindPropertyRelative("targetAnchor").enumValueIndex,
            targetUnitId = ReadString(assignment, "targetUnitId"),
            prefabGuids = prefabGuids.ToArray(),
            scaleMultiplier = assignment.FindPropertyRelative("scaleMultiplier").floatValue,
            scaleWithRadius = assignment.FindPropertyRelative("scaleWithRadius").boolValue,
            positionOffset = assignment.FindPropertyRelative("positionOffset").vector3Value,
            rotationEulerOffset = assignment.FindPropertyRelative("rotationEulerOffset").vector3Value,
            lifetimeOverride = assignment.FindPropertyRelative("lifetimeOverride").floatValue
        };
    }

    private static void ApplyAssignmentsSnapshot(SerializedProperty assignments, AssignmentSnapshotList snapshot)
    {
        if (assignments == null || snapshot?.assignments == null)
        {
            return;
        }

        assignments.arraySize = snapshot.assignments.Length;
        for (var i = 0; i < snapshot.assignments.Length; i++)
        {
            ApplyAssignmentSnapshot(assignments.GetArrayElementAtIndex(i), snapshot.assignments[i]);
        }
    }

    private static void ApplyAssignmentSnapshot(SerializedProperty assignment, AssignmentSnapshot snapshot)
    {
        assignment.FindPropertyRelative("label").stringValue = snapshot.label ?? string.Empty;
        assignment.FindPropertyRelative("cueId").stringValue = snapshot.cueId ?? string.Empty;
        assignment.FindPropertyRelative("category").stringValue = snapshot.category ?? string.Empty;
        assignment.FindPropertyRelative("designerNote").stringValue = snapshot.designerNote ?? string.Empty;
        assignment.FindPropertyRelative("deprecated").boolValue = snapshot.deprecated;
        ApplyStringArray(assignment.FindPropertyRelative("tags"), snapshot.tags);
        assignment.FindPropertyRelative("kind").enumValueIndex = snapshot.kind;
        assignment.FindPropertyRelative("targetFilter").enumValueIndex = snapshot.targetFilter;
        assignment.FindPropertyRelative("sourceRef").stringValue = snapshot.sourceRef ?? string.Empty;
        assignment.FindPropertyRelative("matchSourceRefPrefix").boolValue = snapshot.matchSourceRefPrefix;
        assignment.FindPropertyRelative("amount").intValue = snapshot.amount;
        assignment.FindPropertyRelative("appliedAmount").intValue = snapshot.appliedAmount;
        assignment.FindPropertyRelative("radius").intValue = snapshot.radius;
        assignment.FindPropertyRelative("isArea").boolValue = snapshot.isArea;
        assignment.FindPropertyRelative("targetAnchor").enumValueIndex = snapshot.targetAnchor;
        assignment.FindPropertyRelative("targetUnitId").stringValue = snapshot.targetUnitId ?? string.Empty;
        assignment.FindPropertyRelative("scaleMultiplier").floatValue = snapshot.scaleMultiplier;
        assignment.FindPropertyRelative("scaleWithRadius").boolValue = snapshot.scaleWithRadius;
        assignment.FindPropertyRelative("positionOffset").vector3Value = snapshot.positionOffset;
        assignment.FindPropertyRelative("rotationEulerOffset").vector3Value = snapshot.rotationEulerOffset;
        assignment.FindPropertyRelative("lifetimeOverride").floatValue = snapshot.lifetimeOverride;

        var prefabs = assignment.FindPropertyRelative("prefabs");
        prefabs.arraySize = snapshot.prefabGuids?.Length ?? 0;
        for (var i = 0; i < prefabs.arraySize; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(snapshot.prefabGuids[i]);
            prefabs.GetArrayElementAtIndex(i).objectReferenceValue = string.IsNullOrWhiteSpace(path)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
    }

    private static int CountAssignedPrefabs(SerializedProperty assignment)
    {
        var prefabs = assignment.FindPropertyRelative("prefabs");
        var count = 0;
        for (var i = 0; i < prefabs.arraySize; i++)
        {
            if (prefabs.GetArrayElementAtIndex(i).objectReferenceValue != null)
            {
                count++;
            }
        }

        return count;
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

    private static string[] ReadStringArray(SerializedProperty parent, string propertyName)
    {
        var property = parent.FindPropertyRelative(propertyName);
        if (property == null || !property.isArray)
        {
            return Array.Empty<string>();
        }

        var values = new string[property.arraySize];
        for (var i = 0; i < property.arraySize; i++)
        {
            values[i] = property.GetArrayElementAtIndex(i).stringValue ?? string.Empty;
        }

        return values;
    }

    private static void ApplyStringArray(SerializedProperty property, string[] values)
    {
        if (property == null || !property.isArray)
        {
            return;
        }

        property.arraySize = values?.Length ?? 0;
        for (var i = 0; i < property.arraySize; i++)
        {
            property.GetArrayElementAtIndex(i).stringValue = values[i] ?? string.Empty;
        }
    }

    [Serializable]
    private sealed class AssignmentSnapshotList
    {
        public AssignmentSnapshot[] assignments;
    }

    [Serializable]
    private sealed class AssignmentSnapshot
    {
        public string cueId;
        public string label;
        public string category;
        public string[] tags;
        public string designerNote;
        public bool deprecated;
        public int kind;
        public int targetFilter;
        public string sourceRef;
        public bool matchSourceRefPrefix;
        public int amount;
        public int appliedAmount;
        public int radius;
        public bool isArea;
        public int targetAnchor;
        public string targetUnitId;
        public string[] prefabGuids;
        public float scaleMultiplier;
        public bool scaleWithRadius;
        public Vector3 positionOffset;
        public Vector3 rotationEulerOffset;
        public float lifetimeOverride;
    }

    private readonly struct PrefabOption
    {
        public PrefabOption(string root, string path, GameObject prefab)
        {
            Root = root;
            Path = path;
            Prefab = prefab;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
            RelativePath = path.Substring(Mathf.Min(path.Length, root.Length)).TrimStart('/', '\\');
            SearchText = $"{root} {path} {Name}".ToLowerInvariant();
        }

        public string Root { get; }
        public string Path { get; }
        public GameObject Prefab { get; }
        public string Name { get; }
        public string RelativePath { get; }
        private string SearchText { get; }

        public bool Matches(string token)
        {
            return string.IsNullOrWhiteSpace(token) ||
                   SearchText.Contains(token.ToLowerInvariant());
        }
    }
}
