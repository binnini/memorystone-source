using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Map.Unity
{
    public enum HexOverlayLayer
    {
        Reachable,
        AttackRange,
        Path,
        PlayerActionRange,
        MonsterMoveIntent,
        MonsterAttackIntent,
        MonsterChaseRange,
        PlayerHoverMove,
        PlayerHoverAction,
        PlayerActionEffectArea,
        FieldObjectRange,
        TutorialTarget,

        /// <summary>보스 아레나 결계 링. 봉인되어 있는 동안 항상 표시된다(선택 상태와 무관).</summary>
        BossArenaBoundary,

        /// <summary>멀티셀 보스의 물리 점유 칸(P6 §13.4 C-4). 보스가 살아 있는 동안 항상 표시된다 —
        /// 1페이즈 보스는 모델이 2칸만 덮으면서 7칸을 막으므로 이 오버레이 없이는 읽을 수 없다.</summary>
        BossFootprint,

        /// <summary>정찰로 <b>판명된</b> 보스 취약 부위(§20-A-7). 미판명이면 아무것도 그리지 않는다 —
        /// <c>?</c>를 띄우면 "저기 어딘가"를 알려주는 셈이고, 그건 전멸기 후보 전용 어휘다.</summary>
        BossWeakSpot,

        /// <summary>전멸기 안전지대 <b>후보</b> 칸(§20-B-6). 진위는 정찰로만 갈린다.</summary>
        BossSafeZoneCandidate,

        /// <summary>정찰로 진짜라고 판명된 안전지대(§28 W5) — 초록 확정 채움.</summary>
        BossSafeZoneConfirmed,

        /// <summary>
        /// 자기부여 패턴을 예고한 몬스터의 <b>제자리 footprint</b>(2026-08-20 #10). 공격 예고가 없는
        /// 턴이라 붉은 위험 해치가 뜨지 않아 "이번 턴 아무 일도 없다"로 오독되던 자리다.
        ///
        /// 🔑 <see cref="MonsterAttackIntent"/>(붉은 위험 해치)를 재사용하지 <b>않는다</b>: 그 레이어의
        /// 어휘는 "밟으면 아픈 칸"이고, 자기부여는 그 칸이 위험해지는 것이 아니라 그 몬스터가 세지는
        /// 것이다. <see cref="MonsterMoveIntent"/>(주황 점선 = "여기로 온다")도 뜻이 다르다.
        /// ⚠️append-only — 중간에 끼우면 직렬화된 저작이 통째로 밀린다.
        /// </summary>
        MonsterSelfBuffIntent,

        /// <summary>
        /// 소환 예고(요괴 §4-4): 다음 몬스터 페이즈에 <b>적이 나타나는</b> 칸. 붉은 위험 해치
        /// (<see cref="MonsterAttackIntent"/>)와 어휘가 다르다 — 그 칸이 아픈 것이 아니라 그 칸이
        /// 곧 점유된다. ⚠️append-only.
        /// </summary>
        MonsterSummonIntent,

        /// <summary>
        /// <b>부서진 땅</b>(2026-09-01 #18 · 두억시니의 파열·균열 지대). 몬스터가 오브젝트를 놓은 것이
        /// 아니라 <b>그 칸의 지형이 상한 것</b>이므로, 타일 위에 물건을 세우지 않고 타일 자체를 그린다
        /// (사용자 확정 — 종전에는 실린더/프리팹이 칸 위에 서 있었다).
        ///
        /// <para>🔑 다른 레이어를 빌려 쓰지 않는 이유는 어휘가 전부 다르기 때문이다:
        /// <see cref="MonsterAttackIntent"/>는 「이번 턴 밟으면 아픈 칸」이라 몬스터 페이즈가 지나면
        /// 잔상이 되고, 이쪽은 <b>몇 턴이고 남아 있는 판의 사실</b>이다.
        /// ⚠️append-only — 중간에 끼우면 직렬화된 저작이 통째로 밀린다.</para>
        /// </summary>
        RupturedGround,

        /// <summary>
        /// <b>뒤끝이 미치는 칸</b>(2026-09-01 #3). 이름표의 뒤끝 배지에 손을 얹은 동안에만 뜬다 —
        /// 「여기서 잡으면 대가가 따르고, 떨어져서 잡으면 면한다」는 질문의 답이다.
        ///
        /// <para>🔑 전용 레이어인 이유: 이 칸들은 위험하지도(<see cref="MonsterAttackIntent"/>)
        /// 점유되지도(<see cref="MonsterSummonIntent"/>) 않는다. <b>내가 어디서 죽일지</b>의 문제라
        /// 어휘가 통째로 다르다. ⚠️append-only.</para>
        /// </summary>
        AftermathReach,

        /// <summary>
        /// <b>철조각 사슬 예고</b>(2026-09-05 결정 5 · 결정 8). 보스 중심→철조각 직선 스포크가 다음 몬스터 페이즈에
        /// 전격으로 명중하는 칸. 종전엔 <see cref="MonsterAttackIntent"/> 붉은 해치에 합쳐져 일반 공격 예고와
        /// 구분이 없었고 「스포크」 형태가 칸 집합으로 뭉개졌다. 위험 어휘는 같지만 <b>출처가 다르다</b>(패턴이 아니라
        /// 기물) — 철조각을 부수면 그 가닥만 꺼지는 규칙을 화면이 따로 말해야 한다. ⚠️append-only.
        /// </summary>
        BossScrapChainTelegraph,

        /// <summary>
        /// <b>철조각 폭발 범위</b>(2026-09-05 결정 4). 철조각에 손을 얹은 동안에만 뜬다 — 「이 기물이 터지면 어디가
        /// 아픈가」의 답. 뒤끝 호버(<see cref="AftermathReach"/>)와 같은 「손을 얹은 동안만」 문법이지만 어휘는
        /// 위험(붉은 계열)이라 레이어를 따로 둔다. ⚠️append-only.
        /// </summary>
        BossPropBlastReach,

        /// <summary>
        /// <b>철조각 살포 예고</b>(2026-09-05 후속 #1). 다음 몬스터 페이즈에 철조각이 <b>놓일</b> 칸. 종전엔
        /// <see cref="MonsterSummonIntent"/>(청록 점유 어휘)를 빌렸는데, 사용자 판정은 「공격 예고처럼 붉은 해치로」
        /// — 철조각은 서는 순간 칸을 막고 3턴 뒤 반경 1을 터뜨리는 <b>위협</b>이지 소환수가 아니다. 그러나
        /// <see cref="MonsterAttackIntent"/>에 합치면 「이번 턴 밟으면 아픈 칸」과 섞여 거짓말이 되므로(놓이는 칸은
        /// 당장 아프지 않다) 전용 레이어를 둔다 — 붉은 계열이되 테두리가 있어 「자리」로 읽힌다. ⚠️append-only.
        /// </summary>
        BossPropVolleyTelegraph
    }

    public enum VisibilityPresentationMode
    {
        OverlayTint,
        LightingMask
    }

    public sealed class AtlasTilePresentationView : MonoBehaviour, IHexMapWorldProjector, IHexMapOverlaySurfaceProjector, IMapObjectVisualHost, IPlayerActorVisualHost, ITileSideVisualHost, ITopChunkVisualHost, IVisibilityLightingMaskHost
    {
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");
        private static readonly int MainTexPropertyId = Shader.PropertyToID("_MainTex");
        private static readonly int FogAmountPropertyId = Shader.PropertyToID("_FogAmount");
        private static readonly int FogTintPropertyId = Shader.PropertyToID("_FogTint");
        private static readonly int SurfacePropertyId = Shader.PropertyToID("_Surface");
        private static readonly int BlendPropertyId = Shader.PropertyToID("_Blend");
        private static readonly int SrcBlendPropertyId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendPropertyId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWritePropertyId = Shader.PropertyToID("_ZWrite");
        private static readonly int WaterSurfaceColorPropertyId = Shader.PropertyToID("Color_F01C36BF");
        private static readonly int WaterDepthColorPropertyId = Shader.PropertyToID("Color_7D9A58EC");
        private static readonly int WaterWaveSpeedPropertyId = Shader.PropertyToID("_WaveSpeed");

        private static readonly ProfilerMarker VisCellLoopMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.CellLoop");
        private static readonly ProfilerMarker VisChunkRebuildMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.ChunkRebuild");
        private static readonly ProfilerMarker VisSafeInfoSnapshotMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.SafeInfoSnapshot");
        private static readonly ProfilerMarker VisLightingMaskMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.LightingMask");
        private static readonly ProfilerMarker VisEdgeDepthMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.EdgeDepth");
        private static readonly ProfilerMarker VisObjectVisibilityMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.ObjectVisibility");
        private static readonly ProfilerMarker VisFogCellsMarker = new ProfilerMarker(ProfilerCategory.Scripts, "Atlas.Vis.FogCells");

        [SerializeField] private AtlasTileCatalog catalog;
        [SerializeField] private float tileRadius = 1f;
        [SerializeField] private float tileSpacing = 1f;
        [SerializeField] private float heightStep = 0.5f;
        [SerializeField] private float verticalOffset;
        [SerializeField] private Color playerMarkerColor = new Color(1f, 0.08f, 0.12f, 1f);
        [SerializeField] private float playerMarkerRadius = 0.22f;
        [SerializeField] private GameObject playerMarkerPrefab;
        [SerializeField] private Vector3 playerVisualLocalOffset;
        [SerializeField] private Vector3 playerVisualLocalEulerAngles;
        [SerializeField] private Vector3 playerVisualLocalScale = Vector3.one;
        [SerializeField] private float playerHeightOffset = 0.32f;
        [SerializeField] private Color generatedSideColor = new Color(0.42f, 0.34f, 0.24f, 1f);
        [SerializeField] private Color generatedSideTopColorTint = Color.white;
        [SerializeField] [Range(0f, 2f)] private float generatedSideTopColorMultiplier = 0.72f;
        [SerializeField] private float generatedSideThickness = 0.05f;
        [SerializeField] private float generatedSideRadiusScale = 1f;
        [SerializeField] private float generatedSideCornerOverlap = 0f;
        // Single source for the visibility look (see VisibilityPresentationSettings). Assigned = the
        // asset's values overwrite the fields below on Awake; empty = this instance's values apply.
        [SerializeField] private VisibilityPresentationSettings visibilitySettings;
        [SerializeField] private Color visibilityUnknownColor = new Color(0.02f, 0.025f, 0.03f, 0.78f);
        [SerializeField] private Color visibilityHintedColor = new Color(0.18f, 0.22f, 0.26f, 0.52f);
        [SerializeField] private float visibilityOverlayLift = HexOverlayRenderOrder.VisibilityFogLift;
        [SerializeField] private Color fogUnknownTint = new Color(0.07f, 0.08f, 0.10f, 1f);
        [SerializeField] private Color fogHintedTint = new Color(0.34f, 0.38f, 0.42f, 1f);
        [SerializeField] [Range(0f, 1f)] private float unknownFogAmount = 0.88f;
        [SerializeField] [Range(0f, 1f)] private float hintedFogAmount = 0.58f;
        [Header("Visibility Presentation")]
        [SerializeField] private VisibilityPresentationMode visibilityPresentationMode = VisibilityPresentationMode.OverlayTint;
        [SerializeField] private Shader visibilityLightingShader;
        [SerializeField] [Range(0f, 1f)] private float visibilityUnknownLighting = 0.10f;
        [SerializeField] [Range(0f, 1f)] private float visibilityHintedLighting = 0.55f;
        [SerializeField] [Range(0f, 1f)] private float visibilityRevealedLighting = 1f;
        [SerializeField] [Range(0f, 1f)] private float visibilityEmissionFloor = 0.25f;
        [SerializeField] [Range(64, 1024)] private int visibilityLightingMaskResolution = 128;
        [SerializeField] [Range(0, 4)] private int visibilityLightingMaskBlurPasses = 1;
        [Tooltip("Snaps the sampled mask to the nearer of the Unknown/Revealed levels in the shader, so the vision edge reads as a line instead of a gradient (#12).")]
        [SerializeField] private bool visibilityLightingHardEdge = true;
        [Tooltip("경계 그라데이션 정도(#1 재판정 손잡이): 0 = 완전 하드, 1 = 스냅 없음. HardEdge가 켜져 있을 때만 의미.")]
        [SerializeField] [Range(0f, 1f)] private float visibilityLightingEdgeGradation;
        [Tooltip("마스크를 육각 셀 중심에서 샘플해 경계가 타일 변을 따라가게 한다(#1). 끄면 옛 평면 샘플(A/B 비교용).")]
        [SerializeField] private bool visibilityLightingHexSnap = true;
        [Tooltip("Enables a brightness falloff on Revealed tiles toward the vision edge. 0 = off (original behavior). Otherwise the maximum number of fade rings; set >= your largest vision range so a single light's center reaches full brightness. Opt in per-scene.")]
        [SerializeField] [Min(0)] private int visibilityRevealedFadeMaxSteps;
        [Tooltip("Darkening overlay opacity of the farthest Revealed tile (the vision edge). Fixed regardless of vision range; the tile at the light source fades to fully bright. Keep below the Unknown overlay opacity so the edge stays clearly brighter than Unknown.")]
        [SerializeField] [Range(0f, 1f)] private float visibilityRevealedFadeFloorAlpha = 0.5f;
        [Tooltip("Dark tint that Revealed edge tiles fade toward. Combined with the floor opacity it stays clearly brighter than Unknown.")]
        [SerializeField] private Color visibilityRevealedFadeColor = new Color(0.02f, 0.025f, 0.03f, 1f);
        [Tooltip("Marker tint drawn on a cell whose trap has been discovered by a Scout card. Trap discovery is independent of fog, so the marker shows even on Hinted cells.")]
        [SerializeField] private Color trapMarkerColor = new Color(0.92f, 0.28f, 0.20f, 0.85f);
        [Tooltip("Height the trap marker floats above the tile surface. Kept above the visibility fog so the marker stays readable through the fog overlay.")]
        [SerializeField] private float trapMarkerLift = HexOverlayRenderOrder.CombatBoundaryLift;
        [Tooltip("Optional trap visual prefab. In the editor this falls back to Assets/Prefabs/Object/trap.prefab when unset.")]
        [SerializeField] private GameObject trapMarkerPrefab;
        [SerializeField] private MapObjectCatalogSet mapObjectCatalogSet;
        [SerializeField] private float mapObjectLift = 0.02f;
        [SerializeField] private bool renderSideVisuals = true;
        [SerializeField] private bool useTopChunkMeshes = true;
        [SerializeField] [Min(1)] private int topChunkMaxCells = 400;

        private readonly Dictionary<HexCoord, AtlasTileVisual> cells = new Dictionary<HexCoord, AtlasTileVisual>();
        private Func<HexCoord, int?> cellHeightLevelLookup;
        private readonly Dictionary<HexCoord, float> lastFogAmounts = new Dictionary<HexCoord, float>();
        private readonly Dictionary<HexCoord, float> topSurfaceLocalYByCoord = new Dictionary<HexCoord, float>();
        // Every placed map object — instantiation, hover, occlusion fade, visibility show/hide, the consumed
        // set, buildings-only mode and the night-look dimmer — lives in this collaborator. The public map
        // object API below stays on the view and delegates, so no caller or scene changed when it moved out.
        // Lazily built rather than assigned in Awake: EditMode fixtures drive the view directly without ever
        // running the Unity lifecycle, and a field initializer cannot pass `this`.
        private MapObjectVisualRegistry mapObjectRegistry;
        private MapObjectVisualRegistry mapObjects => mapObjectRegistry ??= new MapObjectVisualRegistry(this);
        // Same lazy pattern, same reason: the player marker is built on first use, not in Awake.
        private PlayerActorVisualProxy playerActorProxy;
        private PlayerActorVisualProxy playerActor => playerActorProxy ??= new PlayerActorVisualProxy(this);
        // Generated tile side faces: chunk mesh, its object, and the material cache behind them.
        private TileSideVisualBuilder sideVisualBuilder;
        private TileSideVisualBuilder sideVisuals => sideVisualBuilder ??= new TileSideVisualBuilder(this);
        private readonly List<string> missingVisualDiagnostics = new List<string>();
        private MaterialPropertyBlock fogPropertyBlock;
        private HexMapData map;
        private Material visibilityOverlayMaterial;
        private Material visibilityUnknownMaterial;
        private Material visibilityHintedMaterial;
        private Material trapMarkerMaterial;
        // Chunk-safe tile tops merged into shared meshes: the chunk objects, their generated meshes and
        // the per-render build plan all live in this collaborator. Which cells are eligible and where each
        // instance lands stays here (projection + catalog resolution). Same lazy pattern, same reason.
        private TopChunkVisualBatcher topChunkBatcher;
        private TopChunkVisualBatcher topChunks => topChunkBatcher ??= new TopChunkVisualBatcher(this);
        private readonly Dictionary<Renderer, Material[]> originalTopMaterialsByRenderer = new Dictionary<Renderer, Material[]>();
        private readonly Dictionary<Material, Material> visibilityLightingMaterialVariants = new Dictionary<Material, Material>();
        private readonly List<Material> ownedVisibilityLightingMaterials = new List<Material>();
        // Fog-of-war lighting mask: the R8 texture, its pixel buffers, the cached world bounds, the
        // texel→cell-slot LUT and the global shader upload all live in this collaborator. The authored
        // settings and the hex projection stay here and are read back through the host interface.
        // ⚠️ Measured hot path — the cs:538 optimizations (LUT, delta skip) live inside the service.
        // Same lazy pattern as the collaborators above.
        private VisibilityLightingMaskService visibilityLightingMaskService;
        private VisibilityLightingMaskService lightingMask => visibilityLightingMaskService ??= new VisibilityLightingMaskService(this);
        private readonly List<GameObject> visibilityOverlayChunks = new List<GameObject>();
        private readonly List<Mesh> visibilityOverlayChunkMeshes = new List<Mesh>();
        private readonly List<HexCoord> pendingUnknownVisibilityChunkCells = new List<HexCoord>();
        private readonly List<HexCoord> pendingHintedVisibilityChunkCells = new List<HexCoord>();
        // Incremental visibility state: only re-apply cells whose (Exists, Visibility) changed, and
        // only rebuild fog chunk meshes when the unknown/hinted cell set changes. See ApplyVisibility.
        private readonly Dictionary<HexCoord, int> lastVisibilityKeyByCoord = new Dictionary<HexCoord, int>();

        // Caller-supplied epoch of the last full visibility pass. When the next pass carries the same
        // epoch, every output (counters, chunk sets, per-cell key cache) is already current, so the
        // whole-map rescan is skipped. Invalidated on map==null and on full view resets.
        private bool hasLastVisibilityEpoch;
        private long lastVisibilityEpoch;
        private readonly HashSet<HexCoord> currentUnknownVisibilityChunkCells = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> currentHintedVisibilityChunkCells = new HashSet<HexCoord>();
        // Unknown cells that border a known (existing, non-Unknown) cell — the fog-of-war frontier the
        // particle decorator fogs. Recomputed only when the Unknown/Hinted set changes (see
        // RebuildVisibilityChunksIfSetChanged); VisibilityFrontierRevision bumps each recompute so the
        // fog presenter can cheaply skip refreshes where the frontier did not move.
        private readonly List<HexCoord> fogUnknownCells = new List<HexCoord>();
        public IReadOnlyList<HexCoord> UnknownCells => fogUnknownCells;
        public int VisibilityFogRevision { get; private set; }
        private readonly HashSet<HexCoord> currentUnknownFogSet = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> scratchUnknownFogSet = new HashSet<HexCoord>();
        // Building/prop footprints (+ a one-ring buffer) that fog must not seed on, so fog clumps do
        // not sit on top of buildings. Rebuilt when the map is rendered (buildings never move).
        private readonly HashSet<HexCoord> fogBlockedCells = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> scratchUnknownVisibilityChunkCells = new HashSet<HexCoord>();
        private readonly HashSet<HexCoord> scratchHintedVisibilityChunkCells = new HashSet<HexCoord>();
        // Revealed-tile fade toward the vision edge: cache the per-cell safe info for one pass, then
        // compute each Revealed cell's depth (hex distance to the nearest non-Revealed cell). Edge
        // cells (depth 1) get the darkest overlay (fixed floor) and brightness ramps up to the lit
        // center, which gets no overlay at all.
        private readonly Dictionary<HexCoord, HexVisibilitySafeCellInfo> visibilitySafeInfoCache = new Dictionary<HexCoord, HexVisibilitySafeCellInfo>();
        // Per-Revealed-cell depth = hex distance to the nearest non-Revealed cell (1 = vision edge).
        private readonly Dictionary<HexCoord, int> revealedEdgeDepth = new Dictionary<HexCoord, int>();
        private int revealedEdgeMaxDepth;
        private readonly Queue<HexCoord> revealedDepthBfsQueue = new Queue<HexCoord>();
        private readonly List<List<HexCoord>> scratchRevealedFadeRingCells = new List<List<HexCoord>>();
        private readonly List<HashSet<HexCoord>> currentRevealedFadeRingCells = new List<HashSet<HexCoord>>();
        private readonly List<List<HexCoord>> pendingRevealedFadeRingCells = new List<List<HexCoord>>();
        private readonly Dictionary<int, Material> revealedFadeRingMaterials = new Dictionary<int, Material>();
        private int currentRevealedFadeEffMax;
        private static readonly int[] HexNeighborDQ = { 1, 1, 0, -1, -1, 0 };
        private static readonly int[] HexNeighborDR = { 0, -1, -1, 0, 1, 1 };

        public int TopVisualCount { get; private set; }
        public int SideVisualCount => sideVisuals.SegmentCount;
        public int MapObjectVisualCount => mapObjects.VisualCount;
        public int ActiveMapObjectVisualCount => mapObjects.ActiveVisualCount;
        public bool SideVisualsDeferred => sideVisuals.Deferred;
        public bool RenderSideVisuals => renderSideVisuals;
        public float TileRadius => Mathf.Max(0.01f, tileRadius);
        public float HeightStep => Mathf.Max(0f, heightStep);
        public int VisibilityRefreshCount { get; private set; }
        public int FogMaterialRefreshCount { get; private set; }
        public int FoggedRendererCount { get; private set; }
        public bool LastVisibilityRefreshWasNoOp { get; private set; } = true;
        public int VisibilityMissingCount { get; private set; }
        public int VisibilityHiddenCount { get; private set; }
        public int VisibilityExploredCount { get; private set; }
        public int VisibilityVisibleCount { get; private set; }
        public int VisibilityChunkRendererCount { get; private set; }
        public VisibilityPresentationMode VisibilityPresentationMode => visibilityPresentationMode;
        public Texture2D VisibilityLightingMaskTexture => lightingMask.MaskTexture;
        public IReadOnlyList<string> MissingVisualDiagnostics => missingVisualDiagnostics;
        public Vector3 PlayerMarkerLocalPosition => playerActor.LocalPosition;
        public AtlasTileCatalog Catalog => catalog;

        /// <inheritdoc cref="PlayerActorVisualProxy.TryGetRendererBoundsWorldCenter"/>
        public bool TryGetPlayerMarkerRendererBoundsWorldCenter(out Vector3 worldCenter)
            => playerActor.TryGetRendererBoundsWorldCenter(out worldCenter);

        /// <inheritdoc cref="MapObjectVisualRegistry.TryGetTopLocalY"/>
        public bool TryGetMapObjectVisualTopLocalY(HexCoord coord, out float topLocalY)
            => mapObjects.TryGetTopLocalY(coord, out topLocalY);

        /// <inheritdoc cref="PlayerActorVisualProxy.TryGetVfxAnchorWorldPosition"/>
        public bool TryGetPlayerVfxAnchorWorldPosition(CharacterVfxAnchorKind anchorKind, out Vector3 worldPosition)
            => playerActor.TryGetVfxAnchorWorldPosition(anchorKind, out worldPosition);

        /// <inheritdoc cref="PlayerActorVisualProxy.TryGetFootprintDiameter"/>
        public bool TryGetPlayerFootprintDiameter(out float diameter)
            => playerActor.TryGetFootprintDiameter(out diameter);

        /// <summary>Transform variant for callers that keep tracking the anchor after spawn (followSourceAnchor cues).</summary>
        public bool TryGetPlayerVfxAnchor(CharacterVfxAnchorKind anchorKind, out Transform anchor)
        {
            if (playerActor != null && playerActor.TryGetVfxAnchor(anchorKind, out anchor) && anchor != null)
            {
                return true;
            }

            anchor = null;
            return false;
        }

        public void ConfigureForTests(
            AtlasTileCatalog catalog = null,
            float tileRadius = 1f,
            float tileSpacing = 1f,
            float heightStep = 0.5f,
            bool renderSideVisuals = true,
            bool useTopChunkMeshes = true,
            int topChunkMaxCells = 400,
            Color? generatedSideTopColorTint = null,
            float generatedSideTopColorMultiplier = 0.72f,
            float? generatedSideRadiusScale = null,
            float? generatedSideCornerOverlap = null,
            GameObject playerMarkerPrefab = null,
            Vector3? playerVisualLocalOffset = null,
            Vector3? playerVisualLocalEulerAngles = null,
            Vector3? playerVisualLocalScale = null,
            MapObjectCatalogSet mapObjectCatalogSet = null)
        {
            this.catalog = catalog;
            this.tileRadius = Mathf.Max(0.01f, tileRadius);
            this.tileSpacing = Mathf.Max(0.01f, tileSpacing);
            this.heightStep = Mathf.Max(0f, heightStep);
            this.mapObjectCatalogSet = mapObjectCatalogSet;
            this.renderSideVisuals = renderSideVisuals;
            this.useTopChunkMeshes = useTopChunkMeshes;
            this.topChunkMaxCells = Mathf.Max(1, topChunkMaxCells);
            this.generatedSideTopColorTint = generatedSideTopColorTint ?? Color.white;
            this.generatedSideTopColorMultiplier = Mathf.Max(0f, generatedSideTopColorMultiplier);
            this.playerMarkerPrefab = playerMarkerPrefab;
            this.playerVisualLocalOffset = playerVisualLocalOffset ?? Vector3.zero;
            this.playerVisualLocalEulerAngles = playerVisualLocalEulerAngles ?? Vector3.zero;
            this.playerVisualLocalScale = playerVisualLocalScale ?? Vector3.one;
            if (generatedSideRadiusScale.HasValue)
            {
                this.generatedSideRadiusScale = Mathf.Max(0.01f, generatedSideRadiusScale.Value);
            }

            if (generatedSideCornerOverlap.HasValue)
            {
                this.generatedSideCornerOverlap = Mathf.Max(0f, generatedSideCornerOverlap.Value);
            }
        }

        public void ConfigureVisibilityPresentationForTests(
            VisibilityPresentationMode mode,
            Shader lightingShader = null,
            int maskResolution = 64,
            int blurPasses = 0)
        {
            visibilityLightingShader = lightingShader;
            visibilityLightingMaskResolution = Mathf.Clamp(maskResolution, 64, 1024);
            visibilityLightingMaskBlurPasses = Mathf.Clamp(blurPasses, 0, 4);
            SetVisibilityPresentationMode(mode);
        }

        public void SetVisibilityPresentationMode(VisibilityPresentationMode mode)
        {
            if (visibilityPresentationMode == mode)
            {
                return;
            }

            visibilityPresentationMode = mode;
            hasLastVisibilityEpoch = false;
            lastVisibilityKeyByCoord.Clear();
            ApplyVisibilityLightingMaterialState();
        }

        public void Render(HexMapData map)
        {
            ClearAll();
            this.map = map;
            RebuildFogBlockedCells();
            if (map == null)
            {
                return;
            }

            var batchTops = useTopChunkMeshes;
            if (batchTops)
            {
                topChunks.BeginPlan();
            }

            foreach (var cell in map.AllCells)
            {
                if (!batchTops || !TryCreateChunkedCellTop(cell))
                {
                    CreateCellTop(cell);
                }
            }

            if (batchTops)
            {
                topChunks.BuildChunks();
            }
            mapObjects.CreateVisuals(map.ObjectRefs);
            RebuildSideVisuals();
        }


        public void Render(HexSparseMapAuthoringSource source)
        {
            if (source != null && source.TryToHexMapData(out var map, out _))
                Render(map);
        }

        public Vector3 Project(HexCoord coord)
        {
            CreateAxialProjection().CoordToWorld(coord, out var x, out var z);
            return new Vector3(x, verticalOffset, z);
        }

        /// <summary>Effective hex radius tiles are projected with (tileRadius × tileSpacing). Editor authoring overlays use this so the grid matches tile spacing exactly.</summary>
        public float AuthoringHexRadius => Mathf.Max(0.01f, tileRadius) * Mathf.Max(0.01f, tileSpacing);

        /// <summary>Local-space Y the tile surface projects to (verticalOffset). Authoring overlays draw their grid plane here, in this view's local frame.</summary>
        public float AuthoringPlaneLocalY => verticalOffset;

        private HexAxialProjection CreateAxialProjection()
        {
            var radius = Mathf.Max(0.01f, tileRadius) * Mathf.Max(0.01f, tileSpacing);
            return new HexAxialProjection(radius, verticalOffset, HeightStep);
        }

        public Vector3 ProjectTop(HexCoord coord)
        {
            if (map != null && map.TryGetCell(coord, out var cell))
            {
                return Project(coord) + Vector3.up * ResolveTopElevation(cell);
            }

            return Project(coord);
        }

        public Vector3 ProjectOverlaySurface(HexCoord coord)
        {
            var surface = ProjectTop(coord);
            if (topSurfaceLocalYByCoord.TryGetValue(coord, out var topSurfaceLocalY))
            {
                surface += Vector3.up * topSurfaceLocalY;
            }

            return surface;
        }

        // Pure-math picking: no physics involved. Height planes are swept in
        // HexAxialProjection; tile click colliders were removed in P6.
        public bool TryRaycastHex(Ray worldRay, out HexCoord coord)
        {
            coord = default;
            if (map == null)
            {
                return false;
            }

            var localOrigin = transform.InverseTransformPoint(worldRay.origin);
            var localDirection = transform.InverseTransformDirection(worldRay.direction);
            cellHeightLevelLookup ??= candidate =>
                map != null && map.TryGetCell(candidate, out var cell) ? cell.HeightLevel : (int?)null;
            return CreateAxialProjection().TryRaycastHeightPlanes(
                localOrigin.x, localOrigin.y, localOrigin.z,
                localDirection.x, localDirection.y, localDirection.z,
                cellHeightLevelLookup,
                out coord);
        }

        public bool TryScreenToHex(Camera camera, Vector2 screenPosition, out HexCoord coord)
        {
            coord = default;
            if (camera == null || !camera.pixelRect.Contains(screenPosition))
            {
                return false;
            }

            return TryRaycastHex(camera.ScreenPointToRay(screenPosition), out coord);
        }

        public void ApplyVisibility(Func<HexCoord, HexVisibilitySafeCellInfo> visibilityProvider, long? stateEpoch = null)
        {
            VisibilityRefreshCount++;

            // Fast path: caller signalled (via a matching epoch) that nothing the visibility output
            // depends on changed since the last full pass. All counters, chunk sets and the per-cell
            // key cache are still current, so we can skip the whole-map rescan entirely. Counts are
            // intentionally NOT reset here so they retain their (still-correct) values.
            if (stateEpoch.HasValue && hasLastVisibilityEpoch && lastVisibilityEpoch == stateEpoch.Value &&
                map != null && visibilityProvider != null)
            {
                LastVisibilityRefreshWasNoOp = true;
                return;
            }

            ResetVisibilityCounts();
            if (map == null)
            {
                lastVisibilityKeyByCoord.Clear();
                currentUnknownVisibilityChunkCells.Clear();
                currentHintedVisibilityChunkCells.Clear();
                if (currentUnknownFogSet.Count > 0 || fogUnknownCells.Count > 0)
                {
                    currentUnknownFogSet.Clear();
                    fogUnknownCells.Clear();
                    VisibilityFogRevision++;
                }
                pendingUnknownVisibilityChunkCells.Clear();
                pendingHintedVisibilityChunkCells.Clear();
                revealedEdgeDepth.Clear();
                revealedEdgeMaxDepth = 0;
                visibilitySafeInfoCache.Clear();
                ClearCurrentRevealedFadeRings();
                DestroyVisibilityOverlayChunks();
                lightingMask.Destroy();
                hasLastVisibilityEpoch = false;
                LastVisibilityRefreshWasNoOp = visibilityProvider == null;
                return;
            }

            scratchUnknownVisibilityChunkCells.Clear();
            scratchHintedVisibilityChunkCells.Clear();
            BeginRevealedFadeRingScratch();

            // Pre-pass: snapshot the safe-cell info for every cell, then build the per-Revealed-cell
            // edge-depth field. The main loop reads from the cache so the provider runs once per cell.
            VisSafeInfoSnapshotMarker.Begin();
            visibilitySafeInfoCache.Clear();
            foreach (var cell in map.AllCells)
            {
                visibilitySafeInfoCache[cell.Coord] = visibilityProvider == null
                    ? new HexVisibilitySafeCellInfo(
                        cell.Coord,
                        HexCellVisibility.Revealed,
                        true,
                        true,
                        true,
                        cell.TileDefinitionId,
                        cell.TerrainTypeId,
                        cell.BaseMoveCost,
                        cell.BaseWalkable,
                        cell.BaseBlocksVision,
                        cell.EventId,
                        cell.LandmarkId,
                        cell.VisualFloor)
                    : visibilityProvider(cell.Coord);
            }
            VisSafeInfoSnapshotMarker.End();

            if (visibilityPresentationMode == VisibilityPresentationMode.LightingMask)
            {
                VisLightingMaskMarker.Begin();
                lightingMask.Update();
                VisLightingMaskMarker.End();
            }

            VisEdgeDepthMarker.Begin();
            RebuildRevealedEdgeDepth();
            VisEdgeDepthMarker.End();

            VisCellLoopMarker.Begin();
            foreach (var cell in map.AllCells)
            {
                var safeInfo = visibilitySafeInfoCache[cell.Coord];

                // Per-refresh counters and chunk membership are recomputed for every cell (cheap),
                // so the public visibility/fog counts stay exact even when expensive work is skipped.
                CountVisibilityState(safeInfo);
                if (visibilityPresentationMode == VisibilityPresentationMode.OverlayTint)
                {
                    ClassifyVisibilityChunkCell(safeInfo);
                }
                AccumulateFoggedRendererCount(safeInfo);

                // Only cells whose (Exists, Visibility) actually changed need their per-tile fog
                // material, overlay GameObjects, and map-object visibility re-applied.
                var key = EncodeVisibilityKey(safeInfo);
                if (lastVisibilityKeyByCoord.TryGetValue(cell.Coord, out var previousKey) && previousKey == key)
                {
                    continue;
                }

                lastVisibilityKeyByCoord[cell.Coord] = key;
                if (cells.TryGetValue(cell.Coord, out var visual))
                {
                    ApplyVisibilityState(visual, safeInfo);
                }

            }
            VisCellLoopMarker.End();

            VisObjectVisibilityMarker.Begin();
            mapObjects.ApplyVisibilityStates();
            VisObjectVisibilityMarker.End();

            VisChunkRebuildMarker.Begin();
            RebuildVisibilityChunksIfSetChanged();
            VisChunkRebuildMarker.End();
            // Mode-independent: the fog-particle cell set is derived from per-cell visibility, which is
            // cached above regardless of OverlayTint vs LightingMask presentation.
            VisFogCellsMarker.Begin();
            UpdateFogCells();
            VisFogCellsMarker.End();
            mapObjects.RecountActive();
            LastVisibilityRefreshWasNoOp = false;

            // Remember the epoch of this full pass so a later refresh carrying the same epoch can skip.
            // A pass without an epoch (e.g. initial render) clears the cache so we never wrongly skip.
            if (stateEpoch.HasValue)
            {
                lastVisibilityEpoch = stateEpoch.Value;
                hasLastVisibilityEpoch = true;
            }
            else
            {
                hasLastVisibilityEpoch = false;
            }
        }

        private int EncodeVisibilityKey(HexVisibilitySafeCellInfo safeInfo)
        {
            var key = (safeInfo.Exists ? 1 : 0) << 4 | (int)safeInfo.Visibility;
            // Fold the fade-ring index into the key so a cell that stays Revealed but changes its
            // edge depth (player moved) is re-applied instead of being skipped.
            if (TryGetRevealedFadeRing(safeInfo, out var ring))
            {
                key |= (1 << 8) | (ring << 9);
            }

            // Fold trap discovery in so a cell that becomes trap-revealed without any fog change
            // (e.g. a Scout that overlaps the player's current vision) still re-runs the per-cell
            // pass and shows/hides its trap marker.
            if (safeInfo.TrapRevealed)
            {
                key |= 1 << 16;
            }

            return key;
        }

        private void ClassifyVisibilityChunkCell(HexVisibilitySafeCellInfo safeInfo)
        {
            if (safeInfo.Exists && safeInfo.Visibility == HexCellVisibility.Revealed)
            {
                if (TryGetRevealedFadeRing(safeInfo, out var fadeRing))
                {
                    scratchRevealedFadeRingCells[fadeRing].Add(safeInfo.Coord);
                }

                return;
            }

            if (safeInfo.Exists && safeInfo.Visibility == HexCellVisibility.Hinted)
            {
                scratchHintedVisibilityChunkCells.Add(safeInfo.Coord);
                return;
            }

            scratchUnknownVisibilityChunkCells.Add(safeInfo.Coord);
        }

        private void AccumulateFoggedRendererCount(HexVisibilitySafeCellInfo safeInfo)
        {
            if (ResolveFogAmount(safeInfo) <= 0.001f || !cells.TryGetValue(safeInfo.Coord, out var visual))
            {
                return;
            }

            foreach (var renderer in visual.FogRenderers)
            {
                if (renderer != null)
                {
                    FoggedRendererCount++;
                }
            }
        }

        private void RebuildVisibilityChunksIfSetChanged()
        {
            if (scratchUnknownVisibilityChunkCells.SetEquals(currentUnknownVisibilityChunkCells) &&
                scratchHintedVisibilityChunkCells.SetEquals(currentHintedVisibilityChunkCells) &&
                RevealedFadeRingScratchMatchesCurrent())
            {
                return;
            }

            DestroyVisibilityOverlayChunks();
            currentUnknownVisibilityChunkCells.Clear();
            currentUnknownVisibilityChunkCells.UnionWith(scratchUnknownVisibilityChunkCells);
            currentHintedVisibilityChunkCells.Clear();
            currentHintedVisibilityChunkCells.UnionWith(scratchHintedVisibilityChunkCells);
            CommitRevealedFadeRingScratchToCurrent();

            pendingUnknownVisibilityChunkCells.Clear();
            pendingUnknownVisibilityChunkCells.AddRange(currentUnknownVisibilityChunkCells);
            pendingHintedVisibilityChunkCells.Clear();
            pendingHintedVisibilityChunkCells.AddRange(currentHintedVisibilityChunkCells);
            BuildVisibilityOverlayChunks();
        }

        // Building/prop footprints plus their immediate neighbors — fog seeds are kept off these so a
        // fog clump does not land on a building. Large fog particles may still feather over a building
        // edge, but no clump is centered on one. Buildings never move, so this is rebuilt only on Render.
        private void RebuildFogBlockedCells()
        {
            fogBlockedCells.Clear();
            if (map == null)
            {
                return;
            }

            foreach (var objectRef in map.ObjectRefs)
            {
                if (!objectRef.IsBuilding)
                {
                    continue;
                }

                foreach (var occupied in objectRef.OccupiedCoords)
                {
                    fogBlockedCells.Add(occupied);
                    for (var i = 0; i < HexNeighborDQ.Length; i++)
                    {
                        fogBlockedCells.Add(new HexCoord(occupied.Q + HexNeighborDQ[i], occupied.R + HexNeighborDR[i]));
                    }
                }
            }
        }

        // The full fog-of-war cell set = every existing cell whose visibility is Unknown ("암시야").
        // The fog presenter scatters particles across these (probabilistically, camera-bounded), so it
        // needs the whole Unknown region, not just its boundary.
        //
        // Computed from visibilitySafeInfoCache (populated for every cell in BOTH presentation modes),
        // NOT from currentUnknownVisibilityChunkCells (only filled in OverlayTint mode). Called
        // unconditionally at the end of a full ApplyVisibility pass. The revision bumps only when the
        // Unknown set actually changes, so the fog presenter still no-ops when it has not moved.
        private void UpdateFogCells()
        {
            scratchUnknownFogSet.Clear();
            if (map != null)
            {
                foreach (var cell in map.AllCells)
                {
                    if (visibilitySafeInfoCache.TryGetValue(cell.Coord, out var info) &&
                        info.Visibility == HexCellVisibility.Unknown &&
                        !fogBlockedCells.Contains(cell.Coord))
                    {
                        scratchUnknownFogSet.Add(cell.Coord);
                    }
                }
            }

            if (scratchUnknownFogSet.SetEquals(currentUnknownFogSet))
            {
                return;
            }

            currentUnknownFogSet.Clear();
            currentUnknownFogSet.UnionWith(scratchUnknownFogSet);
            fogUnknownCells.Clear();
            fogUnknownCells.AddRange(currentUnknownFogSet);
            VisibilityFogRevision++;
        }

        // Returns true when the cell is a Revealed cell inside the edge-fade band, i.e. its depth
        // (hex distance to the nearest non-Revealed cell) is between 1 and the lit blob's max depth
        // minus one. 'ring' is 0-based: ring 0 is the vision edge (darkest, fixed floor), higher
        // rings are progressively closer to the fully bright lit center.
        private bool TryGetRevealedFadeRing(HexVisibilitySafeCellInfo safeInfo, out int ring)
        {
            ring = 0;
            if (visibilityRevealedFadeMaxSteps <= 0 || !safeInfo.Exists ||
                safeInfo.Visibility != HexCellVisibility.Revealed)
            {
                return false;
            }

            // Need at least two depth levels (edge + center) for a fade to make sense.
            if (revealedEdgeMaxDepth < 2)
            {
                return false;
            }

            if (!revealedEdgeDepth.TryGetValue(safeInfo.Coord, out var depth) || depth < 1 ||
                depth >= revealedEdgeMaxDepth)
            {
                // No recorded depth (deep interior, beyond the perf cap) or the lit center: fully bright.
                return false;
            }

            ring = depth - 1;
            return true;
        }

        // Multi-source BFS seeded from every existing non-Revealed cell (depth 0), expanding only into
        // Revealed neighbors so each Revealed cell learns its hex distance to the nearest non-Revealed
        // cell. Capped at visibilityRevealedFadeMaxSteps for perf. Populated from visibilitySafeInfoCache,
        // so it must run after the pre-pass. revealedEdgeMaxDepth is the deepest Revealed depth reached.
        private void RebuildRevealedEdgeDepth()
        {
            revealedEdgeDepth.Clear();
            revealedEdgeMaxDepth = 0;
            if (map == null || visibilityRevealedFadeMaxSteps <= 0)
            {
                return;
            }

            revealedDepthBfsQueue.Clear();
            foreach (var entry in visibilitySafeInfoCache)
            {
                if (entry.Value.Exists && entry.Value.Visibility != HexCellVisibility.Revealed)
                {
                    revealedEdgeDepth[entry.Key] = 0;
                    revealedDepthBfsQueue.Enqueue(entry.Key);
                }
            }

            while (revealedDepthBfsQueue.Count > 0)
            {
                var coord = revealedDepthBfsQueue.Dequeue();
                var depth = revealedEdgeDepth[coord];
                if (depth >= visibilityRevealedFadeMaxSteps)
                {
                    continue;
                }

                for (var i = 0; i < 6; i++)
                {
                    var neighbor = new HexCoord(coord.Q + HexNeighborDQ[i], coord.R + HexNeighborDR[i]);
                    if (revealedEdgeDepth.ContainsKey(neighbor))
                    {
                        continue;
                    }

                    if (!visibilitySafeInfoCache.TryGetValue(neighbor, out var neighborInfo) ||
                        !neighborInfo.Exists || neighborInfo.Visibility != HexCellVisibility.Revealed)
                    {
                        continue;
                    }

                    var neighborDepth = depth + 1;
                    revealedEdgeDepth[neighbor] = neighborDepth;
                    if (neighborDepth > revealedEdgeMaxDepth)
                    {
                        revealedEdgeMaxDepth = neighborDepth;
                    }

                    revealedDepthBfsQueue.Enqueue(neighbor);
                }
            }
        }

        private void EnsureRevealedFadeRingCapacity()
        {
            var needed = Mathf.Max(0, visibilityRevealedFadeMaxSteps);
            while (scratchRevealedFadeRingCells.Count < needed)
            {
                scratchRevealedFadeRingCells.Add(new List<HexCoord>());
                currentRevealedFadeRingCells.Add(new HashSet<HexCoord>());
                pendingRevealedFadeRingCells.Add(new List<HexCoord>());
            }
        }

        private void BeginRevealedFadeRingScratch()
        {
            EnsureRevealedFadeRingCapacity();
            for (var i = 0; i < scratchRevealedFadeRingCells.Count; i++)
            {
                scratchRevealedFadeRingCells[i].Clear();
            }
        }

        private bool RevealedFadeRingScratchMatchesCurrent()
        {
            // The ramp alpha depends on revealedEdgeMaxDepth, so a changed max forces a rebuild even
            // when the per-ring membership is identical.
            if (revealedEdgeMaxDepth != currentRevealedFadeEffMax)
            {
                return false;
            }

            var rings = Mathf.Max(scratchRevealedFadeRingCells.Count, currentRevealedFadeRingCells.Count);
            for (var i = 0; i < rings; i++)
            {
                var scratchRing = i < scratchRevealedFadeRingCells.Count ? scratchRevealedFadeRingCells[i] : null;
                var currentRing = i < currentRevealedFadeRingCells.Count ? currentRevealedFadeRingCells[i] : null;
                var scratchCount = scratchRing?.Count ?? 0;
                var currentCount = currentRing?.Count ?? 0;
                if (scratchCount != currentCount)
                {
                    return false;
                }

                if (scratchRing == null || currentRing == null)
                {
                    continue;
                }

                for (var c = 0; c < scratchRing.Count; c++)
                {
                    if (!currentRing.Contains(scratchRing[c]))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private void CommitRevealedFadeRingScratchToCurrent()
        {
            EnsureRevealedFadeRingCapacity();
            for (var i = 0; i < currentRevealedFadeRingCells.Count; i++)
            {
                currentRevealedFadeRingCells[i].Clear();
                pendingRevealedFadeRingCells[i].Clear();
                if (i < scratchRevealedFadeRingCells.Count)
                {
                    currentRevealedFadeRingCells[i].UnionWith(scratchRevealedFadeRingCells[i]);
                    pendingRevealedFadeRingCells[i].AddRange(scratchRevealedFadeRingCells[i]);
                }
            }

            currentRevealedFadeEffMax = revealedEdgeMaxDepth;
        }

        private void ClearCurrentRevealedFadeRings()
        {
            for (var i = 0; i < currentRevealedFadeRingCells.Count; i++)
            {
                currentRevealedFadeRingCells[i].Clear();
                pendingRevealedFadeRingCells[i].Clear();
            }

            currentRevealedFadeEffMax = 0;
        }

        private Material ResolveRevealedFadeRingMaterial(int ring)
        {
            EnsureVisibilityOverlayMaterial();
            var alpha = ResolveRevealedFadeRingAlpha(ring);
            var alphaKey = Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255);
            if (!revealedFadeRingMaterials.TryGetValue(alphaKey, out var material) || material == null)
            {
                var color = new Color(visibilityRevealedFadeColor.r, visibilityRevealedFadeColor.g, visibilityRevealedFadeColor.b, alpha);
                material = CreateMaterial("Atlas Revealed Fade " + alphaKey, color, HexOverlayRenderOrder.VisibilityFogRenderQueue);
                revealedFadeRingMaterials[alphaKey] = material;
            }

            return material;
        }

        // ring 0 (vision edge) maps to the fixed floor opacity regardless of vision range; deeper
        // rings ramp toward 0 (the fully bright center), normalizing the falloff to the lit blob's
        // depth so the farthest faded tile always lands on the same brightness.
        private float ResolveRevealedFadeRingAlpha(int ring)
        {
            var effMax = Mathf.Max(2, revealedEdgeMaxDepth);
            var depth = ring + 1;
            var floor = Mathf.Clamp01(visibilityRevealedFadeFloorAlpha);
            var t = (float)(effMax - depth) / (effMax - 1);
            return floor * Mathf.Clamp01(t);
        }

        private void DestroyRevealedFadeRingMaterials()
        {
            foreach (var material in revealedFadeRingMaterials.Values)
            {
                if (material != null)
                {
                    DestroyUnityObject(material);
                }
            }

            revealedFadeRingMaterials.Clear();
        }

        // --- Player marker: public surface preserved, state owned by PlayerActorVisualProxy ---

        public void SetPlayerPosition(HexCoord coord) => playerActor.SetPosition(coord);

        public void SetPlayerFacingDirectionLocal(Vector3 localForward)
            => playerActor.SetFacingDirectionLocal(localForward);

        public void SetPlayerMoveSpeed(float speed) => playerActor.SetMoveSpeed(speed);

        /// <inheritdoc cref="PlayerActorVisualProxy.FaceTowards"/>
        public void FacePlayerTowards(HexCoord target) => playerActor.FaceTowards(target);

        public void ResetPlayerVisualState() => playerActor.ResetVisualState();

        /// <inheritdoc cref="PlayerActorVisualProxy.WaitForAttackStrike"/>
        public IEnumerator WaitForPlayerAttackStrike(float strikeFraction, float fallbackSeconds, float maxWaitSeconds)
            => playerActor.WaitForAttackStrike(strikeFraction, fallbackSeconds, maxWaitSeconds);

        public bool TryGetPlayerAnimationSpeed(out float speed) => playerActor.TryGetAnimationSpeed(out speed);

        public void SetPlayerAnimationSpeed(float speed) => playerActor.SetAnimationSpeed(speed);

        public void PulsePlayerMove(float speed = 1f, float duration = 0.25f) => playerActor.PulseMove(speed, duration);

        public void TriggerPlayerAttack() => playerActor.TriggerAttack();

        public void TriggerPlayerShield() => playerActor.TriggerShield();

        public void TriggerPlayerBuff() => playerActor.TriggerBuff();

        public void TriggerPlayerField() => playerActor.TriggerField();

        public void TriggerPlayerHit() => playerActor.TriggerHit();

        public void TriggerPlayerKnockback() => playerActor.TriggerKnockback();

        public void TriggerPlayerDead() => playerActor.TriggerDead();

        public IEnumerator AnimatePlayerMove(HexCoord from, HexCoord to, float duration)
            => playerActor.AnimateMove(from, to, duration);

        public IEnumerator AnimatePlayerKnockback(HexCoord from, HexCoord to, float duration)
            => playerActor.AnimateKnockback(from, to, duration);

        // --- IPlayerActorVisualHost ---
        // MapTransform is declared by both host interfaces and implemented explicitly for each: an explicit
        // implementation satisfies only the interface it names, so one line cannot cover both.

        Transform IPlayerActorVisualHost.MapTransform => transform;

        float IPlayerActorVisualHost.PlayerHeightOffset => playerHeightOffset;

        PlayerMarkerAuthoring IPlayerActorVisualHost.GetPlayerMarkerAuthoring()
            => new PlayerMarkerAuthoring(
                playerMarkerPrefab,
                playerMarkerRadius,
                playerVisualLocalOffset,
                playerVisualLocalEulerAngles,
                playerVisualLocalScale);

        Material IPlayerActorVisualHost.CreatePlayerMarkerMaterial()
            => CreateMaterial("Atlas Player Marker", playerMarkerColor);

        public float GetLastFogAmount(HexCoord coord)
        {
            return lastFogAmounts.TryGetValue(coord, out var amount) ? amount : 0f;
        }

        private bool TryCreateChunkedCellTop(HexCellData cell)
        {
            var resolution = catalog == null ? AtlasTileCatalogResolution.Missing() : catalog.Resolve(cell);
            if (!resolution.HasTopPrefab)
            {
                missingVisualDiagnostics.Add(catalog == null
                    ? $"Atlas presentation has no catalog for cell {cell.Coord}; no fallback top visual was rendered."
                    : $"Atlas presentation could not resolve atlas visual ID '{cell.AtlasVisualId}' for cell {cell.Coord}; no fallback top visual was rendered.");
                return true;
            }

            if (!IsChunkEligibleAtlasVisualId(cell.AtlasVisualId) ||
                !IsChunkEligibleAtlasVisualId(resolution.Entry.AtlasVisualId) ||
                !TopChunkDescriptor.TryCreate(resolution.Entry.TopPrefab, out var descriptor))
            {
                return false;
            }

            var position = Project(cell.Coord) + Vector3.up * ResolveTopElevation(cell);
            var rotation = Quaternion.Euler(0f, cell.RotationSteps * 60f, 0f);
            topChunks.Add(descriptor, position, rotation);
            topSurfaceLocalYByCoord[cell.Coord] = descriptor.TopSurfaceLocalY;
            cells[cell.Coord] = AtlasTileVisual.Chunked(cell.Coord, resolution, descriptor.TopSurfaceLocalY);
            TopVisualCount++;
            return true;
        }

        private static bool IsChunkEligibleAtlasVisualId(string atlasVisualId)
        {
            return !string.IsNullOrWhiteSpace(atlasVisualId) &&
                   atlasVisualId.StartsWith("tile-", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsTopPrefabChunkSafeForTests(GameObject prefab)
        {
            return TopChunkDescriptor.TryCreate(prefab, out _);
        }

        /// <summary>
        /// True when the prefab is an animated water top visual, which <see cref="TopChunkDescriptor.TryCreate"/>
        /// refuses to chunk *by design* — water needs a per-tile property block so neighbouring cells can hold
        /// different visibility-lighting values while the Shader Graph animation keeps running. Chunk-safety
        /// gates must treat these as intentionally excluded, not as regressions.
        /// </summary>
        internal static bool IsAnimatedWaterTopPrefabForTests(GameObject prefab)
        {
            return prefab != null &&
                   prefab.GetComponentsInChildren<MeshRenderer>(true)
                       .Any(renderer => renderer != null && IsVisibilityPreservingWaterMaterial(renderer.sharedMaterial));
        }

        private void CreateCellTop(HexCellData cell)
        {
            var resolution = catalog == null ? AtlasTileCatalogResolution.Missing() : catalog.Resolve(cell);
            if (!resolution.HasTopPrefab)
            {
                missingVisualDiagnostics.Add(catalog == null
                    ? $"Atlas presentation has no catalog for cell {cell.Coord}; no fallback top visual was rendered."
                    : $"Atlas presentation could not resolve atlas visual ID '{cell.AtlasVisualId}' for cell {cell.Coord}; no fallback top visual was rendered.");
                return;
            }

            var prefab = resolution.Entry.TopPrefab;
            var position = Project(cell.Coord) + Vector3.up * ResolveTopElevation(cell);
            var rotation = Quaternion.Euler(0f, cell.RotationSteps * 60f, 0f);
            var top = Instantiate(prefab, transform);
            top.name = $"Atlas_Top_{cell.Coord.Q}_{cell.Coord.R}_{prefab.name}";
            top.transform.localPosition = position;
            top.transform.localRotation = rotation;
            var fogRenderers = top.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var fogRenderer in fogRenderers)
            {
                RegisterVisibilityLightingRenderer(fogRenderer);
            }
            var topSurfaceLocalY = ResolveTopSurfaceLocalY(top.transform);
            topSurfaceLocalYByCoord[cell.Coord] = topSurfaceLocalY;
            cells[cell.Coord] = new AtlasTileVisual(
                cell.Coord,
                top,
                resolution,
                Array.Empty<GameObject>(),
                fogRenderers,
                topSurfaceLocalY);
            TopVisualCount++;
        }

        // --- Map object visuals: public surface preserved, state owned by MapObjectVisualRegistry ---

        /// <inheritdoc cref="MapObjectVisualRegistry.SetNightLookWeight"/>
        public void SetMapObjectNightLookWeight(float weight) => mapObjects.SetNightLookWeight(weight);

        public void RefreshMapObjectFade(Camera camera, HexCoord playerCoord)
        {
            RefreshMapObjectFade(camera, new[]
            {
                new MapObjectOcclusionTarget(playerCoord, ProjectTop(playerCoord))
            });
        }

        public void RefreshMapObjectFade(Camera camera, IEnumerable<MapObjectOcclusionTarget> actorTargets)
            => mapObjects.RefreshFade(camera, actorTargets);

        public void RefreshMapObjectHover(Camera camera, Vector2 screenPosition)
        {
            if (camera == null || !camera.pixelRect.Contains(screenPosition))
            {
                ClearMapObjectHover();
                return;
            }

            RefreshMapObjectHoverRay(camera.ScreenPointToRay(screenPosition));
        }

        public void RefreshMapObjectHoverRay(Ray ray) => mapObjects.RefreshHoverRay(ray);

        public bool TryRaycastMapObject(Camera camera, Vector2 screenPosition, out HexMapObjectData objectData)
        {
            objectData = default;
            if (camera == null || !camera.pixelRect.Contains(screenPosition))
            {
                return false;
            }

            return TryRaycastMapObject(camera.ScreenPointToRay(screenPosition), out objectData);
        }

        public bool TryRaycastMapObject(Ray ray, out HexMapObjectData objectData)
            => mapObjects.TryRaycast(ray, out objectData);

        public void ClearMapObjectHover() => mapObjects.ClearHover();

        public bool SetMapObjectVisualActive(string objectId, bool active)
            => mapObjects.SetVisualActive(objectId, active);

        /// <inheritdoc cref="MapObjectVisualRegistry.SetBuildingsOnly"/>
        public void SetMapObjectVisualsBuildingsOnly(bool enabled) => mapObjects.SetBuildingsOnly(enabled);

        public void HideMapObjectVisuals(IEnumerable<string> objectIds) => mapObjects.Hide(objectIds);

        /// <inheritdoc cref="MapObjectVisualRegistry.Consume"/>
        public void ConsumeMapObjectVisual(string objectId) => mapObjects.Consume(objectId);

        /// <summary>소비 표식 되돌림(#20 서비스 디버그 전용 — 원장 리셋과 짝).</summary>
        public void RestoreMapObjectVisual(string objectId) => mapObjects.RestoreConsumed(objectId);

        // --- IMapObjectVisualHost ---

        Transform IMapObjectVisualHost.MapTransform => transform;

        float IMapObjectVisualHost.MapObjectLift => mapObjectLift;

        bool IMapObjectVisualHost.TryGetVisibilitySafeInfo(HexCoord coord, out HexVisibilitySafeCellInfo safeInfo)
            => visibilitySafeInfoCache.TryGetValue(coord, out safeInfo);

        void IMapObjectVisualHost.ReportMissingVisual(string message) => missingVisualDiagnostics.Add(message);

        bool IMapObjectVisualHost.TryResolveMapObjectPrefab(string objectRef, out GameObject prefab)
        {
            prefab = null;
            if (mapObjectCatalogSet != null && mapObjectCatalogSet.TryResolve(objectRef, out prefab))
            {
                return true;
            }

            return MapObjectCatalogSet.TryResolveDefault(objectRef, out prefab);
        }

        private float ResolveVisibilityFogLift()
        {
            return Mathf.Min(visibilityOverlayLift, HexOverlayRenderOrder.VisibilityFogLift);
        }

        private GameObject CreateVisibilityOverlay(Transform parent, float topSurfaceLocalY)
        {
            return CreateVisibilityOverlay(parent, Vector3.up * topSurfaceLocalY);
        }

        private GameObject CreateVisibilityOverlay(Transform parent, Vector3 surfaceLocalPosition)
        {
            EnsureVisibilityOverlayMaterial();
            var overlay = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            overlay.name = "Visibility";
            overlay.transform.SetParent(parent, false);
            overlay.transform.localPosition = surfaceLocalPosition + Vector3.up * ResolveVisibilityFogLift();
            overlay.transform.localRotation = Quaternion.identity;
            overlay.transform.localScale = ResolveParentScaleCompensatedScale(parent, new Vector3(TileRadius * 0.82f, 0.01f, TileRadius * 0.82f));
            var collider = overlay.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            var renderer = overlay.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = visibilityOverlayMaterial;
                ConfigureOverlayRenderer(renderer);
            }

            overlay.SetActive(false);
            return overlay;
        }


        private static void ConfigureOverlayRenderer(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private void ApplyVisibilityState(AtlasTileVisual visual, HexVisibilitySafeCellInfo safeInfo)
        {
            // Trap marker is orthogonal to fog state, so resolve it before the chunked/Revealed
            // early-returns below (otherwise chunked and Revealed cells would never get a marker).
            ApplyTrapMarkerState(visual, safeInfo);

            if (visibilityPresentationMode == VisibilityPresentationMode.LightingMask)
            {
                if (visual.VisibilityOverlay != null)
                {
                    visual.VisibilityOverlay.SetActive(false);
                }

                ApplyVisibilityLightingPropertyState(visual, safeInfo);
                return;
            }

            if (visual.IsChunked)
            {
                if (visual.VisibilityOverlay != null)
                {
                    visual.VisibilityOverlay.SetActive(false);
                }

                ApplyFogMaterialState(visual, safeInfo);
                return;
            }

            // Revealed cells use no per-tile overlay; Revealed edge-fade cells are darkened by their
            // ramped overlay chunk (see BuildVisibilityOverlayChunks) instead.
            if (safeInfo.Visibility == HexCellVisibility.Revealed && safeInfo.Exists)
            {
                if (visual.VisibilityOverlay != null)
                {
                    visual.VisibilityOverlay.SetActive(false);
                }

                ApplyFogMaterialState(visual, safeInfo);
                return;
            }

            var visibilityOverlay = EnsureVisibilityOverlay(visual);
            visibilityOverlay.SetActive(true);
            var renderer = visibilityOverlay.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = ResolveVisibilityMaterial(safeInfo);
            }
            ApplyFogMaterialState(visual, safeInfo);
        }

        private void ClearFogMaterialState(AtlasTileVisual visual)
        {
            lastFogAmounts[visual.Coord] = 0f;
            FogMaterialRefreshCount++;
            foreach (var renderer in visual.FogRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                EnsureFogPropertyBlock();
                fogPropertyBlock.Clear();
                renderer.SetPropertyBlock(fogPropertyBlock);
            }
        }

        private void ApplyVisibilityLightingPropertyState(AtlasTileVisual visual, HexVisibilitySafeCellInfo safeInfo)
        {
            lastFogAmounts[visual.Coord] = 0f;
            FogMaterialRefreshCount++;
            var lighting = safeInfo.Exists
                ? ResolveVisibilityLighting(safeInfo.Visibility)
                : visibilityUnknownLighting;

            foreach (var renderer in visual.FogRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var waterMaterial = ResolveVisibilityPreservingWaterMaterial(renderer);
                EnsureFogPropertyBlock();
                fogPropertyBlock.Clear();
                if (waterMaterial != null)
                {
                    // 물은 시야에 들어와도 밝아지지 않는다(2026-09-05 실플레이 #6). 땅 타일은 블러된
                    // 조명 마스크로 부드럽게 밝아지지만 물은 자기 셰이더를 지키느라 칸 단위 색 스케일을
                    // 받았고, 그래서 강물 한가운데 육각형 밝은 조각이 떠 있었다. 물은 미지 조도 하나로
                    // 고정한다 — 「보이느냐」는 오버레이·규칙이 답하고, 물의 밝기는 그 축이 아니다.
                    var waterLighting = visibilityUnknownLighting;
                    fogPropertyBlock.SetColor(
                        WaterSurfaceColorPropertyId,
                        ScaleColorRgb(waterMaterial.GetColor(WaterSurfaceColorPropertyId), waterLighting));
                    fogPropertyBlock.SetColor(
                        WaterDepthColorPropertyId,
                        ScaleColorRgb(waterMaterial.GetColor(WaterDepthColorPropertyId), waterLighting));
                }

                renderer.SetPropertyBlock(fogPropertyBlock);
            }
        }


        private void BuildVisibilityOverlayChunks()
        {
            CreateVisibilityOverlayChunks(
                pendingUnknownVisibilityChunkCells,
                "Unknown",
                ResolveVisibilityMaterial(HexVisibilitySafeCellInfo.Unknown(default)));
            CreateVisibilityOverlayChunks(
                pendingHintedVisibilityChunkCells,
                "Hinted",
                ResolveVisibilityMaterial(new HexVisibilitySafeCellInfo(
                    default,
                    HexCellVisibility.Hinted,
                    true,
                    true,
                    false,
                    string.Empty,
                    string.Empty,
                    0,
                    false,
                    false,
                    string.Empty,
                    string.Empty)));

            // Revealed edge-fade rings: ring 0 (touching the vision edge) is the darkest (fixed floor),
            // deeper rings ramp toward the fully bright lit center, producing a gradual falloff.
            for (var ring = 0; ring < pendingRevealedFadeRingCells.Count; ring++)
            {
                if (pendingRevealedFadeRingCells[ring].Count == 0)
                {
                    continue;
                }

                CreateVisibilityOverlayChunks(
                    pendingRevealedFadeRingCells[ring],
                    "RevealedFade" + ring,
                    ResolveRevealedFadeRingMaterial(ring));
            }
        }

        private void CreateVisibilityOverlayChunks(IReadOnlyList<HexCoord> coords, string stateName, Material material)
        {
            if (coords == null || coords.Count == 0)
            {
                return;
            }

            var maxCellsPerChunk = Mathf.Max(1, 65535 / VisibilityOverlayVerticesPerCell);
            for (var start = 0; start < coords.Count; start += maxCellsPerChunk)
            {
                var count = Mathf.Min(maxCellsPerChunk, coords.Count - start);
                var mesh = BuildVisibilityOverlayChunkMesh(coords, start, count, $"Atlas Visibility Chunk {stateName}");
                var chunk = new GameObject($"Atlas_VisibilityChunk_{stateName}_{visibilityOverlayChunks.Count:0000}");
                chunk.transform.SetParent(transform, false);
                chunk.transform.localPosition = Vector3.zero;
                chunk.transform.localRotation = Quaternion.identity;
                chunk.transform.localScale = Vector3.one;

                var meshFilter = chunk.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = mesh;
                var renderer = chunk.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                ConfigureOverlayRenderer(renderer);

                visibilityOverlayChunks.Add(chunk);
                visibilityOverlayChunkMeshes.Add(mesh);
                VisibilityChunkRendererCount++;
            }
        }

        private const int VisibilityOverlayVerticesPerCell = 7;

        private Mesh BuildVisibilityOverlayChunkMesh(IReadOnlyList<HexCoord> coords, int start, int count, string meshName)
        {
            var vertices = new List<Vector3>(count * VisibilityOverlayVerticesPerCell);
            var triangles = new List<int>(count * 18);
            var radius = TileRadius * Mathf.Max(0.01f, tileSpacing) * 0.98f;
            var lift = ResolveVisibilityFogLift();

            for (var i = 0; i < count; i++)
            {
                var center = ProjectOverlaySurface(coords[start + i]) + Vector3.up * lift;
                var vertexOffset = vertices.Count;
                vertices.Add(center);
                for (var corner = 0; corner < 6; corner++)
                {
                    var angle = Mathf.Deg2Rad * (30f + corner * 60f);
                    vertices.Add(center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                }

                for (var corner = 0; corner < 6; corner++)
                {
                    triangles.Add(vertexOffset);
                    triangles.Add(vertexOffset + 1 + ((corner + 1) % 6));
                    triangles.Add(vertexOffset + 1 + corner);
                }
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return mesh;
        }

        private GameObject EnsureVisibilityOverlay(AtlasTileVisual visual)
        {
            if (visual.VisibilityOverlay != null)
            {
                return visual.VisibilityOverlay;
            }

            var overlay = visual.IsChunked
                ? CreateVisibilityOverlay(transform, ProjectOverlaySurface(visual.Coord))
                : CreateVisibilityOverlay(visual.Root.transform, visual.TopSurfaceLocalY);
            visual.SetVisibilityOverlay(overlay);
            return overlay;
        }

        private void ApplyTrapMarkerState(AtlasTileVisual visual, HexVisibilitySafeCellInfo safeInfo)
        {
            // Buildings-only mode (victory cinematic) hides trap markers along with other non-building objects.
            if (mapObjects.RestrictToBuildings || !safeInfo.Exists || !safeInfo.TrapRevealed)
            {
                if (visual.TrapMarker != null)
                {
                    visual.TrapMarker.SetActive(false);
                }

                return;
            }

            EnsureTrapMarker(visual).SetActive(true);
        }

        private GameObject EnsureTrapMarker(AtlasTileVisual visual)
        {
            if (visual.TrapMarker != null)
            {
                return visual.TrapMarker;
            }

            var marker = visual.IsChunked
                ? CreateTrapMarker(transform, ProjectOverlaySurface(visual.Coord))
                : CreateTrapMarker(visual.Root.transform, visual.TopSurfaceLocalY);
            visual.SetTrapMarker(marker);
            return marker;
        }

        private GameObject CreateTrapMarker(Transform parent, float topSurfaceLocalY)
        {
            return CreateTrapMarker(parent, Vector3.up * topSurfaceLocalY);
        }

        private GameObject CreateTrapMarker(Transform parent, Vector3 surfaceLocalPosition)
        {
            var prefab = ResolveTrapMarkerPrefab();
            var marker = prefab != null ? Instantiate(prefab, parent) : GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "TrapMarker";
            if (prefab == null)
            {
                EnsureTrapMarkerMaterial();
                marker.transform.SetParent(parent, false);
            }

            marker.transform.localPosition = surfaceLocalPosition + Vector3.up * Mathf.Max(trapMarkerLift, HexOverlayRenderOrder.CombatBoundaryLift);
            marker.transform.localRotation = Quaternion.identity;
            if (prefab == null)
            {
                marker.transform.localScale = ResolveParentScaleCompensatedScale(parent, new Vector3(TileRadius * 0.4f, 0.01f, TileRadius * 0.4f));
            }

            foreach (var collider in marker.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false;
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (prefab == null)
                {
                    renderer.sharedMaterial = trapMarkerMaterial;
                    ConfigureOverlayRenderer(renderer);
                }
            }

            marker.SetActive(false);
            return marker;
        }

        // Resources path (under Assets/Resources/) so the authored trap visual loads in builds too.
        // Without this, builds fell back to the red cylinder primitive because the editor-only
        // AssetDatabase lookup returns null at runtime.
        private const string TrapMarkerResourcesPath = "Object/trap";

        private GameObject ResolveTrapMarkerPrefab()
        {
            if (trapMarkerPrefab != null)
            {
                return trapMarkerPrefab;
            }

            var fromResources = Resources.Load<GameObject>(TrapMarkerResourcesPath);
            if (fromResources != null)
            {
                return fromResources;
            }

#if UNITY_EDITOR
            return UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Object/trap.prefab");
#else
            return null;
#endif
        }

        private void CountVisibilityState(HexVisibilitySafeCellInfo safeInfo)
        {
            if (!safeInfo.Exists)
            {
                VisibilityMissingCount++;
                return;
            }

            switch (safeInfo.Visibility)
            {
                case HexCellVisibility.Revealed:
                    VisibilityVisibleCount++;
                    break;
                case HexCellVisibility.Hinted:
                    VisibilityExploredCount++;
                    break;
                default:
                    VisibilityHiddenCount++;
                    break;
            }
        }

        private void RegisterVisibilityLightingRenderer(Renderer renderer)
        {
            if (renderer == null || originalTopMaterialsByRenderer.ContainsKey(renderer))
            {
                return;
            }

            originalTopMaterialsByRenderer[renderer] = renderer.sharedMaterials;
            ApplyVisibilityLightingMaterialState(renderer);
        }

        private void ApplyVisibilityLightingMaterialState()
        {
            foreach (var pair in originalTopMaterialsByRenderer.ToArray())
            {
                if (pair.Key == null)
                {
                    originalTopMaterialsByRenderer.Remove(pair.Key);
                    continue;
                }

                ApplyVisibilityLightingMaterialState(pair.Key);
            }
        }

        private void ApplyVisibilityLightingMaterialState(Renderer renderer)
        {
            if (renderer == null || !originalTopMaterialsByRenderer.TryGetValue(renderer, out var originalMaterials))
            {
                return;
            }

            if (visibilityPresentationMode != VisibilityPresentationMode.LightingMask)
            {
                renderer.sharedMaterials = originalMaterials;
                return;
            }

            var shader = ResolveVisibilityLightingShader();
            if (shader == null)
            {
                renderer.sharedMaterials = originalMaterials;
                return;
            }

            var maskedMaterials = new Material[originalMaterials.Length];
            for (var i = 0; i < originalMaterials.Length; i++)
            {
                maskedMaterials[i] = IsVisibilityPreservingWaterMaterial(originalMaterials[i])
                    ? originalMaterials[i]
                    : ResolveVisibilityLightingMaterialVariant(originalMaterials[i], shader);
            }

            renderer.sharedMaterials = maskedMaterials;
        }

        private Material ResolveVisibilityPreservingWaterMaterial(Renderer renderer)
        {
            if (renderer == null)
            {
                return null;
            }

            var materials = originalTopMaterialsByRenderer.TryGetValue(renderer, out var originals)
                ? originals
                : renderer.sharedMaterials;
            return materials?.FirstOrDefault(IsVisibilityPreservingWaterMaterial);
        }

        /// <summary>
        /// Animated water: keeps its own per-tile property block instead of a visibility-lighting material
        /// variant. Internal rather than private because <see cref="TopChunkDescriptor.TryCreate"/> also
        /// refuses to batch these — the rule is shared by the lighting path and the chunking path.
        /// </summary>
        internal static bool IsVisibilityPreservingWaterMaterial(Material material)
        {
            return material != null &&
                   material.HasProperty(WaterSurfaceColorPropertyId) &&
                   material.HasProperty(WaterDepthColorPropertyId) &&
                   material.HasProperty(WaterWaveSpeedPropertyId);
        }

        private static Color ScaleColorRgb(Color color, float multiplier)
        {
            var clampedMultiplier = Mathf.Clamp01(multiplier);
            return new Color(
                color.r * clampedMultiplier,
                color.g * clampedMultiplier,
                color.b * clampedMultiplier,
                color.a);
        }

        private Shader ResolveVisibilityLightingShader()
        {
            if (visibilityLightingShader == null)
            {
                visibilityLightingShader = Shader.Find("SeoulPlayup/Map/Visibility Lit");
            }

            return visibilityLightingShader;
        }

        private Material ResolveVisibilityLightingMaterialVariant(Material source, Shader shader)
        {
            if (source == null)
            {
                return null;
            }

            if (visibilityLightingMaterialVariants.TryGetValue(source, out var existing) && existing != null)
            {
                return existing;
            }

            var variant = new Material(shader)
            {
                name = $"{source.name} (Visibility Lighting)",
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = source.enableInstancing,
                renderQueue = source.renderQueue
            };
            variant.CopyPropertiesFromMaterial(source);
            variant.shaderKeywords = source.shaderKeywords;
            visibilityLightingMaterialVariants[source] = variant;
            ownedVisibilityLightingMaterials.Add(variant);
            return variant;
        }

        private void DestroyVisibilityLightingMaterialVariants()
        {
            foreach (var material in ownedVisibilityLightingMaterials)
            {
                if (material != null)
                {
                    DestroyUnityObject(material);
                }
            }

            ownedVisibilityLightingMaterials.Clear();
            visibilityLightingMaterialVariants.Clear();
        }

        private float ResolveVisibilityLighting(HexCellVisibility visibility)
        {
            switch (visibility)
            {
                case HexCellVisibility.Revealed:
                    return visibilityRevealedLighting;
                case HexCellVisibility.Hinted:
                    return visibilityHintedLighting;
                default:
                    return visibilityUnknownLighting;
            }
        }

        internal float SampleVisibilityLightingMaskForTests(HexCoord coord)
            => lightingMask.SampleForTests(coord);

        private Material ResolveVisibilityMaterial(HexVisibilitySafeCellInfo safeInfo)
        {
            if (!safeInfo.Exists || safeInfo.Visibility == HexCellVisibility.Unknown)
            {
                EnsureVisibilityOverlayMaterial();
                return visibilityUnknownMaterial;
            }

            if (safeInfo.Visibility == HexCellVisibility.Hinted)
            {
                EnsureVisibilityOverlayMaterial();
                return visibilityHintedMaterial;
            }

            EnsureVisibilityOverlayMaterial();
            return visibilityOverlayMaterial;
        }

        private void ApplyFogMaterialState(AtlasTileVisual visual, HexVisibilitySafeCellInfo safeInfo)
        {
            var fogAmount = ResolveFogAmount(safeInfo);
            var fogTint = ResolveFogTint(safeInfo);
            lastFogAmounts[safeInfo.Coord] = fogAmount;
            FogMaterialRefreshCount++;

            foreach (var renderer in visual.FogRenderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                ApplyFogPropertyBlock(renderer, fogAmount, fogTint);
            }
        }

        private void ApplyFogPropertyBlock(Renderer renderer, float fogAmount, Color fogTint)
        {
            if (fogAmount <= 0.001f)
            {
                EnsureFogPropertyBlock();
                fogPropertyBlock.Clear();
                renderer.SetPropertyBlock(fogPropertyBlock);
                return;
            }

            EnsureFogPropertyBlock();
            fogPropertyBlock.Clear();
            if (ResolveVisibilityPreservingWaterMaterial(renderer) == null)
            {
                renderer.GetPropertyBlock(fogPropertyBlock);
            }
            fogPropertyBlock.SetFloat(FogAmountPropertyId, fogAmount);
            fogPropertyBlock.SetColor(FogTintPropertyId, fogTint);
            var color = Color.Lerp(Color.white, fogTint, fogAmount);
            fogPropertyBlock.SetColor(BaseColorPropertyId, color);
            fogPropertyBlock.SetColor(ColorPropertyId, color);
            renderer.SetPropertyBlock(fogPropertyBlock);
        }

        private void EnsureFogPropertyBlock()
        {
            if (fogPropertyBlock == null)
            {
                fogPropertyBlock = new MaterialPropertyBlock();
            }
        }

        private float ResolveFogAmount(HexVisibilitySafeCellInfo safeInfo)
        {
            if (!safeInfo.Exists || safeInfo.Visibility == HexCellVisibility.Unknown)
            {
                return Mathf.Clamp01(unknownFogAmount);
            }

            if (safeInfo.Visibility == HexCellVisibility.Hinted)
            {
                return Mathf.Clamp01(hintedFogAmount);
            }

            return 0f;
        }

        private Color ResolveFogTint(HexVisibilitySafeCellInfo safeInfo)
        {
            if (!safeInfo.Exists || safeInfo.Visibility == HexCellVisibility.Unknown)
            {
                return fogUnknownTint;
            }

            if (safeInfo.Visibility == HexCellVisibility.Hinted)
            {
                return fogHintedTint;
            }

            return Color.white;
        }

        private void RebuildSideVisuals()
        {
            sideVisuals.Rebuild(map == null || !renderSideVisuals ? null : EnumerateRenderedCells());
        }

        // The (cell, resolution) pairs the side builder needs, without handing it the tile dictionary.
        private IEnumerable<(HexCellData Cell, AtlasTileCatalogResolution Resolution)> EnumerateRenderedCells()
        {
            foreach (var cell in map.AllCells)
            {
                if (cells.TryGetValue(cell.Coord, out var visual))
                {
                    yield return (cell, visual.Resolution);
                }
            }
        }

        Transform ITileSideVisualHost.MapTransform => transform;

        SideQuad ITileSideVisualHost.ResolveSideQuad(HexCellData cell, int direction, int heightLevel)
            => ResolveSideQuad(cell, direction, heightLevel);

        int ITileSideVisualHost.ResolveNeighborHeight(HexCoord neighborCoord)
            => map != null && map.TryGetCell(neighborCoord, out var neighbor)
                ? HexCellData.ClampHeight(neighbor.HeightLevel)
                : 0;

        SideMaterialAppearance ITileSideVisualHost.ResolveGeneratedSideAppearance(AtlasTileCatalog.Entry entry)
            => ResolveGeneratedSideAppearance(entry);

        Material ITileSideVisualHost.CreateSideMaterial(string name, Color color, Texture texture)
            => CreateMaterial(name, color, texture: texture);

        void ITileSideVisualHost.RegisterVisibilityLightingRenderer(Renderer renderer)
            => RegisterVisibilityLightingRenderer(renderer);

        void ITileSideVisualHost.DestroyVisualRoot(GameObject root) => DestroyVisualRoot(root);

        void ITileSideVisualHost.DestroyGeneratedAsset(UnityEngine.Object generatedAsset)
            => DestroyUnityObject(generatedAsset);

        Transform ITopChunkVisualHost.MapTransform => transform;

        // Stays a [SerializeField] on the view: authored in four shipping scenes, and board-chunk-audit
        // reads it off the view by name via SerializedObject.FindProperty("topChunkMaxCells").
        int ITopChunkVisualHost.TopChunkMaxCells => topChunkMaxCells;

        void ITopChunkVisualHost.RegisterVisibilityLightingRenderer(Renderer renderer)
            => RegisterVisibilityLightingRenderer(renderer);

        void ITopChunkVisualHost.DestroyVisualRoot(GameObject root) => DestroyVisualRoot(root);

        void ITopChunkVisualHost.DestroyGeneratedAsset(UnityEngine.Object generatedAsset)
            => DestroyUnityObject(generatedAsset);

        Transform IVisibilityLightingMaskHost.MapTransform => transform;

        // Both stay [SerializeField] on the view: authored in MainGameplay/ArtLookdev, and LookdevEnvSync
        // copies them between scenes by name via SerializedObject.FindProperty.
        int IVisibilityLightingMaskHost.MaskResolution => visibilityLightingMaskResolution;

        int IVisibilityLightingMaskHost.MaskBlurPasses => visibilityLightingMaskBlurPasses;

        bool IVisibilityLightingMaskHost.HardVisibilityEdge => visibilityLightingHardEdge;

        float IVisibilityLightingMaskHost.VisibilityEdgeGradation => visibilityLightingEdgeGradation;

        bool IVisibilityLightingMaskHost.HexSnapVisibilityEdge => visibilityLightingHexSnap;

        float IVisibilityLightingMaskHost.EmissionFloor => visibilityEmissionFloor;

        // Snapshotted once per rebuild so the service's per-cell loop needs no interface call.
        VisibilityLightingLevels IVisibilityLightingMaskHost.LightingLevels
            => new VisibilityLightingLevels(visibilityUnknownLighting, visibilityHintedLighting, visibilityRevealedLighting);

        HexMapData IVisibilityLightingMaskHost.Map => map;

        Dictionary<HexCoord, HexVisibilitySafeCellInfo> IVisibilityLightingMaskHost.SafeInfoCache
            => visibilitySafeInfoCache;

        Vector3 IVisibilityLightingMaskHost.Project(HexCoord coord) => Project(coord);

        HexAxialProjection IVisibilityLightingMaskHost.CreateProjection() => CreateAxialProjection();

        void IVisibilityLightingMaskHost.DestroyGeneratedAsset(UnityEngine.Object generatedAsset)
            => DestroyUnityObject(generatedAsset);

        private SideQuad ResolveSideQuad(HexCellData cell, int direction, int heightLevel)
        {
            var center = Project(cell.Coord);
            var bottomY = verticalOffset + heightLevel * HeightStep;
            var topY = verticalOffset + (heightLevel + 1) * HeightStep;
            var cornerA = ResolveSideCornerOffset(direction, -30f);
            var cornerB = ResolveSideCornerOffset(direction, 30f);
            var edge = cornerB - cornerA;
            var tangent = edge.sqrMagnitude <= 0.0001f ? Vector3.zero : edge.normalized;
            var cornerOverlap = Mathf.Max(0f, generatedSideCornerOverlap) * TileRadius * Mathf.Max(0.01f, tileSpacing);
            cornerA -= tangent * cornerOverlap;
            cornerB += tangent * cornerOverlap;
            return new SideQuad(
                new Vector3(center.x + cornerA.x, bottomY, center.z + cornerA.z),
                new Vector3(center.x + cornerA.x, topY, center.z + cornerA.z),
                new Vector3(center.x + cornerB.x, topY, center.z + cornerB.z),
                new Vector3(center.x + cornerB.x, bottomY, center.z + cornerB.z));
        }

        private Vector3 ResolveSideCornerOffset(int direction, float cornerAngleOffsetDegrees)
        {
            var radius = TileRadius * Mathf.Max(0.01f, tileSpacing) * Mathf.Max(0.01f, generatedSideRadiusScale);
            var angle = Mathf.Deg2Rad * (ResolveNeighborAngleDegrees(direction) + cornerAngleOffsetDegrees);
            return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }

        private static float ResolveNeighborAngleDegrees(int direction)
        {
            return direction * 60f;
        }

        private static Vector3 ResolveParentScaleCompensatedScale(Transform parent, Vector3 desiredWorldScale)
        {
            if (parent == null)
            {
                return desiredWorldScale;
            }

            var parentScale = parent.lossyScale;
            return new Vector3(
                DivideByScale(desiredWorldScale.x, parentScale.x),
                DivideByScale(desiredWorldScale.y, parentScale.y),
                DivideByScale(desiredWorldScale.z, parentScale.z));
        }

        private static float DivideByScale(float value, float scale)
        {
            var magnitude = Mathf.Abs(scale);
            return magnitude <= 0.0001f ? value : value / magnitude;
        }

        private void EnsureVisibilityOverlayMaterial()
        {
            if (visibilityOverlayMaterial != null)
            {
                return;
            }

            visibilityOverlayMaterial = CreateMaterial("Atlas Visibility", visibilityUnknownColor, HexOverlayRenderOrder.VisibilityFogRenderQueue);
            visibilityUnknownMaterial = visibilityOverlayMaterial;
            visibilityHintedMaterial = CreateMaterial("Atlas Visibility Hinted", visibilityHintedColor, HexOverlayRenderOrder.VisibilityFogRenderQueue);
        }

        private void EnsureTrapMarkerMaterial()
        {
            if (trapMarkerMaterial != null)
            {
                return;
            }

            // Render above the fog so a discovered trap stays visible through Hinted/Unknown overlays.
            trapMarkerMaterial = CreateMaterial("Atlas Trap Marker", trapMarkerColor, HexOverlayRenderOrder.CombatOverlayRenderQueue);
        }

        private SideMaterialAppearance ResolveGeneratedSideAppearance(AtlasTileCatalog.Entry entry)
        {
            var topAppearance = ResolvePrimaryTopAppearance(entry);
            var texture = entry?.SideTexture != null
                ? entry.SideTexture
                : topAppearance.Texture;
            var color = topAppearance.HasColor
                ? ApplyGeneratedSideTopColorTuning(topAppearance.Color)
                : ApplyGeneratedSideTopColorTuning(generatedSideColor);
            return new SideMaterialAppearance(color, texture);
        }

        private static TopMaterialAppearance ResolvePrimaryTopAppearance(AtlasTileCatalog.Entry entry)
        {
            var renderers = entry?.TopPrefab != null
                ? entry.TopPrefab.GetComponentsInChildren<Renderer>(includeInactive: true)
                : Array.Empty<Renderer>();
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.sharedMaterials == null)
                {
                    continue;
                }

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    return new TopMaterialAppearance(ReadMaterialColor(material), ReadMaterialMainTexture(material), true);
                }
            }

            return TopMaterialAppearance.None;
        }

        /// <summary>
        /// Mirrors <see cref="Material.mainTexture"/> resolution — the [MainTexture]-flagged texture
        /// property first, then "_MainTex" — but stays silent when the shader declares neither.
        /// Reading <c>material.mainTexture</c> directly makes Unity log an error for Shader Graph
        /// materials that legitimately have no main texture (the animated water surface), and the side
        /// visuals only borrow the top texture opportunistically, so "no texture" is a normal answer.
        /// </summary>
        private static Texture ReadMaterialMainTexture(Material material)
        {
            var shader = material != null ? material.shader : null;
            if (shader == null)
            {
                return null;
            }

            var propertyCount = shader.GetPropertyCount();
            for (var i = 0; i < propertyCount; i++)
            {
                if (shader.GetPropertyType(i) != ShaderPropertyType.Texture ||
                    (shader.GetPropertyFlags(i) & ShaderPropertyFlags.MainTexture) == 0)
                {
                    continue;
                }

                return material.GetTexture(shader.GetPropertyNameId(i));
            }

            return material.HasProperty(MainTexPropertyId) ? material.GetTexture(MainTexPropertyId) : null;
        }

        private static Color ReadMaterialColor(Material material)
        {
            if (material.HasProperty(BaseColorPropertyId))
            {
                return material.GetColor(BaseColorPropertyId);
            }

            if (material.HasProperty(ColorPropertyId))
            {
                return material.GetColor(ColorPropertyId);
            }

            return Color.white;
        }

        private Color ApplyGeneratedSideTopColorTuning(Color color)
        {
            var multiplier = Mathf.Max(0f, generatedSideTopColorMultiplier);
            return new Color(
                color.r * generatedSideTopColorTint.r * multiplier,
                color.g * generatedSideTopColorTint.g * multiplier,
                color.b * generatedSideTopColorTint.b * multiplier,
                color.a * generatedSideTopColorTint.a);
        }

        internal static Material CreateRuntimeMaterialForTests(string name, Color color)
        {
            return CreateMaterial(name, color);
        }

        private static Material CreateMaterial(string name, Color color, int transparentRenderQueue = HexOverlayRenderOrder.VisibilityFogRenderQueue, Texture texture = null)
        {
            var shader = ResolveRuntimeMaterialShader();
            var material = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
            material.name = name;
            ApplyMaterialColor(material, color);
            if (texture != null)
            {
                material.mainTexture = texture;
            }

            ConfigureTransparency(material, color, transparentRenderQueue);
            return material;
        }

        private static Shader ResolveRuntimeMaterialShader()
        {
            return Shader.Find("Universal Render Pipeline/Unlit") ??
                   Shader.Find("Universal Render Pipeline/Lit") ??
                   Shader.Find("Sprites/Default") ??
                   Shader.Find("Standard");
        }

        private static void ApplyMaterialColor(Material material, Color color)
        {
            if (material == null)
            {
                return;
            }

            material.color = color;
            if (material.HasProperty(BaseColorPropertyId))
            {
                material.SetColor(BaseColorPropertyId, color);
            }

            if (material.HasProperty(ColorPropertyId))
            {
                material.SetColor(ColorPropertyId, color);
            }
        }

        private static void ConfigureTransparency(Material material, Color color, int transparentRenderQueue)
        {
            if (material == null || color.a >= 0.999f)
            {
                return;
            }

            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = transparentRenderQueue;

            if (material.HasProperty(SurfacePropertyId))
            {
                material.SetFloat(SurfacePropertyId, 1f);
            }

            if (material.HasProperty(BlendPropertyId))
            {
                material.SetFloat(BlendPropertyId, 0f);
            }

            if (material.HasProperty(SrcBlendPropertyId))
            {
                material.SetFloat(SrcBlendPropertyId, (float)BlendMode.SrcAlpha);
            }

            if (material.HasProperty(DstBlendPropertyId))
            {
                material.SetFloat(DstBlendPropertyId, (float)BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty(ZWritePropertyId))
            {
                material.SetFloat(ZWritePropertyId, 0f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.EnableKeyword("_ALPHAPREMULTIPLY_OFF");
        }

        private float ResolveTopElevation(HexCellData cell)
        {
            return HexCellData.ClampHeight(cell.HeightLevel) * HeightStep;
        }

        private static float ResolveTopSurfaceLocalY(Transform root)
        {
            if (root == null)
            {
                return 0f;
            }

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var hasBounds = false;
            var maxLocalY = 0f;
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                var bounds = renderer.bounds;
                var min = bounds.min;
                var max = bounds.max;
                var corners = new[]
                {
                    new Vector3(min.x, min.y, min.z),
                    new Vector3(min.x, min.y, max.z),
                    new Vector3(min.x, max.y, min.z),
                    new Vector3(min.x, max.y, max.z),
                    new Vector3(max.x, min.y, min.z),
                    new Vector3(max.x, min.y, max.z),
                    new Vector3(max.x, max.y, min.z),
                    new Vector3(max.x, max.y, max.z)
                };

                foreach (var corner in corners)
                {
                    var localY = root.InverseTransformPoint(corner).y;
                    if (!hasBounds || localY > maxLocalY)
                    {
                        maxLocalY = localY;
                        hasBounds = true;
                    }
                }
            }

            return hasBounds ? maxLocalY : 0f;
        }

        private void Awake()
        {
            ApplyVisibilitySettingsIfAssigned();
        }

        /// <summary>
        /// Swaps the visibility (암시야) presentation settings at runtime and re-applies them. Used by
        /// <see cref="LookPresetApplier"/> so a stage's look preset can drive the visibility coefficients.
        /// Must be called BEFORE Render — the LightingMask material variants and mask texture are built
        /// during Render, so a change afterwards would require a re-render to take effect.
        /// </summary>
        public void ApplyVisibilitySettings(VisibilityPresentationSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            visibilitySettings = settings;
            ApplyVisibilitySettingsIfAssigned();
        }

        // Runtime toggles (e.g. the lookdev mode button) intentionally win after Awake — the shared
        // asset defines the starting look, not a per-frame lock.
        private void ApplyVisibilitySettingsIfAssigned()
        {
            if (visibilitySettings == null)
            {
                return;
            }

            visibilityPresentationMode = visibilitySettings.VisibilityPresentationMode;
            visibilityUnknownColor = visibilitySettings.VisibilityUnknownColor;
            visibilityHintedColor = visibilitySettings.VisibilityHintedColor;
            visibilityOverlayLift = visibilitySettings.VisibilityOverlayLift;
            fogUnknownTint = visibilitySettings.FogUnknownTint;
            fogHintedTint = visibilitySettings.FogHintedTint;
            unknownFogAmount = visibilitySettings.UnknownFogAmount;
            hintedFogAmount = visibilitySettings.HintedFogAmount;
            visibilityLightingShader = visibilitySettings.VisibilityLightingShader;
            visibilityUnknownLighting = visibilitySettings.VisibilityUnknownLighting;
            visibilityHintedLighting = visibilitySettings.VisibilityHintedLighting;
            visibilityRevealedLighting = visibilitySettings.VisibilityRevealedLighting;
            visibilityEmissionFloor = visibilitySettings.VisibilityEmissionFloor;
            visibilityLightingMaskResolution = visibilitySettings.VisibilityLightingMaskResolution;
            visibilityLightingMaskBlurPasses = visibilitySettings.VisibilityLightingMaskBlurPasses;
            visibilityLightingHardEdge = visibilitySettings.VisibilityLightingHardEdge;
            visibilityLightingEdgeGradation = visibilitySettings.VisibilityLightingEdgeGradation;
            visibilityLightingHexSnap = visibilitySettings.VisibilityLightingHexSnap;
            visibilityRevealedFadeMaxSteps = visibilitySettings.VisibilityRevealedFadeMaxSteps;
            visibilityRevealedFadeFloorAlpha = visibilitySettings.VisibilityRevealedFadeFloorAlpha;
            visibilityRevealedFadeColor = visibilitySettings.VisibilityRevealedFadeColor;
        }

        private void OnDestroy()
        {
            sideVisuals.DestroyAll();
            topChunks.DestroyAll();
            DestroyVisibilityOverlayChunks();
            DestroyRevealedFadeRingMaterials();
            lightingMask.Destroy();
            DestroyVisibilityLightingMaterialVariants();
        }

        private void ClearAll()
        {
            sideVisuals.DestroyAll();
            topChunks.DestroyAll();
            DestroyVisibilityOverlayChunks();
            lightingMask.Destroy();
            foreach (var visual in cells.Values)
            {
                DestroyVisualOverlay(visual.VisibilityOverlay);
                DestroyVisualOverlay(visual.TrapMarker);

                foreach (var side in visual.Sides)
                {
                    if (side != null)
                    {
                        DestroyVisualRoot(side);
                    }
                }

                if (visual.Root != null && !visual.IsChunked)
                {
                    DestroyVisualRoot(visual.Root);
                }
            }

            mapObjects.DestroyAllAndReset(DestroyVisualRoot);

            DestroyOrphanedGeneratedChildren();

            originalTopMaterialsByRenderer.Clear();
            DestroyVisibilityLightingMaterialVariants();

            cells.Clear();
            missingVisualDiagnostics.Clear();
            TopVisualCount = 0;
            FogMaterialRefreshCount = 0;
            FoggedRendererCount = 0;
            VisibilityChunkRendererCount = 0;
            lastFogAmounts.Clear();
            topSurfaceLocalYByCoord.Clear();
            pendingUnknownVisibilityChunkCells.Clear();
            pendingHintedVisibilityChunkCells.Clear();
            lastVisibilityKeyByCoord.Clear();
            lightingMask.InvalidateBounds();
            currentUnknownVisibilityChunkCells.Clear();
            currentHintedVisibilityChunkCells.Clear();
            scratchUnknownVisibilityChunkCells.Clear();
            scratchHintedVisibilityChunkCells.Clear();
            revealedEdgeDepth.Clear();
            revealedEdgeMaxDepth = 0;
            revealedDepthBfsQueue.Clear();
            ClearCurrentRevealedFadeRings();
            foreach (var ring in scratchRevealedFadeRingCells)
            {
                ring.Clear();
            }

            hasLastVisibilityEpoch = false;
            ResetVisibilityCounts();
        }

        private void ResetVisibilityCounts()
        {
            VisibilityMissingCount = 0;
            VisibilityHiddenCount = 0;
            VisibilityExploredCount = 0;
            VisibilityVisibleCount = 0;
            FoggedRendererCount = 0;
        }

        private static void DestroyVisualOverlay(GameObject overlay)
        {
            if (overlay != null)
            {
                DestroyVisualRoot(overlay);
            }
        }

        private void DestroyOrphanedGeneratedChildren()
        {
            for (var i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (IsGeneratedVisualChild(child.gameObject))
                {
                    DestroyVisualRoot(child.gameObject);
                }
            }
        }

        private static bool IsGeneratedVisualChild(GameObject child)
        {
            if (child == null)
            {
                return false;
            }

            var name = child.name;
            // Atlas_ClickCollider_ covers legacy click colliders baked into scenes
            // before P6 removed the physics picking path.
            return name.StartsWith("Atlas_ClickCollider_", StringComparison.Ordinal) ||
                   name.StartsWith("Atlas_Top_", StringComparison.Ordinal) ||
                   name.StartsWith("Atlas_Side_", StringComparison.Ordinal) ||
                   name.StartsWith("Atlas_SideChunk_", StringComparison.Ordinal) ||
                   name.StartsWith("Atlas_TopChunk_", StringComparison.Ordinal) ||
                   name.StartsWith("Atlas_VisibilityChunk_", StringComparison.Ordinal) ||
                   name.StartsWith("Map_Object_", StringComparison.Ordinal) ||
                   string.Equals(name, "Atlas Player Marker", StringComparison.Ordinal);
        }

        private void DestroyVisibilityOverlayChunks()
        {
            foreach (var chunk in visibilityOverlayChunks)
            {
                if (chunk != null)
                {
                    DestroyVisualRoot(chunk);
                }
            }

            visibilityOverlayChunks.Clear();
            foreach (var mesh in visibilityOverlayChunkMeshes)
            {
                if (mesh != null)
                {
                    DestroyUnityObject(mesh);
                }
            }

            visibilityOverlayChunkMeshes.Clear();
            VisibilityChunkRendererCount = 0;
        }

        private static void DestroyVisualRoot(GameObject root)
        {
            DestroyUnityObject(root);
        }

        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }

        private readonly struct TopMaterialAppearance
        {
            public static readonly TopMaterialAppearance None = new TopMaterialAppearance(Color.white, null, false);

            public TopMaterialAppearance(Color color, Texture texture, bool hasColor)
            {
                Color = color;
                Texture = texture;
                HasColor = hasColor;
            }

            public Color Color { get; }
            public Texture Texture { get; }
            public bool HasColor { get; }
        }

        public readonly struct MapObjectOcclusionTarget
        {
            public MapObjectOcclusionTarget(HexCoord coord, Vector3 localPosition)
            {
                Coord = coord;
                LocalPosition = localPosition;
            }

            public HexCoord Coord { get; }
            public Vector3 LocalPosition { get; }
            public bool IsValid => IsFinite(LocalPosition.x) && IsFinite(LocalPosition.y) && IsFinite(LocalPosition.z);

            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }

        private sealed class AtlasTileVisual
        {
            public static AtlasTileVisual Chunked(HexCoord coord, AtlasTileCatalogResolution resolution, float topSurfaceLocalY)
            {
                return new AtlasTileVisual(coord, null, resolution, Array.Empty<GameObject>(), Array.Empty<Renderer>(), topSurfaceLocalY, true);
            }

            public AtlasTileVisual(
                HexCoord coord,
                GameObject root,
                AtlasTileCatalogResolution resolution,
                IReadOnlyList<GameObject> sides,
                IReadOnlyList<Renderer> fogRenderers,
                float topSurfaceLocalY = 0f,
                bool isChunked = false)
            {
                Coord = coord;
                IsChunked = isChunked;
                Root = root;
                Resolution = resolution;
                Sides = sides ?? Array.Empty<GameObject>();
                FogRenderers = fogRenderers ?? Array.Empty<Renderer>();
                TopSurfaceLocalY = topSurfaceLocalY;
            }

            public HexCoord Coord { get; }
            public bool IsChunked { get; }
            public GameObject Root { get; }
            public GameObject VisibilityOverlay { get; private set; }
            public GameObject TrapMarker { get; private set; }
            public AtlasTileCatalogResolution Resolution { get; }
            public IReadOnlyList<GameObject> Sides { get; private set; }
            public IReadOnlyList<Renderer> FogRenderers { get; }
            public float TopSurfaceLocalY { get; }

            public AtlasTileVisual WithSides(IReadOnlyList<GameObject> sides)
            {
                Sides = sides ?? Array.Empty<GameObject>();
                return this;
            }

            public void SetVisibilityOverlay(GameObject overlay)
            {
                VisibilityOverlay = overlay;
            }

            public void SetTrapMarker(GameObject marker)
            {
                TrapMarker = marker;
            }
        }

        private static readonly HexCoord[] NeighborDirections =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1)
        };
    }
}
