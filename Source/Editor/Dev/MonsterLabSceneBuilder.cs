#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// 보스 레이드 확인 랩. <see cref="ObjectLabSceneBuilder"/>와 같은 레시피로
    /// <b>PrototypeTest 씬을 클론</b>해 실제 <see cref="MapCombatController"/> 배선(카탈로그·덱·맵 뷰·
    /// 보스 카탈로그 로딩)을 그대로 보존하고, 보드만 보스전에 필요한 최소 형태로 합성한다.
    ///
    /// 보드 형태가 이 랩의 핵심이다. 아레나만 있으면 <b>진입 트리거를 시험할 수 없다</b> —
    /// 결계는 플레이어가 아레나 밖에서 안으로 걸어 들어올 때 닫히므로, 밖에서 시작해 걸어 들어올
    /// 접근로가 반드시 있어야 한다. 그래서 판은 "접근로 + 결계 링 + 아레나"의 열쇠구멍 모양이다:
    ///
    ///   (-9,0)···(-6,0)  접근로 — 아레나 밖, 플레이어 시작점
    ///   거리 5           결계 링 — 봉인되면 막히는 셀(판에 전부 칠해 두어 담장이 온전히 보인다)
    ///   거리 0..4        아레나 — 보스가 서 있고 철조각이 뿌려지는 싸움터
    ///
    /// <b>위치가 <c>ObjectLabSceneBuilder</c>와 다른 이유</b>: 이 빌더는 보스 HUD 저작 도구
    /// (<c>BossHudPrefabBuilder</c>)를 재사용해야 하는데 그건 asmdef 없는 <c>Assets/Editor/</c> 아래,
    /// 즉 기본 <c>Assembly-CSharp-Editor</c>에 있다. 이름 있는 asmdef(<c>SeoulPlayup.Dev</c>)는 기본
    /// 어셈블리를 참조할 수 없고 방향이 반대라, 빌더를 기본 에디터 어셈블리에 두면 양쪽
    /// (<c>MonsterLabController</c>와 HUD 빌더)에 모두 닿는다.
    /// </summary>
    public static class MonsterLabSceneBuilder
    {
        private const string SourceScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";
        private const string LabScenePath = "Assets/Scenes/Dev/MonsterLab.unity";
        private const string LabMapAssetPath = "Assets/Settings/Combat/MonsterLabMap.asset";

        /// <summary>아레나 반경. 철조각 ringRadius=3보다 커야 살포 링이 아레나 안에 온전히 들어온다.</summary>
        private const int ArenaRadius = 4;

        /// <summary>판의 반경 = 아레나 + 결계 링 한 겹. 링을 전부 칠해야 담장이 끊기지 않고 보인다.</summary>
        private const int BoardRadius = ArenaRadius + 1;

        /// <summary>접근로 길이(링 바깥으로 뻗는 칸 수). 여러 번 이동해 들어오는 맛을 본다.</summary>
        private const int ApproachLength = 4;

        private const string BossSpawnId = "bosslab-boss-spawn";
        private const string BossMonsterId = "M002";
        private const string ArenaId = "bosslab-arena";
        private const string PlayerSpawnId = "bosslab-player";

        private static readonly HexCoord ArenaCenter = new HexCoord(0, 0);

        [MenuItem("Seoul Playup/Dev/Create Monster Lab Scene")]
        public static void CreateOrUpdateScene()
        {
            if (!File.Exists(SourceScenePath))
            {
                Debug.LogError($"[MonsterLab] Source scene not found: {SourceScenePath}");
                return;
            }

            if (File.Exists(LabScenePath))
            {
                AssetDatabase.DeleteAsset(LabScenePath);
            }

            if (!AssetDatabase.CopyAsset(SourceScenePath, LabScenePath))
            {
                Debug.LogError($"[MonsterLab] Failed to copy {SourceScenePath} -> {LabScenePath}");
                return;
            }

            AssetDatabase.Refresh();

            var scene = EditorSceneManager.OpenScene(LabScenePath, OpenSceneMode.Single);

            var controller = UnityEngine.Object.FindObjectsByType<MapCombatController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (controller == null)
            {
                Debug.LogError("[MonsterLab] Cloned scene has no MapCombatController; cannot wire the lab.");
                return;
            }

            DisableLegacyDebugPanel(controller);
            AttachLabController(controller);
            AddBossHud();
            if (!BuildBossBoard(controller))
            {
                return;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, LabScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[MonsterLab] Built boss lab scene at {LabScenePath} (cloned from {SourceScenePath}).");
        }

        // 클론 씬이 레거시 CombatDebugControlPanel을 자동 생성하므로 끈다 — 랩 패널이 단일 dev UI다
        // (ObjectLab·CombatTimingLab과 같은 다듬기).
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
            foreach (var existing in UnityEngine.Object.FindObjectsByType<MonsterLabController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
            }

            var labRoot = new GameObject("MonsterLab");
            var lab = labRoot.AddComponent<MonsterLabController>();
            var serialized = new SerializedObject(lab);
            serialized.FindProperty("controller").objectReferenceValue = controller;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 보스 HUD를 랩 씬에 넣는다. <b>PrototypeTest에는 보스 HUD가 없다</b> — P2에서 출하 씬
        /// (MainGameplay)에만 인스턴스를 붙였기 때문에, 클론만 하면 랩에서 보스바가 영영 안 뜬다.
        /// 저작 도구를 그대로 재사용하므로 프리팹이 갱신되면 랩도 같이 따라온다.
        /// </summary>
        private static void AddBossHud()
        {
            SeoulPlayup.EditorTools.UI.BossHudPrefabBuilder.AddToOpenScene();
        }

        private static bool BuildBossBoard(MapCombatController controller)
        {
            var serialized = new SerializedObject(controller);
            var sourceProp = serialized.FindProperty("sparseSource");
            var source = sourceProp != null ? sourceProp.objectReferenceValue as HexSparseMapAuthoringSource : null;
            if (source == null || source.CellCount == 0)
            {
                Debug.LogError("[MonsterLab] No sparse source to sample tile ids from; aborting.");
                return false;
            }

            // 타일/지형/아틀라스 id를 클론 원본에서 표본으로 뽑아, 랩 보드가 실제 판과 같은 룩으로 렌더된다.
            var sampleCell = source.Cells.FirstOrDefault(c => c != null && c.BaseWalkable && !string.IsNullOrEmpty(c.AtlasVisualId))
                ?? source.Cells.FirstOrDefault(c => c != null && c.BaseWalkable)
                ?? source.Cells.First(c => c != null);
            var samplePlayer = source.ObjectRefs.FirstOrDefault(o => o != null && o.IsPlayerSpawn);

            var boardCoords = BuildBoardCoords();
            var cells = boardCoords.Select(coord => new HexSparseMapAuthoringCell(
                coord,
                sampleCell.TilePresetId,
                sampleCell.TerrainTypeId,
                sampleCell.AtlasVisualId,
                eventId: null, landmarkId: null, heightLevel: 0, rotationSteps: 0,
                edgeConnectionMask: 0, baseWalkable: true)).ToList();

            var playerStart = new HexCoord(-(BoardRadius + ApproachLength), 0);
            var objectRefs = new List<HexMapObjectRef>
            {
                new HexMapObjectRef(
                    PlayerSpawnId,
                    HexMapObjectType.PlayerSpawn,
                    samplePlayer != null && !string.IsNullOrEmpty(samplePlayer.ObjectRef) ? samplePlayer.ObjectRef : "player",
                    playerStart.Q, playerStart.R,
                    role: samplePlayer != null ? samplePlayer.Role : "player_start",
                    enabledForPurpose: samplePlayer != null ? samplePlayer.EnabledForPurpose : HexMapPurpose.Unspecified),
                new HexMapObjectRef(
                    BossSpawnId,
                    HexMapObjectType.MonsterSpawn,
                    BossMonsterId,
                    ArenaCenter.Q, ArenaCenter.R,
                    role: MonsterSpawnRoles.Boss,
                    enabledForPurpose: samplePlayer != null ? samplePlayer.EnabledForPurpose : HexMapPurpose.Unspecified)
            };

            var arenaCoords = HexArea.CellsWithin(ArenaCenter, ArenaRadius).ToList();
            var areaRefs = new List<HexMapAreaAuthoringRef>
            {
                new HexMapAreaAuthoringRef(ArenaId, arenaCoords, HexMapAreaRef.BossArenaPurpose, BossSpawnId)
            };

            var map = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            map.ConfigureForTests(cells, source.BoardPurpose, objectRefs, null, null, areaRefs);

            // 저작 불변식을 여기서 즉시 확인한다: 랩 맵이 빌드되지 않으면 씬을 열어도 판이 안 뜨는데,
            // 그 원인을 플레이 모드에서 역추적하는 것보다 빌드 시점에 말해 주는 편이 싸다.
            if (!map.TryToHexMapData(out var built, out var error))
            {
                Debug.LogError($"[MonsterLab] Synthesized board failed to build: {error}");
                UnityEngine.Object.DestroyImmediate(map);
                return false;
            }

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

            var area = built.Areas.First(a => a.Id == ArenaId);
            var ring = area.EnumerateBoundaryRing().Count(built.Contains);
            Debug.Log($"[MonsterLab] Board: {cells.Count} cells · arena {area.Coords.Count} · ring {ring} · " +
                      $"player {playerStart} · boss {BossMonsterId} at {ArenaCenter} ({LabMapAssetPath}).");
            return true;
        }

        /// <summary>아레나+링 디스크 하나와, 거기서 서쪽으로 뻗는 1칸 폭 접근로.</summary>
        private static List<HexCoord> BuildBoardCoords()
        {
            var coords = new HashSet<HexCoord>(HexArea.CellsWithin(ArenaCenter, BoardRadius));
            for (var step = 1; step <= ApproachLength; step++)
            {
                coords.Add(new HexCoord(ArenaCenter.Q - BoardRadius - step, ArenaCenter.R));
            }

            return coords.OrderBy(c => c).ToList();
        }
    }
}
#endif
