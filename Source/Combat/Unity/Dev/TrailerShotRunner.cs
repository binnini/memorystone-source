using System;
using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Dev-only trailer camera rig: the takes that the stage intro cinematic does not already cover
    /// (docs/trailer-capture-plan.md — take B "cut #4" curved drone flight, take C "cut #5" monster
    /// showcase, take D "cut #6" memory stone orbit-then-push-in). Cuts #1-#3 are the stage intro itself;
    /// replay those with the debug panel's "Replay Stage Intro" button instead of anything here.
    ///
    /// Nothing in here is new game behavior — every piece is assembled out of the seams the intro
    /// already owns (MapCombatController's Debug*Cinematic camera hijack, marker-UI hide, spawn VFX,
    /// face-at-camera, attack trigger). The one genuinely new thing is the curved flight path: the
    /// intro's authored dolly is a straight polyline, and cut #4 asks for a curve, so waypoints are run
    /// through a Catmull-Rom spline with arc-length reparameterization (constant felt speed regardless
    /// of how unevenly the waypoints are spaced).
    ///
    /// Gated behind CombatDebugControlPanel.DebugUiAvailable like the rest of the dev tooling, so a
    /// shipping build never runs a take even if the component is left in the scene.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrailerShotRunner : MonoBehaviour
    {
        [Header("배선")]
        [Tooltip("촬영 대상 전투 컨트롤러. 비우면 씬에서 찾습니다.")]
        [SerializeField] private MapCombatController controller;

        [Tooltip("UI 숨김 컨트롤러. 비우면 씬에서 찾습니다. 없으면 UI는 수동으로 숨겨야 합니다(\\ 키).")]
        [SerializeField] private DebugUiVisibilityController uiVisibility;

        [Header("촬영 상태")]
        [Tooltip("촬영 카메라의 시야각(FOV). 인트로 기본 60보다 좁히면 더 압축된 드론샷 느낌이 납니다.")]
        [SerializeField] private float cameraFieldOfView = 50f;

        [Header("컷4 — 곡선 드론샷")]
        [Tooltip("비행 경로 웨이포인트(헥스 좌표, 저작 순서대로). 2개 이상이어야 비행합니다. 자전거도로 -> 롯데월드.")]
        [SerializeField] private List<TrailerWaypoint> droneWaypoints = new List<TrailerWaypoint>();

        [Tooltip("비행 속도(월드 유닛/초). 경로 전체를 등속으로 통과합니다.")]
        [SerializeField] private float droneSpeedUnitsPerSecond = 6f;

        [Tooltip("카메라가 진행 방향으로 얼마나 앞을 보는지(월드 유닛). 인트로 돌리와 같은 문법.")]
        [SerializeField] private float droneLookAheadDistance = 8f;

        [Tooltip("룩앳 지점을 타일 위로 얼마나 띄울지(월드 유닛).")]
        [SerializeField] private float droneLookAtHeight = 1.5f;

        [Tooltip("막 컷에서 바라볼 몬스터의 카탈로그 id. 비우면 룩앳 전환 없이 경로 끝까지 진행 방향만 봅니다. 콘티 기본값=불가살(M002).")]
        [SerializeField] private string droneFinaleMonsterDefinitionId = "M002";

        [Tooltip("경로 마지막 몇 초 동안 룩앳을 위 몬스터로 넘길지(초). 0이면 전환 없음.")]
        [SerializeField] private float droneFinaleLookAtBlendSeconds = 2.5f;

        [Tooltip("막 컷 룩앳을 몬스터 타일 위로 띄우는 높이(월드 유닛).")]
        [SerializeField] private float droneFinaleLookAtHeight = 1.5f;

        [Tooltip("경로변 몬스터가 카메라에 처음 잡힐 때 정면을 보도록 한 번만 돌립니다(추적 회전은 부자연스러워 안 함).")]
        [SerializeField] private bool droneFaceMonstersAtCamera = true;

        [Tooltip("카메라가 몇 유닛 안으로 들어오면 돌릴지. 룩어헤드(8)보다 커야 화면에 잡히기 전에 돌아섭니다.")]
        [SerializeField] private float droneFaceDistance = 22f;

        [Tooltip("경로가 끝난 뒤 몇 초 동안 카메라가 위·뒤로 빠지며 끝낼지(초). 0이면 경로 끝에서 그대로 정지(줌인 느낌).")]
        [SerializeField] private float dronePullOutSeconds = 2f;

        [Tooltip("풀아웃 동안 올라갈 높이(월드 유닛).")]
        [SerializeField] private float dronePullOutHeightGain = 7f;

        [Tooltip("풀아웃 동안 몬스터 반대쪽으로 물러날 수평 거리(월드 유닛).")]
        [SerializeField] private float dronePullOutBackDistance = 9f;

        [Header("컷5 — 몬스터 쇼케이스")]
        [Tooltip("클로즈업할 몬스터 목록(위에서부터 순서대로). 예: 호랑 선생 M006, 요술 사자탈 M004, 불가살 M002.")]
        [SerializeField] private List<TrailerShowcaseEntry> showcaseEntries = new List<TrailerShowcaseEntry>();

        [Tooltip("클로즈업 1컷 길이(초).")]
        [SerializeField] private float showcaseShotSeconds = 3f;

        [Tooltip("카메라 거리. 인트로 스폰 비트 기저값 7.")]
        [SerializeField] private float showcaseCameraDistance = 7f;

        [Tooltip("카메라 앙각(도). 낮을수록 로우 앵글. 인트로 스폰 비트 기저값 18.")]
        [SerializeField] private float showcaseCameraPitchDegrees = 18f;

        [Tooltip("룩앳을 몬스터 타일 위로 띄우는 높이. 인트로 스폰 비트 기저값 1.2.")]
        [SerializeField] private float showcaseLookAtHeight = 1.2f;

        [Tooltip("한 컷에서 카메라가 그리는 호의 좌우 반각(도). 클수록 크게 돕니다.")]
        [SerializeField] private float showcaseArcHalfDegrees = 16f;

        [Tooltip("컷이 바뀌어도 호를 그리는 방향은 그대로 이어집니다(콘티: 무빙이 컷을 넘어 이어짐). 음수면 반대 방향.")]
        [SerializeField] private float showcaseSweepDirection = 1f;

        [Tooltip("컷 시작 후 몇 초 뒤에 공격 모션을 터뜨릴지(초). 0이면 공격 없음.")]
        [SerializeField] private float showcaseAttackDelaySeconds = 0.6f;

        [Tooltip("컷 시작 시 스폰 VFX를 터뜨립니다. 이미 서 있는 몬스터를 찍을 땐 꺼두세요.")]
        [SerializeField] private bool showcasePlaySpawnVfx;

        [Tooltip("켜면 각 컷 동안 피사체 몬스터만 남기고 나머지 마커를 전부 숨깁니다(콘티: 배경에 다른 요괴 없음).")]
        [SerializeField] private bool showcaseIsolateSubject = true;

        [Tooltip("마지막 컷이 끝난 뒤 최종 프레이밍을 유지하는 여유(초). 공격 모션이 끝까지 재생될 틈입니다.")]
        [SerializeField] private float showcaseTailHoldSeconds = 1.5f;

        [Header("컷6 — 기억결 강조")]
        [Tooltip("오빗 반경(월드 유닛). 후보 스틸 판정값 12.")]
        [SerializeField] private float memoryStoneOrbitDistance = 12f;

        [Tooltip("카메라 부각(도). 후보 스틸 판정값 20(인트로 기억석 샷의 45는 부감이라 결이 눌립니다).")]
        [SerializeField] private float memoryStonePitchDegrees = 20f;

        [Tooltip("오빗 시작 방위각(도). 배경을 고르는 값 — 270°에서 롯데월드 성이 결 뒤를 채웁니다.")]
        [SerializeField] private float memoryStoneStartAzimuthDegrees = 270f;

        [Tooltip("오빗으로 도는 각도(도). 음수면 반대 방향.")]
        [SerializeField] private float memoryStoneOrbitDegrees = 60f;

        [Tooltip("오빗에 쓰는 시간(초).")]
        [SerializeField] private float memoryStoneOrbitSeconds = 5f;

        [Tooltip("오빗이 멈추고 줌인이 시작되기까지의 정지 시간(초). 콘티: 돌다가 '멈춰서' 줌인.")]
        [SerializeField] private float memoryStoneSettleSeconds = 0.8f;

        [Tooltip("줌인(돌리 인)에 쓰는 시간(초).")]
        [SerializeField] private float memoryStoneZoomSeconds = 4f;

        [Tooltip("줌인 종료 거리(월드 유닛). 5에서 기억결+금테가 화면에 딱 들어옵니다(후보 스틸 판정).")]
        [SerializeField] private float memoryStoneZoomDistance = 5f;

        [Tooltip("룩앳을 타일 위로 띄우는 높이. 기억결 실측 높이 3.32u의 수직 중심 = 1.7. 음수면 컨트롤러의 stageIntroLookAtHeightOffset을 씁니다.")]
        [SerializeField] private float memoryStoneLookAtHeightOverride = 1.7f;

        [Tooltip("줌인이 끝난 뒤 최종 프레이밍을 유지하는 여유(초). 편집 여유입니다.")]
        [SerializeField] private float memoryStoneTailHoldSeconds = 1.5f;

        [Tooltip("암시야 처리. RevealAll=암시야 OFF(기본), Covered=암시야에 덮인 그대로, StoneOnly=기억결 칸만 탐색됨, Surroundings=기억결 주변까지 탐색됨.")]
        [SerializeField] private MemoryStoneFogMode memoryStoneFogMode = MemoryStoneFogMode.RevealAll;

        [Tooltip("Surroundings 모드에서 기억결 둘레 몇 칸까지 탐색된 것으로 볼지(헥스 거리).")]
        [SerializeField] private int memoryStoneFogRevealRadius = 3;

        [Header("컷7 — 플레이어 사망")]
        [Tooltip("사망 비트 목록. 0~2 = 확정 구도 3종(2026-07-26 후보 스틸 판정: 잠실대교 +45°, 자전거도로 클로즈업, 롯데타워 +45°), 3~5 = 같은 구도의 다수 몬스터 변주. 가해 몬스터는 테이크 호출 시 오버라이드 가능.")]
        [SerializeField] private List<TrailerPlayerDeathEntry> playerDeathEntries = new List<TrailerPlayerDeathEntry>
        {
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M006", playerColumn = 116, playerRow = -65,
                monsterColumn = 117, monsterRow = -66, cameraAzimuthDegrees = 75f,
            },
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M004", playerColumn = 145, playerRow = -83,
                monsterColumn = 146, monsterRow = -84, cameraAzimuthDegrees = 150f,
                cameraDistanceMultiplier = 0.72f, pitchOverrideDegrees = 10f,
            },
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M002", playerColumn = 152, playerRow = -101,
                monsterColumn = 153, monsterRow = -102, cameraAzimuthDegrees = 295f,
                cameraDistanceMultiplier = 1.45f,
            },
            // r3~r5: 2026-07-26 지역 확장(잠실역 사거리 / 롯데월드 남단대로 / 잠실대교 북단 진입).
            // 좌표는 간선도로(q = 51 - r) 실측 기반 저작, 방위각은 후보 스틸 판정으로 확정.
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M001", playerColumn = 129, playerRow = -78,
                monsterColumn = 130, monsterRow = -79, cameraAzimuthDegrees = 60f,
            },
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M002", playerColumn = 152, playerRow = -104,
                monsterColumn = 153, monsterRow = -105, cameraAzimuthDegrees = 295f,
                cameraDistanceMultiplier = 1.45f,
            },
            // ⚠️ 플레이어 칸을 (108,-57)에서 한 칸 밀었다: 그 칸은 Stage_1_Source의 trapRefs 함정이라
            // 텔레포트만으로 "함정 발동! 중독 3"과 독 링 VFX가 붙어 테이크가 오염된다(affectsPlayer +
            // triggerOnEnter). 같은 간선도로(q + r = 51) 위로 옮겨 잠실대교 표지판 구도는 유지된다.
            // 신규 사망 좌표를 저작할 땐 플레이어 칸을 trapRefs와 대조할 것(몬스터 칸은
            // affectsMonsters = false라 무해).
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M006", playerColumn = 109, playerRow = -58,
                monsterColumn = 110, monsterRow = -59, cameraAzimuthDegrees = 30f,
            },
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M006", playerColumn = 116, playerRow = -65,
                monsterColumn = 117, monsterRow = -66, cameraAzimuthDegrees = 75f,
                extraMonsters = new List<TrailerDeathExtraMonster>
                {
                    new TrailerDeathExtraMonster { definitionId = "M001", column = 114, row = -63 },
                    new TrailerDeathExtraMonster { definitionId = "M003", column = 119, row = -68 },
                    new TrailerDeathExtraMonster { definitionId = "M005", column = 117, row = -64 },
                },
            },
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M004", playerColumn = 145, playerRow = -83,
                monsterColumn = 146, monsterRow = -84, cameraAzimuthDegrees = 150f,
                cameraDistanceMultiplier = 0.72f, pitchOverrideDegrees = 10f,
                extraMonsters = new List<TrailerDeathExtraMonster>
                {
                    new TrailerDeathExtraMonster { definitionId = "M001", column = 143, row = -81 },
                    new TrailerDeathExtraMonster { definitionId = "M006", column = 148, row = -86 },
                    new TrailerDeathExtraMonster { definitionId = "M003", column = 147, row = -82 },
                },
            },
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M002", playerColumn = 152, playerRow = -101,
                monsterColumn = 153, monsterRow = -102, cameraAzimuthDegrees = 295f,
                cameraDistanceMultiplier = 1.45f,
                extraMonsters = new List<TrailerDeathExtraMonster>
                {
                    new TrailerDeathExtraMonster { definitionId = "M006", column = 150, row = -99 },
                    new TrailerDeathExtraMonster { definitionId = "M004", column = 154, row = -104 },
                    new TrailerDeathExtraMonster { definitionId = "M005", column = 150, row = -102 },
                },
            },
            // 엔트리 9: 사용자 지정 콘티 — 지하철역(잠실역) 거리에서 깡패 돼지에게 죽고,
            // 삼목구·성난 황소가 옆에 있는 다수 비트.
            // 장식 2기는 간선도로(q + r = 51)에서 벗어난 칸에 둔다: 도로 위에 두면 플레이어·가해자와
            // 정확히 일직선이라 넷이 한 줄로 늘어선다(후보 스틸 판정). 거리 2·3으로 어긋나게 배치해
            // 서로 다른 깊이에서 플레이어를 둘러싸게 했다(거리 2가 하한 — 1이면 치명타를 가로챈다).
            new TrailerPlayerDeathEntry
            {
                monsterDefinitionId = "M005", playerColumn = 129, playerRow = -78,
                monsterColumn = 130, monsterRow = -79, cameraAzimuthDegrees = 105f,
                extraMonsters = new List<TrailerDeathExtraMonster>
                {
                    new TrailerDeathExtraMonster { definitionId = "M001", column = 127, row = -77 },
                    new TrailerDeathExtraMonster { definitionId = "M003", column = 130, row = -76 },
                },
            },
            // 엔트리 10: 사용자 지정 콘티 2 — 아파트 거리에서 호랑 선생(M006)에게 죽고 요술 사자탈(M004)이
            // 옆에 있는 비트. 기존 6지역에 이 조합(M006 가해 + M004 동반)이 없어서 신설했다.
            // 장소 = apartmentA_3000 6채가 늘어선 줄(q + r = 47, (131,-84)~(146,-99)) 옆 간선도로 —
            // 그 중간인 (139,-88)에 서면 아파트 열이 카메라 배경을 채운다. 장식은 엔트리 9와 같은 이유로
            // 간선도로에서 벗어난 칸에 둔다(도로 위면 셋이 일직선).
            new TrailerPlayerDeathEntry
            {
                // ⚠️방위각은 아크를 함께 봐야 한다. 105°는 카메라가 아파트 벽 안에 놓여 첫 400프레임이
                // 통째로 회색 벽이었고, 아크가 +45° 돌아간 뒤에야 그림이 나왔다. 195°는 시작(195)과
                // 아크 종료(240) 양쪽 다 아파트 열을 배경으로 둔 채 세 배우가 분리돼 읽힌다 —
                // 사다리 판정 output/trailer-takes/cut7/R6_azimuth_ladder.png. 240° 시작은 금지:
                // 그림은 더 좋지만 아크가 285°로 들어가고 그 각도는 카메라가 사자탈에 박힌다.
                monsterDefinitionId = "M006", playerColumn = 139, playerRow = -88,
                monsterColumn = 140, monsterRow = -89, cameraAzimuthDegrees = 195f,
                extraMonsters = new List<TrailerDeathExtraMonster>
                {
                    new TrailerDeathExtraMonster { definitionId = "M004", column = 141, row = -88 },
                },
            },
        };

        [Tooltip("사망 비트의 암시야(시네마틱 스타일 전용 — 인게임 스타일은 항상 실플레이 안개). PlayerSurroundings=플레이어 둘레만 밝음.")]
        [SerializeField] private DeathFogMode deathFogMode = DeathFogMode.PlayerSurroundings;

        [Tooltip("PlayerSurroundings 모드에서 플레이어 둘레 몇 칸까지 밝게 볼지(헥스 거리).")]
        [SerializeField] private int deathFogRevealRadius = 3;

        [Tooltip("공격 트리거 후 임팩트(피격·사망 반응)까지 추가로 벌리는 시간(초). 저작 타이밍 그대로면 휘두르기도 전에 죽는 것처럼 찍혀서 벌립니다.")]
        [SerializeField] private float deathImpactDelayBonusSeconds = 0.5f;

        [Tooltip("사망 순간 정지 연출(히트스톱 0.1s + 슬로모션 1.0s)의 길이 배율(촬영 전용, 게임 저작값 무변경). 1=저작 그대로, 0.5=절반(2026-07-26 사용자 확정).")]
        [SerializeField] private float deathFreezeDurationScale = 0.5f;

        [Tooltip("IngameHud 스타일의 손패 주입 폴백 풀. 기본은 현재 런의 실제 덱(ActiveCardCatalogIds)에서 무작위로 뽑고, 그 풀이 비어 있을 때만 이 목록을 씁니다.")]
        [SerializeField] private List<string> deathHudHandCardIds = new List<string> { "M01", "M02", "A01", "A02", "A03" };

        [Tooltip("IngameHud 스타일에서 테이크마다 무작위로 뽑는 손패 장수의 최소값(사용자 확정 5~9).")]
        [SerializeField] private int deathHudHandMinCards = 5;

        [Tooltip("손패 장수 최대값.")]
        [SerializeField] private int deathHudHandMaxCards = 9;

        [Tooltip("인게임 스타일(igc/hud)의 카메라 구도 변주. 0=기본 플레이 뷰, 1=추가 구도(돌려서 당긴 뷰). 테이크 호출 시 인덱스로 선택합니다. zoomDistance가 0 이하면 세션의 현재 줌을 기준으로 씁니다.")]
        [SerializeField] private List<TrailerIngameCameraVariant> ingameCameraVariants = new List<TrailerIngameCameraVariant>
        {
            new TrailerIngameCameraVariant { orbitYawDegrees = 0f, zoomDistance = -1f },
            // 줌 14(2026-07-26 사용자 확정, 스윕 판정 output/trailer-takes/cut7/ZOOM_sweep.png).
            // 7.5는 너무 당겨져 플레이어가 화면을 채우고 가해자가 등 뒤로 숨었다. 10은 안 되는데,
            // ⚠️변주 0(= hudA)이 쓰는 세션 기본 플레이 줌이 실측 10.14라 구도가 거의 같아진다 —
            // 두 hud 플레이트를 나눈 의미가 사라진다. 클램프는 바인더 프로파일의 7~18.
            new TrailerIngameCameraVariant { orbitYawDegrees = 42f, zoomDistance = 14f },
            // 변주 2 = igc(IngameClean) 전용. 기본 플레이 줌(≈10.1)보다 조금 넓게(2026-07-26 사용자 지시).
            // 회전 0은 유지 — igc는 "실플레이 뷰 그대로, UI만 없음"이 문법이라 각도를 틀면 정체성이 흐려진다.
            // 변주 0을 넓히면 hudA까지 같이 바뀌므로 별도 인덱스로 뺐다.
            new TrailerIngameCameraVariant { orbitYawDegrees = 0f, zoomDistance = 12f },
        };

        [Tooltip("인게임 스타일에서 테이크마다 카메라 요(orbit)를 ±이 범위로 흔들어 '실제 유저가 돌려놓은' 느낌을 냅니다(도).")]
        [SerializeField] private float ingameCameraYawJitterDegrees = 14f;

        [Tooltip("테이크마다 줌 거리를 ±이 범위로 흔듭니다(월드 유닛).")]
        [SerializeField] private float ingameCameraZoomJitter = 1.2f;

        [Tooltip("카메라 거리(월드 유닛). 컷5 클로즈업(7)보다 살짝 넓혀 가해자+피해자 투샷 기저.")]
        [SerializeField] private float deathCameraDistance = 8f;

        [Tooltip("카메라 앙각(도). 낮을수록 로우 앵글 — 쓰러지는 실루엣이 하늘/배경에 걸립니다.")]
        [SerializeField] private float deathCameraPitchDegrees = 14f;

        [Tooltip("룩앳을 타일 위로 띄우는 높이(월드 유닛).")]
        [SerializeField] private float deathLookAtHeight = 1.1f;

        [Tooltip("룩앳을 플레이어(0)와 몬스터(1) 사이 어디에 둘지. 0.4면 플레이어 중심의 투샷.")]
        [SerializeField, Range(0f, 1f)] private float deathLookAtBlendToMonster = 0.4f;

        [Tooltip("비트 동안 카메라가 초당 몇 도씩 천천히 도는지(도/초). 0이면 고정. 음수는 반대 방향.")]
        [SerializeField] private float deathArcDegreesPerSecond = 4f;

        [Tooltip("하드컷 후 공격이 시작되기까지의 정적(초). 구도를 읽을 틈이자 스폰 프레임 트리거 유실 가드.")]
        [SerializeField] private float deathPreAttackDelaySeconds = 0.7f;

        [Tooltip("사망 연출이 끝난 뒤 최종 프레이밍을 유지하는 여유(초). 편집 여유입니다.")]
        [SerializeField] private float deathTailHoldSeconds = 1.2f;

        [Tooltip("켜면 비트 동안 가해 몬스터만 남기고 나머지 마커를 숨깁니다(콘티: 배경에 다른 요괴 없음).")]
        [SerializeField] private bool deathIsolateSubject = true;

        private Coroutine activeTake;
        private bool filmingStateEngaged;
        private bool dayLookEngaged;
        private bool playerDeathTakeEngaged;

        /// <summary>
        /// How cut #6 treats the fog of war. Every mode other than <see cref="RevealAll"/> puts the fog
        /// back after the filming state turned it off, and the two partial modes light their cells through
        /// the intro's per-cell forced-reveal override rather than by exploring anything for real.
        /// </summary>
        public enum MemoryStoneFogMode
        {
            /// <summary>암시야 OFF — 맵 전체가 보이는 트레일러 기본 상태.</summary>
            RevealAll = 0,

            /// <summary>암시야 그대로. 기억결 일대가 미탐색이면 어둠에 덮인 채로 찍힙니다.</summary>
            Covered = 1,

            /// <summary>기억결이 선 칸 하나만 탐색된 것으로 — 어둠 속에 결만 떠오릅니다.</summary>
            StoneOnly = 2,

            /// <summary>기억결과 그 둘레 <c>memoryStoneFogRevealRadius</c>칸까지 탐색된 것으로.</summary>
            Surroundings = 3,
        }

        /// <summary>Waypoint on the drone path: a map cell plus how high above it the camera flies.</summary>
        [Serializable]
        public sealed class TrailerWaypoint
        {
            [Tooltip("헥스 좌표 column (맵 소스의 column 값).")]
            public int column;

            [Tooltip("헥스 좌표 row (맵 소스의 row 값).")]
            public int row;

            [Tooltip("이 지점에서의 카메라 높이(타일 위 월드 유닛). 저공 비행은 2~4 정도.")]
            public float cameraHeight = 3f;
        }

        /// <summary>One close-up beat: which monster, where it should stand, and whether to conjure it.</summary>
        [Serializable]
        public sealed class TrailerShowcaseEntry
        {
            [Tooltip("몬스터 카탈로그 id (M001~M006).")]
            public string monsterDefinitionId = string.Empty;

            [Tooltip("촬영 좌표 column. spawnAtCoord가 꺼져 있으면 이 값은 무시하고 맵에 이미 있는 개체를 찍습니다.")]
            public int column;

            [Tooltip("촬영 좌표 row.")]
            public int row;

            [Tooltip("켜면 위 좌표에 이 몬스터를 새로 세웁니다(각기 다른 배경에 세우는 용도). 끄면 맵에 배치된 개체를 그대로 씁니다.")]
            public bool spawnAtCoord = true;

            [Tooltip("카메라가 어느 방향에서 보는지(도, 0~360). 배경을 고르는 값입니다.")]
            public float cameraAzimuthDegrees;

            [Tooltip("공격 모션 트리거 이름(몬스터별 저작 Attack1~5, monster_animation_attack_clips.csv). 비우면 기본 공격.")]
            public string attackAnimationTrigger = string.Empty;

            [Tooltip("이 몬스터 컷의 카메라 거리 배수. 키 큰 몬스터가 잘리면 1보다 키우세요(불가살=1.45).")]
            public float cameraDistanceMultiplier = 1f;

            [Tooltip("이 몬스터 컷의 룩앳 높이(월드 유닛). 음수면 공용 showcaseLookAtHeight를 씁니다.")]
            public float lookAtHeightOverride = -1f;
        }

        /// <summary>
        /// How a death beat is filmed. Cinematic hijacks the camera into the authored two-shot; the two
        /// Ingame styles keep the real gameplay follow camera (so the built-in death zoom/shake plays) and
        /// differ only in whether the HUD stays up — IngameHud is the "died mid-play" plate and hides
        /// nothing at all, not even the game-over screen at the tail.
        /// </summary>
        public enum DeathTakeStyle
        {
            Cinematic = 0,
            IngameClean = 1,
            IngameHud = 2,
        }

        /// <summary>암시야 처리(시네마틱 스타일 전용). 인게임 스타일은 항상 실플레이 안개를 쓴다.</summary>
        public enum DeathFogMode
        {
            /// <summary>암시야 OFF — 다른 트레일러 테이크와 같은 전체 공개.</summary>
            RevealAll = 0,

            /// <summary>플레이어 둘레 <c>deathFogRevealRadius</c>칸만 밝고 나머지는 어둠(사용자 확정 기본).</summary>
            PlayerSurroundings = 1,

            /// <summary>실제 탐험 상태 그대로(텔레포트가 갱신한 실시야 포함).</summary>
            RealFog = 2,
        }

        /// <summary>
        /// One authored pose of the real play camera for the ingame styles — the orbit yaw and zoom a
        /// player might have left it at. Per-take jitter is layered on top of the picked variant.
        /// </summary>
        [Serializable]
        public sealed class TrailerIngameCameraVariant
        {
            [Tooltip("카메라 공전 요(도). 0 = 기본 정면 플레이 뷰.")]
            public float orbitYawDegrees;

            [Tooltip("줌 거리(월드 유닛). 0 이하 = 세션의 현재 줌 유지(그 값이 지터의 기준이 됩니다).")]
            public float zoomDistance = -1f;
        }

        /// <summary>다수 몬스터 변주의 장식 배치 1기. 공격은 여전히 최근접(저작 가해자)이 한다.</summary>
        [Serializable]
        public sealed class TrailerDeathExtraMonster
        {
            [Tooltip("몬스터 카탈로그 id (M001~M006).")]
            public string definitionId = string.Empty;

            [Tooltip("배치 좌표 column. 플레이어에서 거리 2 이상이어야 치명타를 가로채지 않습니다.")]
            public int column;

            [Tooltip("배치 좌표 row.")]
            public int row;
        }

        /// <summary>
        /// One death beat: where the player stands, which monster kills them from where, and how the camera
        /// frames the two-shot. The lethal blow is the controller's monster-attack replay, which always
        /// picks the monster NEAREST the player — so the authored monster coordinate must be closer than
        /// any other living monster (the beat warns when it is not).
        /// </summary>
        [Serializable]
        public sealed class TrailerPlayerDeathEntry
        {
            [Tooltip("가해 몬스터 카탈로그 id (M001~M006). 공격 모션/타이밍은 그 몬스터의 저작 패턴을 따릅니다.")]
            public string monsterDefinitionId = "M002";

            [Tooltip("플레이어를 세울 좌표 column. 걷기 가능·비점유 타일이어야 하며 함정·기억결 타일은 피하세요.")]
            public int playerColumn;

            [Tooltip("플레이어를 세울 좌표 row.")]
            public int playerRow;

            [Tooltip("끄면 플레이어를 옮기지 않고 지금 서 있는 자리에서 찍습니다(위 좌표 무시).")]
            public bool movePlayerToCoord = true;

            [Tooltip("몬스터를 세울 좌표 column. 플레이어 바로 옆(거리 1)이 정석 — 리플레이는 '가장 가까운 몬스터'가 때립니다.")]
            public int monsterColumn;

            [Tooltip("몬스터를 세울 좌표 row.")]
            public int monsterRow;

            [Tooltip("카메라가 어느 방향에서 보는지(도, 0~360). 배경을 고르는 값입니다.")]
            public float cameraAzimuthDegrees;

            [Tooltip("이 비트의 카메라 거리 배수. 키 큰 몬스터가 잘리면 1보다 키우세요(불가살=1.45).")]
            public float cameraDistanceMultiplier = 1f;

            [Tooltip("이 비트의 카메라 앙각(도). 0 이하면 공용 deathCameraPitchDegrees를 씁니다.")]
            public float pitchOverrideDegrees = -1f;

            [Tooltip("이 비트의 룩앳 높이(월드 유닛). 음수면 공용 deathLookAtHeight를 씁니다.")]
            public float lookAtHeightOverride = -1f;

            [Tooltip("추가 장식 몬스터 목록(다수 몬스터 변주). 비워두면 1:1 비트.")]
            public List<TrailerDeathExtraMonster> extraMonsters = new List<TrailerDeathExtraMonster>();
        }

        private bool Available => CombatDebugControlPanel.DebugUiAvailable;

        // Normally unscaled, so a hit-stop or a paused timeScale cannot stretch a take; switched to
        // Time.deltaTime while capturing, because Time.captureDeltaTime pins deltaTime to 1/fps but leaves
        // unscaledDeltaTime on the wall clock — which time-compresses recorded footage.
        private float TakeDeltaTime => captureTimeSource ? Time.deltaTime : Time.unscaledDeltaTime;

        private bool captureTimeSource;

        /// <summary>Dev-only: see <see cref="MapCombatController.DebugSetCinematicCaptureTimeSource"/>.</summary>
        public void SetCaptureTimeSource(bool useCapturedTime)
        {
            captureTimeSource = useCapturedTime;
            if (controller != null)
            {
                controller.DebugSetCinematicCaptureTimeSource(useCapturedTime);
            }
        }

        /// <summary>True while a take is running (camera hijacked, UI/fog/overlays suppressed).</summary>
        public bool IsFilming => filmingStateEngaged;

        /// <summary>
        /// True only while a take's routine is actually advancing. Distinct from <see cref="IsFilming"/>,
        /// which stays engaged after the routine ends (the final pose is held until StopTake hands the
        /// view back) — capture tooling needs the routine-end edge, not the filming state.
        /// </summary>
        public bool IsTakePlaying => activeTake != null;

        private void Awake()
        {
            controller ??= FindFirstObjectByType<MapCombatController>();
            uiVisibility ??= FindFirstObjectByType<DebugUiVisibilityController>();
        }

        private void OnDisable()
        {
            // A take must never outlive the runner: the camera hijack and the hidden UI/fog are all
            // borrowed state, and nothing else would hand them back.
            activeTake = null;
            if (filmingStateEngaged)
            {
                ExitTrailerFilmingState();
            }
        }

        // --- Filming state ------------------------------------------------------------------------

        /// <summary>
        /// One button to make the game look like a trailer plate: fog off, UI hidden, marker dressing
        /// (name/HP/badge) off, every tactical overlay off. Idempotent.
        /// </summary>
        /// <remarks>
        /// UI is hidden only through <see cref="DebugUiVisibilityController"/>'s CanvasGroup-alpha path,
        /// never CinematicUiHider's SetActive path — mixing the two in one take lets the hider's Restore()
        /// resurrect what this tool switched off (see DebugUiVisibilityController's class comment).
        /// </remarks>
        public void EnterTrailerFilmingState()
        {
            if (!Available || controller == null || filmingStateEngaged)
            {
                return;
            }

            filmingStateEngaged = true;

            // Borrow the stage intro's hover/occlusion suppression. Without it a mouse resting over the map
            // leaves a tooltip in the plate and fades the buildings it is "occluding" to half transparent —
            // both showed up in the first framing captures.
            controller.DebugSetTrailerFilmingActive(true);
            controller.SetFogDebugVisible(false);
            controller.DebugSetActorMarkerUiVisible(false);
            // Floating damage numbers are world-space objects the canvas-alpha UI hide cannot reach; the
            // death take's lethal hit would otherwise stamp its number into the plate.
            EffectPresentationController.DebugSuppressFloatingText = true;
            SetAllOverlaysVisible(false);

            if (uiVisibility != null)
            {
                // The level label is drawn by OnGUI, outside the canvas the alpha pass dims, so it would
                // be the one piece of chrome left in the plate.
                uiVisibility.SetLevelLabelVisible(false);
                uiVisibility.SetLevel(DebugUiVisibilityLevel.None);
            }
        }

        /// <summary>Exactly the inverse of <see cref="EnterTrailerFilmingState"/>. Idempotent.</summary>
        public void ExitTrailerFilmingState()
        {
            if (controller == null)
            {
                filmingStateEngaged = false;
                return;
            }

            filmingStateEngaged = false;

            controller.DebugSetTrailerFilmingActive(false);
            controller.DebugEndCinematicCamera();
            EffectPresentationController.DebugSuppressFloatingText = false;

            // A death beat leaves a killed player, a death-posed marker and a stretched replay impact
            // delay; hand the session back alive on authored timing. Restore is a no-op while a
            // presentation sequence still plays (a take stopped mid-beat) — in that rare case the lethal
            // beat finishes as normal gameplay and the debug panel's restore button brings the run back.
            if (playerDeathTakeEngaged)
            {
                playerDeathTakeEngaged = false;
                controller.DebugSetMonsterAttackReplayImpactDelayBonus(0f);
                controller.DebugSetPlayerDeathFreezeDurationScale(1f);
                controller.DebugSetCinemachineDebugTextSuppressed(false);
                controller.DebugClearIngameFilmingCameraPose();
                controller.DebugRestoreCombatants();
                controller.DebugResetPlayerVisual();
            }
            // Cleared BEFORE the fog restore on purpose: the reveal-all flip below is what forces the
            // whole-map visibility rescan, so dropping the override first is what puts the real fog back
            // (clearing it after would leave the take's lit cells on screen). No-op for RevealAll takes.
            controller.DebugSetForcedRevealCells(null);
            controller.SetFogDebugVisible(true);
            controller.DebugSetActorMarkerUiVisible(true);
            // Teardown safety for a take stopped mid-beat; a no-op when isolation never engaged.
            controller.DebugSetPresentationHiddenMonsters(null);
            SetAllOverlaysVisible(true);

            // Borrowed day look released here and nowhere else, for the same reason as the hide set: the
            // recorder's auto-stop still captures the frame the routine ends on, so restoring night inside
            // the routine would put one night frame at the tail of a day take.
            if (dayLookEngaged)
            {
                dayLookEngaged = false;
                controller.DebugSetTrailerDayLook(false);
            }

            if (uiVisibility != null)
            {
                uiVisibility.SetLevel(DebugUiVisibilityLevel.Full);
                uiVisibility.SetLevelLabelVisible(true);
            }
        }

        private void SetAllOverlaysVisible(bool visible)
        {
            controller.SetOverlayDebugLayerVisible(CombatOverlayDebugLayer.PlayerMovement, visible);
            controller.SetOverlayDebugLayerVisible(CombatOverlayDebugLayer.PlayerAction, visible);
            controller.SetOverlayDebugLayerVisible(CombatOverlayDebugLayer.MonsterMove, visible);
            controller.SetOverlayDebugLayerVisible(CombatOverlayDebugLayer.MonsterAttack, visible);
            controller.SetOverlayDebugLayerVisible(CombatOverlayDebugLayer.MonsterChase, visible);
            controller.SetOverlayDebugLayerVisible(CombatOverlayDebugLayer.MonsterIntentArrows, visible);
        }

        // --- Take B: cut #4, curved drone flight --------------------------------------------------

        /// <summary>Runs the authored drone path as one continuous take, then hands the view back.</summary>
        public void PlayDroneFlightTake()
        {
            StartTake(DroneFlightRoutine(), "drone flight");
        }

        /// <summary>Runs every showcase entry back to back as one take, then hands the view back.</summary>
        public void PlayMonsterShowcaseTake()
        {
            StartTake(MonsterShowcaseRoutine(), "monster showcase");
        }

        /// <summary>
        /// Cut #7: runs every authored player-death beat back to back in the cinematic style. Per beat the
        /// player is placed, the authored monster spawns beside them and lands a lethal blow through the
        /// real monster-attack replay (wind-up, impact VFX/SFX, hit stop, death slow-motion, death hold).
        /// </summary>
        public void PlayPlayerDeathTake()
        {
            StartTake(PlayerDeathRoutine(-1, DeathTakeStyle.Cinematic, null, 0), "player death");
        }

        /// <summary>Films one authored death beat by index in the cinematic style.</summary>
        public void PlayPlayerDeathTake(int entryIndex)
        {
            StartTake(PlayerDeathRoutine(entryIndex, DeathTakeStyle.Cinematic, null, 0), "player death");
        }

        /// <summary>
        /// Films one beat in a chosen style (<see cref="DeathTakeStyle"/> as int for scripted orchestration),
        /// optionally replacing the beat's attacker with another catalog monster — the region × monster ×
        /// style shooting matrix drives this overload. Cinematic hijacks the authored two-shot; the Ingame
        /// styles film through the real play camera so the built-in death zoom/shake runs, IngameClean with
        /// the HUD hidden, IngameHud hiding nothing at all (the "died mid-play" plate — the damage number
        /// and the game-over screen at the tail are authentic there and stay in).
        /// </summary>
        public void PlayPlayerDeathTake(int entryIndex, int style, string monsterDefinitionOverride = null, int cameraVariantIndex = 0)
        {
            var resolvedStyle = (DeathTakeStyle)Mathf.Clamp(style, 0, 2);
            StartTake(
                PlayerDeathRoutine(entryIndex, resolvedStyle, monsterDefinitionOverride, cameraVariantIndex),
                "player death");
        }

        /// <summary>
        /// Cut #6: orbits the memory stone, stops, then dollies in until the stone fills the frame. Monsters,
        /// UI, overlays and fog are off from the first frame. <paramref name="useDayLook"/> films the day
        /// variant (the night variant is simply the stage's authored look).
        /// </summary>
        public void PlayMemoryStoneTake(bool useDayLook)
        {
            StartTake(MemoryStoneRoutine(useDayLook), "memory stone");
        }

        /// <summary>Same, with the fog treatment picked per take (cut #6 ships one variant per mode).</summary>
        public void PlayMemoryStoneTake(bool useDayLook, MemoryStoneFogMode fogMode)
        {
            memoryStoneFogMode = fogMode;
            PlayMemoryStoneTake(useDayLook);
        }

        /// <summary>Serialized-field defaults, i.e. the night variant.</summary>
        public void PlayMemoryStoneTake()
        {
            PlayMemoryStoneTake(useDayLook: false);
        }

        /// <summary>
        /// Take-only overrides for the framing ladder (candidate stills → user pick → shoot). Any value
        /// &lt;= 0 keeps the serialized authoring, so one call moves one variable.
        /// </summary>
        public void PlayMemoryStoneTake(
            bool useDayLook,
            float orbitDistanceOverride,
            float pitchOverrideDegrees,
            float startAzimuthOverrideDegrees,
            float zoomDistanceOverride,
            float lookAtHeightOverride,
            MemoryStoneFogMode fogMode)
        {
            memoryStoneFogMode = fogMode;
            PlayMemoryStoneTake(
                useDayLook, orbitDistanceOverride, pitchOverrideDegrees, startAzimuthOverrideDegrees,
                zoomDistanceOverride, lookAtHeightOverride);
        }

        /// <summary>Same framing overrides, leaving the fog treatment on its serialized value.</summary>
        public void PlayMemoryStoneTake(
            bool useDayLook,
            float orbitDistanceOverride,
            float pitchOverrideDegrees,
            float startAzimuthOverrideDegrees,
            float zoomDistanceOverride,
            float lookAtHeightOverride)
        {
            if (orbitDistanceOverride > 0f)
            {
                memoryStoneOrbitDistance = orbitDistanceOverride;
            }

            if (pitchOverrideDegrees > 0f)
            {
                memoryStonePitchDegrees = pitchOverrideDegrees;
            }

            // Azimuth 0 is a legitimate authored value, so it is taken verbatim rather than "0 = keep".
            memoryStoneStartAzimuthDegrees = startAzimuthOverrideDegrees;

            if (zoomDistanceOverride > 0f)
            {
                memoryStoneZoomDistance = zoomDistanceOverride;
            }

            if (lookAtHeightOverride >= 0f)
            {
                memoryStoneLookAtHeightOverride = lookAtHeightOverride;
            }

            PlayMemoryStoneTake(useDayLook);
        }

        /// <summary>
        /// Freezes the camera on one candidate framing of the memory stone (no motion) so a still can be
        /// captured for the pick page — same borrowed state as the take, so what the still shows is what
        /// the take films. Ends with <see cref="StopTake"/> like any other take.
        /// </summary>
        public void PoseMemoryStoneCandidate(
            bool useDayLook, float azimuthDegrees, float pitchDegrees, float distance, float lookAtHeight)
        {
            PoseMemoryStoneCandidate(
                useDayLook, azimuthDegrees, pitchDegrees, distance, lookAtHeight, memoryStoneFogMode);
        }

        /// <summary>Same, with the fog treatment picked per still (the take's own knob is left alone).</summary>
        public void PoseMemoryStoneCandidate(
            bool useDayLook, float azimuthDegrees, float pitchDegrees, float distance, float lookAtHeight,
            MemoryStoneFogMode fogMode)
        {
            if (!Available || controller == null || controller.State == null)
            {
                Debug.LogWarning("[TrailerShotRunner] memory stone pose: nothing to film.");
                return;
            }

            if (!TryResolveMemoryStoneFocus(lookAtHeight, out var focus, out var stoneCoord))
            {
                Debug.LogWarning("[TrailerShotRunner] memory stone pose: this map has no objective target.");
                return;
            }

            StopTake();
            HideEveryLiveMonster();
            EnterTrailerFilmingState();
            ApplyTakeLook(useDayLook);

            var previousFogMode = memoryStoneFogMode;
            memoryStoneFogMode = fogMode;
            ApplyMemoryStoneFog(stoneCoord);
            memoryStoneFogMode = previousFogMode;

            var position = OrbitCameraPosition(focus, azimuthDegrees, Mathf.Clamp(pitchDegrees, 5f, 89f), distance);
            controller.DebugBeginCinematicCamera(position, focus, cameraFieldOfView);
            controller.DebugApplyCinematicCameraPose(position, focus);
        }

        /// <summary>Stops the take in flight and restores gameplay state.</summary>
        public void StopTake()
        {
            if (activeTake != null)
            {
                StopCoroutine(activeTake);
                activeTake = null;
            }

            ExitTrailerFilmingState();
        }

        private void StartTake(IEnumerator routine, string label)
        {
            if (!Available)
            {
                Debug.LogWarning($"[TrailerShotRunner] {label}: debug UI is unavailable in this build.");
                return;
            }

            if (controller == null || controller.State == null)
            {
                Debug.LogWarning($"[TrailerShotRunner] {label}: no MapCombatController/CombatState to film.");
                return;
            }

            StopTake();
            activeTake = StartCoroutine(routine);
        }

        private IEnumerator DroneFlightRoutine()
        {
            if (!TryBuildDronePath(out var cameraSamples, out var lookSamples, out var cumulative))
            {
                Debug.LogWarning("[TrailerShotRunner] drone flight: need at least 2 resolvable waypoints.");
                activeTake = null;
                yield break;
            }

            EnterTrailerFilmingState();

            var totalLength = cumulative[cumulative.Length - 1];
            var speed = Mathf.Max(0.01f, droneSpeedUnitsPerSecond);
            var duration = totalLength / speed;
            var lookAhead = Mathf.Max(0f, droneLookAheadDistance);

            var hasFinaleTarget = TryResolveMonsterWorld(
                droneFinaleMonsterDefinitionId, droneFinaleLookAtHeight, out var finaleLookAt);
            var finaleBlend = Mathf.Clamp(droneFinaleLookAtBlendSeconds, 0f, duration);

            var startPosition = cameraSamples[0];
            var startLookAt = SampleAtDistance(lookSamples, cumulative, lookAhead);
            controller.DebugBeginCinematicCamera(startPosition, startLookAt, cameraFieldOfView);

            // One-shot facing bookkeeping: which monsters already turned toward the approaching camera.
            var facedMonsterIds = droneFaceMonstersAtCamera ? new HashSet<string>(StringComparer.Ordinal) : null;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                // Shares the intro's clock so fixed-rate capture yields the authored duration (see
                // MapCombatController.CinematicDeltaTime).
                elapsed += TakeDeltaTime;
                var travelled = Mathf.Min(elapsed * speed, totalLength);

                var cameraPosition = SampleAtDistance(cameraSamples, cumulative, travelled);
                var lookAt = SampleAtDistance(lookSamples, cumulative, travelled + lookAhead);

                if (facedMonsterIds != null)
                {
                    FaceUpcomingMonstersAtCamera(facedMonsterIds, cameraPosition);
                }

                // Last beat: swing the aim off the road and onto the featured monster (콘티 "막 컷에 불가살").
                if (hasFinaleTarget && finaleBlend > 0f)
                {
                    var remaining = duration - elapsed;
                    if (remaining <= finaleBlend)
                    {
                        var blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(1f - (remaining / finaleBlend)));
                        lookAt = Vector3.Lerp(lookAt, finaleLookAt, blend);
                    }
                }

                controller.DebugApplyCinematicCameraPose(cameraPosition, lookAt);
                yield return null;
            }

            var endPosition = cameraSamples[cameraSamples.Length - 1];
            var endLookAt = hasFinaleTarget && finaleBlend > 0f
                ? finaleLookAt
                : SampleAtDistance(lookSamples, cumulative, totalLength + lookAhead);
            controller.DebugApplyCinematicCameraPose(endPosition, endLookAt);

            // The take ends on a pull-out, not a zoom-in: the path stops, the aim stays pinned on the
            // featured monster, and the camera retreats up and away. Retreat direction is horizontally away
            // from the monster; the path's last waypoint hovers almost directly over it, which kills that
            // horizontal component, so the fallback is backward along the final travel direction.
            if (hasFinaleTarget && dronePullOutSeconds > 0.01f)
            {
                var away = endPosition - finaleLookAt;
                away.y = 0f;
                if (away.sqrMagnitude < 1f)
                {
                    var tail = endPosition - SampleAtDistance(cameraSamples, cumulative, totalLength - 2f);
                    tail.y = 0f;
                    away = -tail;
                }

                away = away.sqrMagnitude > 0.0001f ? away.normalized : Vector3.back;
                var pullTarget = endPosition
                    + Vector3.up * Mathf.Max(0f, dronePullOutHeightGain)
                    + away * Mathf.Max(0f, dronePullOutBackDistance);

                var pullElapsed = 0f;
                while (pullElapsed < dronePullOutSeconds)
                {
                    pullElapsed += TakeDeltaTime;
                    var t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(pullElapsed / dronePullOutSeconds));
                    controller.DebugApplyCinematicCameraPose(Vector3.Lerp(endPosition, pullTarget, t), finaleLookAt);
                    yield return null;
                }

                controller.DebugApplyCinematicCameraPose(pullTarget, finaleLookAt);
            }

            activeTake = null;
        }

        // 콘티 수정: 경로변 몬스터가 전부 등을 보이면 그림이 죽는다 — 카메라가 다가오면 그 시점의
        // 카메라 위치를 향해 한 번만 돌려서, 처음 잡히는 순간 앞모습이 보이게 한다. 그 뒤로는
        // 고정(계속 추적 회전하는 것은 부자연스럽다는 사용자 판정).
        private void FaceUpcomingMonstersAtCamera(HashSet<string> alreadyFaced, Vector3 cameraPosition)
        {
            var threshold = Mathf.Max(1f, droneFaceDistance);
            var thresholdSqr = threshold * threshold;
            foreach (var monster in controller.State.Monsters)
            {
                if (monster.IsDead || alreadyFaced.Contains(monster.Id))
                {
                    continue;
                }

                if (!controller.TryGetTileWorldPosition(monster.Coord, out var world) ||
                    (world - cameraPosition).sqrMagnitude > thresholdSqr)
                {
                    continue;
                }

                alreadyFaced.Add(monster.Id);
                controller.DebugFaceMonsterAt(monster.Id, world, cameraPosition);
            }
        }

        // --- Take C: cut #5, monster showcase -----------------------------------------------------

        private IEnumerator MonsterShowcaseRoutine()
        {
            // Every spawn happens before the take starts, never inside it: the sandbox spawn wrapper is a
            // no-op while a presentation sequence plays, and a marker created mid-shot would swallow the
            // attack trigger fired on its creation frame (the intro hit exactly that bug).
            var beats = new List<ShowcaseBeat>();
            foreach (var entry in showcaseEntries)
            {
                if (TryPrepareShowcaseBeat(entry, out var beat))
                {
                    beats.Add(beat);
                }
            }

            if (beats.Count == 0)
            {
                Debug.LogWarning("[TrailerShotRunner] showcase: no entry resolved to a monster on the map.");
                activeTake = null;
                yield break;
            }

            EnterTrailerFilmingState();
            if (showcaseIsolateSubject)
            {
                ApplyShowcaseIsolation(beats[0].monsterId);
            }

            // Let the spawns' markers exist for a frame before the first cut frames them.
            yield return null;

            var sweep = showcaseSweepDirection >= 0f ? 1f : -1f;
            for (var i = 0; i < beats.Count; i++)
            {
                if (showcaseIsolateSubject)
                {
                    ApplyShowcaseIsolation(beats[i].monsterId);
                }

                yield return PlayShowcaseBeat(beats[i], sweep);
            }

            // Hold the final framing so the last beat's attack motion plays out before the take ends.
            var tailElapsed = 0f;
            while (tailElapsed < showcaseTailHoldSeconds)
            {
                tailElapsed += TakeDeltaTime;
                yield return null;
            }

            // Isolation is deliberately NOT cleared here: the recorder's auto-stop captures the frame on
            // which activeTake goes null, so clearing first would put the hidden monsters back into the
            // video's last frame (that exact leak shipped in the first isolated takes). StopTake /
            // ExitTrailerFilmingState restores the markers after recording has stopped.
            activeTake = null;
        }

        // Presentation-only: every live monster except the featured one leaves the frame for this beat
        // (콘티: 각기 다른 밤 배경에 피사체만). The sim keeps running; markers come back on teardown.
        private void ApplyShowcaseIsolation(string featuredMonsterId)
        {
            var hidden = new List<string>();
            foreach (var monster in controller.State.Monsters)
            {
                if (!monster.IsDead && !string.Equals(monster.Id, featuredMonsterId, StringComparison.Ordinal))
                {
                    hidden.Add(monster.Id);
                }
            }

            controller.DebugSetPresentationHiddenMonsters(hidden);
        }

        private readonly struct ShowcaseBeat
        {
            public readonly string monsterId;
            public readonly HexCoord coord;
            public readonly Vector3 tileWorld;
            public readonly float azimuthDegrees;
            public readonly string attackTrigger;
            public readonly float distanceMultiplier;
            public readonly float lookAtHeightOverride;

            public ShowcaseBeat(
                string monsterId, HexCoord coord, Vector3 tileWorld, float azimuthDegrees,
                string attackTrigger, float distanceMultiplier, float lookAtHeightOverride)
            {
                this.monsterId = monsterId;
                this.coord = coord;
                this.tileWorld = tileWorld;
                this.azimuthDegrees = azimuthDegrees;
                this.attackTrigger = attackTrigger;
                this.distanceMultiplier = distanceMultiplier;
                this.lookAtHeightOverride = lookAtHeightOverride;
            }
        }

        private bool TryPrepareShowcaseBeat(TrailerShowcaseEntry entry, out ShowcaseBeat beat)
        {
            beat = default;
            if (entry == null || string.IsNullOrWhiteSpace(entry.monsterDefinitionId) || controller?.State == null)
            {
                return false;
            }

            var monsterId = string.Empty;
            var coord = new HexCoord(entry.column, entry.row);

            if (entry.spawnAtCoord)
            {
                if (!controller.DebugSandboxSpawnMonsterAtCoord(entry.monsterDefinitionId, coord, 0, out monsterId))
                {
                    Debug.LogWarning(
                        $"[TrailerShotRunner] showcase: could not spawn {entry.monsterDefinitionId} at {coord} " +
                        $"({controller.LastInputMessage}); falling back to a monster already on the map.");
                    monsterId = string.Empty;
                }
            }

            if (string.IsNullOrEmpty(monsterId))
            {
                if (!TryFindMonster(entry.monsterDefinitionId, out var existing))
                {
                    return false;
                }

                monsterId = existing.Id;
                coord = existing.Coord;
            }

            if (!controller.TryGetTileWorldPosition(coord, out var tileWorld))
            {
                return false;
            }

            beat = new ShowcaseBeat(
                monsterId, coord, tileWorld, entry.cameraAzimuthDegrees, entry.attackAnimationTrigger ?? string.Empty,
                entry.cameraDistanceMultiplier, entry.lookAtHeightOverride);
            return true;
        }

        private IEnumerator PlayShowcaseBeat(ShowcaseBeat beat, float sweepDirection)
        {
            // Tall monsters (불가살) crop at the shared framing; the entry widens its own beat instead.
            var lookAtHeight = beat.lookAtHeightOverride >= 0f ? beat.lookAtHeightOverride : showcaseLookAtHeight;
            var focus = beat.tileWorld + Vector3.up * Mathf.Max(0f, lookAtHeight);
            var distance = Mathf.Max(1.5f, showcaseCameraDistance * Mathf.Max(0.1f, beat.distanceMultiplier));
            var pitch = Mathf.Clamp(showcaseCameraPitchDegrees, 5f, 89f);
            var half = Mathf.Max(0f, showcaseArcHalfDegrees);

            var fromPos = OrbitCameraPosition(focus, beat.azimuthDegrees - sweepDirection * half, pitch, distance);
            var toPos = OrbitCameraPosition(focus, beat.azimuthDegrees + sweepDirection * half, pitch, distance);

            // Hard cut into the beat (trailer grammar — same as the intro's shots).
            controller.DebugBeginCinematicCamera(fromPos, focus, cameraFieldOfView);
            controller.DebugApplyCinematicCameraPose(fromPos, focus);

            if (showcasePlaySpawnVfx)
            {
                controller.DebugPlayMonsterSpawnVfx(beat.coord, beat.tileWorld);
            }

            var duration = Mathf.Max(0.01f, showcaseShotSeconds);
            var attackDelay = showcaseAttackDelaySeconds > 0f
                ? Mathf.Clamp(showcaseAttackDelaySeconds, 0f, duration)
                : -1f;
            var attacked = attackDelay < 0f;
            var elapsed = 0f;
            var firstFrame = true;

            while (elapsed < duration)
            {
                elapsed += TakeDeltaTime;
                // Half-linear/half-smoothstep, matching EaseCinematicShot's default: the ends soften but
                // the middle keeps the near-constant speed a trailer arc wants.
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = Mathf.Lerp(t, Mathf.SmoothStep(0f, 1f, t), 0.5f);
                var cameraPosition = Vector3.Lerp(fromPos, toPos, eased);

                // Hold the monster turned to camera for the whole beat, not just on the opening frame.
                controller.DebugFaceMonsterAt(beat.monsterId, beat.tileWorld, cameraPosition);

                // Never on the beat's first frame: a trigger set the frame a marker's Animator binds is
                // dropped silently (the intro's spawn beats carry the same guard).
                if (!attacked && !firstFrame && elapsed >= attackDelay)
                {
                    attacked = true;
                    // Empty falls through to the presenter's default-resolution overload (Attack1..5 probe).
                    controller.DebugTriggerMonsterAttack(beat.monsterId, beat.attackTrigger);
                }

                controller.DebugApplyCinematicCameraPose(cameraPosition, focus);
                firstFrame = false;
                yield return null;
            }

            controller.DebugApplyCinematicCameraPose(toPos, focus);
        }

        // --- Take D: cut #6, memory stone --------------------------------------------------------

        private IEnumerator MemoryStoneRoutine(bool useDayLook)
        {
            if (!TryResolveMemoryStoneFocus(memoryStoneLookAtHeightOverride, out var focus, out var stoneCoord))
            {
                Debug.LogWarning("[TrailerShotRunner] memory stone: this map has no resolvable objective target.");
                activeTake = null;
                yield break;
            }

            // Hidden BEFORE the filming state engages, so not even the opening frame carries a monster
            // marker (콘티: 시작부터 몬스터·UI·오버레이 전부 OFF). Cleared by StopTake, never in here.
            HideEveryLiveMonster();
            EnterTrailerFilmingState();
            ApplyTakeLook(useDayLook);
            ApplyMemoryStoneFog(stoneCoord);

            var pitch = Mathf.Clamp(memoryStonePitchDegrees, 5f, 89f);
            var orbitDistance = Mathf.Max(1.5f, memoryStoneOrbitDistance);
            var zoomDistance = Mathf.Clamp(memoryStoneZoomDistance, 1.5f, orbitDistance);
            var startAzimuth = memoryStoneStartAzimuthDegrees;
            var endAzimuth = startAzimuth + memoryStoneOrbitDegrees;

            var startPosition = OrbitCameraPosition(focus, startAzimuth, pitch, orbitDistance);
            controller.DebugBeginCinematicCamera(startPosition, focus, cameraFieldOfView);
            controller.DebugApplyCinematicCameraPose(startPosition, focus);

            // 1. Orbit. Eased at both ends so the move starts and lands softly — the stop is part of the
            // shot's grammar (돌다가 멈춘다), not a cut.
            var orbitDuration = Mathf.Max(0.01f, memoryStoneOrbitSeconds);
            var elapsed = 0f;
            while (elapsed < orbitDuration)
            {
                elapsed += TakeDeltaTime;
                var eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / orbitDuration));
                var azimuth = Mathf.Lerp(startAzimuth, endAzimuth, eased);
                controller.DebugApplyCinematicCameraPose(
                    OrbitCameraPosition(focus, azimuth, pitch, orbitDistance), focus);
                yield return null;
            }

            var settledPosition = OrbitCameraPosition(focus, endAzimuth, pitch, orbitDistance);
            controller.DebugApplyCinematicCameraPose(settledPosition, focus);

            // 2. Hold still.
            yield return HoldTakeFrames(memoryStoneSettleSeconds);

            // 3. Physical dolly in — the camera closes on the stone along the same orbit ray. Not an FOV
            // zoom: the FOV is fixed at Begin, and pulling the distance keeps the perspective consistent
            // with every other trailer cut.
            var zoomDuration = Mathf.Max(0.01f, memoryStoneZoomSeconds);
            elapsed = 0f;
            while (elapsed < zoomDuration)
            {
                elapsed += TakeDeltaTime;
                var eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / zoomDuration));
                var distance = Mathf.Lerp(orbitDistance, zoomDistance, eased);
                controller.DebugApplyCinematicCameraPose(
                    OrbitCameraPosition(focus, endAzimuth, pitch, distance), focus);
                yield return null;
            }

            controller.DebugApplyCinematicCameraPose(
                OrbitCameraPosition(focus, endAzimuth, pitch, zoomDistance), focus);

            yield return HoldTakeFrames(memoryStoneTailHoldSeconds);

            activeTake = null;
        }

        private IEnumerator HoldTakeFrames(float seconds)
        {
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += TakeDeltaTime;
                yield return null;
            }
        }

        // The objective target is the memory stone. The aim defaults to the stone's own vertical centre
        // (measured visual top = 3.32u above the tile), NOT the intro's stageIntroLookAtHeightOffset of 5:
        // that lift is sized for far taller landmarks and aims clean over this stone's head — the first
        // framing probe put the stone off the bottom edge. The controller's lift stays reachable via a
        // negative override for takes that deliberately want the intro's framing.
        private bool TryResolveMemoryStoneFocus(float lookAtHeightOverride, out Vector3 focus, out HexCoord stoneCoord)
        {
            focus = Vector3.zero;
            stoneCoord = default;
            var coord = controller.State?.ObjectiveTargetCoord;
            if (!coord.HasValue || !controller.TryGetTileWorldPosition(coord.Value, out var world))
            {
                return false;
            }

            stoneCoord = coord.Value;
            var lift = lookAtHeightOverride >= 0f ? lookAtHeightOverride : controller.StageIntroLookAtHeightOffset;
            focus = world + Vector3.up * Mathf.Max(0f, lift);
            return true;
        }

        // The filming state turns the fog off wholesale (that is what every other take wants). The fog
        // variants put it back — and for the two partial modes hand their lit cells to the intro's per-cell
        // override first, so the handoff never flashes a fully-revealed frame. Nothing is explored for
        // real: the override is presentation-only and ExitTrailerFilmingState drops it.
        private void ApplyMemoryStoneFog(HexCoord stoneCoord)
        {
            if (memoryStoneFogMode == MemoryStoneFogMode.RevealAll)
            {
                return;
            }

            if (memoryStoneFogMode != MemoryStoneFogMode.Covered)
            {
                var radius = memoryStoneFogMode == MemoryStoneFogMode.Surroundings
                    ? Mathf.Max(0, memoryStoneFogRevealRadius)
                    : 0;
                controller.DebugSetForcedRevealCells(HexArea.CellsWithin(stoneCoord, radius));
            }

            controller.SetFogDebugVisible(true);
        }

        private void HideEveryLiveMonster()
        {
            var hidden = new List<string>();
            foreach (var monster in controller.State.Monsters)
            {
                if (!monster.IsDead)
                {
                    hidden.Add(monster.Id);
                }
            }

            controller.DebugSetPresentationHiddenMonsters(hidden);
        }

        private void ApplyTakeLook(bool useDayLook)
        {
            if (!useDayLook || dayLookEngaged)
            {
                return;
            }

            dayLookEngaged = true;
            controller.DebugSetTrailerDayLook(true);
        }

        // --- Take E: cut #7, player death ---------------------------------------------------------

        private IEnumerator PlayerDeathRoutine(int entryIndex, DeathTakeStyle style, string monsterDefinitionOverride, int cameraVariantIndex)
        {
            var entries = new List<TrailerPlayerDeathEntry>();
            if (entryIndex < 0)
            {
                entries.AddRange(playerDeathEntries);
            }
            else if (entryIndex < playerDeathEntries.Count)
            {
                entries.Add(playerDeathEntries[entryIndex]);
            }

            entries.RemoveAll(entry => entry == null || string.IsNullOrWhiteSpace(entry.monsterDefinitionId));
            if (entries.Count == 0)
            {
                Debug.LogWarning("[TrailerShotRunner] player death: no beat to film (check playerDeathEntries / index).");
                activeTake = null;
                yield break;
            }

            // IngameHud deliberately skips the filming state: the HUD, damage numbers, danger flash and
            // the game-over screen ARE the shot ("실플레이 중 사망" 플레이트). Only the two pieces of pure
            // dev chrome go: the IMGUI debug panel (its own static gate) — game UI is untouched.
            // Teardown still runs through ExitTrailerFilmingState via the engaged flag below.
            if (style != DeathTakeStyle.IngameHud)
            {
                EnterTrailerFilmingState();
            }
            else
            {
                DebugUiVisibilityState.ImmediateModeDebugUiHidden = true;
            }

            // Un-hijacked styles keep the CinemachineBrain live, whose debug text is OnGUI chrome burned
            // into frames (the cinematic hijack suppresses it inside Begin/End on its own).
            if (style != DeathTakeStyle.Cinematic)
            {
                controller.DebugSetCinemachineDebugTextSuppressed(true);
            }

            playerDeathTakeEngaged = true;
            controller.DebugSetMonsterAttackReplayImpactDelayBonus(deathImpactDelayBonusSeconds);
            controller.DebugSetPlayerDeathFreezeDurationScale(deathFreezeDurationScale);

            foreach (var entry in entries)
            {
                yield return PlayPlayerDeathBeat(entry, style, monsterDefinitionOverride, cameraVariantIndex);
            }

            // Hold the final framing (the victim stays down). Teardown lives in StopTake, after the
            // recorder's auto-stop has captured this exact frame — reviving here would put a standing
            // player into the video's last frame.
            yield return HoldTakeFrames(deathTailHoldSeconds);
            activeTake = null;
        }

        private IEnumerator PlayPlayerDeathBeat(TrailerPlayerDeathEntry entry, DeathTakeStyle style, string monsterDefinitionOverride, int cameraVariantIndex)
        {
            // Never touch the sim while a sequence is in flight — every debug seam below no-ops then.
            while (controller.IsSequencePlaying)
            {
                yield return null;
            }

            var attackerDefinitionId = string.IsNullOrWhiteSpace(monsterDefinitionOverride)
                ? entry.monsterDefinitionId
                : monsterDefinitionOverride;

            // A previous lethal beat left the player dead: full HP + fresh phase, marker back from the
            // death pose. Prep through the camera cut is yield-free on purpose — the relocation and the
            // hard cut into the new framing land on the same rendered frame, so no teleport/revive frame
            // leaks into the video.
            controller.DebugRestoreCombatants();
            controller.DebugResetPlayerVisual();

            if (entry.movePlayerToCoord &&
                !controller.DebugTeleportPlayerTo(new HexCoord(entry.playerColumn, entry.playerRow)))
            {
                Debug.LogWarning(
                    $"[TrailerShotRunner] player death: teleport failed ({controller.LastInputMessage}); skipping beat.");
                yield break;
            }

            // The HUD plate needs cards in the dock to read as mid-play — the programmatic boot never
            // deals an opening hand. Re-rolled EVERY beat (count 5~9, ids from the run's real deck) so
            // consecutive takes read as different moments of play, not the same frozen hand.
            if (style == DeathTakeStyle.IngameHud)
            {
                RandomizeHudHand();
            }

            var playerCoord = controller.State.PlayerCoord;
            var monsterCoord = new HexCoord(entry.monsterColumn, entry.monsterRow);
            if (!controller.DebugSandboxSpawnMonsterAtCoord(attackerDefinitionId, monsterCoord, 0, out var monsterId))
            {
                // Retake idempotency: the usual spawn failure is a PREVIOUS take's monster still standing
                // on the authored tile (takes deliberately never despawn — cut #5's trap). Reuse the
                // occupant; only a genuine mismatch falls back to a map monster somewhere else, which
                // almost certainly ruins the framing — hence the loud warning. The shooting matrix swaps
                // attackers between takes, so orchestration must despawn sandbox monsters between takes
                // (DebugSandboxRemoveSpawnedMonsters) or the occupant check fails on the definition.
                if (TryFindLivingMonsterAt(monsterCoord, attackerDefinitionId, out var occupant))
                {
                    monsterId = occupant.Id;
                }
                else
                {
                    Debug.LogWarning(
                        $"[TrailerShotRunner] player death: could not spawn {attackerDefinitionId} at {monsterCoord} " +
                        $"({controller.LastInputMessage}); falling back to one already on the map.");
                    if (!TryFindMonster(attackerDefinitionId, out var existing))
                    {
                        Debug.LogWarning(
                            $"[TrailerShotRunner] player death: no {attackerDefinitionId} available; skipping beat.");
                        yield break;
                    }

                    monsterId = existing.Id;
                    monsterCoord = existing.Coord;
                }
            }

            WarnIfSubjectIsNotNearestAttacker(monsterId, monsterCoord, playerCoord);

            // Dressing extras (다수 몬스터 변주) are best-effort: a failed spot logs and the beat carries
            // on — they never gate the kill. They must sit at distance >= 2 or they steal it (warned above
            // on the next run through).
            var keepVisibleIds = new List<string> { monsterId };
            foreach (var extra in entry.extraMonsters ?? new List<TrailerDeathExtraMonster>())
            {
                if (extra == null || string.IsNullOrWhiteSpace(extra.definitionId))
                {
                    continue;
                }

                var extraCoord = new HexCoord(extra.column, extra.row);
                if (controller.DebugSandboxSpawnMonsterAtCoord(extra.definitionId, extraCoord, 0, out var extraId))
                {
                    keepVisibleIds.Add(extraId);
                }
                else if (TryFindLivingMonsterAt(extraCoord, extra.definitionId, out var extraOccupant))
                {
                    keepVisibleIds.Add(extraOccupant.Id);
                }
                else
                {
                    Debug.LogWarning(
                        $"[TrailerShotRunner] player death: extra {extra.definitionId} failed at {extraCoord} " +
                        $"({controller.LastInputMessage}); continuing without it.");
                }
            }

            if (deathIsolateSubject)
            {
                ApplyDeathIsolation(keepVisibleIds);
            }

            if (!controller.TryGetTileWorldPosition(playerCoord, out var playerWorld) ||
                !controller.TryGetTileWorldPosition(monsterCoord, out var monsterWorld))
            {
                Debug.LogWarning("[TrailerShotRunner] player death: beat coordinates do not resolve to tiles; skipping beat.");
                yield break;
            }

            // The victim must face their killer for the fall to read; the replay re-faces the monster on
            // its own, this covers the settle frames before the swing. Extras get turned toward the victim
            // by the same seam so the surround reads as a closing-in pack.
            controller.DebugFacePlayerTowards(monsterCoord);
            controller.DebugFaceMonsterAt(monsterId, monsterWorld, playerWorld);
            for (var i = 1; i < keepVisibleIds.Count; i++)
            {
                if (TryFindMonsterById(keepVisibleIds[i], out var extraState) &&
                    controller.TryGetTileWorldPosition(extraState.Coord, out var extraWorld))
                {
                    controller.DebugFaceMonsterAt(keepVisibleIds[i], extraWorld, playerWorld);
                }
            }

            ApplyDeathFog(style, playerCoord);

            var cinematic = style == DeathTakeStyle.Cinematic;
            var lookAtHeight = entry.lookAtHeightOverride >= 0f ? entry.lookAtHeightOverride : deathLookAtHeight;
            var focus = Vector3.Lerp(
                playerWorld + Vector3.up * Mathf.Max(0f, lookAtHeight),
                monsterWorld + Vector3.up * Mathf.Max(0f, lookAtHeight),
                deathLookAtBlendToMonster);
            var pitch = Mathf.Clamp(
                entry.pitchOverrideDegrees > 0f ? entry.pitchOverrideDegrees : deathCameraPitchDegrees, 5f, 89f);
            var distance = Mathf.Max(1.5f, deathCameraDistance * Mathf.Max(0.1f, entry.cameraDistanceMultiplier));
            var azimuth = entry.cameraAzimuthDegrees;

            if (cinematic)
            {
                // Hard cut into the beat (trailer grammar), then let the framing settle before the swing.
                // The settle is clamped above zero so the attack replay never fires on the spawned
                // marker's Animator-bind frame (a trigger set that frame is dropped silently — intro guard).
                controller.DebugBeginCinematicCamera(OrbitCameraPosition(focus, azimuth, pitch, distance), focus, cameraFieldOfView);
                controller.DebugApplyCinematicCameraPose(OrbitCameraPosition(focus, azimuth, pitch, distance), focus);
            }
            else
            {
                // Ingame styles film through the real follow camera, posed the way a player might have
                // left it: an authored variant (기본 뷰 / 돌려서 당긴 뷰) plus per-take yaw/zoom jitter,
                // then snapped onto the relocated player. The built-in death zoom and shake are the
                // shot's grammar here.
                var variant = ResolveIngameCameraVariant(cameraVariantIndex);
                var yaw = variant.orbitYawDegrees
                    + UnityEngine.Random.Range(-ingameCameraYawJitterDegrees, ingameCameraYawJitterDegrees);
                var zoomOffset = UnityEngine.Random.Range(-ingameCameraZoomJitter, ingameCameraZoomJitter);
                controller.DebugSetIngameFilmingCameraPose(yaw, variant.zoomDistance, zoomOffset);
                controller.DebugRecenterGameplayCameraOnPlayer();
            }

            var settle = Mathf.Max(0.05f, deathPreAttackDelaySeconds);
            var elapsed = 0f;
            while (elapsed < settle)
            {
                elapsed += TakeDeltaTime;
                if (cinematic)
                {
                    azimuth += deathArcDegreesPerSecond * TakeDeltaTime;
                    controller.DebugApplyCinematicCameraPose(OrbitCameraPosition(focus, azimuth, pitch, distance), focus);
                }

                yield return null;
            }

            controller.DebugReplayMonsterAttack(lethal: true);
            if (!controller.IsSequencePlaying)
            {
                Debug.LogWarning(
                    $"[TrailerShotRunner] player death: lethal replay did not start ({controller.LastInputMessage}).");
                yield break;
            }

            // Ride the whole authored beat — wind-up, impact, hit stop, slow motion, death hold — with the
            // arc drifting on the take's own clock, so timeScale dips never stall the camera.
            while (controller.IsSequencePlaying)
            {
                if (cinematic)
                {
                    azimuth += deathArcDegreesPerSecond * TakeDeltaTime;
                    controller.DebugApplyCinematicCameraPose(OrbitCameraPosition(focus, azimuth, pitch, distance), focus);
                }

                yield return null;
            }
        }

        // 매 비트 손패를 다시 굴린다: 장수는 5~9 무작위, 카드는 이 런의 실제 덱(ActiveCardCatalogIds)
        // 풀에서 중복 허용으로 뽑는다(플레이 중 어떤 순간이든 있을 법한 손). 주입은 DeckType을 따라
        // 이동/액션 독으로 각자 들어간다.
        private void RandomizeHudHand()
        {
            var state = controller.State;
            state.MovementDeck.DiscardHand();
            state.ActionDeck.DiscardHand();

            var pool = new List<string>(state.ActiveCardCatalogIds);
            if (pool.Count == 0 && deathHudHandCardIds != null)
            {
                pool.AddRange(deathHudHandCardIds);
            }

            if (pool.Count == 0)
            {
                return;
            }

            var min = Mathf.Min(deathHudHandMinCards, deathHudHandMaxCards);
            var max = Mathf.Max(deathHudHandMinCards, deathHudHandMaxCards);
            var count = UnityEngine.Random.Range(min, max + 1);
            for (var i = 0; i < count; i++)
            {
                state.DebugInjectCardIntoHand(pool[UnityEngine.Random.Range(0, pool.Count)]);
            }
        }

        private TrailerIngameCameraVariant ResolveIngameCameraVariant(int index)
        {
            if (ingameCameraVariants == null || ingameCameraVariants.Count == 0)
            {
                return new TrailerIngameCameraVariant();
            }

            return ingameCameraVariants[Mathf.Clamp(index, 0, ingameCameraVariants.Count - 1)]
                ?? new TrailerIngameCameraVariant();
        }

        // Presentation-only fog dressing per style. Cinematic beats own their look (사용자 확정 기본 =
        // 플레이어 둘레 3칸); the ingame styles show the real explored state — the beat's teleport already
        // refreshed true vision around the player, which is exactly the "실플레이 중" picture.
        private void ApplyDeathFog(DeathTakeStyle style, HexCoord playerCoord)
        {
            if (style != DeathTakeStyle.Cinematic)
            {
                controller.SetFogDebugVisible(true);
                return;
            }

            if (deathFogMode == DeathFogMode.RevealAll)
            {
                return;
            }

            if (deathFogMode == DeathFogMode.PlayerSurroundings)
            {
                // Same order contract as cut #6: forced cells BEFORE the reveal-all flip, or the handoff
                // flashes a fully-revealed frame.
                controller.DebugSetForcedRevealCells(
                    HexArea.CellsWithin(playerCoord, Mathf.Max(0, deathFogRevealRadius)));
            }

            controller.SetFogDebugVisible(true);
        }

        // Presentation-only: every live monster except the beat's cast leaves the frame. The sim keeps
        // running; markers come back on teardown.
        private void ApplyDeathIsolation(List<string> keepVisibleIds)
        {
            var hidden = new List<string>();
            foreach (var monster in controller.State.Monsters)
            {
                if (!monster.IsDead && !keepVisibleIds.Contains(monster.Id))
                {
                    hidden.Add(monster.Id);
                }
            }

            controller.DebugSetPresentationHiddenMonsters(hidden);
        }

        private bool TryFindMonsterById(string monsterId, out MonsterRuntimeState found)
        {
            found = default;
            foreach (var monster in controller.State.Monsters)
            {
                if (!monster.IsDead && string.Equals(monster.Id, monsterId, StringComparison.Ordinal))
                {
                    found = monster;
                    return true;
                }
            }

            return false;
        }

        private bool TryFindLivingMonsterAt(HexCoord coord, string definitionId, out MonsterRuntimeState found)
        {
            found = default;
            foreach (var monster in controller.State.Monsters)
            {
                if (!monster.IsDead && monster.Coord == coord &&
                    string.Equals(monster.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))
                {
                    found = monster;
                    return true;
                }
            }

            return false;
        }

        // The lethal replay targets the monster NEAREST the player; a map monster parked as close as the
        // authored attacker can silently steal the kill — and subject isolation would then hide the actual
        // killer. This warning is the breadcrumb for that exact miss.
        private void WarnIfSubjectIsNotNearestAttacker(string subjectId, HexCoord subjectCoord, HexCoord playerCoord)
        {
            var subjectDistance = playerCoord.DistanceTo(subjectCoord);
            foreach (var monster in controller.State.Monsters)
            {
                if (monster.IsDead || string.Equals(monster.Id, subjectId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (playerCoord.DistanceTo(monster.Coord) <= subjectDistance)
                {
                    Debug.LogWarning(
                        $"[TrailerShotRunner] player death: {monster.Id} ({monster.DefinitionId}) is at least as close " +
                        "to the player as the authored attacker — the lethal replay may pick it instead. " +
                        "Author the beat farther from map monsters.");
                    return;
                }
            }
        }

        // --- Path maths ---------------------------------------------------------------------------

        // Same convention as CombatCameraController.IntroOrbitCameraPosition (that one is internal to the
        // Combat assembly, so the two lines are repeated rather than reached across the assembly boundary).
        private static Vector3 OrbitCameraPosition(Vector3 focus, float azimuthDegrees, float pitchDegrees, float distance)
        {
            var rotation = Quaternion.Euler(pitchDegrees, azimuthDegrees, 0f);
            return focus + rotation * (Vector3.back * Mathf.Max(0.1f, distance));
        }

        /// <summary>
        /// Turns the authored waypoints into two densely-sampled polylines that share one parameterization
        /// — the camera path (waypoint tile + its authored height) and the aim path (tile + look height) —
        /// plus the cumulative arc length along the camera path.
        /// </summary>
        /// <remarks>
        /// Arc length is what makes the flight read as constant speed: sampling the spline in its natural
        /// parameter would speed up through the straight stretches and crawl around the tight corners,
        /// because Catmull-Rom's parameter is not proportional to distance.
        /// </remarks>
        private bool TryBuildDronePath(out Vector3[] cameraSamples, out Vector3[] lookSamples, out float[] cumulative)
        {
            cameraSamples = Array.Empty<Vector3>();
            lookSamples = Array.Empty<Vector3>();
            cumulative = Array.Empty<float>();

            var cameraControls = new List<Vector3>();
            var lookControls = new List<Vector3>();
            foreach (var waypoint in droneWaypoints)
            {
                if (waypoint == null ||
                    !controller.TryGetTileWorldPosition(new HexCoord(waypoint.column, waypoint.row), out var world))
                {
                    continue;
                }

                cameraControls.Add(world + Vector3.up * waypoint.cameraHeight);
                lookControls.Add(world + Vector3.up * Mathf.Max(0f, droneLookAtHeight));
            }

            if (cameraControls.Count < 2)
            {
                return false;
            }

            const int samplesPerSegment = 24;
            var segments = cameraControls.Count - 1;
            var sampleCount = segments * samplesPerSegment + 1;

            cameraSamples = new Vector3[sampleCount];
            lookSamples = new Vector3[sampleCount];
            cumulative = new float[sampleCount];

            for (var i = 0; i < sampleCount; i++)
            {
                var segment = Mathf.Min(i / samplesPerSegment, segments - 1);
                var localT = (i - (segment * samplesPerSegment)) / (float)samplesPerSegment;
                cameraSamples[i] = CatmullRom(cameraControls, segment, localT);
                lookSamples[i] = CatmullRom(lookControls, segment, localT);
                cumulative[i] = i == 0
                    ? 0f
                    : cumulative[i - 1] + Vector3.Distance(cameraSamples[i - 1], cameraSamples[i]);
            }

            return cumulative[sampleCount - 1] > 0.0001f;
        }

        // Uniform Catmull-Rom through controls[segment] -> controls[segment + 1]. The endpoints are
        // duplicated (clamped) so the curve starts and ends exactly on the authored first/last waypoints
        // instead of overshooting them.
        private static Vector3 CatmullRom(List<Vector3> controls, int segment, float t)
        {
            var p0 = controls[Mathf.Max(segment - 1, 0)];
            var p1 = controls[segment];
            var p2 = controls[Mathf.Min(segment + 1, controls.Count - 1)];
            var p3 = controls[Mathf.Min(segment + 2, controls.Count - 1)];

            var t2 = t * t;
            var t3 = t2 * t;
            return 0.5f * (
                (2f * p1) +
                ((-p0 + p2) * t) +
                (((2f * p0) - (5f * p1) + (4f * p2) - p3) * t2) +
                ((-p0 + (3f * p1) - (3f * p2) + p3) * t3));
        }

        // Position at a given arc length along the sampled path, clamped at both ends. Linear scan from a
        // binary search: the tables are a few hundred entries, so this is cheap enough per frame.
        private static Vector3 SampleAtDistance(Vector3[] samples, float[] cumulative, float distance)
        {
            var last = samples.Length - 1;
            if (distance <= 0f)
            {
                return samples[0];
            }

            if (distance >= cumulative[last])
            {
                return samples[last];
            }

            var low = 0;
            var high = last;
            while (low + 1 < high)
            {
                var mid = (low + high) / 2;
                if (cumulative[mid] <= distance)
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            var span = cumulative[high] - cumulative[low];
            var t = span > 0.0001f ? (distance - cumulative[low]) / span : 0f;
            return Vector3.Lerp(samples[low], samples[high], t);
        }

        // --- Monster lookup -----------------------------------------------------------------------

        private bool TryResolveMonsterWorld(string definitionId, float lookAtHeight, out Vector3 world)
        {
            world = Vector3.zero;
            if (string.IsNullOrWhiteSpace(definitionId) ||
                !TryFindMonster(definitionId, out var monster) ||
                !controller.TryGetTileWorldPosition(monster.Coord, out var tileWorld))
            {
                return false;
            }

            world = tileWorld + Vector3.up * Mathf.Max(0f, lookAtHeight);
            return true;
        }

        private bool TryFindMonster(string definitionId, out MonsterRuntimeState found)
        {
            found = default;
            if (controller?.State == null)
            {
                return false;
            }

            foreach (var monster in controller.State.Monsters)
            {
                if (!monster.IsDead &&
                    string.Equals(monster.DefinitionId, definitionId, StringComparison.OrdinalIgnoreCase))
                {
                    found = monster;
                    return true;
                }
            }

            return false;
        }
    }
}
