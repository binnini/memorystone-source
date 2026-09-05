// NOTE(공개 발췌): 이 파일은 서드파티 에셋 「Cartoon FX Remaster (JMO Assets)」의 프리팹·타입을 경로/이름으로만 참조한다. 해당 에셋은 이 리포에 포함되지 않는다.
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using static SeoulPlayup.Combat.Unity.CombatCameraController;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 시네마틱 협력자(4단계 구조 리팩토링 4B-C · 2026-09-04): 기억석 승리 연출(스윕·정화 도시 패스·엔딩 줌인·불꽃)과
    /// 스테이지 인트로(주간 드론 샷·저작 돌리·야간 전환·몬스터 스폰 비트·피날레 안개 리플), 그리고 둘이 나눠 쓰는 룩
    /// 크로스페이드·샷 플레이어. 옛 <c>MapCombatController.Cinematics.cs</c>의 본문을 <b>무변경</b>으로 옮겼고, 호스트에는
    /// <see cref="ICombatCinematicsHost"/>를 통해서만 닿는다 — 사설 필드 직접 접근 0.
    ///
    /// <para>코루틴은 여기서 <c>IEnumerator</c>를 만들고 호스트 MonoBehaviour가 <c>StartCoroutine</c>한다(2단계
    /// <c>RunBossPhaseTransition</c> 선례). 승리 시퀀스 핸들은 여기가 들고, 끊는 것도 <c>host.StopCoroutine</c>.</para>
    ///
    /// <para>호스트에 남긴 것: 직렬화 튜닝 필드 전부(인터페이스 getter로 읽는다)와 그것을 <b>쓰는</b> 디버그 튜닝 3종·
    /// 직렬화 값을 그대로 내보내는 공개 읽기 속성 8종·<c>HideMonsterMarkersForCinematic</c>(마커 직렬화 6필드)·
    /// <c>MemoryStoneVictoryCameraPath</c>(CombatCameraController 시그니처에 잡힌 중첩 타입)·리플렉션이 잡는 static 스텁 3종.
    /// 두 시네마틱이 MapView·Tutorial과 나눠 쓰는 표현 필터(강제 리빌 집합·인트로 숨김 몬스터)는 호스트 필드이고 여기서는
    /// 인터페이스 get/set으로 만진다.</para>
    /// </summary>
    internal sealed class CombatCinematics
    {
        private readonly ICombatCinematicsHost host;

        public CombatCinematics(ICombatCinematicsHost host)
        {
            this.host = host ?? throw new System.ArgumentNullException(nameof(host));
        }

        // ── 호스트·디버그 파사드가 읽고 쓰는 상태 접근자(4B-C) ──
        internal bool IsStageIntroActive => stageIntroActive;
        internal bool IsMemoryStoneVictoryActive => memoryStoneVictoryActive;
        internal bool DeferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes
        {
            get => deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes;
            set => deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes = value;
        }
        internal LookPresetCrossfader StageIntroLookCrossfader
        {
            get => stageIntroLookCrossfader;
            set => stageIntroLookCrossfader = value;
        }
        internal EnvironmentLookPreset StageIntroDayLookPreset => stageIntroDayLookPreset;
        internal EnvironmentLookPreset StageIntroNightLookPreset => stageIntroNightLookPreset;

        /// <summary>디버그 파사드의 주간 룩 전환(옛 ICombatDebugHost 호스트 구현 본문 그대로): 건물 창 발광·프롭 조명을 낮으로 누른다.</summary>
        internal void EngageStageIntroBuildingNightDim()
        {
            if (host.TileView != null)
            {
                stageIntroBuildingNightDimEngaged = true;
                host.TileView.SetMapObjectNightLookWeight(0f);
            }
        }

        public bool MemoryStoneVictoryVirtualCameraActive => memoryStoneVictoryActive;
        public Vector3 MemoryStoneVictoryCameraLiveEulerAngles => memoryStoneVictoryLastCameraRotation.eulerAngles;
        // Last rotation the victory sequence pushed to the shared cinematic camera. The ending beat eases out
        // of it, and the debug panel reads the euler angles back for its live-rotation readout.
        // (4B-A: position/look-at twins were write-only — deleted.)
        private Quaternion memoryStoneVictoryLastCameraRotation = Quaternion.identity;
        internal void BeginMemoryStoneVictoryPresentation()
        {
            // Start the clear/victory BGM the moment the victory presentation begins (covers both the full
            // cinematic path and the immediate-overlay fallback below). The GameVictory stinger is raised
            // separately when the result overlay pops up.
            host.RequestAudioCue(AudioCueIds.MusicVictory, "victory:bgm:start");

            if (!host.PlayMemoryStoneVictorySequence || host.State == null || !host.State.ObjectiveTargetCoord.HasValue)
            {
                deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes = false;
                host.RequestAudioCue(AudioCueIds.GameVictory, "victory:memory-stone:fallback");
                host.ShowGameVictoryOverlay();
                return;
            }

            deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes = true;
            if (memoryStoneVictorySequenceRoutine != null)
            {
                host.StopCoroutine(memoryStoneVictorySequenceRoutine);
                memoryStoneVictorySequenceRoutine = null;
            }

            memoryStoneVictoryIsDebugReplay = false;
            memoryStoneVictorySequenceRoutine = host.StartCoroutine(PlayMemoryStoneVictorySequence(host.State.ObjectiveTargetCoord.Value));
        }
        public void DebugPlayMemoryStoneVictoryPresentation()
        {
            if (host.State == null)
            {
                host.LastInputMessage = "Debug victory presentation failed: combat state is unavailable.";
                host.RefreshHudOnly();
                return;
            }

            host.HideGameVictoryOverlay();
            host.RequestAudioCue(AudioCueIds.MusicVictory, "debug:victory:bgm:start");
            var coord = host.State.ObjectiveTargetCoord ?? host.State.PlayerCoord;
            if (memoryStoneVictorySequenceRoutine != null)
            {
                host.StopCoroutine(memoryStoneVictorySequenceRoutine);
                memoryStoneVictorySequenceRoutine = null;
            }

            deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes = host.PlayMemoryStoneVictorySequence;
            host.LastInputMessage = "Debug victory presentation started.";
            if (!host.PlayMemoryStoneVictorySequence)
            {
                deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes = false;
                host.RequestAudioCue(AudioCueIds.GameVictory, "debug:victory:presentation:fallback");
                host.ShowGameVictoryOverlay();
                host.RefreshHudOnly();
                return;
            }

            // A dev replay keeps playing afterwards, so the sweep's reveal must stay presentation-only.
            memoryStoneVictoryIsDebugReplay = true;
            memoryStoneVictorySequenceRoutine = host.StartCoroutine(PlayMemoryStoneVictorySequence(coord));
            host.RefreshHudOnly();
        }
        private CombatCameraController.MemoryStoneVictoryCameraSettings BuildMemoryStoneVictoryCameraSettings(
            float totalPathLength = 0f) =>
            new CombatCameraController.MemoryStoneVictoryCameraSettings(
                host.MemoryStoneVictoryUseFixedCameraRotation,
                host.MemoryStoneVictoryCameraYawDegrees,
                host.MemoryStoneVictoryCameraPitchDegrees,
                host.MemoryStoneVictoryCameraFocusLag,
                host.MemoryStoneVictoryCameraOrthographicSize,
                CorneringLookAheadProgress(host.MemoryStoneVictoryCorneringLookAheadDistance, totalPathLength));
        // Pushes a victory pose onto the shared cinematic camera and remembers it, so the ending beat can
        // ease out of wherever the sweep actually stopped.
        private void ApplyMemoryStoneVictoryCameraPose(Vector3 position, Vector3 lookAt, Quaternion rotation)
        {
            memoryStoneVictoryLastCameraRotation = rotation;
            host.CameraController.ApplyIntroCameraPose(position, rotation);
        }
        // Eases the sweep camera's heading toward the path direction instead of matching it every frame.
        // The Stage_1 route doubles back at one waypoint (84.5 degrees of heading change), which snapped as a
        // whip; damping spreads that turn over ~a quarter second without altering where the camera goes.
        private Quaternion SmoothMemoryStoneVictoryRotation(Quaternion target)
        {
            if (host.MemoryStoneVictoryYawSmoothingSeconds <= 0f)
            {
                return target;
            }

            var blend = 1f - Mathf.Exp(-host.CinematicDeltaTime / host.MemoryStoneVictoryYawSmoothingSeconds);
            return Quaternion.Slerp(memoryStoneVictoryLastCameraRotation, target, Mathf.Clamp01(blend));
        }
        private void ApplyMemoryStoneVictoryOrbitPose(Vector3 position, Vector3 lookAt)
        {
            var direction = (lookAt + Vector3.up * 0.2f) - position;
            memoryStoneVictoryLastCameraRotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : memoryStoneVictoryLastCameraRotation;
            host.CameraController.ApplyIntroCameraPose(position, lookAt);
        }
        // Drives the night→purified environment crossfade during the victory sweep — the exact mirror of the
        // intro's day→night beat. Null when the stage authored no clear look.
        private LookPresetCrossfader memoryStoneVictoryLookCrossfader;
        // Night -> purified: the stage's night look is the 'from' side (it is what the player has been
        // fighting in) and the authored clear look is the 'to' side. Buildings join the ramp so window
        // emission and prop lights fade out as the sky brightens, exactly as nightfall did in reverse.
        private void BeginMemoryStoneVictoryClearLookCrossfade()
        {
            if (memoryStoneVictoryClearLookPreset == null || stageIntroNightLookPreset == null)
            {
                return;
            }

            memoryStoneVictoryLookCrossfader = new LookPresetCrossfader();
            memoryStoneVictoryLookCrossfader.Begin(stageIntroNightLookPreset, memoryStoneVictoryClearLookPreset);

            if (host.TileView != null)
            {
                memoryStoneVictoryBuildingDayRampEngaged = true;
                host.TileView.SetMapObjectNightLookWeight(1f);
            }
        }
        private void EvaluateMemoryStoneVictoryClearLook(float t)
        {
            t = Mathf.Clamp01(t);
            memoryStoneVictoryLookCrossfader?.Evaluate(t);
            if (memoryStoneVictoryBuildingDayRampEngaged)
            {
                // Inverse of the intro's nightfall ramp: 1 = authored night dressing, 0 = fully unlit windows.
                host.TileView?.SetMapObjectNightLookWeight(1f - t);
            }
        }
        // Whether the sweep completed or was Enter-skipped mid-ramp, the look must land exactly on the
        // purified preset; also the teardown safety net. Idempotent.
        private void SettleMemoryStoneVictoryClearLook()
        {
            memoryStoneVictoryLookCrossfader?.Complete();
            memoryStoneVictoryLookCrossfader = null;
            if (memoryStoneVictoryBuildingDayRampEngaged)
            {
                memoryStoneVictoryBuildingDayRampEngaged = false;
                host.TileView?.SetMapObjectNightLookWeight(0f);
            }
        }
        // A dev replay keeps playing after the presentation, so the purified look must be handed back to the
        // stage's authored night. No-op when the sequence never touched the look (or on a real victory,
        // where the cleared city is the intended end state).
        private void RestoreNightLookAfterMemoryStoneVictoryReplay()
        {
            memoryStoneVictoryLookCrossfader?.Complete();
            memoryStoneVictoryLookCrossfader = null;
            memoryStoneVictoryBuildingDayRampEngaged = false;
            if (memoryStoneVictoryClearLookPreset == null || stageIntroNightLookPreset == null)
            {
                return;
            }

            SeoulPlayup.Map.Unity.LookPresetApplier.Apply(stageIntroNightLookPreset, host.TileView);
            host.TileView?.SetMapObjectNightLookWeight(1f);
        }
        private IEnumerator PlayMemoryStoneVictorySequence(HexCoord memoryStoneCoord)
        {
            memoryStoneVictoryActive = true;
            memoryStoneVictorySkipRequested = false;

            HideGameplayUiForVictory();
            // Victory shows the map as a clean diorama: keep buildings (and the memory stone) but hide
            // monsters, traps and treasure/reward objects so only the cityscape and the goal remain.
            host.TileView?.SetMapObjectVisualsBuildingsOnly(true);
            host.HideMonsterMarkersForCinematic();
            // Same film grammar as the stage intro: no game-piece dressing (nameplates, accent badges) on
            // the diorama. Restored by the teardown in HideGameVictoryOverlay / OnDisable.
            host.ActorMarkers?.SetMarkerUiVisible(false);

            // Presentation-only reveal, mirroring the intro finale. A real victory commits it to the sim in
            // the finalizer (the run is over); a dev replay leaves the run's fog untouched.
            host.CinematicForcedRevealCells = new HashSet<HexCoord>();
            host.RefreshMapVisibilityAndHighlights();

            // Night lifts with the fog: the sweep drives this crossfade from the stage's night look to the
            // authored purified look, so the city reads as waking up behind the advancing reveal.
            BeginMemoryStoneVictoryClearLookCrossfade();

            var cameraPath = BuildMemoryStoneVictoryCameraPath(memoryStoneCoord);
            var settings = BuildMemoryStoneVictoryCameraSettings();
            Vector3 startPosition, startLookAt;
            var startRotation = Quaternion.identity;
            if (cameraPath.IsValid)
            {
                EvaluateVictorySweepPose(cameraPath, 0f, settings, out startPosition, out startLookAt, out startRotation);
            }
            else
            {
                // No usable sweep path (map missing, or fewer than two resolvable focus tiles). The sweep
                // degrades to an instant reveal, but the ending beat still plays — so open on the memory
                // stone's far vantage rather than letting the camera hijack to the world origin.
                var fallbackFocus = host.TryGetTileWorldPosition(memoryStoneCoord, out var stoneWorld)
                    ? stoneWorld + Vector3.up * Mathf.Max(0f, host.StageIntroLookAtHeightOffset)
                    : host.HostTransform.position;
                startLookAt = fallbackFocus;
                startPosition = IntroOrbitCameraPosition(
                    fallbackFocus,
                    0f,
                    Mathf.Clamp(host.StageIntroCameraPitchDegrees, 5f, 89f),
                    Mathf.Max(1f, host.MemoryStoneVictoryEndOrbitDistance));
            }

            // Shared cinematic camera (same rig as the stage intro): it forces the brain to Cut, so the
            // sequence opens on its framing instead of sliding in over the scene's default blend.
            host.CameraController.BeginStageIntroCinemachineCamera(
                startPosition, startLookAt, host.GameplayCamera, host.HostTransform, host.StageIntroCameraFieldOfView);
            if (cameraPath.IsValid)
            {
                ApplyMemoryStoneVictoryCameraPose(startPosition, startLookAt, startRotation);
            }
            else
            {
                ApplyMemoryStoneVictoryOrbitPose(startPosition, startLookAt);
            }

            // Cut 1: chase the purification front along the authored route at a constant world-space speed,
            // so it neither accelerates on long segments nor stutters between waves.
            yield return SweepMemoryStoneVictoryCameraConstantSpeed(
                cameraPath, memoryStoneCoord, BuildMemoryStoneVictoryWaves(memoryStoneCoord));

            if (!memoryStoneVictorySkipRequested)
            {
                if (host.MemoryStoneVictoryRevealRemainingMapAfterWave)
                {
                    RevealAllMemoryStoneVictoryCells();
                }

                // The fireworks deliberately do NOT fire here. They belong to the stone, and the very next
                // statement hard-cuts the camera ~100 units away to the bridge for the whole of cut 2 — a
                // volley let off at this point lives and dies entirely off-screen. Cut 3 fires it instead,
                // once the camera is composed on the stone.

                // Cut 2: hard cut to a low side-on drift over the now-cleared street, then
                // Cut 3: hard cut to the memory stone — orbit, settle, dolly in, and only then the result UI.
                yield return AnimateMemoryStoneVictoryClearedCityPass(cameraPath);
                yield return AnimateMemoryStoneVictoryEndingZoomIn(memoryStoneCoord);
            }

            FinalizeMemoryStoneVictoryPresentation(memoryStoneCoord);
        }
        // Forces the victory presentation to its final state, no matter which beat (or an Enter skip) we
        // arrived from — the counterpart of FinalizeStageIntroToGameplayState. Every skip branch falls
        // through to here so there is one authoritative teardown path.
        private void FinalizeMemoryStoneVictoryPresentation(HexCoord memoryStoneCoord)
        {
            // Authoritative look end-state, whether the sweep completed, was skipped mid-ramp, or never ran.
            SettleMemoryStoneVictoryClearLook();
            RevealAllMemoryStoneVictoryCells();

            // A real victory is terminal, so the swept reveal is committed to the sim and the map stays
            // open behind the result screen. A dev replay must leave the run's fog exactly as it was.
            if (!memoryStoneVictoryIsDebugReplay && host.State != null && host.CinematicForcedRevealCells != null)
            {
                foreach (var coord in host.CinematicForcedRevealCells)
                {
                    host.State.RevealVictoryPathCell(coord);
                }
            }

            host.CinematicForcedRevealCells = null;
            host.RefreshMapVisibilityAndHighlights();
            if (memoryStoneVictoryIsDebugReplay)
            {
                // A replay hands the map back to gameplay, so the markers the cinematic cleared come back.
                // A real victory must NOT: the run is over and re-adding live monsters would repopulate the
                // cleared diorama behind the result screen.
                host.UpdateEnemyMarker();
            }

            host.CameraController.EndStageIntroCinemachineCamera();
            ShowMemoryStoneVictoryOverlayAndAudio(memoryStoneCoord);
            host.RefreshHudOnly();

            memoryStoneVictoryActive = false;
            memoryStoneVictorySkipRequested = false;
            memoryStoneVictorySequenceRoutine = null;
        }
        private void RevealAllMemoryStoneVictoryCells()
        {
            if (host.LoadedMap == null || host.CinematicForcedRevealCells == null)
            {
                return;
            }

            var changed = false;
            foreach (var cell in host.LoadedMap.AllCells)
            {
                changed |= host.CinematicForcedRevealCells.Add(cell.Coord);
            }

            if (changed)
            {
                host.RefreshMapVisibilityAndHighlights();
            }
        }
        // Enter skips the victory presentation and jumps to the result screen, same key and same contract
        // as the stage intro's skip.
        private bool PollMemoryStoneVictorySkip()
        {
            if (!PollStageIntroSkip())
            {
                return false;
            }

            memoryStoneVictorySkipRequested = true;
            return true;
        }
        // Reveals the map along the camera route while moving the victory camera at a constant world-space
        // speed. Progress is mapped through arc length so equal time = equal distance regardless of the
        // uneven spacing between authored route points.
        private IEnumerator SweepMemoryStoneVictoryCameraConstantSpeed(
            MapCombatController.MemoryStoneVictoryCameraPath cameraPath,
            HexCoord memoryStoneCoord,
            IReadOnlyList<MemoryStoneVictoryWave> waves)
        {
            var orderedWaves = waves ?? System.Array.Empty<MemoryStoneVictoryWave>();

            if (!cameraPath.IsValid)
            {
                foreach (var wave in orderedWaves)
                {
                    foreach (var coord in wave.Cells)
                    {
                        host.CinematicForcedRevealCells?.Add(coord);
                    }
                }

                host.RefreshMapVisibilityAndHighlights();
                yield break;
            }

            var cameraPoints = cameraPath.CameraPositions;
            var pointCount = cameraPoints.Count;
            var cumulative = new float[pointCount];
            for (var i = 1; i < pointCount; i++)
            {
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(cameraPoints[i - 1], cameraPoints[i]);
            }

            var totalLength = cumulative[pointCount - 1];
            var speed = Mathf.Max(0.01f, host.MemoryStoneVictorySweepSpeed);
            // Camera and purification share one duration; the front simply runs a constant distance ahead of
            // the camera and converges onto it by the end. The old lag-factor version stretched the camera to
            // 1.8x the reveal time, so the fog finished at 56% of the beat and the last ~6s had nothing
            // brightening at all — the "purification stops partway" report.
            var cameraDuration = totalLength > 0.0001f ? totalLength / speed : 0.01f;
            var leadDistance = Mathf.Max(0f, host.MemoryStoneVictoryRevealLeadDistance);

            var nextWave = 0;
            void RevealWavesUpTo(float progress)
            {
                var changed = false;
                while (nextWave < orderedWaves.Count && orderedWaves[nextWave].NormalizedProgress <= progress)
                {
                    foreach (var coord in orderedWaves[nextWave].Cells)
                    {
                        changed |= host.CinematicForcedRevealCells?.Add(coord) ?? false;
                    }

                    nextWave++;
                }

                if (changed)
                {
                    host.RefreshMapVisibilityAndHighlights();
                }
            }

            var settings = BuildMemoryStoneVictoryCameraSettings(totalLength);
            EvaluateVictorySweepPose(cameraPath, 0f, settings, out var openPosition, out var openLookAt, out var openRotation);
            ApplyMemoryStoneVictoryCameraPose(openPosition, openLookAt, openRotation);
            RevealWavesUpTo(MemoryStoneVictoryProgressFromDistance(cumulative, totalLength, leadDistance));

            // Run until the (slower) camera reaches the end; the reveal finishes earlier and waits.
            var elapsed = 0f;
            while (elapsed < cameraDuration)
            {
                if (PollMemoryStoneVictorySkip())
                {
                    yield break;
                }

                elapsed += host.CinematicDeltaTime;

                var cameraProgressT = Mathf.Clamp01(elapsed / cameraDuration);

                // The front stays a constant distance ahead for the whole pass, so the camera always has
                // cleared ground in front of it. Converging the lead to zero (the previous version) put the
                // camera level with the front near the end, which read as outrunning the purification.
                var cameraDistanceAlongPath = cameraProgressT * totalLength;
                var revealDistance = cameraDistanceAlongPath + leadDistance;
                RevealWavesUpTo(MemoryStoneVictoryProgressFromDistance(cumulative, totalLength, revealDistance));
                // Dawn is paced by the camera, not the (faster) reveal front, so the purified look lands
                // exactly as the sweep arrives instead of finishing early and idling bright.
                EvaluateMemoryStoneVictoryClearLook(cameraProgressT);

                var cameraProgress = MemoryStoneVictoryProgressFromDistance(cumulative, totalLength, cameraDistanceAlongPath);
                EvaluateVictorySweepPose(
                    cameraPath, cameraProgress, settings, out var position, out var lookAt, out var rotation);
                ApplyMemoryStoneVictoryCameraPose(position, lookAt, SmoothMemoryStoneVictoryRotation(rotation));

                yield return null;
            }

            RevealWavesUpTo(1f);
            EvaluateVictorySweepPose(cameraPath, 1f, settings, out var endPosition, out var endLookAt, out var endRotation);
            ApplyMemoryStoneVictoryCameraPose(endPosition, endLookAt, endRotation);
            // Dawn is complete when the sweep lands; the fireworks and ending beat play over settled day.
            SettleMemoryStoneVictoryClearLook();
        }
        // Cut 2: a short, low, side-on pass over a stretch of the route that is now fully purified (on
        // Stage_1 that is the bridge / bike path / apartment street). The camera flies parallel to the
        // route, offset sideways and close to the ground, so the cleared city reads at street level instead
        // of as another overhead shot. Hard-cut in and out, matching the intro's trailer grammar.
        private IEnumerator AnimateMemoryStoneVictoryClearedCityPass(MapCombatController.MemoryStoneVictoryCameraPath cameraPath)
        {
            if (host.MemoryStoneVictorySideViewSeconds <= 0.01f || !cameraPath.IsValid)
            {
                yield break;
            }

            var startProgress = Mathf.Clamp01(host.MemoryStoneVictorySideViewStartProgress);
            var endProgress = Mathf.Clamp01(host.MemoryStoneVictorySideViewEndProgress);
            if (Mathf.Abs(endProgress - startProgress) < 0.01f)
            {
                yield break;
            }

            var focusPoints = cameraPath.FocusPositions;
            var height = Mathf.Max(0.5f, host.MemoryStoneVictorySideViewHeight);

            // Both the pass length and how far to the side it flies are derived from the map rather than
            // taken as flat numbers: the authored values were tuned on Stage_1 and the same numbers read
            // completely differently on a map a third the size.
            var lateralOffset = MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewLateralOffset(
                host.MemoryStoneVictorySideViewLateralOffset,
                ResolveLoadedMapMinHorizontalExtent(),
                host.MemoryStoneVictorySideViewMaxLateralExtentFraction);
            var duration = MemoryStoneVictoryCinematicPlanner.ResolveVictorySideViewSeconds(
                MeasureMemoryStoneVictoryPathTravel(focusPoints, startProgress, endProgress),
                host.MemoryStoneVictorySideViewSpeed,
                host.MemoryStoneVictorySideViewMinSeconds,
                host.MemoryStoneVictorySideViewSeconds);
            if (duration <= 0.01f)
            {
                yield break;
            }

            // Direction of travel sampled as a CHORD across the progress, not the segment direction.
            // EvaluateMemoryStoneVictoryPathDirection is piecewise-constant — it returns the current
            // segment's heading and flips the instant the progress crosses a vertex. Since the camera is
            // offset perpendicular to that heading, every one of the path's 38 vertices teleported the
            // camera sideways, which is the stutter this pass showed in playtest. A chord is continuous.
            const float chord = 0.06f;
            Vector3 ResolveTarget(float progress) =>
                EvaluateMemoryStoneVictoryCameraPosition(focusPoints, Mathf.Clamp01(progress));

            Vector3 ResolveSideOffset(float progress)
            {
                var ahead = ResolveTarget(progress + chord);
                var behind = ResolveTarget(progress - chord);
                var forward = ahead - behind;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    forward = EvaluateMemoryStoneVictoryPathDirection(focusPoints, Mathf.Clamp01(progress));
                }

                return Vector3.Cross(Vector3.up, forward.normalized).normalized * lateralOffset;
            }

            Vector3 ResolvePosition(float progress) =>
                ResolveTarget(progress) + ResolveSideOffset(progress) + Vector3.up * height;

            Vector3 ResolveLookAt(float progress) => ResolveTarget(progress) + Vector3.up * 1.2f;

            // Second pass of smoothing: even a continuous chord still bends at each vertex, so the pose is
            // additionally damped over time. Seeded with the exact opening pose so the cut itself is crisp.
            var smoothedPosition = ResolvePosition(startProgress);
            var smoothedLookAt = ResolveLookAt(startProgress);
            ApplyMemoryStoneVictoryOrbitPose(smoothedPosition, smoothedLookAt);

            const float smoothingSeconds = 0.22f;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (PollMemoryStoneVictorySkip())
                {
                    yield break;
                }

                var step = host.CinematicDeltaTime;
                elapsed += step;
                // Linear in the middle with softened ends: the pass should feel like a steady drift, not a
                // move that accelerates through the street.
                var t = EaseCinematicShot(Mathf.Clamp01(elapsed / duration), CinematicShotEase.EaseInOut);
                var progress = Mathf.Lerp(startProgress, endProgress, t);

                var blend = Mathf.Clamp01(1f - Mathf.Exp(-step / smoothingSeconds));
                smoothedPosition = Vector3.Lerp(smoothedPosition, ResolvePosition(progress), blend);
                smoothedLookAt = Vector3.Lerp(smoothedLookAt, ResolveLookAt(progress), blend);
                ApplyMemoryStoneVictoryOrbitPose(smoothedPosition, smoothedLookAt);
                yield return null;
            }
        }
        /// <summary>
        /// Ground distance the cut-2 camera covers between two progress values, measured by walking the
        /// smoothed path the shot actually follows rather than the straight chord between its ends.
        /// </summary>
        private float MeasureMemoryStoneVictoryPathTravel(
            IReadOnlyList<Vector3> focusPoints,
            float startProgress,
            float endProgress)
        {
            const int samples = 32;
            var travel = 0f;
            var previous = EvaluateMemoryStoneVictoryCameraPosition(focusPoints, Mathf.Clamp01(startProgress));
            for (var i = 1; i <= samples; i++)
            {
                var progress = Mathf.Lerp(startProgress, endProgress, i / (float)samples);
                var current = EvaluateMemoryStoneVictoryCameraPosition(focusPoints, Mathf.Clamp01(progress));
                travel += Vector3.Distance(previous, current);
                previous = current;
            }

            return travel;
        }
        /// <summary>
        /// The smaller of the loaded map's two horizontal extents, in world units — the budget the cut-2
        /// side offset is capped against. Returns 0 when there is no map to measure, which the cap reads as
        /// "leave the authored value alone".
        /// </summary>
        private float ResolveLoadedMapMinHorizontalExtent()
        {
            if (host.LoadedMap == null)
            {
                return 0f;
            }

            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            var any = false;
            foreach (var cell in host.LoadedMap.AllCells)
            {
                if (!host.TryGetTileWorldPosition(cell.Coord, out var world))
                {
                    continue;
                }

                any = true;
                minX = Mathf.Min(minX, world.x);
                maxX = Mathf.Max(maxX, world.x);
                minZ = Mathf.Min(minZ, world.z);
                maxZ = Mathf.Max(maxZ, world.z);
            }

            return any ? Mathf.Min(maxX - minX, maxZ - minZ) : 0f;
        }
        // Cut 3: the memory stone, following the trailer's cut-6 grammar the shot was modelled on — orbit at
        // a held distance, come to a stop, then physically dolly in until the stone fills the frame. The
        // result UI is deliberately withheld until the dolly lands: showing it mid-zoom (the previous
        // version raised it at 55%) is what made the zoom read as stalling half-way.
        private IEnumerator AnimateMemoryStoneVictoryEndingZoomIn(HexCoord memoryStoneCoord)
        {
            if (!host.TryGetTileWorldPosition(memoryStoneCoord, out var stoneWorld))
            {
                ShowMemoryStoneVictoryOverlayAndAudio(memoryStoneCoord);
                yield break;
            }

            var pitch = Mathf.Clamp(host.MemoryStoneVictoryEndPitchDegrees, 5f, 89f);
            var focus = stoneWorld + Vector3.up * Mathf.Max(0f, host.MemoryStoneVictoryEndLookAtHeight);
            var orbitDistance = Mathf.Max(1.5f, host.MemoryStoneVictoryEndOrbitDistance);
            var zoomDistance = Mathf.Clamp(host.MemoryStoneVictoryEndZoomDistance, 1.5f, orbitDistance);

            // The shot OPENS on the framing resolved from the backdrop landmark and rotates away from it,
            // rather than creeping into it — "start on the Lotte World view, then turn and close in".
            // The offset rotates that opening framing bodily around the stone and is the one dial to reach
            // for when the opening view faces the wrong side of the landmark.
            var startAzimuth = ResolveMemoryStoneVictoryEndStartAzimuth(stoneWorld);
            var endAzimuth = startAzimuth + host.MemoryStoneVictoryEndOrbitDegrees;

            // Hard cut into the shot (the cinematic camera's brain blend is Cut), same as the trailer take.
            ApplyMemoryStoneVictoryOrbitPose(IntroOrbitCameraPosition(focus, startAzimuth, pitch, orbitDistance), focus);

            // Celebration goes off HERE, on the first frame the stone is actually on camera. The orbit runs
            // at the held distance (frame is ~14 world units tall there, so a scattered volley above the
            // stone is comfortably inside it) and the orbit+settle beats outlast the volley, so the bursts
            // have faded by the time the dolly closes in for the tight final framing.
            TriggerMemoryStoneVictoryFireworks(memoryStoneCoord);

            // 1) Orbit at a held distance, eased at both ends — the stop is part of the shot, not a cut.
            var orbitSeconds = Mathf.Max(0.01f, host.MemoryStoneVictoryEndOrbitSeconds);
            var elapsed = 0f;
            while (elapsed < orbitSeconds)
            {
                if (PollMemoryStoneVictorySkip())
                {
                    yield break;
                }

                elapsed += host.CinematicDeltaTime;
                var eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / orbitSeconds));
                var azimuth = Mathf.Lerp(startAzimuth, endAzimuth, eased);
                ApplyMemoryStoneVictoryOrbitPose(IntroOrbitCameraPosition(focus, azimuth, pitch, orbitDistance), focus);
                yield return null;
            }

            ApplyMemoryStoneVictoryOrbitPose(IntroOrbitCameraPosition(focus, endAzimuth, pitch, orbitDistance), focus);

            // 2) Hold still on the composed framing.
            var settle = Mathf.Max(0f, host.MemoryStoneVictoryEndSettleSeconds);
            var settleElapsed = 0f;
            while (settleElapsed < settle)
            {
                if (PollMemoryStoneVictorySkip())
                {
                    yield break;
                }

                settleElapsed += host.CinematicDeltaTime;
                yield return null;
            }

            // 3) Physical dolly in along the settled orbit ray (not an FOV zoom, so the perspective matches
            // every other cut) until the stone fills the frame.
            var zoomSeconds = Mathf.Max(0.01f, host.MemoryStoneVictoryEndZoomInSeconds);
            var zoomElapsed = 0f;
            while (zoomElapsed < zoomSeconds)
            {
                if (PollMemoryStoneVictorySkip())
                {
                    yield break;
                }

                zoomElapsed += host.CinematicDeltaTime;
                var eased = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(zoomElapsed / zoomSeconds));
                var distance = Mathf.Lerp(orbitDistance, zoomDistance, eased);
                ApplyMemoryStoneVictoryOrbitPose(IntroOrbitCameraPosition(focus, endAzimuth, pitch, distance), focus);
                yield return null;
            }

            ApplyMemoryStoneVictoryOrbitPose(IntroOrbitCameraPosition(focus, endAzimuth, pitch, zoomDistance), focus);

            // Only now, on the landed close-up, does the result screen come up.
            ShowMemoryStoneVictoryOverlayAndAudio(memoryStoneCoord);

            var hold = Mathf.Max(0f, host.MemoryStoneVictoryFinalHoldSeconds);
            var holdElapsed = 0f;
            while (holdElapsed < hold)
            {
                if (PollMemoryStoneVictorySkip())
                {
                    yield break;
                }

                holdElapsed += host.CinematicDeltaTime;
                yield return null;
            }
        }
        /// <summary>
        /// Azimuth the cut-3 orbit STARTS from. An authored <c>VictoryEndCameraPoint</c> on the map wins:
        /// it names where the camera should come to REST, so the orbit is run backwards out of it and lands
        /// exactly on the authored bearing. Without one, the legacy backdrop-landmark derivation applies.
        /// </summary>
        /// <remarks>
        /// The landmark derivation is correct where a landmark exists (Stage_1 picks the Lotte tower), but a
        /// map with none — TutorialSource has zero — fell through to <c>memoryStoneVictoryCameraYawDegrees</c>,
        /// a scene-level constant shared by every stage on the MainGameplay controller. One number cannot
        /// frame two different maps, which is why the tutorial's closing orbit swung through a building two
        /// cells from the stone. The resting framing is per-map, so it is authored in the map.
        /// </remarks>
        private float ResolveMemoryStoneVictoryEndStartAzimuth(Vector3 stoneWorld)
        {
            if (TryResolveMemoryStoneVictoryAuthoredEndAzimuth(stoneWorld, out var authoredEndAzimuth))
            {
                // The authored point is the LAST frame of the orbit, so wind back by the orbit arc. The
                // azimuth offset is deliberately not applied: it exists to nudge a derived framing, and an
                // authored one is already the answer.
                return authoredEndAzimuth - host.MemoryStoneVictoryEndOrbitDegrees;
            }

            return ResolveMemoryStoneVictoryBackdropAzimuth(stoneWorld) + host.MemoryStoneVictoryEndAzimuthOffsetDegrees;
        }
        /// <summary>
        /// Converts the authored <c>VictoryEndCameraPoint</c> tile into the azimuth that
        /// <see cref="CombatCameraController.IntroOrbitCameraPosition"/> needs in order to place the camera
        /// ON that tile's bearing from the stone. That helper offsets the camera OPPOSITE the azimuth it is
        /// given, so the answer is the point's bearing plus a half turn.
        /// </summary>
        private bool TryResolveMemoryStoneVictoryAuthoredEndAzimuth(Vector3 stoneWorld, out float azimuthDegrees)
        {
            azimuthDegrees = 0f;
            if (!MemoryStoneVictoryCinematicPlanner.TryGetVictoryEndCameraPoint(host.LoadedMap, out var endPoint) ||
                !host.TryGetTileWorldPosition(endPoint.Coord, out var endWorld))
            {
                return false;
            }

            var offset = endWorld - stoneWorld;
            offset.y = 0f;
            if (offset.sqrMagnitude < 0.0001f)
            {
                // Authored on top of the stone: no bearing to read, so leave the derivation alone.
                return false;
            }

            azimuthDegrees = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg + 180f;
            return true;
        }
        /// <summary>
        /// Azimuth that puts the most photogenic nearby landmark directly BEHIND the memory stone, so the
        /// ending shot reads as "the stone, with the skyline behind it". Returns the yaw to hand
        /// <see cref="CombatCameraController.IntroOrbitCameraPosition"/>, whose camera offset already points
        /// opposite the given azimuth — so the landmark's own bearing from the stone is the answer.
        /// </summary>
        /// <remarks>
        /// Landmarks closer than the min distance are rejected: on Stage_1 the Lotte Castle sits 4.6 units
        /// from the stone, so aligning on it would frame a building the camera is practically inside, while
        /// the tower 32 units out is the one that reads as a backdrop. Falls back to the authored yaw offset
        /// when a stage has no qualifying landmark.
        /// </remarks>
        private float ResolveMemoryStoneVictoryBackdropAzimuth(Vector3 stoneWorld)
        {
            if (host.LoadedMap == null)
            {
                return host.MemoryStoneVictoryCameraYawDegrees;
            }

            var minDistance = Mathf.Max(0f, host.MemoryStoneVictoryEndBackdropMinDistance);
            var maxDistance = Mathf.Max(minDistance, host.MemoryStoneVictoryEndBackdropMaxDistance);
            var bestDistance = -1f;
            var bestAzimuth = host.MemoryStoneVictoryCameraYawDegrees;

            foreach (var objectData in host.LoadedMap.ObjectRefs)
            {
                if (!IsStageIntroLandmark(objectData) ||
                    !host.LoadedMap.Contains(objectData.Coord) ||
                    !host.TryGetTileWorldPosition(objectData.Coord, out var landmarkWorld))
                {
                    continue;
                }

                var offset = landmarkWorld - stoneWorld;
                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance < minDistance || distance > maxDistance || distance <= bestDistance)
                {
                    continue;
                }

                // Farthest qualifying landmark wins: depth is what makes it read as a backdrop rather than
                // another object sharing the stone's plane.
                bestDistance = distance;
                bestAzimuth = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            }

            return bestAzimuth;
        }
        private void ShowMemoryStoneVictoryOverlayAndAudio(HexCoord memoryStoneCoord)
        {
            if (host.GameVictoryOverlayRoot != null && host.GameVictoryOverlayRoot.activeSelf)
            {
                return;
            }

            deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes = false;
            host.RequestAudioCue(AudioCueIds.GameVictory, $"victory:memory-stone:complete:{memoryStoneCoord.Q}:{memoryStoneCoord.R}");
            host.ShowGameVictoryOverlay();
            host.RefreshHudOnly();
        }
        private IReadOnlyList<MemoryStoneVictoryWave> BuildMemoryStoneVictoryWaves(HexCoord memoryStoneCoord)
        {
            if (host.LoadedMap == null)
            {
                return new[] { new MemoryStoneVictoryWave(new[] { memoryStoneCoord }, 1f) };
            }

            var startCoord = ResolveMemoryStoneVictoryStartCoord(memoryStoneCoord);
            var focusCoords = MapCombatController.BuildMemoryStoneVictoryFocusCoords(host.LoadedMap, startCoord, memoryStoneCoord);
            return MapCombatController.BuildMemoryStoneVictoryWavesAlongRoute(
                host.LoadedMap,
                MapCombatController.BuildMemoryStoneVictoryRevealRouteSamples(host.LoadedMap, focusCoords),
                host.MemoryStoneVictoryLateralSpreadPercent,
                memoryStoneCoord);
        }
        private MapCombatController.MemoryStoneVictoryCameraPath BuildMemoryStoneVictoryCameraPath(HexCoord memoryStoneCoord)
        {
            if (host.LoadedMap == null)
            {
                return MapCombatController.MemoryStoneVictoryCameraPath.Empty;
            }

            var startCoord = ResolveMemoryStoneVictoryStartCoord(memoryStoneCoord);
            var focusCoords = MapCombatController.BuildMemoryStoneVictoryFocusCoords(host.LoadedMap, startCoord, memoryStoneCoord);
            if (host.MemoryStoneVictoryCameraFollowsRevealRoute)
            {
                focusCoords = MapCombatController.BuildMemoryStoneVictoryRevealRouteSamples(host.LoadedMap, focusCoords);
            }

            var focusPoints = focusCoords
                .Select(coord => host.TryGetTileWorldPosition(coord, out var worldPosition) ? (Vector3?)worldPosition : null)
                .Where(worldPosition => worldPosition.HasValue)
                .Select(worldPosition => worldPosition.Value)
                .ToArray();

            if (focusPoints.Length < 2)
            {
                return MapCombatController.MemoryStoneVictoryCameraPath.Empty;
            }

            // Round the authored corners before deriving camera positions, so both arrays stay 1:1 (the
            // sweep evaluates camera and focus at the same normalized progress) and the camera turns through
            // waypoints instead of snapping at them.
            var routedFocusPoints = RoundPolylineCorners(focusPoints, host.MemoryStoneVictoryCornerRadius, 5);

            return new MapCombatController.MemoryStoneVictoryCameraPath(
                BuildMemoryStoneVictoryCameraPositions(routedFocusPoints),
                routedFocusPoints,
                usesAuthoredPoints: !host.MemoryStoneVictoryCameraFollowsRevealRoute && MemoryStoneVictoryCinematicPlanner.HasUsableVictoryCameraPoints(host.LoadedMap));
        }
        private HexCoord ResolveMemoryStoneVictoryStartCoord(HexCoord memoryStoneCoord)
        {
            return host.LoadedMap != null && MapCombatController.TryFindPlayerSpawnObjectCoord(host.LoadedMap, out var playerSpawn)
                ? playerSpawn
                : host.State != null ? host.State.PlayerCoord : memoryStoneCoord;
        }
        private IReadOnlyList<Vector3> BuildMemoryStoneVictoryCameraPositions(IReadOnlyList<Vector3> focusPoints)
        {
            if (focusPoints == null || focusPoints.Count == 0)
            {
                return System.Array.Empty<Vector3>();
            }

            if (focusPoints.Count == 1)
            {
                return new[] { focusPoints[0] };
            }

            var height = Mathf.Max(1f, host.MemoryStoneVictoryCameraHeight);
            var positions = new Vector3[focusPoints.Count];

            for (var i = 0; i < focusPoints.Count; i++)
            {
                positions[i] = focusPoints[i] + Vector3.up * height;
            }

            return positions;
        }
        // A volley rather than a single pop: one burst reads as a stray effect at this scale, and the cue
        // carries two CFXR firework prefabs so successive bursts do not look identical. Scattered around the
        // stone and staggered in time. Runs as its own coroutine so the sequence keeps moving underneath it.
        private void TriggerMemoryStoneVictoryFireworks(HexCoord coord)
        {
            host.StartCoroutine(PlayMemoryStoneVictoryFireworkVolley(coord));
            host.RequestAudioCue(AudioCueIds.ObjectiveMemoryGyeolRevealed, $"victory:memory-stone:{coord.Q}:{coord.R}");
            if (host.MemoryStoneVictoryFireworkShakeStrength <= 0f)
            {
                return;
            }

            host.AddCinemachineCameraShake(host.MemoryStoneVictoryFireworkShakeStrength, 0.45f, 14, 120f);
        }
        private IEnumerator PlayMemoryStoneVictoryFireworkVolley(HexCoord coord)
        {
            var bursts = Mathf.Max(1, host.MemoryStoneVictoryFireworkBurstCount);
            var interval = Mathf.Max(0f, host.MemoryStoneVictoryFireworkBurstInterval);
            var scatter = Mathf.Max(0f, host.MemoryStoneVictoryFireworkScatterRadius);

            for (var i = 0; i < bursts; i++)
            {
                // Golden-angle spiral: deterministic (a replay films the same volley) but never lands two
                // bursts on the same bearing, and the radius grows so the volley opens outward.
                var angle = i * 137.508f * Mathf.Deg2Rad;
                var radius = bursts > 1 ? scatter * (i / (float)(bursts - 1)) : 0f;
                var offset = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                PlayMemoryStoneVictoryFireworkVfx(coord, offset);

                if (interval > 0f && i < bursts - 1)
                {
                    var waited = 0f;
                    while (waited < interval)
                    {
                        waited += host.CinematicDeltaTime;
                        yield return null;
                    }
                }
            }
        }
        private void PlayMemoryStoneVictoryFireworkVfx(HexCoord coord, Vector3 worldOffset)
        {
            var presentation = host.ResolveMovementEffectPresentation();
            if (presentation == null || !host.TryGetTileWorldPosition(coord, out var worldPosition))
            {
                return;
            }

            var resultEvent = new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                appliedAmount: 1,
                center: coord,
                sourceRef: MapCombatController.MemoryStoneVictoryFireworkVfxCue);
            presentation.Play(resultEvent, worldPosition + worldOffset, Quaternion.identity);
        }
        private void HideGameplayUiForVictory()
        {
            memoryStoneVictoryUiHider ??= new CinematicUiHider(
                () => MapCombatController.FindSceneRectTransform(host.HostScene, GameplaySceneContract.GameplayLayerRootName),
                ShouldKeepUiVisibleDuringMemoryStoneVictory);
            memoryStoneVictoryUiHider.Hide();
        }
        // Restore is called on teardown paths too; never instantiate the hider there.
        internal void RestoreGameplayUiAfterVictory() => memoryStoneVictoryUiHider?.Restore();
        /// <summary>
        /// The only two things that survive the victory cinematic's UI hide: the world map (it IS the shot)
        /// and the result overlay (raised while the hider is still engaged, at the end of cut 3).
        /// </summary>
        /// <remarks>
        /// The sidebar used to be kept as well, which left the deck/map/relic rail down the side of every
        /// frame of a sequence whose whole point is a clean diorama. Dropping it puts victory on the same
        /// grammar as the stage intro, which keeps the world map and nothing else. Restoring is unchanged
        /// and still happens on every teardown path, so the rail is back the moment the result screen is
        /// dismissed — and while it is up the rail would be behind an opaque panel anyway.
        /// </remarks>
        private static bool ShouldKeepUiVisibleDuringMemoryStoneVictory(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            return objectName == GameplaySceneContract.WorldMapLayerName
                || objectName == MapCombatController.GameVictoryOverlayRootName;
        }
        // ── 4B-A(2026-09-04): 본체에서 옮겨온 시네마틱 전용 런타임 상태(직렬화 아님 · 본문 무변경). 시네마틱 파셜 밖에서
        // 읽는 곳은 IsCinematicViewActive·입력 게이트·오디오/HUD 어댑터·디버그 파사드뿐이다. 두 시네마틱이 공유하는
        // 표현 필터(cinematicForcedRevealCells·stageIntroHiddenMonsterIds)는 MapView·Tutorial도 쓰므로 본체에 남긴다.
        private Coroutine memoryStoneVictorySequenceRoutine;
        private CinematicUiHider memoryStoneVictoryUiHider;
        // Victory cinematic run state, mirroring the stage intro's: active guards the presentation-only
        // view filters, skipRequested is set by the Enter poll, and isDebugReplay marks a dev re-run that
        // must not commit the sweep's reveal to the sim (a real victory is terminal, so it may).
        private bool memoryStoneVictoryActive;
        private bool memoryStoneVictorySkipRequested;
        private bool memoryStoneVictoryIsDebugReplay;
        private CinematicUiHider stageIntroUiHider;
        // Dev-only re-run of the stage intro (DebugReplayStageIntro); null unless a replay take is in flight.
        private Coroutine stageIntroReplayRoutine;
        private bool stageIntroSkipRequested;
        private bool stageIntroActive;
        // True while the intro has map-object night dressing (window emission + prop lights) pressed to
        // day; every teardown path must snap it back to weight 1 (authored night).
        private bool stageIntroBuildingNightDimEngaged;
        // True while the victory sweep has map-object night dressing ramped toward the purified (day) look;
        // every teardown path must settle it — to weight 0 on a real victory, back to 1 on a dev replay.
        private bool memoryStoneVictoryBuildingDayRampEngaged;
        private bool deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes;
        // Runtime-injected day/night looks for the intro's day→night crossfade (see SetStageIntroLookPresets).
        // Not serialized: sourced per-stage from StageDefinition each run, not authored on the scene component.
        private SeoulPlayup.Map.Unity.EnvironmentLookPreset stageIntroDayLookPreset;
        private SeoulPlayup.Map.Unity.EnvironmentLookPreset stageIntroNightLookPreset;
        // Purified look the victory sweep crossfades to (see SetMemoryStoneVictoryClearLookPreset).
        private SeoulPlayup.Map.Unity.EnvironmentLookPreset memoryStoneVictoryClearLookPreset;
        // Runtime-injected by MainGameplayController: the daytime look shown during the intro trailer and the
        // stage's night look to crossfade back to over the finale. Day null = no day treatment (camera-only).
        public void SetStageIntroLookPresets(
            SeoulPlayup.Map.Unity.EnvironmentLookPreset dayPreset,
            SeoulPlayup.Map.Unity.EnvironmentLookPreset nightPreset)
        {
            stageIntroDayLookPreset = dayPreset;
            stageIntroNightLookPreset = nightPreset;
        }
        // Runtime-injected by MainGameplayController: the purified look the victory sweep crossfades to as
        // the fog lifts (the exact mirror of the intro's day->night beat). Null = no lighting change.
        // The night side of that crossfade is the stage look injected above.
        public void SetMemoryStoneVictoryClearLookPreset(SeoulPlayup.Map.Unity.EnvironmentLookPreset clearPreset)
        {
            memoryStoneVictoryClearLookPreset = clearPreset;
        }
        // Drives the day→night environment crossfade during the intro; null when the stage has no day look.
        private LookPresetCrossfader stageIntroLookCrossfader;
        // ----- Stage intro cinematic -------------------------------------------------------------
        // Opens a stage as a short trailer: day drone shots over the map (overview, authored street
        // dolly, landmarks) with every monster still hidden, then night falls (day→night crossfade
        // beat), then monsters spawn one by one — a trap-style burst at the spawn point with a
        // low-angle close-up arc per featured monster — and finally the fog-of-war ripples over the
        // whole map from the memory stone. Runs inside BeginGameplayStartSequence while
        // gameStartCompleted is still false, so the turn never starts and input stays blocked.
        /// <summary>
        /// Dev-only: replays the stage intro cinematic mid-run, so a trailer take can be re-shot (and
        /// authored dolly points re-checked) without restarting the stage. Counterpart of
        /// <see cref="DebugPlayMemoryStoneVictoryPresentation"/>; surfaced by CombatDebugControlPanel,
        /// which is gated behind <see cref="CombatDebugControlPanel.DebugUiAvailable"/>.
        /// See docs/trailer-capture-plan.md (Phase 1).
        /// </summary>
        /// <remarks>
        /// No state is rebuilt here on purpose: <see cref="PlayStageIntroSequence"/> owns its whole
        /// setup/teardown contract (hide set BEFORE reveal-all, UI hider, day-look crossfader, marker UI,
        /// and the single authoritative <see cref="FinalizeStageIntroToGameplayState"/> exit), so a replay
        /// only has to guarantee that no second run overlaps an in-flight one. The day/night presets injected
        /// once by MainGameplayController persist on the component, so the day borrow works on every replay.
        /// </remarks>
        public void DebugReplayStageIntro()
        {
            if (!Application.isPlaying || host.State == null || host.LoadedMap == null)
            {
                host.LastInputMessage = "Stage intro replay failed: combat state is unavailable.";
                host.RefreshHudOnly();
                return;
            }

            if (stageIntroActive)
            {
                host.LastInputMessage = "Stage intro is already playing (Enter skips it).";
                host.RefreshHudOnly();
                return;
            }

            stageIntroReplayRoutine = host.StartCoroutine(ReplayStageIntroRoutine());
            host.LastInputMessage = "Stage intro replay started.";
            host.RefreshHudOnly();
        }
        /// <summary>
        /// Dev-only: plays ONE authored dolly zone (0 = the first run) on its own — the trailer's cut 1 or
        /// cut 2 — instead of the whole intro. Same borrowed state as the intro (day look, hidden monsters,
        /// no UI, no overlays) so a recorded take matches what the intro would produce, but ~5-7s instead of
        /// ~35s, which is what makes A/B-ing authored angles practical. See docs/trailer-capture-plan.md.
        /// </summary>
        public void DebugPlayStageIntroDollyRun(int runIndex)
        {
            if (!Application.isPlaying || host.State == null || host.LoadedMap == null)
            {
                host.LastInputMessage = "Dolly take failed: combat state is unavailable.";
                host.RefreshHudOnly();
                return;
            }

            if (stageIntroActive)
            {
                host.LastInputMessage = "A stage intro / dolly take is already playing (Enter skips it).";
                host.RefreshHudOnly();
                return;
            }

            stageIntroReplayRoutine = host.StartCoroutine(PlayStageIntroDollyRunRoutine(runIndex));
            host.LastInputMessage = $"Dolly take {runIndex} started.";
            host.RefreshHudOnly();
        }
        /// <summary>True while a stage intro or an isolated dolly take is playing; polled by capture tooling.</summary>
        public bool IsStageIntroPlaying => stageIntroActive;
        private IEnumerator PlayStageIntroDollyRunRoutine(int runIndex)
        {
            var shots = new List<CinematicShot>();
            AppendStageIntroAuthoredDollyShots(shots, runIndex);
            if (shots.Count == 0)
            {
                host.LastInputMessage = $"Dolly take failed: run {runIndex} has no shots.";
                host.RefreshHudOnly();
                stageIntroReplayRoutine = null;
                yield break;
            }

            // Same borrowed state as PlayStageIntroSequence, in the same order (hide set BEFORE reveal-all).
            stageIntroActive = true;
            stageIntroSkipRequested = false;
            host.SuppressHoverDuringStageIntro();

            host.StageIntroHiddenMonsterIds = new HashSet<string>(
                host.State.Monsters.Where(monster => !monster.IsDead).Select(monster => monster.Id),
                System.StringComparer.Ordinal);
            host.ActorMarkers.SetMarkerUiVisible(false);

            var previousRevealAll = host.RevealAllMapCellsInDebugMode;
            host.SetDebugRevealAllMapCells(true, refresh: true);
            HideAllGameplayUiForCinematic();

            // Explicit refresh, not the one SetDebugRevealAllMapCells happens to trigger: that call is a
            // no-op when reveal-all is ALREADY on, and then nothing would rebuild the markers/overlays for
            // the hide set above — the take would film the map with its monsters, nameplates and tile
            // overlays still showing.
            host.RefreshView();

            // Cuts 1-2 are DAY shots: the intro borrows the day look for its whole dolly stretch and only
            // crosses to night later. Without this the take would film the authored path under night light.
            if (stageIntroDayLookPreset != null && stageIntroNightLookPreset != null)
            {
                stageIntroLookCrossfader = new LookPresetCrossfader();
                stageIntroLookCrossfader.Begin(stageIntroDayLookPreset, stageIntroNightLookPreset);
                if (host.TileView != null)
                {
                    stageIntroBuildingNightDimEngaged = true;
                    host.TileView.SetMapObjectNightLookWeight(0f);
                }
            }

            host.CameraController.BeginStageIntroCinemachineCamera(
                shots[0].FromPos, shots[0].FromLookAt, host.GameplayCamera, host.HostTransform, host.StageIntroCameraFieldOfView);

            for (var i = 0; i < shots.Count && !stageIntroSkipRequested; i++)
            {
                yield return AnimateCinematicShot(shots[i]);
            }

            // Single authoritative teardown, exactly as the full sequence ends (also snaps the look to night).
            FinalizeStageIntroToGameplayState(previousRevealAll);
            stageIntroReplayRoutine = null;
            host.LastInputMessage = $"Dolly take {runIndex} finished.";
            host.RefreshHudOnly();
        }
        /// <summary>
        /// Dev-only: plays the intro's second half on its own — night transition, featured monster spawn
        /// beats, memory stone shot, fog ripple finale — which is the trailer's cut #3. Counterpart of
        /// <see cref="DebugPlayStageIntroDollyRun"/> for the non-dolly stretch: same borrowed state and
        /// the same single authoritative teardown, but ~12s instead of the full ~35s intro, so takes can
        /// be re-shot without editing the dolly runs out afterwards. See docs/trailer-capture-plan.md.
        /// </summary>
        public void DebugPlayStageIntroFinaleRun()
        {
            DebugPlayStageIntroFinaleRun(
                skipSpawnBeats: false, widePitchOverrideDegrees: 0f, overviewDistanceScale: 1f,
                rippleCameraMode: 0, rippleYawSweepDegrees: 0f);
        }
        /// <summary>
        /// Take-only variation knobs for the cut #3 ladder (docs/trailer-capture-plan.md). All defaults
        /// reproduce the plain overload; the in-game intro never passes through here.
        /// widePitchOverrideDegrees &lt;= 0 keeps the authored pitch; overviewDistanceScale scales the wide
        /// beats' orbit radius; wideHeightScale lowers ONLY the camera's altitude over the wide beats
        /// (same horizontal offset, aim unchanged); rippleCameraMode 0 = straight pull-back, 1 = orbit
        /// pull-back (yaw sweep), 2 = vertical crane (hold the memory stone framing and rise);
        /// endHoldSeconds holds the settled final frame before teardown so a recorded take does not end
        /// abruptly.
        /// </summary>
        public void DebugPlayStageIntroFinaleRun(
            bool skipSpawnBeats,
            float widePitchOverrideDegrees,
            float overviewDistanceScale,
            int rippleCameraMode,
            float rippleYawSweepDegrees,
            float wideHeightScale = 1f,
            float endHoldSeconds = 0f)
        {
            if (!Application.isPlaying || host.State == null || host.LoadedMap == null)
            {
                host.LastInputMessage = "Finale take failed: combat state is unavailable.";
                host.RefreshHudOnly();
                return;
            }

            if (stageIntroActive)
            {
                host.LastInputMessage = "A stage intro / dolly take is already playing (Enter skips it).";
                host.RefreshHudOnly();
                return;
            }

            stageIntroReplayRoutine = host.StartCoroutine(PlayStageIntroFinaleRunRoutine(
                skipSpawnBeats, widePitchOverrideDegrees, overviewDistanceScale,
                rippleCameraMode, rippleYawSweepDegrees, wideHeightScale, endHoldSeconds));
            host.LastInputMessage = "Finale take started.";
            host.RefreshHudOnly();
        }
        private IEnumerator PlayStageIntroFinaleRunRoutine(
            bool skipSpawnBeats,
            float widePitchOverrideDegrees,
            float overviewDistanceScale,
            int rippleCameraMode,
            float rippleYawSweepDegrees,
            float wideHeightScale,
            float endHoldSeconds)
        {
            if (!TryBuildStageIntroFoci(out var overviewCenter, out var hasOverview, out var landmarkFoci) ||
                (!hasOverview && landmarkFoci.Count == 0))
            {
                host.LastInputMessage = "Finale take failed: no overview/landmark foci on this map.";
                host.RefreshHudOnly();
                stageIntroReplayRoutine = null;
                yield break;
            }

            HexCoord memoryStoneCoord = default;
            var memoryStoneWorld = Vector3.zero;
            var hasMemoryStone = false;
            if (host.State.ObjectiveTargetCoord.HasValue &&
                host.TryGetTileWorldPosition(host.State.ObjectiveTargetCoord.Value, out var resolvedMemoryStoneWorld))
            {
                hasMemoryStone = true;
                memoryStoneCoord = host.State.ObjectiveTargetCoord.Value;
                memoryStoneWorld = resolvedMemoryStoneWorld + Vector3.up * Mathf.Max(0f, host.StageIntroLookAtHeightOffset);
            }

            // Same borrowed state as PlayStageIntroSequence, in the same order (hide set BEFORE reveal-all).
            stageIntroActive = true;
            stageIntroSkipRequested = false;
            host.SuppressHoverDuringStageIntro();

            host.StageIntroHiddenMonsterIds = new HashSet<string>(
                host.State.Monsters.Where(monster => !monster.IsDead).Select(monster => monster.Id),
                System.StringComparer.Ordinal);
            host.ActorMarkers.SetMarkerUiVisible(false);

            var previousRevealAll = host.RevealAllMapCellsInDebugMode;
            host.SetDebugRevealAllMapCells(true, refresh: true);

            // Explicit, because the call above is a no-op when reveal-all is already on — see
            // PlayStageIntroDollyRunRoutine for the failure this prevents.
            host.RefreshView();
            HideAllGameplayUiForCinematic();

            // Cut #3 opens on the tail end of day: the crossfader starts pressed to the day look and the
            // night transition beat rides it to night, exactly as the full sequence does mid-trailer.
            if (stageIntroDayLookPreset != null && stageIntroNightLookPreset != null)
            {
                stageIntroLookCrossfader = new LookPresetCrossfader();
                stageIntroLookCrossfader.Begin(stageIntroDayLookPreset, stageIntroNightLookPreset);
                if (host.TileView != null)
                {
                    stageIntroBuildingNightDimEngaged = true;
                    host.TileView.SetMapObjectNightLookWeight(0f);
                }
            }

            var pitch = Mathf.Clamp(host.StageIntroCameraPitchDegrees, 5f, 89f);
            // The wide beats (night drift, finale pull-back) take the ladder's pitch/distance; the memory
            // stone focus shot keeps the authored framing so only one variable moves per take.
            var widePitch = widePitchOverrideDegrees > 0f ? Mathf.Clamp(widePitchOverrideDegrees, 5f, 89f) : pitch;
            var orbitDistance = Mathf.Max(0.5f, host.StageIntroOrbitDistance);
            var overviewDistance = ResolveStageIntroOverviewDistance(overviewCenter)
                * Mathf.Max(0.1f, overviewDistanceScale <= 0f ? 1f : overviewDistanceScale);

            // Height-only ladder: keep the wide beats' horizontal orbit radius and aim, scale just the
            // camera's altitude by k. Expressed through the existing (pitch, distance) parameterization:
            // pitch' = atan(k·tan(p)) and distance' = d·√(cos²p + k²·sin²p) land on exactly that position.
            if (wideHeightScale > 0f && !Mathf.Approximately(wideHeightScale, 1f))
            {
                var widePitchRadians = widePitch * Mathf.Deg2Rad;
                widePitch = Mathf.Atan(wideHeightScale * Mathf.Tan(widePitchRadians)) * Mathf.Rad2Deg;
                overviewDistance *= Mathf.Sqrt(
                    Mathf.Cos(widePitchRadians) * Mathf.Cos(widePitchRadians)
                    + wideHeightScale * wideHeightScale * Mathf.Sin(widePitchRadians) * Mathf.Sin(widePitchRadians));
            }

            var seed = ComputeStageIntroTrailerSeed(overviewCenter, landmarkFoci.Count);

            // Built but never played: the memory stone shot's framing is seeded with the dolly shot count
            // in the full sequence, so reproducing the count keeps this take frame-identical to the intro.
            var unplayedShots = BuildStageIntroTrailerShots(
                overviewCenter, hasOverview, landmarkFoci, overviewDistance, orbitDistance, pitch, seed);

            // Open exactly where the night transition starts its drift (its chordHalf is 10).
            var startPosition = IntroOrbitCameraPosition(overviewCenter, 10f, widePitch, overviewDistance);
            host.CameraController.BeginStageIntroCinemachineCamera(
                startPosition, overviewCenter, host.GameplayCamera, host.HostTransform, host.StageIntroCameraFieldOfView);

            yield return AnimateStageIntroNightTransition(overviewCenter, hasOverview, overviewDistance, widePitch);

            if (!stageIntroSkipRequested && !skipSpawnBeats)
            {
                yield return AnimateStageIntroMonsterSpawnShots(seed);
            }

            var finaleRipplesRemainingMonsters = hasMemoryStone && !stageIntroSkipRequested;
            if (!finaleRipplesRemainingMonsters)
            {
                RevealRemainingStageIntroMonsters(playVfx: !stageIntroSkipRequested);
            }

            if (finaleRipplesRemainingMonsters)
            {
                var memoryStoneShot = BuildStageIntroFocusShot(
                    memoryStoneWorld, orbitDistance, pitch, seed, unplayedShots.Count);
                yield return AnimateCinematicShot(memoryStoneShot);

                // Mode 2 (vertical crane) rises straight out of the memory stone framing and keeps aiming
                // at the stone, so the ripple reads as concentric rings; every other mode pulls back to
                // the map-center wide as before.
                Vector3 finaleToCamera, finaleToLookAt;
                if (rippleCameraMode == 2)
                {
                    finaleToCamera = memoryStoneShot.ToPos + Vector3.up * (overviewDistance * 0.85f);
                    finaleToLookAt = memoryStoneShot.ToLookAt;
                }
                else
                {
                    finaleToCamera = IntroOrbitCameraPosition(overviewCenter, 0f, widePitch, overviewDistance);
                    finaleToLookAt = overviewCenter;
                }

                if (!stageIntroSkipRequested)
                {
                    yield return AnimateStageIntroFinale(
                        memoryStoneCoord, memoryStoneShot.ToPos, memoryStoneShot.ToLookAt,
                        finaleToCamera, finaleToLookAt,
                        rippleCameraMode == 1 ? rippleYawSweepDegrees : 0f);
                }

                RevealRemainingStageIntroMonsters(playVfx: false);
            }

            // Breathing room before teardown (사용자 판정: 리플 직후 뚝 끊기면 편집 여유가 없다). The
            // borrowed state is still engaged here, so the recorder keeps capturing a clean settled frame.
            var endHoldElapsed = 0f;
            while (endHoldElapsed < endHoldSeconds && !stageIntroSkipRequested)
            {
                endHoldElapsed += host.CinematicDeltaTime;
                yield return null;
            }

            FinalizeStageIntroToGameplayState(previousRevealAll);
            stageIntroReplayRoutine = null;
            host.LastInputMessage = "Finale take finished.";
            host.RefreshHudOnly();
        }
        private IEnumerator ReplayStageIntroRoutine()
        {
            yield return PlayStageIntroSequence();

            // Same settle wait as BeginGameplayStartSequence: the intro only lowers the intro vcam's
            // priority, so the brain is still blending back to the gameplay camera for a few frames after
            // the sequence returns. Bounded so a missing brain / Cut blend can never stall the run.
            var cameraSettleTimeoutSeconds = 3f;
            while (host.CameraController.IsCameraBlending && cameraSettleTimeoutSeconds > 0f)
            {
                cameraSettleTimeoutSeconds -= host.CinematicDeltaTime;
                yield return null;
            }

            stageIntroReplayRoutine = null;
            host.LastInputMessage = "Stage intro replay finished.";
            host.RefreshHudOnly();
        }
        internal IEnumerator PlayStageIntroSequence()
        {
            if (!Application.isPlaying || host.State == null || host.LoadedMap == null)
            {
                yield break;
            }

            if (!TryBuildStageIntroFoci(out var overviewCenter, out var hasOverview, out var landmarkFoci) ||
                (!hasOverview && landmarkFoci.Count == 0))
            {
                yield break;
            }

            // The memory stone (objective) gets a special finale instead of a normal orbit dwell.
            HexCoord memoryStoneCoord = default;
            var memoryStoneWorld = Vector3.zero;
            var hasMemoryStone = false;
            if (host.State.ObjectiveTargetCoord.HasValue &&
                host.TryGetTileWorldPosition(host.State.ObjectiveTargetCoord.Value, out var resolvedMemoryStoneWorld))
            {
                hasMemoryStone = true;
                memoryStoneCoord = host.State.ObjectiveTargetCoord.Value;
                // Raise the focus above the tile so a tall memory stone is vertically centered.
                memoryStoneWorld = resolvedMemoryStoneWorld + Vector3.up * Mathf.Max(0f, host.StageIntroLookAtHeightOffset);
            }

            stageIntroActive = true;
            stageIntroSkipRequested = false;
            host.SuppressHoverDuringStageIntro();

            // Monsters start invisible (presentation only; the sim spawned them at combat start). The
            // reveal-all sweep below must not expose them: they enter one by one during the spawn beats
            // after night falls, and any without a beat pop in right before the finale. Set the hide
            // BEFORE reveal-all so its refresh already applies the filter.
            host.StageIntroHiddenMonsterIds = new HashSet<string>(
                host.State.Monsters.Where(monster => !monster.IsDead).Select(monster => monster.Id),
                System.StringComparer.Ordinal);

            // Film grammar: monsters revealed during the intro carry no game-piece dressing (name/HP
            // nameplate, accent badge). Tile overlays are suppressed separately via stageIntroActive in
            // RefreshMapVisibilityAndHighlights. Restored by FinalizeStageIntroToGameplayState/OnDisable.
            host.ActorMarkers.SetMarkerUiVisible(false);

            // Temporarily reveal the whole map (visual only) so landmarks/memory stone are not hidden
            // by fog during the sweep, then restore the prior fog state before the first turn.
            var previousRevealAll = host.RevealAllMapCellsInDebugMode;
            host.SetDebugRevealAllMapCells(true, refresh: true);

            // Explicit, because the call above is a no-op when reveal-all is already on (debug fog off, a
            // replay, a re-entered stage) — and then nothing would rebuild markers/overlays for the hide
            // set, leaving monsters and their nameplates on screen through the whole intro.
            host.RefreshView();

            // Hide every gameplay UI layer except the world map itself, so the intro plays like a film.
            HideAllGameplayUiForCinematic();

            // Borrow the day look for the trailer (held until the mid-trailer night transition beat). Only when
            // the stage supplies both a day preset and a night look to return to; otherwise the trailer plays
            // over the normal night look.
            if (stageIntroDayLookPreset != null && stageIntroNightLookPreset != null)
            {
                stageIntroLookCrossfader = new LookPresetCrossfader();
                stageIntroLookCrossfader.Begin(stageIntroDayLookPreset, stageIntroNightLookPreset);

                // Buildings join the day borrow: their night dressing (window emission, prop lights) is
                // pressed to the day look and ramps back alongside the night transition beat's crossfade.
                if (host.TileView != null)
                {
                    stageIntroBuildingNightDimEngaged = true;
                    host.TileView.SetMapObjectNightLookWeight(0f);
                }
            }

            var pitch = Mathf.Clamp(host.StageIntroCameraPitchDegrees, 5f, 89f);
            var orbitDistance = Mathf.Max(0.5f, host.StageIntroOrbitDistance);
            var overviewDistance = ResolveStageIntroOverviewDistance(overviewCenter);

            // Build the whole trailer up front (opening wide establishing shot + one lateral dolly per
            // landmark). Deterministic per map layout so a stage always frames the same way.
            var seed = ComputeStageIntroTrailerSeed(overviewCenter, landmarkFoci.Count);
            var shots = BuildStageIntroTrailerShots(
                overviewCenter, hasOverview, landmarkFoci, overviewDistance, orbitDistance, pitch, seed);

            // Establish the opening pose from the first shot (or a plain framing if there are no shots).
            Vector3 startPosition, startLookAt;
            if (shots.Count > 0)
            {
                startPosition = shots[0].FromPos;
                startLookAt = shots[0].FromLookAt;
            }
            else
            {
                var startFocus = hasOverview ? overviewCenter : (landmarkFoci.Count > 0 ? landmarkFoci[0] : memoryStoneWorld);
                var startDistance = hasOverview ? overviewDistance : orbitDistance;
                startPosition = IntroOrbitCameraPosition(startFocus, 0f, pitch, startDistance);
                startLookAt = startFocus;
            }

            host.CameraController.BeginStageIntroCinemachineCamera(
                startPosition, startLookAt, host.GameplayCamera, host.HostTransform, host.StageIntroCameraFieldOfView);

            // Play each shot as a slow straight dolly, cutting hard between shots (trailer grammar).
            for (var i = 0; i < shots.Count && !stageIntroSkipRequested; i++)
            {
                yield return AnimateCinematicShot(shots[i]);
            }

            // Night falls mid-trailer: a wide drift over the map while the day look crossfades to the
            // stage's night look. The crossfade is completed here (not in the finale), so the monster
            // spawn beats below and the finale's fog ripple both play over the settled night.
            if (!stageIntroSkipRequested)
            {
                yield return AnimateStageIntroNightTransition(overviewCenter, hasOverview, overviewDistance, pitch);
            }

            // Monster spawn beats: hard-cut to a low-angle close-up arcing around a few spawn points;
            // each beat fires the spawn VFX, then its monster appears mid-arc.
            if (!stageIntroSkipRequested)
            {
                yield return AnimateStageIntroMonsterSpawnShots(seed);
            }

            // Memory stone finale: cut to a framing on the memory stone and ease toward it, then ripple
            // darkness outward from it while the camera pulls back to a wide shot of the map center.
            var finaleRipplesRemainingMonsters = hasMemoryStone && !stageIntroSkipRequested;
            if (!finaleRipplesRemainingMonsters)
            {
                // No finale to interleave with (no memory stone, or an Enter skip): every monster that did
                // not get its own beat spawns off-camera now, keeping the featured beats short. VFX still
                // fires so wide framings read as a map coming alive.
                RevealRemainingStageIntroMonsters(playVfx: !stageIntroSkipRequested);
            }

            if (finaleRipplesRemainingMonsters)
            {
                var finaleWideCamera = IntroOrbitCameraPosition(overviewCenter, 0f, pitch, overviewDistance);
                var memoryStoneShot = BuildStageIntroFocusShot(memoryStoneWorld, orbitDistance, pitch, seed, shots.Count);
                yield return AnimateCinematicShot(memoryStoneShot);

                if (!stageIntroSkipRequested)
                {
                    yield return AnimateStageIntroFinale(
                        memoryStoneCoord, memoryStoneShot.ToPos, memoryStoneShot.ToLookAt, finaleWideCamera, overviewCenter);
                }

                // Safety net: the finale reveals the remaining monsters as the darkness front reaches them,
                // but an Enter skip (or a monster on a cell the ripple never reported) can leave some hidden.
                // No VFX here — the beat is over, and a burst after the ripple would read as a stray flash.
                RevealRemainingStageIntroMonsters(playVfx: false);
            }

            FinalizeStageIntroToGameplayState(previousRevealAll);
        }
        // Forces the stage intro to its final gameplay state, no matter which step (or an Enter skip) we
        // arrived from. Every coroutine skip-branch snaps its own layer to the end pose and returns, so the
        // sequence falls through to a single call here; this keeps one authoritative teardown path.
        // Extension point: phases 3-4 add the light-preset crossfade and building night re-swap, whose
        // final states must also be applied here so a mid-transition Enter lands on the same end result.
        private void FinalizeStageIntroToGameplayState(bool previousRevealAll)
        {
            // Snap the environment to the final night look and dispose the crossfade's temp rig/volume. Safe
            // (and a no-op) when no day treatment ran; this is also the skip path's authoritative end state.
            stageIntroLookCrossfader?.Complete();
            stageIntroLookCrossfader = null;
            SnapStageIntroBuildingNightLook();

            host.CameraController.EndStageIntroCinemachineCamera();
            RestoreAllGameplayUiAfterCinematic();
            host.CinematicForcedRevealCells = null;
            host.StageIntroHiddenMonsterIds = null;
            host.ActorMarkers.SetMarkerUiVisible(true);
            host.SetDebugRevealAllMapCells(previousRevealAll, refresh: true);
            host.RecenterGameplayCameraOnPlayer(immediate: true);
            stageIntroSkipRequested = false;
            stageIntroActive = false;
        }
        // Darkness ripples out from the memory stone tile (cells leave the forced-reveal set and fall back
        // to real fog) while the camera eases from the memory stone out to a wide shot of the map center.
        private IEnumerator AnimateStageIntroFinale(
            HexCoord memoryStoneCoord,
            Vector3 fromCameraPosition,
            Vector3 fromLookAt,
            Vector3 toCameraPosition,
            Vector3 toLookAt,
            float rippleYawSweepDegrees = 0f)
        {
            if (host.LoadedMap == null)
            {
                yield break;
            }

            // Hand off from the global reveal-all flag to a per-cell forced-reveal set covering every cell,
            // so there is no flash when we turn the debug flag off.
            var forced = new HashSet<HexCoord>();
            var ordered = new List<HexCoord>();
            var maxDistance = 0;
            foreach (var cell in host.LoadedMap.AllCells)
            {
                forced.Add(cell.Coord);
                ordered.Add(cell.Coord);
                maxDistance = Mathf.Max(maxDistance, memoryStoneCoord.DistanceTo(cell.Coord));
            }

            ordered.Sort((a, b) => memoryStoneCoord.DistanceTo(a).CompareTo(memoryStoneCoord.DistanceTo(b)));

            // Storyboard cut #3: the monsters without a featured spawn beat appear *while* the darkness
            // rolls out, each one popping in just ahead of the front so the shot reads as "the map keeps
            // spawning as it goes dark" instead of a bulk pop before the ripple starts. Queued in the same
            // distance order as the cells, so one cursor walks both.
            var pendingReveals = BuildStageIntroFinaleMonsterReveals(memoryStoneCoord);
            var revealLead = Mathf.Max(0f, host.StageIntroFinaleMonsterRevealLeadDistance);
            var revealIndex = 0;

            host.CinematicForcedRevealCells = forced;
            host.SetDebugRevealAllMapCells(false, refresh: false);
            host.RefreshMapVisibilityAndHighlights();

            var duration = Mathf.Max(0.01f, host.StageIntroFinaleSeconds);
            var elapsed = 0f;
            var removeIndex = 0;

            while (elapsed < duration && !stageIntroSkipRequested)
            {
                if (PollStageIntroSkip())
                {
                    stageIntroSkipRequested = true;
                    break;
                }

                elapsed += host.CinematicDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);

                // (The day→night crossfade used to share this t; it now completes during the night
                // transition beat, so the finale drives only the fog-of-war ripple over settled night.)

                // Expanding ring of darkness from the memory stone: remove every cell now inside the radius.
                var radius = t * (maxDistance + 1);

                // Spawn just ahead of the front (revealLead cells of headroom) so each monster is seen
                // arriving before its tile goes dark. Reveals ride the forced-reveal set like the tiles do,
                // so the same advancing darkness swallows them a moment later.
                while (revealIndex < pendingReveals.Count && pendingReveals[revealIndex].distance <= radius + revealLead)
                {
                    var reveal = pendingReveals[revealIndex];
                    revealIndex++;
                    if (reveal.hasWorld)
                    {
                        PlayStageIntroMonsterSpawnVfx(reveal.coord, reveal.world);
                    }

                    RevealStageIntroMonster(reveal.monsterId);
                }

                var changed = false;
                while (removeIndex < ordered.Count && memoryStoneCoord.DistanceTo(ordered[removeIndex]) <= radius)
                {
                    forced.Remove(ordered[removeIndex]);
                    removeIndex++;
                    changed = true;
                }

                if (changed)
                {
                    host.RefreshMapVisibilityAndHighlights();
                    // Monster markers share the forced-reveal set (see IsMonsterVisibleForPresentation),
                    // so the advancing darkness swallows monsters in sync with their tiles.
                    host.UpdateEnemyMarker();
                }

                var ease = Mathf.SmoothStep(0f, 1f, t);
                var rippleCameraPosition = Vector3.Lerp(fromCameraPosition, toCameraPosition, ease);
                var rippleLookAt = Vector3.Lerp(fromLookAt, toLookAt, ease);
                // Take-only orbit pull-back (cut #3 ladder): the pull-back path additionally swings around
                // the current aim point, so the camera circles the darkening city while it retreats.
                if (rippleYawSweepDegrees != 0f)
                {
                    rippleCameraPosition = rippleLookAt
                        + Quaternion.Euler(0f, rippleYawSweepDegrees * ease, 0f) * (rippleCameraPosition - rippleLookAt);
                }

                host.CameraController.ApplyIntroCameraPose(rippleCameraPosition, rippleLookAt);
                yield return null;
            }

            // Drop the override entirely so the real fog (player vision only) takes over, and settle the
            // camera on the map center.
            host.CinematicForcedRevealCells = null;
            host.RefreshMapVisibilityAndHighlights();
            host.UpdateEnemyMarker();
            var settledCameraPosition = rippleYawSweepDegrees != 0f
                ? toLookAt + Quaternion.Euler(0f, rippleYawSweepDegrees, 0f) * (toCameraPosition - toLookAt)
                : toCameraPosition;
            host.CameraController.ApplyIntroCameraPose(settledCameraPosition, toLookAt);
        }
        // One queued spawn for a monster that gets no featured beat, ordered by how soon the finale's
        // darkness front reaches it.
        private readonly struct StageIntroFinaleMonsterReveal
        {
            public readonly string monsterId;
            public readonly HexCoord coord;
            public readonly Vector3 world;
            public readonly bool hasWorld;
            public readonly float distance;

            public StageIntroFinaleMonsterReveal(string monsterId, HexCoord coord, Vector3 world, bool hasWorld, float distance)
            {
                this.monsterId = monsterId;
                this.coord = coord;
                this.world = world;
                this.hasWorld = hasWorld;
                this.distance = distance;
            }
        }
        // Snapshot of the still-hidden monsters at finale start, sorted by ring distance from the memory
        // stone so the finale can walk them with a single cursor alongside the darkening cells. Empty when
        // every monster already got a featured spawn beat.
        private List<StageIntroFinaleMonsterReveal> BuildStageIntroFinaleMonsterReveals(HexCoord memoryStoneCoord)
        {
            var reveals = new List<StageIntroFinaleMonsterReveal>();
            if (host.State == null || host.StageIntroHiddenMonsterIds == null || host.StageIntroHiddenMonsterIds.Count == 0)
            {
                return reveals;
            }

            foreach (var monster in host.State.Monsters)
            {
                if (monster.IsDead || !host.StageIntroHiddenMonsterIds.Contains(monster.Id))
                {
                    continue;
                }

                var hasWorld = host.TryGetTileWorldPosition(monster.Coord, out var tileWorld);
                reveals.Add(new StageIntroFinaleMonsterReveal(
                    monster.Id, monster.Coord, tileWorld, hasWorld, memoryStoneCoord.DistanceTo(monster.Coord)));
            }

            reveals.Sort((a, b) => a.distance.CompareTo(b.distance));
            return reveals;
        }
        // Wide drift over the map while the day look crossfades to night. Owns the crossfade's whole
        // lifetime past this point: whether the beat completes or is Enter-skipped, the crossfader is
        // Completed (night authoritative) and released, so later beats never re-drive it.
        private IEnumerator AnimateStageIntroNightTransition(
            Vector3 overviewCenter, bool hasOverview, float overviewDistance, float pitch)
        {
            if (stageIntroLookCrossfader == null || !stageIntroLookCrossfader.IsActive)
            {
                yield break;
            }

            // Mirror of the opening wide, swept the other way, so the trailer bookends read as a pair.
            // Without an overview center the camera simply holds its last pose while night falls.
            Vector3 fromPos = default, toPos = default;
            if (hasOverview)
            {
                const float chordHalf = 10f;
                fromPos = IntroOrbitCameraPosition(overviewCenter, chordHalf, pitch, overviewDistance);
                toPos = IntroOrbitCameraPosition(overviewCenter, -chordHalf, pitch, overviewDistance);
                host.CameraController.ApplyIntroCameraPose(fromPos, overviewCenter);
            }

            var duration = Mathf.Max(0.01f, host.StageIntroNightTransitionSeconds);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (PollStageIntroSkip())
                {
                    stageIntroSkipRequested = true;
                    break;
                }

                elapsed += host.CinematicDeltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                stageIntroLookCrossfader.Evaluate(t);
                // Building night dressing (window emission + prop lights) ramps back on the same t, so
                // the sky, global lighting and the city's own lights all read as one nightfall.
                if (stageIntroBuildingNightDimEngaged)
                {
                    host.TileView?.SetMapObjectNightLookWeight(t);
                }

                if (hasOverview)
                {
                    var eased = EaseCinematicShot(t, CinematicShotEase.EaseInOut);
                    host.CameraController.ApplyIntroCameraPose(Vector3.Lerp(fromPos, toPos, eased), overviewCenter);
                }

                yield return null;
            }

            stageIntroLookCrossfader.Complete();
            stageIntroLookCrossfader = null;
            SnapStageIntroBuildingNightLook();
        }
        // Whether the transition finished or was Enter-skipped mid-ramp, buildings must land exactly on
        // their authored night dressing; also the skip/teardown safety net (Finalize, OnDisable).
        internal void SnapStageIntroBuildingNightLook()
        {
            if (!stageIntroBuildingNightDimEngaged)
            {
                return;
            }

            stageIntroBuildingNightDimEngaged = false;
            if (host.TileView != null)
            {
                host.TileView.SetMapObjectNightLookWeight(1f);
            }
        }
        // One close-up beat per featured monster spawn: hard cut to a low-angle framing of the spawn
        // tile, fire the spawn VFX, reveal the monster mid-arc, and keep arcing past it. A single sweep
        // direction is used for the whole run so the arc reads as one continuous camera move carried
        // across the cuts (the reference cut-editing grammar).
        private IEnumerator AnimateStageIntroMonsterSpawnShots(int seed)
        {
            if (host.State == null)
            {
                yield break;
            }

            var featured = SelectStageIntroFeaturedMonsters();
            if (featured.Count == 0)
            {
                yield break;
            }

            var sweepDirection = new System.Random(seed * 31 + 7).NextDouble() < 0.5 ? 1f : -1f;
            for (var i = 0; i < featured.Count && !stageIntroSkipRequested; i++)
            {
                var monster = featured[i];
                if (!host.TryGetTileWorldPosition(monster.Coord, out var tileWorld))
                {
                    continue;
                }

                yield return AnimateStageIntroMonsterSpawnShot(
                    monster, tileWorld, BuildStageIntroMonsterSpawnShot(tileWorld, seed, i, sweepDirection));
            }
        }
        // Boss first, then elites, then the rest; prefers unseen monster kinds so the beats read as a
        // roster introduction instead of three copies of the same creature. Deterministic (no shuffle):
        // State.Monsters keeps authoring order, and OrderByDescending is a stable sort.
        private List<MonsterRuntimeState> SelectStageIntroFeaturedMonsters()
        {
            var featured = new List<MonsterRuntimeState>();
            var count = Mathf.Max(0, host.StageIntroMonsterSpawnShotCount);
            if (host.State == null || count == 0)
            {
                return featured;
            }

            var ordered = host.State.Monsters
                // 보스 기물은 인트로 소개 대상이 아니다(스테이지 인트로 시점에는 아직 존재하지도 않지만,
                // 재개 세이브로 기물이 살아 있는 상태에서 인트로가 돌 수 있어 명시적으로 뺀다).
                .Where(monster => !monster.IsDead && !MonsterSpawnRoles.IsProp(monster.SpawnRole))
                .OrderByDescending(monster => MonsterSpawnRoles.IsBoss(monster.SpawnRole) ? 2 : MonsterSpawnRoles.IsElite(monster.SpawnRole) ? 1 : 0)
                .ToList();

            var seenDefinitions = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (var monster in ordered)
            {
                if (featured.Count >= count)
                {
                    return featured;
                }

                if (seenDefinitions.Add(monster.DefinitionId))
                {
                    featured.Add(monster);
                }
            }

            foreach (var monster in ordered)
            {
                if (featured.Count >= count)
                {
                    break;
                }

                if (!featured.Any(candidate => candidate.Id == monster.Id))
                {
                    featured.Add(monster);
                }
            }

            return featured;
        }
        // Low-angle close-up arc past a spawn tile, seeded like the landmark shots but with its own
        // tighter distance/pitch authoring so monsters read as heroes rather than map features.
        private CinematicShot BuildStageIntroMonsterSpawnShot(Vector3 tileWorld, int seed, int shotIndex, float sweepDirection)
        {
            var rng = new System.Random(seed + 977 * (shotIndex + 1));
            float Next01() => (float)rng.NextDouble();

            var focus = tileWorld + Vector3.up * Mathf.Max(0f, host.StageIntroMonsterSpawnLookAtHeight);
            var azCenter = Next01() * 360f;                                   // seeded viewing side per beat
            var chordHalf = Mathf.Lerp(14f, 18f, Next01());                   // short lateral arc
            var pitch = Mathf.Clamp(host.StageIntroMonsterSpawnPitchDegrees + Mathf.Lerp(-4f, 6f, Next01()), 5f, 89f);
            var distance = Mathf.Max(1.5f, host.StageIntroMonsterSpawnDistance) * Mathf.Lerp(0.9f, 1.15f, Next01());

            var fromPos = IntroOrbitCameraPosition(focus, azCenter - sweepDirection * chordHalf, pitch, distance);
            var toPos = IntroOrbitCameraPosition(focus, azCenter + sweepDirection * chordHalf, pitch, distance);
            return new CinematicShot(fromPos, toPos, focus, focus, Mathf.Max(0.01f, host.StageIntroMonsterSpawnShotSeconds));
        }
        // Cut, VFX, reveal, keep arcing. The reveal happens a short beat into the shot so the trap-style
        // burst reads first and the monster appears out of it. An Enter skip still reveals the monster
        // (never leave a beat's monster hidden longer than its beat).
        private IEnumerator AnimateStageIntroMonsterSpawnShot(
            MonsterRuntimeState monster, Vector3 tileWorld, CinematicShot shot)
        {
            host.CameraController.ApplyIntroCameraPose(shot.FromPos, shot.FromLookAt);
            PlayStageIntroMonsterSpawnVfx(monster.Coord, tileWorld);

            var duration = Mathf.Max(0.01f, shot.Duration);
            var revealDelay = Mathf.Clamp(host.StageIntroMonsterSpawnRevealDelaySeconds, 0f, duration);
            // The attack swing must land at least one frame after the reveal: RevealStageIntroMonster ->
            // UpdateEnemyMarker creates the marker and its Animator on that frame, and a trigger set before
            // the animator binds is dropped silently (that is why the swing never played).
            var attackDelay = Mathf.Clamp(host.StageIntroMonsterSpawnAttackDelaySeconds, revealDelay, duration);
            var revealed = false;
            var attacked = false;
            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (PollStageIntroSkip())
                {
                    stageIntroSkipRequested = true;
                    RevealStageIntroMonster(monster.Id);
                    host.CameraController.ApplyIntroCameraPose(shot.ToPos, shot.ToLookAt);
                    yield break;
                }

                elapsed += host.CinematicDeltaTime;
                var eased = EaseCinematicShot(Mathf.Clamp01(elapsed / duration), shot.Ease);
                var cameraPosition = Vector3.Lerp(shot.FromPos, shot.ToPos, eased);

                var revealedThisFrame = false;
                if (!revealed && elapsed >= revealDelay)
                {
                    revealed = true;
                    revealedThisFrame = true;
                    RevealStageIntroMonster(monster.Id);
                }

                if (revealed)
                {
                    // Hold the monster turned to camera for the whole beat: it must not only greet the
                    // camera on the reveal frame but stay facing it as the shot arcs past.
                    FaceStageIntroMonsterAtCamera(monster.Id, tileWorld, cameraPosition);

                    // revealedThisFrame keeps the one-frame gap even if the attack delay is authored down
                    // to the reveal delay — the trigger must never share the marker's creation frame.
                    if (!attacked && !revealedThisFrame && elapsed >= attackDelay)
                    {
                        attacked = true;
                        // Featured monsters greet the camera with their attack swing (reference: dynamic
                        // motion in every close-up). No authored trigger is passed on purpose: the
                        // MonsterRuntimeState projection carries only the pattern id, so the presenter
                        // resolves whichever attack parameter the controller exposes (monsters author
                        // Attack1..5, never "AttackTrigger"). Safe no-op if the marker/visual is missing.
                        host.ActorMarkers.TriggerAttack(monster.Id);
                    }
                }

                host.CameraController.ApplyIntroCameraPose(
                    cameraPosition,
                    Vector3.Lerp(shot.FromLookAt, shot.ToLookAt, eased));
                yield return null;
            }

            if (!revealed)
            {
                RevealStageIntroMonster(monster.Id);
            }

            host.CameraController.ApplyIntroCameraPose(shot.ToPos, shot.ToLookAt);
        }
        // Turns a featured monster's model to the camera (horizontal only, so a low-angle shot doesn't
        // tip it back). Overwriting intent facing is safe for the intro's duration because
        // ShouldUpdateMonsterIntentFacing is closed while stageIntroActive.
        internal void FaceStageIntroMonsterAtCamera(string monsterId, Vector3 monsterWorld, Vector3 cameraPosition)
        {
            var toCamera = cameraPosition - monsterWorld;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude < 0.0001f)
            {
                return;
            }

            host.ActorMarkers.SetFacingDirection(monsterId, toCamera);
        }
        private void RevealStageIntroMonster(string monsterId)
        {
            if (host.StageIntroHiddenMonsterIds == null || !host.StageIntroHiddenMonsterIds.Remove(monsterId))
            {
                return;
            }

            host.UpdateEnemyMarker();
        }
        // Drops the whole hide set (monsters without a featured beat spawn off-camera). VFX still fires
        // at their tiles so wide framings read as the map coming alive; skipped when Enter-skipping.
        private void RevealRemainingStageIntroMonsters(bool playVfx)
        {
            if (host.StageIntroHiddenMonsterIds == null)
            {
                return;
            }

            var remaining = host.StageIntroHiddenMonsterIds;
            host.StageIntroHiddenMonsterIds = null;

            if (playVfx && host.State != null && remaining.Count > 0)
            {
                foreach (var monster in host.State.Monsters)
                {
                    if (!monster.IsDead && remaining.Contains(monster.Id) &&
                        host.TryGetTileWorldPosition(monster.Coord, out var tileWorld))
                    {
                        PlayStageIntroMonsterSpawnVfx(monster.Coord, tileWorld);
                    }
                }
            }

            host.UpdateEnemyMarker();
        }
        // Reuses the trap-activation burst (cue "trap.trigger") as the spawn flash. Resolved by cue id so
        // the beat is pinned to that exact authored entry; the sourceRef keeps the "trap." prefix purely
        // as a fallback path (prefix tier) if the cue id is ever renamed. No floating text: this is a
        // spawn flourish, not a combat result.
        internal void PlayStageIntroMonsterSpawnVfx(HexCoord coord, Vector3 tileWorld)
        {
            var presentation = host.ResolveMovementEffectPresentation();
            if (presentation == null)
            {
                return;
            }

            var resultEvent = new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "field",
                center: coord,
                sourceRef: StageIntroMonsterSpawnVfxSourceRef);

            EffectVfxCatalog.Entry entry = null;
            var catalog = presentation.VfxCatalog;
            if (catalog != null)
            {
                foreach (var candidate in catalog.Entries)
                {
                    if (candidate != null &&
                        string.Equals(candidate.CueId, StageIntroMonsterSpawnVfxCueId, System.StringComparison.Ordinal))
                    {
                        entry = candidate;
                        break;
                    }
                }
            }

            if (entry != null)
            {
                presentation.PlayResolvedEntry(resultEvent, entry, tileWorld, Quaternion.identity, showFloatingText: false);
            }
            else
            {
                presentation.Play(resultEvent, tileWorld, Quaternion.identity);
            }
        }
        private const string StageIntroMonsterSpawnVfxCueId = "trap.trigger";
        private const string StageIntroMonsterSpawnVfxSourceRef = "trap.intro-monster-spawn";
        private bool TryBuildStageIntroFoci(out Vector3 overviewCenter, out bool hasOverview, out List<Vector3> landmarkFoci)
        {
            overviewCenter = Vector3.zero;
            hasOverview = false;
            landmarkFoci = new List<Vector3>();
            if (host.LoadedMap == null)
            {
                return false;
            }

            // Opening wide overview, framed on the map centroid.
            if (TryGetStageIntroMapCenter(out var center))
            {
                overviewCenter = center;
                hasOverview = true;
            }

            // Landmarks, in authoring order. (The memory stone is handled separately by the finale.)
            // Raise the focus above the tile so tall objects (towers) are vertically centered.
            var lookAtLift = Vector3.up * Mathf.Max(0f, host.StageIntroLookAtHeightOffset);
            foreach (var coord in GetStageIntroLandmarkCoords())
            {
                if (host.TryGetTileWorldPosition(coord, out var landmarkWorld))
                {
                    landmarkFoci.Add(landmarkWorld + lookAtLift);
                }
            }

            return hasOverview || landmarkFoci.Count > 0;
        }
        private float ResolveStageIntroOverviewDistance(Vector3 center)
        {
            if (host.StageIntroOverviewDistance > 0f)
            {
                return host.StageIntroOverviewDistance;
            }

            // Auto-fit: frame the whole map by backing off proportional to its bounding radius.
            var radius = 0f;
            if (host.LoadedMap != null)
            {
                foreach (var cell in host.LoadedMap.AllCells)
                {
                    if (host.TryGetTileWorldPosition(cell.Coord, out var worldPosition))
                    {
                        radius = Mathf.Max(radius, Vector3.Distance(center, worldPosition));
                    }
                }
            }

            return Mathf.Max(host.StageIntroOrbitDistance * 2f, radius * 1.4f);
        }
        private bool TryGetStageIntroMapCenter(out Vector3 center)
        {
            center = Vector3.zero;
            if (host.LoadedMap == null)
            {
                return false;
            }

            var sum = Vector3.zero;
            var count = 0;
            foreach (var cell in host.LoadedMap.AllCells)
            {
                if (host.TryGetTileWorldPosition(cell.Coord, out var worldPosition))
                {
                    sum += worldPosition;
                    count++;
                }
            }

            if (count == 0)
            {
                return false;
            }

            center = sum / count;
            return true;
        }
        private IEnumerable<HexCoord> GetStageIntroLandmarkCoords()
        {
            if (host.LoadedMap == null)
            {
                yield break;
            }

            foreach (var objectData in host.LoadedMap.ObjectRefs)
            {
                if (host.LoadedMap.Contains(objectData.Coord) && IsStageIntroLandmark(objectData))
                {
                    yield return objectData.Coord;
                }
            }
        }
        private static bool IsStageIntroLandmark(HexMapObjectData objectData)
        {
            return string.Equals(objectData.ObjectType, "Landmark", System.StringComparison.Ordinal)
                || string.Equals(objectData.Role?.Trim(), "landmark", System.StringComparison.OrdinalIgnoreCase);
        }
        // Deterministic seed derived from the map layout (no runtime map name is available), so the same
        // stage always produces the same shot framing/variation.
        private static int ComputeStageIntroTrailerSeed(Vector3 overviewCenter, int landmarkCount)
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + Mathf.RoundToInt(overviewCenter.x * 10f);
                hash = hash * 31 + Mathf.RoundToInt(overviewCenter.z * 10f);
                hash = hash * 31 + landmarkCount;
                return hash;
            }
        }
        // Opening establishing wide (gentle lateral drift across the whole map) + one lateral-dolly shot
        // per landmark. The memory stone is not included here; it gets the finale shot instead.
        private List<CinematicShot> BuildStageIntroTrailerShots(
            Vector3 overviewCenter,
            bool hasOverview,
            List<Vector3> landmarkFoci,
            float overviewDistance,
            float orbitDistance,
            float basePitch,
            int seed)
        {
            var shots = new List<CinematicShot>();

            if (hasOverview)
            {
                // Clean establishing wide at the authored pitch (no random variation) so the map reads first.
                const float chordHalf = 10f;
                var fromPos = IntroOrbitCameraPosition(overviewCenter, -chordHalf, basePitch, overviewDistance);
                var toPos = IntroOrbitCameraPosition(overviewCenter, chordHalf, basePitch, overviewDistance);
                shots.Add(new CinematicShot(
                    fromPos, toPos, overviewCenter, overviewCenter, Mathf.Max(0.01f, host.StageIntroOverviewHoldSeconds)));
            }

            // Authored "street dolly" chain (IntroCameraPoint markers), between the establishing wide and
            // the landmark orbits. No-op when the map has no authored points (full fallback: unchanged).
            AppendStageIntroAuthoredDollyShots(shots);

            for (var i = 0; i < landmarkFoci.Count; i++)
            {
                shots.Add(BuildStageIntroFocusShot(landmarkFoci[i], orbitDistance, basePitch, seed, i));
            }

            return shots;
        }
        // Appends a straight-line camera dolly through the authored IntroCameraPoint markers in order, split
        // into separate "zones": a point flagged cameraStartsNewSegment begins a NEW run, so the camera
        // hard-cuts to it instead of dollying in from the previous point (the shot loop already cuts to each
        // shot's FromPos, so dropping the connecting segment IS the cut). Within a run the move is continuous;
        // each segment's duration is length/(global speed x start point's cameraSpeedMultiplier) so the felt
        // speed stays constant, and the camera aims a look-ahead point along the direction of travel. A point
        // with cameraDwellSeconds > 0 holds (zero-move shot) on arrival — or, for a run's first point, as a
        // lead-in beat. A run needs >= 2 points to travel; a lone point (e.g. a 1-point zone) is ignored.
        private void AppendStageIntroAuthoredDollyShots(List<CinematicShot> shots) =>
            AppendStageIntroAuthoredDollyShots(shots, onlyRunIndex: -1);
        // onlyRunIndex >= 0 emits just that zone (0 = the first run), which is how a trailer take films one
        // authored cut on its own instead of sitting through the whole intro. -1 emits every run, which is
        // what the intro itself uses.
        private void AppendStageIntroAuthoredDollyShots(List<CinematicShot> shots, int onlyRunIndex)
        {
            if (host.LoadedMap == null)
            {
                return;
            }

            var globalCameraHeight = Mathf.Max(0f, host.StageIntroDollyCameraHeight);
            // Deliberately NOT stageIntroLookAtHeightOffset: that one raises the aim so a *tall landmark*
            // is vertically centered in an orbit shot. Reusing it here aimed the dolly ABOVE its own camera
            // (height 2.2 looking at +5), which framed nothing but sky. The authored dolly aims at the
            // street itself, so it gets its own — normally near-zero — offset.
            var lookAtLift = Mathf.Max(0f, host.StageIntroDollyLookAtHeightOffset);

            var points = new List<(Vector3 pos, float lookY, float speedMultiplier, float dwell, float yaw, bool yawKeepsFraming)>();
            var isRunStart = new List<bool>();
            var groundYs = new List<float>();
            var pointHeights = new List<float>();
            foreach (var objectData in MemoryStoneVictoryCinematicPlanner.GetOrderedIntroCameraPoints(host.LoadedMap))
            {
                if (host.TryGetTileWorldPosition(objectData.Coord, out var world))
                {
                    // Per-point height (0 = global) so two zones over the same street can be framed
                    // differently — a low pass over the bridge, a high wide sweep over the city.
                    var height = objectData.CameraHeightOverride > 0f
                        ? objectData.CameraHeightOverride
                        : globalCameraHeight;
                    points.Add((
                        world + Vector3.up * height,
                        world.y + lookAtLift,
                        objectData.CameraSpeedMultiplier,
                        objectData.CameraDwellSeconds,
                        objectData.CameraYawOffsetDegrees,
                        objectData.CameraYawKeepsFraming));
                    isRunStart.Add(objectData.CameraStartsNewSegment);
                    groundYs.Add(world.y);
                    pointHeights.Add(height);
                }
            }

            if (points.Count < 2)
            {
                return;
            }

            // Altitude flattening: within one run the camera flies like a drone at a single altitude,
            // referenced to the run's HIGHEST waypoint ground. Waypoint positions ride each tile's own
            // surface Y, and the shots interpolate positions linearly — so a terrain step between
            // waypoints (잠실대교 진입로 0.2 → 상판 0.7) put a slope kink in the path at the waypoint,
            // which reads as a visible jolt mid-run (every cut-1 take showed it at role 01). The aim
            // height is flattened to the same reference so the camera pitch stays kink-free too.
            {
                var flattenRunStart = 0;
                for (var i = 1; i <= points.Count; i++)
                {
                    if (i != points.Count && !isRunStart[i])
                    {
                        continue;
                    }

                    var referenceGroundY = float.MinValue;
                    for (var j = flattenRunStart; j < i; j++)
                    {
                        referenceGroundY = Mathf.Max(referenceGroundY, groundYs[j]);
                    }

                    for (var j = flattenRunStart; j < i; j++)
                    {
                        var entry = points[j];
                        var pos = entry.pos;
                        pos.y = referenceGroundY + pointHeights[j];
                        points[j] = (pos, referenceGroundY + lookAtLift,
                            entry.speedMultiplier, entry.dwell, entry.yaw, entry.yawKeepsFraming);
                    }

                    flattenRunStart = i;
                }
            }

            var speed = Mathf.Max(0.01f, host.StageIntroDollySpeedUnitsPerSecond);
            var lookAhead = Mathf.Max(0f, host.StageIntroDollyLookAheadDistance);

            // Partition into runs at zone boundaries (a flagged point starts a new run; index 0 always does),
            // then build each run as its own continuous, hard-cut-in dolly.
            var runStart = 0;
            var runIndex = 0;
            for (var i = 1; i <= points.Count; i++)
            {
                var boundary = i == points.Count || isRunStart[i];
                if (!boundary)
                {
                    continue;
                }

                if (onlyRunIndex < 0 || runIndex == onlyRunIndex)
                {
                    AppendStageIntroDollyRun(shots, points, runStart, i, speed, lookAhead);
                }

                runIndex++;
                runStart = i;
            }
        }
        // Builds one continuous dolly run over points[start, end). The run's first pose is cut to (the shot
        // loop snaps to FromPos), giving the zone cut for free. Easing shapes each run on its own: a single
        // segment eases in-out, otherwise the first eases in, the last eases out, and the middle runs linear.
        private void AppendStageIntroDollyRun(
            List<CinematicShot> shots,
            List<(Vector3 pos, float lookY, float speedMultiplier, float dwell, float yaw, bool yawKeepsFraming)> points,
            int start,
            int end,
            float speed,
            float lookAhead)
        {
            var runSegmentCount = end - start - 1;
            if (runSegmentCount < 1)
            {
                // A lone point in a zone has no travel; ignored (a zone needs >= 2 points).
                return;
            }

            // Direction of the run's first segment, used to aim an optional lead-in hold on the run's first
            // point so an authored dwell there reads as a held opening beat before the zone starts moving.
            var leadDirection = points[start + 1].pos - points[start].pos;
            leadDirection = leadDirection.sqrMagnitude > 1e-8f ? leadDirection.normalized : Vector3.forward;
            var leadKeepsFraming = points[start].yawKeepsFraming;
            if (!leadKeepsFraming)
            {
                leadDirection = ApplyStageIntroDollyYaw(leadDirection, points[start].yaw);
            }

            if (points[start].dwell > 0f)
            {
                var holdPos = points[start].pos;
                var holdLook = new Vector3(holdPos.x, points[start].lookY, holdPos.z) + leadDirection * lookAhead;
                if (leadKeepsFraming)
                {
                    // The opening hold has to sit on the same orbit the first moving segment starts from,
                    // otherwise the run jump-cuts from the held pose to a laterally offset one.
                    holdPos = OrbitStageIntroDollyCameraAroundAim(holdPos, holdLook, points[start].yaw);
                }

                shots.Add(new CinematicShot(holdPos, holdPos, holdLook, holdLook, points[start].dwell, CinematicShotEase.Linear));
            }

            for (var s = 0; s < runSegmentCount; s++)
            {
                var i = start + s;
                var fromPos = points[i].pos;
                var toPos = points[i + 1].pos;

                var travel = toPos - fromPos;
                var distance = travel.magnitude;
                var direction = distance > 0.0001f ? travel / distance : Vector3.forward;

                // Aim a look-ahead point along the direction of travel, kept at the look-at height.
                // The segment carries its START point's yaw into its END point's yaw, so authoring different
                // angles on consecutive points makes the camera swing around the subject as it travels
                // instead of jumping sideways at the boundary (a per-segment constant yaw would, because the
                // orbit offset would change discontinuously there). Equal yaws = a constant-angle segment.
                var yaw = points[i].yaw;
                var toYaw = points[i + 1].yaw;
                var keepsFraming = points[i].yawKeepsFraming;

                // Framing-preserving yaw: leave the aim on the street ahead and swing the CAMERA around it,
                // so the same ground stays centered and only the viewing side changes. The lateral camera
                // offset is a consequence of the angle, not something the author has to dial in per angle.
                // Otherwise turn only the aim, which walks the street out of frame as the angle grows.
                var aimDirection = keepsFraming ? direction : ApplyStageIntroDollyYaw(direction, yaw);
                var fromLook = new Vector3(fromPos.x, points[i].lookY, fromPos.z) + aimDirection * lookAhead;
                var toLook = new Vector3(toPos.x, points[i + 1].lookY, toPos.z) + aimDirection * lookAhead;

                if (keepsFraming)
                {
                    // Each end sits on its own point's orbit; the shot's straight interpolation then slides
                    // the camera between them. That chord runs slightly inside the arc, so a very wide swing
                    // in one segment reads as a small push-in mid-move (negligible up to ~45 degrees of
                    // change; split the change over more authored points if a 90 degree swing is wanted).
                    fromPos = OrbitStageIntroDollyCameraAroundAim(fromPos, fromLook, yaw);
                    toPos = OrbitStageIntroDollyCameraAroundAim(toPos, toLook, toYaw);
                }

                var segmentSpeed = speed * (points[i].speedMultiplier > 0f ? points[i].speedMultiplier : 1f);

                // Ramps belong to the RUN, not to a segment: only the run's first segment eases up to speed
                // and only its last eases back down; everything between holds that speed exactly.
                AppendStageIntroDollySegmentShots(
                    shots, fromPos, toPos, fromLook, toLook, distance, segmentSpeed,
                    accelSeconds: s == 0 ? host.StageIntroDollyRampSeconds : 0f,
                    decelSeconds: s == runSegmentCount - 1 ? host.StageIntroDollyRampSeconds : 0f);

                // Hold on arrival if the destination point authored a dwell (zero-move shot = free Enter skip).
                var dwell = points[i + 1].dwell;
                if (dwell > 0f)
                {
                    shots.Add(new CinematicShot(toPos, toPos, toLook, toLook, dwell, CinematicShotEase.Linear));
                }
            }
        }
        // Emits one dolly segment as up to three shots: ease up to speed, hold it, ease back down. Only the
        // run's outer ends pass a non-zero ramp, so the middle of a run is a single unbroken constant speed.
        //
        // The split is what makes the speed continuous. Easing a WHOLE segment (the earlier scheme) forces
        // that segment to still cover its full distance in its authored time, so an eased-in segment has to
        // overshoot to twice the cruise speed by its end — and the next Linear segment then drops straight
        // back to cruise. That halving at the boundary is the "it slows down and stutters mid-flight" the
        // authored runs showed. Here the ramp instead takes its own extra time: covering rampDistance while
        // easing from 0 costs 2*rampDistance/speed, i.e. twice the cruising time for that stretch, which is
        // exactly what lets it arrive at cruise speed rather than double it. EaseIn (t^2) and EaseOut
        // (1-(1-t)^2) already have the derivatives this relies on, so the ramp meets the cruise shot at the
        // same velocity from both sides.
        private static void AppendStageIntroDollySegmentShots(
            List<CinematicShot> shots,
            Vector3 fromPos,
            Vector3 toPos,
            Vector3 fromLook,
            Vector3 toLook,
            float distance,
            float speed,
            float accelSeconds,
            float decelSeconds)
        {
            speed = Mathf.Max(0.01f, speed);
            var accelDistance = Mathf.Max(0f, accelSeconds) * speed * 0.5f;
            var decelDistance = Mathf.Max(0f, decelSeconds) * speed * 0.5f;

            // A segment shorter than its own ramps keeps their ratio and just never reaches cruise speed.
            var ramps = accelDistance + decelDistance;
            if (ramps > distance && ramps > 0.0001f)
            {
                var scale = distance / ramps;
                accelDistance *= scale;
                decelDistance *= scale;
            }

            var cruiseDistance = Mathf.Max(0f, distance - accelDistance - decelDistance);
            var accelEnd = distance > 0.0001f ? accelDistance / distance : 0f;
            var cruiseEnd = distance > 0.0001f ? (accelDistance + cruiseDistance) / distance : 1f;
            var emitted = false;

            void Emit(float fromFraction, float toFraction, float seconds, CinematicShotEase ease)
            {
                if (seconds <= 0.0001f)
                {
                    return;
                }

                shots.Add(new CinematicShot(
                    Vector3.Lerp(fromPos, toPos, fromFraction),
                    Vector3.Lerp(fromPos, toPos, toFraction),
                    Vector3.Lerp(fromLook, toLook, fromFraction),
                    Vector3.Lerp(fromLook, toLook, toFraction),
                    seconds,
                    ease));
                emitted = true;
            }

            Emit(0f, accelEnd, 2f * accelDistance / speed, CinematicShotEase.EaseIn);
            Emit(accelEnd, cruiseEnd, cruiseDistance / speed, CinematicShotEase.Linear);
            Emit(cruiseEnd, 1f, 2f * decelDistance / speed, CinematicShotEase.EaseOut);

            if (!emitted)
            {
                // Degenerate (zero-length) segment: keep a minimal shot so the run still cuts to this pose.
                shots.Add(new CinematicShot(fromPos, toPos, fromLook, toLook, 0.01f, CinematicShotEase.Linear));
            }
        }
        // Turns a horizontal travel direction left/right around world up. Yaw 0 returns it untouched, so an
        // unauthored point keeps the plain look-where-you-are-going aim.
        private static Vector3 ApplyStageIntroDollyYaw(Vector3 direction, float yawDegrees)
        {
            return Mathf.Approximately(yawDegrees, 0f)
                ? direction
                : Quaternion.AngleAxis(yawDegrees, Vector3.up) * direction;
        }
        // Swings the camera around the vertical axis through its aim point. Distance and height to the aim
        // are preserved (it is a rotation of the whole camera-minus-aim offset), so the aim stays exactly
        // centered and only the side it is viewed from changes — that is what keeps the framed ground the
        // same as the 0-degree take. Yaw 0 is the identity, so an unauthored point is untouched.
        private static Vector3 OrbitStageIntroDollyCameraAroundAim(Vector3 cameraPosition, Vector3 aimPoint, float yawDegrees)
        {
            return Mathf.Approximately(yawDegrees, 0f)
                ? cameraPosition
                : aimPoint + Quaternion.AngleAxis(yawDegrees, Vector3.up) * (cameraPosition - aimPoint);
        }
        // A lateral dolly that sweeps a short arc past 'focus' while holding the look-at on it. Pitch,
        // distance, viewing side and sweep direction vary deterministically by shot index (seeded), so a
        // sequence of shots reads as different angles instead of a single monotonous orbit.
        private CinematicShot BuildStageIntroFocusShot(Vector3 focus, float baseDistance, float basePitch, int seed, int shotIndex)
        {
            var rng = new System.Random(seed + shotIndex * 101);
            float Next01() => (float)rng.NextDouble();

            var azCenter = Next01() * 360f;                                 // seeded viewing side
            var chordHalf = Mathf.Lerp(12f, 18f, Next01());                 // short lateral chord (design: ~12-18 deg)
            var dir = Next01() < 0.5f ? 1f : -1f;                           // sweep left->right or right->left
            var pitch = Mathf.Clamp(basePitch + Mathf.Lerp(-12f, 15f, Next01()), 5f, 89f); // low-angle near <-> high-angle far
            var distance = baseDistance * Mathf.Lerp(0.85f, 1.3f, Next01());// tighter close-up <-> wider framing

            var fromPos = IntroOrbitCameraPosition(focus, azCenter - dir * chordHalf, pitch, distance);
            var toPos = IntroOrbitCameraPosition(focus, azCenter + dir * chordHalf, pitch, distance);
            return new CinematicShot(fromPos, toPos, focus, focus, Mathf.Max(0.01f, host.StageIntroDwellSeconds));
        }
        // Hard cut to the shot's opening pose, then dolly to its end pose over the shot duration.
        internal IEnumerator AnimateCinematicShot(CinematicShot shot)
        {
            // Cut (trailer grammar): snap to the opening pose with no blend from the previous shot.
            host.CameraController.ApplyIntroCameraPose(shot.FromPos, shot.FromLookAt);

            var duration = Mathf.Max(0.01f, shot.Duration);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (PollStageIntroSkip())
                {
                    stageIntroSkipRequested = true;
                    host.CameraController.ApplyIntroCameraPose(shot.ToPos, shot.ToLookAt);
                    yield break;
                }

                elapsed += host.CinematicDeltaTime;
                var eased = EaseCinematicShot(Mathf.Clamp01(elapsed / duration), shot.Ease);
                host.CameraController.ApplyIntroCameraPose(
                    Vector3.Lerp(shot.FromPos, shot.ToPos, eased),
                    Vector3.Lerp(shot.FromLookAt, shot.ToLookAt, eased));
                yield return null;
            }

            host.CameraController.ApplyIntroCameraPose(shot.ToPos, shot.ToLookAt);
        }
        // Gentle ease-in-out (EaseInOut): half linear, half SmoothStep, so the ends soften but the middle
        // keeps a near-constant slow speed (a full SmoothStep would read as too much acceleration for a
        // trailer dolly). Linear/EaseIn/EaseOut let an authored chain shape acceleration across segments.
        private static float EaseCinematicShot(float t, CinematicShotEase ease)
        {
            switch (ease)
            {
                case CinematicShotEase.Linear:
                    return t;
                case CinematicShotEase.EaseIn:
                    return t * t;
                case CinematicShotEase.EaseOut:
                    return 1f - (1f - t) * (1f - t);
                default:
                    return Mathf.Lerp(t, Mathf.SmoothStep(0f, 1f, t), 0.5f);
            }
        }
        private static bool PollStageIntroSkip()
        {
            // Enter (and numpad Enter) skips the stage-intro cinematic and jumps straight to the final
            // gameplay state. Enter only by design — click/Space/Esc stay excluded so the intro cannot be
            // dismissed by the inputs players use for other things.
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null &&
                (keyboard.enterKey.wasPressedThisFrame ||
                 keyboard.numpadEnterKey.wasPressedThisFrame);
#else
            return Input.GetKeyDown(KeyCode.Return) ||
                   Input.GetKeyDown(KeyCode.KeypadEnter);
#endif
        }
        private void HideAllGameplayUiForCinematic()
        {
            // Keep only the world map layer (the map itself) visible; hide everything else,
            // including the sidebar, so the intro plays like a film.
            stageIntroUiHider ??= new CinematicUiHider(
                () => MapCombatController.FindSceneRectTransform(host.HostScene, GameplaySceneContract.GameplayLayerRootName),
                objectName => objectName == GameplaySceneContract.WorldMapLayerName);
            stageIntroUiHider.Hide();
        }
        // Restore is called on teardown paths too; never instantiate the hider there.
        private void RestoreAllGameplayUiAfterCinematic() => stageIntroUiHider?.Restore();
        // 4B-A(2026-09-04): OnDisable의 시네마틱 해체 블록을 그대로 옮겼다(본문 무변경 · trailerFilmingActive만 호스트 잔류).
        // 비활성 중 인트로/승리 연출이 끊기면(로비 복귀·씬 교체) 숨긴 UI·인트로 카메라·룩 크로스페이드·연출 전용
        // 리빌/숨김 집합을 전부 되돌려, 재활성 시 반쯤 재생된 연출이 이어지지 않게 한다.
        internal void TeardownCinematicsOnDisable()
        {
            // If the object is disabled mid stage-intro (e.g. returning to the lobby), restore the
            // hidden UI and release the intro camera so a re-enable does not start in a hijacked state.
            host.CameraController.EndStageIntroCinemachineCamera();
            RestoreAllGameplayUiAfterCinematic();
            // The victory sequence shares the cinematic camera/reveal state above; restore its own UI hide
            // and clear its run flags so a re-enable does not resume a half-played presentation.
            RestoreGameplayUiAfterVictory();
            // Disabling mid-victory (returning to the lobby, a scene swap): drop the crossfade's temp rig and
            // volume. A dev replay additionally hands the stage's night look back.
            if (memoryStoneVictoryIsDebugReplay)
            {
                RestoreNightLookAfterMemoryStoneVictoryReplay();
            }
            else
            {
                SettleMemoryStoneVictoryClearLook();
            }

            memoryStoneVictoryActive = false;
            memoryStoneVictorySkipRequested = false;
            memoryStoneVictoryIsDebugReplay = false;
            memoryStoneVictorySequenceRoutine = null;
            host.CinematicForcedRevealCells = null;
            host.StageIntroHiddenMonsterIds = null;
            host.ActorMarkers.SetMarkerUiVisible(true);
            SnapStageIntroBuildingNightLook();
            // Disabling the behaviour already stops its coroutines; just drop the handle so a re-enable
            // does not think a replay take is still running.
            stageIntroReplayRoutine = null;
            stageIntroActive = false;
            stageIntroSkipRequested = false;
        }
        // 4B-A(2026-09-04): HideGameVictoryOverlay의 승리 연출 중단 블록을 그대로 옮겼다(본문 무변경). 오버레이 자체의
        // SetActive(false)는 HudTooltips에 남는다. 이 경로는 시퀀스를 StopCoroutine으로 끊으므로 finalizer가 안 돌고,
        // 그래서 해체를 손으로 다시 한다 — 순서(리플레이 룩 복원 → 플래그 클리어)를 바꾸지 말 것.
        internal void AbortMemoryStoneVictoryPresentation()
        {
            if (memoryStoneVictorySequenceRoutine != null)
            {
                host.StopCoroutine(memoryStoneVictorySequenceRoutine);
                memoryStoneVictorySequenceRoutine = null;
            }

            RestoreGameplayUiAfterVictory();
            host.CameraController.EndStageIntroCinemachineCamera();
            // Drop the presentation-only reveal/dressing the sequence installed, so a dev replay that is
            // dismissed mid-flight hands the map back exactly as gameplay left it.
            host.CinematicForcedRevealCells = null;
            host.ActorMarkers?.SetMarkerUiVisible(true);
            host.TileView?.SetMapObjectVisualsBuildingsOnly(false);
            if (memoryStoneVictoryIsDebugReplay)
            {
                // Must run before the replay flag is cleared below.
                RestoreNightLookAfterMemoryStoneVictoryReplay();
            }

            memoryStoneVictoryActive = false;
            memoryStoneVictorySkipRequested = false;
            memoryStoneVictoryIsDebugReplay = false;
            deferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes = false;
        }
    }
}
