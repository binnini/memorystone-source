#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Combat.Unity.Dev;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// touch: force AssetDatabase re-registration (per-file compile glitch workaround).
namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Builds the Combat Timing Lab scene by cloning the proven PrototypeTest scene (so the real
    /// MapCombatController + CombatState wiring — ~100 serialized fields, map authoring, deck, catalogs —
    /// is preserved exactly) and attaching a <see cref="CombatTimingLabController"/> panel on top.
    ///
    /// Rationale (decided 2026-06-23): re-deriving the full combat scene from scratch (VfxAnimationLab-style)
    /// would risk subtle divergence from live combat; cloning guarantees an identical turn system with zero
    /// re-wiring. The clone is then lightly trimmed for a focused tuning sandbox.
    /// </summary>
    public static class CombatTimingLabSceneBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";
        private const string LabScenePath = "Assets/Scenes/Dev/CombatTimingLab.unity";
        private const string LabMapAssetPath = "Assets/Settings/Combat/CombatTimingLabMap.asset";

        // Small flat tuning pad: a hex disk of this radius around (0,0). Big enough for movement-timing
        // tests, small enough to keep the lab focused. Player spawns at (0,0), one monster at (2,0) —
        // matching MapCombatController's playerStart/enemyStart defaults so no field rewiring is needed.
        private const int LabMapRadius = 3;

        [MenuItem("Seoul Playup/Dev/Create Combat Timing Lab Scene")]
        public static void CreateOrUpdateScene()
        {
            if (!File.Exists(SourceScenePath))
            {
                Debug.LogError($"[CombatTimingLab] Source scene not found: {SourceScenePath}");
                return;
            }

            // Clone the source scene asset, overwriting any previous lab build so this is idempotent.
            if (File.Exists(LabScenePath))
            {
                AssetDatabase.DeleteAsset(LabScenePath);
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, LabScenePath))
            {
                Debug.LogError($"[CombatTimingLab] Failed to copy {SourceScenePath} -> {LabScenePath}");
                return;
            }

            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(LabScenePath, OpenSceneMode.Single);

            var controller = UnityEngine.Object.FindObjectOfType<MapCombatController>();
            if (controller == null)
            {
                Debug.LogError("[CombatTimingLab] Cloned scene has no MapCombatController; cannot wire the lab.");
                return;
            }

            DisableLegacyDebugPanel(controller);
            AttachLabController(controller);
            ShrinkToSmallTestMap(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, LabScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[CombatTimingLab] Built timing lab scene at {LabScenePath} (cloned from {SourceScenePath}).");
        }

        // The cloned scene may auto-create the legacy CombatDebugControlPanel; suppress it so the lab panel
        // is the single timing UI. Field is private+serialized, so route through SerializedObject.
        private static void DisableLegacyDebugPanel(MapCombatController controller)
        {
            var serialized = new SerializedObject(controller);
            var autoCreate = serialized.FindProperty("autoCreateDebugControlPanel");
            if (autoCreate != null)
            {
                autoCreate.boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            foreach (var existing in UnityEngine.Object.FindObjectsOfType<CombatDebugControlPanel>(true))
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }
        }

        private static void AttachLabController(MapCombatController controller)
        {
            foreach (var existing in UnityEngine.Object.FindObjectsOfType<CombatTimingLabController>(true))
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            var labRoot = new GameObject("CombatTimingLab");
            var lab = labRoot.AddComponent<CombatTimingLabController>();
            var serialized = new SerializedObject(lab);
            serialized.FindProperty("controller").objectReferenceValue = controller;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // Replace the cloned full PrototypeTest board with a small flat tuning pad. We synthesize a fresh
        // HexSparseMapAuthoringSource (a hex disk) and reuse valid tile/terrain/atlas ids + a real monster id
        // + the board purpose sampled from the cloned source, so the small map renders and spawns exactly like
        // the real one — just without traps, treasure, objectives or extra monsters.
        private static void ShrinkToSmallTestMap(MapCombatController controller)
        {
            var serialized = new SerializedObject(controller);
            var sourceProp = serialized.FindProperty("sparseSource");
            var source = sourceProp != null ? sourceProp.objectReferenceValue as HexSparseMapAuthoringSource : null;
            if (source == null || source.CellCount == 0)
            {
                Debug.LogWarning("[CombatTimingLab] No sparse source to sample; keeping the cloned full map.");
                return;
            }

            var sampleCell = source.Cells.FirstOrDefault(c => c != null && c.BaseWalkable && !string.IsNullOrEmpty(c.AtlasVisualId))
                ?? source.Cells.FirstOrDefault(c => c != null && c.BaseWalkable)
                ?? source.Cells.First(c => c != null);
            var sampleMonster = source.ObjectRefs.FirstOrDefault(o => o != null && o.IsMonsterSpawn);
            var samplePlayer = source.ObjectRefs.FirstOrDefault(o => o != null && o.IsPlayerSpawn);

            var cells = BuildDiskCells(sampleCell);
            var objectRefs = new List<HexMapObjectRef>
            {
                new HexMapObjectRef(
                    samplePlayer != null && !string.IsNullOrEmpty(samplePlayer.ObjectId) ? samplePlayer.ObjectId : "timinglab-player",
                    HexMapObjectType.PlayerSpawn,
                    samplePlayer != null && !string.IsNullOrEmpty(samplePlayer.ObjectRef) ? samplePlayer.ObjectRef : "player",
                    0, 0,
                    role: samplePlayer != null ? samplePlayer.Role : "player_start",
                    enabledForPurpose: samplePlayer != null ? samplePlayer.EnabledForPurpose : HexMapPurpose.Unspecified),
                new HexMapObjectRef(
                    "timinglab-monster",
                    HexMapObjectType.MonsterSpawn,
                    sampleMonster != null && !string.IsNullOrEmpty(sampleMonster.ObjectRef) ? sampleMonster.ObjectRef : "M001",
                    2, 0,
                    role: sampleMonster != null ? sampleMonster.Role : "primary_pressure",
                    enabledForPurpose: sampleMonster != null ? sampleMonster.EnabledForPurpose : source.BoardPurpose,
                    patrolAreaId: string.Empty)
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
            Debug.Log($"[CombatTimingLab] Shrank board to a radius-{LabMapRadius} pad ({cells.Count} cells, 1 monster '{objectRefs[1].ObjectRef}') at {LabMapAssetPath}.");
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
