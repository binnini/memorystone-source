using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Applies an <see cref="EnvironmentLookPreset"/> to the current scene at stage entry. Called just
    /// before the map source is rendered, so the "set values → build map" order is guaranteed: the
    /// visibility (암시야) mask and its material variants are built during Render, and re-injecting the
    /// view's settings afterwards would require a costly re-render.
    ///
    /// A null preset is a complete no-op — the scene keeps whatever it authored. Each of the preset's
    /// three asset slots is independent: an empty slot leaves that layer of the scene untouched.
    /// </summary>
    public static class LookPresetApplier
    {
        // The anchor authored by LookdevEnvSync. The directional rig lives under this root so applying a
        // preset can swap the rig by clearing the anchor's children — matching how the scene is built.
        public const string EnvLightingRootName = "── ENV LIGHTING (synced from Lookdev) ──";

        // Fallback anchor for scenes that have no ENV LIGHTING root but still want a preset-driven rig —
        // notably the ArtLookdev tuning scene, whose "── LIGHTING RIG ──" root also owns the studio rig.
        // Giving the preset rig its own child anchor keeps "clear the anchor" from eating those siblings.
        public const string PresetRigAnchorName = "Preset Rig";

        private const string PostVolumeName = "PoC Post Volume";

        /// <summary>Resolves the scene's look objects and applies every layer of the preset. Returns a log summary.</summary>
        public static string Apply(EnvironmentLookPreset preset, AtlasTilePresentationView view = null)
        {
            if (preset == null)
            {
                return "no look preset — scene look untouched";
            }

            preset.ApplyEnvironment();
            var rigNote = ApplyLightRig(preset, ResolveOrCreateLightRoot());
            var volNote = ApplyVolumeProfile(preset, ResolvePostVolume());
            var visNote = ApplyVisibility(preset, view != null ? view : ResolveView());
            return $"env applied; rig: {rigNote}; volume: {volNote}; visibility: {visNote}";
        }

        /// <summary>Replaces the anchor's children with a fresh instance of the preset's rig prefab (idempotent).</summary>
        public static string ApplyLightRig(EnvironmentLookPreset preset, Transform anchor)
        {
            if (preset == null || preset.LightRigPrefab == null)
            {
                return "no rig prefab — kept scene rig";
            }

            if (anchor == null)
            {
                return "ENV LIGHTING root missing — rig skipped";
            }

            for (var i = anchor.childCount - 1; i >= 0; i--)
            {
                DestroyObject(anchor.GetChild(i).gameObject);
            }

            var instance = InstantiateRig(preset.LightRigPrefab);
            instance.name = preset.LightRigPrefab.name; // strip "(Clone)"
            instance.transform.SetParent(anchor, worldPositionStays: false);
            return $"instantiated '{instance.name}'";
        }

        /// <summary>
        /// In play mode a plain clone is enough (the rig is discarded with the scene). In the editor the
        /// instance must stay *connected* to its prefab asset, otherwise a designer tuning the lights in
        /// the hierarchy would be editing a throwaway copy that no preset can ever capture.
        /// </summary>
        private static GameObject InstantiateRig(GameObject prefab)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && UnityEditor.PrefabUtility.IsPartOfPrefabAsset(prefab))
            {
                return (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(prefab);
            }
#endif
            return Object.Instantiate(prefab);
        }

        /// <summary>Points the scene's post Volume at the preset's profile.</summary>
        public static string ApplyVolumeProfile(EnvironmentLookPreset preset, Volume volume)
        {
            if (preset == null || preset.VolumeProfile == null)
            {
                return "no profile — kept scene profile";
            }

            if (volume == null)
            {
                return "post Volume missing — profile skipped";
            }

            volume.sharedProfile = preset.VolumeProfile;
            return $"set profile '{preset.VolumeProfile.name}'";
        }

        /// <summary>Re-injects the preset's visibility coefficients into the map view before it renders.</summary>
        public static string ApplyVisibility(EnvironmentLookPreset preset, AtlasTilePresentationView view)
        {
            if (preset == null || preset.VisibilitySettings == null)
            {
                return "no visibility settings — kept scene settings";
            }

            if (view == null)
            {
                return "map view missing — visibility skipped";
            }

            view.ApplyVisibilitySettings(preset.VisibilitySettings);
            return $"applied '{preset.VisibilitySettings.name}'";
        }

        /// <summary>
        /// Finds the transform the preset rig lives under, creating the ENV LIGHTING root if the scene has
        /// neither anchor. The gameplay scene's ENV LIGHTING root wins; the <see cref="PresetRigAnchorName"/>
        /// child anchor is the tuning-scene fallback. Public so the editor authoring tool swaps rigs into the
        /// exact same place the runtime does.
        /// </summary>
        public static Transform ResolveOrCreateLightRoot()
        {
            var existing = FindInActiveScene(EnvLightingRootName) ?? FindInActiveScene(PresetRigAnchorName);
            if (existing != null)
            {
                return existing.transform;
            }

            var root = new GameObject(EnvLightingRootName);
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            return root.transform;
        }

        /// <summary>
        /// Resolves the scene's post-processing Volume (the well-known "PoC Post Volume", else any global
        /// volume, else the first found). Public so the day→night crossfader can layer a temporary volume
        /// above it. Returns null when the scene has no Volume.
        /// </summary>
        public static Volume ResolvePostVolume()
        {
            var volumes = Object.FindObjectsByType<Volume>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (volumes.Length == 0)
            {
                return null;
            }

            // Prefer the well-known post volume by name; otherwise fall back to a global volume.
            return volumes.FirstOrDefault(v => v.gameObject.name == PostVolumeName)
                   ?? volumes.FirstOrDefault(v => v.isGlobal)
                   ?? volumes[0];
        }

        private static AtlasTilePresentationView ResolveView()
        {
            return Object.FindFirstObjectByType<AtlasTilePresentationView>(FindObjectsInactive.Include);
        }

        private static GameObject FindInActiveScene(string name)
        {
            return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Select(t => t.gameObject)
                .FirstOrDefault(go => go.name == name);
        }

        private static void DestroyObject(GameObject go)
        {
            if (Application.isPlaying)
            {
                Object.Destroy(go);
            }
            else
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
