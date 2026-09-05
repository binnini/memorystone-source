using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// A reusable "look" for a stage: the environment lighting rig, ambient/fog RenderSettings,
    /// post-processing volume profile, and visibility (암시야) presentation — bundled into one asset
    /// so a <c>StageDefinition</c> can pick a mood the way it already picks a map source.
    ///
    /// The four layers a look is made of, and who owned them before this asset existed:
    /// <list type="bullet">
    /// <item>Ambient / fog / skybox — scene <see cref="RenderSettings"/> (static, no runtime injection hook).</item>
    /// <item>Directional light rig — a scene GameObject ("NightRig") under the ENV LIGHTING root.</item>
    /// <item>Post-processing — a <see cref="VolumeProfile"/> shared by the lookdev and game scenes.</item>
    /// <item>Visibility coefficients — a shared <see cref="VisibilityPresentationSettings"/> asset.</item>
    /// </list>
    ///
    /// A stage with no preset assigned keeps whatever the scene authored (backwards-compatible, roll-back
    /// safe): every field here is optional and a null/empty slot means "leave the scene value alone".
    /// The first preset, <c>SeoulNight</c>, captures the shipped night look 1:1 so applying it is a visual
    /// no-op. <see cref="LookPresetApplier"/> applies a preset at stage entry, before the map is rendered.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Map/Environment Look Preset", fileName = "EnvironmentLookPreset")]
    public sealed class EnvironmentLookPreset : ScriptableObject
    {
        [Header("Ambient")]
        [SerializeField] private AmbientMode ambientMode = AmbientMode.Trilight;
        [SerializeField] [ColorUsage(false, true)] private Color ambientSkyColor = new Color(0.0545f, 0.0832f, 0.149f, 1f);
        [SerializeField] [ColorUsage(false, true)] private Color ambientEquatorColor = new Color(0.106f, 0.141f, 0.220f, 1f);
        [SerializeField] [ColorUsage(false, true)] private Color ambientGroundColor = new Color(0.0314f, 0.0431f, 0.0784f, 1f);
        [Tooltip("Flat ambient mode only. Unity stores this in the same field as Ambient Sky Color, so it is ignored (and left unwritten) while the mode is Trilight or Skybox.")]
        [SerializeField] [ColorUsage(false, true)] private Color ambientLight = new Color(0.212f, 0.227f, 0.259f, 1f);
        [SerializeField] private float ambientIntensity = 0.8f;

        [Header("Fog")]
        [SerializeField] private bool fog = true;
        [SerializeField] private Color fogColor = new Color(0.06f, 0.11f, 0.13f, 1f);
        [SerializeField] private FogMode fogMode = FogMode.ExponentialSquared;
        [SerializeField] private float fogDensity = 0.005f;
        [SerializeField] private float fogStartDistance;
        [SerializeField] private float fogEndDistance = 300f;

        [Header("Sky / reflection")]
        [SerializeField] private Material skybox;
        [SerializeField] private float reflectionIntensity = 1f;
        [SerializeField] private Color subtractiveShadowColor = new Color(0.42f, 0.478f, 0.627f, 1f);

        [Header("Look assets (empty slot = keep the scene's)")]
        [Tooltip("Directional light rig prefab (e.g. NightRig). Instantiated under the ENV LIGHTING root, replacing any existing rig. Empty = leave the scene rig untouched.")]
        [SerializeField] private GameObject lightRigPrefab;
        [Tooltip("Post-processing profile applied to the scene's post Volume. Empty = leave the Volume's profile untouched.")]
        [SerializeField] private VolumeProfile volumeProfile;
        [Tooltip("Visibility (암시야) presentation coefficients re-injected into the map view before it renders. Empty = leave the view's settings untouched.")]
        [SerializeField] private VisibilityPresentationSettings visibilitySettings;

        public GameObject LightRigPrefab => lightRigPrefab;
        public VolumeProfile VolumeProfile => volumeProfile;
        public VisibilityPresentationSettings VisibilitySettings => visibilitySettings;
        public Material Skybox => skybox;

        /// <summary>
        /// Writes this preset's ambient/fog/sky values into the global <see cref="RenderSettings"/>.
        /// <para>
        /// <c>RenderSettings.ambientLight</c> and <c>ambientSkyColor</c> are the *same* underlying field —
        /// writing both would let the flat colour silently overwrite the Trilight sky tint (which is exactly
        /// what happened to the day preset's sky). So each ambient mode writes only the fields it owns.
        /// </para>
        /// </summary>
        public void ApplyEnvironment()
        {
            RenderSettings.ambientMode = ambientMode;
            if (ambientMode == AmbientMode.Flat)
            {
                RenderSettings.ambientLight = ambientLight;
            }
            else
            {
                RenderSettings.ambientSkyColor = ambientSkyColor;
                RenderSettings.ambientEquatorColor = ambientEquatorColor;
                RenderSettings.ambientGroundColor = ambientGroundColor;
            }

            RenderSettings.ambientIntensity = ambientIntensity;
            RenderSettings.fog = fog;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogMode = fogMode;
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogStartDistance = fogStartDistance;
            RenderSettings.fogEndDistance = fogEndDistance;
            RenderSettings.skybox = skybox;
            RenderSettings.reflectionIntensity = reflectionIntensity;
            RenderSettings.subtractiveShadowColor = subtractiveShadowColor;
        }

        /// <summary>
        /// Writes a blend of two presets' ambient/fog/sky values into the global <see cref="RenderSettings"/>,
        /// with <paramref name="t"/> going 0 (fully <paramref name="from"/>) to 1 (fully <paramref name="to"/>).
        /// Used by the stage-intro day→night crossfade. Numeric fields lerp; non-lerpable fields (ambient mode
        /// when the two differ, fog on/off, fog mode, skybox material) hard-swap at the midpoint. A null side
        /// degrades to applying the other preset whole.
        /// </summary>
        public static void ApplyEnvironmentBlended(EnvironmentLookPreset from, EnvironmentLookPreset to, float t)
        {
            if (from == null)
            {
                to?.ApplyEnvironment();
                return;
            }

            if (to == null)
            {
                from.ApplyEnvironment();
                return;
            }

            t = Mathf.Clamp01(t);
            var pastMid = t >= 0.5f;

            // Ambient: lerp the colours when both presets share a mode; otherwise hard-swap at the midpoint,
            // since the mode enum (and which field it aliases) cannot be interpolated.
            if (from.ambientMode == to.ambientMode)
            {
                RenderSettings.ambientMode = to.ambientMode;
                if (to.ambientMode == AmbientMode.Flat)
                {
                    RenderSettings.ambientLight = Color.Lerp(from.ambientLight, to.ambientLight, t);
                }
                else
                {
                    RenderSettings.ambientSkyColor = Color.Lerp(from.ambientSkyColor, to.ambientSkyColor, t);
                    RenderSettings.ambientEquatorColor = Color.Lerp(from.ambientEquatorColor, to.ambientEquatorColor, t);
                    RenderSettings.ambientGroundColor = Color.Lerp(from.ambientGroundColor, to.ambientGroundColor, t);
                }
            }
            else
            {
                var ambientSource = pastMid ? to : from;
                RenderSettings.ambientMode = ambientSource.ambientMode;
                if (ambientSource.ambientMode == AmbientMode.Flat)
                {
                    RenderSettings.ambientLight = ambientSource.ambientLight;
                }
                else
                {
                    RenderSettings.ambientSkyColor = ambientSource.ambientSkyColor;
                    RenderSettings.ambientEquatorColor = ambientSource.ambientEquatorColor;
                    RenderSettings.ambientGroundColor = ambientSource.ambientGroundColor;
                }
            }

            RenderSettings.ambientIntensity = Mathf.Lerp(from.ambientIntensity, to.ambientIntensity, t);
            RenderSettings.fog = pastMid ? to.fog : from.fog;
            RenderSettings.fogColor = Color.Lerp(from.fogColor, to.fogColor, t);
            RenderSettings.fogMode = pastMid ? to.fogMode : from.fogMode;
            RenderSettings.fogDensity = Mathf.Lerp(from.fogDensity, to.fogDensity, t);
            RenderSettings.fogStartDistance = Mathf.Lerp(from.fogStartDistance, to.fogStartDistance, t);
            RenderSettings.fogEndDistance = Mathf.Lerp(from.fogEndDistance, to.fogEndDistance, t);
            RenderSettings.skybox = pastMid ? to.skybox : from.skybox;
            RenderSettings.reflectionIntensity = Mathf.Lerp(from.reflectionIntensity, to.reflectionIntensity, t);
            RenderSettings.subtractiveShadowColor = Color.Lerp(from.subtractiveShadowColor, to.subtractiveShadowColor, t);
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only capture: snapshots the live <see cref="RenderSettings"/> into this preset. Used by the
        /// lookdev capture workflow so a designer can tune in the scene then save the result as a preset asset.
        /// The three asset slots (rig prefab / volume profile / visibility settings) are assigned separately.
        /// </summary>
        public void CaptureEnvironmentFromRenderSettings()
        {
            ambientMode = RenderSettings.ambientMode;
            // Mirrors ApplyEnvironment: only read back the field the current mode actually owns, so a Trilight
            // capture cannot overwrite the flat colour with the sky tint (they alias) or the other way round.
            if (ambientMode == AmbientMode.Flat)
            {
                ambientLight = RenderSettings.ambientLight;
            }
            else
            {
                ambientSkyColor = RenderSettings.ambientSkyColor;
                ambientEquatorColor = RenderSettings.ambientEquatorColor;
                ambientGroundColor = RenderSettings.ambientGroundColor;
            }

            ambientIntensity = RenderSettings.ambientIntensity;
            fog = RenderSettings.fog;
            fogColor = RenderSettings.fogColor;
            fogMode = RenderSettings.fogMode;
            fogDensity = RenderSettings.fogDensity;
            fogStartDistance = RenderSettings.fogStartDistance;
            fogEndDistance = RenderSettings.fogEndDistance;
            skybox = RenderSettings.skybox;
            reflectionIntensity = RenderSettings.reflectionIntensity;
            subtractiveShadowColor = RenderSettings.subtractiveShadowColor;
        }

        public void SetLookAssets(GameObject rigPrefab, VolumeProfile profile, VisibilityPresentationSettings visibility)
        {
            lightRigPrefab = rigPrefab;
            volumeProfile = profile;
            visibilitySettings = visibility;
        }
#endif
    }
}
