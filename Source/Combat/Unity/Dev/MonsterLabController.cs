using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// 보스 레이드 확인 랩. 조우(결계) → 페이즈 전환 → 기믹(철조각) → 처치까지 한 판에서 본다.
    ///
    /// <see cref="ObjectLabController"/>와 같은 구조: 씬의 진짜 <see cref="MapCombatController"/>를
    /// 몰고 <b>랩 전용 규칙 지름길을 만들지 않는다</b>. 턴은 프로덕션 <c>EndAction</c>으로 넘기고,
    /// 아레나 진입도 실제 이동 카드로 해야 한다 — 랩에서 되면 실게임에서도 된다.
    ///
    /// 패널이 존재하는 이유의 절반은 <b>순간이동으로는 결계가 닫히지 않는다</b>는 사실을 화면에
    /// 적어 두기 위해서다. 디버그 이동은 <c>TryDebugMovePlayer</c>라 <c>TryPlayerMove</c> 안에 있는
    /// 진입 트리거를 타지 않는다(의도된 설계 — 디버그 이동이 조우를 실수로 터뜨리면 안 된다).
    /// 이걸 모르면 "결계가 안 닫힌다"고 오판하게 되므로, 순간이동 버튼은 아레나 <b>밖</b>까지만 태운다.
    ///
    /// Build-gated: 에디터/개발 빌드에서만 그려진다(<see cref="CombatDebugControlPanel.DebugUiAvailable"/>).
    ///
    /// <para>🔗 <b>VFX 확인은 랩이 둘이다</b>(정본 <c>docs/vfx-lab-guide.md</c>): 여기는
    /// <b>"실게임에서 제대로 터지나"</b>를 예고→집행으로 답하는 자리이고, <b>"이 큐가 제대로 보이나"</b>는
    /// <see cref="VfxAnimationLabController"/>의 패턴 무대 절이 답한다(헥스 기준자·형상 하이라이트·
    /// 튜닝·CSV 저장). 큐를 만들거나 고칠 때는 저쪽에서 돌리고 여기서 한 번 터뜨려 확인한다.</para>
    /// </summary>
    public sealed class MonsterLabController : MonoBehaviour
    {
        [Tooltip("The real combat controller this lab drives. Auto-resolved from the scene when left empty.")]
        [SerializeField] private MapCombatController controller;

        private const int PanelWindowId = 0x0B055;
        private const float MinPanelWidth = 320f;
        private const float MinPanelHeight = 240f;
        private const float ChromeHeight = 92f;
        private const float ButtonHeight = 24f;
        private const int MaxLogLines = 14;

        private Rect panelRect;
        private bool panelRectInitialized;
        private Vector2 scrollPosition;

        /// <summary>패턴 확인 절(§25)의 대상 몬스터. 비면 목록 첫 번째(보스 우선)로 되돌아간다.</summary>
        private string patternLabMonsterId = string.Empty;

        private readonly List<string> eventLog = new List<string>();
        private string lastSeenMessage;
        private bool fogForcedOff;

        /// <summary>패턴 형상 미리보기 텍스처 캐시. 키=형상·조준·몸반경·플레이어 상대칸(색에 영향).</summary>
        private readonly Dictionary<string, Texture2D> shapePreviewCache = new Dictionary<string, Texture2D>();

        private float MaxPanelWidth => Mathf.Max(MinPanelWidth + 40f, Screen.width - 24f);
        private float MaxPanelHeight => Mathf.Max(MinPanelHeight + 40f, Screen.height - 24f);

        private void Awake()
        {
            EnsureController();
        }

        private void OnDestroy()
        {
            ClearShapePreviewCache();
        }

        private void ClearShapePreviewCache()
        {
            foreach (var texture in shapePreviewCache.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            shapePreviewCache.Clear();
        }

        private void Update()
        {
            // 랩에서는 암시야를 절대 보지 않는다: 컨트롤러가 잡히는 즉시 전체 공개를 강제한다.
            // (뷰 계층이 매 리프레시마다 이 플래그를 소비하므로, 켜 두는 한 어떤 구간에서도
            // 어두워질 수 없다 — 임시 공개 강등 같은 시야 규칙은 화면에 닿지 않는다.)
            if (!fogForcedOff && controller != null)
            {
                controller.SetFogDebugVisible(false);
                fogForcedOff = true;
            }

            var message = controller != null ? controller.LastInputMessage : null;
            if (!string.IsNullOrWhiteSpace(message) && message != lastSeenMessage)
            {
                lastSeenMessage = message;
                PushLog(message);
            }
        }

        private void EnsureController()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }
        }

        private void PushLog(string line)
        {
            eventLog.Add($"[{Time.frameCount}] {line}");
            if (eventLog.Count > MaxLogLines)
            {
                eventLog.RemoveAt(0);
            }
        }

        private void OnGUI()
        {
            if (!CombatDebugControlPanel.DebugUiAvailable)
            {
                return;
            }

            if (!panelRectInitialized)
            {
                panelRect = new Rect(12f, 12f, 430f, Mathf.Min(720f, Screen.height - 24f));
                panelRectInitialized = true;
            }

            var drawn = GUILayout.Window(
                PanelWindowId,
                panelRect,
                DrawPanelWindow,
                "Monster Lab",
                GUILayout.Width(panelRect.width),
                GUILayout.Height(panelRect.height));
            panelRect.x = drawn.x;
            panelRect.y = drawn.y;
        }

        private void DrawPanelWindow(int windowId)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"W {panelRect.width:0}", GUILayout.Width(64f));
            panelRect.width = Mathf.Round(GUILayout.HorizontalSlider(panelRect.width, MinPanelWidth, MaxPanelWidth));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"H {panelRect.height:0}", GUILayout.Width(64f));
            panelRect.height = Mathf.Round(GUILayout.HorizontalSlider(panelRect.height, MinPanelHeight, MaxPanelHeight));
            GUILayout.EndHorizontal();
            GUILayout.Label("제목줄 드래그=이동 · W/H 슬라이더=크기");

            var bodyHeight = Mathf.Max(80f, panelRect.height - ChromeHeight);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(bodyHeight));
            DrawBody();
            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0f, 0f, panelRect.width, 20f));
        }

        private void DrawBody()
        {
            if (controller == null)
            {
                GUILayout.Label("MapCombatController: 미발견");
                if (GUILayout.Button("씬에서 찾기"))
                {
                    EnsureController();
                }

                return;
            }

            if (controller.State == null)
            {
                if (GUILayout.Button("전투 초기화", GUILayout.Height(ButtonHeight)))
                {
                    controller.InitializeIntegration();
                }

                return;
            }

            DrawArenaSection();
            DrawBossSection();
            DrawPresentationSection();
            DrawLeapSection();
            DrawPatternSection();
            DrawPropSection();
            DrawTurnSection();
            DrawViewSection();
            DrawLogSection();
        }

        private void DrawArenaSection()
        {
            var state = controller.State;
            GUILayout.Label("── 아레나 / 결계 ──");

            var arena = state.Map.Areas.FirstOrDefault(a => a.IsBossArena);
            if (!arena.IsConfigured)
            {
                GUILayout.Label("이 맵에 보스 아레나가 저작되어 있지 않다.");
                return;
            }

            GUILayout.Label($"아레나 '{arena.Id}' · {arena.Coords.Count}칸 · 보스스폰={arena.BossSpawnRefId}");
            GUILayout.Label(state.IsBossArenaBarrierActive
                ? $"결계: ● 활성 (링 {state.SealedBossArenaBoundaryCoords.Count}칸)"
                : "결계: ○ 열림");
            GUILayout.Label($"플레이어 {state.PlayerCoord} · 아레나 안={arena.Contains(state.PlayerCoord)}");

            GUILayout.Space(4f);
            GUILayout.Label("순간이동은 결계를 닫지 않는다(TryPlayerMove를 우회). 아래 버튼은 아레나 '밖'까지만 태우고, 마지막 한 칸은 이동 카드로 직접 들어갈 것.");

            if (GUILayout.Button("접근로 시작점으로", GUILayout.Height(ButtonHeight)))
            {
                TeleportOutsideArena(arena, farthest: true);
            }

            if (GUILayout.Button("아레나 코앞(결계 링)으로", GUILayout.Height(ButtonHeight)))
            {
                TeleportOutsideArena(arena, farthest: false);
            }
        }

        /// <summary>
        /// 아레나 바깥 셀로 순간이동한다. <paramref name="farthest"/>면 접근로 끝(여러 번 걸어 들어오는
        /// 맛을 보고 싶을 때), 아니면 경계 바로 앞(한 칸만 밟으면 조우가 터지는 자리).
        /// 결계가 이미 닫혀 있으면 링이 막혀 있어 이동이 거부된다 — 그건 정상 동작이다.
        /// </summary>
        private void TeleportOutsideArena(HexMapAreaRef arena, bool farthest)
        {
            var state = controller.State;
            var outside = state.Map.AllCells
                .Select(cell => cell.Coord)
                .Where(coord => !arena.Contains(coord) && state.Map.TryGetCell(coord, out var cell) && cell.BaseWalkable)
                .ToList();
            if (outside.Count == 0)
            {
                PushLog("아레나 밖에 설 수 있는 칸이 없다.");
                return;
            }

            var target = farthest
                ? outside.OrderByDescending(coord => arena.Coords.Min(coord.DistanceTo)).ThenBy(coord => coord).First()
                : outside.OrderBy(coord => arena.Coords.Min(coord.DistanceTo)).ThenBy(coord => coord).First();
            controller.TryDebugMoveTo(target);
        }

        private void DrawBossSection()
        {
            var state = controller.State;
            GUILayout.Space(6f);
            GUILayout.Label("── 보스 ──");

            if (!state.HasBossPhaseTrack)
            {
                GUILayout.Label("보스 페이즈 트랙 없음 (보스 프로필이 없거나 보스가 판에 없다).");
                return;
            }

            foreach (var phase in state.BossPhases)
            {
                // MonsterRuntimeState는 struct라 FirstOrDefault가 null을 주지 않는다 — 존재 여부를 따로 센다.
                var matches = state.Monsters.Where(m => m.Id == phase.BossUnitId).ToList();
                var hp = matches.Count > 0 ? $"{matches[0].Hp}/{matches[0].MaxHp}" : "(없음)";
                var living = matches.Count > 0 && !matches[0].IsDead;
                GUILayout.Label($"{phase.DisplayName} · HP {hp} · {(living ? "생존" : "사망/없음")}");
                GUILayout.Label($"  페이즈 {phase.CurrentPhase}/{phase.PhaseCount} · 지표({phase.PhaseMetric}) {phase.MetricProgress}" +
                                (phase.IsFinalPhase ? " · 최종" : $" → 다음 {phase.NextPhaseThreshold}"));
                GUILayout.Label($"  흡수 스택 {phase.AbsorbedStacks} · 시각배율 {phase.VisualScale:0.##} · BGM {phase.BgmCueId}");
                GUILayout.Label($"  몸 반경 {phase.FootprintRadius} ({(phase.FootprintRadius <= 0 ? "1" : (3 * phase.FootprintRadius * (phase.FootprintRadius + 1) + 1).ToString())}칸)");
            }

            // 성장 재배치(§23). 커진 몸이 결계·기물을 덮으면 보스를 유효 중심으로 당긴다 — 화면에서는
            // 보스가 살짝 미끄러지는 것으로만 보여서, 이 줄이 없으면 "일어났는가"를 확인할 방법이 없다.
            if (!string.IsNullOrEmpty(state.LastBossGrowthRepositionReport))
            {
                GUILayout.Label($"  최근 성장 재배치: {state.LastBossGrowthRepositionReport}");
            }

            // 결계 해제 확인용. 최근접을 치므로 보스 옆에 서 있어야 한다(DebugReplayPlayerAttack 계약).
            if (GUILayout.Button("최근접 대상 즉사시키기 (보스 옆에서 = 결계 해제 확인)", GUILayout.Height(ButtonHeight)))
            {
                if (controller.State.DebugReplayPlayerAttack(lethal: true, out var coord, out var targetId))
                {
                    PushLog($"즉사: {targetId} @ {coord}");
                }
                else
                {
                    PushLog("사거리 안에 대상이 없다 — 먼저 옆으로 이동할 것.");
                }
            }
        }

        /// <summary>
        /// P4d 전환 연출을 버튼 한 번으로 확인한다. "연출만"은 규칙 무변경이라 카메라 감각 튜닝을 위해
        /// 몇 번이고 반복할 수 있고, "실제 전환"은 규칙 경로(스탯·아우라·BGM 이벤트)를 그대로 태운
        /// 뒤 연출을 얹는다 — 실전과의 차이는 트리거(지표 대신 버튼)뿐이다.
        /// </summary>
        private void DrawPresentationSection()
        {
            var state = controller.State;
            GUILayout.Space(6f);
            GUILayout.Label("── 연출 (원클릭) ──");
            if (!state.HasBossPhaseTrack)
            {
                GUILayout.Label("보스 페이즈 트랙 없음 — 연출 확인 불가.");
                return;
            }

            var beatActive = controller.IsBossPhaseTransitionBeatActive || controller.IsBossArenaEncounterBeatActive;
            var previousEnabled = GUI.enabled;
            GUI.enabled = previousEnabled && !beatActive;

            if (GUILayout.Button("조우(입장) 연출만 재생 (규칙 무변경 · 반복 가능)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugPlayBossArenaEncounterBeat(out var report);
                PushLog(report);
            }

            if (GUILayout.Button("페이즈 전환 연출만 재생 (규칙 무변경 · 반복 가능)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugPlayBossPhaseTransitionBeat(out var report);
                PushLog(report);
            }

            var phase = state.BossPhases[0];
            GUI.enabled = previousEnabled && !beatActive && !phase.IsFinalPhase;
            var advanceLabel = phase.IsFinalPhase
                ? "다음 페이즈로 전환 — 최종 페이즈 (전투 리셋으로 되돌릴 것)"
                : $"다음 페이즈로 전환 {phase.CurrentPhase}→{phase.CurrentPhase + 1} (실제 규칙 + BGM + 연출)";
            if (GUILayout.Button(advanceLabel, GUILayout.Height(ButtonHeight)))
            {
                controller.DebugAdvanceBossPhaseWithBeat(out var report);
                PushLog(report);
            }

            GUI.enabled = previousEnabled;
            if (beatActive)
            {
                GUILayout.Label("(연출 재생 중…)");
            }
        }

        /// <summary>
        /// 도약(§17)은 버튼이 트리거가 아니라 <b>위치 관계</b>가 트리거다 — 보스가 지금 자리에서
        /// 플레이어를 못 덮고, 도약 사거리 안에 원판이 통째로 들어가면서 거기서는 덮는 칸이 있을 때 뛴다.
        /// 그래서 이 절은 두 가지를 준다: <b>지금 왜 뛰는가/못 뛰는가</b>(값으로), 그리고 그 조건을
        /// 만족하는 자리로 <b>플레이어를 옮겨 주는</b> 버튼.
        /// </summary>
        private void DrawLeapSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("── 도약 ──");
            GUILayout.Label($"  {controller.State.DebugDescribeBossLeapState()}");

            if (GUILayout.Button("도약 조건 만들기 (플레이어를 뛰게 되는 자리로)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugArrangeBossLeap(out var report);
                PushLog(report);
            }
        }

        /// <summary>
        /// 패턴 확인 절(§25). 대상 몬스터를 하나 고르고, 그 몬스터의 공격 패턴을 <b>한 종씩</b> 본다.
        ///
        /// <para>세 가지를 준다: ①패턴마다 <b>지금 왜 후보인가/아닌가</b>(값으로) · ②<b>강제 커밋</b>
        /// (선택기 우회 — 연출·형상 확인용) · ③<b>조건 만들기</b>(우회 없이 후보가 되는 자리로 이동).
        /// ②는 결정적이라 10종을 훑기 좋고, ③은 "실게임에서 이 상황이 나오는가"에 답한다.</para>
        ///
        /// <para>🔑 예고 표시는 새로 만들지 않는다 — 강제 커밋이 계획을 다시 세우면 <b>기존 공격 예고
        /// 오버레이</b>가 그대로 그 패턴을 그린다.</para>
        /// </summary>
        private void DrawPatternSection()
        {
            var state = controller.State;
            GUILayout.Space(6f);
            GUILayout.Label("── 공격 패턴 확인 ──");

            var targets = state.DebugListPatternLabMonsters();
            if (targets.Count == 0)
            {
                GUILayout.Label("  대상 몬스터가 없다 (기물은 제외된다).");
                return;
            }

            // 선택이 유효하지 않으면(죽었거나 아직 안 골랐으면) 목록 첫 번째 = 보스 우선으로 되돌린다.
            if (!targets.Any(monster => monster.Id == patternLabMonsterId))
            {
                patternLabMonsterId = targets[0].Id;
            }

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("대상:", GUILayout.Width(36f));
                foreach (var monster in targets)
                {
                    var selected = monster.Id == patternLabMonsterId;
                    var label = $"{(selected ? "▶ " : string.Empty)}{monster.DefinitionId} · {monster.Id}";
                    if (GUILayout.Button(label, GUILayout.Height(ButtonHeight)))
                    {
                        patternLabMonsterId = monster.Id;
                    }
                }
            }

            var selectedMonster = targets.First(monster => monster.Id == patternLabMonsterId);
            GUILayout.Label($"  {selectedMonster.DefinitionId} · {selectedMonster.Id} @ {selectedMonster.Coord} · HP {selectedMonster.Hp}/{selectedMonster.MaxHp}"
                            + $" · 플레이어까지 {state.PlayerCoord.DistanceTo(selectedMonster.Coord)}칸");

            GUILayout.Label("  미리보기 = 실제 조준(계획 지점→플레이어) · 빨강=타격 · 파랑=몸통 · 초록=플레이어(노랑=명중)");

            var gates = state.DebugDescribeAttackPatternGates(patternLabMonsterId);
            for (var index = 0; index < gates.Count; index++)
            {
                GUILayout.Label($"  [{index}] {gates[index]}");
                DrawPatternShapePreview(index);
                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Space(20f);
                    if (GUILayout.Button("강제 커밋(예고)", GUILayout.Height(ButtonHeight)))
                    {
                        state.DebugForceAttackPattern(patternLabMonsterId, index, out var report);
                        PushLog(report);
                    }

                    if (GUILayout.Button("조건 만들기", GUILayout.Height(ButtonHeight)))
                    {
                        state.DebugTryArrangeAttackPatternCondition(patternLabMonsterId, index, out var report);
                        PushLog(report);
                    }
                }
            }

            if (GUILayout.Button("강제 커밋 해제 (선택기에 되돌리기)", GUILayout.Height(ButtonHeight)))
            {
                state.DebugClearForcedAttackPattern(patternLabMonsterId, out var report);
                PushLog(report);
            }

            GUILayout.Label("  강제 커밋은 선택기를 <b>우회</b>한다 — 연출·형상 확인용이고, '실제로 이 상황이 나오는가'는 조건 만들기가 답한다.");
            GUILayout.Label("  예고를 확인했으면 아래 '행동 종료'로 집행해 예고=명중을 본다.");
        }

        /// <summary>
        /// 패턴 형상 미리보기(§25). 형상 계산은 <c>DebugTryGetAttackPatternShapePreview</c>가 런타임
        /// 경로(<c>AttackShapeLibrary.GetAffectedCells</c>)로 답하고, 여기서는 그 칸들을 작은 텍스처에
        /// 칠하기만 한다 — 방향이 실제 조준(계획 지점→플레이어)이라 판 위 예고와 같은 그림이 나온다.
        /// 값어치는 <b>커밋하지 않고도</b> 형상들을 나란히 비교하는 데 있다(커밋하면 예고 오버레이가 정답).
        /// </summary>
        private void DrawPatternShapePreview(int patternIndex)
        {
            if (!controller.State.DebugTryGetAttackPatternShapePreview(patternLabMonsterId, patternIndex, out var preview))
            {
                return;
            }

            var texture = GetShapePreviewTexture(preview);
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(24f);
                GUILayout.Label(texture, GUILayout.Width(texture.width), GUILayout.Height(texture.height));
            }
        }

        private Texture2D GetShapePreviewTexture(CombatState.DebugAttackShapePreview preview)
        {
            var playerRel = new HexCoord(
                preview.PlayerCoord.Q - preview.Origin.Q,
                preview.PlayerCoord.R - preview.Origin.R);
            var key = $"{preview.ShapeId}|{(int)preview.AttackDirection}|{preview.FootprintRadius}|{playerRel.Q},{playerRel.R}";
            if (shapePreviewCache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            // 플레이어·조준이 움직일 때마다 키가 바뀐다 — 무한히 쌓이지 않게 가끔 비운다.
            if (shapePreviewCache.Count > 64)
            {
                ClearShapePreviewCache();
            }

            var texture = BuildShapePreviewTexture(preview, playerRel);
            shapePreviewCache[key] = texture;
            return texture;
        }

        private static Texture2D BuildShapePreviewTexture(CombatState.DebugAttackShapePreview preview, HexCoord playerRel)
        {
            var zero = new HexCoord(0, 0);
            var affected = new HashSet<HexCoord>();
            foreach (var cell in preview.AffectedCells)
            {
                affected.Add(new HexCoord(cell.Q - preview.Origin.Q, cell.R - preview.Origin.R));
            }

            var extent = 1 + preview.FootprintRadius;
            foreach (var cell in affected)
            {
                extent = Mathf.Max(extent, zero.DistanceTo(cell));
            }

            if (zero.DistanceTo(playerRel) <= extent + 1)
            {
                extent = Mathf.Max(extent, zero.DistanceTo(playerRel));
            }

            extent = Mathf.Min(extent + 1, 7);

            const float S = 5.5f; // 육각 한 변(px). 뾰족머리: 폭 √3·S · 세로 간격 1.5·S.
            var width = Mathf.CeilToInt(Mathf.Sqrt(3f) * S * (2 * extent + 1)) + 4;
            var height = Mathf.CeilToInt(1.5f * S * (2 * extent) + 2f * S) + 4;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.HideAndDontSave,
            };

            var background = new Color(0.11f, 0.12f, 0.19f, 0.92f);
            var empty = new Color(0.21f, 0.22f, 0.31f, 0.92f);
            var hitColor = new Color(0.91f, 0.31f, 0.25f, 1f);
            var bodyColor = new Color(0.25f, 0.66f, 0.96f, 1f);
            var playerSafe = new Color(0.30f, 0.85f, 0.45f, 1f);
            var playerHit = new Color(1f, 0.82f, 0.25f, 1f);

            var cx = width * 0.5f;
            var cy = height * 0.5f;
            var inradiusSq = 3f * 0.25f * S * S * 0.8f; // (√3/2·S)²의 80% — 격자 틈용.
            for (var py = 0; py < height; py++)
            {
                for (var px = 0; px < width; px++)
                {
                    // 텍스처는 아래가 원점이라 화면 y로 뒤집는다 — r+가 화면 아래로 가야
                    // 웹 도표·판 위 오버레이와 같은 방향으로 읽힌다.
                    var sx = px - cx;
                    var sy = height - 1 - py - cy;
                    var fq = (Mathf.Sqrt(3f) / 3f * sx - sy / 3f) / S;
                    var fr = 2f / 3f * sy / S;
                    var cell = RoundToHex(fq, fr);
                    var color = background;
                    if (zero.DistanceTo(cell) <= extent)
                    {
                        var hx = Mathf.Sqrt(3f) * S * (cell.Q + cell.R * 0.5f);
                        var hy = 1.5f * S * cell.R;
                        var dx = sx - hx;
                        var dy = sy - hy;
                        if (dx * dx + dy * dy <= inradiusSq)
                        {
                            var isHit = affected.Contains(cell);
                            if (cell == playerRel)
                            {
                                color = isHit ? playerHit : playerSafe;
                            }
                            else if (isHit)
                            {
                                color = hitColor;
                            }
                            else if (zero.DistanceTo(cell) <= preview.FootprintRadius)
                            {
                                color = bodyColor;
                            }
                            else
                            {
                                color = empty;
                            }
                        }
                    }

                    texture.SetPixel(px, py, color);
                }
            }

            texture.Apply(false, true);
            return texture;
        }

        /// <summary>축좌표 실수값을 가장 가까운 육각 칸으로 반올림한다(표준 큐브 반올림).</summary>
        private static HexCoord RoundToHex(float fq, float fr)
        {
            var fs = -fq - fr;
            var q = Mathf.RoundToInt(fq);
            var r = Mathf.RoundToInt(fr);
            var s = Mathf.RoundToInt(fs);
            var dq = Mathf.Abs(q - fq);
            var dr = Mathf.Abs(r - fr);
            var ds = Mathf.Abs(s - fs);
            if (dq > dr && dq > ds)
            {
                q = -r - s;
            }
            else if (dr > ds)
            {
                r = -q - s;
            }

            return new HexCoord(q, r);
        }

        private void DrawPropSection()
        {
            var state = controller.State;
            GUILayout.Space(6f);
            var props = state.Monsters.Where(m => MonsterSpawnRoles.IsBossProp(m.SpawnRole) && !m.IsDead).ToList();
            GUILayout.Label($"── 기물(철조각) {props.Count}개 ──");
            foreach (var prop in props)
            {
                GUILayout.Label($"  {prop.Id} @ {prop.Coord} · HP {prop.Hp}/{prop.MaxHp}");
            }

            if (props.Count == 0)
            {
                GUILayout.Label("  (살포는 주기적이다 — 주기가 돌아올 때까지 턴을 넘길 것)");
            }

            // 볼리가 잘렸는지 여기서 바로 보인다. 이 줄이 없으면 "왜 5개가 아니라 3개지"를 추적할 방법이 없다.
            if (!string.IsNullOrEmpty(state.LastBossPropVolleyReport))
            {
                GUILayout.Label($"  최근 살포: {state.LastBossPropVolleyReport}");
            }
        }

        private void DrawTurnSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label($"── 턴 {controller.State.OverallTurnNumber} · {controller.State.Phase} ──");
            // ⚠️ 이 줄은 저작을 손으로 옮겨 적은 것이라 조용히 낡는다(실제로 낡아 있었다 —
            // 성숙 2턴·임계 100/200은 §9.2와 §17.3 이전 값이다). 저작을 바꾸면 여기도 고칠 것.
            GUILayout.Label("기믹은 몬스터 행동에서 돈다. 철조각 성숙 3턴 · 개당 15스택 · 임계 70/150이라 첫 흡수(5개×15=75)만으로 2페이즈에 들어간다.");

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("행동 종료 ×1", GUILayout.Height(ButtonHeight)))
                {
                    controller.EndAction();
                }

                if (GUILayout.Button("×5", GUILayout.Height(ButtonHeight)))
                {
                    for (var i = 0; i < 5; i++)
                    {
                        controller.EndAction();
                    }
                }
            }
        }

        private void DrawViewSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("── 보기 ──");
            // 토글을 두지 않는다: 랩에서는 암시야가 존재하면 안 된다(Update가 상시 강제).
            GUILayout.Label("안개: 상시 OFF (랩 강제 — 암시야 없음)");

            if (GUILayout.Button(controller.IsClickMoveDebugModeEnabled ? "● 클릭 순간이동 ON" : "클릭 순간이동 OFF", GUILayout.Height(ButtonHeight)))
            {
                controller.ToggleClickMoveDebugMode();
            }

            if (GUILayout.Button("전투 리셋 (처음부터)", GUILayout.Height(ButtonHeight)))
            {
                controller.InitializeIntegration();
                PushLog("전투를 초기화했다.");
            }
        }

        private void DrawLogSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("── 로그 ──");
            foreach (var line in eventLog)
            {
                GUILayout.Label(line);
            }
        }
    }
}
