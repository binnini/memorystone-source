#if UNITY_EDITOR
using System.Linq;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Designer convenience: add a hand-authored prop light to a prop prefab. Creates a child
    /// "PropLight" GameObject with an unshadowed point <see cref="Light"/> at the prop's base (neutral
    /// warm starting values) plus a <see cref="PropLight"/> marker. The designer then art-directs the
    /// Light's color / intensity / range directly, per prop and per placed instance in the scene —
    /// there is no shared profile.
    /// </summary>
    public static class PropLightEditorMenu
    {
        [MenuItem("Seoul Playup/Dev/Add Prop Light")]
        public static void AddPropLight()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                AddChildLight(stage.prefabContentsRoot, select: true);
                EditorSceneManager.MarkSceneDirty(stage.scene);
                Debug.Log($"[PropLight] Added a PropLight to open prefab '{stage.prefabContentsRoot.name}'. Tune it, then Ctrl+S.");
                return;
            }

            var paths = Selection.objects
                .Select(AssetDatabase.GetAssetPath)
                .Where(p => !string.IsNullOrEmpty(p) && p.EndsWith(".prefab"))
                .Distinct()
                .ToList();
            if (paths.Count == 0)
            {
                Debug.LogWarning("[PropLight] Open a prefab in Prefab Mode, or select prop prefab(s) in the Project window, then run this again.");
                return;
            }

            foreach (var path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                AddChildLight(root, select: false);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                PrefabUtility.UnloadPrefabContents(root);
                Debug.Log($"[PropLight] Added a PropLight to {path}");
            }

            AssetDatabase.SaveAssets();
        }

        private static void AddChildLight(GameObject root, bool select)
        {
            var existing = root.transform.Find("PropLight");
            if (existing != null)
            {
                Object.DestroyImmediate(existing.gameObject);
            }

            var go = new GameObject("PropLight");
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = Vector3.zero;

            // Base-centre, slightly up. World position (not local) dodges the prefab preview-scene offset.
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                go.transform.position = new Vector3(bounds.center.x, bounds.min.y + 1.3f, bounds.center.z);
            }

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.shadows = LightShadows.None;
            // Neutral warm starting point — the designer art-directs from here, per prop.
            light.color = new Color(1f, 0.7f, 0.4f);
            light.intensity = 4f;
            light.range = 6f;

            go.AddComponent<PropLight>();

            if (select)
            {
                Selection.activeGameObject = go;
            }
        }
    }
}
#endif
