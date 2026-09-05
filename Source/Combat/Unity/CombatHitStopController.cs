using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    // Hit-stop / player-death slow-motion runtime extracted from MapCombatController (P4 Stage 4).
    // Owns the Time.timeScale/fixedDeltaTime save-restore pair and the paused-animator snapshots so
    // a single authority reconciles overlapping freezes. Plain class owned by MapCombatController;
    // animator access and timing-profile resolution arrive as delegates (serialized config stays on
    // the host). The player-death impact-hold coroutine skeleton stays on the host because it
    // chains into the death camera zoom/shake (Stage 4 death-presentation territory).
    internal sealed class CombatHitStopController
    {
        private readonly System.Func<float?> getPlayerAnimationSpeed;
        private readonly System.Action<float> setPlayerAnimationSpeed;
        private readonly System.Func<string, float?> getMonsterAnimationSpeed;
        private readonly System.Action<string, float> setMonsterAnimationSpeed;
        private readonly System.Func<float> resolveHitStopAnimationSpeed;
        private readonly System.Func<float> resolveHitStopTimeScale;
        private readonly System.Func<float> resolveRealtimeDeltaTime;

        private readonly List<AnimatorSnapshot> activeAnimatorSnapshots = new List<AnimatorSnapshot>();
        private bool hasActiveTimeScale;
        private float previousTimeScale = 1f;
        private float previousFixedDeltaTime = 0.02f;
        private bool hasActivePlayerDeathSlowMotion;
        private float playerDeathPreviousTimeScale = 1f;
        private float playerDeathPreviousFixedDeltaTime = 0.02f;

        public CombatHitStopController(
            System.Func<float?> getPlayerAnimationSpeed,
            System.Action<float> setPlayerAnimationSpeed,
            System.Func<string, float?> getMonsterAnimationSpeed,
            System.Action<string, float> setMonsterAnimationSpeed,
            System.Func<float> resolveHitStopAnimationSpeed,
            System.Func<float> resolveHitStopTimeScale,
            System.Func<float> resolveRealtimeDeltaTime = null)
        {
            this.getPlayerAnimationSpeed = getPlayerAnimationSpeed;
            this.setPlayerAnimationSpeed = setPlayerAnimationSpeed;
            this.getMonsterAnimationSpeed = getMonsterAnimationSpeed;
            this.setMonsterAnimationSpeed = setMonsterAnimationSpeed;
            this.resolveHitStopAnimationSpeed = resolveHitStopAnimationSpeed;
            this.resolveHitStopTimeScale = resolveHitStopTimeScale;
            this.resolveRealtimeDeltaTime = resolveRealtimeDeltaTime;
        }

        private readonly struct AnimatorSnapshot
        {
            public AnimatorSnapshot(bool isPlayer, string monsterId, float speed)
            {
                IsPlayer = isPlayer;
                MonsterId = monsterId ?? string.Empty;
                Speed = speed;
            }

            public bool IsPlayer { get; }
            public string MonsterId { get; }
            public float Speed { get; }
        }

        public IEnumerator PlayHitStop(float seconds, bool includePlayer, params string[] affectedMonsterIds)
        {
            if (seconds <= 0f)
            {
                yield break;
            }

            // Let the impact trigger advance one frame so the actor freezes on the hit/death pose
            // instead of the pre-impact pose. Effects were already flushed on the impact beat.
            yield return null;
            BeginAnimationPause(includePlayer, affectedMonsterIds);
            BeginTimeScale(resolveHitStopTimeScale());
            // Realtime by hand instead of WaitForSecondsRealtime so the host can substitute the captured
            // clock during fixed-rate frame recording (wall-clock waits come out compressed in footage).
            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += resolveRealtimeDeltaTime?.Invoke() ?? Time.unscaledDeltaTime;
                yield return null;
            }

            RestoreTimeScale();
            RestoreAnimationSpeeds();
        }

        public void BeginAnimationPause(bool includePlayer, IReadOnlyList<string> affectedMonsterIds)
        {
            RestoreAnimationSpeeds();

            if (includePlayer && getPlayerAnimationSpeed() is { } playerSpeed)
            {
                activeAnimatorSnapshots.Add(new AnimatorSnapshot(isPlayer: true, string.Empty, playerSpeed));
                setPlayerAnimationSpeed(resolveHitStopAnimationSpeed());
            }

            if (affectedMonsterIds == null)
            {
                return;
            }

            var seenMonsterIds = new HashSet<string>();
            for (var i = 0; i < affectedMonsterIds.Count; i++)
            {
                var monsterId = affectedMonsterIds[i];
                if (string.IsNullOrWhiteSpace(monsterId) || !seenMonsterIds.Add(monsterId))
                {
                    continue;
                }

                if (getMonsterAnimationSpeed(monsterId) is { } monsterSpeed)
                {
                    activeAnimatorSnapshots.Add(new AnimatorSnapshot(isPlayer: false, monsterId, monsterSpeed));
                    setMonsterAnimationSpeed(monsterId, resolveHitStopAnimationSpeed());
                }
            }
        }

        public void RestoreAnimationSpeeds()
        {
            if (activeAnimatorSnapshots.Count == 0)
            {
                return;
            }

            for (var i = activeAnimatorSnapshots.Count - 1; i >= 0; i--)
            {
                var snapshot = activeAnimatorSnapshots[i];
                if (snapshot.IsPlayer)
                {
                    setPlayerAnimationSpeed(snapshot.Speed);
                }
                else
                {
                    setMonsterAnimationSpeed(snapshot.MonsterId, snapshot.Speed);
                }
            }

            activeAnimatorSnapshots.Clear();
        }

        public void BeginTimeScale(float targetTimeScale)
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (targetTimeScale >= 0.999f)
            {
                return;
            }

            RestoreTimeScale();
            previousTimeScale = Time.timeScale;
            previousFixedDeltaTime = Time.fixedDeltaTime;
            hasActiveTimeScale = true;

            Time.timeScale = targetTimeScale;
            if (previousTimeScale > 0.0001f)
            {
                Time.fixedDeltaTime = previousFixedDeltaTime * (targetTimeScale / previousTimeScale);
            }
        }

        public void RestoreTimeScale()
        {
            if (!hasActiveTimeScale)
            {
                return;
            }

            Time.timeScale = previousTimeScale;
            Time.fixedDeltaTime = previousFixedDeltaTime;
            hasActiveTimeScale = false;
        }

        public void BeginPlayerDeathSlowMotion(float slowMotionScale, float slowMotionDuration)
        {
            if (!Application.isPlaying || slowMotionDuration <= 0f || slowMotionScale >= 0.999f)
            {
                return;
            }

            if (hasActivePlayerDeathSlowMotion)
            {
                return;
            }

            playerDeathPreviousTimeScale = Time.timeScale;
            playerDeathPreviousFixedDeltaTime = Time.fixedDeltaTime;
            hasActivePlayerDeathSlowMotion = true;

            var targetTimeScale = Mathf.Clamp(slowMotionScale, 0.05f, 1f);
            Time.timeScale = targetTimeScale;
            if (playerDeathPreviousTimeScale > 0.0001f)
            {
                Time.fixedDeltaTime = playerDeathPreviousFixedDeltaTime * (targetTimeScale / playerDeathPreviousTimeScale);
            }
        }

        public void RestorePlayerDeathSlowMotion()
        {
            if (!hasActivePlayerDeathSlowMotion)
            {
                return;
            }

            Time.timeScale = playerDeathPreviousTimeScale;
            Time.fixedDeltaTime = playerDeathPreviousFixedDeltaTime;
            hasActivePlayerDeathSlowMotion = false;
        }
    }
}
