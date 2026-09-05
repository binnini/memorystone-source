using SeoulPlayup.CardCore;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Dedicated tuning lab for combat presentation timing (attack ↔ hit reaction, hit stop, movement).
    /// Unlike <see cref="VfxAnimationLabController"/> (which plays VFX cues directly), this drives the
    /// REAL turn system: it finds the scene's <see cref="MapCombatController"/> and replays attacks through
    /// the same <c>ResolveAttackSequence</c> + effect-buffering path as live combat, so what is tuned here
    /// matches the game exactly.
    ///
    /// Phase 1 scope: real combat foundation + player-attack replay + global timing sliders + slow-motion.
    /// Per-card/pattern timing tables, monster-attack direction, status effects and traps land in later phases.
    ///
    /// Build-gated: the panel only draws in the editor or a development build (see
    /// <see cref="CombatDebugControlPanel.DebugUiAvailable"/>), so release player builds are unaffected.
    /// </summary>
    public sealed class CombatTimingLabController : MonoBehaviour
    {
        [Tooltip("The real combat controller this lab drives. Auto-resolved from the scene when left empty.")]
        [SerializeField] private MapCombatController controller;

        // Movable / resizable IMGUI window (mirrors VfxAnimationLabController's panel ergonomics):
        // drag the title strip to move, drag the W/H sliders or the corner grip to resize.
        private const int PanelWindowId = 0x5FA1B;
        private const float MinPanelWidth = 280f;
        private const float MinPanelHeight = 200f;
        private const float ChromeHeight = 92f;
        private const float ButtonHeight = 26f;

        private Rect panelRect;
        private bool panelRectInitialized;
        private Vector2 scrollPosition;
        private bool resizingPanel;

        // Currently selected per-attack timing id (cardId for player attacks, patternId for monster attacks).
        private string attackEditId = "A01";

        private float MaxPanelWidth => Mathf.Max(MinPanelWidth + 40f, Screen.width - 24f);
        private float MaxPanelHeight => Mathf.Max(MinPanelHeight + 40f, Screen.height - 24f);

        public MapCombatController Controller => controller;

        private void Awake()
        {
            EnsureController();
        }

        private void EnsureController()
        {
            if (controller == null)
            {
                controller = FindObjectOfType<MapCombatController>();
            }
        }

        private void OnGUI()
        {
            if (!CombatDebugControlPanel.DebugUiAvailable)
            {
                return;
            }

            EnsurePanelRect();
            HandleResizeInput();

            var drawn = GUILayout.Window(
                PanelWindowId,
                panelRect,
                DrawPanelWindow,
                "Combat Timing Lab",
                GUILayout.Width(panelRect.width),
                GUILayout.Height(panelRect.height));
            panelRect.x = drawn.x;
            panelRect.y = drawn.y;
        }

        private void EnsurePanelRect()
        {
            if (panelRectInitialized)
            {
                return;
            }

            panelRect = new Rect(12f, 12f, 400f, Mathf.Min(640f, Screen.height - 24f));
            panelRectInitialized = true;
        }

        private void DrawPanelWindow(int windowId)
        {
            DrawChrome();

            var bodyHeight = Mathf.Max(80f, panelRect.height - ChromeHeight);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(bodyHeight));
            DrawBody();
            GUILayout.EndScrollView();

            DrawResizeGrip();

            // Only the title strip drags the window so the sliders/buttons/grip stay clickable.
            GUI.DragWindow(new Rect(0f, 0f, panelRect.width, 20f));
        }

        // Width / height sliders (position is adjusted by dragging the title strip). The returned window rect
        // copies back x/y only, so width/height written here persist across frames.
        private void DrawChrome()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"W {panelRect.width:0}", GUILayout.Width(64f));
            panelRect.width = Mathf.Round(GUILayout.HorizontalSlider(panelRect.width, MinPanelWidth, MaxPanelWidth));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"H {panelRect.height:0}", GUILayout.Width(64f));
            panelRect.height = Mathf.Round(GUILayout.HorizontalSlider(panelRect.height, MinPanelHeight, MaxPanelHeight));
            GUILayout.EndHorizontal();
            if (GUILayout.Button("패널 위치/크기 리셋"))
            {
                panelRect = new Rect(12f, 12f, 400f, Mathf.Min(640f, Screen.height - 24f));
            }
            GUILayout.Label("제목줄 드래그=이동 · W/H 슬라이더 또는 ↘ 모서리=크기");
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

            GUILayout.Label(controller.IsSequencePlaying
                ? $"재생 중: {controller.PresentationPhase}"
                : "대기 중 (idle)");

            // The player-attack replay buttons below resolve per-attack timing under the currently selected id
            // (default "A01" = basic attack). Editing happens in the "공격별 타이밍" section.
            controller.DebugReplayPlayerAttackTimingId = string.IsNullOrWhiteSpace(attackEditId) ? "A01" : attackEditId;

            GUILayout.Space(4f);
            GUILayout.Label($"공격 → 타격 타이밍 (플레이어 → 가장 가까운 몬스터) · 적용 ID: {controller.DebugReplayPlayerAttackTimingId}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("공격 재생", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayPlayerAttack(false);
            }
            if (GUILayout.Button("처치 재생", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayPlayerAttack(true);
            }
            if (GUILayout.Button("전투 리셋", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugRestoreCombatants();
            }
            GUILayout.EndHorizontal();

            DrawPerCardReplayButtons();

            GUILayout.Space(4f);
            GUILayout.Label("공격 → 타격 타이밍 (가장 가까운 몬스터 → 플레이어)");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("몬스터 공격 재생", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayMonsterAttack(false);
            }
            if (GUILayout.Button("몬스터 처치 재생", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayMonsterAttack(true);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("재생은 대상(몬스터/플레이어)을 풀피로 되돌린 뒤 실제 공격 시퀀스를 그대로 재생합니다.");

            DrawNonAttackCardReplayButtons();

            DrawSlowMotion();
            DrawPresentationTrace();
            DrawGlobalTimingSliders();
            DrawPerAttackTimingSection();
        }

        // Buttons that replay each attack card the player actually holds, by its catalog id, so the card's own
        // animation/VFX cue and per-attack timing show. Clicking also selects that id in the editor below.
        // (Presentation replay: damage is synthetic and area/multi-hit/knockback are not reproduced.)
        private void DrawPerCardReplayButtons()
        {
            var choices = controller.GetPlayerAttackCardChoices();
            GUILayout.Space(2f);
            if (choices == null || choices.Count == 0)
            {
                GUILayout.Label("카드별 재생: 플레이어 공격 카드 없음 (덱/핸드에 Attack 카드 필요)");
                return;
            }

            GUILayout.Label("카드별 재생 (실제 카드 id로 공격 — VFX/타이밍이 그 카드 기준)");
            const int perRow = 2;
            for (var i = 0; i < choices.Count; i++)
            {
                if (i % perRow == 0)
                {
                    GUILayout.BeginHorizontal();
                }

                var choice = choices[i];
                if (GUILayout.Button($"{choice.Value} ({choice.Key})", GUILayout.Height(ButtonHeight)))
                {
                    attackEditId = choice.Key;
                    controller.DebugReplayPlayerAttack(choice.Key, false);
                }

                if (i % perRow == perRow - 1 || i == choices.Count - 1)
                {
                    GUILayout.EndHorizontal();
                }
            }
        }

        // Non-attack cards, replayed through their own real TryPlayer* path (not a synthetic stand-in), so the
        // effect that actually resolves — area, ticks, status application, floating text — is the shipped one.
        // Listed from the catalog rather than the deck so a newly authored card can be auditioned before it is
        // ever drafted. Targets are picked by the card's own validator; a card with no valid target says so.
        private static readonly (CardEffectType Type, string Label)[] ReplayCardSections =
        {
            (CardEffectType.Scout, "정찰"),
            (CardEffectType.FieldObject, "필드(설치)"),
            (CardEffectType.Defend, "방어"),
            (CardEffectType.Move, "이동"),
            (CardEffectType.Utility, "유틸리티"),
        };

        private int replayCardSectionIndex;

        private void DrawNonAttackCardReplayButtons()
        {
            GUILayout.Space(6f);
            GUILayout.Label("── 카드 종류별 재생 (실제 카드 경로로 효과까지 해결) ──");

            GUILayout.BeginHorizontal();
            for (var i = 0; i < ReplayCardSections.Length; i++)
            {
                var isActive = i == replayCardSectionIndex;
                if (GUILayout.Toggle(isActive, ReplayCardSections[i].Label, GUI.skin.button) && !isActive)
                {
                    replayCardSectionIndex = i;
                }
            }

            GUILayout.EndHorizontal();

            var section = ReplayCardSections[replayCardSectionIndex];
            var choices = controller.GetCatalogCardChoices(section.Type);
            if (choices == null || choices.Count == 0)
            {
                GUILayout.Label($"{section.Label}: 카탈로그에 카드 없음");
                return;
            }

            const int perRow = 2;
            for (var i = 0; i < choices.Count; i++)
            {
                if (i % perRow == 0)
                {
                    GUILayout.BeginHorizontal();
                }

                var choice = choices[i];
                if (GUILayout.Button($"{choice.Value} ({choice.Key})", GUILayout.Height(ButtonHeight)))
                {
                    controller.DebugReplayCard(choice.Key);
                }

                if (i % perRow == perRow - 1 || i == choices.Count - 1)
                {
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Label("재생은 카드를 손에 넣고 기(Ki)/페이즈를 초기화한 뒤 실제로 사용합니다(덱은 그대로).");
        }

        // Per-attack timing editor: pick a cardId/patternId, override individual beats/offsets (unset = inherit
        // the global profile), then save back to the asset and/or CSV. Only the authored fields differ from the
        // global feel, so an all-inherit row changes nothing.
        private void DrawPerAttackTimingSection()
        {
            GUILayout.Space(8f);
            GUILayout.Label("── 공격별 타이밍 (CSV 테이블, 미설정=전역 상속) ──");

            var table = controller.AttackTimingTable;
            if (table == null)
            {
                GUILayout.Label("CombatAttackTimingTable 미배선 — 컨트롤러 attackTimingTable에 에셋을 할당하세요.");
                return;
            }

            var profile = controller.TimingProfile;

            GUILayout.BeginHorizontal();
            GUILayout.Label("ID", GUILayout.Width(28f));
            attackEditId = GUILayout.TextField(attackEditId ?? string.Empty);
            if (GUILayout.Button("◀", GUILayout.Width(28f)))
            {
                attackEditId = CycleEntryId(table, attackEditId, -1);
            }
            if (GUILayout.Button("▶", GUILayout.Width(28f)))
            {
                attackEditId = CycleEntryId(table, attackEditId, +1);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("플레이어=cardId(예: A01) · 몬스터=patternId · 행이 없으면 편집 시 자동 생성");

            var id = attackEditId;
            var hasRow = table.TryGet(id, out var entry);
            GUILayout.Label(hasRow
                ? (entry.HasAnyOverride ? "이 ID: override 있음" : "이 ID: 행은 있으나 전부 상속")
                : "이 ID: 행 없음 (전부 전역 상속)");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("이 ID로 공격 재생", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayPlayerAttackTimingId = string.IsNullOrWhiteSpace(id) ? "A01" : id;
                controller.DebugReplayPlayerAttack(false);
            }
            if (GUILayout.Button("이 ID로 처치 재생", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugReplayPlayerAttackTimingId = string.IsNullOrWhiteSpace(id) ? "A01" : id;
                controller.DebugReplayPlayerAttack(true);
            }
            GUILayout.EndHorizontal();

            DrawOptionalField(table, id, "Windup 선딜", profile != null ? profile.AttackWindupDelay : 0f, clampNonNeg: true, OptionalField.Windup);
            DrawOptionalField(table, id, "Impact 타격까지", profile != null ? profile.AttackImpactDelay : 0f, clampNonNeg: true, OptionalField.Impact);
            DrawOptionalField(table, id, "Death 사망 후", profile != null ? profile.DeathDelay : 0f, clampNonNeg: true, OptionalField.Death);
            DrawOptionalField(table, id, "HitStop 플레이어", profile != null ? profile.PlayerAttackHitStopSeconds : 0f, clampNonNeg: true, OptionalField.PlayerHitStop);
            DrawOptionalField(table, id, "HitStop 몬스터", profile != null ? profile.MonsterAttackHitStopSeconds : 0f, clampNonNeg: true, OptionalField.MonsterHitStop);
            DrawOptionalField(table, id, "HitStop 치명타", profile != null ? profile.LethalHitStopSeconds : 0f, clampNonNeg: true, OptionalField.LethalHitStop);
            DrawOptionalField(table, id, "오프셋 VFX+숫자", profile != null ? profile.VisualImpactOffset : 0f, clampNonNeg: false, OptionalField.VisualOffset);
            DrawOptionalField(table, id, "오프셋 셰이크", profile != null ? profile.ShakeImpactOffset : 0f, clampNonNeg: false, OptionalField.ShakeOffset);
            DrawOptionalField(table, id, "오프셋 히트스탑", profile != null ? profile.HitStopImpactOffset : 0f, clampNonNeg: false, OptionalField.HitStopOffset);

#if UNITY_EDITOR
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("에셋에 저장", GUILayout.Height(ButtonHeight)))
            {
                UnityEditor.EditorUtility.SetDirty(table);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            if (GUILayout.Button("CSV로 내보내기", GUILayout.Height(ButtonHeight)))
            {
                UnityEditor.EditorUtility.SetDirty(table);
                UnityEditor.AssetDatabase.SaveAssets();
                SeoulPlayup.Combat.Unity.Dev.Editor.CombatAttackTimingTableCsvImporter.WriteCsv(table);
            }
            GUILayout.EndHorizontal();
            GUILayout.Label("스테퍼는 즉시 반영(다음 재생부터). 만족스러우면 에셋/CSV로 영구 기록하세요.");
#endif
        }

        private enum OptionalField
        {
            Windup, Impact, Death, PlayerHitStop, MonsterHitStop, LethalHitStop,
            VisualOffset, ShakeOffset, HitStopOffset
        }

        // One editable row for an optional per-attack value. Shows the inherited global value when unset; the
        // −/+ buttons author an override (seeded from the displayed value), "상속" clears it back to inherit.
        private void DrawOptionalField(CombatAttackTimingTable table, string id, string label, float globalValue, bool clampNonNeg, OptionalField field)
        {
            var hasRow = table.TryGet(id, out var entry);
            var optional = hasRow ? Get(entry, field) : CombatAttackTimingTable.OptionalFloat.Unset;
            var has = optional.HasValue;
            var displayValue = has ? optional.Value : globalValue;

            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} {(has ? displayValue.ToString("0.00") + "s" : $"(상속 {globalValue:0.00})")}", GUILayout.Width(200f));

            var stepped = false;
            var next = displayValue;
            if (GUILayout.Button("−0.05", GUILayout.Width(50f)))
            {
                next = Mathf.Round((displayValue - TimingStep) / TimingStep) * TimingStep;
                stepped = true;
            }
            if (GUILayout.Button("+0.05", GUILayout.Width(50f)))
            {
                next = Mathf.Round((displayValue + TimingStep) / TimingStep) * TimingStep;
                stepped = true;
            }
            if (GUILayout.Button("상속", GUILayout.Width(44f)))
            {
                if (hasRow)
                {
                    var e = table.GetOrCreate(id);
                    Clear(e, field);
                    MarkTableDirty(table);
                }
            }
            GUILayout.EndHorizontal();

            if (stepped && !string.IsNullOrWhiteSpace(id))
            {
                if (clampNonNeg)
                {
                    next = Mathf.Max(0f, next);
                }

                var e = table.GetOrCreate(id);
                Set(e, field, next);
                MarkTableDirty(table);
            }
        }

        private static CombatAttackTimingTable.OptionalFloat Get(CombatAttackTimingTable.Entry e, OptionalField field)
        {
            switch (field)
            {
                case OptionalField.Windup: return e.windupDelay;
                case OptionalField.Impact: return e.impactDelay;
                case OptionalField.Death: return e.deathDelay;
                case OptionalField.PlayerHitStop: return e.playerHitStopSeconds;
                case OptionalField.MonsterHitStop: return e.monsterHitStopSeconds;
                case OptionalField.LethalHitStop: return e.lethalHitStopSeconds;
                case OptionalField.VisualOffset: return e.visualImpactOffset;
                case OptionalField.ShakeOffset: return e.shakeImpactOffset;
                default: return e.hitStopImpactOffset;
            }
        }

        private static void Set(CombatAttackTimingTable.Entry e, OptionalField field, float value)
        {
            switch (field)
            {
                case OptionalField.Windup: e.windupDelay.Set(value); break;
                case OptionalField.Impact: e.impactDelay.Set(value); break;
                case OptionalField.Death: e.deathDelay.Set(value); break;
                case OptionalField.PlayerHitStop: e.playerHitStopSeconds.Set(value); break;
                case OptionalField.MonsterHitStop: e.monsterHitStopSeconds.Set(value); break;
                case OptionalField.LethalHitStop: e.lethalHitStopSeconds.Set(value); break;
                case OptionalField.VisualOffset: e.visualImpactOffset.Set(value); break;
                case OptionalField.ShakeOffset: e.shakeImpactOffset.Set(value); break;
                default: e.hitStopImpactOffset.Set(value); break;
            }
        }

        private static void Clear(CombatAttackTimingTable.Entry e, OptionalField field)
        {
            switch (field)
            {
                case OptionalField.Windup: e.windupDelay.Clear(); break;
                case OptionalField.Impact: e.impactDelay.Clear(); break;
                case OptionalField.Death: e.deathDelay.Clear(); break;
                case OptionalField.PlayerHitStop: e.playerHitStopSeconds.Clear(); break;
                case OptionalField.MonsterHitStop: e.monsterHitStopSeconds.Clear(); break;
                case OptionalField.LethalHitStop: e.lethalHitStopSeconds.Clear(); break;
                case OptionalField.VisualOffset: e.visualImpactOffset.Clear(); break;
                case OptionalField.ShakeOffset: e.shakeImpactOffset.Clear(); break;
                default: e.hitStopImpactOffset.Clear(); break;
            }
        }

        private static string CycleEntryId(CombatAttackTimingTable table, string current, int direction)
        {
            var entries = table.Entries;
            if (entries == null || entries.Count == 0)
            {
                return current;
            }

            var index = -1;
            for (var i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && string.Equals(entries[i].id, current, System.StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            var next = index < 0 ? 0 : (index + direction + entries.Count) % entries.Count;
            return entries[next]?.id ?? current;
        }

        private static void MarkTableDirty(CombatAttackTimingTable table)
        {
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(table);
#endif
        }

        private void DrawSlowMotion()
        {
            GUILayout.Space(6f);
            var timeScale = controller.DebugPlaybackTimeScale;
            GUILayout.Label($"재생 속도(슬로모션): {timeScale:0.00}x");
            var nextTimeScale = GUILayout.HorizontalSlider(timeScale, 0.05f, 1f);
            if (!Mathf.Approximately(nextTimeScale, timeScale))
            {
                controller.DebugSetPlaybackTimeScale(nextTimeScale);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("0.1x")) controller.DebugSetPlaybackTimeScale(0.1f);
            if (GUILayout.Button("0.25x")) controller.DebugSetPlaybackTimeScale(0.25f);
            if (GUILayout.Button("0.5x")) controller.DebugSetPlaybackTimeScale(0.5f);
            if (GUILayout.Button("1x")) controller.DebugSetPlaybackTimeScale(1f);
            GUILayout.EndHorizontal();
        }

        // The slow-motion slider above makes channel drift *visible*; this makes it *measurable*. Tuning the
        // per-channel offsets below by eye alone means guessing at how far apart two channels landed.
        private void DrawPresentationTrace()
        {
            GUILayout.Space(6f);
            controller.TracePresentationTimeline = GUILayout.Toggle(
                controller.TracePresentationTimeline,
                "타임라인 기록 (채널별 착지 시각을 콘솔에 표)");

            var log = controller.LastPresentationTrace;
            if (log == null)
            {
                GUILayout.Label("아직 기록 없음 — 켠 뒤 공격/처치를 재생하세요.");
                return;
            }

            GUILayout.Label($"최근 기록: {log.Label} — {log.DurationSeconds:0.000}s / {log.Entries.Count}항목");
            if (GUILayout.Button("콘솔에 다시 출력", GUILayout.Height(ButtonHeight)))
            {
                Debug.Log(log.Format());
            }
        }

        private void DrawGlobalTimingSliders()
        {
            var profile = controller.TimingProfile;
            if (profile == null)
            {
                GUILayout.Space(6f);
                GUILayout.Label("CombatTimingProfile 미배선 — 슬라이더 비활성. 컨트롤러 timingProfile에 에셋을 할당하세요.");
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label("전역 타이밍 (CombatTimingProfile)");
            profile.TunableGlobalSpeedMultiplier = DrawTimingStepper("전역 연출 속도(배율)", profile.TunableGlobalSpeedMultiplier);
            profile.TunableEffectStaggerSeconds = DrawTimingStepper("효과 스태거(타격음/VFX 간격)", profile.TunableEffectStaggerSeconds);
            profile.TunableAoeTargetIntervalSeconds = DrawTimingStepper("광역 대상 간격(대상 사이)", profile.TunableAoeTargetIntervalSeconds);
            profile.TunableAlignImpactToAnimation = GUILayout.Toggle(
                profile.TunableAlignImpactToAnimation,
                "임팩트 동기화 (데미지/VFX/셰이크를 타격 프레임에 정렬)");
            profile.TunableAttackWindupDelay = DrawTimingStepper("Windup 선딜", profile.TunableAttackWindupDelay);
            profile.TunableAttackImpactDelay = DrawTimingStepper("Impact 타격까지", profile.TunableAttackImpactDelay);
            profile.TunableDeathDelay = DrawTimingStepper("Death 사망 후", profile.TunableDeathDelay);

            GUILayout.Space(6f);
            profile.TunableEnableHitStop = GUILayout.Toggle(profile.TunableEnableHitStop, "히트스탑 사용");
            profile.TunablePlayerAttackHitStopSeconds = DrawTimingStepper("HitStop 플레이어공격", profile.TunablePlayerAttackHitStopSeconds);
            profile.TunableMonsterAttackHitStopSeconds = DrawTimingStepper("HitStop 몬스터공격", profile.TunableMonsterAttackHitStopSeconds);
            profile.TunableLethalHitStopSeconds = DrawTimingStepper("HitStop 치명타", profile.TunableLethalHitStopSeconds);
            profile.TunableHitStopTimeScale = DrawTimingStepper("HitStop timeScale", profile.TunableHitStopTimeScale);
            profile.TunableHitStopAnimationSpeed = DrawTimingStepper("HitStop 애니속도", profile.TunableHitStopAnimationSpeed);

            GUILayout.Space(6f);
            GUILayout.Label("임팩트 채널 오프셋 (임팩트 기준 −선행 / +지연, 초)");
            GUILayout.Label("0이면 전부 임팩트에 동시 발화(기존 동작). 가장 빠른 채널이 임팩트 순간을 정의.");
            profile.TunableVisualImpactOffset = DrawTimingStepper("VFX+데미지숫자", profile.TunableVisualImpactOffset);
            profile.TunableShakeImpactOffset = DrawTimingStepper("카메라 셰이크", profile.TunableShakeImpactOffset);
            profile.TunableHitStopImpactOffset = DrawTimingStepper("히트스탑", profile.TunableHitStopImpactOffset);

            GUILayout.Space(6f);
            profile.TunablePlayerMoveSeconds = DrawTimingStepper("이동 플레이어", profile.TunablePlayerMoveSeconds);
            profile.TunableEnemyMoveSeconds = DrawTimingStepper("이동 몬스터", profile.TunableEnemyMoveSeconds);
            profile.TunableEnemyMoveStartDelay = DrawTimingStepper("몬스터 이동 시작딜레이", profile.TunableEnemyMoveStartDelay);

#if UNITY_EDITOR
            GUILayout.Space(4f);
            if (GUILayout.Button("프로파일 에셋에 영구 저장", GUILayout.Height(ButtonHeight)))
            {
                UnityEditor.EditorUtility.SetDirty(profile);
                UnityEditor.AssetDatabase.SaveAssets();
            }
            GUILayout.Label("버튼은 즉시 반영. 만족스러우면 위 버튼으로 에셋에 영구 기록하세요.");
#endif
        }

        // −/+ buttons stepping by 0.05s. No min/max clamp here (signed offsets and unbounded delays are allowed);
        // value sanity (e.g. >=0 for durations) is enforced by the profile's Tunable setters. Rounded to the step
        // grid so repeated float adds stay clean (0.05, 0.10, 0.15, ...).
        private const float TimingStep = 0.05f;

        private static float DrawTimingStepper(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label} {value:0.00}s", GUILayout.Width(180f));
            if (GUILayout.Button("−0.05", GUILayout.Width(54f)))
            {
                value = Mathf.Round((value - TimingStep) / TimingStep) * TimingStep;
            }

            if (GUILayout.Button("+0.05", GUILayout.Width(54f)))
            {
                value = Mathf.Round((value + TimingStep) / TimingStep) * TimingStep;
            }

            if (GUILayout.Button("0", GUILayout.Width(30f)))
            {
                value = 0f;
            }

            GUILayout.EndHorizontal();
            return value;
        }

        private void DrawResizeGrip()
        {
            var grip = new Rect(panelRect.width - 22f, panelRect.height - 22f, 18f, 18f);
            GUI.Box(grip, "↘");

            var e = Event.current;
            if (e.type == EventType.MouseDown && grip.Contains(e.mousePosition))
            {
                resizingPanel = true;
                e.Use();
            }
        }

        private void HandleResizeInput()
        {
            if (!resizingPanel)
            {
                return;
            }

            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDrag:
                    panelRect.width = Mathf.Clamp(panelRect.width + e.delta.x, MinPanelWidth, MaxPanelWidth);
                    panelRect.height = Mathf.Clamp(panelRect.height + e.delta.y, MinPanelHeight, MaxPanelHeight);
                    e.Use();
                    break;
                case EventType.MouseUp:
                    resizingPanel = false;
                    e.Use();
                    break;
            }
        }
    }
}
