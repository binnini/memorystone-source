using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Pins a <see cref="MapObjectFadeTarget"/> to one fade state so the ArtLookdev scene can show
    /// Normal / VisibilityDarkened / OcclusionFaded side by side. Darkening is a property-block-only
    /// change and is safe in edit mode; the semi-transparent occlusion fade swaps in non-persistent
    /// material instances, so it is applied in play mode only (edit mode would save broken references).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class ArtLookdevFadeStatePreview : MonoBehaviour
    {
        public enum FadeStateMode
        {
            Normal,
            VisibilityDarkened,
            OcclusionFaded
        }

        [Tooltip("Normal=원본 / VisibilityDarkened=암시야 어둡힘(에디트 모드 지원) / OcclusionFaded=반투명 페이드(플레이 중만)")]
        [SerializeField] private FadeStateMode mode = FadeStateMode.Normal;
        [SerializeField] private MapObjectFadeTarget target;

        public void Configure(MapObjectFadeTarget fadeTarget, FadeStateMode fadeMode)
        {
            target = fadeTarget;
            mode = fadeMode;
            Apply();
        }

        private void OnEnable()
        {
            ScheduleApply();
        }

        private void OnValidate()
        {
            ScheduleApply();
        }

        private void ScheduleApply()
        {
#if UNITY_EDITOR
            // MapObjectFadeTarget.OnEnable도 상태를 초기화하므로, 로드 프레임 이후에 덮어써야 확실하다.
            if (!Application.isPlaying)
            {
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        Apply();
                    }
                };
                return;
            }
#endif
            Apply();
        }

        [ContextMenu("Reapply State")]
        public void Apply()
        {
            if (target == null)
            {
                return;
            }

            target.ApplyVisibilityDarkening(mode == FadeStateMode.VisibilityDarkened);
            target.ApplyFade(mode == FadeStateMode.OcclusionFaded && Application.isPlaying);
        }
    }
}
