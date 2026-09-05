#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `board-chunk-audit` MCP tool. One eligibility verdict per catalog
    // top prefab, plus the live AtlasTilePresentationView settings that drive top-chunk batching.
    public sealed class BoardChunkAuditResult
    {
        // Catalog top prefabs inspected (entries with a non-null TopPrefab).
        public int totalTiles;
        // Prefabs that pass every chunk-eligibility rule (would be merged into a top chunk).
        public int eligible;
        // "atlasVisualId: reason1; reason2" for each prefab that fails at least one rule.
        public List<string> ineligible = new();
        // Prefabs whose AtlasVisualId lacks the required prefix (e.g. "tile-").
        public int noPrefixCount;
        // Prefabs with at least one Collider anywhere in the hierarchy.
        public int hasColliderCount;
        // Prefabs that are not a single MeshRenderer + single MeshFilter.
        public int multiMeshCount;
        // Total Collider components summed across every inspected prefab hierarchy.
        public int colliderCount;
        // Live AtlasTilePresentationView.VisibilityChunkRendererCount (0 until a runtime visibility pass runs).
        public int chunkRendererCount;
        // Serialized AtlasTilePresentationView.topChunkMaxCells.
        public int topChunkMaxCells;
        // Serialized AtlasTilePresentationView.useTopChunkMeshes.
        public bool useTopChunkMeshes;

        // --- Placed-only filter (empty/0 when no map source filter was applied) ---
        // The HexSparseMapAuthoringSource asset path used to filter to placed AtlasVisualIds; empty when
        // the whole catalog was audited.
        public string placedFilterSource = string.Empty;
        // Distinct AtlasVisualIds placed on the filter map source.
        public int placedTileCount;
        // Catalog entries skipped because their AtlasVisualId is not placed on the filter map source.
        public int unplacedSkipped;
    }
}
