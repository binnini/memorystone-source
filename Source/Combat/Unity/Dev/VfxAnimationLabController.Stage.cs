using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// 랩의 <b>패턴 무대</b> 절 — 개편의 알맹이다.
    ///
    /// <para>기존 절(<c>Monster Pattern VFX Tuning</c>)과 무엇이 다른가:</para>
    /// <list type="bullet">
    /// <item>재생이 프리팹 직접 <c>Instantiate</c>가 아니라 <b>프로덕션</b>
    /// <see cref="EffectPresentationController.PlayArea"/>를 탄다 → <b>모드 C(PerTile)가 실제로 보인다</b>.
    /// 옛 경로는 배치를 따로 계산해서 칸별 스폰이라는 개념 자체가 없었다.</item>
    /// <item>어떤 큐가 뜨는지를 <see cref="EffectVfxCatalog.ResolveAll"/>에 묻는다 → 하이브리드(A018)는
    /// 두 큐가 같이 뜨고, 미저작 패턴은 <b>범용 폴백</b>이라고 화면에 적힌다.</item>
    /// <item>형상 칸이 보드에 칠해진다 → 발주서 1순위 스펙("형상이 그대로 읽히는가")을 판정할 수 있다.</item>
    /// <item><c>Prev/Next</c> 스테핑 대신 <b>검색·모드·폴백 필터</b> 목록.</item>
    /// </list>
    /// </summary>
    public sealed partial class VfxAnimationLabController
    {
        private const string ShippingCatalogResourcePath = "Combat/DefaultEffectVfxCatalog";

        private VfxLabStage stage;
        private EffectVfxCatalog shippingCatalog;
        private IReadOnlyList<VfxLabPatternIndex.Entry> stagePatterns;
        private Vector2 stageListScroll;

        private string stageSearch = string.Empty;
        private bool stageShowModeA = true;
        private bool stageShowModeB = true;
        private bool stageShowModeC = true;
        private bool stageFallbackOnly;
        private string stageSelectedPatternId = string.Empty;
        private HexDirection stageDirection = HexDirection.East;
        private string stageStatusLine = "패턴을 고르고 '무대 재생'을 누르면 프로덕션 경로로 재생된다.";

        private VfxLabStage Stage
        {
            get
            {
                if (stage == null)
                {
                    stage = FindObjectOfType<VfxLabStage>();
                }

                if (stage == null)
                {
                    // 씬에 아직 없으면 만들어 붙인다 — 기존 저작 씬에서도 바로 쓰이게 하려는 것이고,
                    // 정식 배치는 씬 빌더(Seoul Playup/Dev/Create VFX Lab Scene)가 한다.
                    var go = new GameObject("VFX Lab Stage");
                    stage = go.AddComponent<VfxLabStage>();
                    if (effectPresentation != null)
                    {
                        stage.SetPresentation(effectPresentation);
                    }
                }

                return stage;
            }
        }

        private EffectVfxCatalog ShippingCatalog
        {
            get
            {
                if (shippingCatalog == null)
                {
                    shippingCatalog = Resources.Load<EffectVfxCatalog>(ShippingCatalogResourcePath);
                }

                return shippingCatalog;
            }
        }

        private void DrawPatternStageSection()
        {
            GUILayout.Space(8f);
            GUILayout.Label("── 패턴 무대 (프로덕션 경로 · 모드 C 포함) ──");

            if (stagePatterns == null)
            {
                ReloadStagePatterns();
            }

            if (ShippingCatalog == null)
            {
                GUILayout.Label("  출하 카탈로그를 못 찾았다 — Resources/Combat/DefaultEffectVfxCatalog");
                return;
            }

            DrawStageFilters();

            var visible = FilterStagePatterns().ToList();
            GUILayout.Label($"  {visible.Count}개 표시 / 전체 {stagePatterns.Count}개");

            stageListScroll = GUILayout.BeginScrollView(stageListScroll, GUILayout.Height(190f));
            foreach (var entry in visible)
            {
                DrawStageListRow(entry);
            }

            GUILayout.EndScrollView();

            DrawStageSelection();
            GUILayout.Label("  " + stageStatusLine);
        }

        private void DrawStageFilters()
        {
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("검색", GUILayout.Width(32f));
                stageSearch = GUILayout.TextField(stageSearch ?? string.Empty, GUILayout.Width(150f));
                if (GUILayout.Button("지우기", GUILayout.Width(52f)))
                {
                    stageSearch = string.Empty;
                }

                stageShowModeA = GUILayout.Toggle(stageShowModeA, "A 임팩트", GUILayout.Width(78f));
                stageShowModeB = GUILayout.Toggle(stageShowModeB, "B 방향성", GUILayout.Width(78f));
                stageShowModeC = GUILayout.Toggle(stageShowModeC, "C 칸모듈", GUILayout.Width(78f));
                stageFallbackOnly = GUILayout.Toggle(stageFallbackOnly, "폴백만", GUILayout.Width(64f));
            }

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("공격 방향", GUILayout.Width(60f));
                foreach (HexDirection dir in Enum.GetValues(typeof(HexDirection)))
                {
                    var on = stageDirection == dir;
                    if (GUILayout.Button((on ? "▶ " : string.Empty) + dir, GUILayout.Width(74f)))
                    {
                        stageDirection = dir;
                    }
                }

                if (GUILayout.Button("CSV 다시 읽기", GUILayout.Width(96f)))
                {
                    ReloadStagePatterns();
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                // 랩 기본 바닥은 1u 사각 격자다 — 헥스 피치(1.732u)와 어긋나 눈이 사각을 기준으로
                // 잡아 버린다. 헥스 보드가 기준자인 동안에는 꺼 두는 게 맞다.
                var hidden = GUILayout.Toggle(Stage.SquareGridHidden, " 랩 기본 바닥 숨기기(헥스 기준자만 보기)");
                if (hidden != Stage.SquareGridHidden)
                {
                    Stage.SetSquareGridHidden(hidden);
                }

                if (GUILayout.Button("보드 다시 만들기", GUILayout.Width(120f)))
                {
                    Stage.RebuildBoard();
                    stageStatusLine = $"보드 재생성 — 반경 {Stage.BoardRadius}칸 · 셀 피치 {VfxLabStage.CellPitch:0.###}u";
                }
            }
        }

        private IEnumerable<VfxLabPatternIndex.Entry> FilterStagePatterns()
        {
            foreach (var entry in stagePatterns)
            {
                var resolved = ResolveStageEntries(entry);
                var mode = DescribeStageMode(resolved);
                var fallback = resolved.Any(e => string.IsNullOrEmpty(e.SourceRef));

                if (stageFallbackOnly && !fallback)
                {
                    continue;
                }

                var allowed = mode switch
                {
                    "C" => stageShowModeC,
                    "B" => stageShowModeB,
                    _ => stageShowModeA,
                };
                if (!allowed)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(stageSearch))
                {
                    var haystack = $"{entry.PatternId} {entry.Pattern.DisplayName} {entry.MonsterId} {entry.MonsterName} {entry.Pattern.ShapeId} {string.Join(" ", entry.CueIds)}";
                    if (haystack.IndexOf(stageSearch, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }
                }

                yield return entry;
            }
        }

        private void DrawStageListRow(VfxLabPatternIndex.Entry entry)
        {
            var resolved = ResolveStageEntries(entry);
            var mode = DescribeStageMode(resolved);
            var fallback = resolved.Any(e => string.IsNullOrEmpty(e.SourceRef));
            var selected = string.Equals(entry.PatternId, stageSelectedPatternId, StringComparison.Ordinal);
            var cues = resolved.Length == 0
                ? "-"
                : string.Join("+", resolved.Select(e => string.IsNullOrEmpty(e.CueId) ? "폴백" : e.CueId));

            var label = $"{(selected ? "▶ " : "   ")}[{mode}] {entry.PatternId} {entry.Pattern.DisplayName}"
                        + $"  ·{entry.MonsterId}  ·{entry.Pattern.ShapeId}  ·{cues}"
                        + (fallback ? "  ⚠폴백" : string.Empty);

            var previous = GUI.color;
            if (fallback)
            {
                GUI.color = new Color(1f, 0.72f, 0.4f);
            }

            if (GUILayout.Button(label, GUILayout.Height(20f)))
            {
                stageSelectedPatternId = entry.PatternId;
                PreviewStageShape(entry);
            }

            GUI.color = previous;
        }

        private void DrawStageSelection()
        {
            var entry = stagePatterns.FirstOrDefault(item =>
                string.Equals(item.PatternId, stageSelectedPatternId, StringComparison.Ordinal));
            if (string.IsNullOrEmpty(entry.PatternId))
            {
                return;
            }

            var cells = entry.ResolveCells(new HexCoord(0, 0), stageDirection);
            var resolved = ResolveStageEntries(entry);

            GUILayout.Label($"  선택: {entry.PatternId} {entry.Pattern.DisplayName} · 형상 {entry.Pattern.ShapeId}"
                            + $" · 덮는 칸 {cells.Count} · 반경 {entry.Pattern.AreaRadius}");
            foreach (var e in resolved)
            {
                var perTile = e.AreaSpawnMode == EffectVfxAreaSpawnMode.PerTile;
                var last = perTile
                    ? EffectPresentationController.ResolvePerTileSpawnDelay(e, Mathf.Max(0, cells.Count - 1))
                    : e.PlaybackDelaySeconds;
                GUILayout.Label($"    {(string.IsNullOrEmpty(e.CueId) ? "<범용 폴백>" : e.CueId)}"
                                + $" · {e.AreaSpawnMode}{(perTile ? $" 보폭 {e.PerTileDelaySeconds:0.###}s" : string.Empty)}"
                                + $" · 앵커 {e.SpawnAnchor} · delay {e.PlaybackDelaySeconds:0.##}s → 마지막 {last:0.###}s"
                                + $" · swr {(e.ScaleWithRadius ? "on" : "off")}");
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("무대 재생 (출하 카탈로그)", GUILayout.Height(24f)))
                {
                    PlayStage(entry, cells, resolved);
                }

                if (GUILayout.Button("형상만 표시", GUILayout.Height(24f)))
                {
                    PreviewStageShape(entry);
                }

                if (GUILayout.Button("VFX 지우기", GUILayout.Height(24f)))
                {
                    Stage.ClearSpawnedVfx();
                    stageStatusLine = "지웠다.";
                }
            }
        }

        private void PreviewStageShape(VfxLabPatternIndex.Entry entry)
        {
            var cells = entry.ResolveCells(new HexCoord(0, 0), stageDirection);
            Stage.HighlightCells(cells);
            stageStatusLine = $"{entry.PatternId} 형상 {entry.Pattern.ShapeId} — {cells.Count}칸 (방향 {stageDirection})";
        }

        private void PlayStage(
            VfxLabPatternIndex.Entry entry,
            IReadOnlyList<HexCoord> cells,
            EffectVfxCatalog.Entry[] resolved)
        {
            if (resolved.Length == 0)
            {
                stageStatusLine = $"{entry.PatternId}: 해소되는 엔트리가 없다.";
                return;
            }

            var resultEvent = entry.BuildEvent(new HexCoord(0, 0), cells);
            var reports = new List<string>();
            Stage.ClearSpawnedVfx();

            // 하이브리드는 엔트리마다 한 번씩 — PlayArea가 PerTile/None을 알아서 갈라 준다.
            foreach (var catalogEntry in resolved)
            {
                var report = Stage.Play(catalogEntry, resultEvent, cells, stageDirection);
                reports.Add(report.Ok ? report.Message : report.Message);
            }

            stageStatusLine = $"{entry.PatternId} 재생 — " + string.Join(" | ", reports);
        }

        private EffectVfxCatalog.Entry[] ResolveStageEntries(VfxLabPatternIndex.Entry entry)
        {
            var catalog = ShippingCatalog;
            if (catalog == null)
            {
                return Array.Empty<EffectVfxCatalog.Entry>();
            }

            var cells = entry.ResolveCells(new HexCoord(0, 0), stageDirection);
            return catalog.ResolveAll(entry.BuildEvent(new HexCoord(0, 0), cells));
        }

        private static string DescribeStageMode(EffectVfxCatalog.Entry[] resolved)
        {
            if (resolved.Any(e => e.AreaSpawnMode == EffectVfxAreaSpawnMode.PerTile))
            {
                return "C";
            }

            if (resolved.Any(e => e.SpawnAnchor == EffectVfxSpawnAnchor.SourceAttack || e.SpawnAnchor == EffectVfxSpawnAnchor.SourceGround))
            {
                return "B";
            }

            return "A";
        }

        private void ReloadStagePatterns()
        {
            stagePatterns = VfxLabPatternIndex.Build();
            shippingCatalog = null;
            stageStatusLine = $"패턴 {stagePatterns.Count}개를 다시 읽었다.";
        }
    }
}
