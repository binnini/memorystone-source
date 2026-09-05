using SeoulPlayup.CardCore;
using System.Collections;
using System.Collections.Generic;
using Cinemachine;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Presentation;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // Seam through which CombatCameraController drives host-owned behaviour it cannot perform as a
    // plain (non-MonoBehaviour) service — currently coroutine scheduling. Mirrors the 4-0 Audio
    // ICombatAudioHost inversion; MapCombatController implements it.
    public interface ICombatCameraHost
    {
        Coroutine StartCameraCoroutine(IEnumerator routine);
        void StopCameraCoroutine(Coroutine routine);
    }

    // Camera responsibility extracted from MapCombatController (refactoring stage 4-1).
    // Plain service (no MonoBehaviour) owned by MapCombatController, mirroring the
    // CombatGameplayCameraPresenter pattern: the controller keeps the serialized camera
    // config and forwards it, while this class holds the camera behaviour/runtime logic.
    // Grown incrementally, one camera responsibility per refactoring sub-step.
    public sealed class CombatCameraController
    {
        private readonly ICombatCameraHost host;
        private readonly System.Func<CinemachineImpulseSource> resolveShakeImpulseSource;
        private readonly System.Func<EffectShakeSettings> resolveShakeSettings;
        private readonly System.Func<EffectResultEvent, float> resolveEffectVfxDelaySeconds;
        private readonly System.Func<bool> isImpactSyncedAttackFlush;
        private readonly System.Func<float> activeShakeImpactDelay;
        private readonly System.Func<bool> canScheduleShakeDelay;
        // Realtime step for camera moves that must ignore timeScale (the death zoom). Injectable so the
        // host can substitute the captured clock during fixed-rate frame recording; null falls back to
        // Time.unscaledDeltaTime (same pattern as CombatHitStopController).
        private readonly System.Func<float> resolveRealtimeDeltaTime;
        // 공격 카드 판별의 정본은 저작(cards.csv type 컬럼)이므로 카탈로그를 읽어야 한다. 카탈로그는
        // 전투가 시작돼야 존재하고 재시작하면 갈리므로, 스냅샷이 아니라 델리게이트로 받아 흔들릴
        // 때마다 다시 묻는다(resolveShakeSettings와 같은 이유·같은 패턴).
        private readonly System.Func<CardCatalogDefinition> resolveCardCatalog;

        // The shake configuration and impulse source stay serialized on the host (scene-reference
        // protection), so they arrive as delegates and are re-read on every shake for live tuning.
        public CombatCameraController(
            ICombatCameraHost host,
            System.Func<CinemachineImpulseSource> resolveShakeImpulseSource,
            System.Func<EffectShakeSettings> resolveShakeSettings,
            System.Func<EffectResultEvent, float> resolveEffectVfxDelaySeconds,
            System.Func<bool> isImpactSyncedAttackFlush,
            System.Func<float> activeShakeImpactDelay,
            System.Func<bool> canScheduleShakeDelay,
            System.Func<CardCatalogDefinition> resolveCardCatalog,
            System.Func<float> resolveRealtimeDeltaTime = null)
        {
            this.resolveCardCatalog = resolveCardCatalog;
            this.resolveRealtimeDeltaTime = resolveRealtimeDeltaTime;
            this.host = host;
            this.resolveShakeImpulseSource = resolveShakeImpulseSource;
            this.resolveShakeSettings = resolveShakeSettings;
            this.resolveEffectVfxDelaySeconds = resolveEffectVfxDelaySeconds;
            this.isImpactSyncedAttackFlush = isImpactSyncedAttackFlush;
            this.activeShakeImpactDelay = activeShakeImpactDelay;
            this.canScheduleShakeDelay = canScheduleShakeDelay;
        }

        // Cinemachine impulse-based hit shake. The impulse source is resolved and owned by the
        // host (it lives on the host GameObject and is serialized there); this method only drives
        // the impulse generation so the shake math/configuration lives in one place.
        public void AddCinemachineShake(
            CinemachineImpulseSource source,
            float strength,
            float duration,
            int vibrato,
            float randomness)
        {
            if (source == null || strength <= 0f || duration <= 0f)
            {
                return;
            }

            ConfigureImpulseSource(source, duration, vibrato);
            source.GenerateImpulseWithVelocity(CreatePlaneShakeVelocity(strength, randomness));
        }

        private static void ConfigureImpulseSource(
            CinemachineImpulseSource source,
            float duration,
            int vibrato)
        {
            if (source == null)
            {
                return;
            }

            var definition = source.m_ImpulseDefinition ?? new CinemachineImpulseDefinition();
            definition.m_ImpulseChannel = 1;
            definition.m_ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            definition.m_ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Custom;
            definition.m_CustomImpulseShape = CreateDampedShakeCurve(vibrato);
            definition.m_ImpulseDuration = Mathf.Max(0.01f, duration);
            // A hit shake must land on the impact frame everywhere on screen — it is not a physical
            // wave radiating from a world point. The impulse source sits at the controller origin
            // while the camera follows the player, so the default sound-speed propagation (343 m/s)
            // made the shake arrive tens of milliseconds late and by a variable amount as the camera
            // drifted from origin, and dissipation weakened it with distance. Use an effectively
            // instant, non-dissipating signal so the shake is frame-aligned with the flushed impact.
            definition.m_DissipationDistance = 10000f;
            definition.m_DissipationRate = 0f;
            definition.m_PropagationSpeed = 100000f;
            source.m_ImpulseDefinition = definition;
            source.m_DefaultVelocity = Vector3.up;
        }

        private static AnimationCurve CreateDampedShakeCurve(int vibrato)
        {
            var sampleCount = Mathf.Max(2, vibrato);
            var keys = new Keyframe[sampleCount + 1];
            for (var i = 0; i <= sampleCount; i++)
            {
                var time = i / (float)sampleCount;
                var fade = 1f - time;
                var sign = i % 2 == 0 ? 1f : -1f;
                keys[i] = new Keyframe(time, sign * fade);
            }

            return new AnimationCurve(keys);
        }

        private static Vector3 CreatePlaneShakeVelocity(float strength, float randomness)
        {
            var clampedRandomness = Mathf.Clamp(randomness, 0f, 180f);
            var angle = clampedRandomness <= 0f
                ? 0f
                : Random.Range(-clampedRandomness, clampedRandomness);
            var radians = angle * Mathf.Deg2Rad;
            var direction = new Vector3(Mathf.Sin(radians), Mathf.Cos(radians), 0f);
            return direction.normalized * strength;
        }

        // --- Combat-effect camera shake (subscription + damage-scaled impulse) ---------------
        // Shakes the camera when a damage effect resolves: card attacks (player-sourced) shake
        // lighter than monster attacks landing on the player, and bigger hits shake harder
        // (clamped). Subscribes to CombatState.EffectResolved; the host re-ensures the
        // subscription when its state instance changes and unsubscribes on disable/destroy.
        private CombatState shakeSubscribedState;

        // Host-serialized shake tuning, snapshotted per shake through resolveShakeSettings.
        public readonly struct EffectShakeSettings
        {
            public EffectShakeSettings(
                float cardAttackStrength,
                float monsterAttackStrength,
                float duration,
                int vibrato,
                float randomness,
                float damageScale,
                float maxMultiplier)
            {
                CardAttackStrength = cardAttackStrength;
                MonsterAttackStrength = monsterAttackStrength;
                Duration = duration;
                Vibrato = vibrato;
                Randomness = randomness;
                DamageScale = damageScale;
                MaxMultiplier = maxMultiplier;
            }

            public float CardAttackStrength { get; }
            public float MonsterAttackStrength { get; }
            public float Duration { get; }
            public int Vibrato { get; }
            public float Randomness { get; }
            public float DamageScale { get; }
            public float MaxMultiplier { get; }
        }

        public void EnsureEffectShakeSubscription(CombatState state)
        {
            if (shakeSubscribedState == state)
            {
                return;
            }

            if (shakeSubscribedState != null)
            {
                shakeSubscribedState.EffectResolved -= OnEffectResolvedForShake;
            }

            shakeSubscribedState = state;
            if (shakeSubscribedState != null)
            {
                shakeSubscribedState.EffectResolved += OnEffectResolvedForShake;
            }
        }

        public void UnsubscribeEffectShake()
        {
            if (shakeSubscribedState != null)
            {
                shakeSubscribedState.EffectResolved -= OnEffectResolvedForShake;
                shakeSubscribedState = null;
            }
        }

        private void OnEffectResolvedForShake(EffectResultEvent effect)
        {
            // Match the camera shake to the effect's VFX so the hit reads as one beat: the VFX is spawned
            // after the cue's PlaybackDelaySeconds (authored to land on the animation), so the shake waits the
            // same amount instead of firing the instant the effect resolves. The legacy impact-sync offset is
            // honored too (whichever is larger).
            var delay = resolveEffectVfxDelaySeconds(effect);
            if (isImpactSyncedAttackFlush())
            {
                delay = Mathf.Max(delay, activeShakeImpactDelay());
            }

            if (delay > 0f && canScheduleShakeDelay())
            {
                host?.StartCameraCoroutine(PerformShakeAfterDelay(effect, delay));
                return;
            }

            PerformShake(effect);
        }

        private IEnumerator PerformShakeAfterDelay(EffectResultEvent effect, float delay)
        {
            yield return new WaitForSeconds(delay);
            PerformShake(effect);
        }

        private void PerformShake(EffectResultEvent effect)
        {
            if (effect.Kind != EffectKind.Damage)
            {
                return;
            }

            // A field announces itself as a zero-amount Damage effect so its footprint renders — both when it
            // is installed and at the start of each tick. Neither is an impact, and shaking the camera for
            // one reads as a hit that never landed. This is a separate path from the audio guard in
            // CombatAudioPresenter.MapEffectToCueIds, so it has to be excluded here too.
            if (CombatEffectSourceClassifier.IsFieldAreaAnnounce(effect))
            {
                return;
            }

            var settings = resolveShakeSettings();
            var baseStrength = CombatEffectSourceClassifier.IsPlayerAttackCardSource(effect.SourceRef, resolveCardCatalog?.Invoke())
                ? settings.CardAttackStrength
                : settings.MonsterAttackStrength;
            if (baseStrength <= 0f)
            {
                return;
            }

            var damage = Mathf.Max(0, effect.AppliedAmount > 0 ? effect.AppliedAmount : effect.Amount);
            var strength = ScaleShakeStrengthForDamage(
                baseStrength, damage, settings.DamageScale, settings.MaxMultiplier);

            CombatPresentationTrace.Record(
                CombatTraceChannel.Shake,
                effect.SourceRef,
                $"strength={strength:0.###} damage={damage} duration={settings.Duration:0.###}");

            AddCinemachineShake(strength, settings.Duration, settings.Vibrato, settings.Randomness);
        }

        // Resolves the host-owned impulse source, then drives it with the shared shake math above.
        public void AddCinemachineShake(float strength, float duration, int vibrato, float randomness)
        {
            if (strength <= 0f || duration <= 0f)
            {
                return;
            }

            var source = resolveShakeImpulseSource?.Invoke();
            if (source == null)
            {
                return;
            }

            AddCinemachineShake(source, strength, duration, vibrato, randomness);
        }

        // --- Camera path / orbit geometry (pure) ---------------------------------------------
        // Constant-speed parameterization of the memory-stone victory sweep and the stage-intro
        // orbit. Pure vector math, kept here so the victory/intro sequences (still driven from the
        // host coroutines) share one source of truth for camera positioning.

        internal static float MemoryStoneVictoryProgressFromDistance(float[] cumulative, float totalLength, float distance)
        {
            var pointCount = cumulative.Length;
            if (pointCount < 2 || totalLength <= 0.0001f)
            {
                return 1f;
            }

            distance = Mathf.Clamp(distance, 0f, totalLength);
            var segment = 0;
            while (segment < pointCount - 2 && cumulative[segment + 1] < distance)
            {
                segment++;
            }

            var segmentLength = cumulative[segment + 1] - cumulative[segment];
            var localT = segmentLength > 0.0001f ? (distance - cumulative[segment]) / segmentLength : 0f;
            return (segment + localT) / (pointCount - 1);
        }

        internal static Vector3 EvaluateMemoryStoneVictoryCameraPosition(IReadOnlyList<Vector3> points, float progress)
        {
            if (points == null || points.Count == 0)
            {
                return Vector3.zero;
            }

            if (points.Count == 1)
            {
                return points[0];
            }

            var scaled = Mathf.Clamp01(progress) * (points.Count - 1);
            var index = Mathf.FloorToInt(scaled);
            if (index >= points.Count - 1)
            {
                return points[points.Count - 1];
            }

            var localT = scaled - index;
            return Vector3.Lerp(points[index], points[index + 1], localT);
        }

        internal static Vector3 EvaluateMemoryStoneVictoryPathDirection(IReadOnlyList<Vector3> points, float progress)
        {
            if (points == null || points.Count < 2)
            {
                return Vector3.forward;
            }

            var scaled = Mathf.Clamp01(progress) * (points.Count - 1);
            var index = Mathf.FloorToInt(scaled);
            if (index >= points.Count - 1)
            {
                index = points.Count - 2;
            }

            var direction = points[index + 1] - points[index];
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
            {
                return Vector3.forward;
            }

            return direction.normalized;
        }

        // Rounds each interior vertex of a polyline into a short quadratic Bezier, so a sweep along it turns
        // through corners instead of snapping. The corner arc is clamped to half of each adjacent segment,
        // which keeps short segments from being swallowed; cornerRadius <= 0 returns the polyline unchanged.
        // Measured motivation: the Stage_1 victory route doubles back at waypoint 4 with an 84.5 degree
        // heading change, which read as a whip in playtest.
        internal static IReadOnlyList<Vector3> RoundPolylineCorners(
            IReadOnlyList<Vector3> points,
            float cornerRadius,
            int samplesPerCorner)
        {
            if (points == null || points.Count < 3 || cornerRadius <= 0f)
            {
                return points ?? (IReadOnlyList<Vector3>)System.Array.Empty<Vector3>();
            }

            var samples = Mathf.Max(2, samplesPerCorner);
            var rounded = new List<Vector3> { points[0] };

            for (var i = 1; i < points.Count - 1; i++)
            {
                var previous = points[i - 1];
                var corner = points[i];
                var next = points[i + 1];

                var inLength = Vector3.Distance(previous, corner);
                var outLength = Vector3.Distance(corner, next);
                var inCut = Mathf.Min(cornerRadius, inLength * 0.5f);
                var outCut = Mathf.Min(cornerRadius, outLength * 0.5f);
                if (inCut <= 0.0001f || outCut <= 0.0001f)
                {
                    rounded.Add(corner);
                    continue;
                }

                var arcStart = corner + (previous - corner).normalized * inCut;
                var arcEnd = corner + (next - corner).normalized * outCut;
                rounded.Add(arcStart);
                for (var s = 1; s < samples; s++)
                {
                    var t = s / (float)samples;
                    var a = Vector3.Lerp(arcStart, corner, t);
                    var b = Vector3.Lerp(corner, arcEnd, t);
                    rounded.Add(Vector3.Lerp(a, b, t));
                }

                rounded.Add(arcEnd);
            }

            rounded.Add(points[points.Count - 1]);
            return rounded;
        }

        internal static Vector3 EvaluateMemoryStoneVictoryCorneringDirection(
            IReadOnlyList<Vector3> points,
            float progress,
            float lookAheadProgress)
        {
            if (points == null || points.Count < 2)
            {
                return Vector3.forward;
            }

            var lookAhead = Mathf.Clamp(lookAheadProgress, 0.001f, 0.5f);
            var current = EvaluateMemoryStoneVictoryCameraPosition(points, progress);
            var futureProgress = Mathf.Clamp01(progress + lookAhead);
            var future = EvaluateMemoryStoneVictoryCameraPosition(points, futureProgress);
            var direction = future - current;
            direction.y = 0f;
            if (direction.sqrMagnitude >= 0.0001f)
            {
                return direction.normalized;
            }

            var previousProgress = Mathf.Clamp01(progress - lookAhead);
            var previous = EvaluateMemoryStoneVictoryCameraPosition(points, previousProgress);
            direction = current - previous;
            direction.y = 0f;
            return direction.sqrMagnitude >= 0.0001f
                ? direction.normalized
                : EvaluateMemoryStoneVictoryPathDirection(points, progress);
        }

        // Progress-space look-ahead equivalent to a world-space distance along the path. The look-ahead used
        // to be a flat 8% of the whole route, which on a long path with short end segments started turning
        // the camera a full segment early; expressing it in world units keeps corner anticipation constant
        // regardless of how long the route is.
        internal static float CorneringLookAheadProgress(float lookAheadDistance, float totalPathLength) =>
            totalPathLength > 0.0001f
                ? Mathf.Clamp(lookAheadDistance / totalPathLength, 0.001f, 0.5f)
                : 0.08f;

        // Places the camera on a downward-looking orbit: spin the focus->camera offset around the
        // focus by azimuth, tilt by pitch, push back by distance. Sweeping azimuth over time yields
        // the orbiting motion used by the stage-intro and victory ending sequences.
        internal static Vector3 IntroOrbitCameraPosition(Vector3 focus, float azimuthDegrees, float pitchDegrees, float distance)
        {
            var rotation = Quaternion.Euler(pitchDegrees, azimuthDegrees, 0f);
            return focus + rotation * (Vector3.back * Mathf.Max(0.1f, distance));
        }

        // Derived camera-minus-player offset when auto-calculate is on: pull back along the camera
        // forward by followDistance, then add the rotated screen-space framing offset. The host
        // exposes this through its EffectiveCameraPlayerOffset property.
        internal static Vector3 ComputeAutoGameplayCameraPlayerOffset(
            Vector3 eulerAngles,
            float followDistance,
            Vector2 framingOffset)
        {
            var rotation = Quaternion.Euler(eulerAngles);
            var centeredOffset = -(rotation * Vector3.forward) * followDistance;
            var rotatedFramingOffset = rotation * new Vector3(framingOffset.x, framingOffset.y, 0f);
            return centeredOffset + rotatedFramingOffset;
        }

        // --- Player-death camera zoom -------------------------------------------------------
        // Punch-in on the killing blow: eases the orthographic size down and adds a small yaw/pitch
        // dolly. Prefers the Cinemachine binder's temporary-motion override; falls back to driving
        // the legacy camera transform directly. The host supplies the camera, resolved binder and
        // config values (which stay serialized on the host); this owns the zoom runtime state.
        private bool hasPlayerDeathZoom;
        private float playerDeathPrevOrthographicSize = 5f;
        private Quaternion playerDeathPrevRotation = Quaternion.identity;
        private CinemachineCombatCameraBinder playerDeathZoomBinder;
        private Coroutine playerDeathZoomRoutine;

        public void BeginPlayerDeathZoom(
            Camera camera,
            CinemachineCombatCameraBinder binder,
            float duration,
            float multiplier,
            float fallbackOrthographicSize,
            float yawOffsetDegrees,
            float pitchOffsetDegrees)
        {
            RestorePlayerDeathZoom(camera);

            hasPlayerDeathZoom = true;
            playerDeathPrevOrthographicSize = camera != null ? camera.orthographicSize : fallbackOrthographicSize;
            playerDeathPrevRotation = camera != null ? camera.transform.rotation : Quaternion.identity;
            playerDeathZoomBinder = binder;

            if (duration <= 0f)
            {
                ApplyPlayerDeathMotion(camera, multiplier, 1f, yawOffsetDegrees, pitchOffsetDegrees);
                return;
            }

            playerDeathZoomRoutine = host?.StartCameraCoroutine(
                PlayPlayerDeathZoom(camera, duration, multiplier, yawOffsetDegrees, pitchOffsetDegrees));
        }

        private IEnumerator PlayPlayerDeathZoom(
            Camera camera,
            float duration,
            float targetMultiplier,
            float yawOffsetDegrees,
            float pitchOffsetDegrees)
        {
            var elapsed = 0f;
            while (elapsed < duration && hasPlayerDeathZoom)
            {
                var t = Mathf.Clamp01(elapsed / duration);
                var eased = t * t * (3f - 2f * t);
                ApplyPlayerDeathMotion(camera, Mathf.Lerp(1f, targetMultiplier, eased), eased, yawOffsetDegrees, pitchOffsetDegrees);
                elapsed += resolveRealtimeDeltaTime?.Invoke() ?? Time.unscaledDeltaTime;
                yield return null;
            }

            if (hasPlayerDeathZoom)
            {
                ApplyPlayerDeathMotion(camera, targetMultiplier, 1f, yawOffsetDegrees, pitchOffsetDegrees);
            }

            playerDeathZoomRoutine = null;
        }

        private void ApplyPlayerDeathMotion(
            Camera camera,
            float multiplier,
            float motionFactor,
            float yawOffsetDegrees,
            float pitchOffsetDegrees)
        {
            multiplier = Mathf.Clamp(multiplier, 0.25f, 1f);
            motionFactor = Mathf.Clamp01(motionFactor);

            if (playerDeathZoomBinder != null)
            {
                playerDeathZoomBinder.SetTemporaryCameraMotion(
                    multiplier,
                    yawOffsetDegrees * motionFactor,
                    pitchOffsetDegrees * motionFactor);
            }

            if (playerDeathZoomBinder == null && camera != null)
            {
                camera.transform.rotation = playerDeathPrevRotation
                    * Quaternion.Euler(
                        pitchOffsetDegrees * motionFactor,
                        yawOffsetDegrees * motionFactor,
                        0f);
                if (camera.orthographic)
                {
                    camera.orthographicSize = Mathf.Max(0.1f, playerDeathPrevOrthographicSize * multiplier);
                }
            }
        }


        // --- Combat-effect camera shake damage scale ------------------------------------------
        // Scales the base per-source shake strength by the effect's applied damage, clamped to a
        // max multiplier, so bigger hits shake harder without runaway strength on large hits.
        public static float ScaleShakeStrengthForDamage(float baseStrength, int damage, float damageScale, float maxMultiplier)
        {
            var scaled = baseStrength * (1f + damage * damageScale);
            return Mathf.Min(scaled, baseStrength * maxMultiplier);
        }

        public void RestorePlayerDeathZoom(Camera camera)
        {
            if (playerDeathZoomRoutine != null)
            {
                host?.StopCameraCoroutine(playerDeathZoomRoutine);
                playerDeathZoomRoutine = null;
            }

            if (!hasPlayerDeathZoom)
            {
                return;
            }

            if (playerDeathZoomBinder != null)
            {
                playerDeathZoomBinder.ClearTemporaryZoomMultiplier();
                playerDeathZoomBinder = null;
            }

            if (camera != null)
            {
                camera.transform.rotation = playerDeathPrevRotation;
                if (camera.orthographic)
                {
                    camera.orthographicSize = playerDeathPrevOrthographicSize;
                }
            }

            hasPlayerDeathZoom = false;
        }

        // --- Memory-stone victory sweep pose (pure) ------------------------------------------
        // The victory sequence runs on the shared cinematic virtual camera below (same rig as the
        // stage intro), so nothing here owns a camera: this is the pose math that turns a sweep
        // path + progress into the position/rotation the host feeds to ApplyIntroCameraPose.

        // Bundles the host-owned (serialized) victory camera config that the methods below need
        // repeatedly; the controller does not own these values, so callers pass them each time.
        public readonly struct MemoryStoneVictoryCameraSettings
        {
            public MemoryStoneVictoryCameraSettings(
                bool useFixedRotation,
                float yawDegrees,
                float pitchDegrees,
                float focusLag,
                float orthographicSize,
                float corneringLookAheadProgress)
            {
                UseFixedRotation = useFixedRotation;
                YawDegrees = yawDegrees;
                PitchDegrees = pitchDegrees;
                FocusLag = focusLag;
                OrthographicSize = orthographicSize;
                CorneringLookAheadProgress = corneringLookAheadProgress;
            }

            public bool UseFixedRotation { get; }
            public float YawDegrees { get; }
            public float PitchDegrees { get; }
            public float FocusLag { get; }
            public float OrthographicSize { get; }
            public float CorneringLookAheadProgress { get; }
        }

        // The look-at trails the camera slightly so the framing leads into corners instead of
        // snapping onto them.
        internal static float ResolveFocusProgress(float progress, MemoryStoneVictoryCameraSettings settings) =>
            Mathf.Clamp01(progress - settings.FocusLag);

        internal static Quaternion ResolveCameraRotation(
            MapCombatController.MemoryStoneVictoryCameraPath cameraPath,
            float progress,
            Vector3 cameraPosition,
            Vector3 lookAt,
            MemoryStoneVictoryCameraSettings settings)
        {
            if (settings.UseFixedRotation)
            {
                var rotationProgress = ResolveFocusProgress(progress, settings);
                var forward = EvaluateMemoryStoneVictoryCorneringDirection(
                    cameraPath.CameraPositions, rotationProgress, settings.CorneringLookAheadProgress);
                var yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg + settings.YawDegrees;
                return Quaternion.Euler(
                    Mathf.Clamp(settings.PitchDegrees, 5f, 85f),
                    yaw,
                    0f);
            }

            var direction = lookAt - cameraPosition;
            return direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, Vector3.up)
                : Quaternion.identity;
        }

        // Resolves the sweep camera pose (position + rotation) at a normalized progress along the
        // victory path. Pure: the host applies the result to the shared cinematic camera via
        // ApplyIntroCameraPose, so the victory sequence owns no camera rig of its own.
        internal static void EvaluateVictorySweepPose(
            MapCombatController.MemoryStoneVictoryCameraPath cameraPath,
            float progress,
            MemoryStoneVictoryCameraSettings settings,
            out Vector3 position,
            out Vector3 lookAt,
            out Quaternion rotation)
        {
            position = EvaluateMemoryStoneVictoryCameraPosition(cameraPath.CameraPositions, progress);
            lookAt = EvaluateMemoryStoneVictoryCameraPosition(
                cameraPath.FocusPositions, ResolveFocusProgress(progress, settings)) + Vector3.up * 0.2f;
            rotation = ResolveCameraRotation(cameraPath, progress, position, lookAt, settings);
        }


        // --- Stage-intro sequence camera (pure camera mechanics) -----------------------------
        // Same split as the memory-stone victory camera above: the intro's orbit/travel/finale
        // coroutines interleave camera pose with host-owned skip/reveal state, so they stay on the
        // host. Only the Cinemachine virtual-camera lifecycle and pose application move here.
        private CinemachineVirtualCamera stageIntroVirtualCamera;
        private int stageIntroVirtualCameraPreviousPriority;
        private bool stageIntroVirtualCameraActive;
        // Captured when the stage-intro camera starts so callers can wait out the Cinemachine blend back to
        // the gameplay camera after the intro ends (EndStageIntroCinemachineCamera only lowers priority; the
        // brain then eases the blend over the following frames). See IsCameraBlending.
        private CinemachineBrain stageIntroCinemachineBrain;
        private bool stageIntroBrainShowDebugText;
        private CinemachineBlendDefinition stageIntroBrainDefaultBlend;

        /// <summary>
        /// True while the CinemachineBrain is mid-blend between virtual cameras. The game-start sequence polls
        /// this after the stage intro so the first turn / opening-hand deal-in only begins once the camera has
        /// fully settled back onto the gameplay view (rather than during the intro-out blend).
        /// </summary>
        public bool IsCameraBlending => stageIntroCinemachineBrain != null && stageIntroCinemachineBrain.IsBlending;

        // Filming-only: the brain's debug text is an OnGUI overlay burned into recorded frames. The
        // cinematic hijack suppresses it inside Begin/End; ingame-style trailer takes never hijack, so
        // they toggle it through here instead. Save/restore mirrors the Begin/End pair.
        private CinemachineBrain filmingSuppressedBrain;
        private bool filmingSuppressedBrainShowDebugText;

        public void SetBrainDebugTextSuppressed(Camera camera, bool suppressed)
        {
            if (suppressed)
            {
                var brain = camera != null ? camera.GetComponent<CinemachineBrain>() : null;
                if (brain == null || filmingSuppressedBrain != null)
                {
                    return;
                }

                filmingSuppressedBrain = brain;
                filmingSuppressedBrainShowDebugText = brain.m_ShowDebugText;
                brain.m_ShowDebugText = false;
                return;
            }

            if (filmingSuppressedBrain != null)
            {
                filmingSuppressedBrain.m_ShowDebugText = filmingSuppressedBrainShowDebugText;
                filmingSuppressedBrain = null;
            }
        }

        // Explicit-rotation overload: the victory sweep resolves its own rotation (fixed pitch + path
        // heading yaw) rather than aiming at a look-at point, so it cannot go through the aim-based
        // overload below.
        public void ApplyIntroCameraPose(Vector3 cameraPosition, Quaternion rotation)
        {
            if (stageIntroVirtualCamera == null)
            {
                return;
            }

            stageIntroVirtualCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
        }

        public void ApplyIntroCameraPose(Vector3 cameraPosition, Vector3 lookAtPoint)
        {
            if (stageIntroVirtualCamera == null)
            {
                return;
            }

            var lookAt = lookAtPoint + Vector3.up * 0.2f;
            var direction = lookAt - cameraPosition;
            var rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction)
                : stageIntroVirtualCamera.transform.rotation;
            stageIntroVirtualCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
        }

        public void BeginStageIntroCinemachineCamera(
            Vector3 startCameraPosition,
            Vector3 startLookAt,
            Camera camera,
            Transform parent,
            float fieldOfView)
        {
            if (camera == null)
            {
                return;
            }

            stageIntroCinemachineBrain = camera.GetComponent<CinemachineBrain>();
            if (stageIntroCinemachineBrain == null)
            {
                stageIntroCinemachineBrain = camera.gameObject.AddComponent<CinemachineBrain>();
            }

            // The brain's debug text is an OnGUI overlay drawn into the Game View, so it is burned into any
            // recorded frame ("CM Main Camera: [Stage Intro Cinemachine Camera]" showed up in the first
            // trailer take). Suppressed for the cinematic's duration and restored on End.
            stageIntroBrainShowDebugText = stageIntroCinemachineBrain.m_ShowDebugText;
            stageIntroCinemachineBrain.m_ShowDebugText = false;

            // Cut into the cinematic instead of blending from wherever the gameplay camera happened to be.
            // The intro's whole language is hard cuts, and the incidental blend-in cost the first ~0.33s of
            // every shot to a swoop through nothing (measured on the first trailer takes). The blend OUT is
            // deliberately left alone: EndStageIntroCinemachineCamera restores this before lowering the
            // priority, and BeginGameplayStartSequence waits on that blend to hand the view back smoothly.
            stageIntroBrainDefaultBlend = stageIntroCinemachineBrain.m_DefaultBlend;
            stageIntroCinemachineBrain.m_DefaultBlend =
                new CinemachineBlendDefinition(CinemachineBlendDefinition.Style.Cut, 0f);

            if (stageIntroVirtualCamera == null)
            {
                var go = new GameObject("Stage Intro Cinemachine Camera");
                go.transform.SetParent(parent, false);
                stageIntroVirtualCamera = go.AddComponent<CinemachineVirtualCamera>();
            }

            stageIntroVirtualCameraPreviousPriority = stageIntroVirtualCamera.Priority;
            stageIntroVirtualCamera.Priority = 10000;
            stageIntroVirtualCamera.enabled = true;
            stageIntroVirtualCamera.gameObject.SetActive(true);
            stageIntroVirtualCameraActive = true;

            // Bare vcam (no Body/Aim): the brain uses its transform directly, so we drive position and
            // rotation by hand each frame. Widen the lens so the opening overview reads as a wide shot.
            var lens = stageIntroVirtualCamera.m_Lens;
            lens.Orthographic = camera.orthographic;
            if (camera.orthographic)
            {
                lens.OrthographicSize = Mathf.Max(camera.orthographicSize, fieldOfView);
            }
            else
            {
                lens.FieldOfView = Mathf.Max(camera.fieldOfView, fieldOfView);
            }

            stageIntroVirtualCamera.m_Lens = lens;
            stageIntroVirtualCamera.Follow = null;
            stageIntroVirtualCamera.LookAt = null;

            var direction = startLookAt - startCameraPosition;
            var rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction)
                : Quaternion.identity;
            stageIntroVirtualCamera.transform.SetPositionAndRotation(startCameraPosition, rotation);
        }

        public void EndStageIntroCinemachineCamera()
        {
            if (stageIntroVirtualCamera == null)
            {
                return;
            }

            if (stageIntroVirtualCameraActive)
            {
                stageIntroVirtualCamera.Priority = stageIntroVirtualCameraPreviousPriority;
            }

            if (stageIntroCinemachineBrain != null)
            {
                stageIntroCinemachineBrain.m_ShowDebugText = stageIntroBrainShowDebugText;
                // Restored BEFORE the priority drop below, so the blend back to gameplay uses the authored
                // blend rather than the cut this cinematic borrowed.
                stageIntroCinemachineBrain.m_DefaultBlend = stageIntroBrainDefaultBlend;
            }

            stageIntroVirtualCamera.enabled = false;
            stageIntroVirtualCameraActive = false;
        }

        // --- Tutorial camera focus pan --------------------------------------------------------
        // Pans to a tutorial-called-out tile, holds, then eases back to the player via the
        // Cinemachine binder's follow-target override; fully self-contained, running here via the
        // host.StartCameraCoroutine seam. tutorialCameraFocusRoutine tracks the in-flight routine so
        // a new focus request can cancel whichever one is running.
        private Coroutine tutorialCameraFocusRoutine;

        public void StopTutorialCameraFocusRoutine()
        {
            if (tutorialCameraFocusRoutine != null)
            {
                host?.StopCameraCoroutine(tutorialCameraFocusRoutine);
                tutorialCameraFocusRoutine = null;
            }
        }

        public void PlayTutorialCameraFocusCinemachine(CinemachineCombatCameraBinder binder, Vector3 tileWorld, float holdSeconds)
        {
            tutorialCameraFocusRoutine = host?.StartCameraCoroutine(
                PlayTutorialCameraFocusCinemachineRoutine(binder, tileWorld, holdSeconds));
        }

        private IEnumerator PlayTutorialCameraFocusCinemachineRoutine(
            CinemachineCombatCameraBinder binder, Vector3 tileWorld, float holdSeconds)
        {
            binder.SetTutorialFocusOverride(tileWorld);
            // Wait for the eased pan to the goal plus the dwell time, then clear so it eases back to the
            // player. Real-time wait so the dwell is unaffected by any hit-stop / time scaling.
            const float panSeconds = 1.1f;
            yield return new WaitForSecondsRealtime(panSeconds + Mathf.Max(0f, holdSeconds));
            binder.ClearTutorialFocusOverride();
            tutorialCameraFocusRoutine = null;
        }

        // --- Action-event camera focus geometry (pure) -----------------------------------------
        // The action-focus feature (docs/monster-action-camera-focus-plan.md) asks one question per
        // presented action event: is its world position on screen right now? Gating on geometry rather
        // than on a per-event-kind whitelist is deliberate — an adjacent attacker is already framed and
        // needs no camera move, while a field tick the player walked away from is not, and neither case
        // has to be enumerated. Extending the vision system (정찰, 시야 유물, 필드 리빌) therefore needs
        // no change here.
        //
        // Expressed against a value struct instead of a Camera so the decision is unit-testable without
        // a scene; CameraFrame.FromCamera bridges the live camera at the call site. CombatCameraController
        // is where the host's other pure camera math already lives (see ComputeAutoGameplayCameraPlayerOffset).

        /// <summary>
        /// The projection state of a camera at one instant: everything <see cref="IsPointFramed"/> needs,
        /// and nothing else. Copied by value so a decision can be replayed or tested in isolation.
        /// </summary>
        public readonly struct CameraFrame
        {
            public CameraFrame(
                Vector3 position,
                Quaternion rotation,
                bool orthographic,
                float verticalFieldOfViewDegrees,
                float orthographicSize,
                float aspect)
            {
                Position = position;
                Rotation = rotation;
                Orthographic = orthographic;
                VerticalFieldOfViewDegrees = verticalFieldOfViewDegrees;
                OrthographicSize = orthographicSize;
                Aspect = aspect;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public bool Orthographic { get; }

            /// <summary>Vertical FOV in degrees (Unity's <c>Camera.fieldOfView</c>). Ignored when orthographic.</summary>
            public float VerticalFieldOfViewDegrees { get; }

            /// <summary>Half-height of the orthographic view volume. Ignored when perspective.</summary>
            public float OrthographicSize { get; }

            /// <summary>Viewport width / height.</summary>
            public float Aspect { get; }

            public static CameraFrame FromCamera(Camera camera)
            {
                if (camera == null)
                {
                    return default;
                }

                return new CameraFrame(
                    camera.transform.position,
                    camera.transform.rotation,
                    camera.orthographic,
                    camera.fieldOfView,
                    camera.orthographicSize,
                    camera.aspect);
            }

            /// <summary>False for a <c>default</c> frame (no camera resolved), which must never read as "framed".</summary>
            public bool IsValid => Aspect > 0f && (Orthographic ? OrthographicSize > 0f : VerticalFieldOfViewDegrees > 0f);
        }

        /// <summary>
        /// True when <paramref name="worldPoint"/> projects inside the frame, inset on every side by
        /// <paramref name="marginFraction"/> of the full viewport. The margin exists so an event that only
        /// just clips the screen edge counts as off-screen: focusing it is what the player needs, and
        /// treating it as visible would leave the event half-cropped. margin 0 is the exact Unity
        /// <c>WorldToViewportPoint</c> [0,1] test (pinned by a cross-check test), margin 0.1 keeps the
        /// inner 80% of each axis. An invalid frame is never framed, so a missing camera degrades to
        /// "focus it" rather than to "silently skip every focus".
        /// </summary>
        public static bool IsPointFramed(CameraFrame frame, Vector3 worldPoint, float marginFraction = 0f)
        {
            if (!frame.IsValid || !TryProjectToViewport(frame, worldPoint, out var viewportX, out var viewportY))
            {
                return false;
            }

            var margin = Mathf.Clamp(marginFraction, 0f, 0.49f);
            return viewportX >= margin
                && viewportX <= 1f - margin
                && viewportY >= margin
                && viewportY <= 1f - margin;
        }

        /// <summary>
        /// Projects a world point to viewport coordinates (0..1 across the frame, origin bottom-left).
        /// Returns false when the point is at or behind the camera plane, where no viewport position exists.
        /// Mirrors Unity's projection for both camera modes; exposed so a test can compare it against a
        /// real <see cref="Camera"/> instead of trusting the two to agree.
        /// </summary>
        public static bool TryProjectToViewport(CameraFrame frame, Vector3 worldPoint, out float viewportX, out float viewportY)
        {
            viewportX = 0f;
            viewportY = 0f;
            if (!frame.IsValid)
            {
                return false;
            }

            // Camera-local coordinates with +Z along the view direction (Unity's view matrix negates Z; the
            // sign convention only has to be self-consistent with the half-extent math below).
            var local = Quaternion.Inverse(frame.Rotation) * (worldPoint - frame.Position);
            if (local.z <= 0.0001f)
            {
                return false;
            }

            float halfHeight;
            if (frame.Orthographic)
            {
                halfHeight = frame.OrthographicSize;
            }
            else
            {
                halfHeight = local.z * Mathf.Tan(frame.VerticalFieldOfViewDegrees * 0.5f * Mathf.Deg2Rad);
            }

            var halfWidth = halfHeight * frame.Aspect;
            if (halfHeight <= 0.0001f || halfWidth <= 0.0001f)
            {
                return false;
            }

            viewportX = 0.5f + 0.5f * (local.x / halfWidth);
            viewportY = 0.5f + 0.5f * (local.y / halfHeight);
            return true;
        }

        /// <summary>
        /// Where to aim while a monster attacks the player: the player's tile leaned toward the attacker by
        /// <paramref name="bias"/> (0 = no lean, 1 = fully on the attacker).
        ///
        /// This is deliberately NOT the action-focus gate. That gate asks "is this off screen" and relocates
        /// the camera; a melee attacker is 1–2 tiles from a centred player and therefore always on screen, so
        /// relocating to it would be a near-invisible nudge. Emphasis is the separate concern the plan filed
        /// under §1 non-goals ("비카메라 주의 유도 강화") — it never decides *whether* the camera is
        /// somewhere else, only that the existing framing leans into the blow.
        /// </summary>
        public static Vector3 ResolveAttackEmphasisPoint(Vector3 playerPosition, Vector3 attackerPosition, float bias)
            => Vector3.Lerp(playerPosition, attackerPosition, Mathf.Clamp01(bias));

        /// <summary>
        /// The point to frame for a group of action events: the centre of their bounding box, not their
        /// centroid. A centroid drifts toward whichever side happens to hold more events and can leave an
        /// outlier off screen — the whole reason the group is being framed together. Returns
        /// <see cref="Vector3.zero"/> for an empty group; callers check the count first.
        /// </summary>
        public static Vector3 ResolveClusterFocusPoint(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count == 0)
            {
                return Vector3.zero;
            }

            var min = points[0];
            var max = points[0];
            for (var i = 1; i < points.Count; i++)
            {
                min = Vector3.Min(min, points[i]);
                max = Vector3.Max(max, points[i]);
            }

            return (min + max) * 0.5f;
        }
    }
}
