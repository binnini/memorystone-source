using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// 맵 오브젝트 확인 랩. 카탈로그(<see cref="MapObjectCatalogSet"/>)의 오브젝트를 버튼으로
    /// 소환해 생김새를 보고, 실제 이벤트(뽑기·상점·기억결)를 발생시켜 본다.
    ///
    /// <see cref="CombatTimingLabController"/>와 같은 구조: 씬의 진짜
    /// <see cref="MapCombatController"/>를 몰고, 트리거는 프로덕션 이동 확정 후와 같은 폴백
    /// 체인을 태운다(랩 전용 지름길 로직 없음 — 랩에서 되면 실게임에서도 된다).
    /// 팔레트는 카탈로그에서 자동 생성되므로 신규 오브젝트 타입을 추가하면 여기 자동으로 나타난다.
    ///
    /// Build-gated: 에디터/개발 빌드에서만 그려진다(<see cref="CombatDebugControlPanel.DebugUiAvailable"/>).
    /// </summary>
    public sealed class ObjectLabController : MonoBehaviour
    {
        [Tooltip("The real combat controller this lab drives. Auto-resolved from the scene when left empty.")]
        [SerializeField] private MapCombatController controller;

        // 소환 위치: 플레이어(0,0) 바로 옆. 어떤 이동 카드로도 한 번에 밟을 수 있는 거리다.
        private static readonly HexCoord SpawnCoord = new HexCoord(1, 0);

        private const int PanelWindowId = 0x0B1EC7;
        private const float MinPanelWidth = 300f;
        private const float MinPanelHeight = 220f;
        private const float ChromeHeight = 92f;
        private const float ButtonHeight = 24f;
        private const int MaxLogLines = 14;

        private Rect panelRect;
        private bool panelRectInitialized;
        private Vector2 scrollPosition;

        private MapObjectCatalogSet catalogSet;
        private readonly List<string> eventLog = new List<string>();
        private string lastSeenMessage;

        private RuntimeMapObjectPrefabCatalog.Entry spawnedEntry;
        private bool hasSpawned;
        private int rollModeIndex;

        // 함정 탭. 함정은 objectRefs가 아니라 trapRefs라는 별도 저작 축이라 오브젝트 팔레트에 나타나지
        // 않는다(MapObjectCatalogSet에 Trap 카탈로그가 없다). 그래서 팔레트를 따로 둔다.
        private int modeIndex;
        private static readonly string[] ModeLabels = { "오브젝트", "함정" };
        private TrapPresetCatalog trapCatalog;
        private TrapPresetCatalog.Entry spawnedTrapPreset;
        private bool hasSpawnedTrap;

        private static readonly string[] RollModeLabels = { "실제 랜덤", "카드팩 고정", "돈 고정", "유물 고정" };
        // 뽑기 누적 구간 CardPack [0,50) / Money [50,90) / Relic [90,100)에 맞춘 고정값.
        private static readonly int[] RollModeValues = { -1, 0, 55, 95 };

        /// <summary>모든 Next 호출에 같은 값을 돌려주는 고정 난수(테스트 FixedRewardRandom과 동일 계약).</summary>
        private sealed class PinnedRewardRandom : IRewardRandom
        {
            private readonly int value;

            public PinnedRewardRandom(int value) => this.value = value;

            public int Next(int maxExclusive) => value % Mathf.Max(1, maxExclusive);
        }

        private float MaxPanelWidth => Mathf.Max(MinPanelWidth + 40f, Screen.width - 24f);
        private float MaxPanelHeight => Mathf.Max(MinPanelHeight + 40f, Screen.height - 24f);

        private void Awake()
        {
            EnsureController();
            catalogSet = MapObjectCatalogSet.LoadDefault();
        }

        private void Update()
        {
            // 컨트롤러의 상태 메시지가 곧 이벤트 로그다 — 뽑기 결과·구매·거부 사유 전부 이 줄로 나온다.
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
                "Object Lab",
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
                    controller.ConfigureDebugRevealAllMapCellsForTests(true);
                }

                return;
            }

            DrawModeTabs();
            if (modeIndex == 1)
            {
                DrawTrapPalette();
                DrawTrapTriggerSection();
                DrawStateSection();
                DrawLogSection();
                return;
            }

            DrawRollPin();
            DrawPalette();
            DrawTriggerSection();
            DrawStateSection();
            DrawLogSection();
        }

        private void DrawRollPin()
        {
            GUILayout.Label("뽑기 롤 고정 (상자 결과가 무작위라 갈래별 확인용)");
            var next = GUILayout.Toolbar(rollModeIndex, RollModeLabels, GUILayout.Height(ButtonHeight));
            if (next != rollModeIndex)
            {
                rollModeIndex = next;
                var value = RollModeValues[rollModeIndex];
                controller.ConfigureRewardRandomForTests(value < 0 ? null : new PinnedRewardRandom(value));
                PushLog($"뽑기 롤: {RollModeLabels[rollModeIndex]}");
            }

            GUILayout.Space(6f);
        }

        private void DrawPalette()
        {
            if (catalogSet == null)
            {
                catalogSet = MapObjectCatalogSet.LoadDefault();
                if (catalogSet == null)
                {
                    GUILayout.Label("MapObjectCatalogSet 로드 실패");
                    return;
                }
            }

            GUILayout.Label($"오브젝트 팔레트 — 클릭하면 {SpawnCoord}에 소환하고 보드를 리셋합니다");
            foreach (var catalog in catalogSet.Catalogs)
            {
                if (catalog == null || catalog.Entries.Count == 0)
                {
                    continue;
                }

                GUILayout.Label($"— {catalog.CategoryLabel} —");
                const int perRow = 3;
                for (var i = 0; i < catalog.Entries.Count; i += perRow)
                {
                    GUILayout.BeginHorizontal();
                    for (var j = i; j < Mathf.Min(i + perRow, catalog.Entries.Count); j++)
                    {
                        var entry = catalog.Entries[j];
                        if (GUILayout.Button(entry.ObjectRef, GUILayout.Height(ButtonHeight)))
                        {
                            Spawn(entry);
                        }
                    }

                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.Space(6f);
        }

        /// <summary>
        /// 선택한 오브젝트 하나만 놓인 보드로 재구성한다. 부분 스폰이 아니라 전체 리셋인 이유:
        /// 소환할 때마다 상태(지갑·클레임 대장·덱)가 깨끗해야 이벤트를 반복 확인할 수 있고,
        /// Render()가 ClearAll()부터 하므로 비주얼 중복도 없다.
        /// </summary>
        private void Spawn(RuntimeMapObjectPrefabCatalog.Entry entry)
        {
            if (controller.LoadedMap == null)
            {
                PushLog("소환 실패: LoadedMap이 없다. 전투 초기화를 먼저 할 것.");
                return;
            }

            var cells = controller.LoadedMap.AllCells.ToList();
            var keptRefs = controller.LoadedMap.ObjectRefs
                .Where(objectData => objectData.IsPlayerSpawn)
                .ToList();
            keptRefs.Add(new HexMapObjectData(
                $"objectlab-{entry.ObjectRef}",
                entry.ObjectType.ToString(),
                entry.ObjectRef,
                SpawnCoord,
                role: "objectlab",
                blocksMovement: entry.BlocksMovement,
                blocksVision: entry.BlocksVision,
                interactable: entry.Interactable,
                visualScaleX: entry.VisualScaleMultiplier.x,
                visualScaleY: entry.VisualScaleMultiplier.y,
                visualScaleZ: entry.VisualScaleMultiplier.z));

            controller.ConfigureMapForTests(new HexMapData(cells, objectRefs: keptRefs));
            controller.InitializeIntegration();
            controller.ConfigureDebugRevealAllMapCellsForTests(true);

            spawnedEntry = entry;
            hasSpawned = true;
            lastSeenMessage = controller.LastInputMessage;
            PushLog($"소환: {entry.ObjectRef} ({entry.ObjectType}) @{SpawnCoord} — 보드/상태 리셋됨");
        }

        private void DrawModeTabs()
        {
            var next = GUILayout.Toolbar(modeIndex, ModeLabels, GUILayout.Height(ButtonHeight));
            if (next != modeIndex)
            {
                modeIndex = next;
            }

            GUILayout.Space(6f);
        }

        // --- 함정 탭 -----------------------------------------------------------------------------

        private void DrawTrapPalette()
        {
            if (trapCatalog == null)
            {
                trapCatalog = TrapPresetCatalog.LoadDefault();
                if (trapCatalog == null)
                {
                    GUILayout.Label("TrapPresetCatalog 로드 실패");
                    return;
                }
            }

            GUILayout.Label($"함정 팔레트 — 클릭하면 {SpawnCoord}에 놓고 보드를 리셋합니다");
            // ⚠️전 함정이 같은 마커 프리팹(Resources/Object/trap)과 같은 단색 틴트로 그려진다. 이 랩이
            //   보여 줄 수 있는 것은 생김새가 아니라 **발동 결과**다.
            GUILayout.Label("⚠️ 6종 모두 화면에선 같은 붉은 마커다 — 여기서 볼 것은 '무슨 일이 일어나는가'.");

            foreach (var preset in trapCatalog.Presets.Where(preset => preset != null))
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(preset.DisplayName, GUILayout.Height(ButtonHeight)))
                {
                    SpawnTrap(preset);
                }

                GUILayout.Label(DescribeTrapPreset(preset), GUILayout.Width(150f));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(6f);
        }

        private static string DescribeTrapPreset(TrapPresetCatalog.Entry preset)
        {
            var shape = preset.Radius > 0 ? $"반경 {preset.Radius}" : "단일";
            var timing = preset.PeriodTurns > 0 ? $"{preset.PeriodTurns}턴 주기(예고형)" : preset.OneShot ? "1회성" : "재사용";
            var payload = string.IsNullOrEmpty(preset.MonsterDefinitionId)
                ? $"{preset.EffectKind} {preset.EffectAmount}"
                : $"{preset.MonsterDefinitionId} ×{preset.EffectAmount}";
            return $"{payload} · {shape} · {timing}";
        }

        /// <summary>
        /// 선택한 함정 하나만 놓인 보드로 재구성한다. 오브젝트 <see cref="Spawn"/>과 같은 전체 리셋 규약
        /// (반복 확인을 위해 소진 기록·상태가 매번 깨끗해야 한다). 프리셋 → 런타임 변환은 맵 저작이 타는
        /// <see cref="HexTrapRef.ToRuntimeTrapData"/> 경로를 그대로 쓴다 — 랩 전용 변환을 만들면 저작과
        /// 랩이 갈라진다.
        /// </summary>
        private void SpawnTrap(TrapPresetCatalog.Entry preset)
        {
            if (controller.LoadedMap == null)
            {
                PushLog("소환 실패: LoadedMap이 없다. 전투 초기화를 먼저 할 것.");
                return;
            }

            var cells = controller.LoadedMap.AllCells.ToList();
            var keptRefs = controller.LoadedMap.ObjectRefs
                .Where(objectData => objectData.IsPlayerSpawn)
                .ToList();

            // presetId만 채운 ref를 프리셋 카탈로그로 굳힌다 = 출하 맵이 하는 것과 같은 해소 경로.
            var trapRef = new HexTrapRef(
                $"objectlab-{preset.PresetId}",
                SpawnCoord.Q,
                SpawnCoord.R,
                preset.Radius,
                effects: null,
                affectsPlayer: true,
                affectsMonsters: false,
                oneShot: preset.OneShot,
                triggerOnEnter: preset.TriggerOnEnter,
                presetId: preset.PresetId,
                periodTurns: preset.PeriodTurns);

            controller.ConfigureMapForTests(new HexMapData(
                cells,
                objectRefs: keptRefs,
                trapRefs: new[] { trapRef.ToRuntimeTrapData(trapCatalog) }));
            controller.InitializeIntegration();
            controller.ConfigureDebugRevealAllMapCellsForTests(true);

            spawnedTrapPreset = preset;
            hasSpawnedTrap = true;
            lastSeenMessage = controller.LastInputMessage;
            PushLog($"함정 배치: {preset.DisplayName} @{SpawnCoord} — 보드/상태 리셋됨");
            RevealSpawnedTrap();
        }

        /// <summary>
        /// 함정 발견 기록을 남겨 붉은 마커(와 예고형 오버레이)가 보이게 한다.
        /// ⚠️발견은 안개와 독립이다 — 안개를 전부 걷어도 발견 기록이 없으면 마커는 안 뜬다.
        /// </summary>
        private void RevealSpawnedTrap()
        {
            var revealed = controller.State?.DebugRevealTrapsForTuning(SpawnCoord, 1) ?? 0;
            PushLog(revealed > 0
                ? $"정찰 발견: 함정 {revealed}개 — 붉은 마커가 떠야 한다"
                : "정찰 발견: 새로 발견된 함정 없음 (이미 발견됐거나 소진됨)");
        }

        private void DrawTrapTriggerSection()
        {
            if (!hasSpawnedTrap || spawnedTrapPreset == null)
            {
                return;
            }

            GUILayout.Label($"트리거 — 대상: {spawnedTrapPreset.DisplayName} @{SpawnCoord}");
            GUILayout.Label($"플레이어 HP {controller.State?.Player.Hp}/{controller.State?.Player.MaxHp}"
                            + $" · 몬스터 {controller.State?.Monsters.Count ?? 0}마리"
                            + $" · 전체 턴 {controller.State?.OverallTurnNumber}");

            var isPeriodic = spawnedTrapPreset.PeriodTurns > 0;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("이동해서 밟기 (실경로)", GUILayout.Height(ButtonHeight)))
            {
                if (isPeriodic)
                {
                    PushLog("⚠️ 예고형은 밟아도 안 터진다 — 런타임이 주기형을 밟기 경로에서 뺀다. '턴 넘기기'로 확인할 것.");
                }

                if (!controller.BeginMoveSelection() || !controller.TryMoveTo(SpawnCoord))
                {
                    PushLog($"이동 실패: {controller.LastInputMessage}");
                }
            }

            if (GUILayout.Button("발견 다시 (마커)", GUILayout.Height(ButtonHeight)))
            {
                RevealSpawnedTrap();
            }

            if (GUILayout.Button("리셋(재배치)", GUILayout.Height(ButtonHeight)))
            {
                SpawnTrap(spawnedTrapPreset);
            }

            GUILayout.EndHorizontal();

            if (GUILayout.Button("턴 넘기기 (예고→발동 확인)", GUILayout.Height(ButtonHeight)))
            {
                // 이동 페이즈면 한 번, 행동 페이즈면 한 번 — 두 번 눌러 한 바퀴가 돈다. 주기 함정은
                // OverallTurnNumber % periodTurns == 0에서 터지므로 전체 턴 수를 위 라벨에서 같이 본다.
                if (!controller.EndAction())
                {
                    PushLog($"턴 넘기기 실패: {controller.LastInputMessage}");
                }
            }

            if (isPeriodic)
            {
                GUILayout.Label($"⚠️ 예고형({spawnedTrapPreset.PeriodTurns}턴 주기): 밟기로는 안 터진다. "
                                + "플레이어 턴에 붉은 사선 예고가 뜨고 턴 말에 발동한다.");
            }

            if (spawnedTrapPreset.EffectKind == HexTrapEffectKind.SpawnMonsters)
            {
                GUILayout.Label("⚠️ 스폰 함정: 소환 중심은 함정 칸이 아니라 **밟은 칸**이다. 위 '몬스터' 수로 확인.");
            }

            GUILayout.Space(6f);
        }

        private void DrawTriggerSection()
        {
            if (!hasSpawned || spawnedEntry == null)
            {
                return;
            }

            GUILayout.Label($"트리거 — 대상: {spawnedEntry.ObjectRef} @{SpawnCoord}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("이동해서 밟기 (실경로)", GUILayout.Height(ButtonHeight)))
            {
                if (!controller.BeginMoveSelection() || !controller.TryMoveTo(SpawnCoord))
                {
                    PushLog($"이동 실패: {controller.LastInputMessage}");
                }
            }

            if (GUILayout.Button("즉시 트리거 (순간이동)", GUILayout.Height(ButtonHeight)))
            {
                if (!controller.DebugTeleportPlayerTo(SpawnCoord))
                {
                    PushLog("순간이동 실패 (이동 차단 오브젝트는 밟을 수 없음)");
                }
                else if (!controller.DebugTriggerTileInteractionAtPlayerCoord())
                {
                    PushLog("상호작용 없음 (이 오브젝트는 밟기 이벤트가 없거나 이미 소비됨)");
                }
            }

            if (GUILayout.Button("리셋(재소환)", GUILayout.Height(ButtonHeight)))
            {
                Spawn(spawnedEntry);
            }

            GUILayout.EndHorizontal();
            if (spawnedEntry.ObjectType == HexMapObjectType.MemoryStone)
            {
                GUILayout.Label("⚠️ 기억결은 승리 연출로 이어집니다 — 트리거 후에는 리셋(재소환)으로 복구하세요.");
            }

            GUILayout.Space(6f);
        }

        private void DrawStateSection()
        {
            var state = controller.State;
            var wallet = state.PlayerInventory?.Wallet;
            var relics = state.PlayerInventory?.RelicsAndCurses;
            GUILayout.Label($"상태 — 재화 {wallet?.Balance ?? 0} · 유물 {relics?.RelicCount ?? 0} · 소비된 오브젝트 {state.ClaimedEventObjectIds.Count}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("재화 +100", GUILayout.Height(ButtonHeight)))
            {
                wallet?.Add(100);
                PushLog($"재화 +100 (보유 {wallet?.Balance ?? 0}) — 상점 구매 확인용");
            }

            if (GUILayout.Button("보상 팝업 (디버그)", GUILayout.Height(ButtonHeight)))
            {
                controller.DebugShowCardReward(false);
            }

            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
        }

        private void DrawLogSection()
        {
            GUILayout.Label("이벤트 로그 (최근이 아래)");
            foreach (var line in eventLog)
            {
                GUILayout.Label(line);
            }
        }
    }
}
