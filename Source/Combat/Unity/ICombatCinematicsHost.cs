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
    /// <see cref="CombatCinematics"/>가 호스트(<see cref="MapCombatController"/>)에서 필요로 하는 조각(4B-C). 직렬화 튜닝 필드
    /// 62개는 <b>읽기 전용 getter</b>로만(쓰는 디버그 튜닝은 호스트에 남았다). 쓰기는 <c>LastInputMessage</c>(private
    /// setter → 명시적 구현)와 두 시네마틱이 MapView·Tutorial과 나눠 쓰는 표현 필터 2개(get/set — 집합 자체는 호스트 소유).
    /// 코루틴은 호스트 MonoBehaviour가 돌린다(<c>StartCoroutine</c>/<c>StopCoroutine</c>).
    /// </summary>
    internal interface ICombatCinematicsHost
    {
        // ── 상태 ──
        CombatState State { get; }
        HexMapData LoadedMap { get; }
        string LastInputMessage { get; set; }
        float CinematicDeltaTime { get; }
        Transform HostTransform { get; }
        Scene HostScene { get; }
        CombatCameraController CameraController { get; }
        /// <summary>null일 수 있다(테스트 픽스처).</summary>
        CombatActorMarkerPresenter ActorMarkers { get; }
        /// <summary>null일 수 있다.</summary>
        AtlasTilePresentationView TileView { get; }
        Camera GameplayCamera { get; }
        /// <summary>null일 수 있다(오버레이 미생성).</summary>
        GameObject GameVictoryOverlayRoot { get; }
        /// <summary>두 시네마틱이 공유하는 강제 리빌 집합 — null = 실제 안개 규칙. MapView·Tutorial도 읽고 쓴다.</summary>
        HashSet<HexCoord> CinematicForcedRevealCells { get; set; }
        /// <summary>인트로 동안 숨긴 몬스터 id — null = 비활성. MapView가 읽는다.</summary>
        HashSet<string> StageIntroHiddenMonsterIds { get; set; }

        // ── 코루틴 ──
        Coroutine StartCoroutine(IEnumerator routine);
        void StopCoroutine(Coroutine routine);

        // ── 호스트 서비스 ──
        bool TryGetTileWorldPosition(HexCoord coord, out Vector3 worldPosition);
        void RequestAudioCue(string cueId, string context);
        void RecenterGameplayCameraOnPlayer(bool immediate);
        void AddCinemachineCameraShake(float strength, float duration, int vibrato, float randomness);
        void SuppressHoverDuringStageIntro();
        void RefreshHudOnly();
        void ShowGameVictoryOverlay();
        void HideGameVictoryOverlay();
        void RefreshView();
        void RefreshMapVisibilityAndHighlights();
        void UpdateEnemyMarker();
        EffectPresentationController ResolveMovementEffectPresentation();
        void SetDebugRevealAllMapCells(bool enabled, bool refresh);
        /// <summary>마커 직렬화 6필드를 읽어 몬스터 마커를 숨긴다 — 직렬화 필드라 호스트에 남긴 서비스.</summary>
        void HideMonsterMarkersForCinematic();

        // ── 직렬화 튜닝(호스트 소유 — 읽기만) ──
        float MemoryStoneVictoryCameraFocusLag { get; }
        bool MemoryStoneVictoryCameraFollowsRevealRoute { get; }
        float MemoryStoneVictoryCameraHeight { get; }
        float MemoryStoneVictoryCameraOrthographicSize { get; }
        float MemoryStoneVictoryCameraPitchDegrees { get; }
        float MemoryStoneVictoryCameraYawDegrees { get; }
        float MemoryStoneVictoryCornerRadius { get; }
        float MemoryStoneVictoryCorneringLookAheadDistance { get; }
        float MemoryStoneVictoryEndAzimuthOffsetDegrees { get; }
        float MemoryStoneVictoryEndBackdropMaxDistance { get; }
        float MemoryStoneVictoryEndBackdropMinDistance { get; }
        float MemoryStoneVictoryEndLookAtHeight { get; }
        float MemoryStoneVictoryEndOrbitDegrees { get; }
        float MemoryStoneVictoryEndOrbitDistance { get; }
        float MemoryStoneVictoryEndOrbitSeconds { get; }
        float MemoryStoneVictoryEndPitchDegrees { get; }
        float MemoryStoneVictoryEndSettleSeconds { get; }
        float MemoryStoneVictoryEndZoomDistance { get; }
        float MemoryStoneVictoryEndZoomInSeconds { get; }
        float MemoryStoneVictoryFinalHoldSeconds { get; }
        int MemoryStoneVictoryFireworkBurstCount { get; }
        float MemoryStoneVictoryFireworkBurstInterval { get; }
        float MemoryStoneVictoryFireworkScatterRadius { get; }
        float MemoryStoneVictoryFireworkShakeStrength { get; }
        int MemoryStoneVictoryLateralSpreadPercent { get; }
        float MemoryStoneVictoryRevealLeadDistance { get; }
        bool MemoryStoneVictoryRevealRemainingMapAfterWave { get; }
        float MemoryStoneVictorySideViewEndProgress { get; }
        float MemoryStoneVictorySideViewHeight { get; }
        float MemoryStoneVictorySideViewLateralOffset { get; }
        float MemoryStoneVictorySideViewMaxLateralExtentFraction { get; }
        float MemoryStoneVictorySideViewMinSeconds { get; }
        float MemoryStoneVictorySideViewSeconds { get; }
        float MemoryStoneVictorySideViewSpeed { get; }
        float MemoryStoneVictorySideViewStartProgress { get; }
        float MemoryStoneVictorySweepSpeed { get; }
        bool MemoryStoneVictoryUseFixedCameraRotation { get; }
        float MemoryStoneVictoryYawSmoothingSeconds { get; }
        bool PlayMemoryStoneVictorySequence { get; }
        bool RevealAllMapCellsInDebugMode { get; }
        float StageIntroCameraFieldOfView { get; }
        float StageIntroCameraPitchDegrees { get; }
        float StageIntroDollyCameraHeight { get; }
        float StageIntroDollyLookAheadDistance { get; }
        float StageIntroDollyLookAtHeightOffset { get; }
        float StageIntroDollyRampSeconds { get; }
        float StageIntroDollySpeedUnitsPerSecond { get; }
        float StageIntroDwellSeconds { get; }
        float StageIntroFinaleMonsterRevealLeadDistance { get; }
        float StageIntroFinaleSeconds { get; }
        float StageIntroLookAtHeightOffset { get; }
        float StageIntroMonsterSpawnAttackDelaySeconds { get; }
        float StageIntroMonsterSpawnDistance { get; }
        float StageIntroMonsterSpawnLookAtHeight { get; }
        float StageIntroMonsterSpawnPitchDegrees { get; }
        float StageIntroMonsterSpawnRevealDelaySeconds { get; }
        int StageIntroMonsterSpawnShotCount { get; }
        float StageIntroMonsterSpawnShotSeconds { get; }
        float StageIntroNightTransitionSeconds { get; }
        float StageIntroOrbitDistance { get; }
        float StageIntroOverviewDistance { get; }
        float StageIntroOverviewHoldSeconds { get; }
    }
}
