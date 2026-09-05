using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Cinemachine tuning values for combat prototype cameras.
    /// Keeps scene-specific camera composition and player input policy outside the legacy main-camera profile.
    /// </summary>
    [CreateAssetMenu(
        fileName = "CombatCinemachineCameraProfile",
        menuName = "Seoul Playup/Combat/Cinemachine Camera Profile")]
    public sealed class CombatCinemachineCameraProfile : ScriptableObject
    {
        private static readonly Vector3 DefaultFollowOffset = new(0f, 9.5f, -7f);

        [Header("Follow")]
        [SerializeField] private bool preferPlayerRendererBoundsCenter = true;
        [SerializeField] private Vector3 targetOffset = new(0f, 0.35f, 0f);
        [SerializeField] private Vector3 followOffset = DefaultFollowOffset;
        [SerializeField] private float damping = 0.35f;

        [Tooltip("Damping applied only while an action focus owns the camera (plan §3.2). Lower than the " +
            "global damping so the pan lands inside its beat; the global value is deliberately untouched.")]
        [SerializeField] private float actionFocusDamping = 0.15f;

        [Tooltip("Viewport inset that counts as off-screen. P0 measured that 0.1 reclassifies ~17% of all " +
            "events from framed to unframed, so this materially sets how much the camera works (plan §6.1).")]
        [Range(0f, 0.49f)]
        [SerializeField] private float frustumMarginFraction = 0.1f;

        [Tooltip("How far the framing leans from the player toward a melee attacker (0 = none, 1 = fully on " +
            "the attacker). Emphasis only — the geometry gate still decides relocation.\n\n" +
            "0.5 → 0.7 on 2026-07-31 (plan §10.16): at 0.5 an r1 attacker moved the player only from viewport " +
            "0.50 to 0.45 — about half a hex, which measured as present but did not read as emphasis. The lean " +
            "is proportional to the attacker's distance, so melee is the weakest case this value has to carry.")]
        [Range(0f, 1f)]
        [SerializeField] private float attackEmphasisBias = 0.7f;
        [SerializeField] [Range(0f, 1f)] private float screenX = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float screenY = 0.5f;
        [SerializeField] [Range(0f, 2f)] private float deadZoneWidth;
        [SerializeField] [Range(0f, 2f)] private float deadZoneHeight;

        [Header("Player Controls")]
        [SerializeField] private bool enableRuntimeControls = true;
        [SerializeField] private bool enableMouseWheelZoom = true;
        [SerializeField] private bool enableKeyboardYaw = true;
        [SerializeField] private float minZoomDistance = 7f;
        [SerializeField] private float maxZoomDistance = 18f;
        [SerializeField] private float mouseWheelZoomSpeed = 1.2f;
        [SerializeField] private float keyboardYawDegreesPerSecond = 75f;
        [SerializeField] private bool snapOrbitWhileInput = true;

        public bool PreferPlayerRendererBoundsCenter => preferPlayerRendererBoundsCenter;
        public Vector3 TargetOffset => targetOffset;
        public Vector3 FollowOffset => followOffset.sqrMagnitude > 0.0001f ? followOffset : DefaultFollowOffset;
        public float Damping => Mathf.Max(0f, damping);
        public float ActionFocusDamping => Mathf.Max(0f, actionFocusDamping);
        public float FrustumMarginFraction => Mathf.Clamp(frustumMarginFraction, 0f, 0.49f);
        public float AttackEmphasisBias => Mathf.Clamp01(attackEmphasisBias);
        public float ScreenX => Mathf.Clamp01(screenX);
        public float ScreenY => Mathf.Clamp01(screenY);
        public float DeadZoneWidth => Mathf.Clamp(deadZoneWidth, 0f, 2f);
        public float DeadZoneHeight => Mathf.Clamp(deadZoneHeight, 0f, 2f);
        public bool EnableRuntimeControls => enableRuntimeControls;
        public bool EnableMouseWheelZoom => enableMouseWheelZoom;
        public bool EnableKeyboardYaw => enableKeyboardYaw;
        public float MinZoomDistance => Mathf.Max(1f, minZoomDistance);
        public float MaxZoomDistance => Mathf.Max(MinZoomDistance, maxZoomDistance);
        public float MouseWheelZoomSpeed => Mathf.Max(0f, mouseWheelZoomSpeed);
        public float KeyboardYawDegreesPerSecond => Mathf.Max(0f, keyboardYawDegreesPerSecond);
        public bool SnapOrbitWhileInput => snapOrbitWhileInput;

        private void OnValidate()
        {
            damping = Mathf.Max(0f, damping);
            screenX = Mathf.Clamp01(screenX);
            screenY = Mathf.Clamp01(screenY);
            deadZoneWidth = Mathf.Clamp(deadZoneWidth, 0f, 2f);
            deadZoneHeight = Mathf.Clamp(deadZoneHeight, 0f, 2f);
            minZoomDistance = Mathf.Max(1f, minZoomDistance);
            maxZoomDistance = Mathf.Max(minZoomDistance, maxZoomDistance);
            mouseWheelZoomSpeed = Mathf.Max(0f, mouseWheelZoomSpeed);
            keyboardYawDegreesPerSecond = Mathf.Max(0f, keyboardYawDegreesPerSecond);
        }
    }
}
