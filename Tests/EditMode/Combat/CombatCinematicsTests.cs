using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// <c>CombatCinematics</c> 단위 테스트(4B-C): 스텁 호스트로 협력자가 호스트 서비스를 <b>어떤 순서로 부르는지</b>만 잰다.
    /// 시네마틱 판정 자체는 실플레이·촬영(트레일러 러너)이라 EditMode에서는 코루틴을 돌리지 않는 가드 경로만 문다 —
    /// 승리 연출 폴백(연출 비활성 시 즉시 결과창)과 인트로 리플레이 가드. 출하값은 핀하지 않는다.
    /// </summary>
    public sealed class CombatCinematicsTests
    {
        private sealed class StubHost : ICombatCinematicsHost
        {
            public CombatState State { get; set; }
            public HexMapData LoadedMap => State?.Map;
            public string LastInputMessage { get; set; } = string.Empty;
            public bool PlayMemoryStoneVictorySequence { get; set; }
            public HashSet<HexCoord> CinematicForcedRevealCells { get; set; }
            public HashSet<string> StageIntroHiddenMonsterIds { get; set; }

            public readonly List<string> AudioCues = new List<string>();
            public int ShowGameVictoryOverlayCalls;
            public int RefreshHudOnlyCalls;
            public int StartCoroutineCalls;
            public int StopCoroutineCalls;

            public void RequestAudioCue(string cueId, string context) => AudioCues.Add(cueId);
            public void ShowGameVictoryOverlay() => ShowGameVictoryOverlayCalls++;
            public void RefreshHudOnly() => RefreshHudOnlyCalls++;
            public Coroutine StartCoroutine(IEnumerator routine)
            {
                StartCoroutineCalls++;
                return null;
            }
            public void StopCoroutine(Coroutine routine) => StopCoroutineCalls++;

            public float CinematicDeltaTime => default;
            public Transform HostTransform => default;
            public Scene HostScene => default;
            public CombatCameraController CameraController => default;
            public CombatActorMarkerPresenter ActorMarkers => default;
            public AtlasTilePresentationView TileView => default;
            public Camera GameplayCamera => default;
            public GameObject GameVictoryOverlayRoot => default;
            public float MemoryStoneVictoryCameraFocusLag => default;
            public bool MemoryStoneVictoryCameraFollowsRevealRoute => default;
            public float MemoryStoneVictoryCameraHeight => default;
            public float MemoryStoneVictoryCameraOrthographicSize => default;
            public float MemoryStoneVictoryCameraPitchDegrees => default;
            public float MemoryStoneVictoryCameraYawDegrees => default;
            public float MemoryStoneVictoryCornerRadius => default;
            public float MemoryStoneVictoryCorneringLookAheadDistance => default;
            public float MemoryStoneVictoryEndAzimuthOffsetDegrees => default;
            public float MemoryStoneVictoryEndBackdropMaxDistance => default;
            public float MemoryStoneVictoryEndBackdropMinDistance => default;
            public float MemoryStoneVictoryEndLookAtHeight => default;
            public float MemoryStoneVictoryEndOrbitDegrees => default;
            public float MemoryStoneVictoryEndOrbitDistance => default;
            public float MemoryStoneVictoryEndOrbitSeconds => default;
            public float MemoryStoneVictoryEndPitchDegrees => default;
            public float MemoryStoneVictoryEndSettleSeconds => default;
            public float MemoryStoneVictoryEndZoomDistance => default;
            public float MemoryStoneVictoryEndZoomInSeconds => default;
            public float MemoryStoneVictoryFinalHoldSeconds => default;
            public int MemoryStoneVictoryFireworkBurstCount => default;
            public float MemoryStoneVictoryFireworkBurstInterval => default;
            public float MemoryStoneVictoryFireworkScatterRadius => default;
            public float MemoryStoneVictoryFireworkShakeStrength => default;
            public int MemoryStoneVictoryLateralSpreadPercent => default;
            public float MemoryStoneVictoryRevealLeadDistance => default;
            public bool MemoryStoneVictoryRevealRemainingMapAfterWave => default;
            public float MemoryStoneVictorySideViewEndProgress => default;
            public float MemoryStoneVictorySideViewHeight => default;
            public float MemoryStoneVictorySideViewLateralOffset => default;
            public float MemoryStoneVictorySideViewMaxLateralExtentFraction => default;
            public float MemoryStoneVictorySideViewMinSeconds => default;
            public float MemoryStoneVictorySideViewSeconds => default;
            public float MemoryStoneVictorySideViewSpeed => default;
            public float MemoryStoneVictorySideViewStartProgress => default;
            public float MemoryStoneVictorySweepSpeed => default;
            public bool MemoryStoneVictoryUseFixedCameraRotation => default;
            public float MemoryStoneVictoryYawSmoothingSeconds => default;
            public bool RevealAllMapCellsInDebugMode => default;
            public float StageIntroCameraFieldOfView => default;
            public float StageIntroCameraPitchDegrees => default;
            public float StageIntroDollyCameraHeight => default;
            public float StageIntroDollyLookAheadDistance => default;
            public float StageIntroDollyLookAtHeightOffset => default;
            public float StageIntroDollyRampSeconds => default;
            public float StageIntroDollySpeedUnitsPerSecond => default;
            public float StageIntroDwellSeconds => default;
            public float StageIntroFinaleMonsterRevealLeadDistance => default;
            public float StageIntroFinaleSeconds => default;
            public float StageIntroLookAtHeightOffset => default;
            public float StageIntroMonsterSpawnAttackDelaySeconds => default;
            public float StageIntroMonsterSpawnDistance => default;
            public float StageIntroMonsterSpawnLookAtHeight => default;
            public float StageIntroMonsterSpawnPitchDegrees => default;
            public float StageIntroMonsterSpawnRevealDelaySeconds => default;
            public int StageIntroMonsterSpawnShotCount => default;
            public float StageIntroMonsterSpawnShotSeconds => default;
            public float StageIntroNightTransitionSeconds => default;
            public float StageIntroOrbitDistance => default;
            public float StageIntroOverviewDistance => default;
            public float StageIntroOverviewHoldSeconds => default;
            public bool TryGetTileWorldPosition(HexCoord coord, out Vector3 worldPosition) { worldPosition = default; return default; }
            public void RecenterGameplayCameraOnPlayer(bool immediate) { }
            public void AddCinemachineCameraShake(float strength, float duration, int vibrato, float randomness) { }
            public void SuppressHoverDuringStageIntro() { }
            public void HideGameVictoryOverlay() { }
            public void RefreshView() { }
            public void RefreshMapVisibilityAndHighlights() { }
            public void UpdateEnemyMarker() { }
            public EffectPresentationController ResolveMovementEffectPresentation() => default;
            public void SetDebugRevealAllMapCells(bool enabled, bool refresh) { }
            public void HideMonsterMarkersForCinematic() { }
        }

        [Test]
        public void VictoryPresentationFallsBackToTheOverlayWhenTheSequenceIsDisabled()
        {
            var host = new StubHost { PlayMemoryStoneVictorySequence = false };
            var cinematics = new CombatCinematics(host);

            cinematics.BeginMemoryStoneVictoryPresentation();

            Assert.That(host.AudioCues, Is.EqualTo(new[] { AudioCueIds.MusicVictory, AudioCueIds.GameVictory }),
                "승리 BGM은 연출 시작 즉시, 결과 스팅어는 폴백에서 — 이 순서가 계약이다.");
            Assert.That(host.ShowGameVictoryOverlayCalls, Is.EqualTo(1), "연출을 안 돌리면 결과창을 바로 띄워야 한다.");
            Assert.That(host.StartCoroutineCalls, Is.Zero, "폴백은 코루틴을 시작하지 않는다.");
            Assert.That(cinematics.DeferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes, Is.False,
                "폴백은 오버레이/오디오 지연 플래그를 내려야 한다 — 남아 있으면 HUD/오디오 어댑터가 결과창을 영원히 미룬다.");
            Assert.That(cinematics.IsMemoryStoneVictoryActive, Is.False);
        }

        [Test]
        public void StageIntroReplayRefusesWithoutCombatStateAndReportsThroughTheHost()
        {
            var host = new StubHost();
            var cinematics = new CombatCinematics(host);

            cinematics.DebugReplayStageIntro();

            Assert.That(host.LastInputMessage, Does.Contain("replay failed"), "가드 실패 사유가 호스트 LastInputMessage로 돌아와야 한다.");
            Assert.That(host.RefreshHudOnlyCalls, Is.EqualTo(1));
            Assert.That(host.StartCoroutineCalls, Is.Zero);
            Assert.That(cinematics.IsStageIntroActive, Is.False);
        }
    }
}
