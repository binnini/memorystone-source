using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using Unity.Profiling;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// The three authored brightness levels the mask paints, snapshotted once per rebuild.
    ///
    /// Passed as a value rather than resolved through the host per cell on purpose: the per-cell loop
    /// runs over every cell of the map on a turn-hot path, and an interface call per cell is exactly the
    /// kind of cost this refactor must not introduce. The authored values still live on the view.
    /// </summary>
    internal readonly struct VisibilityLightingLevels
    {
        public VisibilityLightingLevels(float unknown, float hinted, float revealed)
        {
            Unknown = unknown;
            Hinted = hinted;
            Revealed = revealed;
        }

        public float Unknown { get; }
        public float Hinted { get; }
        public float Revealed { get; }

        public float Resolve(HexCellVisibility visibility)
        {
            switch (visibility)
            {
                case HexCellVisibility.Revealed:
                    return Revealed;
                case HexCellVisibility.Hinted:
                    return Hinted;
                default:
                    return Unknown;
            }
        }
    }

    /// <summary>
    /// What <see cref="VisibilityLightingMaskService"/> needs from <see cref="AtlasTilePresentationView"/>.
    ///
    /// The split is authoring-and-projection on the view, mask-resource-lifetime in the service. The view
    /// owns the authored settings (all [SerializeField]), the hex projection and the per-pass visibility
    /// snapshot; the service owns the mask texture, the pixel buffers, the texel→slot LUT and the global
    /// shader upload.
    ///
    /// <see cref="MaskResolution"/> / <see cref="MaskBlurPasses"/> stay [SerializeField] on the view: they
    /// are authored in MainGameplay and ArtLookdev, and LookdevEnvSync copies them between scenes by
    /// *name* via SerializedObject.FindProperty — moving them would break a tool no compiler check covers.
    /// </summary>
    internal interface IVisibilityLightingMaskHost
    {
        Transform MapTransform { get; }

        /// <summary>Authored mask texture resolution, clamped to [64, 1024] by the service.</summary>
        int MaskResolution { get; }

        /// <summary>Authored box-blur pass count.</summary>
        int MaskBlurPasses { get; }

        /// <summary>Snap the shader's mask sample to the nearer authored level (vision edge as a line, #12).</summary>
        bool HardVisibilityEdge { get; }

        /// <summary>
        /// 경계 그라데이션 정도(2026-08-20 #1) — 0 = 완전 하드(스냅 100%), 1 = 스냅 없음(풀 그라데이션).
        /// <see cref="HardVisibilityEdge"/>가 켜져 있을 때만 의미가 있다: 셰이더에 올라가는 스냅 강도는
        /// hardEdge ? (1 − gradation) : 0.
        /// </summary>
        float VisibilityEdgeGradation { get; }

        /// <summary>
        /// 셰이더가 마스크를 육각 셀 중심에서 샘플한다(2026-08-20 #1 — 경계가 육각 타일 변을 따라간다).
        /// 끄면 옛 평면 UV 샘플(블러+bilinear가 만든 경계 모양)로 돌아간다 — A/B 캡처용.
        /// </summary>
        bool HexSnapVisibilityEdge { get; }

        float EmissionFloor { get; }

        VisibilityLightingLevels LightingLevels { get; }

        HexMapData Map { get; }

        /// <summary>
        /// This pass's per-cell visibility snapshot. Deliberately the concrete Dictionary type rather than
        /// IReadOnlyDictionary: the service foreaches it once per cell, and the interface form would box
        /// the enumerator on a turn-hot path.
        /// </summary>
        Dictionary<HexCoord, HexVisibilitySafeCellInfo> SafeInfoCache { get; }

        Vector3 Project(HexCoord coord);

        HexAxialProjection CreateProjection();

        void DestroyGeneratedAsset(UnityEngine.Object generatedAsset);
    }

    /// <summary>
    /// Builds and uploads the fog-of-war lighting mask: an R8 texture covering the map's world bounds
    /// whose texels carry per-cell brightness, sampled by the Visibility Lit shader variants.
    ///
    /// ⚠️ This is a measured hot path. `ApplyVisibility` spent 74% of its per-turn cost here before the
    /// cs:538 optimization pass took it from 8.0ms to 1.97ms, and three of that pass's mechanisms live in
    /// this class — keep them intact when editing:
    ///  • #6 texel→cell-slot LUT — the texel→cell mapping is a pure function of (fixed bounds, resolution),
    ///    so it is computed once and every rebuild only gathers per-slot bytes through it.
    ///  • delta skip — if no slot's byte changed since the last upload, fill+blur+upload are skipped
    ///    entirely. <see cref="contentValid"/> starts false so every invalidation forces a full rebuild.
    ///  • #3 resolution 128 (authored) rather than 256.
    ///
    /// Resource ownership: the mask texture and both pixel buffers are created and destroyed here and
    /// nowhere else. The texture is HideAndDontSave, so a missed <see cref="Destroy"/> leaks.
    /// </summary>
    internal sealed class VisibilityLightingMaskService
    {
        private static readonly int MaskTexturePropertyId = Shader.PropertyToID("_SP_VisibilityMask");
        private static readonly int MaskWorldToLocalPropertyId = Shader.PropertyToID("_SP_VisibilityWorldToLocal");
        private static readonly int MaskBoundsPropertyId = Shader.PropertyToID("_SP_VisibilityMaskBounds");
        private static readonly int MaskEnabledPropertyId = Shader.PropertyToID("_SP_VisibilityMaskEnabled");
        private static readonly int MaskEmissionFloorPropertyId = Shader.PropertyToID("_SP_VisibilityEmissionFloor");
        private static readonly int MaskHardEdgePropertyId = Shader.PropertyToID("_SP_VisibilityHardEdge");
        private static readonly int MaskLightingLowPropertyId = Shader.PropertyToID("_SP_VisibilityLightingLow");
        private static readonly int MaskLightingHighPropertyId = Shader.PropertyToID("_SP_VisibilityLightingHigh");
        private static readonly int MaskCellSnapPropertyId = Shader.PropertyToID("_SP_VisibilityCellSnap");
        private static readonly int MaskTileRadiusPropertyId = Shader.PropertyToID("_SP_VisibilityTileRadius");

        private static readonly ProfilerMarker FillMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.LightingMask.Fill");
        private static readonly ProfilerMarker BlurMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.LightingMask.Blur");
        private static readonly ProfilerMarker UploadMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.LightingMask.Upload");

        private readonly IVisibilityLightingMaskHost host;

        private Texture2D maskTexture;
        private byte[] maskPixels;
        private byte[] maskScratch;
        private Vector4 maskBounds;

        // The mask's world-space pixel bounds are a pure function of the fixed map, so cache them once
        // (whole-map Project loop) and recompute only when the map changes (Invalidate).
        private bool hasBounds;
        private float minX;
        private float minZ;
        private float maxX;
        private float maxZ;

        // #6 texel→cell LUT. Slot 0 is reserved for "no cell" = unknown. Invalidated together with the
        // bounds and on any mask-texture recreation (resolution change).
        private readonly Dictionary<HexCoord, int> coordToSlot = new Dictionary<HexCoord, int>();
        private int[] texelToSlot;
        private byte[] bySlot;
        private byte[] bySlotPrev;
        private bool hasLut;
        private int lutResolution;

        // False whenever the mask content must be fully rebuilt regardless of the per-slot delta (first
        // pass, LUT rebuild, texture recreation). Set true after a successful fill+blur+upload.
        private bool contentValid;

        public VisibilityLightingMaskService(IVisibilityLightingMaskHost host)
        {
            this.host = host;
        }

        /// <summary>The live mask texture, or null before the first successful rebuild.</summary>
        public Texture2D MaskTexture => maskTexture;

        /// <summary>Drops the cached world bounds so the next rebuild recomputes them. Call when the map changes.</summary>
        public void InvalidateBounds()
        {
            hasBounds = false;
        }

        public void Update()
        {
            var map = host.Map;
            var safeInfoCache = host.SafeInfoCache;
            if (map == null || safeInfoCache.Count == 0)
            {
                return;
            }

            if (!hasBounds && !TryCacheBounds(map))
            {
                return;
            }

            EnsureTexture();
            var resolution = maskTexture.width;

            // #6: the texel→cell mapping is a pure function of the fixed map bounds + resolution, so
            // precompute it once (WorldToCoord per texel done here, not every rebuild). Each rebuild
            // then only refreshes the per-cell lighting bytes and gathers them through the LUT.
            EnsureLut(resolution);

            // Refresh the per-slot lighting byte from this pass's safe-info snapshot (one byte write
            // per cell, ~4.5K vs 16K texels). Slot 0 is reserved for "no cell" = unknown lighting.
            var levels = host.LightingLevels;
            var unknownByte = LightingToByte(levels.Unknown);
            bySlot[0] = unknownByte;
            foreach (var pair in safeInfoCache)
            {
                if (!coordToSlot.TryGetValue(pair.Key, out var slot))
                {
                    continue;
                }

                var safeInfo = pair.Value;
                var lighting = safeInfo.Exists
                    ? levels.Resolve(safeInfo.Visibility)
                    : levels.Unknown;
                bySlot[slot] = LightingToByte(lighting);
            }

            // #6 delta skip: if no slot's lighting changed since the last upload, the mask is already
            // current — skip fill+blur+upload. contentValid starts false (first pass and every
            // invalidation force a full rebuild — pitfall #2).
            var lightingChanged = !contentValid;
            if (!lightingChanged)
            {
                for (var slot = 0; slot < bySlot.Length; slot++)
                {
                    if (bySlot[slot] != bySlotPrev[slot])
                    {
                        lightingChanged = true;
                        break;
                    }
                }
            }

            if (lightingChanged)
            {
                FillMarker.Begin();
                var texels = texelToSlot;
                var slots = bySlot;
                var texelCount = resolution * resolution;
                for (var i = 0; i < texelCount; i++)
                {
                    maskPixels[i] = slots[texels[i]];
                }

                FillMarker.End();

                BlurMarker.Begin();
                var pixelsToUpload = maskPixels;
                var blurDestination = maskScratch;
                for (var pass = 0; pass < host.MaskBlurPasses; pass++)
                {
                    BoxBlur(pixelsToUpload, blurDestination, resolution);
                    var swap = pixelsToUpload;
                    pixelsToUpload = blurDestination;
                    blurDestination = swap;
                }

                BlurMarker.End();

                UploadMarker.Begin();
                maskTexture.SetPixelData(pixelsToUpload, 0);
                maskTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                UploadMarker.End();

                System.Array.Copy(bySlot, bySlotPrev, bySlot.Length);
                contentValid = true;
            }

            Shader.SetGlobalTexture(MaskTexturePropertyId, maskTexture);
            Shader.SetGlobalMatrix(MaskWorldToLocalPropertyId, host.MapTransform.worldToLocalMatrix);
            Shader.SetGlobalVector(MaskBoundsPropertyId, maskBounds);
            Shader.SetGlobalFloat(MaskEnabledPropertyId, 1f);
            Shader.SetGlobalFloat(MaskEmissionFloorPropertyId, Mathf.Clamp01(host.EmissionFloor));
            // Hard-edge snap targets (#12). Low is the darker of Unknown/Hinted (they ship equal); the
            // shader snaps each sample to whichever target is nearer, killing the blur/bilinear gradient
            // without touching the CPU fill path (this method stays byte-for-byte identical above).
            // 2026-08-20 #1: 스냅 강도 = 1 − 그라데이션 정도(연속 손잡이) — 셰이더의 lerp가 원래
            // 연속이었으니 bool 대신 강도를 그대로 올린다.
            Shader.SetGlobalFloat(
                MaskHardEdgePropertyId,
                host.HardVisibilityEdge ? Mathf.Clamp01(1f - host.VisibilityEdgeGradation) : 0f);
            Shader.SetGlobalFloat(MaskLightingLowPropertyId, Mathf.Clamp01(Mathf.Min(levels.Unknown, levels.Hinted)));
            Shader.SetGlobalFloat(MaskLightingHighPropertyId, Mathf.Clamp01(levels.Revealed));
            // 육각 셀 스냅(#1): 셰이더가 프래그먼트의 셀 중심에서 샘플하면 경계가 구성상 육각 변이
            // 된다 — CPU 경로는 그대로라 성능 게이트에 중립. 반경은 LUT과 같은 projection의 것.
            Shader.SetGlobalFloat(MaskCellSnapPropertyId, host.HexSnapVisibilityEdge ? 1f : 0f);
            Shader.SetGlobalFloat(MaskTileRadiusPropertyId, host.CreateProjection().TileRadius);
        }

        // The map is fixed after Render, so the mask's world-space pixel bounds are constant — compute
        // them once (whole-map Project loop) and reuse until the map changes (InvalidateBounds).
        private bool TryCacheBounds(HexMapData map)
        {
            var boundsMinX = float.PositiveInfinity;
            var boundsMinZ = float.PositiveInfinity;
            var boundsMaxX = float.NegativeInfinity;
            var boundsMaxZ = float.NegativeInfinity;
            // Assign each cell a stable 1-based lighting slot while we already iterate the whole map.
            // Slot 0 stays reserved for "no cell" = unknown lighting. The texel→slot LUT rebuilds off
            // these slots (hasLut reset below).
            coordToSlot.Clear();
            var nextSlot = 1;
            foreach (var cell in map.AllCells)
            {
                var position = host.Project(cell.Coord);
                boundsMinX = Mathf.Min(boundsMinX, position.x);
                boundsMinZ = Mathf.Min(boundsMinZ, position.z);
                boundsMaxX = Mathf.Max(boundsMaxX, position.x);
                boundsMaxZ = Mathf.Max(boundsMaxZ, position.z);
                if (!coordToSlot.ContainsKey(cell.Coord))
                {
                    coordToSlot[cell.Coord] = nextSlot++;
                }
            }

            if (float.IsInfinity(boundsMinX))
            {
                return false;
            }

            var margin = host.CreateProjection().TileRadius * 1.05f;
            boundsMinX -= margin;
            boundsMinZ -= margin;
            boundsMaxX += margin;
            boundsMaxZ += margin;
            minX = boundsMinX;
            minZ = boundsMinZ;
            maxX = boundsMaxX;
            maxZ = boundsMaxZ;
            var width = Mathf.Max(0.01f, maxX - minX);
            var depth = Mathf.Max(0.01f, maxZ - minZ);
            maskBounds = new Vector4(minX, minZ, 1f / width, 1f / depth);
            hasBounds = true;
            // Slots changed → the texel→slot LUT and per-slot buffers must rebuild before next fill.
            hasLut = false;
            contentValid = false;
            return true;
        }

        // #6: build (or rebuild) the texel→cell-slot LUT for the current bounds + resolution. This runs
        // the WorldToCoord + coord→slot resolution once per texel; every subsequent rebuild only gathers
        // the per-slot lighting bytes through it. Kept in lockstep with the mask texture's resolution.
        private void EnsureLut(int resolution)
        {
            if (hasLut && lutResolution == resolution &&
                texelToSlot != null &&
                texelToSlot.Length == resolution * resolution)
            {
                return;
            }

            var texelCount = resolution * resolution;
            if (texelToSlot == null || texelToSlot.Length != texelCount)
            {
                texelToSlot = new int[texelCount];
            }

            var slotCount = coordToSlot.Count + 1; // + slot 0 (no cell / unknown)
            if (bySlot == null || bySlot.Length != slotCount)
            {
                bySlot = new byte[slotCount];
                bySlotPrev = new byte[slotCount];
            }

            var projection = host.CreateProjection();
            for (var y = 0; y < resolution; y++)
            {
                var localZ = Mathf.Lerp(minZ, maxZ, (y + 0.5f) / resolution);
                for (var x = 0; x < resolution; x++)
                {
                    var localX = Mathf.Lerp(minX, maxX, (x + 0.5f) / resolution);
                    var coord = projection.WorldToCoord(localX, localZ);
                    texelToSlot[y * resolution + x] =
                        coordToSlot.TryGetValue(coord, out var slot) ? slot : 0;
                }
            }

            hasLut = true;
            lutResolution = resolution;
            // New LUT / resized buffers → next pass must fully rebuild regardless of the delta.
            contentValid = false;
        }

        private static byte LightingToByte(float lighting)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(lighting) * 255f);
        }

        private void EnsureTexture()
        {
            var resolution = Mathf.Clamp(host.MaskResolution, 64, 1024);
            if (maskTexture != null && maskTexture.width == resolution)
            {
                return;
            }

            Destroy();
            maskTexture = new Texture2D(resolution, resolution, TextureFormat.R8, mipChain: false, linear: true)
            {
                name = "Atlas Visibility Lighting Mask",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            maskPixels = new byte[resolution * resolution];
            maskScratch = new byte[resolution * resolution];
        }

        public void Destroy()
        {
            if (maskTexture != null)
            {
                host.DestroyGeneratedAsset(maskTexture);
                maskTexture = null;
            }

            maskPixels = null;
            maskScratch = null;
            // The LUT is bound to a specific texture resolution; invalidate it so it rebuilds against
            // the next texture (resolution change) and the next fill is treated as fully dirty.
            hasLut = false;
            contentValid = false;
        }

        private static void BoxBlur(byte[] source, byte[] destination, int resolution)
        {
            for (var y = 0; y < resolution; y++)
            {
                for (var x = 0; x < resolution; x++)
                {
                    var sum = 0;
                    var count = 0;
                    for (var offsetY = -1; offsetY <= 1; offsetY++)
                    {
                        var sampleY = Mathf.Clamp(y + offsetY, 0, resolution - 1);
                        for (var offsetX = -1; offsetX <= 1; offsetX++)
                        {
                            var sampleX = Mathf.Clamp(x + offsetX, 0, resolution - 1);
                            sum += source[sampleY * resolution + sampleX];
                            count++;
                        }
                    }

                    destination[y * resolution + x] = (byte)(sum / count);
                }
            }
        }

        /// <summary>Bilinear mask sample at a cell, for the EditMode visibility-lighting tests.</summary>
        public float SampleForTests(HexCoord coord)
        {
            if (maskTexture == null)
            {
                return 1f;
            }

            var position = host.Project(coord);
            var uv = new Vector2(
                (position.x - maskBounds.x) * maskBounds.z,
                (position.z - maskBounds.y) * maskBounds.w);
            return maskTexture.GetPixelBilinear(uv.x, uv.y).r;
        }
    }
}
