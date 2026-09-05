using System.Linq;
using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 시네마틱 파사드(4B-C). 본문은 전부 <see cref="CombatCinematics"/>에 있고 여기는 ①직렬화 필드를 직접 읽고 쓰는
    /// 호스트 잔류 멤버(공개 튜닝 읽기 8·디버그 튜닝 3·마커 숨김·<c>MemoryStoneVictoryCameraPath</c>·리플렉션 static 스텁 3)
    /// ②시그니처 불변 공개 위임 ③<see cref="ICombatCinematicsHost"/> 명시적 구현뿐이다. 협력자는 지연 생성(<c>??=</c>).
    /// </summary>
    public sealed partial class MapCombatController : ICombatCinematicsHost
    {
        private CombatCinematics cinematics;

        private CombatCinematics Cinematics => cinematics ??= new CombatCinematics(this);

        public readonly struct MemoryStoneVictoryCameraPath
        {
            public MemoryStoneVictoryCameraPath(
                IReadOnlyList<Vector3> cameraPositions,
                IReadOnlyList<Vector3> focusPositions,
                bool usesAuthoredPoints)
            {
                CameraPositions = cameraPositions ?? System.Array.Empty<Vector3>();
                FocusPositions = focusPositions ?? System.Array.Empty<Vector3>();
                UsesAuthoredPoints = usesAuthoredPoints;
            }

            public IReadOnlyList<Vector3> CameraPositions { get; }
            public IReadOnlyList<Vector3> FocusPositions { get; }
            public bool UsesAuthoredPoints { get; }

            public bool IsValid =>
                CameraPositions != null &&
                FocusPositions != null &&
                CameraPositions.Count >= 2 &&
                FocusPositions.Count >= 2;

            public static MemoryStoneVictoryCameraPath Empty =>
                new MemoryStoneVictoryCameraPath(System.Array.Empty<Vector3>(), System.Array.Empty<Vector3>(), false);
        }
        public float MemoryStoneVictoryCameraHeight => memoryStoneVictoryCameraHeight;
        public float MemoryStoneVictoryCameraOrthographicSize => memoryStoneVictoryCameraOrthographicSize;
        public bool MemoryStoneVictoryCameraFollowsRevealRoute => memoryStoneVictoryCameraFollowsRevealRoute;
        public bool MemoryStoneVictoryUseFixedCameraRotation => memoryStoneVictoryUseFixedCameraRotation;
        public float MemoryStoneVictoryCameraPitchDegrees => memoryStoneVictoryCameraPitchDegrees;
        public float MemoryStoneVictoryCameraYawDegrees => memoryStoneVictoryCameraYawDegrees;
        public float MemoryStoneVictoryCameraFocusLag => memoryStoneVictoryCameraFocusLag;
        public bool MemoryStoneVictoryCameraUsesLegacyBaseRotation =>
            Mathf.Approximately(memoryStoneVictoryCameraPitchDegrees, LegacyMemoryStoneVictoryCameraPitchDegrees) &&
            Mathf.Approximately(Mathf.Repeat(memoryStoneVictoryCameraYawDegrees, 360f), LegacyMemoryStoneVictoryCameraYawDegrees);
        public string MemoryStoneVictoryCameraPointDebugText => MemoryStoneVictoryCinematicPlanner.FormatVictoryCameraPointDebugText(LoadedMap);
        public void DebugSetMemoryStoneVictoryCameraTuning(
            float height,
            float orthographicSize,
            bool useFixedRotation,
            float pitchDegrees,
            float yawDegrees,
            float focusLag)
        {
            memoryStoneVictoryCameraHeight = Mathf.Max(1f, height);
            memoryStoneVictoryCameraOrthographicSize = Mathf.Max(1f, orthographicSize);
            memoryStoneVictoryUseFixedCameraRotation = useFixedRotation;
            memoryStoneVictoryCameraPitchDegrees = Mathf.Clamp(pitchDegrees, 5f, 85f);
            memoryStoneVictoryCameraYawDegrees = Mathf.Repeat(yawDegrees, 360f);
            memoryStoneVictoryCameraFocusLag = Mathf.Clamp01(focusLag);
        }
        public void DebugSetMemoryStoneVictoryCameraFollowsRevealRoute(bool followsRevealRoute)
        {
            memoryStoneVictoryCameraFollowsRevealRoute = followsRevealRoute;
        }
        public void DebugResetMemoryStoneVictoryCameraBaseRotationToDefaults()
        {
            memoryStoneVictoryCameraPitchDegrees = DefaultMemoryStoneVictoryCameraPitchDegrees;
            memoryStoneVictoryCameraYawDegrees = DefaultMemoryStoneVictoryCameraYawDegrees;
        }
        // Clears all monster markers (used by the victory cinematic so live monsters do not show on the
        // revealed map). The next full RefreshView re-shows them, so this is only for terminal sequences.
        private void HideMonsterMarkersForCinematic()
        {
            actorMarkerPresenter?.ShowMonsterMarkers(
                Enumerable.Empty<CombatActorMarkerPresenter.MonsterMarkerState>(),
                GetSelected3DViewTransform(),
                transform,
                enemyMarkerPrefab,
                enemyMarkerColor,
                enemyMarkerRadius,
                enemyVisualLocalOffset,
                enemyVisualLocalEulerAngles,
                enemyVisualLocalScale);
        }
        // Victory-cinematic planning math lives in MemoryStoneVictoryCinematicPlanner. These
        // name-identical private static stubs remain because MemoryStoneVictoryPresentationTests
        // invokes them by reflection on this type.
        internal static IReadOnlyList<HexCoord> BuildMemoryStoneVictoryFocusCoords(
            HexMapData map,
            HexCoord startCoord,
            HexCoord memoryStoneCoord)
        {
            return MemoryStoneVictoryCinematicPlanner.BuildMemoryStoneVictoryFocusCoords(map, startCoord, memoryStoneCoord);
        }
        internal static IReadOnlyList<HexCoord> BuildMemoryStoneVictoryRevealRouteSamples(
            HexMapData map,
            IReadOnlyList<HexCoord> focusCoords)
        {
            return MemoryStoneVictoryCinematicPlanner.BuildMemoryStoneVictoryRevealRouteSamples(map, focusCoords);
        }
        internal static IReadOnlyList<MemoryStoneVictoryWave> BuildMemoryStoneVictoryWavesAlongRoute(
            HexMapData map,
            IReadOnlyList<HexCoord> route,
            int revealRadius,
            HexCoord fallbackCoord)
        {
            return MemoryStoneVictoryCinematicPlanner.BuildMemoryStoneVictoryWavesAlongRoute(map, route, revealRadius, fallbackCoord);
        }

        // ── 공개 위임(옛 Cinematics 파셜 공개 표면, 시그니처 불변) ──
        public bool MemoryStoneVictoryVirtualCameraActive => Cinematics.MemoryStoneVictoryVirtualCameraActive;
        public Vector3 MemoryStoneVictoryCameraLiveEulerAngles => Cinematics.MemoryStoneVictoryCameraLiveEulerAngles;
        public void DebugPlayMemoryStoneVictoryPresentation() => Cinematics.DebugPlayMemoryStoneVictoryPresentation();
        public void SetStageIntroLookPresets(SeoulPlayup.Map.Unity.EnvironmentLookPreset dayPreset, SeoulPlayup.Map.Unity.EnvironmentLookPreset nightPreset) => Cinematics.SetStageIntroLookPresets(dayPreset, nightPreset);
        public void SetMemoryStoneVictoryClearLookPreset(SeoulPlayup.Map.Unity.EnvironmentLookPreset clearPreset) => Cinematics.SetMemoryStoneVictoryClearLookPreset(clearPreset);
        public void DebugReplayStageIntro() => Cinematics.DebugReplayStageIntro();
        public void DebugPlayStageIntroDollyRun(int runIndex) => Cinematics.DebugPlayStageIntroDollyRun(runIndex);
        public bool IsStageIntroPlaying => Cinematics.IsStageIntroPlaying;
        public void DebugPlayStageIntroFinaleRun() => Cinematics.DebugPlayStageIntroFinaleRun();
        public void DebugPlayStageIntroFinaleRun(bool skipSpawnBeats, float widePitchOverrideDegrees, float overviewDistanceScale, int rippleCameraMode, float rippleYawSweepDegrees, float wideHeightScale = 1f, float endHoldSeconds = 0f) => Cinematics.DebugPlayStageIntroFinaleRun(skipSpawnBeats, widePitchOverrideDegrees, overviewDistanceScale, rippleCameraMode, rippleYawSweepDegrees, wideHeightScale, endHoldSeconds);

        // ── ICombatCinematicsHost (명시적 구현 — 공개 API 무증가) ──
        // State · LoadedMap · TryGetTileWorldPosition · StartCoroutine · StopCoroutine(Coroutine)은 이미 public이라 암시적으로 만족한다.
        string ICombatCinematicsHost.LastInputMessage
        {
            get => LastInputMessage;
            set => LastInputMessage = value;
        }
        float ICombatCinematicsHost.CinematicDeltaTime => CinematicDeltaTime;
        Transform ICombatCinematicsHost.HostTransform => transform;
        Scene ICombatCinematicsHost.HostScene => gameObject.scene;
        CombatCameraController ICombatCinematicsHost.CameraController => CameraController;
        CombatActorMarkerPresenter ICombatCinematicsHost.ActorMarkers => actorMarkerPresenter;
        AtlasTilePresentationView ICombatCinematicsHost.TileView => atlasTilePresentationView;
        Camera ICombatCinematicsHost.GameplayCamera => prototype3DCamera;
        GameObject ICombatCinematicsHost.GameVictoryOverlayRoot => gameVictoryOverlayRoot;
        HashSet<HexCoord> ICombatCinematicsHost.CinematicForcedRevealCells
        {
            get => cinematicForcedRevealCells;
            set => cinematicForcedRevealCells = value;
        }
        HashSet<string> ICombatCinematicsHost.StageIntroHiddenMonsterIds
        {
            get => stageIntroHiddenMonsterIds;
            set => stageIntroHiddenMonsterIds = value;
        }
        void ICombatCinematicsHost.RequestAudioCue(string cueId, string context) => RequestAudioCue(cueId, context);
        void ICombatCinematicsHost.RecenterGameplayCameraOnPlayer(bool immediate) => RecenterGameplayCameraOnPlayer(immediate);
        void ICombatCinematicsHost.AddCinemachineCameraShake(float strength, float duration, int vibrato, float randomness) => AddCinemachineCameraShake(strength, duration, vibrato, randomness);
        void ICombatCinematicsHost.SuppressHoverDuringStageIntro() => SuppressHoverDuringStageIntro();
        void ICombatCinematicsHost.RefreshHudOnly() => RefreshHudOnly();
        void ICombatCinematicsHost.ShowGameVictoryOverlay() => ShowGameVictoryOverlay();
        void ICombatCinematicsHost.HideGameVictoryOverlay() => HideGameVictoryOverlay();
        void ICombatCinematicsHost.RefreshView() => RefreshView();
        void ICombatCinematicsHost.RefreshMapVisibilityAndHighlights() => RefreshMapVisibilityAndHighlights();
        void ICombatCinematicsHost.UpdateEnemyMarker() => UpdateEnemyMarker();
        EffectPresentationController ICombatCinematicsHost.ResolveMovementEffectPresentation() => ResolveMovementEffectPresentation();
        void ICombatCinematicsHost.SetDebugRevealAllMapCells(bool enabled, bool refresh) => SetDebugRevealAllMapCells(enabled, refresh);
        void ICombatCinematicsHost.HideMonsterMarkersForCinematic() => HideMonsterMarkersForCinematic();

        // 직렬화 튜닝 getter(읽기 전용)
        float ICombatCinematicsHost.MemoryStoneVictoryCameraFocusLag => memoryStoneVictoryCameraFocusLag;
        bool ICombatCinematicsHost.MemoryStoneVictoryCameraFollowsRevealRoute => memoryStoneVictoryCameraFollowsRevealRoute;
        float ICombatCinematicsHost.MemoryStoneVictoryCameraHeight => memoryStoneVictoryCameraHeight;
        float ICombatCinematicsHost.MemoryStoneVictoryCameraOrthographicSize => memoryStoneVictoryCameraOrthographicSize;
        float ICombatCinematicsHost.MemoryStoneVictoryCameraPitchDegrees => memoryStoneVictoryCameraPitchDegrees;
        float ICombatCinematicsHost.MemoryStoneVictoryCameraYawDegrees => memoryStoneVictoryCameraYawDegrees;
        float ICombatCinematicsHost.MemoryStoneVictoryCornerRadius => memoryStoneVictoryCornerRadius;
        float ICombatCinematicsHost.MemoryStoneVictoryCorneringLookAheadDistance => memoryStoneVictoryCorneringLookAheadDistance;
        float ICombatCinematicsHost.MemoryStoneVictoryEndAzimuthOffsetDegrees => memoryStoneVictoryEndAzimuthOffsetDegrees;
        float ICombatCinematicsHost.MemoryStoneVictoryEndBackdropMaxDistance => memoryStoneVictoryEndBackdropMaxDistance;
        float ICombatCinematicsHost.MemoryStoneVictoryEndBackdropMinDistance => memoryStoneVictoryEndBackdropMinDistance;
        float ICombatCinematicsHost.MemoryStoneVictoryEndLookAtHeight => memoryStoneVictoryEndLookAtHeight;
        float ICombatCinematicsHost.MemoryStoneVictoryEndOrbitDegrees => memoryStoneVictoryEndOrbitDegrees;
        float ICombatCinematicsHost.MemoryStoneVictoryEndOrbitDistance => memoryStoneVictoryEndOrbitDistance;
        float ICombatCinematicsHost.MemoryStoneVictoryEndOrbitSeconds => memoryStoneVictoryEndOrbitSeconds;
        float ICombatCinematicsHost.MemoryStoneVictoryEndPitchDegrees => memoryStoneVictoryEndPitchDegrees;
        float ICombatCinematicsHost.MemoryStoneVictoryEndSettleSeconds => memoryStoneVictoryEndSettleSeconds;
        float ICombatCinematicsHost.MemoryStoneVictoryEndZoomDistance => memoryStoneVictoryEndZoomDistance;
        float ICombatCinematicsHost.MemoryStoneVictoryEndZoomInSeconds => memoryStoneVictoryEndZoomInSeconds;
        float ICombatCinematicsHost.MemoryStoneVictoryFinalHoldSeconds => memoryStoneVictoryFinalHoldSeconds;
        int ICombatCinematicsHost.MemoryStoneVictoryFireworkBurstCount => memoryStoneVictoryFireworkBurstCount;
        float ICombatCinematicsHost.MemoryStoneVictoryFireworkBurstInterval => memoryStoneVictoryFireworkBurstInterval;
        float ICombatCinematicsHost.MemoryStoneVictoryFireworkScatterRadius => memoryStoneVictoryFireworkScatterRadius;
        float ICombatCinematicsHost.MemoryStoneVictoryFireworkShakeStrength => memoryStoneVictoryFireworkShakeStrength;
        int ICombatCinematicsHost.MemoryStoneVictoryLateralSpreadPercent => memoryStoneVictoryLateralSpreadPercent;
        float ICombatCinematicsHost.MemoryStoneVictoryRevealLeadDistance => memoryStoneVictoryRevealLeadDistance;
        bool ICombatCinematicsHost.MemoryStoneVictoryRevealRemainingMapAfterWave => memoryStoneVictoryRevealRemainingMapAfterWave;
        float ICombatCinematicsHost.MemoryStoneVictorySideViewEndProgress => memoryStoneVictorySideViewEndProgress;
        float ICombatCinematicsHost.MemoryStoneVictorySideViewHeight => memoryStoneVictorySideViewHeight;
        float ICombatCinematicsHost.MemoryStoneVictorySideViewLateralOffset => memoryStoneVictorySideViewLateralOffset;
        float ICombatCinematicsHost.MemoryStoneVictorySideViewMaxLateralExtentFraction => memoryStoneVictorySideViewMaxLateralExtentFraction;
        float ICombatCinematicsHost.MemoryStoneVictorySideViewMinSeconds => memoryStoneVictorySideViewMinSeconds;
        float ICombatCinematicsHost.MemoryStoneVictorySideViewSeconds => memoryStoneVictorySideViewSeconds;
        float ICombatCinematicsHost.MemoryStoneVictorySideViewSpeed => memoryStoneVictorySideViewSpeed;
        float ICombatCinematicsHost.MemoryStoneVictorySideViewStartProgress => memoryStoneVictorySideViewStartProgress;
        float ICombatCinematicsHost.MemoryStoneVictorySweepSpeed => memoryStoneVictorySweepSpeed;
        bool ICombatCinematicsHost.MemoryStoneVictoryUseFixedCameraRotation => memoryStoneVictoryUseFixedCameraRotation;
        float ICombatCinematicsHost.MemoryStoneVictoryYawSmoothingSeconds => memoryStoneVictoryYawSmoothingSeconds;
        bool ICombatCinematicsHost.PlayMemoryStoneVictorySequence => playMemoryStoneVictorySequence;
        bool ICombatCinematicsHost.RevealAllMapCellsInDebugMode => revealAllMapCellsInDebugMode;
        float ICombatCinematicsHost.StageIntroCameraFieldOfView => stageIntroCameraFieldOfView;
        float ICombatCinematicsHost.StageIntroCameraPitchDegrees => stageIntroCameraPitchDegrees;
        float ICombatCinematicsHost.StageIntroDollyCameraHeight => stageIntroDollyCameraHeight;
        float ICombatCinematicsHost.StageIntroDollyLookAheadDistance => stageIntroDollyLookAheadDistance;
        float ICombatCinematicsHost.StageIntroDollyLookAtHeightOffset => stageIntroDollyLookAtHeightOffset;
        float ICombatCinematicsHost.StageIntroDollyRampSeconds => stageIntroDollyRampSeconds;
        float ICombatCinematicsHost.StageIntroDollySpeedUnitsPerSecond => stageIntroDollySpeedUnitsPerSecond;
        float ICombatCinematicsHost.StageIntroDwellSeconds => stageIntroDwellSeconds;
        float ICombatCinematicsHost.StageIntroFinaleMonsterRevealLeadDistance => stageIntroFinaleMonsterRevealLeadDistance;
        float ICombatCinematicsHost.StageIntroFinaleSeconds => stageIntroFinaleSeconds;
        float ICombatCinematicsHost.StageIntroLookAtHeightOffset => stageIntroLookAtHeightOffset;
        float ICombatCinematicsHost.StageIntroMonsterSpawnAttackDelaySeconds => stageIntroMonsterSpawnAttackDelaySeconds;
        float ICombatCinematicsHost.StageIntroMonsterSpawnDistance => stageIntroMonsterSpawnDistance;
        float ICombatCinematicsHost.StageIntroMonsterSpawnLookAtHeight => stageIntroMonsterSpawnLookAtHeight;
        float ICombatCinematicsHost.StageIntroMonsterSpawnPitchDegrees => stageIntroMonsterSpawnPitchDegrees;
        float ICombatCinematicsHost.StageIntroMonsterSpawnRevealDelaySeconds => stageIntroMonsterSpawnRevealDelaySeconds;
        int ICombatCinematicsHost.StageIntroMonsterSpawnShotCount => stageIntroMonsterSpawnShotCount;
        float ICombatCinematicsHost.StageIntroMonsterSpawnShotSeconds => stageIntroMonsterSpawnShotSeconds;
        float ICombatCinematicsHost.StageIntroNightTransitionSeconds => stageIntroNightTransitionSeconds;
        float ICombatCinematicsHost.StageIntroOrbitDistance => stageIntroOrbitDistance;
        float ICombatCinematicsHost.StageIntroOverviewDistance => stageIntroOverviewDistance;
        float ICombatCinematicsHost.StageIntroOverviewHoldSeconds => stageIntroOverviewHoldSeconds;
    }
}
