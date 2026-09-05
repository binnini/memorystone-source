using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Presentation;
using SeoulPlayup.Map.Runtime;
using UnityEngine;
using static SeoulPlayup.Combat.Unity.CombatCameraController;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Dedicated lab for camera presentation work (docs/monster-action-camera-focus-plan.md; later the stage
    /// intro, victory cinematic, death zoom and trailer poses belong here too).
    ///
    /// The problem it solves: the action-focus gate is pure geometry — "is this world point inside the
    /// frustum right now" — and geometry is exactly the thing you cannot check by watching. Producing one
    /// genuine off-screen event in real play costs a field card, several turns of walking away, and the
    /// right vision state, and even then the verdict is invisible. This panel makes the gate readable: every
    /// actor's hex distance, its viewport coordinates, and its framed verdict at both margins, live.
    ///
    /// Like <see cref="CombatTimingLabController"/> it drives the REAL controller/state rather than a
    /// synthetic mock, so the ring rig below spawns through the same sandbox seam live combat uses and the
    /// monster phase it advances is the real one. That matters for the P0 measurement: hand-placed actors
    /// would only measure the distances we chose, while cluster counts have to come from real monster AI and
    /// real vision.
    ///
    /// Build-gated behind <see cref="CombatDebugControlPanel.DebugUiAvailable"/>, so release builds never
    /// draw it.
    /// </summary>
    public sealed class CameraLabController : MonoBehaviour
    {
        [Tooltip("The real combat controller this lab drives. Auto-resolved from the scene when left empty.")]
        [SerializeField] private MapCombatController controller;

        private const int PanelWindowId = 0x5CA31;
        private const float MinPanelWidth = 320f;
        private const float MinPanelHeight = 240f;
        private const float ChromeHeight = 92f;
        private const float ButtonHeight = 24f;
        private const int MaxLogLines = 8;

        // The rings the rig spawns on. Chosen against the measured rig geometry (§2.1): r2 is always framed,
        // r5 straddles the horizontal edge (half-width is 5.45 hexes), r8 and r12 are the field-tick /
        // walked-away distances the feature exists for.
        private static readonly int[] RigRings = { 2, 5, 8, 12 };

        private Rect panelRect;
        private bool panelRectInitialized;
        private Vector2 scrollPosition;
        private string[] monsterIds = System.Array.Empty<string>();
        private int monsterIndex;
        private int rigHp = 30;
        private int scenarioFieldDistance = 10;
        private const int scenarioFieldRadius = 2;
        private const int scenarioFieldTurns = 6;
        private readonly List<string> eventLog = new List<string>();
        private readonly List<string> rigSpawnedIds = new List<string>();

        private float MaxPanelWidth => Mathf.Max(MinPanelWidth + 40f, Screen.width - 24f);
        private float MaxPanelHeight => Mathf.Max(MinPanelHeight + 40f, Screen.height - 24f);

        public MapCombatController Controller => controller;

        private void Awake() => EnsureController();

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
            // Also honours the debug-UI hide toggle, not just the build gate: this panel is IMGUI, so it burns
            // into recorded frames and no CanvasGroup can reach it. Recording a take through the lab is a
            // first-class use of the lab, so leaking the panel into the plate would defeat it.
            if (!CombatDebugControlPanel.DebugUiAvailable || DebugUiVisibilityState.ImmediateModeDebugUiHidden)
            {
                return;
            }

            if (!panelRectInitialized)
            {
                // Right-hand side: the scene keeps the standard CombatDebugControlPanel on the left, because
                // camera work leans on its slow-motion and fog toggles.
                panelRect = new Rect(
                    Mathf.Max(12f, Screen.width - 452f), 12f, 440f, Mathf.Min(760f, Screen.height - 24f));
                panelRectInitialized = true;
            }

            var drawn = GUILayout.Window(
                PanelWindowId,
                panelRect,
                DrawPanelWindow,
                "Camera Lab",
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
                if (GUILayout.Button("씬에서 찾기", GUILayout.Height(ButtonHeight)))
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

            DrawFramingSection();
            DrawZoomSection();
            DrawRigSection();
            DrawScenarioSection();
            DrawFocusToggleSection();
            DrawMetricsSection();
            DrawLogSection();
        }

        /// <summary>
        /// The core readout: the same <see cref="IsPointFramed"/> decision the action-focus sink will make,
        /// printed per actor. "밖"/"안" here is literally the gate's answer, not an approximation of it.
        /// </summary>
        private void DrawFramingSection()
        {
            GUILayout.Label("■ 프레이밍 판독 (액션 포커스 게이트와 같은 판정)");

            var camera = controller.DebugGameplayCamera;
            if (camera == null)
            {
                GUILayout.Label("gameplay 카메라 미배선 — 판독 불가");
                return;
            }

            var frame = CameraFrame.FromCamera(camera);
            if (!frame.IsValid)
            {
                GUILayout.Label("카메라 프레임이 유효하지 않음(aspect/FOV 0)");
                return;
            }

            var margin = controller.ActionFocusMetricsMarginFraction;
            GUILayout.Label($"margin {margin:0.##} · aspect {frame.Aspect:0.00} · FOV {frame.VerticalFieldOfViewDegrees:0.#}");

            var state = controller.State;
            DrawFramingRow("player", state.PlayerCoord, frame, margin);

            var offScreen = 0;
            var living = 0;
            foreach (var monster in state.Monsters)
            {
                if (monster.IsDead)
                {
                    continue;
                }

                living++;
                if (!DrawFramingRow(monster.Id, monster.Coord, frame, margin))
                {
                    offScreen++;
                }
            }

            if (living == 0)
            {
                GUILayout.Label("  살아있는 몬스터 없음 — 아래 링 리그로 배치하세요.");
                return;
            }

            GUILayout.Label($"  → 살아있는 몬스터 {living}마리 중 화면 밖 {offScreen}마리");
        }

        /// <summary>Returns the margin-applied verdict, which is the one the gate will use.</summary>
        private bool DrawFramingRow(string label, HexCoord coord, CameraFrame frame, float margin)
        {
            if (!controller.TryGetTileWorldPosition(coord, out var world))
            {
                GUILayout.Label($"  {label}  {coord}  (월드 좌표 없음)");
                return true;
            }

            var distance = controller.State.PlayerCoord.DistanceTo(coord);
            var framedStrict = IsPointFramed(frame, world);
            var framedMargin = IsPointFramed(frame, world, margin);
            var viewport = TryProjectToViewport(frame, world, out var vx, out var vy)
                ? $"({vx:0.00}, {vy:0.00})"
                : "(카메라 뒤)";

            // Both verdicts are shown because the margin is still a provisional tunable: the pair makes the
            // events that only just clip the screen edge — the ones the margin exists for — visible as a row
            // where the two disagree.
            var verdict = framedMargin ? "안" : (framedStrict ? "밖(마진)" : "밖");
            GUILayout.Label($"  {label}  r{distance}  {verdict}  뷰포트 {viewport}");
            return framedMargin;
        }

        private void DrawZoomSection()
        {
            GUILayout.Space(6f);
            var zoom = controller.DebugCameraZoomDistance;
            if (zoom < 0f)
            {
                GUILayout.Label("■ 줌 — Cinemachine binder 미배선");
                return;
            }

            var min = controller.DebugCameraMinZoomDistance;
            var max = controller.DebugCameraMaxZoomDistance;

            // Framing is strongly zoom-dependent (§2.1: 49% of a radius-7 disk on screen at max zoom-in vs
            // 90% at max zoom-out), so any framing verdict above has to be read together with this number.
            GUILayout.Label($"■ 줌 거리: {zoom:0.0}  (min {min:0.0} / max {max:0.0})");
            var next = GUILayout.HorizontalSlider(zoom, min, max);
            if (!Mathf.Approximately(next, zoom))
            {
                controller.DebugSetCameraZoomDistance(next);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"최대 줌인 ({min:0.0})", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSetCameraZoomDistance(min);
            }
            if (GUILayout.Button("기본", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSetCameraZoomDistance(11.4f);
            }
            if (GUILayout.Button($"최대 줌아웃 ({max:0.0})", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugSetCameraZoomDistance(max);
            }
            GUILayout.EndHorizontal();
        }

        private void DrawRigSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("■ 거리 리그 — 플레이어 기준 링에 몬스터 배치");

            RefreshMonsterIds();
            if (monsterIds.Length == 0)
            {
                GUILayout.Label("몬스터 카탈로그 비어 있음");
                return;
            }

            monsterIndex = Mathf.Clamp(monsterIndex, 0, monsterIds.Length - 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", GUILayout.Width(26f), GUILayout.Height(ButtonHeight)))
            {
                monsterIndex = (monsterIndex - 1 + monsterIds.Length) % monsterIds.Length;
            }
            GUILayout.Label(monsterIds[monsterIndex], GUILayout.ExpandWidth(true));
            if (GUILayout.Button(">", GUILayout.Width(26f), GUILayout.Height(ButtonHeight)))
            {
                monsterIndex = (monsterIndex + 1) % monsterIds.Length;
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label($"HP {rigHp}", GUILayout.Width(64f));
            rigHp = Mathf.RoundToInt(GUILayout.HorizontalSlider(rigHp, 1f, 200f));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"링 배치 (r{string.Join("/r", RigRings)})", GUILayout.Height(ButtonHeight)))
            {
                SpawnRing();
            }
            if (GUILayout.Button("리그 정리", GUILayout.Height(ButtonHeight)))
            {
                var removed = controller.DebugSandboxRemoveSpawnedMonsters();
                rigSpawnedIds.Clear();
                PushLog($"리그 정리: {removed}마리 제거");
            }
            GUILayout.EndHorizontal();

            // ⚠ View filter only. Measured 2026-07-30: with revealAllDebug=true every monster still reported
            // vis=Hinted / IsMonsterVisible=false, because revealAllMapCellsInDebugMode is consumed purely by
            // the view layer and never reaches CombatState. Turning fog off lets you SEE a distant monster; it
            // does not make it presentable, so it produces no action event either way. Use the scenario
            // section below to create real vision.
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("보기용 전체 공개 (연출엔 영향 없음)", GUILayout.Height(ButtonHeight)))
            {
                controller.SetFogDebugVisible(false);
                PushLog("안개 OFF — 화면에만 보입니다. 연출/시야 판정은 그대로");
            }
            if (GUILayout.Button("안개 복원", GUILayout.Height(ButtonHeight)))
            {
                controller.SetFogDebugVisible(true);
                PushLog("안개 ON");
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Builds the situation the feature exists for: a lit patch of map far from the player, with monsters
        /// in it. A field object is the mechanism because it is re-unioned into the revealed set every vision
        /// refresh while it lives, so the tiles stay visible after the player walks away — which is what makes
        /// a distant monster's action get presented at all. (Scout reaches only r4: range 3 + blast-1.)
        /// </summary>
        private void DrawScenarioSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("■ 시나리오 (a) — 원거리 필드 + 그 안의 몬스터");
            GUILayout.Label($"필드 거리 r{scenarioFieldDistance} · 반경 {scenarioFieldRadius} · {scenarioFieldTurns}턴");

            GUILayout.BeginHorizontal();
            GUILayout.Label("거리", GUILayout.Width(40f));
            scenarioFieldDistance = Mathf.RoundToInt(GUILayout.HorizontalSlider(scenarioFieldDistance, 4f, 13f));
            GUILayout.EndHorizontal();

            if (GUILayout.Button("필드 설치 + 몬스터 배치", GUILayout.Height(ButtonHeight)))
            {
                InstallDistantFieldScenario();
            }

            GUILayout.Label("설치 후 턴을 진행하면 필드 틱이 그 몬스터들을 때립니다 — 그게 화면 밖 이벤트입니다.");

            GUILayout.Space(6f);
            GUILayout.Label("■ 시연 프리셋 — 계획 §10.15.2로 촬영한 4종을 그대로 재현");
            GUILayout.Label("⚠ 누른 뒤 반드시 '한 턴 무녹화 진행' → 그 다음 턴부터 관찰(설치한 턴엔 카메라가 안 움직입니다)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("A 다중 클러스터", GUILayout.Height(ButtonHeight)))
            {
                InstallDemoPreset("A");
            }
            if (GUILayout.Button("B 원거리 이동·공격", GUILayout.Height(ButtonHeight)))
            {
                InstallDemoPreset("B");
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("C 원거리 사망", GUILayout.Height(ButtonHeight)))
            {
                InstallDemoPreset("C");
            }
            if (GUILayout.Button("D 턴 시작 복귀", GUILayout.Height(ButtonHeight)))
            {
                InstallDemoPreset("D");
            }
            GUILayout.EndHorizontal();
        }

        /// <summary>
        /// Rebuilds one of the four demo situations from plan §10.15.2 in a single click.
        ///
        /// Exists because the scenario button above installs on ONE bearing
        /// (<see cref="HexCoord.Directions"/>[0]), so the headline case — several clusters at different
        /// bearings, which is what makes coalescing and the per-phase budget observable — could not be built
        /// from the panel at all. Reproducing what was filmed had to be possible by hand, not only by script.
        ///
        /// Each preset resets the fight first: presets differ in field damage and monster HP, so leftovers
        /// from a previous one would quietly change what the next one shows.
        /// </summary>
        private void InstallDemoPreset(string preset)
        {
            if (controller == null || controller.State == null)
            {
                PushLog("프리셋 실패: 컨트롤러/상태 없음");
                return;
            }

            controller.InitializeIntegration();
            controller.EnableActionCameraFocus = true;
            controller.DebugResetActionFocusDwellCounters();

            var monsterId = monsterIds.Length > 0 ? monsterIds[Mathf.Clamp(monsterIndex, 0, monsterIds.Length - 1)] : null;
            if (string.IsNullOrEmpty(monsterId))
            {
                PushLog("프리셋 실패: 몬스터 카탈로그 비어 있음");
                return;
            }

            int[] bearings;
            int[] distances;
            int radius, damage, hp, perField;
            switch (preset)
            {
                case "A":
                    // 4 bearings so the budget (4/phase) and the coalescing radius both come into play.
                    bearings = new[] { 0, 1, 3, 4 };
                    distances = new[] { 8, 8, 8, 8 };
                    radius = 2; damage = 2; hp = 30; perField = 2;
                    break;
                case "B":
                    // Fat, low-damage patches at three ranges: the monsters survive and keep moving/attacking,
                    // which is what the movement-phase framing needs to be visible at all.
                    bearings = new[] { 0, 2, 4 };
                    distances = new[] { 7, 9, 11 };
                    radius = 3; damage = 1; hp = 200; perField = 2;
                    break;
                case "C":
                    // ⚠ HP is sized to SURVIVE the warm-up tick and die on the next one (12 - 9 = 3, then dead).
                    // Killing them on the first tick puts the death inside the un-watched warm-up turn and
                    // leaves nothing to see — that mistake cost a take (plan §10.4).
                    bearings = new[] { 0, 3 };
                    distances = new[] { 9, 9 };
                    radius = 2; damage = 9; hp = 12; perField = 3;
                    break;
                default:
                    // One distant cluster: the cleanest read on the turn-start return, because there is exactly
                    // one framing to come home from and the trip is the longest of the four.
                    bearings = new[] { 0 };
                    distances = new[] { 10 };
                    radius = 2; damage = 2; hp = 60; perField = 2;
                    break;
            }

            var origin = controller.State.PlayerCoord;
            var offsets = new[] { new HexCoord(0, 0), HexCoord.Directions[2], HexCoord.Directions[4] };
            var fields = 0;
            var mobs = 0;
            for (var b = 0; b < bearings.Length; b++)
            {
                var d = HexCoord.Directions[bearings[b]];
                var centre = new HexCoord(origin.Q + d.Q * distances[b], origin.R + d.R * distances[b]);
                if (controller.DebugSandboxInstallFieldObject(centre, radius, 8, FieldObjectKind.FieldDamage, damage))
                {
                    fields++;
                }

                for (var k = 0; k < perField && k < offsets.Length; k++)
                {
                    var coord = new HexCoord(centre.Q + offsets[k].Q, centre.R + offsets[k].R);
                    if (controller.DebugSandboxSpawnMonsterAtCoord(monsterId, coord, hp, out _))
                    {
                        mobs++;
                    }
                }
            }

            PushLog($"프리셋 {preset}: 필드 {fields}개(피해 {damage}) · 몬스터 {mobs}마리(HP {hp}) — 한 턴 무녹화 진행 후 관찰");
        }

        private void InstallDistantFieldScenario()
        {
            var origin = controller.State.PlayerCoord;
            var direction = HexCoord.Directions[0];
            var centre = new HexCoord(
                origin.Q + direction.Q * scenarioFieldDistance,
                origin.R + direction.R * scenarioFieldDistance);

            var installed = controller.DebugSandboxInstallFieldObject(
                centre, scenarioFieldRadius, scenarioFieldTurns, FieldObjectKind.FieldDamage, value: 2);

            var spawned = 0;
            if (monsterIds.Length > 0)
            {
                // Inside the field footprint, so the tick actually has targets and the field's reveal covers them.
                var offsets = new[] { new HexCoord(0, 0), HexCoord.Directions[2], HexCoord.Directions[4] };
                for (var i = 0; i < offsets.Length; i++)
                {
                    var coord = new HexCoord(centre.Q + offsets[i].Q, centre.R + offsets[i].R);
                    if (controller.DebugSandboxSpawnMonsterAtCoord(monsterIds[monsterIndex], coord, rigHp, out _))
                    {
                        spawned++;
                    }
                }
            }

            PushLog($"필드 {centre} 설치={installed}, 몬스터 {spawned}마리 배치");
        }

        private void SpawnRing()
        {
            var origin = controller.State.PlayerCoord;
            var spawned = 0;
            for (var i = 0; i < RigRings.Length; i++)
            {
                // One direction per ring so the actors land on different bearings rather than in a line —
                // a single bearing would make every coalescing question trivially "yes".
                var direction = HexCoord.Directions[i % HexCoord.Directions.Count];
                var coord = new HexCoord(
                    origin.Q + direction.Q * RigRings[i],
                    origin.R + direction.R * RigRings[i]);

                if (controller.DebugSandboxSpawnMonsterAtCoord(monsterIds[monsterIndex], coord, rigHp, out var id))
                {
                    rigSpawnedIds.Add(id);
                    spawned++;
                }
                else
                {
                    PushLog($"r{RigRings[i]} {coord} 배치 실패: {controller.LastInputMessage}");
                }
            }

            PushLog($"링 배치: {spawned}/{RigRings.Length}마리");
        }

        /// <summary>
        /// The feature's master switch, live. Exposed here because the whole point of P4 is a human deciding
        /// whether the camera work feels right, and that judgement needs A/B on the same board without
        /// leaving play mode. Ships OFF; flipping it here does not touch the shipping scenes.
        /// </summary>
        private void DrawFocusToggleSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("■ 액션 카메라 포커싱 (P4 체감 확인)");

            var enabled = controller.EnableActionCameraFocus;
            var next = GUILayout.Toggle(enabled, enabled ? "● 포커싱 ON" : "포커싱 OFF (현행 동작)");
            if (next != enabled)
            {
                controller.EnableActionCameraFocus = next;
                PushLog(next ? "포커싱 ON — 다음 몬스터 페이즈부터 적용" : "포커싱 OFF — 현행 동작");
            }

            var timing = controller.TimingProfile;
            if (timing != null)
            {
                GUILayout.Label($"패닝(도착 리드) {timing.TunableFocusPanSeconds:0.00}s · 병합 r{timing.TunableCoalesceRadiusHexes} · 페이즈당 최대 {timing.TunableMaxFocusPerPhase}회");
                timing.TunableFocusPanSeconds = DrawFloatSlider("패닝 시간", timing.TunableFocusPanSeconds, 0.1f, 1.5f);
                timing.TunableMaxFocusPerPhase = Mathf.RoundToInt(
                    DrawFloatSlider("페이즈당 최대 컷", timing.TunableMaxFocusPerPhase, 0f, 6f));
                timing.TunableCoalesceRadiusHexes = Mathf.RoundToInt(
                    DrawFloatSlider("병합 반경", timing.TunableCoalesceRadiusHexes, 0f, 10f));

                // The dwell, not the pan, is what decides whether an event is still on screen when the camera
                // leaves — the pan happens before the event's beats play (plan §10.6). These two are the knobs
                // that answer "폭발을 다 보기 전에 넘어간다".
                GUILayout.Label($"체류: 마지막 텍스트 후 {timing.TunableFocusDwellTextReadSeconds:0.00}s · 상한 {timing.TunableFocusDwellMaxSeconds:0.00}s");
                timing.TunableFocusDwellTextReadSeconds =
                    DrawFloatSlider("텍스트 후 체류", timing.TunableFocusDwellTextReadSeconds, 0f, 1.5f);
                timing.TunableFocusDwellMaxSeconds =
                    DrawFloatSlider("체류 상한", timing.TunableFocusDwellMaxSeconds, 0.5f, 6f);

                // 복귀 대기 상한(plan §10.13): 페이즈가 끝나고 카메라가 플레이어에 안착할 때까지 턴 시작을
                // 미루는 시간의 천장. 실제 대기는 안착하는 즉시 끝나므로 이 값은 길이가 아니라 타임아웃이다.
                timing.TunableFocusReturnMaxSeconds =
                    DrawFloatSlider("복귀 대기 상한", timing.TunableFocusReturnMaxSeconds, 0f, 3f);
            }

            var cameraProfile = controller.DebugCameraProfile;
            if (cameraProfile != null)
            {
                // The measurement singled this out as the highest-leverage knob: 0.1 reclassifies ~17% of all
                // events from framed to unframed (plan §6.1), so it sets how often the camera moves at all.
                GUILayout.Label($"프러스텀 마진 {cameraProfile.FrustumMarginFraction:0.00} — 실측상 이 값이 작업량을 가장 크게 좌우");
            }

            GUILayout.Space(4f);
            var emphasis = controller.EnableMeleeAttackEmphasis;
            var nextEmphasis = GUILayout.Toggle(
                emphasis, emphasis ? "● 근접 공격 강조 ON" : "근접 공격 강조 OFF");
            if (nextEmphasis != emphasis)
            {
                controller.EnableMeleeAttackEmphasis = nextEmphasis;
                PushLog(nextEmphasis ? "근접 공격 강조 ON" : "근접 공격 강조 OFF");
            }

            if (timing != null)
            {
                GUILayout.Label($"강조 유지 {timing.TunableAttackEmphasisSeconds:0.00}s");
                timing.TunableAttackEmphasisSeconds = DrawFloatSlider(
                    "강조 유지 시간", timing.TunableAttackEmphasisSeconds, 0f, 1.5f);
            }

            if (cameraProfile != null)
            {
                // Bias, not relocation: 0 keeps the player centred, 1 puts the attacker centred.
                GUILayout.Label($"강조 치우침 {cameraProfile.AttackEmphasisBias:0.00} (0=플레이어 중앙, 1=공격자 중앙)");
            }

            GUILayout.Label("끈 상태가 현행 출하 동작입니다. 켜고 A/B로 비교하세요.");

            GUILayout.Space(4f);
            var recorder = ResolveRecorder();
            if (recorder != null && recorder.IsRecording)
            {
                GUILayout.Label($"● 녹화 중 — {recorder.FrameIndex} 프레임");
            }
            else if (GUILayout.Button("영상 녹화 (다음 연출 1회)", GUILayout.Height(ButtonHeight)))
            {
                StartTakeRecording();
            }

            GUILayout.Label($"PNG 출력: {RecordingFolder}  → ffmpeg로 mp4 변환");
        }

        private const string RecordingFolder = "output/camera-lab-takes";

        /// <summary>
        /// The recorder is created on demand rather than authored into the scene: it is a capture tool, and a
        /// component sitting in the scene would be one more thing to keep wired through scene rebuilds.
        /// </summary>
        private TrailerFrameRecorder ResolveRecorder()
        {
            var existing = FindFirstObjectByType<TrailerFrameRecorder>();
            if (existing != null)
            {
                return existing;
            }

            return new GameObject("Camera Lab Recorder").AddComponent<TrailerFrameRecorder>();
        }

        private void StartTakeRecording()
        {
            var recorder = ResolveRecorder();
            if (recorder == null)
            {
                PushLog("레코더 생성 실패");
                return;
            }

            // Stops on the frame the sequence ends (probes IsSequencePlaying), so the clip has no frozen tail.
            recorder.StartRecordingUntilPresentationEnds(RecordingFolder, 60, 1, controller, frameLimit: 3600);
            PushLog("녹화 시작 — 다음 연출(턴 종료 등)을 진행하세요");
        }

        private static float DrawFloatSlider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(110f));
            var next = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return next;
        }

        private void DrawMetricsSection()
        {
            GUILayout.Space(6f);
            GUILayout.Label("■ P0 계측 — 화면 밖 이벤트 누적");

            var measuring = controller.MeasureActionFocus;
            var next = GUILayout.Toggle(measuring, "계측 ON (연출 타이밍 영향 없음)");
            if (next != measuring)
            {
                controller.MeasureActionFocus = next;
            }

            GUILayout.Label($"누적 표본: {CombatActionFocusMetrics.SampleCount}건"
                + (CombatActionFocusMetrics.Truncated ? " (상한 초과)" : string.Empty));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("요약 출력", GUILayout.Height(ButtonHeight)))
            {
                Debug.Log(CombatActionFocusMetrics.FormatSummary());
            }
            if (GUILayout.Button("표본 초기화", GUILayout.Height(ButtonHeight)))
            {
                CombatActionFocusMetrics.Clear();
                PushLog("표본 초기화");
            }
            GUILayout.EndHorizontal();
        }

        private void DrawLogSection()
        {
            if (eventLog.Count == 0)
            {
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label("■ 로그");
            for (var i = eventLog.Count - 1; i >= 0; i--)
            {
                GUILayout.Label("  " + eventLog[i]);
            }
        }

        private void RefreshMonsterIds()
        {
            var catalog = controller.State?.MonsterCatalog;
            var ids = catalog?.Entries
                ?.Where(entry => !string.IsNullOrWhiteSpace(entry.Id))
                .Select(entry => entry.Id)
                .ToArray() ?? System.Array.Empty<string>();
            if (ids.Length != monsterIds.Length)
            {
                monsterIds = ids;
            }
        }
    }
}
