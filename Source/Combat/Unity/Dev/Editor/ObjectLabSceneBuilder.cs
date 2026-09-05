#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Builds the Object Lab scene by cloning the proven PrototypeTest scene — the same recipe as
    /// <see cref="CombatTimingLabSceneBuilder"/> and for the same reason: the real MapCombatController
    /// wiring (catalogs, deck, map view) is preserved exactly, so events triggered in the lab run the
    /// production code path. The clone is then trimmed to a small flat pad with a player and NO
    /// monsters — objects are the subject here, and a wandering monster would interrupt the loop.
    /// </summary>
    public static class ObjectLabSceneBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";
        private const string LabScenePath = "Assets/Scenes/Dev/ObjectLab.unity";
        private const string LabMapAssetPath = "Assets/Settings/Combat/ObjectLabMap.asset";

        // Radius-4 disk: room to look at big multi-cell buildings while staying one move away from the
        // object spawn cell (1,0). Player spawns at (0,0).
        private const int LabMapRadius = 4;

        [MenuItem("Seoul Playup/Dev/Create Object Lab Scene")]
        public static void CreateOrUpdateScene()
        {
            if (!File.Exists(SourceScenePath))
            {
                Debug.LogError($"[ObjectLab] Source scene not found: {SourceScenePath}");
                return;
            }

            if (File.Exists(LabScenePath))
            {
                AssetDatabase.DeleteAsset(LabScenePath);
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, LabScenePath))
            {
                Debug.LogError($"[ObjectLab] Failed to copy {SourceScenePath} -> {LabScenePath}");
                return;
            }

            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(LabScenePath, OpenSceneMode.Single);

            var controller = UnityEngine.Object.FindObjectsByType<MapCombatController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (controller == null)
            {
                Debug.LogError("[ObjectLab] Cloned scene has no MapCombatController; cannot wire the lab.");
                return;
            }

            DisableLegacyDebugPanel(controller);
            AttachLabController(controller);
            ShrinkToObjectPad(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, LabScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[ObjectLab] Built object lab scene at {LabScenePath} (cloned from {SourceScenePath}).");
        }

        // The cloned scene may auto-create the legacy CombatDebugControlPanel; suppress it so the lab
        // panel is the single dev UI (same trim as the timing lab).
        private static void DisableLegacyDebugPanel(MapCombatController controller)
        {
            var serialized = new SerializedObject(controller);
            var autoCreate = serialized.FindProperty("autoCreateDebugControlPanel");
            if (autoCreate != null)
            {
                autoCreate.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (var existing in UnityEngine.Object.FindObjectsByType<CombatDebugControlPanel>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static void AttachLabController(MapCombatController controller)
        {
            foreach (var existing in UnityEngine.Object.FindObjectsByType<ObjectLabController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            var labRoot = new GameObject("ObjectLab");
            var lab = labRoot.AddComponent<ObjectLabController>();
            var serialized = new SerializedObject(lab);
            serialized.FindProperty("controller").objectReferenceValue = controller;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Same synthesis as the timing lab's pad, minus the monster: sample real tile/terrain/atlas ids
        // and the board purpose from the cloned source so the pad renders like the real board.
        private static void ShrinkToObjectPad(MapCombatController controller)
        {
            var serialized = new SerializedObject(controller);
            var sourceProp = serialized.FindProperty("sparseSource");
            var source = sourceProp != null ? sourceProp.objectReferenceValue as HexSparseMapAuthoringSource : null;
            if (source == null || source.CellCount == 0)
            {
                Debug.LogWarning("[ObjectLab] No sparse source to sample; keeping the cloned full map.");
                return;
            }

            var sampleCell = source.Cells.FirstOrDefault(c => c != null && c.BaseWalkable && !string.IsNullOrEmpty(c.AtlasVisualId))
                ?? source.Cells.FirstOrDefault(c => c != null && c.BaseWalkable)
                ?? source.Cells.First(c => c != null);
            var samplePlayer = source.ObjectRefs.FirstOrDefault(o => o != null && o.IsPlayerSpawn);

            var cells = BuildDiskCells(sampleCell);
            var objectRefs = new List<HexMapObjectRef>
            {
                new HexMapObjectRef(
                    samplePlayer != null && !string.IsNullOrEmpty(samplePlayer.ObjectId) ? samplePlayer.ObjectId : "objectlab-player",
                    HexMapObjectType.PlayerSpawn,
                    samplePlayer != null && !string.IsNullOrEmpty(samplePlayer.ObjectRef) ? samplePlayer.ObjectRef : "player",
                    0, 0,
                    role: samplePlayer != null ? samplePlayer.Role : "player_start",
                    enabledForPurpose: samplePlayer != null ? samplePlayer.EnabledForPurpose : HexMapPurpose.Unspecified)
            };

            var map = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            map.ConfigureForTests(cells, source.BoardPurpose, objectRefs, null, null);

            var folder = Path.GetDirectoryName(LabMapAssetPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(folder) && !AssetDatabase.IsValidFolder(folder))
            {
                Directory.CreateDirectory(folder);
            }

            if (File.Exists(LabMapAssetPath))
            {
                AssetDatabase.DeleteAsset(LabMapAssetPath);
            }

            AssetDatabase.CreateAsset(map, LabMapAssetPath);
            AssetDatabase.SaveAssets();

            sourceProp.objectReferenceValue = map;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"[ObjectLab] Shrank board to a radius-{LabMapRadius} pad ({cells.Count} cells, no monsters) at {LabMapAssetPath}.");
        }

        private static List<HexSparseMapAuthoringCell> BuildDiskCells(HexSparseMapAuthoringCell sample)
        {
            var tilePreset = sample != null ? sample.TilePresetId : string.Empty;
            var terrain = sample != null ? sample.TerrainTypeId : string.Empty;
            var atlas = sample != null ? sample.AtlasVisualId : string.Empty;

            var cells = new List<HexSparseMapAuthoringCell>();
            for (var q = -LabMapRadius; q <= LabMapRadius; q++)
            {
                var rLo = Math.Max(-LabMapRadius, -q - LabMapRadius);
                var rHi = Math.Min(LabMapRadius, -q + LabMapRadius);
                for (var r = rLo; r <= rHi; r++)
                {
                    cells.Add(new HexSparseMapAuthoringCell(
                        new HexCoord(q, r), tilePreset, terrain, atlas,
                        eventId: null, landmarkId: null, heightLevel: 0, rotationSteps: 0,
                        edgeConnectionMask: 0, baseWalkable: true));
                }
            }

            return cells;
        }
    }
}
#endif
