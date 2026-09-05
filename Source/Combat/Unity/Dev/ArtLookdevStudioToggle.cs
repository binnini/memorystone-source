using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Switches the ArtLookdev scene between the night lighting rig (the look being tuned)
    /// and a neutral studio rig (pure-albedo inspection, "조명 탓/재질 탓" separation).
    /// Toggle the checkbox in the inspector, or use the component context menu.
    ///
    /// The toggle also swaps the <see cref="RenderSettings"/> ambient environment: the night
    /// look uses a deep-indigo Trilight gradient (어둡고 차가운 야경 베이스), while studio mode
    /// uses flat neutral grey so albedo can be judged without the night ambient tint. Values
    /// are authored by <c>ArtLookdevSceneBuilder</c> and serialized here so the toggle is the
    /// single source of truth for "night vs studio" including environment.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ArtLookdevStudioToggle : MonoBehaviour
    {
        [Tooltip("체크 = 중립 스튜디오 조명(알베도 확인), 해제 = 야경 조명(튜닝 대상)")]
        [SerializeField] private bool useStudioLighting;
        [SerializeField] private GameObject nightRigRoot;
        [SerializeField] private GameObject studioRigRoot;

        [Header("Night ambient (Trilight — 딥 인디고 야경 베이스)")]
        [SerializeField] private Color nightAmbientSky = new Color(0.0549f, 0.0824f, 0.1490f);      // #0E1526
        [SerializeField] private Color nightAmbientEquator = new Color(0.1059f, 0.1412f, 0.2196f);  // #1B2438
        [SerializeField] private Color nightAmbientGround = new Color(0.0314f, 0.0431f, 0.0784f);   // #080B14

        [Header("Studio ambient (Flat neutral)")]
        [SerializeField] private Color studioAmbient = new Color(0.5f, 0.5f, 0.5f);

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            Apply();
        }

        [ContextMenu("Toggle Night / Studio")]
        public void Toggle()
        {
            useStudioLighting = !useStudioLighting;
            Apply();
        }

        public void Apply()
        {
            if (nightRigRoot != null)
            {
                nightRigRoot.SetActive(!useStudioLighting);
            }

            if (studioRigRoot != null)
            {
                studioRigRoot.SetActive(useStudioLighting);
            }

            if (useStudioLighting)
            {
                RenderSettings.ambientMode = AmbientMode.Flat;
                RenderSettings.ambientLight = studioAmbient;
            }
            else
            {
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = nightAmbientSky;
                RenderSettings.ambientEquatorColor = nightAmbientEquator;
                RenderSettings.ambientGroundColor = nightAmbientGround;
            }
        }
    }
}
