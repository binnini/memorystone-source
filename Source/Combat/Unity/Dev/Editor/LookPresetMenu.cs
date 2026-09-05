#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SeoulPlayup.Flow.Unity;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Lists every look preset (and every stage that uses one) directly in the menu, so switching a look is
    /// one click instead of "find the asset in the Project window, select it, then hit Apply":
    ///
    /// <code>
    /// Seoul Playup ▸ Dev ▸ Look Preset ▸ Apply ▸ SeoulNight
    ///                                 ▸ Capture Into ▸ SeoulNight
    ///                                 ▸ Preview Stage ▸ Stage_000_Tutorial (TutorialDay)
    /// </code>
    ///
    /// <c>[MenuItem]</c> paths are baked at compile time, so the entries are registered at runtime instead —
    /// through <c>UnityEditor.Menu.AddMenuItem</c>, which is internal and therefore reached by reflection.
    /// If a future Unity version changes that API the menu simply does not appear (logged once): the
    /// selection-based commands in <see cref="LookPresetAuthoring"/> keep working, so nothing is lost.
    ///
    /// The list rebuilds on domain reload and whenever a preset or stage asset is added, deleted or renamed.
    /// </summary>
    [InitializeOnLoad]
    public static class LookPresetMenu
    {
        private const string Root = "Seoul Playup/Dev/Look Preset/";
        private const string ApplyGroup = Root + "Apply/";
        private const string CaptureGroup = Root + "Capture Into/";
        private const string PreviewGroup = Root + "Preview Stage/";

        // Priorities keep the generated groups above the selection-based commands; gaps > 10 draw separators.
        private const int ApplyPriority = 100;
        private const int CapturePriority = 200;
        private const int PreviewPriority = 300;

        private static readonly List<string> Registered = new List<string>();
        private static bool reflectionFailureLogged;

        static LookPresetMenu()
        {
            // The asset database is not queryable while the domain is still loading.
            EditorApplication.delayCall += Rebuild;
        }

        public static void Rebuild()
        {
            if (!TryGetMenuApi(out var add, out var remove))
            {
                return;
            }

            foreach (var path in Registered)
            {
                remove(path);
            }

            Registered.Clear();

            var presets = LoadAll<EnvironmentLookPreset>()
                .OrderBy(p => p.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (var i = 0; i < presets.Count; i++)
            {
                // The commands capture a *path*, not the asset: a menu entry outlives domain reloads and
                // asset unloads, and a captured Object reference would quietly go null underneath it.
                var path = AssetDatabase.GetAssetPath(presets[i]);
                Register(add, ApplyGroup + Sanitize(presets[i].name), ApplyPriority + i,
                    () => LookPresetAuthoring.ApplyPresetToActiveScene(Load<EnvironmentLookPreset>(path)));
                Register(add, CaptureGroup + Sanitize(presets[i].name), CapturePriority + i,
                    () => LookPresetAuthoring.CaptureActiveSceneToPreset(Load<EnvironmentLookPreset>(path)));
            }

            var stages = LoadAll<StageDefinition>()
                .Where(s => s.LookPreset != null)
                .OrderBy(s => s.name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (var i = 0; i < stages.Count; i++)
            {
                var path = AssetDatabase.GetAssetPath(stages[i]);
                var label = $"{Sanitize(stages[i].name)} ({Sanitize(stages[i].LookPreset.name)})";
                Register(add, PreviewGroup + label, PreviewPriority + i,
                    () => LookPresetAuthoring.ApplyPresetToActiveScene(Load<StageDefinition>(path)?.LookPreset));
            }
        }

        private static T Load<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                Debug.LogWarning($"[LookPresetMenu] '{path}' no longer exists — the menu will refresh on the next asset change.");
            }

            return asset;
        }

        private static void Register(Action<string, int, Action> add, string path, int priority, Action command)
        {
            add(path, priority, command);
            Registered.Add(path);
        }

        private static IEnumerable<T> LoadAll<T>() where T : ScriptableObject
        {
            return AssetDatabase.FindAssets("t:" + typeof(T).Name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<T>)
                .Where(asset => asset != null);
        }

        /// <summary>A '/' in an asset name would silently split the entry into another submenu level.</summary>
        private static string Sanitize(string name)
        {
            return name.Replace('/', '-');
        }

        private static bool TryGetMenuApi(out Action<string, int, Action> add, out Action<string> remove)
        {
            add = null;
            remove = null;
            try
            {
                var menu = typeof(UnityEditor.Menu);
                var addMethod = menu.GetMethod(
                    "AddMenuItem",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(string), typeof(string), typeof(bool), typeof(int), typeof(Action), typeof(Func<bool>) },
                    null);
                var removeMethod = menu.GetMethod("RemoveMenuItem", BindingFlags.NonPublic | BindingFlags.Static);
                if (addMethod == null || removeMethod == null)
                {
                    LogReflectionFailureOnce("UnityEditor.Menu.AddMenuItem/RemoveMenuItem not found");
                    return false;
                }

                add = (path, priority, command) =>
                    addMethod.Invoke(null, new object[] { path, string.Empty, false, priority, command, null });
                remove = path => removeMethod.Invoke(null, new object[] { path });
                return true;
            }
            catch (Exception e)
            {
                LogReflectionFailureOnce(e.Message);
                return false;
            }
        }

        private static void LogReflectionFailureOnce(string reason)
        {
            if (reflectionFailureLogged)
            {
                return;
            }

            reflectionFailureLogged = true;
            Debug.LogWarning(
                $"[LookPresetMenu] Could not build the preset menu ({reason}). Use " +
                "'Look Preset ▸ Apply Selected Preset → Active Scene' with the asset selected instead.");
        }

    }

    /// <summary>
    /// Keeps the menu in sync when presets or stages are created, deleted or renamed. Top-level on purpose —
    /// Unity discovers asset postprocessors by scanning the assembly's types, and a class nested inside
    /// <see cref="LookPresetMenu"/> is not picked up.
    /// </summary>
    internal sealed class LookPresetMenuAssetWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Assets that still exist can be type-checked. Deleted / moved-from paths cannot — the type
            // query returns null — so any vanished .asset triggers a rebuild. Rescanning is cheap and
            // deletions are rare; the alternative is a preset that lingers in the menu after it is gone.
            if (imported.Any(IsLookAsset) || moved.Any(IsLookAsset)
                || deleted.Any(IsAssetFile) || movedFrom.Any(IsAssetFile))
            {
                // Rebuilt inline rather than on delayCall: the import is finished by the time this runs, so
                // the asset database is queryable, and the menu is correct the moment the callback returns.
                LookPresetMenu.Rebuild();
            }
        }

        private static bool IsAssetFile(string path)
        {
            return path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase);
        }

        // Cheap path filter first: loading every imported asset just to type-check it would be wasteful.
        private static bool IsLookAsset(string path)
        {
            if (!IsAssetFile(path))
            {
                return false;
            }

            var type = AssetDatabase.GetMainAssetTypeAtPath(path);
            return type == typeof(EnvironmentLookPreset) || type == typeof(StageDefinition);
        }
    }
}
#endif
