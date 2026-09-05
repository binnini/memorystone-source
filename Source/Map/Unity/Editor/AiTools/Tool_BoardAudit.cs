#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_BoardAudit
    {
        [AiTool
        (
            "board-chunk-audit",
            Title = "Board / Chunk Audit",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("Active 씬의 AtlasTilePresentationView가 참조하는 AtlasTileCatalog top 프리팹의 " +
            "top-chunk 배칭 적격성(tile- 접두사·단일 메시·콜라이더 유무·여분 컴포넌트)을 감사한다. " +
            "게임/씬 상태를 바꾸지 않는 읽기 전용 감사. mapSourceAssetPath를 주면 그 맵 소스에 실제 배치된 " +
            "AtlasVisualId만 감사(legacy 미배치 tile 오탐 제거); 비우면 카탈로그 전체를 감사한다.")]
        public BoardChunkAuditResult ChunkAudit
        (
            [Description("적격으로 볼 AtlasVisualId 접두사. 기본 'tile-'.")]
            string tilePrefix = "tile-",
            [Description("실배치 필터용 HexSparseMapAuthoringSource 에셋 경로. 비우면 카탈로그 전체 감사. " +
                "예: 'Assets/Data/Map/Authoring/EastSeoulSource.asset'.")]
            string mapSourceAssetPath = ""
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var prefix = string.IsNullOrEmpty(tilePrefix) ? "tile-" : tilePrefix;
                var result = new BoardChunkAuditResult();

                // Optional placed-only filter: restrict the audit to AtlasVisualIds actually placed on a
                // shipping map source, so orphaned legacy catalog entries (e.g. unplaced mvp- tiles) don't
                // register as false-alarm ineligibles.
                HashSet<string>? placedVisualIds = null;
                if (!string.IsNullOrWhiteSpace(mapSourceAssetPath))
                {
                    var mapSource = AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(mapSourceAssetPath);
                    if (mapSource == null)
                    {
                        throw new InvalidOperationException(
                            $"No HexSparseMapAuthoringSource asset at '{mapSourceAssetPath}'.");
                    }

                    placedVisualIds = new HashSet<string>(
                        mapSource.Cells
                            .Select(cell => cell.AtlasVisualId)
                            .Where(id => !string.IsNullOrWhiteSpace(id)),
                        StringComparer.OrdinalIgnoreCase);
                    result.placedFilterSource = mapSourceAssetPath;
                    result.placedTileCount = placedVisualIds.Count;
                }

                var view = UnityEngine.Object
                    .FindObjectsByType<AtlasTilePresentationView>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .FirstOrDefault();
                if (view == null)
                {
                    throw new InvalidOperationException(
                        "No AtlasTilePresentationView found in the active scene. Open a map scene " +
                        "(e.g. Assets/Scenes/Game/MainGameplay.unity) before running board-chunk-audit.");
                }

                // Live view settings: useTopChunkMeshes / topChunkMaxCells are [SerializeField] private with
                // no getter, so read them through SerializedObject (Editor-only). VisibilityChunkRendererCount
                // is a public runtime counter (0 until a play-mode visibility pass populates it).
                using (var serialized = new SerializedObject(view))
                {
                    var useTopChunkProp = serialized.FindProperty("useTopChunkMeshes");
                    var maxCellsProp = serialized.FindProperty("topChunkMaxCells");
                    result.useTopChunkMeshes = useTopChunkProp != null && useTopChunkProp.boolValue;
                    result.topChunkMaxCells = maxCellsProp != null ? maxCellsProp.intValue : 0;
                }

                result.chunkRendererCount = view.VisibilityChunkRendererCount;

                var catalog = view.Catalog;
                if (catalog == null)
                {
                    throw new InvalidOperationException(
                        "AtlasTilePresentationView has no AtlasTileCatalog assigned; cannot audit tile prefabs.");
                }

                foreach (var entry in catalog.Entries)
                {
                    if (entry == null || entry.TopPrefab == null)
                    {
                        continue;
                    }

                    // Placed-only filter: skip catalog entries not placed on the selected map source.
                    if (placedVisualIds != null &&
                        (string.IsNullOrWhiteSpace(entry.AtlasVisualId) || !placedVisualIds.Contains(entry.AtlasVisualId)))
                    {
                        result.unplacedSkipped++;
                        continue;
                    }

                    result.totalTiles++;
                    var prefab = entry.TopPrefab;
                    var id = string.IsNullOrWhiteSpace(entry.AtlasVisualId) ? prefab.name : entry.AtlasVisualId;
                    var reasons = new List<string>();

                    // Rule 1 — AtlasVisualId prefix (mirrors AtlasTilePresentationView.IsChunkEligibleAtlasVisualId).
                    var hasPrefix = !string.IsNullOrWhiteSpace(entry.AtlasVisualId) &&
                                    entry.AtlasVisualId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                    if (!hasPrefix)
                    {
                        result.noPrefixCount++;
                        reasons.Add($"AtlasVisualId lacks '{prefix}' prefix");
                    }

                    // Rule 2 — no colliders anywhere in the hierarchy (mirrors TopChunkDescriptor.TryCreate).
                    var colliders = prefab.GetComponentsInChildren<Collider>(true).Length;
                    result.colliderCount += colliders;
                    if (colliders > 0)
                    {
                        result.hasColliderCount++;
                        reasons.Add($"{colliders} collider(s) in hierarchy");
                    }

                    // Rule 3 — exactly one MeshRenderer and one MeshFilter.
                    var meshRenderers = prefab.GetComponentsInChildren<MeshRenderer>(true).Length;
                    var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true).Length;
                    if (meshRenderers != 1 || meshFilters != 1)
                    {
                        result.multiMeshCount++;
                        reasons.Add($"not single-mesh (renderers={meshRenderers}, filters={meshFilters}, need 1/1)");
                    }

                    // Rule 4 — no components other than Transform / MeshFilter / MeshRenderer.
                    var hasExtra = prefab.GetComponentsInChildren<UnityEngine.Component>(true).Any(component =>
                        component != null && !(component is Transform) &&
                        !(component is MeshFilter) && !(component is MeshRenderer));
                    if (hasExtra)
                    {
                        reasons.Add("has component(s) beyond Transform/MeshFilter/MeshRenderer");
                    }

                    if (reasons.Count == 0)
                    {
                        result.eligible++;
                    }
                    else
                    {
                        result.ineligible.Add($"{id}: {string.Join("; ", reasons)}");
                    }
                }

                return result;
            });
        }
    }
}
