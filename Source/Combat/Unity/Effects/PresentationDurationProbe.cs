using SeoulPlayup.Combat.Runtime;
using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Reads timing off Unity assets and hands it to the pure
    /// <see cref="PresentationDurationMeasurement"/> (docs/presentation-duration-data-plan.md P2/P3).
    ///
    /// This lives outside the editor assemblies on purpose: the bake tool
    /// (<c>PresentationDurationBaker</c>, in Assembly-CSharp-Editor) and the drift audit
    /// (<c>presentation-duration-audit</c>, in SeoulPlayup.Combat.Unity.Editor.AiTools) cannot reference each
    /// other, so without a shared probe each would need its own copy of "how to read a prefab". Two copies
    /// would drift, and the audit compares its own measurement against the baked one — so a divergence would
    /// surface as permanent phantom drift on every entry. One reader, one answer.
    /// </summary>
    public static class PresentationDurationProbe
    {
        /// <summary>
        /// Measures a VFX prefab's natural play length at playback speed 1, including whether it is
        /// measurable at all (no ParticleSystem, or any looping system, means it has no natural end).
        /// </summary>
        public static ParticleMeasurement MeasurePrefab(GameObject prefab)
        {
            if (prefab == null)
            {
                return PresentationDurationMeasurement.MeasureParticles(null);
            }

            var timings = new List<ParticleTiming>();
            foreach (var particle in prefab.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                timings.Add(new ParticleTiming(main.duration, main.startLifetime.constantMax, main.loop));
            }

            return PresentationDurationMeasurement.MeasureParticles(timings);
        }

        /// <summary>
        /// Measures an audio clip's longest possible playback: the clip length at the slowest pitch the cue
        /// can pick. Returns 0 for a null clip.
        /// </summary>
        public static float MeasureAudioUpperBound(AudioClip clip, float pitchMin, float pitchMax)
        {
            if (clip == null)
            {
                return 0f;
            }

            return PresentationDurationMeasurement.ResolveAudioUpperBoundSeconds(
                clip.length,
                Mathf.Min(pitchMin, pitchMax));
        }
    }
}
