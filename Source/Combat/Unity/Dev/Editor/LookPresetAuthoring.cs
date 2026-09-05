#if UNITY_EDITOR
using System.Linq;
using SeoulPlayup.Flow.Unity;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Designer authoring loop for <see cref="EnvironmentLookPreset"/> assets. Two directions:
    /// <list type="bullet">
    /// <item><b>Apply → Active Scene</b>: pushes the selected preset's environment (RenderSettings),
    /// directional light rig, post Volume profile, and visibility settings into whatever scene is open
    /// (e.g. ArtLookdev) so the look can be seen and compared (flip night ↔ day in one click).</item>
    /// <item><b>Capture ← Active Scene</b>: writes the active scene's current RenderSettings + post profile
    /// + view visibility + light rig back into the selected preset asset — the "confirm this tuning" step.</item>
    /// </list>
    ///
    /// The rig round-trips through its <b>prefab</b>, not through preset fields: Apply instantiates the
    /// preset's rig prefab (prefab-connected, so hierarchy edits are real prefab overrides) and Capture
    /// pushes those overrides back into the prefab asset — or, for a hand-built scene rig, saves it as a new
    /// prefab and wires it into the preset. That keeps a rig as rich as a prefab can be (cookies, extra
    /// components, nested lights) instead of a flat list of serialized light values.
    ///
    /// Where the rig goes: the gameplay scene's "── ENV LIGHTING ──" root when present, otherwise a
    /// "Preset Rig" anchor — created under the lookdev scene's studio toggle so swapping rigs never eats
    /// the neutral StudioRig sibling. Same anchor the runtime applier uses at stage entry.
    ///
    /// This replaces the "night-only, scene→scene" assumption of <see cref="LookdevEnvSync"/> for the
    /// gameplay path: presets are now authoritative for what a stage looks like. LookdevEnvSync is kept as
    /// a convenience for refreshing the MainGameplay scene-authored fallback (editor-open / no-stage runs).
    /// </summary>
    public static class LookPresetAuthoring
    {
        private const string ApplyMenu = "Seoul Playup/Dev/Look Preset/Apply Selected Preset → Active Scene";
        private const string CaptureMenu = "Seoul Playup/Dev/Look Preset/Capture Active Scene → Selected Preset";
        private const string PreviewStageMenu = "Seoul Playup/Dev/Look Preset/Preview Selected Stage's Look → Active Scene";
        private const string RigFolder = "Assets/Data/Lighting/LookPresets";
        private const string RetiredRigPrefix = "(retired) ";

        [MenuItem(ApplyMenu)]
        public static void ApplySelectedPresetToActiveScene()
        {
            if (!TryGetSelectedPreset(out var preset))
            {
                return;
            }

            ApplyPresetToActiveScene(preset);
        }

        [MenuItem(PreviewStageMenu)]
        public static void PreviewSelectedStageLook()
        {
            var stage = Selection.activeObject as StageDefinition;
            if (stage == null)
            {
                Debug.LogWarning("[LookPresetAuthoring] Select a StageDefinition asset in the Project window first.");
                return;
            }

            if (stage.LookPreset == null)
            {
                Debug.LogWarning($"[LookPresetAuthoring] Stage '{stage.name}' has no Look Preset assigned — nothing to preview.");
                return;
            }

            ApplyPresetToActiveScene(stage.LookPreset);
        }

        [MenuItem(CaptureMenu)]
        public static void CaptureActiveSceneToSelectedPreset()
        {
            if (!TryGetSelectedPreset(out var preset))
            {
                return;
            }

            CaptureActiveSceneToPreset(preset);
        }

        /// <summary>Writes the active scene's look into <paramref name="preset"/>. Entry point for the preset menu.</summary>
        public static void CaptureActiveSceneToPreset(EnvironmentLookPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            var volume = FindPostVolume();
            var view = FindView();
            // A scene that has no post Volume / map view says nothing about those layers — keep what the
            // preset already had instead of silently clearing the slot (the lookdev scene has no map view).
            var profile = (volume != null ? volume.sharedProfile : null) ?? preset.VolumeProfile;
            var visibility = (view != null ? ReadViewVisibilitySettings(view) : null) ?? preset.VisibilitySettings;

            Undo.RecordObject(preset, "Capture Look Preset");
            preset.CaptureEnvironmentFromRenderSettings();
            var rigNote = CaptureRig(preset, out var rigPrefab);
            preset.SetLookAssets(rigPrefab, profile, visibility);
            EditorUtility.SetDirty(preset);
            AssetDatabase.SaveAssetIfDirty(preset);

            Debug.Log($"[LookPresetAuthoring] Captured active scene → '{preset.name}'. " +
                      $"env(RenderSettings) captured; profile={(profile != null ? profile.name : "kept/none")}; " +
                      $"visibility={(visibility != null ? visibility.name : "kept/none")}; rig: {rigNote}.");
        }

        [MenuItem(ApplyMenu, validate = true)]
        [MenuItem(CaptureMenu, validate = true)]
        private static bool ValidateSelectedPreset()
        {
            return Selection.activeObject is EnvironmentLookPreset;
        }

        [MenuItem(PreviewStageMenu, validate = true)]
        private static bool ValidateSelectedStage()
        {
            return Selection.activeObject is StageDefinition;
        }

        /// <summary>Pushes every layer of <paramref name="preset"/> into the open scene. Entry point for the preset menu.</summary>
        public static void ApplyPresetToActiveScene(EnvironmentLookPreset preset)
        {
            if (preset == null)
            {
                return;
            }

            preset.ApplyEnvironment();
            var rigNote = LookPresetApplier.ApplyLightRig(preset, ResolveRigAnchor());
            var volNote = LookPresetApplier.ApplyVolumeProfile(preset, FindPostVolume());
            var visNote = LookPresetApplier.ApplyVisibility(preset, FindView());
            SyncStudioToggleAmbient();

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log($"[LookPresetAuthoring] Applied '{preset.name}' → scene '{scene.name}'. " +
                      $"env applied; rig: {rigNote}; {volNote}; {visNote}. " +
                      "Edit the rig in the hierarchy, then Capture (or Ctrl+S in prefab mode) to save it.");
        }

        // ── Rig anchor ──────────────────────────────────────────────────────────

        /// <summary>
        /// Resolves where the preset rig lives in the open scene, migrating the lookdev scene on first use:
        /// its hand-built rig is retired (deactivated, not deleted — the values stay inspectable) and the
        /// studio toggle is repointed at the new anchor so night/studio switching keeps working.
        /// </summary>
        private static Transform ResolveRigAnchor()
        {
            var envRoot = FindByName(LookPresetApplier.EnvLightingRootName);
            if (envRoot != null)
            {
                return envRoot.transform;
            }

            var toggle = Object.FindFirstObjectByType<ArtLookdevStudioToggle>(FindObjectsInactive.Include);
            return toggle != null ? ResolveLookdevAnchor(toggle) : LookPresetApplier.ResolveOrCreateLightRoot();
        }

        private static Transform ResolveLookdevAnchor(ArtLookdevStudioToggle toggle)
        {
            var anchor = toggle.transform.Find(LookPresetApplier.PresetRigAnchorName);
            if (anchor == null)
            {
                var created = new GameObject(LookPresetApplier.PresetRigAnchorName);
                created.transform.SetParent(toggle.transform, worldPositionStays: false);
                anchor = created.transform;
            }

            anchor.gameObject.SetActive(true);

            var serialized = new SerializedObject(toggle);
            var nightRigProp = serialized.FindProperty("nightRigRoot");
            var previous = nightRigProp?.objectReferenceValue as GameObject;
            if (previous != null && previous != anchor.gameObject && !previous.name.StartsWith(RetiredRigPrefix))
            {
                // A scene-local rig would keep lighting the scene on top of the preset rig. Deactivate it.
                previous.SetActive(false);
                previous.name = RetiredRigPrefix + previous.name;
                Debug.LogWarning(
                    $"[LookPresetAuthoring] Retired the scene-authored rig '{previous.name}' — the preset rig under " +
                    $"'{LookPresetApplier.PresetRigAnchorName}' is now authoritative. Delete it once you've confirmed the look.",
                    previous);
            }

            if (nightRigProp != null)
            {
                nightRigProp.objectReferenceValue = anchor.gameObject;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return anchor;
        }

        /// <summary>
        /// The lookdev studio toggle re-asserts its own ambient colours on every OnValidate, which would
        /// silently drag a day preset back to night. Point it at whatever preset was just applied.
        /// </summary>
        private static void SyncStudioToggleAmbient()
        {
            var toggle = Object.FindFirstObjectByType<ArtLookdevStudioToggle>(FindObjectsInactive.Include);
            if (toggle == null)
            {
                return;
            }

            if (RenderSettings.ambientMode != AmbientMode.Trilight)
            {
                Debug.LogWarning(
                    $"[LookPresetAuthoring] Preset uses ambient mode {RenderSettings.ambientMode}, but the lookdev studio " +
                    "toggle only restores Trilight. Toggling night/studio in this scene will not round-trip exactly.",
                    toggle);
                return;
            }

            var serialized = new SerializedObject(toggle);
            serialized.FindProperty("nightAmbientSky").colorValue = RenderSettings.ambientSkyColor;
            serialized.FindProperty("nightAmbientEquator").colorValue = RenderSettings.ambientEquatorColor;
            serialized.FindProperty("nightAmbientGround").colorValue = RenderSettings.ambientGroundColor;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ── Rig capture ─────────────────────────────────────────────────────────

        /// <summary>
        /// Saves the scene's rig into the preset. A prefab-connected rig has its hierarchy overrides applied
        /// back to the prefab asset; a hand-built scene rig is saved as a new prefab in the preset folder and
        /// connected. Returns the outgoing rig prefab through <paramref name="rigPrefab"/> (the preset's
        /// existing one when there is nothing to capture, so the slot is never cleared by accident).
        /// </summary>
        private static string CaptureRig(EnvironmentLookPreset preset, out GameObject rigPrefab)
        {
            rigPrefab = preset.LightRigPrefab;

            var anchor = FindRigAnchor();
            if (anchor == null)
            {
                return "no rig anchor in scene — slot kept";
            }

            var rig = Enumerable.Range(0, anchor.childCount)
                .Select(anchor.GetChild)
                .FirstOrDefault(child => child.gameObject.activeSelf);
            if (rig == null)
            {
                return "no active rig under anchor — slot kept";
            }

            var rigObject = rig.gameObject;
            if (PrefabUtility.IsAnyPrefabInstanceRoot(rigObject))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(rigObject);
                var hadOverrides = PrefabUtility.HasPrefabInstanceAnyOverrides(rigObject, includeDefaultOverrides: false);
                // Applied unconditionally: the override query only drives the log line, and it can miss edits
                // that were made from a script rather than through the inspector. Applying nothing is a no-op.
                PrefabUtility.ApplyPrefabInstance(rigObject, InteractionMode.AutomatedAction);

                rigPrefab = source != null ? source : rigPrefab;
                var name = rigPrefab != null ? rigPrefab.name : rigObject.name;
                return hadOverrides ? $"applied scene overrides to '{name}'" : $"'{name}' unchanged";
            }

            var path = AssetDatabase.GenerateUniqueAssetPath($"{RigFolder}/{preset.name}Rig.prefab");
            var saved = PrefabUtility.SaveAsPrefabAssetAndConnect(rigObject, path, InteractionMode.AutomatedAction);
            if (saved == null)
            {
                return $"failed to save scene rig '{rigObject.name}' as a prefab — slot kept";
            }

            rigPrefab = saved;
            return $"saved scene rig as '{path}' and wired it into the preset";
        }

        // ── Scene lookups ───────────────────────────────────────────────────────

        private static bool TryGetSelectedPreset(out EnvironmentLookPreset preset)
        {
            preset = Selection.activeObject as EnvironmentLookPreset;
            if (preset == null)
            {
                Debug.LogWarning("[LookPresetAuthoring] Select an EnvironmentLookPreset asset in the Project window first.");
                return false;
            }

            return true;
        }

        private static Volume FindPostVolume()
        {
            var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            return volumes.FirstOrDefault(v => v.gameObject.name == "PoC Post Volume")
                   ?? volumes.FirstOrDefault(v => v.isGlobal)
                   ?? volumes.FirstOrDefault();
        }

        private static AtlasTilePresentationView FindView()
        {
            return Object.FindFirstObjectByType<AtlasTilePresentationView>(FindObjectsInactive.Include);
        }

        /// <summary>Finds the rig anchor without creating one — capture must not author scene objects.</summary>
        private static Transform FindRigAnchor()
        {
            var root = FindByName(LookPresetApplier.EnvLightingRootName) ?? FindByName(LookPresetApplier.PresetRigAnchorName);
            return root != null ? root.transform : null;
        }

        private static GameObject FindByName(string name)
        {
            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(t => t.gameObject)
                .FirstOrDefault(go => go.name == name);
        }

        private static VisibilityPresentationSettings ReadViewVisibilitySettings(AtlasTilePresentationView view)
        {
            var serialized = new SerializedObject(view);
            return serialized.FindProperty("visibilitySettings")?.objectReferenceValue as VisibilityPresentationSettings;
        }
    }
}
#endif
