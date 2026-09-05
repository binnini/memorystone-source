// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Timeline;
using SeoulPlayup.Combat.Unity.Tutorial;
using SeoulPlayup.Combat.Unity.Presentation;
using SeoulPlayup.Codex;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using static SeoulPlayup.Combat.Unity.CombatCameraController;
using Cinemachine;
using TMPro;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public enum CombatPresentationPhase
    {
        None,
        PlayerMoving,
        PlayerAttacking,
        MonstersMoving,
        MonsterAttacking,
        Completing
    }

    public sealed partial class MapCombatController : MonoBehaviour, ICombatPresentationSink, ICombatAudioHost, ICombatCameraHost, ICombatCardHudHost
    {
        private const string LegacyM1MoveControlsName = "M1 Move Controls";
        private const string CardRewardOverlayRootName = "Card Reward Overlay Root";
        private const string CardRewardOverlayResourcesPath = "UI/Prototype/Card Reward Overlay Root";
        private const string PlayerDeathVfxSourceRef = "player.death";
        private const string GameOverOverlayRootName = "Game Over Overlay Root";
        internal const string GameVictoryOverlayRootName = "Game Victory Overlay Root";
#if UNITY_EDITOR
        private const string CardRewardOverlayPrefabAssetPath = "Assets/Resources/UI/Prototype/Card Reward Overlay Root.prefab";
        private const string GameOverOverlayPrefabAssetPath = "Assets/Prefabs/UI/Prototype/Game Over Overlay Root.prefab";
        private const string GameVictoryOverlayPrefabAssetPath = "Assets/Prefabs/UI/Prototype/Game Victory Overlay Root.prefab";
#endif
        // Dedicated celebration cue (CFXR firework prefabs). Previously this aliased
        // TreasureChestRewardVfxCue, which is authored for the chest: its floatingTextMode is Show with the
        // override "card acquired", so the victory burst printed that text over the memory stone.
        internal const string MemoryStoneVictoryFireworkVfxCue = "objective.memory_gyeol.victory_firework";
        private const float DefaultMemoryStoneVictoryCameraHeight = 15f;
        private const float DefaultMemoryStoneVictoryCameraOrthographicSize = 13f;
        private const float LegacyMemoryStoneVictoryCameraHeight = 16f;
        private const float LegacyMemoryStoneVictoryCameraOrthographicSize = 9f;
        private const float DefaultMemoryStoneVictoryCameraPitchDegrees = 40f;
        private const float DefaultMemoryStoneVictoryCameraYawDegrees = 0f;
        private const float LegacyMemoryStoneVictoryCameraPitchDegrees = 58f;
        private const float LegacyMemoryStoneVictoryCameraYawDegrees = 35f;

        // Per-turn performance markers (read via ProfilerRecorder in perf tests). Cheap when not recording.
        private static readonly ProfilerMarker PathfindMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.Pathfind");
        private static readonly ProfilerMarker StateMoveMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.StateMove");
        private static readonly ProfilerMarker VisibilityHighlightsMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.RefreshVisibilityHighlights");
        private static readonly ProfilerMarker CommitPresentationMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.CommitPresentation");
        private static readonly ProfilerMarker HudRefreshMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.RefreshHud");
        private static readonly ProfilerMarker VisApplyMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.Vis.ApplyVisibility");
        private static readonly ProfilerMarker VisBuildMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.Vis.OverlayBuild");
        private static readonly ProfilerMarker VisApplyPresentationMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.Vis.OverlayApply");
        private static readonly ProfilerMarker VisPhysicsSyncMarker = new ProfilerMarker(ProfilerCategory.Scripts, "MapCombat.Vis.PhysicsSync");
        private static readonly Color[] MonsterMarkerAccentColors =
        {
            new Color(0.95f, 0.25f, 0.2f, 1f),
            new Color(0.15f, 0.65f, 1f, 1f),
            new Color(0.35f, 0.9f, 0.35f, 1f),
            new Color(1f, 0.72f, 0.18f, 1f),
            new Color(0.78f, 0.42f, 1f, 1f),
            new Color(0.1f, 0.95f, 0.85f, 1f)
        };
        [Header("M1 Map Sources")]
        [SerializeField] private HexSparseMapAuthoringSource sparseSource;
        [SerializeField] private HexMapPurpose expectedBoardPurpose = HexMapPurpose.Unspecified;
        [SerializeField] private HexTerrainPalette terrainPalette;
        [SerializeField] private AtlasTilePresentationView atlasTilePresentationView;
        [Tooltip("Optional. Decorates fog-of-war frontier tiles with particles. Leave null to disable.")]
        [SerializeField] private VisibilityFogPresenter visibilityFogPresenter;
        [SerializeField] private HexMapInputController inputController;
        [SerializeField] private Camera prototype3DCamera;

        [Header("Field Object Visuals")]
        [Tooltip("Maps field object visualRef values to prefab assets. visualRef is the id used by card/effect data. " +
                 "When empty, the default prefab for the effect kind is used.")]
        [SerializeField] private List<FieldObjectVisualMapping> fieldObjectVisualMappings = new List<FieldObjectVisualMapping>();

        [Header("M2 Starting Positions")]
        [SerializeField] private HexCoord playerStart = new HexCoord(0, 0);
        [SerializeField] private HexCoord enemyStart = new HexCoord(2, 0);

        [Header("M2 Combat Config")]
        [SerializeField] private CombatCatalogTextAssetSource catalogTextAssetSource;
        [SerializeField] private CardCatalogAsset cardCatalogAsset;
        [Tooltip("Player combat balance profile id from Assets/Data/Combat/Players/Source/player_combat_profiles.csv.")]
        [SerializeField] private string playerCombatProfileId = PlayerCombatProfileCatalog.DefaultProfileId;
        [SerializeField] private int playerHp = 80;
        [SerializeField] private int enemyHp = 30;
        [SerializeField] private int playerMovePoints = 2;
        [Tooltip("Draw opening hands immediately at combat start. Disable when a start sequence should call StartPlayerTurn after presentation.")]
        [SerializeField] private int playerVisionRange = 7;
        [SerializeField] private int playerAttackDamage = 4;
        [SerializeField] private int playerBlock = 4;
        [SerializeField] private int movementHandSize = 3;
        [SerializeField] private int actionHandSize = 5;
        [Tooltip("Canonical player-owned starting deck for this scene. Card IDs must exist in the selected CSV card catalog.")]
        [SerializeField] private PlayerStartingDeckAsset startingDeckAsset;
        private PlayerStartingDeckAsset startingDeckOverride;
        private PlayerRunSaveData playerRunRestore;
        private CombatSuspendEnvelope combatSuspendRestore;
        // 배치 랜덤화(placement-randomization-plan §2-2). 시드는 combatSuspendRestore처럼 소비-소거하지
        // 않는다 — 같은 씬 인스턴스의 재초기화가 배치를 다시 굴리면 ClaimedEventObjectIds 정합이 깨진다.
        // 새 시드는 스테이지 진입(씬 로드)마다 플로우 계층이 발급한다(Q1).
        private bool placementRandomizationEnabled;
        private bool hasPlacementSeed;
        private int placementSeed;
        private string placementStageId = string.Empty;

        /// <summary>
        /// True when the most recent <see cref="InitializeIntegration"/> actually applied a ② suspend
        /// snapshot (not merely that one was queued). The flow layer keys the anti-scum disk delete on this
        /// so a skipped init never discards a save that was not restored. Reset at the start of each init.
        /// </summary>
        public bool CombatSuspendRestoreApplied { get; private set; }
        [SerializeField] private int enemyChaseRange = 6;
        [SerializeField] private int enemyDisengageRange = 7;
        [SerializeField] private int enemyAttackDamage = 5;

        [Header("M2 Inventory")]
        [SerializeField] private string[] startingPermanentItemIds = { };

        [Header("Runtime View")]
        [SerializeField] private CardRewardPopupView cardRewardPopupView;

        // Seam accessor (refactoring stage 4-5): route MapCombatController's direct calls into
        // cardRewardPopupView through the interface seam instead of the concrete View type. The
        // serialized field above stays concrete (Unity needs it for FindObjectsByType lookups in
        // EnsureCardRewardPopupView); only this call-site accessor is interface-typed. Mirrors
        // ICombatAudioHost/ICombatCameraHost seam pattern.
        private ICardRewardPopupView CardRewardPresentation => cardRewardPopupView;
        [SerializeField] private GameObject gameOverOverlayRoot;
        [SerializeField] private GameObject gameVictoryOverlayRoot;
        [SerializeField] private TutorialDirector tutorialDirector;
        [Tooltip("Monster id treated as the tutorial boss. When empty, any monster whose id contains " +
                 "'boss' is used. Fires the 'boss.sighted' tutorial event once it enters the player's vision.")]
        [SerializeField] private string tutorialBossMonsterId;

        [Header("MemoryStone Victory Presentation")]
        [SerializeField] private bool playMemoryStoneVictorySequence = true;
        [SerializeField] private float memoryStoneVictoryCameraHeight = DefaultMemoryStoneVictoryCameraHeight;
        [SerializeField] private bool memoryStoneVictoryCameraFollowsRevealRoute;
        [SerializeField] private bool memoryStoneVictoryUseFixedCameraRotation = true;
        [SerializeField] private float memoryStoneVictoryCameraPitchDegrees = DefaultMemoryStoneVictoryCameraPitchDegrees;
        [SerializeField] private float memoryStoneVictoryCameraYawDegrees;
        [SerializeField] private float memoryStoneVictoryCameraFocusLag = 0.08f;
        [SerializeField] private float memoryStoneVictoryCameraOrthographicSize = DefaultMemoryStoneVictoryCameraOrthographicSize;
        [Tooltip("Shape of the purification front. How much later a cell off to the side of the route lights up, in percent of a wave step per hex of sideways distance. 0 = a straight band wiping along the route; 100 = sideways distance counts the same as distance along the route, so the front is a circular wave rippling outward; higher still = an increasingly round, slower-spreading ripple. Every cell is covered at any setting.")]
        [SerializeField] private int memoryStoneVictoryLateralSpreadPercent = 100;
        [SerializeField] private bool memoryStoneVictoryRevealRemainingMapAfterWave = true;
        [Tooltip("Corner rounding radius (world units) applied to the sweep path so authored waypoints are turned through instead of snapped at. 0 = keep the raw angular polyline.")]
        [SerializeField] private float memoryStoneVictoryCornerRadius = 7f;
        [Tooltip("How far ahead along the path (world units) the camera anticipates a corner when it turns to face the route direction. Smaller = turns later and tighter.")]
        [SerializeField] private float memoryStoneVictoryCorneringLookAheadDistance = 6f;
        [Tooltip("Seconds for the sweep camera's heading to catch up to the route direction. Damps the yaw whip on sharp corners; 0 = snap to the path heading every frame.")]
        [SerializeField] private float memoryStoneVictoryYawSmoothingSeconds = 0.28f;
        [Tooltip("Seconds to hold the final framing after the ending dolly-in lands and the result UI appears.")]
        [SerializeField] private float memoryStoneVictoryFinalHoldSeconds = 1.2f;
        [SerializeField] private float memoryStoneVictoryFireworkShakeStrength = 0.35f;
        [Tooltip("Number of firework bursts let off over the purified memory stone. 1 = a single burst.")]
        [SerializeField] private int memoryStoneVictoryFireworkBurstCount = 4;
        [Tooltip("Seconds between firework bursts.")]
        [SerializeField] private float memoryStoneVictoryFireworkBurstInterval = 0.32f;
        [Tooltip("How far around the stone the bursts are scattered, in world units. 0 stacks them all on the stone.")]
        [SerializeField] private float memoryStoneVictoryFireworkScatterRadius = 5f;

        [Header("MemoryStone Victory - Cut 1 (purification sweep)")]
        [Tooltip("Reveal-sweep speed in world units per second. Tiles are revealed at this constant (arc-length based) pace so the reveal no longer speeds up on long segments.")]
        [SerializeField] private float memoryStoneVictorySweepSpeed = 19f;
        [Tooltip("How far ahead of the camera the purification front runs, in world units. Held constant for the whole sweep so there is always cleared ground in front of the camera — the camera chases the wave rather than overtaking it.")]
        [SerializeField] private float memoryStoneVictoryRevealLeadDistance = 20f;

        [Header("MemoryStone Victory - Cut 2 (cleared-city side pass)")]
        [Tooltip("Cut 2: plays a short low side-on pass over the already-purified route (bridge / bike path / apartment street) before the ending. 0 seconds skips the cut entirely. This is the UPPER bound: the actual length comes from how far the camera travels, at Side View Speed.")]
        [SerializeField] private float memoryStoneVictorySideViewSeconds = 5f;
        [Tooltip("Cut 2: felt travel speed in world units per second, the same way Cut 1 paces its sweep. The pass length is derived from this so a small map does not crawl through the same shot a large one drifts through.")]
        [SerializeField] private float memoryStoneVictorySideViewSpeed = 9.4f;
        [Tooltip("Cut 2: shortest the derived pass is allowed to get, so a very short route still reads as a shot rather than a flash.")]
        [SerializeField] private float memoryStoneVictorySideViewMinSeconds = 1.5f;
        [Tooltip("Cut 2: caps Side View Lateral Offset at this fraction of the map's smaller horizontal extent, so the pass cannot fly off the edge of a small map. 0 disables the cap.")]
        [SerializeField] private float memoryStoneVictorySideViewMaxLateralExtentFraction = 0.22f;
        [Tooltip("Cut 2: camera height above the tiles. Low, so the pass reads as street level rather than another overhead shot.")]
        [SerializeField] private float memoryStoneVictorySideViewHeight = 5f;
        [Tooltip("Cut 2: sideways distance from the route the camera flies at. Sign picks which side of the street it watches from; flip it if the pass ends up staring into buildings.")]
        [SerializeField] private float memoryStoneVictorySideViewLateralOffset = 15f;
        [Tooltip("Cut 2: where along the sweep route the pass starts (0 = player spawn, 1 = memory stone).")]
        [SerializeField] private float memoryStoneVictorySideViewStartProgress = 0.05f;
        [Tooltip("Cut 2: where along the sweep route the pass ends.")]
        [SerializeField] private float memoryStoneVictorySideViewEndProgress = 0.45f;

        [Header("MemoryStone Victory - Cut 3 (memory stone)")]
        [Tooltip("Cut 3: radius the camera orbits the stone at before closing in. Follows the trailer's cut-6 grammar: orbit at a held distance, settle, then dolly in.")]
        [SerializeField] private float memoryStoneVictoryEndOrbitDistance = 12f;
        [Tooltip("Cut 3: final distance from the stone after the dolly-in. At the 60 degree cinematic FOV this puts the 3.3u stone across ~68% of the frame height — a real close-up, not the half-hearted 24% the old 12u ending landed on.")]
        [SerializeField] private float memoryStoneVictoryEndZoomDistance = 4.2f;
        [Tooltip("Cut 3: degrees the camera orbits away from its opening azimuth before settling. The shot OPENS on the landmark-facing framing and rotates from there.")]
        [SerializeField] private float memoryStoneVictoryEndOrbitDegrees = 60f;
        [Tooltip("Cut 3: rotates the whole shot around the stone, on top of the azimuth resolved from the backdrop landmark. 180 opens the shot from the landmark's own side (the front-of-Lotte-World view); 0 opens from directly opposite it.")]
        [SerializeField] private float memoryStoneVictoryEndAzimuthOffsetDegrees = 180f;
        [Tooltip("Cut 3: seconds for the orbit.")]
        [SerializeField] private float memoryStoneVictoryEndOrbitSeconds = 3.5f;
        [Tooltip("Cut 3: seconds the camera holds still after the orbit, before the dolly-in. The stop is part of the shot, not a cut.")]
        [SerializeField] private float memoryStoneVictoryEndSettleSeconds = 0.8f;
        [Tooltip("Cut 3: seconds for the dolly-in onto the stone. The result UI is withheld until this completes.")]
        [SerializeField] private float memoryStoneVictoryEndZoomInSeconds = 2.6f;
        [Tooltip("Cut 3: camera elevation in degrees. Low, matching the trailer cut, so a tall landmark behind the stone stays in frame.")]
        [SerializeField] private float memoryStoneVictoryEndPitchDegrees = 20f;
        [Tooltip("Cut 3: height above the memory stone's tile that the camera aims at. Sits near the middle of the stone model (the intro's +5 aims over its head).")]
        [SerializeField] private float memoryStoneVictoryEndLookAtHeight = 1.7f;
        [Tooltip("Cut 3: the shot lines the camera up so the stone sits in front of a landmark. Only landmarks at least this far from the stone qualify as a background (closer ones would sit beside it instead of behind).")]
        [SerializeField] private float memoryStoneVictoryEndBackdropMinDistance = 10f;
        [Tooltip("Cut 3: landmarks farther than this from the stone are ignored when picking the backdrop.")]
        [SerializeField] private float memoryStoneVictoryEndBackdropMaxDistance = 70f;

        [Header("Stage Intro Cinematic")]
        [Tooltip("When enabled, the stage opens with a cinematic camera sweep over the map (overview -> landmarks -> memory stone) before the first turn starts. Set per-stage via StageDefinition.")]
        [SerializeField] private bool playStageIntroCinematic;
        [Tooltip("Camera elevation angle in degrees above the horizon when framing each landmark / the memory stone. ~45 gives a pleasant 3/4 view; 90 = straight top-down.")]
        [SerializeField] private float stageIntroCameraPitchDegrees = 45f;
        [Tooltip("Distance from the focused landmark/memory stone to the camera while orbiting it. Larger = wider framing.")]
        [SerializeField] private float stageIntroOrbitDistance = 16f;
        [Tooltip("Raises the camera aim point above the focused tile so tall objects (e.g. towers) are vertically centered instead of having their tops cut off.")]
        [SerializeField] private float stageIntroLookAtHeightOffset = 5f;
        [Tooltip("How many degrees the camera orbits around each landmark / the memory stone during its dwell. Positive = clockwise seen from above.")]
        [SerializeField] private float stageIntroOrbitDegrees = 70f;
        [Tooltip("Distance from the map center for the opening wide overview shot. 0 or less auto-fits to the map bounds.")]
        [SerializeField] private float stageIntroOverviewDistance;
        [Tooltip("Perspective FOV (or orthographic size) forced on the intro camera so the framing reads wide.")]
        [SerializeField] private float stageIntroCameraFieldOfView = 60f;
        [Tooltip("How long (realtime seconds) the intro holds (slowly orbiting) on the opening wide overview shot.")]
        [SerializeField] private float stageIntroOverviewHoldSeconds = 2f;
        [Tooltip("Travel time (realtime seconds) the camera takes to move between intro focus points.")]
        [SerializeField] private float stageIntroMoveSeconds = 1.3f;
        [Tooltip("How long (realtime seconds) the camera dwells (orbiting) on each landmark / the memory stone.")]
        [SerializeField] private float stageIntroDwellSeconds = 1.8f;
        [Tooltip("Finale (realtime seconds): after dwelling on the memory stone, darkness ripples out from it across the map while the camera slowly zooms out to the map center.")]
        [SerializeField] private float stageIntroFinaleSeconds = 2.8f;
        [Tooltip("Finale: how many cells ahead of the advancing darkness a monster without its own spawn beat pops in. Larger = monsters appear earlier and are visible longer before the front swallows them; 0 makes them spawn exactly as their tile goes dark.")]
        [SerializeField] private float stageIntroFinaleMonsterRevealLeadDistance = 3f;
        [Tooltip("Authored dolly (IntroCameraPoint chain): camera travel speed in world units/second. Per-segment duration is derived from the segment length so the felt speed stays constant regardless of point spacing. Scaled per-point by cameraSpeedMultiplier.")]
        [SerializeField] private float stageIntroDollySpeedUnitsPerSecond = 8f;
        [Tooltip("Authored dolly: how far ahead (world units) along the direction of travel the camera aims, so a straight street move looks where it is going.")]
        [SerializeField] private float stageIntroDollyLookAheadDistance = 6f;
        [Tooltip("Authored dolly: camera height (world units) above each authored IntroCameraPoint tile as it moves along the path. A point can override this with its own cameraHeightOverride.")]
        [SerializeField] private float stageIntroDollyCameraHeight = 3f;
        [Tooltip("Authored dolly: how far above the street the camera AIMS. Keep it well below the camera height or the shot tilts up into the sky — the depression angle is atan((camera height - this) / look-ahead distance). This is separate from the landmark orbit's look-at offset on purpose.")]
        [SerializeField] private float stageIntroDollyLookAtHeightOffset;
        [Tooltip("Authored dolly: seconds spent easing up to speed at the START of a run and easing back down at its END. Everything between holds one constant velocity, so the camera never speeds up or slows down mid-flight. 0 = no ramps (instant full speed, instant stop). Each ramp lengthens the run by half this value.")]
        [SerializeField] private float stageIntroDollyRampSeconds = 0.8f;
        [Tooltip("Night transition beat (realtime seconds): after the day shots, a wide drift over the map while the day look crossfades to night. Monsters spawn after this beat.")]
        [SerializeField] private float stageIntroNightTransitionSeconds = 2.5f;
        [Tooltip("How many monster spawns get their own close-up beat after night falls (0 disables the beats; remaining monsters appear off-camera). Boss/elite spawns are featured first.")]
        [SerializeField] private int stageIntroMonsterSpawnShotCount = 3;
        [Tooltip("Length (realtime seconds) of each monster spawn close-up beat (low-angle arc around the spawn point).")]
        [SerializeField] private float stageIntroMonsterSpawnShotSeconds = 2.2f;
        [Tooltip("Delay (realtime seconds) into a spawn beat before the monster appears, so the spawn VFX reads first.")]
        [SerializeField] private float stageIntroMonsterSpawnRevealDelaySeconds = 0.35f;
        [Tooltip("Delay (realtime seconds) into a spawn beat before the featured monster swings its attack. Must sit after the reveal delay: the marker/animator is created on the reveal frame, and a trigger fired that same frame is dropped before the animator binds.")]
        [SerializeField] private float stageIntroMonsterSpawnAttackDelaySeconds = 0.55f;
        [Tooltip("Camera distance from the spawn point during a monster spawn close-up beat.")]
        [SerializeField] private float stageIntroMonsterSpawnDistance = 7f;
        [Tooltip("Camera elevation angle (degrees above the horizon) for monster spawn close-ups. Low values give the hero low-angle framing.")]
        [SerializeField] private float stageIntroMonsterSpawnPitchDegrees = 18f;
        [Tooltip("Raises the camera aim point above the spawn tile so the monster's body (not its feet) is centered.")]
        [SerializeField] private float stageIntroMonsterSpawnLookAtHeight = 1.2f;

        [SerializeField] private CombatDebugControlPanel debugControlPanel;
        [SerializeField] private bool autoCreateDebugControlPanel;
        [SerializeField] private CombatAudioPresenter audioPresenter;
        [SerializeField] private SoundCatalog soundCatalog;
        [SerializeField] private bool autoCreateAudioPresenter = true;
        [SerializeField] private Color enemyMarkerColor = new Color(0.78f, 0.12f, 0.12f, 1f);
        [SerializeField] private float enemyMarkerRadius = 0.24f;
        // 0.38은 옛 구형 마커(반지름 0.24)의 중심 높이였다. 모델은 Ground 앵커로 타일 윗면에 붙이므로(2026-09-03) 기본 0.
        [SerializeField] private float enemyMarkerHeightOffset = 0f;
        [SerializeField] private GameObject enemyMarkerPrefab;
        [Tooltip("Per-monster visual prefabs keyed by catalog definition id (e.g. M001..M006). Required for player " +
                 "builds: AssetDatabase path loading is editor-only, so without these bindings every monster falls " +
                 "back to enemyMarkerPrefab in a build. Use the 'Populate Monster Visual Prefabs' context menu to fill " +
                 "from the active monster catalog.")]
        [SerializeField] private List<MonsterVisualPrefabBinding> monsterVisualPrefabBindings = new List<MonsterVisualPrefabBinding>();
        [SerializeField] private Vector3 enemyVisualLocalOffset;
        [SerializeField] private Vector3 enemyVisualLocalEulerAngles;
        [SerializeField] private Vector3 enemyVisualLocalScale = Vector3.one;

        // ── 보스 아우라: 발밑 링 층 (docs/object-outline-aura-plan.md §11) ────────────────────
        // ⚠️ 출하 씬은 이 값들을 직렬화하지 않으므로 <b>여기 초기값이 곧 실효값</b>이다(카메라 트랙에서
        // 확인된 이 프로젝트의 패턴). 한 줄이 전 씬을 바꾸고, 되돌릴 곳도 여기 한 줄이다.
        //
        // 🔴 프레넬 림 층은 2026-08-06 사용자 판정으로 폐기했다(§11.8). 3D 메시에서 선 굵기가 면의
        // 기울기로 정해져 갈기·털처럼 스치는 면이 많은 메시에서 잘게 흩어졌다. 되살리지 말 것 —
        // 각진 메시에서는 반대로 면 전체가 통으로 밝아져 "테두리"가 아니라 "면 하이라이트"가 된다.
        [Tooltip("보스 아우라의 발밑 링 층을 켠다.")]
        // §28 W8(2026-08-06 실플레이 판정): 페이즈 아우라 링 OFF. 되살릴 땐 이 기본값과
        // MonsterLab 씬의 직렬화 값(씬이 기본값을 이긴다 — 카메라 트랙 교훈)을 함께 켤 것.
        [SerializeField] private bool bossAuraRingEnabled = false;

        // 프로젝트 소유 클론이다. 원본(Hovl 'Magic circle')을 직접 참조하면 룩을 고칠 때
        // ThirdParty를 편집하게 되고, 그 순간 같은 팩을 쓰는 다른 연출까지 함께 바뀐다.
        [Tooltip("발밑 링 프리팹. 비우면 링 층이 뜨지 않는다.")]
        [SerializeField] private GameObject bossAuraRingPrefab;

        // 링 지름 = 모델 발자국 지름 × 이 값. 발자국은 CharacterHoverTarget의 판정 상자에서 오므로
        // 페이즈 배율이 바뀌면 링도 함께 커진다.
        [Tooltip("발밑 링 지름 배수(1이면 모델 발자국과 같은 지름).")]
        [SerializeField] private float bossAuraRingRadiusMultiplier = 1.35f;

        private const string BossAuraRingResourcePath = "Combat/Vfx/BossAuraRing";
        private GameObject bossAuraRingPrefabFallback;
        private bool bossAuraRingPrefabResolved;

        [Header("Presentation Sequencing")]
        [SerializeField] private bool playPresentationSequences = true;
        [Tooltip("Optional reusable timing profile. When assigned, its values override the per-field timings below so presentation feel can be tuned from one asset.")]
        [SerializeField] private CombatTimingProfile timingProfile;
        [Tooltip("Optional per-attack timing overrides keyed by cardId/patternId. Unset fields inherit the profile above; an empty table leaves timing byte-identical to profile-only.")]
        [SerializeField] private CombatAttackTimingTable attackTimingTable;
        [SerializeField] private float playerMoveSeconds = 0.35f;
        [SerializeField] private float enemyMoveStartDelay = 0.08f;
        [Tooltip("Short anticipation beat after the enemy-turn cue before monster movement/attack presentation starts.")]
        [SerializeField] private float enemyTurnBeginPresentationDelay = 0.25f;
        [Tooltip("Realtime hold after all monster movement/action presentation cues finish, before the next phase becomes interactive.")]
        [SerializeField] private float monsterPhaseCompleteHoldSeconds = 1f;
        [SerializeField] private float enemyMoveSeconds = 0.3f;
        [SerializeField] private float attackWindupDelay = 0.12f;
        [SerializeField] private float attackImpactDelay = 0.2f;
        [SerializeField] private float deathDelay = 0.25f;
        [SerializeField] private EffectPresentationController movementEffectPresentation;
        [SerializeField] private bool playPlayerMoveVfx;
        [SerializeField] private bool snapPlayerMoveVfxToHexSides = true;
        [SerializeField] private float playerMoveVfxYawOffset = CombatFacingUtility.DefaultHexSideYawOffset;

        [Header("Debug")]
        [SerializeField] private bool initializeOnStart = true;
        [SerializeField] private bool drawOpeningHandsOnInitialize = true;
        [SerializeField] private bool keepStartingDeckOrderInPlayMode;
        [SerializeField] private bool revealAllMapCellsInDebugMode;
        [SerializeField] private bool showOverlayDebugControls = true;
        [SerializeField] private bool showPlayerMovementOverlay = true;
        [SerializeField] private bool showPlayerActionOverlay = true;
        [SerializeField] private bool showMonsterMoveOverlay = true;
        [SerializeField] private bool showMonsterAttackOverlay = true;
        [SerializeField] private bool showMonsterChaseOverlay;
        [Tooltip("Debug-only floating monster intent arrows. Off by default: monster model facing is always applied, but the arrow meshes stay hidden from players.")]
        [SerializeField] private bool showMonsterIntentArrows;
        [SerializeField] private bool clickMoveDebugModeEnabled;
        [SerializeField] private CombatOverlayRendererBackend overlayRendererBackend = CombatOverlayRendererBackend.Auto;
        [SerializeField] private StatusIconOverlayRenderer statusIconOverlayRenderer;

        [Header("Gameplay Camera")]
        [SerializeField] private CombatCameraProfile cameraProfile;
        [SerializeField] private bool keepCameraCenteredOnPlayer = true;
        [SerializeField] private Vector3 cameraPlayerOffset = new Vector3(0f, 9.5f, -7f);
        [SerializeField] private bool autoCalculateCameraPlayerOffset;
        [SerializeField] private float cameraFollowDistance = 10f;
        [SerializeField] private Vector2 cameraFramingOffset;
        [SerializeField] private Vector3 cameraEulerAngles = new Vector3(35f, 0f, 0f);
        [SerializeField] private float cameraOrthographicSize = 5f;
        [SerializeField] private float minCameraOrthographicSize = 5f;
        [SerializeField] private float maxCameraOrthographicSize = 14f;
        [SerializeField] private float cameraFollowSmoothing = 12f;

        [Header("Camera Shake (attack/hit presentation)")]
        [Tooltip("Enable camera shake during combat presentation.")]
        [SerializeField] private float cardAttackCameraShakeStrength = 0.25f;
        [Tooltip("Default camera shake amplitude. Higher values shake more strongly.")]
        [SerializeField] private float monsterAttackCameraShakeStrength = 0.5f;
        [SerializeField] private float cameraShakeDuration = 0.25f;
        [SerializeField] private int cameraShakeVibrato = 12;
        [SerializeField] private float cameraShakeRandomness = 90f;
        [SerializeField] private CinemachineImpulseSource cinemachineCameraShakeSource;
        [Tooltip("Camera shake duration. Short values under one second are recommended.")]
        [SerializeField] private float cameraShakeDamageScale = 0.05f;
        [Tooltip("Additional shake multiplier for heavy or lethal hits.")]
        [SerializeField] private float cameraShakeMaxMultiplier = 2.5f;

        [Header("Player Death Presentation")]
        [Tooltip("Cue fired when the player death sequence begins. Author the final SFX on this cue in the SoundCatalog.")]
        [SerializeField] private string playerDeathAudioCueId = AudioCueIds.CombatPlayerDeath;
        [Tooltip("Realtime length of the player death sequence before defeat UI may appear. Set to override; 0 uses the authored death SFX clip length, then falls back to the attack timing death delay.")]
        [SerializeField] private float playerDeathSequenceDuration = 0f;
        [SerializeField] private float playerDeathCameraShakeStrength = 1.25f;
        [SerializeField] private float playerDeathCameraShakeDuration = 0.65f;
        [SerializeField] private int playerDeathCameraShakeVibrato = 20;
        [SerializeField] private float playerDeathCameraShakeRandomness = 120f;
        [Tooltip("Short impact freeze before the death slow-motion starts.")]
        [SerializeField] private float playerDeathHitStopDuration = 0.1f;
        [Range(0.01f, 1f)]
        [SerializeField] private float playerDeathHitStopTimeScale = 0.04f;
        [Tooltip("Secondary, softer camera shake that runs after the first death impact shake.")]
        [SerializeField] private float playerDeathSettleCameraShakeStrength = 0.35f;
        [SerializeField] private float playerDeathSettleCameraShakeDuration = 0.9f;
        [SerializeField] private int playerDeathSettleCameraShakeVibrato = 12;
        [SerializeField] private float playerDeathSettleCameraShakeRandomness = 65f;
        [Range(0.05f, 1f)]
        [SerializeField] private float playerDeathSlowMotionScale = 0.45f;
        [Tooltip("Realtime slow-motion window inside the death sequence. Keep short so the UI handoff does not feel sluggish.")]
        [SerializeField] private float playerDeathSlowMotionDuration = 1.0f;
        [Tooltip("During the death slow-motion beat, ease the gameplay camera closer to the player.")]
        [SerializeField] private bool playerDeathZoomInEnabled = true;
        [Tooltip("Realtime length of the death camera zoom. 0 uses the slow-motion duration.")]
        [SerializeField] private float playerDeathZoomInDuration = 0f;
        [Tooltip("Target zoom multiplier. Lower than 1 moves/zooms the camera closer.")]
        [Range(0.25f, 1f)]
        [SerializeField] private float playerDeathZoomInMultiplier = 0.82f;
        [Tooltip("Temporary yaw offset applied while the death camera zoom eases in.")]
        [SerializeField] private float playerDeathCameraYawOffsetDegrees = 4f;
        [Tooltip("Temporary pitch offset applied while the death camera zoom eases in.")]
        [SerializeField] private float playerDeathCameraPitchOffsetDegrees = -3f;

        private HexMapData configuredMap;
        private HexTerrainTraits activeTerrainTraits = HexTerrainTraits.Default;
        private IReadOnlyDictionary<HexCoord, int> reachable = new Dictionary<HexCoord, int>();
        private HexCoord? lastHoveredCoord;
        // Id of the monster currently under the mouse (null when none). When set, the monster
        // attack-intent overlay narrows to this monster only; cleared when the pointer leaves.
        private string hoveredMonsterId;
        private MonsterMovePathPresenter monsterMovePathPresenter;
        private readonly List<MonsterMovePathPresenter.Entry> monsterMovePathEntries = new List<MonsterMovePathPresenter.Entry>();
        private readonly PresentationScheduler presentationScheduler = new PresentationScheduler();
        private readonly HashSet<int> dispatchedEffectIndices = new HashSet<int>();
        private CombatTimingProfile runtimeTimingProfileFallback;
        private readonly CombatActorMarkerPresenter actorMarkerPresenter = new CombatActorMarkerPresenter();
        private readonly Dictionary<string, GameObject> monsterVisualPrefabByDefinitionId = new Dictionary<string, GameObject>(System.StringComparer.Ordinal);
        private CombatStatusLoopVfxController statusLoopVfxController;
        private CombatCameraController cameraController;
        private CombatAudioHostAdapter audioHostAdapter;
        private CombatCardHudHostAdapter cardHudHostAdapter;
        private bool tutorialDirectorStepHooked;
        private bool gameStartCompleted = true;
        private readonly CombatHudPresenter hudPresenter = new CombatHudPresenter();
        private CombatMapOverlayPresenter overlayPresenter;
        private CombatOverlayRendererBackend activeOverlayRendererBackend;
        private AtlasTilePresentationView activeOverlayPresenterView;
        private BatchedMeshCombatOverlayRenderer batchedOverlayRenderer;
        private readonly CombatOverlayQuery overlayQuery = new CombatOverlayQuery();
        private readonly CombatOverlayPresentationBuilder overlayPresentationBuilder = new CombatOverlayPresentationBuilder();
        private readonly MovementPathQuery movementPathQuery = new MovementPathQuery();
        private readonly CombatVisibilityPresenter visibilityPresenter = new CombatVisibilityPresenter();
        private CombatSelectionState selectionState = CombatSelectionState.None;
        private ChoiceCardPanelModel pendingChoicePanel = new ChoiceCardPanelModel(string.Empty, System.Array.Empty<ChoiceCardOptionModel>());
        private bool isChoicePanelVisible;
        private bool isHandCardSelectionActive;
        private bool isHandCardSelectionAwaitingTarget;
        private bool hasHandCardSelectionLockedTarget;
        private HexCoord handCardSelectionLockedTarget;
        private string handCardSelectionSourceCardKey = string.Empty;
        private string handCardSelectionPromptText = string.Empty;
        private int handCardSelectionMinCount;
        private int handCardSelectionMaxCount;
        private readonly HashSet<string> handCardSelectionSelectedKeys = new HashSet<string>(System.StringComparer.Ordinal);
        private bool subscribedToInput;
        private HexMapInputController subscribedInputController;
        private MonsterTooltipHudPresenter tooltipPresenter;
        private FieldObjectVisualPresenter fieldObjectVisualPresenter;
        private FieldObjectTooltipHudPresenter fieldObjectTooltipPresenter;
        private HexCoord? lastFieldObjectHoverCoord;
        private ObjectInfoTooltipHudPresenter objectInfoTooltipPresenter;
        private bool isStatusIconOverlayHoverActive;
        // 네임플레이트 배지가 호버를 쥐고 있는 동안 다른 호버 판정은 전부 비켜선다 — 배지는 몬스터
        // 본체 <b>위에 겹쳐</b> 뜨므로, 양보하지 않으면 몬스터 툴팁이 매 프레임 배지 툴팁을 덮어쓴다.
        private bool isNameplateBadgeHoverActive;

        /// <summary>
        /// 지금 뒤끝 배지에 손을 얹어 그리고 있는 칸들(2026-09-01 #3). 커서가 떠나면 다음 프레임에
        /// 비워진다 — 유도값이라 「지우는 걸 잊어 오버레이가 남는」 사고가 안 난다.
        /// </summary>
        /// <summary>
        /// 지금 손을 얹은 특성 배지가 그리는 칸(2026-09-04). 뒤끝·담력 시험·홀림이 각자 다른 계산으로
        /// 같은 보라 레이어를 채운다 — 어휘가 하나이기 때문이다: <b>손을 얹은 동안에만 뜨는, 이 특성이
        /// 미치는 자리</b>. 유도값이라 커서가 떠나면 다음 프레임에 저절로 꺼진다.
        /// </summary>
        /// <summary>은신 해제 페이드 길이·시작 알파. 사라질 때는 연막이 가리지만 나타날 때는 없다.</summary>
        private const float StealthRevealFadeSeconds = 0.45f;
        private const float StealthRevealFadeStartAlpha = 0.12f;

        /// <summary>지난 프레임에 <b>보이던</b> 은신 특성 몬스터. 이 집합에 없다가 생기면 방금 드러난 것이다.</summary>
        private readonly HashSet<string> stealthVisibleLastFrame = new HashSet<string>(System.StringComparer.Ordinal);
        private readonly HashSet<string> stealthRevealFadeSeen = new HashSet<string>(System.StringComparer.Ordinal);

        private IReadOnlyList<HexCoord> hoveredTraitReachCells = System.Array.Empty<HexCoord>();

        /// <summary>뒤끝 호버 오버레이가 그릴 칸들 — 오버레이 요청 조립부가 읽는다.</summary>
        public IReadOnlyList<HexCoord> HoveredTraitReachCells => hoveredTraitReachCells;
        private bool isTutorialTileHighlightActive;
        private readonly Dictionary<string, GameObject> monsterArrowOverlays = new Dictionary<string, GameObject>();
        private Mesh arrowMesh;
        private Material arrowMaterialAttack;
        private Material arrowMaterialChase;
        private bool hasAppliedRuntimeInspectorSettings;
        private int? testPlayerVisionRangeOverride;
        private bool useExplicitTestReferences;
        private bool resolvePresentationSequencesImmediately;
        private bool isSequencePlaying;
        private Coroutine activeSequenceRoutine;
        // Set by the dev trailer runner while a take is filming; see IsCinematicViewActive.
        private bool trailerFilmingActive;
        // Set by the dev capture tooling only; see CinematicDeltaTime.
        private bool cinematicUsesCaptureTimeSource;
        // Presentation-only forced-reveal set shared by both cinematics: the stage-intro finale starts with
        // every cell in it and removes them as darkness rolls out; the victory sweep starts empty and adds
        // cells as the light front passes. Null = no cinematic override, real fog rules apply.
        private HashSet<HexCoord> cinematicForcedRevealCells;
        // Monster ids kept invisible during the stage intro until their spawn beat reveals them
        // (presentation-only; the sim's monsters exist from combat start). Null = inactive.
        private HashSet<string> stageIntroHiddenMonsterIds;
        private GameObject memoryStoneActivationLabelRoot;
        private TMP_Text memoryStoneActivationLabel;
        private int presentationSequenceVersion;
        private int stateGeneration;
        private string resolvingStatusText = string.Empty;
        private CombatPresentationPhase presentationPhase = CombatPresentationPhase.None;
        private RuntimeInspectorSettingsSnapshot appliedRuntimeInspectorSettings;
        private HexMapPurpose selectedBoardPurpose = HexMapPurpose.Unspecified;
        private string selectedBoardName = string.Empty;
        private string selectedBoardEvidenceText = string.Empty;
        private HexCoord? clickMoveDebugSelectedCoord;
        private CombatHitStopController hitStopController;
        private bool pauseMenuInputBlocked;
        private bool playerDeathSequenceAwaitingHold;
        private Coroutine playerDeathHitStopRoutine;
        private float combatStartRealtimeSeconds;
        private string lastPlayerDeathSourceText = string.Empty;

        public CombatState State { get; private set; }
        public HexMapData LoadedMap { get; private set; }
        public IReadOnlyDictionary<HexCoord, int> Reachable => reachable;
        public bool IsMoveSelectionActive => selectionState.IsMove;
        public CombatCardKind? SelectedTargetCardKind => selectionState.TargetCardKind;
        public string SelectedTargetCardId => selectionState.TargetCardId;
        public string SelectedTargetCardKey => selectionState.TargetCardKey;
        public string SelectedCardId => selectionState.CardId;
        public string SelectedCardInstanceId => selectionState.CardInstanceId;
        public string SelectedCardKey => selectionState.CardKey;
        public string SelectedCardName => selectionState.CardName;
        public ChoiceCardPanelModel PendingChoicePanel => isChoicePanelVisible
            ? pendingChoicePanel
            : new ChoiceCardPanelModel(string.Empty, System.Array.Empty<ChoiceCardOptionModel>());
        public HandCardSelectionPanelModel HandCardSelectionPanel => isHandCardSelectionActive && !isHandCardSelectionAwaitingTarget
            ? new HandCardSelectionPanelModel(
                true,
                handCardSelectionSourceCardKey,
                handCardSelectionPromptText,
                handCardSelectionMinCount,
                handCardSelectionMaxCount,
                handCardSelectionSelectedKeys)
            : HandCardSelectionPanelModel.Empty;
        public bool IsAttackSelectionActive => SelectedTargetCardKind == CombatCardKind.Attack;

        /// <summary>
        /// 카드 수치 미리보기가 대상 의존분(허점·표식)을 합류시킬 칸 — <b>공격 카드를 들고 겨누는
        /// 중일 때만</b> 값이 있다(D-7: "카드 발동 단계에서 해당 대상을 타겟팅한 경우").
        /// <para>저장하지 않고 매번 유도하는 이유: 선택을 취소하면 마우스가 그대로 있어도 즉시
        /// null이 되어야 한다. 값을 들고 있으면 취소 경로마다 지우는 걸 잊는 순간 수치가 부풀어 남는다.</para>
        /// </summary>
        public HexCoord? CardPreviewTargetCoord => IsAttackSelectionActive ? lastHoveredCoord : null;
        public bool IsScoutSelectionActive => SelectedTargetCardKind == CombatCardKind.Scout;
        public bool IsInvestigateSelectionActive => SelectedTargetCardKind == CombatCardKind.Investigate;
        public string StatusText { get; private set; } = "Uninitialized";
        public string LastInputMessage { get; private set; } = "Press Play Move to show reachable M1 map cells.";
        public string LastTargetInfoText { get; private set; } = string.Empty;
        public GameObject EnemyMarker => actorMarkerPresenter.Marker;
        public int EnemyMarkerCount => actorMarkerPresenter.MarkerCount;
        public CharacterActorVisual EnemyActorVisual => actorMarkerPresenter.ActorVisual;
        public string EnemyIntentDisplayText => hudPresenter.FormatEnemyIntentDisplayText(State);
        public string MonsterStatusText => hudPresenter.FormatMonsterStatusText(State);
        public string ObjectiveStatusText => State == null ? string.Empty : State.ObjectiveStatusText;
        public string DeckStatusText => hudPresenter.FormatDeckStatusText(State);
        public HexMapPurpose SelectedBoardPurpose => selectedBoardPurpose;
        public string SelectedBoardName => selectedBoardName;
        public string BoardSelectionEvidenceText => selectedBoardEvidenceText;
        public bool RevealAllMapCellsInDebugMode => revealAllMapCellsInDebugMode;
        public bool IsFogDebugVisible => !revealAllMapCellsInDebugMode;
        public bool ShowOverlayDebugControls => showOverlayDebugControls;
        public bool ShowPlayerMovementOverlay => showPlayerMovementOverlay;
        public bool ShowPlayerActionOverlay => showPlayerActionOverlay;
        public bool ShowMonsterMoveOverlay => showMonsterMoveOverlay;
        public bool ShowMonsterAttackOverlay => showMonsterAttackOverlay;
        public bool ShowMonsterChaseOverlay => showMonsterChaseOverlay;
        public bool IsInitialized => State != null;
        public bool IsGameStartCompleted => gameStartCompleted;
        public bool ShowMonsterIntentArrows => showMonsterIntentArrows;
        public bool IsClickMoveDebugModeEnabled => clickMoveDebugModeEnabled;
        public bool HasClickMoveDebugSelection => clickMoveDebugSelectedCoord.HasValue;
        public HexCoord ClickMoveDebugSelectedCoord => clickMoveDebugSelectedCoord.GetValueOrDefault();
        public TutorialDirector TutorialDirector => ResolveTutorialDirector();
        public CombatOverlayRendererBackend ConfiguredOverlayRendererBackend => overlayRendererBackend;
        public CombatOverlayRendererBackend ActiveOverlayRendererBackend => EnsureOverlayPresenter().OverlayRenderer != null ? activeOverlayRendererBackend : ResolveOverlayRendererBackend();
        public string OverlayRendererStatusText => FormatOverlayRendererStatus();
        public bool IsSequencePlaying => isSequencePlaying;
        public CombatPresentationPhase PresentationPhase => presentationPhase;
        public int PresentationSequenceVersion => presentationSequenceVersion;
        public int StateGeneration => stateGeneration;
        public CombatCameraProfile CameraProfile => cameraProfile;
        public bool ShouldSuppressVictoryPhaseAudio => AudioHost.ShouldSuppressVictoryPhaseAudio;
        public bool KeepCameraCenteredOnPlayer => keepCameraCenteredOnPlayer;
        public Vector3 CameraPlayerOffset => cameraPlayerOffset;
        public bool AutoCalculateCameraPlayerOffset => autoCalculateCameraPlayerOffset;
        public float CameraFollowDistance => cameraFollowDistance;
        public Vector2 CameraFramingOffset => cameraFramingOffset;
        public Vector3 CameraEulerAngles => cameraEulerAngles;
        public float CameraOrthographicSize => cameraOrthographicSize;
        public float MinCameraOrthographicSize => minCameraOrthographicSize;
        public float MaxCameraOrthographicSize => maxCameraOrthographicSize;
        public event System.Action<string, string> AudioCueRequested
        {
            add => AudioHost.AudioCueRequested += value;
            remove => AudioHost.AudioCueRequested -= value;
        }
        public event System.Action<string, string, string> MonsterDeathAudioRequested
        {
            add => AudioHost.MonsterDeathAudioRequested += value;
            remove => AudioHost.MonsterDeathAudioRequested -= value;
        }
        public event System.Action PlayerDeathSequenceCompleted;

        public HexVisibilitySafeCellInfo GetVisibilitySafeCellInfo(HexCoord coord)
        {
            return GetViewVisibilitySafeCellInfo(coord);
        }

        public string GetVisibilitySafeTooltipText(HexCoord coord)
        {
            return visibilityPresenter.GetTooltipText(GetVisibilitySafeCellInfo(coord));
        }

        /// <summary>
        /// Project a hex coord to its world position on the live map view (top surface of the tile).
        /// Returns false when no 3D map view is available. Use this so presentation layers place
        /// effects exactly where the tile renders instead of recomputing hex-to-world independently.
        /// </summary>
        public bool TryGetTileWorldPosition(HexCoord coord, out Vector3 worldPosition)
        {
            if (atlasTilePresentationView != null)
            {
                worldPosition = atlasTilePresentationView.transform.TransformPoint(atlasTilePresentationView.ProjectOverlaySurface(coord));
                return true;
            }

            worldPosition = default;
            return false;
        }

        /// <summary>Project the player's current hex to its world position on the live map view.</summary>
        public bool TryGetPlayerWorldPosition(out Vector3 worldPosition)
        {
            if (State != null && TryGetTileWorldPosition(State.PlayerCoord, out worldPosition))
            {
                return true;
            }

            worldPosition = default;
            return false;
        }

        /// <summary>Resolve the rendered player's visible bounds center when a 3D marker is available.</summary>
        public bool TryGetPlayerVisualBoundsCenterWorldPosition(out Vector3 worldPosition)
        {
            if (atlasTilePresentationView != null &&
                atlasTilePresentationView.TryGetPlayerMarkerRendererBoundsWorldCenter(out worldPosition))
            {
                return true;
            }

            worldPosition = default;
            return false;
        }

        /// <summary>
        /// Resolve the current rendered actor anchor for a combatant when available. This is more
        /// precise than the tile center for hit/status VFX because it follows animated player and
        /// monster markers during presentation sequences.
        /// </summary>
        public bool TryGetCombatantEffectAnchorWorldPosition(string targetUnitId, out Vector3 worldPosition)
        {
            return TryGetCombatantVfxAnchorWorldPosition(targetUnitId, CharacterVfxAnchorKind.Root, out worldPosition);
        }

        /// <summary>
        /// Transform variant of <see cref="TryGetCombatantVfxAnchorWorldPosition"/> for followSourceAnchor
        /// cues that must keep tracking the anchor after spawn. Unlike the position variant there is no
        /// tile-center fallback — without a live actor anchor there is nothing to follow.
        /// </summary>
        public bool TryGetCombatantVfxAnchor(
            string targetUnitId,
            CharacterVfxAnchorKind anchorKind,
            out Transform anchor)
        {
            anchor = null;
            if (State == null || string.IsNullOrWhiteSpace(targetUnitId))
            {
                return false;
            }

            if (string.Equals(targetUnitId, State.Player.Id, System.StringComparison.Ordinal) ||
                string.Equals(targetUnitId, "player", System.StringComparison.OrdinalIgnoreCase))
            {
                return atlasTilePresentationView != null &&
                       atlasTilePresentationView.TryGetPlayerVfxAnchor(anchorKind, out anchor);
            }

            return actorMarkerPresenter.TryGetMarkerVfxAnchor(targetUnitId, anchorKind, out anchor);
        }

        /// <summary>
        /// 전투 유닛 모델의 발자국 지름(월드 단위, 2026-08-20 WS-2). 상태이상 바닥 링의 비례 스케일이
        /// 이 값을 쓴다 — 플레이어의 값이 기준 체구(계수 1)다. 앵커 조회와 같은 플레이어/몬스터 분기를
        /// 탄다. 잴 수 없으면 <see langword="false"/>(호출자는 계수 1 = 현행 크기로 물러난다).
        /// </summary>
        public bool TryGetCombatantFootprintDiameter(string targetUnitId, out float diameter)
        {
            diameter = 0f;
            if (State == null || string.IsNullOrWhiteSpace(targetUnitId))
            {
                return false;
            }

            if (string.Equals(targetUnitId, State.Player.Id, System.StringComparison.Ordinal) ||
                string.Equals(targetUnitId, "player", System.StringComparison.OrdinalIgnoreCase))
            {
                return atlasTilePresentationView != null &&
                       atlasTilePresentationView.TryGetPlayerFootprintDiameter(out diameter);
            }

            return actorMarkerPresenter.TryGetMarkerFootprintDiameter(targetUnitId, out diameter);
        }

        /// <summary>
        /// 이 유닛의 몸이 <b>지금 보고 있는</b> 방향(월드). 몬스터 마커만 답한다 —
        /// 플레이어 몸은 다른 표면(아틀라스 뷰)이 쥐고 있고, 공격 VFX 회전이 필요한 쪽은 몬스터다.
        /// 2026-09-02 #4의 정본 표면.
        /// </summary>
        public bool TryGetCombatantFacingDirection(string unitId, out Vector3 worldForward)
        {
            worldForward = default;
            if (State == null || string.IsNullOrWhiteSpace(unitId) || actorMarkerPresenter == null)
            {
                return false;
            }

            if (string.Equals(unitId, State.Player.Id, System.StringComparison.Ordinal) ||
                string.Equals(unitId, "player", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return actorMarkerPresenter.TryGetFacingDirection(unitId, out worldForward);
        }

        public bool TryGetCombatantVfxAnchorWorldPosition(
            string targetUnitId,
            CharacterVfxAnchorKind anchorKind,
            out Vector3 worldPosition)
        {
            if (State == null || string.IsNullOrWhiteSpace(targetUnitId))
            {
                worldPosition = default;
                return false;
            }

            if (string.Equals(targetUnitId, State.Player.Id, System.StringComparison.Ordinal) ||
                string.Equals(targetUnitId, "player", System.StringComparison.OrdinalIgnoreCase))
            {
                if (atlasTilePresentationView != null &&
                    atlasTilePresentationView.TryGetPlayerVfxAnchorWorldPosition(anchorKind, out worldPosition))
                {
                    return true;
                }

                return TryGetPlayerWorldPosition(out worldPosition);
            }

            if (actorMarkerPresenter.TryGetMarkerVfxAnchorWorldPosition(targetUnitId, anchorKind, out worldPosition))
            {
                return true;
            }

            foreach (var monster in State.Monsters)
            {
                if (!string.Equals(monster.Id, targetUnitId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryGetTileWorldPosition(monster.Coord, out worldPosition))
                {
                    worldPosition += Vector3.up * enemyMarkerHeightOffset;
                    return true;
                }

                break;
            }

            worldPosition = default;
            return false;
        }

        private void OnEnable()
        {
            if (initializeOnStart && Application.isPlaying && State == null && HasAutoInitializeSource())
            {
                InitializeIntegration();
            }
        }

        private bool HasAutoInitializeSource()
        {
            return configuredMap != null
                || sparseSource != null
                || useExplicitTestReferences
                || atlasTilePresentationView != null;
        }

        private void Awake()
        {
            MigrateLegacyMemoryStoneVictoryCameraDefaults();
            MigrateLegacyMemoryStoneVictoryCameraFramingDefaults();
            foreach (var loader in FindObjectsOfType<HexSparseMapSourceLoader>())
                loader.enabled = false;
        }

        private void Start()
        {
            if (initializeOnStart && State == null)
            {
                InitializeIntegration();
            }
        }

        private void OnDisable()
        {
            CancelActivePresentationSequence(commitCurrentState: false);
            StopPlayerDeathHitStopRoutine();
            RestorePlayerDeathSlowMotion();
            RestorePlayerDeathCameraZoom();
            Cinematics.TeardownCinematicsOnDisable();
            trailerFilmingActive = false;
            stateGeneration++;
            UnsubscribeInput();
            cameraController?.UnsubscribeEffectShake();
            cardHudHostAdapter?.UnsubscribeTurnPhase();
            cardHudHostAdapter?.UnsubscribeBossPhase();
        }

        private void OnDestroy()
        {
            if (tutorialDirector != null && tutorialDirectorStepHooked)
            {
                tutorialDirector.StepPresented -= OnTutorialStepPresented;
                tutorialDirectorStepHooked = false;
            }

            if (cardRewardPopupView != null && CardRewardPresentation.transform.parent == null)
            {
                if (Application.isPlaying)
                {
                    Destroy(CardRewardPresentation.gameObject);
                }
                else
                {
                    DestroyImmediate(CardRewardPresentation.gameObject);
                }
            }
            foreach (var go in monsterArrowOverlays.Values)
            {
                if (go != null)
                {
                    if (Application.isPlaying) Destroy(go);
                    else DestroyImmediate(go);
                }
            }
            monsterArrowOverlays.Clear();
            if (arrowMesh != null) { Destroy(arrowMesh); arrowMesh = null; }
            if (arrowMaterialAttack != null) { Destroy(arrowMaterialAttack); arrowMaterialAttack = null; }
            if (arrowMaterialChase != null) { Destroy(arrowMaterialChase); arrowMaterialChase = null; }
            cameraController?.UnsubscribeEffectShake();
            cardHudHostAdapter?.UnsubscribeTurnPhase();
            cardHudHostAdapter?.UnsubscribeBossPhase();
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(playerCombatProfileId))
            {
                playerCombatProfileId = PlayerCombatProfileCatalog.DefaultProfileId;
            }

            minCameraOrthographicSize = Mathf.Max(1f, minCameraOrthographicSize);
            maxCameraOrthographicSize = Mathf.Max(minCameraOrthographicSize, maxCameraOrthographicSize);
            cameraOrthographicSize = Mathf.Clamp(cameraOrthographicSize, minCameraOrthographicSize, maxCameraOrthographicSize);
            cameraFollowDistance = Mathf.Max(0.01f, cameraFollowDistance);
            cameraFollowSmoothing = Mathf.Max(0f, cameraFollowSmoothing);
            cameraShakeDuration = Mathf.Max(0f, cameraShakeDuration);
            cameraShakeVibrato = Mathf.Max(1, cameraShakeVibrato);
            cameraShakeRandomness = Mathf.Clamp(cameraShakeRandomness, 0f, 180f);
            cameraShakeDamageScale = Mathf.Max(0f, cameraShakeDamageScale);
            cameraShakeMaxMultiplier = Mathf.Max(1f, cameraShakeMaxMultiplier);
            MigrateLegacyMemoryStoneVictoryCameraFramingDefaults();
            memoryStoneVictoryCameraHeight = Mathf.Max(1f, memoryStoneVictoryCameraHeight);
            memoryStoneVictoryCameraOrthographicSize = Mathf.Max(1f, memoryStoneVictoryCameraOrthographicSize);
            MigrateLegacyMemoryStoneVictoryCameraDefaults();
            memoryStoneVictoryCameraPitchDegrees = Mathf.Clamp(memoryStoneVictoryCameraPitchDegrees, 5f, 85f);
            memoryStoneVictoryCameraYawDegrees = Mathf.Repeat(memoryStoneVictoryCameraYawDegrees, 360f);
            memoryStoneVictoryCameraFocusLag = Mathf.Clamp01(memoryStoneVictoryCameraFocusLag);
            memoryStoneVictoryLateralSpreadPercent = Mathf.Max(0, memoryStoneVictoryLateralSpreadPercent);
            memoryStoneVictorySweepSpeed = Mathf.Max(0.01f, memoryStoneVictorySweepSpeed);
            memoryStoneVictoryRevealLeadDistance = Mathf.Max(0f, memoryStoneVictoryRevealLeadDistance);
            memoryStoneVictoryCornerRadius = Mathf.Max(0f, memoryStoneVictoryCornerRadius);
            memoryStoneVictoryCorneringLookAheadDistance = Mathf.Max(0.01f, memoryStoneVictoryCorneringLookAheadDistance);
            memoryStoneVictoryYawSmoothingSeconds = Mathf.Max(0f, memoryStoneVictoryYawSmoothingSeconds);
            memoryStoneVictoryEndPitchDegrees = Mathf.Clamp(memoryStoneVictoryEndPitchDegrees, 5f, 89f);
            memoryStoneVictoryEndLookAtHeight = Mathf.Max(0f, memoryStoneVictoryEndLookAtHeight);
            memoryStoneVictoryEndZoomDistance = Mathf.Max(0.5f, memoryStoneVictoryEndZoomDistance);
            memoryStoneVictoryEndOrbitDistance = Mathf.Max(memoryStoneVictoryEndZoomDistance, memoryStoneVictoryEndOrbitDistance);
            memoryStoneVictoryEndOrbitSeconds = Mathf.Max(0.01f, memoryStoneVictoryEndOrbitSeconds);
            memoryStoneVictoryEndSettleSeconds = Mathf.Max(0f, memoryStoneVictoryEndSettleSeconds);
            memoryStoneVictorySideViewSeconds = Mathf.Max(0f, memoryStoneVictorySideViewSeconds);
            memoryStoneVictorySideViewSpeed = Mathf.Max(0f, memoryStoneVictorySideViewSpeed);
            memoryStoneVictorySideViewMinSeconds =
                Mathf.Clamp(memoryStoneVictorySideViewMinSeconds, 0f, memoryStoneVictorySideViewSeconds);
            memoryStoneVictorySideViewMaxLateralExtentFraction =
                Mathf.Max(0f, memoryStoneVictorySideViewMaxLateralExtentFraction);
            memoryStoneVictorySideViewHeight = Mathf.Max(0.5f, memoryStoneVictorySideViewHeight);
            memoryStoneVictorySideViewStartProgress = Mathf.Clamp01(memoryStoneVictorySideViewStartProgress);
            memoryStoneVictorySideViewEndProgress = Mathf.Clamp01(memoryStoneVictorySideViewEndProgress);
            memoryStoneVictoryEndBackdropMinDistance = Mathf.Max(0f, memoryStoneVictoryEndBackdropMinDistance);
            memoryStoneVictoryEndBackdropMaxDistance =
                Mathf.Max(memoryStoneVictoryEndBackdropMinDistance, memoryStoneVictoryEndBackdropMaxDistance);
            memoryStoneVictoryFinalHoldSeconds = Mathf.Max(0f, memoryStoneVictoryFinalHoldSeconds);
            memoryStoneVictoryFireworkShakeStrength = Mathf.Max(0f, memoryStoneVictoryFireworkShakeStrength);
            memoryStoneVictoryFireworkBurstCount = Mathf.Max(1, memoryStoneVictoryFireworkBurstCount);
            memoryStoneVictoryFireworkBurstInterval = Mathf.Max(0f, memoryStoneVictoryFireworkBurstInterval);
            memoryStoneVictoryFireworkScatterRadius = Mathf.Max(0f, memoryStoneVictoryFireworkScatterRadius);
            movementHandSize = Mathf.Max(1, movementHandSize);
            actionHandSize = Mathf.Max(1, actionHandSize);
            playerMoveSeconds = Mathf.Max(0f, playerMoveSeconds);
            enemyMoveStartDelay = Mathf.Max(0f, enemyMoveStartDelay);
            enemyMoveSeconds = Mathf.Max(0f, enemyMoveSeconds);
            attackWindupDelay = Mathf.Max(0f, attackWindupDelay);
            attackImpactDelay = Mathf.Max(0f, attackImpactDelay);
            deathDelay = Mathf.Max(0f, deathDelay);
        }

        private void MigrateLegacyMemoryStoneVictoryCameraDefaults()
        {
            if (Mathf.Approximately(memoryStoneVictoryCameraPitchDegrees, LegacyMemoryStoneVictoryCameraPitchDegrees) &&
                Mathf.Approximately(Mathf.Repeat(memoryStoneVictoryCameraYawDegrees, 360f), LegacyMemoryStoneVictoryCameraYawDegrees))
            {
                memoryStoneVictoryCameraPitchDegrees = DefaultMemoryStoneVictoryCameraPitchDegrees;
                memoryStoneVictoryCameraYawDegrees = DefaultMemoryStoneVictoryCameraYawDegrees;
            }
        }

        private void MigrateLegacyMemoryStoneVictoryCameraFramingDefaults()
        {
            if (Mathf.Approximately(memoryStoneVictoryCameraHeight, LegacyMemoryStoneVictoryCameraHeight) &&
                Mathf.Approximately(memoryStoneVictoryCameraOrthographicSize, LegacyMemoryStoneVictoryCameraOrthographicSize))
            {
                memoryStoneVictoryCameraHeight = DefaultMemoryStoneVictoryCameraHeight;
                memoryStoneVictoryCameraOrthographicSize = DefaultMemoryStoneVictoryCameraOrthographicSize;
            }
        }

        private void Update()
        {
            CameraController.EnsureEffectShakeSubscription(State);
            HudHost.EnsureTurnPhaseSubscription();
            HudHost.EnsureBossHud();
            HandleEndActionShortcutInput();
            HandleCardCancelShortcutInput();
            RefreshMapObjectHover();
            if (inputController == null && CombatInputRouter.WasPrimaryClickPressed(out var mousePosition))
            {
                if (CanAcceptPlayerInput(out _) && !IsPointerOverDebugPanel(mousePosition))
                {
                    TryMoveFromScreenPosition(mousePosition);
                }
            }
        }

        private void HandleEndActionShortcutInput()
        {
            if (ShouldIgnoreGameplayShortcutInputForPhaseEnd())
            {
                return;
            }

            if (CombatInputRouter.WasEndActionShortcutPressed())
            {
                EndAction();
            }
        }

        private void HandleCardCancelShortcutInput()
        {
            if ((!HasCancelableCardSelection() && !HasPendingBagItemTargeting) || CombatInputRouter.IsTextInputFocused())
            {
                return;
            }

            if (CombatInputRouter.WasCardCancelShortcutPressed())
            {
                CancelSelectedCard();
                CancelBagItemTargeting();
            }
        }

        private void LateUpdate()
        {
            ApplyRuntimeInspectorSettingsIfChanged();
            if (IsCinematicViewActive)
            {
                // During the intro cinematic (or a trailer take) keep every map object opaque and suppress
                // all hover so the sweep is not interrupted by occlusion fade or hover tooltips.
                SuppressHoverDuringStageIntro();
            }
            else if (pauseMenuInputBlocked)
            {
                // Paused: freeze hover state and drop any tooltip that was open when the pause
                // overlay came up, so nothing hover-driven renders behind the dim.
                HideAllHoverTooltips();
            }
            else
            {
                RefreshMapObjectFade();
                // 배지 판정이 먼저다 — 배지는 몬스터 본체 위에 겹쳐 뜨므로 뒤에 돌면 진다.
                UpdateNameplateBadgeHover();
                UpdateMonsterTooltip();
                UpdateStatusIconOverlayHover();
                UpdateFieldObjectHover();
                UpdateObjectInfoHover();
            }

            UpdateMonsterFacing();
            UpdateMonsterMovePaths();
        }

        // True while any cinematic owns the view: the stage intro, the memory-stone victory presentation,
        // a trailer take driven by TrailerShotRunner, or the boss phase-transition beat. All need the same
        // suppressions (occlusion fade off so buildings stay opaque, no map-object hover, no tooltips, no
        // gameplay overlays) — without it a stray mouse position leaves a tooltip and a half-transparent
        // building in the recorded plate, and the victory diorama keeps drawing reachability/intent overlays
        // over the cleared city.
        internal bool IsCinematicViewActive =>
            Cinematics.IsStageIntroActive || Cinematics.IsMemoryStoneVictoryActive || trailerFilmingActive
            || bossPhaseTransitionViewActive || bossEncounterViewActive;

        /// <summary>
        /// Time step the cinematics advance by. Normally <see cref="Time.unscaledDeltaTime"/> so a hit-stop
        /// or a paused timeScale can never stretch a cinematic. Under
        /// <see cref="DebugSetCinematicCaptureTimeSource"/> it switches to <see cref="Time.deltaTime"/>.
        /// </summary>
        /// <remarks>
        /// Why the switch exists: fixed-rate frame capture works by setting <see cref="Time.captureDeltaTime"/>,
        /// which pins deltaTime to 1/fps — but NOT unscaledDeltaTime, which keeps reporting real elapsed time.
        /// So an unscaled-timed cinematic advances at wall-clock speed while the recorder writes frames far
        /// slower than realtime, and the resulting footage is time-compressed (measured: a 5.0s take came out
        /// as 48 frames = 0.8s). Recording therefore has to move the cinematics onto the captured clock.
        /// Gameplay is untouched: the flag is only ever set by the dev capture tooling.
        /// </remarks>
        internal float CinematicDeltaTime =>
            cinematicUsesCaptureTimeSource ? Time.deltaTime : Time.unscaledDeltaTime;

        /// <summary>
        /// Realtime step for presentation holds that must not stretch with a slow-motion timeScale (the
        /// player-death hold, hit-stop durations). Normally <see cref="Time.unscaledDeltaTime"/>; under
        /// <see cref="DebugSetCinematicCaptureTimeSource"/> it advances by <see cref="Time.captureDeltaTime"/>
        /// per frame instead.
        /// </summary>
        /// <remarks>
        /// <see cref="CinematicDeltaTime"/> cannot serve here: under capture it is <see cref="Time.deltaTime"/>,
        /// which the death slow-motion scales down — a "2s realtime" hold would stretch to 2s/timeScale of
        /// footage. The unscaled captured step is the configured capture rate itself, regardless of timeScale.
        /// </remarks>
        internal float CinematicUnscaledDeltaTime =>
            cinematicUsesCaptureTimeSource && Time.captureDeltaTime > 0f
                ? Time.captureDeltaTime
                : Time.unscaledDeltaTime;

        /// <summary>
        /// Dev-only: moves the cinematics off unscaled time onto <see cref="Time.deltaTime"/> so fixed-rate
        /// frame capture (<see cref="Time.captureDeltaTime"/>) yields exactly the authored shot durations.
        /// </summary>
        public void DebugSetCinematicCaptureTimeSource(bool useCapturedTime)
        {
            cinematicUsesCaptureTimeSource = useCapturedTime;
        }

        /// <summary>
        /// Dev-only: makes a trailer take borrow the stage intro's hover/occlusion suppression.
        /// Set by <c>TrailerShotRunner.EnterTrailerFilmingState</c> and cleared on exit.
        /// </summary>
        public void DebugSetTrailerFilmingActive(bool active)
        {
            trailerFilmingActive = active;
            if (active)
            {
                SuppressHoverDuringStageIntro();
            }
        }

        private void SuppressHoverDuringStageIntro()
        {
            atlasTilePresentationView?.RefreshMapObjectFade(
                prototype3DCamera,
                System.Array.Empty<AtlasTilePresentationView.MapObjectOcclusionTarget>());
            atlasTilePresentationView?.ClearMapObjectHover();
            HideAllHoverTooltips();
        }

        private void HideAllHoverTooltips()
        {
            tooltipPresenter?.Hide();
            fieldObjectTooltipPresenter?.Hide();
            objectInfoTooltipPresenter?.Hide();
            SetHoveredMonster(null);
        }

        /// <summary>
        /// Editor/development-only hook for Dev scenes that need live camera tuning.
        /// Calls are omitted from non-development player builds.
        /// </summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        public void ApplyGameplayCameraSettingsForDev(
            Vector3 playerOffset,
            Vector3 eulerAngles,
            float orthographicSize,
            bool keepCenteredOnPlayer)
        {
            if (!autoCalculateCameraPlayerOffset)
            {
                cameraPlayerOffset = playerOffset;
            }

            cameraEulerAngles = eulerAngles;
            cameraOrthographicSize = Mathf.Clamp(
                orthographicSize,
                Mathf.Max(1f, minCameraOrthographicSize),
                Mathf.Max(minCameraOrthographicSize, maxCameraOrthographicSize));
            keepCameraCenteredOnPlayer = keepCenteredOnPlayer;
            RememberAppliedRuntimeInspectorSettings();
        }

        /// <summary>
        /// Applies a reusable scene camera profile without requiring the dev-only tuning panel.
        /// </summary>
        public void ApplyCameraProfileForScene(CombatCameraProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            cameraProfile = profile;
            ApplyCameraProfile(profile);
            RememberAppliedRuntimeInspectorSettings();
        }

        public void InitializeIntegration()
        {
            CancelActivePresentationSequence(commitCurrentState: false);
            stateGeneration++;
            UnsubscribeInput();
            ResolveSceneReferences();
            ApplyCameraProfileIfAssigned();
            ClearBoardSelectionEvidence();
            // 표시 배율 래치는 전투 인스턴스에 묶인다(§14.2) — 비우지 않으면 리셋한 전투의 1페이즈 보스가
            // 이전 판의 3페이즈 크기를 물려받는다.
            bossVisualScaleDisplayed.Clear();
            ResetRewardStateForNewCombat();
            lastPlayerDeathSourceText = string.Empty;
            combatStartRealtimeSeconds = Time.realtimeSinceStartup;
            cardRewardPopupView?.Hide();
            HideGameOverOverlay();
            HideGameVictoryOverlay();
            LoadedMap = LoadMap();
            if (LoadedMap == null || LoadedMap.Count == 0)
            {
                State = null;
                StatusText = "M1 map unavailable";
                ClearSelection();
                reachable = new Dictionary<HexCoord, int>();
                LastTargetInfoText = string.Empty;
                LastInputMessage = "M1 map combat integration requires a configured map, board source, or non-empty tilemap loader.";
                EnsureCardRewardPopupView();
                EnsureGameOverOverlayRoot();
                EnsureGameVictoryOverlayRoot();
                EnsureDebugControlPanel();
                actorMarkerPresenter.ShowMonsterMarkers(
                    Enumerable.Empty<CombatActorMarkerPresenter.MonsterMarkerState>(),
                    GetSelected3DViewTransform(),
                    transform,
                    enemyMarkerPrefab,
                    enemyMarkerColor,
                    enemyMarkerRadius,
                    enemyVisualLocalOffset,
                    enemyVisualLocalEulerAngles,
                    enemyVisualLocalScale);

                RememberAppliedRuntimeInspectorSettings();
                return;
            }

            var (playerCoord, _) = ResolveStartingCoords(LoadedMap);
            var config = CreateCombatConfig();
            // Publish the CSV-authored status-effect defaults before anything reads them. Monster pattern
            // amounts resolve at catalog-parse time (MonsterCatalogCsv.ResolvePatternStatusEffectAmount),
            // so this has to run ahead of CreateMonsterCatalog — not just ahead of the CombatState ctor.
            if (catalogTextAssetSource != null && catalogTextAssetSource.HasStatusEffectCatalog)
            {
                StatusEffectCatalogProvider.Active = catalogTextAssetSource.CreateStatusEffectCatalog();
            }

            // 특성 배선 표(2026-09-04). 배지 조립·툴팁·호버 오버레이가 전부 이 한 벌을 읽으므로
            // 몬스터 카탈로그보다 먼저 심는다. 미할당은 조용히 넘어가면 배지가 통째로 사라지는
            // 종류의 사고라 경고를 남긴다 — 씬 배선 누락이 빌드에서만 드러난 전례가 있다.
            if (catalogTextAssetSource != null && catalogTextAssetSource.HasMonsterTraitCatalog)
            {
                MonsterTraitCatalogProvider.Active = catalogTextAssetSource.CreateMonsterTraitCatalog();
            }
            else if (catalogTextAssetSource != null)
            {
                Debug.LogWarning(
                    "CombatCatalogTextAssetSource에 monster_traits.csv가 할당되지 않았다 — 특성 배지·툴팁·범위 오버레이가 뜨지 않는다.",
                    this);
            }

            var monsterCatalog = CreateMonsterCatalog(config);
            // Monsters come exclusively from the board's spawn refs. When a map has no configured
            // spawner, no monsters spawn (previously a hardcoded ThreeEyeDog "manual-test-monster"
            // was injected here, which made monsters appear in scenes that authored none).
            // 스폰 시 체력 변주(hpVariancePct): 배치 랜덤화와 같은 시드 규율 — placementSeed의 파생
            // 스트림(3; 0=몬스터 배치는 원시 시드, 1=함정, 2=상자)이라 같은 시드 = 같은 체력이고
            // 배치 추첨과 서로 소비량을 흔들지 않는다. 시드가 없으면(랜덤화 off·구세이브) 고정 체력.
            var hpVarianceSeed = placementRandomizationEnabled && hasPlacementSeed
                ? (int?)RunSeedStreams.Derive(placementSeed, RunSeedStreams.SpawnHpVariance)
                : null;
            var monsterConfigs = LoadedMap.MonsterSpawnRefs.Count > 0
                ? CombatState.ResolveMonsterConfigsFromBoardSpawns(LoadedMap, config, monsterCatalog, hpVarianceSeed)
                : System.Array.Empty<MonsterConfig>();
            var cardCatalog = cardCatalogAsset != null
                ? cardCatalogAsset.ToCardCatalogDefinition(config)
                : CombatState.CreateCardCatalog(config);
            if (cardCatalogAsset == null)
            {
                Debug.LogWarning("MapCombatController has no CSV CardCatalogAsset assigned; using runtime fallback catalog.", this);
            }

            // Inject the CSV-authored relic/curse catalog before inventory resolution so player builds
            // (where Assets/ CSVs are not on disk) resolve permanent-item effects. In the editor the
            // catalog also lazy-loads from the source CSV, so this Register just keeps the two paths aligned.
            if (catalogTextAssetSource != null && catalogTextAssetSource.HasRelicCatalog)
            {
                PlayerPermanentItemCatalog.Register(catalogTextAssetSource.CreateRelicCatalog());
            }

            // 소모품 카탈로그(T4-1)도 같은 이유·같은 시점에 주입한다 — CSV만 고치면 빌드에 안 붙는다.
            if (catalogTextAssetSource != null && catalogTextAssetSource.HasConsumableItemCatalog)
            {
                ConsumableItemCatalog.Register(catalogTextAssetSource.CreateConsumableItemCatalog());
            }

            // 오브젝트 도감 저작(P6)도 같은 자리에서 주입한다.
            // 🔴 로비의 BuildCodexDomains에도 같은 Register가 있지만 그것으로는 모자란다 —
            //    그쪽은 <b>도감을 열어야</b> 돌기 때문에, 도감을 한 번도 안 연 채로 전투에 들어가면
            //    빌드에서 표가 비고(빌드에는 Assets/ 경로가 없어 CSV 직독 폴백이 죽는다) 오브젝트
            //    해금 신호가 통째로 사라진다. 에디터에서는 폴백이 살아 있어 절대 안 드러나는 종류의
            //    구멍이라, 신호가 나는 자리에서 한 번 더 못 박는다.
            if (catalogTextAssetSource != null && catalogTextAssetSource.HasMapObjectCodexCatalog)
            {
                CodexObjectCatalogSource.Register(catalogTextAssetSource.CreateMapObjectCodexCatalog());
            }

            activeTerrainTraits = terrainPalette != null ? terrainPalette.ToTerrainTraits() : HexTerrainTraits.Default;
            // Shuffle the opening deck only in actual play sessions so every play differs; EditMode
            // scene tests (which also call InitializeIntegration) keep a deterministic deck order.
            var shuffleDecks = Application.isPlaying && !keepStartingDeckOrderInPlayMode;
            State = new CombatState(LoadedMap, playerCoord, monsterConfigs, config, terrainPalette != null ? terrainPalette.ToTerrainTable() : null, cardCatalog, monsterCatalog, activeTerrainTraits, ResolveInitialInventory(), ResolveInitialPlayerDeck(cardCatalog), drawOpeningHands: drawOpeningHandsOnInitialize, shuffleDecks: shuffleDecks, bossCatalog: CreateBossCatalog(), runSeed: hasPlacementSeed ? placementSeed : (int?)null);
            // 보상 난수원은 상태와 같은 런 시드에서 갈라진다(seed-determinism P3). 상태 생성 직후,
            // 어떤 보상 흐름보다 앞서 꽂는다.
            ApplyRewardRandom();
            // 도감 해금 신호를 받을 곳을 붙인다(P3). 붙이는 즉시 한 번 훑으므로 생성자에서 이미
            // 뽑힌 개시 손패도 잡힌다. 서스펜드 복원은 아래에서 상태를 갈아 끼우지만 그 뒤의
            // 첫 페이즈 전이가 다시 훑는다.
            State.CodexSightings = CodexProgressStore.Shared.Progress;
            CombatSuspendRestoreApplied = false;
            if (combatSuspendRestore?.Combat != null)
            {
                // ② full-snapshot resume: rebuild the exact mid-combat state (monsters, decks, effects,
                // visibility, context) captured when the player last left this stage. Supersedes the ①
                // HP-only restore because the suspend payload carries the full player loadout.
                State.RestoreFromSuspend(combatSuspendRestore.Combat);
                foreach (var unresolvedId in State.SuspendRestoreUnresolvedMonsterDefinitionIds)
                {
                    Debug.LogWarning($"Combat suspend restore: monster definition '{unresolvedId}' has no catalog entry; rebuilt with a generic fallback pattern.", this);
                }

                // Consume the payload: null it out so a later re-init on this same scene instance cannot
                // re-apply a stale snapshot over live progress, and flag that restore actually ran so the
                // flow layer deletes the disk slot only on a genuine restore (anti-scum, strict ordering).
                combatSuspendRestore = null;
                CombatSuspendRestoreApplied = true;
            }
            else if (playerRunRestore?.Vitals != null && playerRunRestore.Vitals.Hp > 0)
            {
                State.RestorePlayerRunHp(playerRunRestore.Vitals.Hp);
            }
            gameStartCompleted = drawOpeningHandsOnInitialize;
            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            LastTargetInfoText = string.Empty;
            LastInputMessage = gameStartCompleted
                ? "Press Play Move to show reachable M1 map cells."
                : "\uAC8C\uC784 \uC2DC\uC791 \uC5F0\uCD9C\uC774 \uB05D\uB098\uBA74 \uC870\uC791\uD560 \uC218 \uC788\uC2B5\uB2C8\uB2E4.";

            RenderSelected3DView(LoadedMap);
            atlasTilePresentationView?.ResetPlayerVisualState();
            // Reset the victory-cinematic "buildings only" restriction on a fresh init / restart.
            atlasTilePresentationView?.SetMapObjectVisualsBuildingsOnly(false);
            InvalidateBatchedOverlayGeometry();

            EnsureCardRewardPopupView();
            EnsureGameOverOverlayRoot();
            EnsureGameVictoryOverlayRoot();
            EnsureDebugControlPanel();
            EnsureAudioPresenter();
            DisableLegacyM1MoveControls();
            EnsureEventSystem();
            SubscribeInput();
            EnsureTooltipPresenter();
            RefreshView();
            RecenterGameplayCameraOnPlayer(immediate: true);
            RememberAppliedRuntimeInspectorSettings();
        }

        public void SetAutomaticInitialization(bool enabled)
        {
            initializeOnStart = enabled;
        }

        public void SetDrawOpeningHandsOnInitialize(bool enabled)
        {
            drawOpeningHandsOnInitialize = enabled;
        }

        public void SetKeepStartingDeckOrderInPlayMode(bool enabled)
        {
            keepStartingDeckOrderInPlayMode = enabled;
        }

        // Stage-level switch for the memory-stone victory sweep (the tutorial clears without it).
        public void SetPlayMemoryStoneVictorySequence(bool enabled)
        {
            playMemoryStoneVictorySequence = enabled;
        }

        public void SetPlayStageIntroCinematic(bool enabled)
        {
            playStageIntroCinematic = enabled;
        }

        public void SetStartingDeckOverride(PlayerStartingDeckAsset deckOverride)
        {
            startingDeckOverride = deckOverride;
        }

        /// <summary>
        /// Injects a persisted run loadout (HP, owned decks, inventory) to apply on the next
        /// <see cref="InitializeIntegration"/>. Set by the flow layer's continue path before
        /// initialization; null keeps the authored starting loadout. Position/zone state is not
        /// restored — resuming re-enters the stage from its start with the saved loadout.
        /// </summary>
        public void SetPlayerRunRestore(PlayerRunSaveData restoreData)
        {
            playerRunRestore = restoreData;
        }

        /// <summary>
        /// Injects a ② full-snapshot suspend payload to apply on the next
        /// <see cref="InitializeIntegration"/>. Set by the flow layer's resume path before initialization;
        /// null keeps the authored (or ①-restored) loadout. When set it fully restores the mid-combat
        /// board (monsters, decks, active effects, visibility, turn context), superseding the ① HP restore.
        /// </summary>
        public void SetCombatSuspendRestore(CombatSuspendEnvelope envelope)
        {
            combatSuspendRestore = envelope;
        }

        /// <summary>
        /// 배치 랜덤화 상태 주입(placement-randomization-plan §2-2). 플로우 계층이 스테이지 진입 시
        /// 호출한다: 새 진입이면 새 시드, 재개(①·②)면 세이브의 시드. hasSeed=false는 「랜덤화
        /// 미적용(저작 원본)」— 시드 필드가 없는 구세이브의 해석이다. 시드는 재초기화에도 유지된다.
        /// </summary>
        public void SetPlacementRandomization(bool enabled, bool hasSeed, int seed, string stageId = "")
        {
            placementRandomizationEnabled = enabled;
            hasPlacementSeed = hasSeed;
            placementSeed = seed;
            // 스테이지 id는 P1 프로파일(stage_randomization*.csv) 조회 키다. 빈 값 = P0 슬롯 셔플.
            placementStageId = stageId ?? string.Empty;
        }

        /// <summary>현재 전투의 배치 시드 — 랜덤화가 <b>적용된</b> 경우에만 true(맵 뷰·체력 변주 게이트).</summary>
        public bool TryGetPlacementSeed(out int seed)
        {
            seed = placementSeed;
            return hasPlacementSeed && placementRandomizationEnabled;
        }

        /// <summary>
        /// 현재 런의 시드(seed-determinism-handoff P1). 배치 랜덤화가 꺼진 스테이지에서도 발급되며
        /// 전투 판정·덱 셔플·보상 스트림이 이 값에서 갈라진다. 세이브 봉투 작성자(플로우 계층)가
        /// 읽는다 — 봉투의 <c>PlacementSeed</c> 필드가 곧 런 시드다(포맷 변경 없음).
        /// </summary>
        public bool TryGetRunSeed(out int seed)
        {
            seed = placementSeed;
            return hasPlacementSeed;
        }

        public void ApplySparseSource(HexSparseMapAuthoringSource source)
        {
            if (source == null)
            {
                return;
            }

            sparseSource = source;
            configuredMap = null;
            LoadedMap = null;
            State = null;
        }

        public IEnumerator BeginGameplayStartSequence(float gameStartDwellSeconds = 1.2f)
        {
            if (State == null)
            {
                InitializeIntegration();
            }

            RefreshView();

            // Play the optional stage-intro cinematic while gameStartCompleted is still false, so the turn
            // does not start and all player input stays blocked (CanAcceptPlayerInput gates on the flag)
            // until the camera sweep finishes.
            if (playStageIntroCinematic && Application.isPlaying)
            {
                yield return Cinematics.PlayStageIntroSequence();

                // The stage intro ends by lowering the intro camera's priority, which makes the
                // CinemachineBrain blend back to the gameplay camera over the following frames. That blend is
                // not awaited by the sweep coroutine, so without this wait the dwell + StartPlayerTurn (and the
                // opening-hand deal-in) would fire while the camera is still settling — the turn visibly
                // starting "before the cinematic ends". Poll until the blend finishes so the first turn begins
                // only once the camera has fully returned to the gameplay view. Bounded by a timeout so a
                // missing brain or an instant (Cut) blend can never stall the game start.
                var cameraSettleTimeoutSeconds = 3f;
                while (CameraController.IsCameraBlending && cameraSettleTimeoutSeconds > 0f)
                {
                    cameraSettleTimeoutSeconds -= CinematicDeltaTime;
                    yield return null;
                }
            }

            HudHost.AnnounceGameStartForDock();

            if (gameStartDwellSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(gameStartDwellSeconds);
            }

            State?.StartPlayerTurn();
            gameStartCompleted = true;
            LastInputMessage = "\uC774\uB3D9 \uCE74\uB4DC\uB97C \uC0AC\uC6A9\uD558\uC138\uC694.";
            RefreshView();
            HudHost.AnnounceOverallTurnStartForDock();
            HudHost.AnnouncePlayerTurnStartForDock();
        }

        /// <summary>
        /// True while the pause menu holds the gameplay input gate (P5). Pausing is input-block
        /// only: presentation sequences keep running to completion and Time.timeScale stays owned
        /// by <see cref="CombatHitStopController"/>, so no timeScale save/restore pair can be
        /// clobbered by the pause state.
        /// </summary>
        public bool IsPauseMenuInputBlocked => pauseMenuInputBlocked;

        public void SetPauseMenuInputBlocked(bool blocked)
        {
            pauseMenuInputBlocked = blocked;
        }

        private bool CanAcceptPlayerInput(out string message)
        {
            if (pauseMenuInputBlocked)
            {
                message = "일시정지 중입니다.";
                return false;
            }

            if (!gameStartCompleted || Cinematics.IsStageIntroActive)
            {
                // stageIntroActive is redundant on the normal path (the opening intro runs while
                // gameStartCompleted is still false) but not for DebugReplayStageIntro, which replays the
                // cinematic mid-run: input must stay blocked for the take there too.
                message = "\uAC8C\uC784 \uC2DC\uC791 \uC5F0\uCD9C\uC774 \uB05D\uB098\uBA74 \uC870\uC791\uD560 \uC218 \uC788\uC2B5\uB2C8\uB2E4.";
                return false;
            }

            if (isSequencePlaying)
            {
                message = string.IsNullOrEmpty(resolvingStatusText) ? "Resolving action animation..." : resolvingStatusText;
                return false;
            }

            message = string.Empty;
            return true;
        }

        private bool RejectInputWhileResolving(bool refreshHudOnly = true)
        {
            if (CanAcceptPlayerInput(out var message))
            {
                return false;
            }

            LastInputMessage = message;
            if (refreshHudOnly)
            {
                RefreshHudOnly();
            }

            return true;
        }

        public bool EndAction()
        {
            if (RejectInputWhileResolving())
            {
                return false;
            }

            if (State == null)
            {
                LastInputMessage = "Integration demo is not initialized.";
                RefreshView();
                return false;
            }

            var tutorialButton = State.Phase == CombatPhase.PlayerMovement
                ? TutorialButtonId.EndMovePhase
                : TutorialButtonId.EndTurn;
            if (!CanPressTutorialButton(tutorialButton, out var tutorialFailure))
            {
                ShowTutorialBlockedFeedback(tutorialFailure);
                return false;
            }

            var before = CapturePresentationSnapshot();
            if (!State.EndAction())
            {
                LastInputMessage = State.LastFailureReason;
                RequestInvalidInputAudioCue(LastInputMessage);
                RefreshView();
                return false;
            }

            if (State.Phase == CombatPhase.MonsterMovement)
            {
                RequestAudioCue(AudioCueIds.EnemyTurnBegin, "turn:enemy-movement");
                var playMonsterMovementPresentationSequence = ShouldPlayPresentationSequence();
                State.ResolveMonsterMovement();
                var monsterMovementRecords = State.LastMonsterActionRecords.ToList();
                ClearSelection();
                reachable = new Dictionary<HexCoord, int>();
                LastInputMessage = State.IsTerminal ? "Combat ended. Press Restart." : "Monsters moved. Choose an action.";
                NotifyTutorialButtonPressed(tutorialButton);

                if (playMonsterMovementPresentationSequence)
                {
                    var after = CapturePresentationSnapshot();
                    StartPresentationSequence(
                        "Resolving monster movement...",
                        RunMonsterMovementPhaseTimeline(before, after, monsterMovementRecords, LastInputMessage),
                        refreshHudAtStart: false);
                    return true;
                }

                RefreshView();
                return true;
            }

            if (State.Phase == CombatPhase.PlayerAction)
            {
                RequestAudioCue(AudioCueIds.UiCardSelect, "phase:movement-end");
                LastInputMessage = "Movement phase ended. Choose an action.";
                NotifyTutorialButtonPressed(tutorialButton);
                RefreshView();
                return true;
            }

            RequestAudioCue(AudioCueIds.EnemyTurnBegin, "turn:enemy-begin");
            var beforePlayerDead = State.Player.IsDead;
            var playPresentationSequence = ShouldPlayPresentationSequence();
            var bufferMonsterEffects = playPresentationSequence;
            HudHost.SuppressNextPlayerTurnDockAnnouncement = playPresentationSequence;
            if (bufferMonsterEffects)
            {
                State.BeginEffectBuffering();
                State.BeginDeferredMonsterActionState();
            }

            State.ResolveMonsterAction(drawPlayerTurnHands: !playPresentationSequence);
            if (State.IsTerminal)
            {
                HudHost.SuppressNextPlayerTurnDockAnnouncement = false;
            }

            var monsterActionRecords = State.LastMonsterActionRecords.ToList();
            var playerDied = !beforePlayerDead && State.Player.IsDead;
            ClearSelection();
            reachable = new Dictionary<HexCoord, int>();
            LastInputMessage = State.IsTerminal ? "Combat ended. Press Restart." : "Ended action. Monsters attacked; new turn started.";
            if (playPresentationSequence)
            {
                var after = CapturePresentationSnapshot();
                State.HoldDeferredMonsterActionStateUntilPresentation();

                NotifyTutorialButtonPressed(tutorialButton);
                StartPresentationSequence(
                    "Resolving enemy action...",
                    RunEndActionTimeline(before, after, monsterActionRecords, playerDied, LastInputMessage),
                    refreshHudAtStart: false);
                return true;
            }

            if (bufferMonsterEffects && State != null && State.IsBufferingEffects)
            {
                State.FlushBufferedEffects();
            }

            if (State != null && !State.IsTerminal)
            {
                HudHost.AnnounceOverallTurnStartForDock();
                HudHost.AnnouncePlayerTurnStartForDock();
            }

            NotifyTutorialButtonPressed(tutorialButton);
            RefreshView();
            foreach (var attackRecord in MonsterActionPresentationFilters.GetPresentableMonsterAttackRecords(monsterActionRecords))
            {
                actorMarkerPresenter.TriggerAttack(attackRecord.MonsterId, attackRecord.AttackAnimationTrigger);
            }

            return true;
        }
        private static bool TryRestartViaMainGameplayController()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != "SeoulPlayup.Flow.Unity.MainGameplayController")
                {
                    continue;
                }

                behaviour.SendMessage("RestartStage", SendMessageOptions.DontRequireReceiver);
                return true;
            }

            return false;
        }

        private static bool TryReturnToLobbyViaMainGameplayController()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != "SeoulPlayup.Flow.Unity.MainGameplayController")
                {
                    continue;
                }

                behaviour.SendMessage("ReturnToLobby", SendMessageOptions.DontRequireReceiver);
                return true;
            }

            return false;
        }

        private static bool TryPlayStageOutroViaMainGameplayController()
        {
            var behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (var i = 0; i < behaviours.Length; i++)
            {
                var behaviour = behaviours[i];
                if (behaviour == null || behaviour.GetType().FullName != "SeoulPlayup.Flow.Unity.MainGameplayController")
                {
                    continue;
                }

                behaviour.SendMessage("PlayStageOutroOrAdvance", SendMessageOptions.DontRequireReceiver);
                return true;
            }

            return false;
        }

        // Wired to the victory overlay "다음으로" button. Plays the cleared stage's outro story
        // cutscene (or advances to the next stage) through the flow controller.
        public void ContinueToNextStageViaCutscene()
        {
            hitStopController?.RestoreTimeScale();
            hitStopController?.RestoreAnimationSpeeds();
            RestorePlayerDeathSlowMotion();
            HideGameVictoryOverlay();

            if (TryPlayStageOutroViaMainGameplayController())
            {
                return;
            }

            ReturnToLobbyViaSceneFlow();
        }

        public void ReturnToLobby()
        {
            // 🔴도감 진행도를 디스크에 앉히는 자리. 신호마다 쓰지 않는 이유는 훑기가 페이즈마다
            // 돌기 때문이고(파일 입출력이 턴마다 돈다), 여기서 쓰는 이유는 전투를 떠나는 길이
            // 반드시 이 문을 지나기 때문이다. 실패해도 IsDirty가 남아 다음 기회에 다시 쓴다.
            CodexProgressStore.Shared.SaveIfDirty();
            hitStopController?.RestoreTimeScale();
            hitStopController?.RestoreAnimationSpeeds();
            RestorePlayerDeathSlowMotion();
            if (TryReturnToLobbyViaMainGameplayController())
            {
                return;
            }

            ReturnToLobbyViaSceneFlow();
        }

        // SeoulPlayup.Flow references this assembly, so SceneFlowController cannot be referenced
        // directly. Resolve it reflectively so the lobby scene name stays authored in one place;
        // the literal load remains only as a last resort when the Flow assembly is absent.
        private static void ReturnToLobbyViaSceneFlow()
        {
            var flowType = System.Type.GetType(
                "SeoulPlayup.Flow.Unity.SceneFlowController, SeoulPlayup.Flow");
            var controller = flowType != null
                ? flowType.GetMethod("GetOrCreate")?.Invoke(null, null) as MonoBehaviour
                : null;
            if (controller != null)
            {
                controller.SendMessage("ReturnToLobby", SendMessageOptions.DontRequireReceiver);
                return;
            }

            SceneManager.LoadScene("Lobby");
        }

        public void RestartDemo()
        {
            if (TryRestartViaMainGameplayController())
            {
                return;
            }

            StopPlayerDeathHitStopRoutine();
            hitStopController?.RestoreTimeScale();
            hitStopController?.RestoreAnimationSpeeds();
            RestorePlayerDeathSlowMotion();
            RestorePlayerDeathCameraZoom();
            playerDeathSequenceAwaitingHold = false;
            HideGameOverOverlay();
            HideGameVictoryOverlay();
            Cinematics.RestoreGameplayUiAfterVictory();
            atlasTilePresentationView?.ResetPlayerVisualState();
            InitializeIntegration();
            if (!gameStartCompleted && Application.isPlaying)
            {
                StartCoroutine(BeginGameplayStartSequence(0f));
            }
            audioPresenter?.RestartGameplayAudio();
        }

        public void RequestCardHoverAudio(string cardId)
        {
            AudioHost.RequestCardHoverAudio(cardId);
        }

        private void RequestAudioCue(string cueId, string context)
        {
            AudioHost.RequestAudioCue(cueId, context);
        }

        private void RequestInvalidInputAudioCue(string reason)
        {
            AudioHost.RequestInvalidInputAudioCue(reason);
        }

        // Cinemachine's binder continuously follows the player on its own; this just keeps the
        // change-detection snapshot in sync after camera-affecting settings change.
        private void RecenterGameplayCameraOnPlayer(bool immediate)
        {
            RememberAppliedRuntimeInspectorSettings();
        }

        private void ApplyCameraProfileIfAssigned()
        {
            if (cameraProfile == null)
            {
                return;
            }

            ApplyCameraProfile(cameraProfile);
        }

        private void ApplyCameraProfile(CombatCameraProfile profile)
        {
            keepCameraCenteredOnPlayer = profile.KeepCenteredOnPlayer;
            cameraPlayerOffset = profile.PlayerOffset;
            autoCalculateCameraPlayerOffset = profile.AutoCalculatePlayerOffset;
            cameraFollowDistance = profile.FollowDistance;
            cameraFramingOffset = profile.FramingOffset;
            cameraEulerAngles = profile.EulerAngles;
            cameraOrthographicSize = profile.OrthographicSize;
            minCameraOrthographicSize = profile.MinOrthographicSize;
            maxCameraOrthographicSize = profile.MaxOrthographicSize;
            cameraFollowSmoothing = profile.FollowSmoothing;
        }

        // Reflection contract: CombatGameplayCameraPresenterTests reads this property by name; the
        // auto-offset math lives in CombatCameraController.
        private Vector3 EffectiveCameraPlayerOffset =>
            autoCalculateCameraPlayerOffset
                ? ComputeAutoGameplayCameraPlayerOffset(cameraEulerAngles, cameraFollowDistance, cameraFramingOffset)
                : cameraPlayerOffset;

        private bool ShouldIgnoreGameplayShortcutInput()
        {
            if (pauseMenuInputBlocked)
            {
                return true;
            }

            if (isChoicePanelVisible || isHandCardSelectionActive || IsRewardPopupOpen())
            {
                return true;
            }

            if (CombatInputRouter.IsTextInputFocused())
            {
                return true;
            }

            if (CombatInputRouter.TryGetCurrentPointerPosition(out var pointerPosition) && IsPointerOverDebugPanel(pointerPosition))
            {
                return true;
            }

            return false;
        }

        private bool ShouldIgnoreGameplayShortcutInputForPhaseEnd()
        {
            if (pauseMenuInputBlocked)
            {
                return true;
            }

            if (IsRewardPopupOpen() || CombatInputRouter.IsTextInputFocused())
            {
                return true;
            }

            if (CombatInputRouter.TryGetCurrentPointerPosition(out var pointerPosition) && IsPointerOverDebugPanel(pointerPosition))
            {
                return true;
            }

            return false;
        }

        // P4 Stage 2 camera seam service: owns the combat-effect shake (subscription + firing) and
        // gameplay camera math; this controller keeps the serialized camera config plus the
        // ICombatCameraHost surface and injects values/delegates.
        private CombatCameraController CameraController =>
            cameraController ??= new CombatCameraController(
                this,
                ResolveCinemachineCameraShakeSource,
                () => new EffectShakeSettings(
                    cardAttackCameraShakeStrength,
                    monsterAttackCameraShakeStrength,
                    cameraShakeDuration,
                    cameraShakeVibrato,
                    cameraShakeRandomness,
                    cameraShakeDamageScale,
                    cameraShakeMaxMultiplier),
                ResolveEffectVfxDelaySeconds,
                () => IsImpactSyncedAttackFlush,
                () => ActiveShakeImpactDelay,
                () => isActiveAndEnabled,
                () => State?.CardCatalog,
                () => CinematicUnscaledDeltaTime);

        // P4 Stage 1 audio seam service: owns the ICombatAudioHost implementation; this
        // controller keeps the seam surface and forwards (scene refs + presenter binding intact).
        private CombatAudioHostAdapter AudioHost =>
            audioHostAdapter ??= new CombatAudioHostAdapter(
                () => State,
                () => Cinematics.DeferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes,
                ResolveMovementEffectPresentation);

        // P4 Stage 3 HUD seam service: owns host-side HUD presentation wiring (turn-phase dock
        // subscription + announcements); this controller keeps the ICombatCardHudHost surface and
        // the serialized view references.
        private CombatCardHudHostAdapter HudHost =>
            cardHudHostAdapter ??= new CombatCardHudHostAdapter(
                () => State,
                () => Cinematics.DeferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes,
                ShowGameVictoryOverlay);

        // P4 Stage 4 hit-stop service: owns the timeScale/fixedDeltaTime save-restore state and the
        // paused-animator snapshots; this controller keeps the serialized hit-stop/death tuning and
        // schedules the coroutines.
        private CombatHitStopController HitStop =>
            hitStopController ??= new CombatHitStopController(
                () => atlasTilePresentationView != null && atlasTilePresentationView.TryGetPlayerAnimationSpeed(out var speed) ? speed : (float?)null,
                speed => atlasTilePresentationView?.SetPlayerAnimationSpeed(speed),
                monsterId => actorMarkerPresenter.TryGetAnimationSpeed(monsterId, out var speed) ? speed : (float?)null,
                (monsterId, speed) => actorMarkerPresenter.SetAnimationSpeed(monsterId, speed),
                () => EffectiveHitStopAnimationSpeed,
                () => EffectiveHitStopTimeScale,
                () => CinematicUnscaledDeltaTime);

        Coroutine ICombatCameraHost.StartCameraCoroutine(IEnumerator routine) => StartCoroutine(routine);

        void ICombatCameraHost.StopCameraCoroutine(Coroutine routine)
        {
            if (routine != null)
            {
                StopCoroutine(routine);
            }
        }

        // Reflection contract: CombatGameplayCameraPresenterTests invokes this method by name; the
        // shake behaviour lives in CombatCameraController.
        private void AddCinemachineCameraShake(float strength, float duration, int vibrato, float randomness) =>
            CameraController.AddCinemachineShake(strength, duration, vibrato, randomness);

        private CinemachineImpulseSource ResolveCinemachineCameraShakeSource()
        {
            if (cinemachineCameraShakeSource != null)
            {
                return cinemachineCameraShakeSource;
            }

            cinemachineCameraShakeSource = GetComponent<CinemachineImpulseSource>();
            if (cinemachineCameraShakeSource == null)
            {
                cinemachineCameraShakeSource = gameObject.AddComponent<CinemachineImpulseSource>();
            }

            return cinemachineCameraShakeSource;
        }

        private void ResolveSceneReferences()
        {
            if (useExplicitTestReferences)
            {
                ConfigureInputControllerForSelectedView();
                return;
            }

            if (atlasTilePresentationView == null)
            {
                atlasTilePresentationView = Object.FindFirstObjectByType<AtlasTilePresentationView>();
            }

            EnsureOverlayPresenter();

            if (inputController == null)
            {
                inputController = Object.FindFirstObjectByType<HexMapInputController>();
            }

            if (prototype3DCamera == null)
            {
                prototype3DCamera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            }

            if (movementEffectPresentation == null)
            {
                movementEffectPresentation = Object.FindFirstObjectByType<EffectPresentationController>();
            }

            ConfigureInputControllerForSelectedView();
        }

        private void ConfigureInputControllerForSelectedView()
        {
            if (inputController != null && atlasTilePresentationView != null)
            {
                inputController.Configure3D(prototype3DCamera, atlasTilePresentationView);
                inputController.SetPresentationMode(HexMapInputController.PresentationMode.ThreeD);
            }
        }

        private void SubscribeInput()
        {
            if (inputController == null)
            {
                return;
            }

            if (subscribedToInput)
            {
                if (subscribedInputController == inputController)
                {
                    return;
                }

                UnsubscribeInput();
            }

            inputController.HexClicked += OnHexClicked;
            inputController.HexHovered += OnHexHovered;
            inputController.HoverCleared += OnHoverCleared;
            subscribedInputController = inputController;
            subscribedToInput = true;
        }

        private void UnsubscribeInput()
        {
            if (!subscribedToInput)
            {
                return;
            }

            if (subscribedInputController != null)
            {
                subscribedInputController.HexClicked -= OnHexClicked;
                subscribedInputController.HexHovered -= OnHexHovered;
                subscribedInputController.HoverCleared -= OnHoverCleared;
            }

            subscribedInputController = null;
            subscribedToInput = false;
        }

        private void OnHexClicked(HexCoord coord)
        {
            if (Application.isPlaying && CombatInputRouter.TryGetCurrentPointerPosition(out var pointerPosition) && IsPointerOverDebugPanel(pointerPosition))
            {
                LastInputMessage = "Debug panel hover blocked map click input.";
                RefreshHudOnly();
                return;
            }

            if (RejectUnselectableTile(coord))
            {
                return;
            }

            if (clickMoveDebugModeEnabled)
            {
                TryDebugMoveTo(coord);
                return;
            }

            // 가방 아이템 타게팅(T4-2)이 걸려 있으면 이번 클릭은 그 아이템의 대상이다 — 카드 선택보다
            // 먼저 본다(BeginBagItemTargeting이 카드 선택을 지우므로 실제로 겹치지 않는다).
            if (HasPendingBagItemTargeting)
            {
                TryUseBagItemOn(coord);
                return;
            }

            if (SelectedTargetCardKind.HasValue)
            {
                TryUseSelectedTargetCard(coord);
                return;
            }

            TryMoveTo(coord);
        }


        private void OnHexHovered(HexCoord coord)
        {
            lastHoveredCoord = coord;
            var presenter = EnsureOverlayPresenter();
            if (presenter == null)
            {
                return;
            }

            if (IsMoveSelectionActive && reachable.ContainsKey(coord))
            {
                presenter.ShowPlayerHoverMove(new[] { coord });
                presenter.ClearPlayerHoverAction();
            }
            else if (SelectedTargetCardKind.HasValue && GetSelectedTargetHighlightCells().Contains(coord))
            {
                presenter.ShowPlayerHoverAction(new[] { coord });
                presenter.ClearPlayerHoverMove();
            }
            else
            {
                presenter.ClearPlayerHoverMove();
                presenter.ClearPlayerHoverAction();
            }

            if (selectionState.Mode != CombatSelectionMode.None)
            {
                var effectArea = overlayQuery.GetSelectedEffectAreaHighlightCells(State, LoadedMap, selectionState, coord).ToList();
                if (effectArea.Count > 0)
                {
                    presenter.ShowPlayerActionEffectArea(effectArea);
                }
                else if (SelectedTargetCardKind.HasValue)
                {
                    presenter.ClearPlayerActionEffectArea();
                }
            }
        }

        private void OnHoverCleared()
        {
            lastHoveredCoord = null;
            var presenter = EnsureOverlayPresenter();
            presenter?.ClearPlayerHoverMove();
            presenter?.ClearPlayerHoverAction();
            if (selectionState.Mode != CombatSelectionMode.None)
            {
                var effectArea = overlayQuery.GetSelectedEffectAreaHighlightCells(State, LoadedMap, selectionState).ToList();
                if (effectArea.Count > 0)
                {
                    presenter?.ShowPlayerActionEffectArea(effectArea);
                }
                else
                {
                    presenter?.ClearPlayerActionEffectArea();
                }
            }
        }


        private static void DisableLegacyM1MoveControls()
        {
            var legacyControls = GameObject.Find(LegacyM1MoveControlsName);
            if (legacyControls != null)
            {
                legacyControls.SetActive(false);
            }
        }
        private PlayerInventoryState ResolveInitialInventory()
        {
            return playerRunRestore?.Inventory != null
                ? playerRunRestore.Inventory.ToState()
                : CreateStartingInventory();
        }

        private PlayerDeckData ResolveInitialPlayerDeck(CardCatalogDefinition cardCatalog)
        {
            var restoredDeck = playerRunRestore?.Decks != null ? playerRunRestore.Decks.ToPlayerDeckData() : null;
            var hasRestoredCards = restoredDeck != null &&
                (restoredDeck.MovementCards.Count > 0 || restoredDeck.ActionCards.Count > 0);
            return hasRestoredCards ? restoredDeck : CreateStartingPlayerDeck(cardCatalog);
        }

        private PlayerInventoryState CreateStartingInventory()
        {
            var relicsAndCurses = new PlayerRelicCurseInventory();
            foreach (var itemId in startingPermanentItemIds ?? new string[0])
            {
                var normalizedId = itemId?.Trim();
                if (string.IsNullOrEmpty(normalizedId))
                {
                    continue;
                }

                if (!relicsAndCurses.TryAddDefinition(normalizedId, out var reason))
                {
                    Debug.LogWarning($"Starting permanent item '{normalizedId}' was ignored: {reason}", this);
                }
            }

            return new PlayerInventoryState(relicsAndCurses);
        }

        private PlayerDeckData CreateStartingPlayerDeck(CardCatalogDefinition cardCatalog)
        {
            var selectedStartingDeckAsset = startingDeckOverride != null ? startingDeckOverride : startingDeckAsset;
            if (selectedStartingDeckAsset == null)
            {
                return null;
            }

            if (!selectedStartingDeckAsset.Validate(cardCatalog, out var reason))
            {
                Debug.LogWarning($"Starting deck asset is invalid and will be ignored: {reason}", this);
                return null;
            }

            return selectedStartingDeckAsset.ToPlayerDeckData(cardCatalog, "prototype");
        }
    }
}






