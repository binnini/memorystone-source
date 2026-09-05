using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    public enum CombatCameraProjectionMode
    {
        Orthographic = 0,
        Perspective = 1
    }

    /// <summary>
    /// Scene-level art direction profile for the combat gameplay camera.
    /// Keeps final camera framing values reusable instead of leaving them only on scene objects.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Combat/Camera Profile", fileName = "CombatCameraProfile")]
    public sealed class CombatCameraProfile : ScriptableObject
    {
        private const float MinPerspectiveFieldOfView = 1f;
        private const float MaxPerspectiveFieldOfView = 179f;
        private const float MinPerspectiveNearClip = 0.01f;

        [Header("Projection")]
        [SerializeField] private CombatCameraProjectionMode projectionMode = CombatCameraProjectionMode.Orthographic;
        [SerializeField] private float fieldOfView = 50f;
        [SerializeField] private float nearClipPlane = 0.3f;
        [SerializeField] private float farClipPlane = 200f;

        [Header("Follow")]
        [SerializeField] private bool keepCenteredOnPlayer = true;
        [SerializeField] private Vector3 playerOffset = new Vector3(0f, 9.5f, -7f);
        [SerializeField] private bool autoCalculatePlayerOffset;
        [SerializeField] private float followDistance = 10f;
        [SerializeField] private Vector2 framingOffset;
        [SerializeField] private Vector3 eulerAngles = new Vector3(35f, 0f, 0f);
        [SerializeField] private float followSmoothing = 12f;

        [Header("Orthographic Fallback")]
        [SerializeField] private float orthographicSize = 7f;
        [SerializeField] private float minOrthographicSize = 5f;
        [SerializeField] private float maxOrthographicSize = 14f;

        public CombatCameraProjectionMode ProjectionMode => projectionMode;
        public float FieldOfView => Mathf.Clamp(fieldOfView, MinPerspectiveFieldOfView, MaxPerspectiveFieldOfView);
        public float NearClipPlane => projectionMode == CombatCameraProjectionMode.Perspective
            ? Mathf.Max(MinPerspectiveNearClip, nearClipPlane)
            : nearClipPlane;
        public float FarClipPlane => Mathf.Max(NearClipPlane + MinPerspectiveNearClip, farClipPlane);
        public bool KeepCenteredOnPlayer => keepCenteredOnPlayer;
        public Vector3 PlayerOffset => playerOffset;
        public bool AutoCalculatePlayerOffset => autoCalculatePlayerOffset;
        public float FollowDistance => Mathf.Max(0.01f, followDistance);
        public Vector2 FramingOffset => framingOffset;
        public Vector3 EulerAngles => eulerAngles;
        public float FollowSmoothing => Mathf.Max(0f, followSmoothing);
        public float OrthographicSize => Mathf.Clamp(orthographicSize, MinOrthographicSize, MaxOrthographicSize);
        public float MinOrthographicSize => Mathf.Max(1f, minOrthographicSize);
        public float MaxOrthographicSize => Mathf.Max(MinOrthographicSize, maxOrthographicSize);

        private void OnValidate()
        {
            fieldOfView = Mathf.Clamp(fieldOfView, MinPerspectiveFieldOfView, MaxPerspectiveFieldOfView);
            if (projectionMode == CombatCameraProjectionMode.Perspective)
            {
                nearClipPlane = Mathf.Max(MinPerspectiveNearClip, nearClipPlane);
            }

            farClipPlane = Mathf.Max(nearClipPlane + MinPerspectiveNearClip, farClipPlane);
            followDistance = Mathf.Max(0.01f, followDistance);
            followSmoothing = Mathf.Max(0f, followSmoothing);
            minOrthographicSize = Mathf.Max(1f, minOrthographicSize);
            maxOrthographicSize = Mathf.Max(minOrthographicSize, maxOrthographicSize);
            orthographicSize = Mathf.Clamp(orthographicSize, minOrthographicSize, maxOrthographicSize);
        }

        public void ApplyTo(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            var useOrthographic = projectionMode == CombatCameraProjectionMode.Orthographic;
            camera.orthographic = useOrthographic;
            camera.nearClipPlane = NearClipPlane;
            camera.farClipPlane = FarClipPlane;
            camera.transform.rotation = Quaternion.Euler(EulerAngles);
            if (useOrthographic)
            {
                camera.orthographicSize = OrthographicSize;
            }
            else
            {
                camera.fieldOfView = FieldOfView;
            }
        }
    }
}
