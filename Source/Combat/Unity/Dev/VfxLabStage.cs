using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// VFX 랩의 <b>무대</b> — 헥스 셀 기준자 보드 + 프로덕션 경로 재생을 담당한다.
    ///
    /// <para>존재 이유 둘:</para>
    /// <para>① <b>배치를 재구현하지 않는다.</b> 재생은 실제
    /// <see cref="EffectPresentationController.PlayArea"/>를 부르고, 튜닝값은 인메모리 카탈로그
    /// 엔트리로 주입한다. 그래서 <b>모드 C(PerTile)가 공짜로 동작</b>하고, 랩에서 맞고 실게임에서
    /// 다른 상황이 구조적으로 생기지 않는다. 개편 전 랩은 프리팹을 직접 Instantiate 하며 위치·회전·
    /// 스케일을 따로 계산했고("Mirrors EffectPresentationController..." 주석이 네 군데 있었다),
    /// 그게 카메라 트랙에서 한 번 밟은 "랩↔실게임 갭"의 재발 조건이었다.</para>
    /// <para>② <b>형상 칸을 눈으로 판정할 수 있게 한다.</b> 발주서의 1순위 스펙이 "공격 형상이 그대로
    /// 읽히는 것"인데, 바닥 격자가 없으면 아무도 그 물음에 답할 수 없다.</para>
    ///
    /// <para>🔑 칸 좌표는 <see cref="AttackShapeLibrary.GetAffectedCells"/>로 얻는다 — 플래너·집행·
    /// 예고 오버레이가 쓰는 <b>바로 그 함수</b>다. 랩이 형상을 따로 파싱하면 저작과 어긋날 수 있다.</para>
    /// </summary>
    [ExecuteAlways]
    public sealed class VfxLabStage : MonoBehaviour
    {
        /// <summary>출하 씬(MainGameplay)의 <c>AtlasTilePresentationView.tileRadius × tileSpacing</c>와 같은 값.
        /// 이웃 셀 중심 거리 = √3 × 이 값 = 1.732u다.</summary>
        public const float ShippingTileRadius = 1f;

        [Tooltip("재생을 위임할 프로덕션 프레젠테이션 컨트롤러. 비면 같은 계층에서 찾는다.")]
        [SerializeField] private EffectPresentationController presentation;

        [Tooltip("기준자 보드 반경(칸). 4면 A022 artillery-3(반경 3)까지 여유 있게 담긴다.")]
        [SerializeField] private int boardRadius = 4;

        [SerializeField] private Color cellColor = new Color(0.106f, 0.141f, 0.220f, 1f);
        [SerializeField] private Color originColor = new Color(0.165f, 0.208f, 0.314f, 1f);
        [SerializeField] private Color hitColor = new Color(1f, 0.231f, 0.278f, 1f);

        /// <summary>
        /// 🔴 반드시 랩 바닥보다 <b>위</b>여야 한다. 랩은 y=0에 평면을, y=0.015에 1u 사각 그리드 선을
        /// 깐다 — 처음에 보드를 y=−0.02에 두는 바람에 평면 아래로 들어가 헥스 칸이 아예 안 보였다.
        /// </summary>
        [SerializeField] private float boardHeight = 0.03f;

        /// <summary>
        /// 랩이 만드는 <b>1u 사각 그리드</b>를 끈다. 헥스 셀 피치는 1.732u라 두 격자가 겹치면
        /// 눈이 사각 쪽을 기준으로 잡아 "한 칸에 맞는가" 판정이 오히려 어려워진다 — 이 보드가
        /// 기준자 노릇을 하는 동안에는 사각 격자가 방해물이다.
        /// </summary>
        [SerializeField] private bool hideSquareLabGrid = true;

        private readonly Dictionary<HexCoord, Renderer> cellRenderers = new Dictionary<HexCoord, Renderer>();
        private readonly List<Vector3> tileWorldScratch = new List<Vector3>();
        private readonly List<HexCoord> cellScratch = new List<HexCoord>();

        private Transform boardRoot;
        private Material cellMaterial;
        private Material originMaterial;
        private Material hitMaterial;
        private EffectVfxCatalog previewCatalog;

        public int BoardRadius => Mathf.Max(1, boardRadius);

        /// <summary>이웃 셀 중심 거리(= 한 칸 지름). 모드 C 모듈 크기 판정의 기준자다.</summary>
        public static float CellPitch => Mathf.Sqrt(3f) * ShippingTileRadius;

        public EffectPresentationController Presentation
        {
            get
            {
                if (presentation == null)
                {
                    presentation = GetComponentInChildren<EffectPresentationController>(true);
                }

                return presentation;
            }
        }

        /// <summary>씬에 저작된 프레젠테이션 컨트롤러를 랩이 넘겨줄 때 쓴다.</summary>
        public void SetPresentation(EffectPresentationController controller)
        {
            presentation = controller;
        }

        public static Vector3 CellToWorld(HexCoord coord)
        {
            new HexAxialProjection(ShippingTileRadius).CoordToWorld(coord, out var x, out var z);
            return new Vector3(x, 0f, z);
        }

        /// <summary>
        /// 패턴 형상이 덮는 칸. 프로덕션 겨냥 함수를 그대로 부른다 — 저작(<c>attack_shapes.csv</c>)이
        /// 바뀌면 랩도 자동으로 따라간다.
        /// </summary>
        public static IReadOnlyList<HexCoord> ResolveShapeCells(
            string shapeId,
            HexCoord origin,
            HexDirection direction,
            int footprintRadius)
        {
            var cells = new List<HexCoord>();
            if (string.IsNullOrWhiteSpace(shapeId))
            {
                return cells;
            }

            foreach (var cell in AttackShapeLibrary.GetAffectedCells(shapeId, origin, direction, footprintRadius))
            {
                cells.Add(cell);
            }

            return cells;
        }

        private void OnEnable()
        {
            EnsureBoard();
        }

        public void EnsureBoard()
        {
            if (boardRoot != null && cellRenderers.Count > 0)
            {
                return;
            }

            RebuildBoard();
        }

        public void RebuildBoard()
        {
            ClearBoard();

            cellMaterial = CreateUnlit(cellColor);
            originMaterial = CreateUnlit(originColor);
            hitMaterial = CreateUnlit(hitColor);

            boardRoot = new GameObject("VFX Lab Board").transform;
            boardRoot.SetParent(transform, false);
            boardRoot.gameObject.hideFlags = HideFlags.DontSave;

            var radius = BoardRadius;
            for (var q = -radius; q <= radius; q++)
            {
                for (var r = -radius; r <= radius; r++)
                {
                    if (Mathf.Abs(q + r) > radius)
                    {
                        continue;
                    }

                    var coord = new HexCoord(q, r);
                    var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    DestroyObject(disc.GetComponent<Collider>());
                    disc.name = $"Cell {q},{r}";
                    disc.hideFlags = HideFlags.DontSave;
                    disc.transform.SetParent(boardRoot, false);
                    disc.transform.position = CellToWorld(coord) + new Vector3(0f, boardHeight, 0f);
                    // Unity Cylinder는 지름 2·높이 2다 → 셀 폭의 92%로 깔아 칸 경계가 눈에 보이게 한다.
                    disc.transform.localScale = new Vector3(CellPitch * 0.46f, 0.01f, CellPitch * 0.46f);

                    var renderer = disc.GetComponent<Renderer>();
                    renderer.sharedMaterial = coord.Equals(default) ? originMaterial : cellMaterial;
                    cellRenderers[coord] = renderer;
                }
            }

            ApplySquareGridSuppression();
        }

        /// <summary>
        /// 랩 기본 바닥(1u 사각 그리드 선 + 회색 평면)을 켜고 끈다.
        ///
        /// <para>왜 평면까지 끄나: 그리드 선만 꺼도 <b>회색 사각 평면이 남아</b> 보드보다 작게 잘린
        /// 네모로 보인다(평면은 ±3.5u, 보드는 반경 4칸 ≈ 6.9u). 헥스 칸이 기준자인 화면에 사각
        /// 경계가 남으면 눈이 그쪽을 먼저 잡는다. 어두운 지면은 카메라 배경색이 대신한다 —
        /// 발광 판정을 어두운 바닥 위에서 한다는 커밋된 선택은 그대로 지켜진다.</para>
        /// </summary>
        public void SetSquareGridHidden(bool hidden)
        {
            hideSquareLabGrid = hidden;
            ApplySquareGridSuppression();
        }

        public bool SquareGridHidden => hideSquareLabGrid;

        private void ApplySquareGridSuppression()
        {
            foreach (var root in new[] { "Lab Runtime Ground", "Lab Ground Placeholder" })
            {
                var host = GameObject.Find(root);
                if (host == null)
                {
                    continue;
                }

                foreach (var line in host.GetComponentsInChildren<LineRenderer>(true))
                {
                    line.enabled = !hideSquareLabGrid;
                }

                foreach (var mesh in host.GetComponentsInChildren<MeshRenderer>(true))
                {
                    mesh.enabled = !hideSquareLabGrid;
                }
            }
        }

        /// <summary>형상이 덮는 칸을 붉게 칠한다. 나머지는 기준자 색으로 되돌린다.</summary>
        public void HighlightCells(IReadOnlyList<HexCoord> cells)
        {
            EnsureBoard();
            ClearHighlight();
            if (cells == null)
            {
                return;
            }

            foreach (var cell in cells)
            {
                if (cellRenderers.TryGetValue(cell, out var renderer) && renderer != null)
                {
                    renderer.sharedMaterial = hitMaterial;
                }
            }
        }

        public void ClearHighlight()
        {
            foreach (var pair in cellRenderers)
            {
                if (pair.Value == null)
                {
                    continue;
                }

                pair.Value.sharedMaterial = pair.Key.Equals(default) ? originMaterial : cellMaterial;
            }
        }

        /// <summary>
        /// 큐 하나를 <b>프로덕션 경로로</b> 재생한다. <paramref name="entry"/>는 출하 카탈로그 엔트리든
        /// 미저장 튜닝으로 만든 임시 엔트리든 상관없다 — 어느 쪽이든 같은
        /// <see cref="EffectPresentationController.PlayArea"/>를 통과하므로 PerTile·앵커·delay가
        /// 실게임과 동일하게 나온다.
        /// </summary>
        public VfxLabPlayReport Play(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            IReadOnlyList<HexCoord> cells,
            HexDirection direction)
        {
            var controller = Presentation;
            if (controller == null || entry == null)
            {
                return VfxLabPlayReport.Failed("VfxLabStage: EffectPresentationController 또는 엔트리가 없다.");
            }

            EnsureBoard();
            // 랩 바닥은 Start에서 만들어져 보드 생성보다 늦을 수 있다 — 실제로 쓰는 시점에 다시 건다.
            ApplySquareGridSuppression();
            HighlightCells(cells);

            if (previewCatalog == null)
            {
                previewCatalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
                previewCatalog.hideFlags = HideFlags.DontSave;
            }

            previewCatalog.SetEntries(entry);
            controller.SetVfxCatalogForTests(previewCatalog);

            tileWorldScratch.Clear();
            cellScratch.Clear();
            if (cells != null)
            {
                foreach (var cell in cells)
                {
                    cellScratch.Add(cell);
                    tileWorldScratch.Add(CellToWorld(cell));
                }
            }

            var centerWorld = resultEvent.Center.HasValue ? CellToWorld(resultEvent.Center.Value) : Vector3.zero;
            var facing = ResolveFacing(direction);
            controller.PlayArea(resultEvent, centerWorld, tileWorldScratch, facing);

            return VfxLabPlayReport.Succeeded(entry, resultEvent, cellScratch, tileWorldScratch, facing, centerWorld);
        }

        /// <summary>
        /// 공격 방향의 스폰 회전. 🔴 프리팹 로컬 <b>+Z</b>가 이 방향에 얹힌다(+X가 아니다) —
        /// 런타임과 같은 <see cref="CombatFacingUtility.ResolveHexSideRotation"/>을 쓴다.
        /// </summary>
        public static Quaternion ResolveFacing(HexDirection direction)
        {
            var target = CellToWorld(new HexCoord(0, 0).Neighbor(direction));
            return CombatFacingUtility.ResolveHexSideRotation(target - Vector3.zero);
        }

        public void ClearSpawnedVfx()
        {
            var controller = Presentation;
            if (controller != null)
            {
                controller.ClearSpawnedEffects();
            }
        }

        private void ClearBoard()
        {
            cellRenderers.Clear();
            if (boardRoot != null)
            {
                DestroyObject(boardRoot.gameObject);
                boardRoot = null;
            }

            DestroyObject(cellMaterial);
            DestroyObject(originMaterial);
            DestroyObject(hitMaterial);
            cellMaterial = null;
            originMaterial = null;
            hitMaterial = null;
        }

        private static Material CreateUnlit(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader) { hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private static void DestroyObject(Object target)
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
    }

    /// <summary>랩 재생 1회의 실측 요약. 패널 표시와 캡처 리포트(JSON)가 같은 값을 쓴다.</summary>
    public readonly struct VfxLabPlayReport
    {
        private VfxLabPlayReport(
            bool ok,
            string message,
            string cueId,
            int tileCount,
            float perTileDelaySeconds,
            float playbackDelaySeconds,
            bool perTile,
            bool scaleWithRadius,
            Vector3 centerWorld,
            Quaternion facing)
        {
            Ok = ok;
            Message = message ?? string.Empty;
            CueId = cueId ?? string.Empty;
            TileCount = tileCount;
            PerTileDelaySeconds = perTileDelaySeconds;
            PlaybackDelaySeconds = playbackDelaySeconds;
            PerTile = perTile;
            ScaleWithRadius = scaleWithRadius;
            CenterWorld = centerWorld;
            Facing = facing;
        }

        public bool Ok { get; }
        public string Message { get; }
        public string CueId { get; }
        public int TileCount { get; }
        public float PerTileDelaySeconds { get; }
        public float PlaybackDelaySeconds { get; }
        public bool PerTile { get; }
        public bool ScaleWithRadius { get; }
        public Vector3 CenterWorld { get; }
        public Quaternion Facing { get; }

        /// <summary>마지막 칸이 뜨는 시각. PerTile 판정은 평균 보폭이 아니라 <b>이 값</b>으로 한다 —
        /// 시작 히치 프레임에 앞쪽 칸이 뭉쳐 평균은 언제나 작게 나온다.</summary>
        public float LastSpawnSeconds =>
            PlaybackDelaySeconds + Mathf.Max(0, TileCount - 1) * PerTileDelaySeconds;

        public static VfxLabPlayReport Failed(string message) =>
            new VfxLabPlayReport(false, message, null, 0, 0f, 0f, false, false, Vector3.zero, Quaternion.identity);

        public static VfxLabPlayReport Succeeded(
            EffectVfxCatalog.Entry entry,
            EffectResultEvent resultEvent,
            IReadOnlyList<HexCoord> cells,
            IReadOnlyList<Vector3> tiles,
            Quaternion facing,
            Vector3 centerWorld)
        {
            var perTile = entry.AreaSpawnMode == EffectVfxAreaSpawnMode.PerTile;
            return new VfxLabPlayReport(
                true,
                perTile
                    ? $"{entry.CueId}: PerTile {tiles.Count}칸 · 보폭 {entry.PerTileDelaySeconds:0.###}s"
                    : $"{entry.CueId}: 단발(None) · 중심 1발",
                entry.CueId,
                perTile ? tiles.Count : 1,
                entry.PerTileDelaySeconds,
                entry.PlaybackDelaySeconds,
                perTile,
                entry.ScaleWithRadius,
                centerWorld,
                facing);
        }
    }
}
