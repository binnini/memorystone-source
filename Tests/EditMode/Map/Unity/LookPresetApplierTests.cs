using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    /// <summary>
    /// Covers the look-preset apply contract: environment values land in RenderSettings, the rig/volume/view
    /// layers are swapped when the preset supplies them, and a null preset (or an empty slot) is a no-op.
    /// </summary>
    public sealed class LookPresetApplierTests
    {
        [Test]
        public void ApplyEnvironmentWritesRenderSettings()
        {
            var saved = RenderSettingsSnapshot.Capture();
            try
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.1f, 0.2f, 0.3f, 1f);
                RenderSettings.fog = true;
                RenderSettings.fogColor = new Color(0.4f, 0.5f, 0.6f, 1f);
                RenderSettings.fogDensity = 0.0123f;

                var preset = ScriptableObject.CreateInstance<EnvironmentLookPreset>();
                preset.CaptureEnvironmentFromRenderSettings();

                // Perturb, then confirm ApplyEnvironment restores the captured values.
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.fog = false;
                RenderSettings.fogColor = Color.magenta;
                RenderSettings.fogDensity = 0.9f;

                preset.ApplyEnvironment();

                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                Assert.That(RenderSettings.fog, Is.True);
                Assert.That(RenderSettings.fogColor, Is.EqualTo(new Color(0.4f, 0.5f, 0.6f, 1f)));
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(0.0123f).Within(1e-6f));

                Object.DestroyImmediate(preset);
            }
            finally
            {
                saved.Restore();
            }
        }

        [Test]
        public void TrilightAmbientSurvivesTheFlatAmbientAlias()
        {
            // RenderSettings.ambientLight and ambientSkyColor are the same field: applying a Trilight preset
            // must not let the (unused) flat colour overwrite the authored sky tint.
            var saved = RenderSettingsSnapshot.Capture();
            try
            {
                var sky = new Color(0.55f, 0.68f, 0.85f, 1f);
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = sky;

                var preset = ScriptableObject.CreateInstance<EnvironmentLookPreset>();
                preset.CaptureEnvironmentFromRenderSettings();
                SetPrivateColor(preset, "ambientLight", new Color(0.6f, 0.65f, 0.7f, 1f));

                RenderSettings.ambientSkyColor = Color.magenta;
                preset.ApplyEnvironment();

                Assert.That(RenderSettings.ambientSkyColor, Is.EqualTo(sky));
                Object.DestroyImmediate(preset);
            }
            finally
            {
                saved.Restore();
            }
        }

        [Test]
        public void ApplyNullPresetIsNoOp()
        {
            var saved = RenderSettingsSnapshot.Capture();
            try
            {
                RenderSettings.fogDensity = 0.055f;
                RenderSettings.ambientMode = AmbientMode.Trilight;

                var note = LookPresetApplier.Apply(null);

                Assert.That(note, Does.Contain("untouched"));
                Assert.That(RenderSettings.fogDensity, Is.EqualTo(0.055f).Within(1e-6f));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
            }
            finally
            {
                saved.Restore();
            }
        }

        [Test]
        public void ApplyLightRigReplacesAnchorChildrenWithPrefabInstance()
        {
            var anchor = new GameObject("EnvAnchorTest");
            var stray = new GameObject("StrayChild");
            stray.transform.SetParent(anchor.transform, false);
            var rig = new GameObject("TestRig");

            var preset = ScriptableObject.CreateInstance<EnvironmentLookPreset>();
            preset.SetLookAssets(rig, null, null);
            try
            {
                var note = LookPresetApplier.ApplyLightRig(preset, anchor.transform);

                Assert.That(anchor.transform.childCount, Is.EqualTo(1));
                Assert.That(anchor.transform.GetChild(0).name, Is.EqualTo("TestRig"));
                Assert.That(anchor.transform.GetChild(0).gameObject, Is.Not.SameAs(rig), "should be an instance, not the source object");
                Assert.That(note, Does.Contain("TestRig"));
            }
            finally
            {
                Object.DestroyImmediate(anchor);
                Object.DestroyImmediate(rig);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void ApplyLightRigWithNoPrefabKeepsSceneRig()
        {
            var anchor = new GameObject("EnvAnchorNoPrefab");
            var existing = new GameObject("ExistingRig");
            existing.transform.SetParent(anchor.transform, false);
            var preset = ScriptableObject.CreateInstance<EnvironmentLookPreset>();
            try
            {
                var note = LookPresetApplier.ApplyLightRig(preset, anchor.transform);

                Assert.That(anchor.transform.childCount, Is.EqualTo(1));
                Assert.That(anchor.transform.GetChild(0).name, Is.EqualTo("ExistingRig"));
                Assert.That(note, Does.Contain("kept scene rig"));
            }
            finally
            {
                Object.DestroyImmediate(anchor);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void ResolveLightRootPrefersEnvLightingOverPresetRigAnchor()
        {
            var envRoot = new GameObject(LookPresetApplier.EnvLightingRootName);
            var presetAnchor = new GameObject(LookPresetApplier.PresetRigAnchorName);
            try
            {
                Assert.That(LookPresetApplier.ResolveOrCreateLightRoot(), Is.SameAs(envRoot.transform));
            }
            finally
            {
                Object.DestroyImmediate(envRoot);
                Object.DestroyImmediate(presetAnchor);
            }
        }

        [Test]
        public void ResolveLightRootFallsBackToPresetRigAnchor()
        {
            // The lookdev scene has no ENV LIGHTING root — its rig lives under the "Preset Rig" anchor, and
            // both the runtime applier and the authoring tool must land on that same transform.
            var presetAnchor = new GameObject(LookPresetApplier.PresetRigAnchorName);
            try
            {
                Assert.That(LookPresetApplier.ResolveOrCreateLightRoot(), Is.SameAs(presetAnchor.transform));
            }
            finally
            {
                Object.DestroyImmediate(presetAnchor);
            }
        }

        [Test]
        public void ApplyVisibilityReinjectsSettingsIntoView()
        {
            var root = new GameObject("LookPresetViewTest");
            var view = root.AddComponent<AtlasTilePresentationView>();
            var vis = ScriptableObject.CreateInstance<VisibilityPresentationSettings>();
            SetPrivateFloat(vis, "visibilityEmissionFloor", 0.777f);

            var preset = ScriptableObject.CreateInstance<EnvironmentLookPreset>();
            preset.SetLookAssets(null, null, vis);
            try
            {
                var note = LookPresetApplier.ApplyVisibility(preset, view);

                var readBack = GetPrivateFloat(view, "visibilityEmissionFloor");
                Assert.That(readBack, Is.EqualTo(0.777f).Within(1e-6f));
                Assert.That(note, Does.Contain("applied"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(vis);
                Object.DestroyImmediate(preset);
            }
        }

        [Test]
        public void ApplyVolumeProfileSwapsSharedProfile()
        {
            var go = new GameObject("LookPresetVolumeTest");
            var volume = go.AddComponent<Volume>();
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var preset = ScriptableObject.CreateInstance<EnvironmentLookPreset>();
            preset.SetLookAssets(null, profile, null);
            try
            {
                var note = LookPresetApplier.ApplyVolumeProfile(preset, volume);

                Assert.That(volume.sharedProfile, Is.SameAs(profile));
                Assert.That(note, Does.Contain("set profile"));
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(preset);
            }
        }

        private static void SetPrivateColor(Object target, string field, Color value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).colorValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetPrivateFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            so.FindProperty(field).floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static float GetPrivateFloat(Object target, string field)
        {
            var so = new SerializedObject(target);
            so.Update();
            return so.FindProperty(field).floatValue;
        }

        private readonly struct RenderSettingsSnapshot
        {
            private readonly AmbientMode ambientMode;
            private readonly Color ambientSky;
            private readonly Color ambientEquator;
            private readonly Color ambientGround;
            private readonly Color ambientLight;
            private readonly float ambientIntensity;
            private readonly bool fog;
            private readonly Color fogColor;
            private readonly FogMode fogMode;
            private readonly float fogDensity;
            private readonly float fogStart;
            private readonly float fogEnd;
            private readonly Material skybox;
            private readonly float reflectionIntensity;
            private readonly Color subtractiveShadow;

            private RenderSettingsSnapshot(bool _)
            {
                ambientMode = RenderSettings.ambientMode;
                ambientSky = RenderSettings.ambientSkyColor;
                ambientEquator = RenderSettings.ambientEquatorColor;
                ambientGround = RenderSettings.ambientGroundColor;
                ambientLight = RenderSettings.ambientLight;
                ambientIntensity = RenderSettings.ambientIntensity;
                fog = RenderSettings.fog;
                fogColor = RenderSettings.fogColor;
                fogMode = RenderSettings.fogMode;
                fogDensity = RenderSettings.fogDensity;
                fogStart = RenderSettings.fogStartDistance;
                fogEnd = RenderSettings.fogEndDistance;
                skybox = RenderSettings.skybox;
                reflectionIntensity = RenderSettings.reflectionIntensity;
                subtractiveShadow = RenderSettings.subtractiveShadowColor;
            }

            public static RenderSettingsSnapshot Capture() => new RenderSettingsSnapshot(true);

            public void Restore()
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.fog = fog;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
                RenderSettings.skybox = skybox;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.subtractiveShadowColor = subtractiveShadow;
            }
        }
    }
}
