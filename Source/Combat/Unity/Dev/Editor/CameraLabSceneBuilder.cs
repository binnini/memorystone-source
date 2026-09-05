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

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Builds the Camera Lab scene by cloning PrototypeTest — the same rationale as
    /// <see cref="CombatTimingLabSceneBuilder"/>: re-deriving the combat scene from scratch risks silent
    /// divergence from live combat, and for camera work that divergence would be in the very thing under
    /// test (the rig's follow offset, FOV, zoom bounds and damping all come from the cloned wiring).
    ///
    /// The one deliberate difference from the timing lab: the board is made *large*, not small. Framing is a
    /// question about distance, so a radius-3 pad — fine for attack timing — cannot produce a single
    /// off-screen event. The rig here needs actors 12+ hexes out.
    ///
    /// Also unlike the timing lab, the stock CombatDebugControlPanel is left in place: camera tuning leans on
    /// its slow-motion playback and fog toggles, and the lab panel docks to the opposite side of the screen.
    /// </summary>
    public static class CameraLabSceneBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";
        private const string LabScenePath = "Assets/Scenes/Dev/CameraLab.unity";
        private const string LabMapAssetPath = "Assets/Settings/Combat/CameraLabMap.asset";

        // A hex disk of this radius around (0,0). Sized from the measured rig geometry: the screen reaches
        // ~5.45 hexes to each side and ~12.5 ahead (§2.1), so a radius-14 board guarantees room for actors
        // that are genuinely off-screen in every direction, with walkable ground for them to path over.
        private const int LabMapRadius = 14;

        [MenuItem("Seoul Playup/Dev/Create Camera Lab Scene")]
        public static void CreateOrUpdateScene()
        {
            if (!File.Exists(SourceScenePath))
            {
                Debug.LogError($"[CameraLab] Source scene not found: {SourceScenePath}");
                return;
            }

            // Overwrite any previous build so the menu item is idempotent.
            if (File.Exists(LabScenePath))
            {
                AssetDatabase.DeleteAsset(LabScenePath);
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, LabScenePath))
            {
                Debug.LogError($"[CameraLab] Failed to copy {SourceScenePath} -> {LabScenePath}");
                return;
            }

            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(LabScenePath, OpenSceneMode.Single);

            var controller = UnityEngine.Object.FindObjectOfType<MapCombatController>();
            if (controller == null)
            {
                Debug.LogError("[CameraLab] Cloned scene has no MapCombatController; cannot wire the lab.");
                return;
            }

            AttachLabController(controller);
            var cellCount = ExpandToWideCameraBoard(controller);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, LabScenePath);
            AssetDatabase.Refresh();
            Debug.Log(
                $"[CameraLab] Built camera lab scene at {LabScenePath} "
                + $"(cloned from {SourceScenePath}, radius-{LabMapRadius} board, {cellCount} cells).");
        }

        private static void AttachLabController(MapCombatController controller)
        {
            foreach (var existing in UnityEngine.Object.FindObjectsOfType<CameraLabController>(true))
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            var labRoot = new GameObject("CameraLab");
            var lab = labRoot.AddComponent<CameraLabController>();
            var serialized = new SerializedObject(lab);
            serialized.FindProperty("controller").objectReferenceValue = controller;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Replaces the cloned board with a wide flat disk, sampling tile/terrain/atlas ids and the board
        /// purpose from the source so it renders and spawns exactly like a real map. Returns the cell count.
        /// </summary>
        private static int ExpandToWideCameraBoard(MapCombatController controller)
        {
            var serialized = new SerializedObject(controller);
            var sourceProp = serialized.FindProperty("sparseSource");
            var source = sourceProp != null ? sourceProp.objectReferenceValue as HexSparseMapAuthoringSource : null;
            if (source == null || source.CellCount == 0)
            {
                Debug.LogWarning("[CameraLab] No sparse source to sample; keeping the cloned board.");
                return 0;
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
                    samplePlayer != null && !string.IsNullOrEmpty(samplePlayer.ObjectId) ? samplePlayer.ObjectId : "cameralab-player",
                    HexMapObjectType.PlayerSpawn,
                    samplePlayer != null && !string.IsNullOrEmpty(samplePlayer.ObjectRef) ? samplePlayer.ObjectRef : "player",
                    0, 0,
                    role: samplePlayer != null ? samplePlayer.Role : "player_start",
                    enabledForPurpose: samplePlayer != null ? samplePlayer.EnabledForPurpose : HexMapPurpose.Unspecified),
                // One authored monster so the scene boots a valid combat; every other actor comes from the
                // lab's ring rig at runtime, which is what keeps the distances explicit and repeatable.
                new HexMapObjectRef(
                    "cameralab-monster",
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
            return cells.Count;
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
