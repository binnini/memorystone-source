using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Runtime day→night (or any A→B) crossfade between two <see cref="EnvironmentLookPreset"/>s, driven by
    /// a single 0→1 parameter. Used by the stage-intro trailer: the intro borrows a bright day look, holds
    /// it during the camera shots, then crossfades back to the stage's night look over the finale ripple.
    ///
    /// Three layers move together on the shared <c>t</c>:
    /// <list type="bullet">
    /// <item><b>Ambient / fog / reflection</b> — numerically lerped via <see cref="EnvironmentLookPreset.ApplyEnvironmentBlended"/>.</item>
    /// <item><b>Light rig</b> — both presets' rigs are instantiated at once and their lights' intensities are
    /// weighted (from-rig 1→0, to-rig 0→1). Only the dominant rig keeps shadows so the ground never carries
    /// two shadow sets at once.</item>
    /// <item><b>Post-processing</b> — a temporary Volume holding the from-profile is layered just above the
    /// scene's post Volume (which already holds the to-profile) and its weight fades 1→0, revealing the
    /// scene volume underneath. No scene-volume state is mutated.</item>
    /// </list>
    ///
    /// Play-mode only (the intro never runs in edit mode): rigs are plain clones discarded on
    /// <see cref="Complete"/>, which hands off to <see cref="LookPresetApplier.Apply"/> for the authoritative
    /// final state. <see cref="Complete"/> is safe to call without a matching <see cref="Begin"/> (no-op).
    /// </summary>
    public sealed class LookPresetCrossfader
    {
        private sealed class RigWeighting
        {
            public GameObject Instance;
            public readonly List<Light> Lights = new List<Light>();
            public readonly List<float> AuthoredIntensity = new List<float>();
            public readonly List<LightShadows> AuthoredShadows = new List<LightShadows>();
        }

        private const string SkyboxExposureProperty = "_Exposure";

        private EnvironmentLookPreset from;
        private EnvironmentLookPreset to;
        private RigWeighting fromRig;
        private RigWeighting toRig;
        private Volume fromVolume;
        private Material fromSkyboxInstance;
        private Material toSkyboxInstance;
        private float fromSkyboxBaseExposure;
        private float toSkyboxBaseExposure;
        private bool active;

        public bool IsActive => active;

        /// <summary>
        /// Applies <paramref name="from"/> fully and prepares the crossfade toward <paramref name="to"/> at
        /// t=0. Both presets must be non-null; a rig/volume that a preset does not define is simply skipped
        /// for that side. Clears the ENV LIGHTING root and re-instantiates both rigs so ownership of each
        /// light is unambiguous.
        /// </summary>
        public void Begin(EnvironmentLookPreset from, EnvironmentLookPreset to)
        {
            if (from == null || to == null)
            {
                return;
            }

            this.from = from;
            this.to = to;

            var root = LookPresetApplier.ResolveOrCreateLightRoot();
            if (root != null)
            {
                for (var i = root.childCount - 1; i >= 0; i--)
                {
                    Object.Destroy(root.GetChild(i).gameObject);
                }

                fromRig = InstantiateRig(from.LightRigPrefab, root);
                toRig = InstantiateRig(to.LightRigPrefab, root);
            }

            // Layer a temporary volume holding the from-profile just above the scene's post volume, which
            // already carries the to-profile. Fading this out later reveals the to look with no scene mutation.
            if (from.VolumeProfile != null)
            {
                var sceneVolume = LookPresetApplier.ResolvePostVolume();
                var host = new GameObject("Intro Look Crossfade Volume");
                fromVolume = host.AddComponent<Volume>();
                fromVolume.isGlobal = true;
                fromVolume.priority = (sceneVolume != null ? sceneVolume.priority : 0f) + 1f;
                fromVolume.sharedProfile = from.VolumeProfile;
                fromVolume.weight = 1f;
            }

            // Runtime skybox instances so the exposure dip does not mutate the shared skybox asset. The
            // shared assets are restored by Complete's LookPresetApplier.Apply(to).
            fromSkyboxInstance = CreateSkyboxInstance(from.Skybox, out fromSkyboxBaseExposure);
            toSkyboxInstance = CreateSkyboxInstance(to.Skybox, out toSkyboxBaseExposure);

            active = true;
            Evaluate(0f);
        }

        /// <summary>Drives the crossfade: 0 = fully <c>from</c>, 1 = fully <c>to</c>.</summary>
        public void Evaluate(float t)
        {
            if (!active)
            {
                return;
            }

            t = Mathf.Clamp01(t);
            EnvironmentLookPreset.ApplyEnvironmentBlended(from, to, t);

            // The dominant rig (by weight) owns shadows; the other's are disabled to avoid a double shadow set.
            var toOwnsShadows = t >= 0.5f;
            ApplyRigWeight(fromRig, 1f - t, keepShadows: !toOwnsShadows);
            ApplyRigWeight(toRig, t, keepShadows: toOwnsShadows);

            if (fromVolume != null)
            {
                fromVolume.weight = 1f - t;
            }

            EvaluateSkybox(t);
        }

        // Cross-dissolves the sky through darkness: the outgoing (from) sky dims to black over the first
        // half, then the incoming (to) sky brightens from black over the second half. The assigned material
        // swaps at the darkest point (t=0.5) so the cut is invisible. Overrides the plain midpoint swap that
        // ApplyEnvironmentBlended just wrote. Runs on runtime instances so the shared skybox assets are safe.
        private void EvaluateSkybox(float t)
        {
            if (fromSkyboxInstance == null && toSkyboxInstance == null)
            {
                return;
            }

            if (t < 0.5f)
            {
                if (fromSkyboxInstance != null)
                {
                    SetSkyboxExposure(fromSkyboxInstance, fromSkyboxBaseExposure * (1f - t / 0.5f));
                    RenderSettings.skybox = fromSkyboxInstance;
                }
                else
                {
                    RenderSettings.skybox = toSkyboxInstance;
                }
            }
            else
            {
                if (toSkyboxInstance != null)
                {
                    SetSkyboxExposure(toSkyboxInstance, toSkyboxBaseExposure * ((t - 0.5f) / 0.5f));
                    RenderSettings.skybox = toSkyboxInstance;
                }
                else
                {
                    RenderSettings.skybox = fromSkyboxInstance;
                }
            }
        }

        /// <summary>
        /// Snaps to the final <c>to</c> look and disposes the crossfade's temporary objects. Idempotent and
        /// safe to call even if <see cref="Begin"/> was never invoked.
        /// </summary>
        public void Complete()
        {
            if (fromVolume != null)
            {
                Object.Destroy(fromVolume.gameObject);
                fromVolume = null;
            }

            if (fromSkyboxInstance != null)
            {
                Object.Destroy(fromSkyboxInstance);
                fromSkyboxInstance = null;
            }

            if (toSkyboxInstance != null)
            {
                Object.Destroy(toSkyboxInstance);
                toSkyboxInstance = null;
            }

            // Authoritative final state: clears the crossfade rigs from the ENV root, re-instantiates the
            // clean to-rig, and re-applies the to environment/volume/visibility exactly as stage entry did.
            if (to != null)
            {
                LookPresetApplier.Apply(to);
            }

            fromRig = null;
            toRig = null;
            from = null;
            to = null;
            active = false;
        }

        private static RigWeighting InstantiateRig(GameObject prefab, Transform parent)
        {
            if (prefab == null || parent == null)
            {
                return null;
            }

            var instance = Object.Instantiate(prefab);
            instance.name = prefab.name; // strip "(Clone)"
            instance.transform.SetParent(parent, worldPositionStays: false);

            var rig = new RigWeighting { Instance = instance };
            foreach (var light in instance.GetComponentsInChildren<Light>(includeInactive: true))
            {
                rig.Lights.Add(light);
                rig.AuthoredIntensity.Add(light.intensity);
                rig.AuthoredShadows.Add(light.shadows);
            }

            return rig;
        }

        private static Material CreateSkyboxInstance(Material source, out float baseExposure)
        {
            baseExposure = 1f;
            if (source == null)
            {
                return null;
            }

            var instance = Object.Instantiate(source);
            if (instance.HasProperty(SkyboxExposureProperty))
            {
                baseExposure = instance.GetFloat(SkyboxExposureProperty);
            }

            return instance;
        }

        private static void SetSkyboxExposure(Material material, float exposure)
        {
            if (material != null && material.HasProperty(SkyboxExposureProperty))
            {
                material.SetFloat(SkyboxExposureProperty, Mathf.Max(0f, exposure));
            }
        }

        private static void ApplyRigWeight(RigWeighting rig, float weight, bool keepShadows)
        {
            if (rig == null)
            {
                return;
            }

            for (var i = 0; i < rig.Lights.Count; i++)
            {
                var light = rig.Lights[i];
                if (light == null)
                {
                    continue;
                }

                light.intensity = rig.AuthoredIntensity[i] * Mathf.Clamp01(weight);
                light.shadows = keepShadows ? rig.AuthoredShadows[i] : LightShadows.None;
            }
        }
    }
}
