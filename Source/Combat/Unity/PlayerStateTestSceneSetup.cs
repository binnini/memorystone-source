using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Scene-scoped PlayerStateTest camera and lighting policy.
    /// Applies the CameraLightTest-approved Low Oblique/Baseline Warm defaults without promoting
    /// production renderer, art-direction, material-emissive, projected, or mask lighting systems.
    /// </summary>
    public sealed class PlayerStateTestSceneSetup : MonoBehaviour
    {
        public const string LowObliquePresetName = "Low Oblique";
        public const string BaselineWarmPresetName = "Baseline Warm";
        public const string CoolOvercastCandidateName = "Cool Overcast";

        private const float DefaultLowObliquePitchDegrees = 28f;
        private const float DefaultLowObliqueYawDegrees = 0f;
        private const float LowObliqueCameraDistance = 15f;
        private const float LowObliqueOrthographicSize = 7f;
        private const float DefaultMaxOrthographicSize = 14f;
        private const float DragStartThresholdPixels = 4f;

        [SerializeField] private MapCombatController controller;
        [SerializeField] private Camera targetCamera;
        [SerializeField] private Light directionalLight;
        [SerializeField] private AtlasTilePresentationView atlasTilePresentationView;
        [SerializeField] private bool applyOnStart = true;
        // Gates ONLY the Start-time baseline-warm lighting. Disable in scenes whose lighting is owned
        // by the scene/art direction (e.g. MainGameplay night tone) so the camera defaults still apply
        // on Start without stomping RenderSettings/the directional light.
        [SerializeField] private bool applyBaselineWarmLightingOnStart = true;
        [SerializeField] private float zoomStep = 0.5f;
        [SerializeField] private float dragPanSpeed = 1f;
        [SerializeField] private float maxZoomOutOrthographicSize = DefaultMaxOrthographicSize;
        [SerializeField] private float lowObliquePitchDegrees = DefaultLowObliquePitchDegrees;
        [SerializeField] private float lowObliqueYawDegrees = DefaultLowObliqueYawDegrees;
        [SerializeField] private bool useCustomInitialCameraFrame;
        [SerializeField] private Vector3 customInitialCameraPlayerOffset;
        [SerializeField] private Vector3 customInitialCameraEulerAngles;
        [SerializeField] private float customInitialCameraOrthographicSize = LowObliqueOrthographicSize;

        private Vector3 currentPlayerOffset;
        private Vector3 currentEulerAngles;
        private float currentOrthographicSize;
        private bool canStartCameraDrag;
        private bool isDraggingCamera;
        private Vector2 dragStartMousePosition;
        private Vector2 lastDragMousePosition;

        public string DefaultCameraPresetName => LowObliquePresetName;
        public string DefaultLightingPresetName => BaselineWarmPresetName;
        public string NightLightingCandidateName => CoolOvercastCandidateName;
        public float MinimumOrthographicSize => LowObliqueOrthographicSize;
        public float CurrentOrthographicSize => currentOrthographicSize;
        public bool IsUsingBaselineWarmLighting { get; private set; }
        public bool IsUsingCoolOvercastLighting { get; private set; }

        private void Awake()
        {
            ResolveReferences();
            currentEulerAngles = CreateLowObliqueEulerAngles();
            currentPlayerOffset = useCustomInitialCameraFrame
                ? customInitialCameraPlayerOffset
                : CreateCenteredPlayerOffset(currentEulerAngles, LowObliqueCameraDistance);
            currentOrthographicSize = GetInitialOrthographicSize();
            maxZoomOutOrthographicSize = Mathf.Max(LowObliqueOrthographicSize, maxZoomOutOrthographicSize);
        }

        private void Start()
        {
            if (!applyOnStart)
            {
                return;
            }

            ApplyLowObliqueDefaults();
            if (applyBaselineWarmLightingOnStart)
            {
                ApplyBaselineWarmLighting();
            }
        }

        private void Update()
        {
            HandleZoomInput();
            HandleRecenterInput();
            UpdateCameraDrag();
        }

        public void BindForTests(
            MapCombatController mapCombatController,
            Camera camera,
            Light sceneDirectionalLight,
            AtlasTilePresentationView atlasView)
        {
            controller = mapCombatController;
            targetCamera = camera;
            directionalLight = sceneDirectionalLight;
            atlasTilePresentationView = atlasView;
            ResolveReferences();
        }

        public void ApplyLowObliqueDefaults()
        {
            currentEulerAngles = CreateLowObliqueEulerAngles();
            currentPlayerOffset = useCustomInitialCameraFrame
                ? customInitialCameraPlayerOffset
                : CreateCenteredPlayerOffset(currentEulerAngles, LowObliqueCameraDistance);
            currentOrthographicSize = GetInitialOrthographicSize();
            ApplyCamera(keepCenteredOnPlayer: true);
        }

        public void ZoomBy(float orthographicDelta)
        {
            currentOrthographicSize = Mathf.Clamp(
                currentOrthographicSize + orthographicDelta,
                LowObliqueOrthographicSize,
                Mathf.Max(LowObliqueOrthographicSize, maxZoomOutOrthographicSize));
            ApplyCamera(keepCenteredOnPlayer: true);
        }

        public void RecenterCameraToPlayerForTests() => RecenterCameraToPlayer();

        public bool CanBeginMapDragAtScreenPosition(Vector2 screenPosition)
        {
            if (IsPointerOverDebugControlPanel(screenPosition))
            {
                return false;
            }

            if (targetCamera == null || !targetCamera.pixelRect.Contains(screenPosition))
            {
                return false;
            }

            if (IsPointerOverUi())
            {
                return false;
            }

            return atlasTilePresentationView != null
                && atlasTilePresentationView.TryScreenToHex(targetCamera, screenPosition, out HexCoord _);
        }

        public void ApplyBaselineWarmLighting()
        {
            IsUsingBaselineWarmLighting = true;
            IsUsingCoolOvercastLighting = false;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.42f, 1f);

            if (targetCamera != null)
            {
                targetCamera.backgroundColor = new Color(0.192f, 0.302f, 0.475f, 1f);
            }

            if (directionalLight == null)
            {
                return;
            }

            directionalLight.type = LightType.Directional;
            directionalLight.color = new Color(1f, 0.957f, 0.839f, 1f);
            directionalLight.intensity = 1f;
            directionalLight.transform.rotation = Quaternion.Euler(50f, -25f, 15f);
        }

        private void ResolveReferences()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            if (directionalLight == null)
            {
                foreach (var light in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (light.type == LightType.Directional)
                    {
                        directionalLight = light;
                        break;
                    }
                }
            }

            if (atlasTilePresentationView == null)
            {
                atlasTilePresentationView = FindFirstObjectByType<AtlasTilePresentationView>();
            }
        }

        private void HandleZoomInput()
        {
            if (IsPointerOverDebugControlPanel())
            {
                return;
            }

            var scroll = GetScrollDeltaY();
            if (Mathf.Approximately(scroll, 0f))
            {
                return;
            }

            ZoomBy(scroll > 0f ? -Mathf.Abs(zoomStep) : Mathf.Abs(zoomStep));
        }

        private void HandleRecenterInput()
        {
            if (WasYPressedThisFrame())
            {
                RecenterCameraToPlayer();
            }
        }

        private void UpdateCameraDrag()
        {
            if (targetCamera == null)
            {
                isDraggingCamera = false;
                return;
            }

            var mousePosition = GetMousePosition();
            if (GetMouseButtonDown())
            {
                canStartCameraDrag = CanBeginMapDragAtScreenPosition(mousePosition);
                isDraggingCamera = false;
                dragStartMousePosition = mousePosition;
                lastDragMousePosition = mousePosition;
            }

            if (!GetMouseButtonHeld())
            {
                canStartCameraDrag = false;
                isDraggingCamera = false;
                return;
            }

            if (!isDraggingCamera && canStartCameraDrag)
            {
                if ((mousePosition - dragStartMousePosition).sqrMagnitude < DragStartThresholdPixels * DragStartThresholdPixels)
                {
                    return;
                }

                isDraggingCamera = true;
                ApplyCamera(keepCenteredOnPlayer: false);
            }

            if (!isDraggingCamera)
            {
                return;
            }

            var delta = mousePosition - lastDragMousePosition;
            lastDragMousePosition = mousePosition;
            PanCamera(delta);
        }

        private void PanCamera(Vector2 screenDelta)
        {
            if (screenDelta == Vector2.zero || targetCamera == null)
            {
                return;
            }

            var viewportHeight = Mathf.Max(1, Screen.height);
            var unitsPerPixel = targetCamera.orthographic
                ? currentOrthographicSize * 2f / viewportHeight
                : Mathf.Max(0.01f, Vector3.Distance(targetCamera.transform.position, Vector3.zero)) / viewportHeight;
            var movement = (-screenDelta.x * targetCamera.transform.right - screenDelta.y * targetCamera.transform.up)
                * unitsPerPixel
                * Mathf.Max(0.01f, dragPanSpeed);
            targetCamera.transform.position += movement;
        }

        private void RecenterCameraToPlayer()
        {
            currentOrthographicSize = Mathf.Clamp(
                currentOrthographicSize,
                LowObliqueOrthographicSize,
                Mathf.Max(LowObliqueOrthographicSize, maxZoomOutOrthographicSize));
            ApplyCamera(keepCenteredOnPlayer: true);
        }

        private void ApplyCamera(bool keepCenteredOnPlayer)
        {
            if (controller != null)
            {
                controller.ApplyGameplayCameraSettingsForDev(
                    currentPlayerOffset,
                    currentEulerAngles,
                    currentOrthographicSize,
                    keepCenteredOnPlayer);
                return;
            }

            if (targetCamera == null)
            {
                return;
            }

            targetCamera.transform.rotation = Quaternion.Euler(currentEulerAngles);
            if (targetCamera.orthographic)
            {
                targetCamera.orthographicSize = currentOrthographicSize;
            }
        }

        private static Vector3 CreateCenteredPlayerOffset(Vector3 eulerAngles, float cameraDistance)
        {
            return -(Quaternion.Euler(eulerAngles) * Vector3.forward) * cameraDistance;
        }

        private Vector3 CreateLowObliqueEulerAngles()
        {
            return useCustomInitialCameraFrame
                ? customInitialCameraEulerAngles
                : new Vector3(lowObliquePitchDegrees, lowObliqueYawDegrees, 0f);
        }

        private float GetInitialOrthographicSize()
        {
            return useCustomInitialCameraFrame
                ? Mathf.Clamp(
                    customInitialCameraOrthographicSize,
                    LowObliqueOrthographicSize,
                    Mathf.Max(LowObliqueOrthographicSize, maxZoomOutOrthographicSize))
                : LowObliqueOrthographicSize;
        }

        private static bool IsPointerOverUi()
        {
            if (EventSystem.current == null)
            {
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject(Mouse.current.deviceId))
            {
                return true;
            }
#endif
            return EventSystem.current.IsPointerOverGameObject();
        }

        private static Vector2 GetMousePosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#else
            return Input.mousePosition;
#endif
        }


        private static bool IsPointerOverDebugControlPanel()
        {
            return IsPointerOverDebugControlPanel(GetMousePosition());
        }

        private static bool IsPointerOverDebugControlPanel(Vector2 screenPosition)
        {
            return CombatDebugControlPanel.IsPointerOverAnyGameViewPanel(screenPosition)
                || CameraLightTestPanel.IsPointerOverAnyVisiblePanel(screenPosition);
        }

        private static bool GetMouseButtonDown()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        private static bool GetMouseButtonHeld()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#else
            return Input.GetMouseButton(0);
#endif
        }

        private static float GetScrollDeltaY()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0f;
#else
            return Input.mouseScrollDelta.y;
#endif
        }

        private static bool WasYPressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            return Keyboard.current != null && Keyboard.current.yKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Y);
#endif
        }
    }
}
