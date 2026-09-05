using Cinemachine;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Prototype-scene bridge that lets Cinemachine follow the live combat player's projected map position.
    /// MapCombatController still owns gameplay state; this component only exposes that state as a Transform target.
    /// </summary>
    public sealed class CinemachineCombatCameraBinder : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private MapCombatController controller;
        [SerializeField] private CinemachineVirtualCamera virtualCamera;
        [SerializeField] private Transform followTarget;
        [SerializeField] private Camera sourceCamera;
        [SerializeField] private CombatCinemachineCameraProfile profile;

        [Header("Follow")]
        [SerializeField] private bool preferPlayerRendererBoundsCenter = true;
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 0.35f, 0f);
        [SerializeField] private Vector3 followOffset = new Vector3(0f, 9.5f, -7f);
        [SerializeField] private float damping = 0.35f;

        [Tooltip("Damping used only while an action focus owns the camera. Lower than the global damping so " +
            "the pan settles inside the beat it is standing in for; the global value is left alone.")]
        [SerializeField] private float actionFocusDamping = 0.15f;

        [Tooltip("How far the framing leans from the player toward a melee attacker (0 = none, 1 = fully on " +
            "the attacker). A melee attacker is already on screen, so this is emphasis, not relocation.")]
        [Range(0f, 1f)]
        [SerializeField] private float attackEmphasisBias = 0.5f;
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

        [Header("Pan Mode (WASD 이동, Space 복귀)")]
        [Tooltip("Y로 토글되는 자유 이동 모드 사용 여부.")]
        [SerializeField] private bool enableEdgePan = true;
        [Tooltip("Legacy field kept for prefab compatibility; camera pan now uses W/S forward/back movement.")]
        [SerializeField] private float panEdgeThickness = 28f;
        [Tooltip("WASD로 카메라가 움직이는 속도(초당 월드 단위).")]
        [SerializeField] private float panSpeed = 18f;

        private Vector3 normalizedOffsetDirection;
        private Vector3 runtimeFollowOffset;
        private float zoomDistance;
        private float orbitDegrees;
        private bool orbitOrZoomInputThisFrame;
        private bool panModeEnabled;
        private bool hasPanAnchor;
        private Vector3 panAnchorPosition;
        private bool tutorialFocusActive;
        private Vector3 tutorialFocusTilePosition;
        private bool actionFocusActive;
        private Vector3 actionFocusTilePosition;
        private bool attackEmphasisActive;
        private Vector3 attackEmphasisAttackerPosition;
        private float temporaryZoomMultiplier = 1f;
        private float temporaryYawDegrees;
        private float temporaryPitchDegrees;
        private bool hasTemporaryZoomBaseLens;
        private float temporaryZoomBaseFieldOfView;
        private float temporaryZoomBaseOrthographicSize;

        public CombatCinemachineCameraProfile Profile => profile;

        /// <summary>
        /// The player-controllable zoom distance, and its authored bounds. Read by the camera lab so a
        /// framing readout can state which zoom it was taken at — how much of the map is on screen swings
        /// from ~49% to ~90% across this range (docs/monster-action-camera-focus-plan.md §2.1), so a framing
        /// measurement without its zoom is not interpretable.
        /// </summary>
        public float DebugZoomDistance => zoomDistance;
        public float DebugMinZoomDistance => MinZoomDistance;
        public float DebugMaxZoomDistance => MaxZoomDistance;

        /// <summary>
        /// Dev-only zoom jump. Distinct from <see cref="SetFilmingCameraPose"/> on purpose: that one also
        /// takes over the orbit yaw and latches a filming pose to restore later, which would make a lab's
        /// zoom preset quietly reset the angle the user was inspecting from.
        /// </summary>
        public void DebugSetZoomDistance(float distance)
        {
            zoomDistance = Mathf.Clamp(distance, MinZoomDistance, MaxZoomDistance);
            runtimeFollowOffset = normalizedOffsetDirection * zoomDistance;
        }

        public void SetTemporaryZoomMultiplier(float multiplier)
            => SetTemporaryCameraMotion(multiplier, 0f, 0f);

        public void SetTemporaryCameraMotion(float zoomMultiplier, float yawDegrees, float pitchDegrees)
        {
            if (!hasTemporaryZoomBaseLens)
            {
                CaptureTemporaryZoomBaseLens();
            }

            temporaryZoomMultiplier = Mathf.Clamp(zoomMultiplier, 0.1f, 2f);
            temporaryYawDegrees = yawDegrees;
            temporaryPitchDegrees = pitchDegrees;
        }

        public void ClearTemporaryZoomMultiplier()
        {
            temporaryZoomMultiplier = 1f;
            temporaryYawDegrees = 0f;
            temporaryPitchDegrees = 0f;
            hasTemporaryZoomBaseLens = false;
        }

        private void CaptureTemporaryZoomBaseLens()
        {
            if (sourceCamera != null)
            {
                temporaryZoomBaseFieldOfView = sourceCamera.fieldOfView;
                temporaryZoomBaseOrthographicSize = sourceCamera.orthographicSize;
                hasTemporaryZoomBaseLens = true;
                return;
            }

            if (virtualCamera != null)
            {
                temporaryZoomBaseFieldOfView = virtualCamera.m_Lens.FieldOfView;
                temporaryZoomBaseOrthographicSize = virtualCamera.m_Lens.OrthographicSize;
                hasTemporaryZoomBaseLens = true;
            }
        }

        private void Awake()
        {
            InitializeOffsetState();
            ResolveReferences();
            ProcessRuntimeControls();
            UpdateFollowTarget();
            ConfigureVirtualCamera();
        }

        private void OnValidate()
        {
            minZoomDistance = Mathf.Max(1f, minZoomDistance);
            maxZoomDistance = Mathf.Max(minZoomDistance, maxZoomDistance);
            damping = Mathf.Max(0f, damping);
            if (!Application.isPlaying)
            {
                InitializeOffsetState();
            }
        }

        private void LateUpdate()
        {
            ResolveReferences();
            ProcessRuntimeControls();
            ProcessPanControls();
            UpdateFollowTarget();
            ConfigureVirtualCamera();
        }

        private void InitializeOffsetState()
        {
            var offset = FollowOffset;
            zoomDistance = Mathf.Clamp(offset.magnitude, MinZoomDistance, MaxZoomDistance);
            normalizedOffsetDirection = offset.normalized;
            orbitDegrees = 0f;
            runtimeFollowOffset = normalizedOffsetDirection * zoomDistance;
        }

        private void ResolveReferences()
        {
            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }

            if (virtualCamera == null)
            {
                virtualCamera = GetComponent<CinemachineVirtualCamera>() ?? FindFirstObjectByType<CinemachineVirtualCamera>();
            }

            if (sourceCamera == null)
            {
                sourceCamera = Camera.main;
            }

            if (followTarget == null)
            {
                var targetObject = GameObject.Find("CM_Prototype_BoardTarget") ?? new GameObject("CM_Prototype_BoardTarget");
                followTarget = targetObject.transform;
            }
        }

        private void ProcessRuntimeControls()
        {
            orbitOrZoomInputThisFrame = false;
            if (!EnableRuntimeControls || !Application.isPlaying)
            {
                return;
            }

            if (EnableMouseWheelZoom && TryReadMouseScroll(out var scrollY) && !Mathf.Approximately(scrollY, 0f))
            {
                zoomDistance = Mathf.Clamp(
                    zoomDistance - scrollY * MouseWheelZoomSpeed,
                    MinZoomDistance,
                    MaxZoomDistance);
                orbitOrZoomInputThisFrame = true;
            }

            if (!IsRuntimeShortcutBlocked() && EnableKeyboardYaw && TryReadKeyboardYaw(out var keyboardYawInput) && !Mathf.Approximately(keyboardYawInput, 0f))
            {
                orbitDegrees += keyboardYawInput * KeyboardYawDegreesPerSecond * Time.unscaledDeltaTime;
                orbitOrZoomInputThisFrame = true;
            }

            runtimeFollowOffset = normalizedOffsetDirection * zoomDistance;
        }

        // LoL-style free-pan: Y toggles whether WASD moves a world-anchored camera target
        // independent of the player. In fixed/follow mode, WASD is ignored; Q/E yaw remains
        // handled separately by ProcessRuntimeControls regardless of pan mode.
        private void ProcessPanControls()
        {
            if (!Application.isPlaying || !EnableEdgePan)
            {
                panModeEnabled = false;
                hasPanAnchor = false;
                return;
            }

            if (IsRuntimeShortcutBlocked())
            {
                return;
            }

            TryReadKeyboardPan(out var horizontalInput, out var forwardInput);
            var hasPanInput = !Mathf.Approximately(horizontalInput, 0f) || !Mathf.Approximately(forwardInput, 0f);

            if (WasTogglePanModePressed())
            {
                panModeEnabled = !panModeEnabled;
                if (panModeEnabled)
                {
                    // Anchor where the camera currently frames so enabling the mode does not jump.
                    panAnchorPosition = TryResolvePlayerTargetPosition(out var playerTarget)
                        ? playerTarget
                        : (followTarget != null ? followTarget.position : Vector3.zero);
                    hasPanAnchor = true;
                }
                else
                {
                    hasPanAnchor = false;
                }
            }

            if (!panModeEnabled)
            {
                hasPanAnchor = false;
                return;
            }

            if (WasRecenterPressed() && TryResolvePlayerTargetPosition(out var recenterTarget))
            {
                panAnchorPosition = recenterTarget;
                hasPanAnchor = true;
            }

            var camera = sourceCamera != null ? sourceCamera : Camera.main;
            if (camera == null || !hasPanInput)
            {
                return;
            }

            if (!hasPanAnchor)
            {
                panAnchorPosition = followTarget != null ? followTarget.position : Vector3.zero;
                hasPanAnchor = true;
            }

            var forward = ResolveHorizontalCameraForward(camera);
            var right = ResolveHorizontalCameraRight(camera);
            var direction = forward * forwardInput + right * horizontalInput;
            if (direction.sqrMagnitude > 1f)
            {
                direction.Normalize();
            }

            panAnchorPosition += direction * (Mathf.Max(0f, panSpeed) * Time.unscaledDeltaTime);
        }

        private static bool WasTogglePanModePressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.yKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Y);
#endif
        }

        private static bool WasRecenterPressed()
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard.spaceKey.wasPressedThisFrame;
#else
            return Input.GetKeyDown(KeyCode.Space);
#endif
        }

        private static bool TryReadMouseScroll(out float scrollY)
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            if (mouse == null)
            {
                scrollY = 0f;
                return false;
            }

            scrollY = mouse.scroll.ReadValue().y / 120f;
            return true;
#else
            scrollY = Input.mouseScrollDelta.y;
            return true;
#endif
        }

        private static bool IsRuntimeShortcutBlocked()
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return false;
            }

            var selected = eventSystem.currentSelectedGameObject;
            if (selected != null &&
                (selected.GetComponentInParent<InputField>() != null ||
                 selected.GetComponentInParent<TMP_InputField>() != null))
            {
                return true;
            }

            return false;
        }

        private static bool TryReadKeyboardYaw(out float yawInput)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                yawInput = 0f;
                return false;
            }

            yawInput = 0f;
            if (keyboard.qKey.isPressed)
            {
                yawInput -= 1f;
            }

            if (keyboard.eKey.isPressed)
            {
                yawInput += 1f;
            }

            return true;
#else
            yawInput = 0f;
            if (Input.GetKey(KeyCode.Q))
            {
                yawInput -= 1f;
            }

            if (Input.GetKey(KeyCode.E))
            {
                yawInput += 1f;
            }

            return true;
#endif
        }

        private static bool TryReadKeyboardPan(out float horizontalInput, out float forwardInput)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                horizontalInput = 0f;
                forwardInput = 0f;
                return false;
            }

            horizontalInput = 0f;
            forwardInput = 0f;
            if (keyboard.wKey.isPressed)
            {
                forwardInput += 1f;
            }

            if (keyboard.sKey.isPressed)
            {
                forwardInput -= 1f;
            }

            if (keyboard.dKey.isPressed)
            {
                horizontalInput += 1f;
            }

            if (keyboard.aKey.isPressed)
            {
                horizontalInput -= 1f;
            }

            return true;
#else
            horizontalInput = 0f;
            forwardInput = 0f;
            if (Input.GetKey(KeyCode.W))
            {
                forwardInput += 1f;
            }

            if (Input.GetKey(KeyCode.S))
            {
                forwardInput -= 1f;
            }

            if (Input.GetKey(KeyCode.D))
            {
                horizontalInput += 1f;
            }

            if (Input.GetKey(KeyCode.A))
            {
                horizontalInput -= 1f;
            }

            return true;
#endif
        }

        private static Vector3 ResolveHorizontalCameraForward(Camera camera)
        {
            var forward = camera != null ? camera.transform.forward : Vector3.forward;
            forward.y = 0f;
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }

        private static Vector3 ResolveHorizontalCameraRight(Camera camera)
        {
            var right = camera != null ? camera.transform.right : Vector3.right;
            right.y = 0f;
            return right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        }

        private void ConfigureVirtualCamera()
        {
            if (virtualCamera == null || followTarget == null)
            {
                return;
            }

            virtualCamera.Follow = followTarget;
            virtualCamera.LookAt = followTarget;
            virtualCamera.Priority = Mathf.Max(virtualCamera.Priority, 10);

            if (sourceCamera != null)
            {
                var baseFieldOfView = hasTemporaryZoomBaseLens ? temporaryZoomBaseFieldOfView : sourceCamera.fieldOfView;
                var baseOrthographicSize = hasTemporaryZoomBaseLens ? temporaryZoomBaseOrthographicSize : sourceCamera.orthographicSize;
                virtualCamera.m_Lens.FieldOfView = Mathf.Max(1f, baseFieldOfView * temporaryZoomMultiplier);
                virtualCamera.m_Lens.OrthographicSize = Mathf.Max(0.1f, baseOrthographicSize * temporaryZoomMultiplier);
                virtualCamera.m_Lens.NearClipPlane = sourceCamera.nearClipPlane;
                virtualCamera.m_Lens.FarClipPlane = sourceCamera.farClipPlane;
            }

            var plainTransposer = virtualCamera.GetCinemachineComponent<CinemachineTransposer>();
            if (plainTransposer != null && plainTransposer is not CinemachineOrbitalTransposer)
            {
                Destroy(plainTransposer);
                virtualCamera.InvalidateComponentPipeline();
            }

            var transposer = virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>()
                ?? virtualCamera.AddCinemachineComponent<CinemachineOrbitalTransposer>();
            // An action focus pans on its own (tighter) damping: the global 0.35 settles in ~1.05s, but the
            // beat the pan has to fit inside is 0.3–0.9s, so reusing it would leave the camera still drifting
            // when the action plays. The global value stays untouched so player following keeps its feel.
            var effectiveDamping = SnapOrbitWhileInput && orbitOrZoomInputThisFrame
                ? 0f
                : ((actionFocusActive || attackEmphasisActive) ? ActionFocusDamping : Damping);
            transposer.m_BindingMode = CinemachineTransposer.BindingMode.WorldSpace;
            var temporaryRotation = Quaternion.AngleAxis(temporaryYawDegrees, Vector3.up)
                * Quaternion.AngleAxis(temporaryPitchDegrees, Vector3.right);
            transposer.m_FollowOffset = Quaternion.AngleAxis(orbitDegrees, Vector3.up)
                * temporaryRotation
                * (runtimeFollowOffset * temporaryZoomMultiplier);
            transposer.m_XDamping = effectiveDamping;
            transposer.m_YDamping = effectiveDamping;
            transposer.m_ZDamping = effectiveDamping;
            transposer.m_Heading = new CinemachineOrbitalTransposer.Heading(
                CinemachineOrbitalTransposer.Heading.HeadingDefinition.WorldForward,
                0,
                0f);
            transposer.m_RecenterToTargetHeading = new AxisState.Recentering(false, 1f, 2f);
            transposer.m_XAxis.m_InputAxisName = string.Empty;
            transposer.m_XAxis.m_InputAxisValue = 0f;
            transposer.m_XAxis.Value = 0f;

            var composer = virtualCamera.GetCinemachineComponent<CinemachineComposer>()
                ?? virtualCamera.AddCinemachineComponent<CinemachineComposer>();
            composer.m_ScreenX = ScreenX;
            composer.m_ScreenY = ScreenY;
            composer.m_DeadZoneWidth = DeadZoneWidth;
            composer.m_DeadZoneHeight = DeadZoneHeight;
            composer.m_HorizontalDamping = effectiveDamping;
            composer.m_VerticalDamping = effectiveDamping;

            var impulseListener = virtualCamera.GetComponent<CinemachineImpulseListener>()
                ?? virtualCamera.gameObject.AddComponent<CinemachineImpulseListener>();
            impulseListener.m_ApplyAfter = CinemachineCore.Stage.Noise;
            impulseListener.m_ChannelMask = 1;
            impulseListener.m_Gain = 1f;
            impulseListener.m_Use2DDistance = false;
            impulseListener.m_UseCameraSpace = true;
            impulseListener.m_ReactionSettings.m_Duration = 0f;
        }

        // Drives a brief tutorial "look at this goal" pan: the follow target is parked on the given tile
        // (plus the usual framing offset) so Cinemachine eases the camera over to it; clearing the override
        // lets the camera ease back to the player. Damping makes both transitions smooth automatically.
        /// <summary>
        /// Points the rig at an action that happened away from the player
        /// (docs/monster-action-camera-focus-plan.md §3). Returns false without doing anything when the
        /// player is freely panning with Y — hijacking the camera out from under a deliberate player input
        /// is worse than missing the event — or when the rig is not bound.
        /// </summary>
        public bool TrySetActionFocus(Vector3 tileWorldPosition)
        {
            if (virtualCamera == null || followTarget == null || panModeEnabled)
            {
                return false;
            }

            actionFocusActive = true;
            actionFocusTilePosition = tileWorldPosition;
            UpdateFollowTarget();
            return true;
        }

        /// <summary>
        /// Leans the framing toward a monster attacking the player. Refused while the player is panning (their
        /// input wins) or while an action focus holds the camera (that framing is deliberate and current).
        /// </summary>
        public bool TrySetAttackEmphasis(Vector3 attackerTileWorldPosition)
        {
            if (virtualCamera == null || followTarget == null || panModeEnabled || actionFocusActive)
            {
                return false;
            }

            attackEmphasisActive = true;
            attackEmphasisAttackerPosition = attackerTileWorldPosition;
            UpdateFollowTarget();
            return true;
        }

        /// <summary>Drops the attack lean so the framing settles back onto the player.</summary>
        public void ClearAttackEmphasis()
        {
            if (!attackEmphasisActive)
            {
                return;
            }

            attackEmphasisActive = false;
            UpdateFollowTarget();
        }

        /// <summary>True while an attack lean is applied.</summary>
        public bool IsAttackEmphasisActive => attackEmphasisActive;

        /// <summary>Releases the action focus so the rig eases back to the player (once, at phase end).</summary>
        public void ClearActionFocus()
        {
            if (!actionFocusActive)
            {
                return;
            }

            actionFocusActive = false;
            UpdateFollowTarget();
        }

        /// <summary>True while an action focus owns the follow target.</summary>
        public bool IsActionFocusActive => actionFocusActive;

        /// <summary>
        /// How far the rig still has to travel to sit where its follow target says it belongs, as a fraction of
        /// the framing distance so the answer does not change with zoom. 0 means settled.
        ///
        /// This is the measurable form of "the camera has actually come back" (plan §10.13, definition 나).
        /// Deliberately NOT <see cref="CombatCameraController.IsCameraBlending"/>: releasing an action focus
        /// only moves the follow target, and Cinemachine damps the rig toward it — no brain blend is ever
        /// started, so the blend flag reads false for the entire return trip.
        ///
        /// Raw position, not final: impulse shake rides on the correction and would keep a settled camera
        /// reporting motion forever.
        ///
        /// Returns false when the rig cannot answer (unbound, or a pipeline without the transposer). Callers
        /// must treat that as "do not wait" — a camera that cannot report its position must never hold a turn.
        /// </summary>
        public bool TryGetFramingSettleFraction(out float fraction)
        {
            fraction = 0f;
            if (virtualCamera == null || followTarget == null)
            {
                return false;
            }

            var transposer = virtualCamera.GetCinemachineComponent<CinemachineOrbitalTransposer>();
            if (transposer == null)
            {
                return false;
            }

            // WorldSpace binding, so the rig's resting place is exactly target + offset (the same expression
            // ApplyRig builds the offset from).
            var offset = transposer.m_FollowOffset;
            var remaining = Vector3.Distance(virtualCamera.State.RawPosition, followTarget.position + offset);
            fraction = remaining / Mathf.Max(1f, offset.magnitude);
            return true;
        }

        public void SetTutorialFocusOverride(Vector3 tileWorldPosition)
        {
            tutorialFocusTilePosition = tileWorldPosition;
            tutorialFocusActive = true;
            UpdateFollowTarget();
        }

        public void ClearTutorialFocusOverride()
        {
            tutorialFocusActive = false;
            UpdateFollowTarget();
        }

        private void UpdateFollowTarget()
        {
            if (controller == null || followTarget == null)
            {
                return;
            }

            // A tutorial goal focus takes priority over everything else so the camera can briefly show a
            // distant objective (e.g. the memory stone) and then ease back once the override is cleared.
            if (tutorialFocusActive)
            {
                followTarget.position = tutorialFocusTilePosition + TargetOffset;
                followTarget.rotation = Quaternion.identity;
                return;
            }

            // An action focus outranks the player but never the tutorial: the tutorial override is an
            // explicit instruction the player is reading, while this is ambient framing of something that
            // just happened. Deliberately its own channel rather than reusing SetTutorialFocusOverride —
            // sharing one would let the two silently overwrite each other.
            if (actionFocusActive)
            {
                followTarget.position = actionFocusTilePosition + TargetOffset;
                followTarget.rotation = Quaternion.identity;
                return;
            }

            // Melee attack emphasis: the framing leans from the player toward the attacker. Ranked below the
            // action focus on purpose — if the camera has already been sent to a distant cluster, yanking it
            // back for an adjacent blow would undo the framing the player is currently reading.
            if (attackEmphasisActive && TryResolvePlayerTargetPosition(out var emphasisPlayerPosition))
            {
                followTarget.position = CombatCameraController.ResolveAttackEmphasisPoint(
                    emphasisPlayerPosition, attackEmphasisAttackerPosition + TargetOffset, AttackEmphasisBias);
                followTarget.rotation = Quaternion.identity;
                return;
            }

            // While panning, the follow target is the world-anchored pan position so the camera stays put
            // regardless of where the player moves (until Space re-snaps it or the mode is toggled off).
            if (panModeEnabled && hasPanAnchor)
            {
                followTarget.position = panAnchorPosition;
                followTarget.rotation = Quaternion.identity;
                return;
            }

            if (TryResolvePlayerTargetPosition(out var playerTargetPosition))
            {
                followTarget.position = playerTargetPosition;
                followTarget.rotation = Quaternion.identity;
            }
        }

        // Filming-only override of the player-controllable orbit/zoom (an ingame-style trailer beat poses
        // the play camera the way a player might have left it). Base values are captured on first apply so
        // repeated jittered takes never drift, and Clear restores the session's own pose.
        private bool filmingPoseApplied;
        private float filmingPreviousOrbitDegrees;
        private float filmingPreviousZoomDistance;

        public void SetFilmingCameraPose(float orbitYawDegrees, float zoomDistanceOverride, float zoomOffset)
        {
            if (!filmingPoseApplied)
            {
                filmingPoseApplied = true;
                filmingPreviousOrbitDegrees = orbitDegrees;
                filmingPreviousZoomDistance = zoomDistance;
            }

            orbitDegrees = orbitYawDegrees;
            var baseZoom = zoomDistanceOverride > 0f ? zoomDistanceOverride : filmingPreviousZoomDistance;
            zoomDistance = Mathf.Clamp(baseZoom + zoomOffset, MinZoomDistance, MaxZoomDistance);
            runtimeFollowOffset = normalizedOffsetDirection * zoomDistance;
        }

        public void ClearFilmingCameraPose()
        {
            if (!filmingPoseApplied)
            {
                return;
            }

            filmingPoseApplied = false;
            orbitDegrees = filmingPreviousOrbitDegrees;
            zoomDistance = filmingPreviousZoomDistance;
            runtimeFollowOffset = normalizedOffsetDirection * zoomDistance;
        }

        /// <summary>
        /// Snaps the follow rig onto the player's current position, skipping the transposer damping.
        /// A debug teleport moves the player instantly, and without this the camera spends seconds flying
        /// across the (fogged, black) map to catch up — an ingame-style trailer beat must instead open
        /// already framed on the player. OnTargetObjectWarped shifts Cinemachine's internal damping state
        /// by the warp delta, which is the engine's own teleport contract.
        /// </summary>
        public void SnapToPlayerNow()
        {
            if (virtualCamera == null || followTarget == null)
            {
                return;
            }

            var before = followTarget.position;
            UpdateFollowTarget();
            var delta = followTarget.position - before;
            if (delta.sqrMagnitude > 0.0001f)
            {
                virtualCamera.OnTargetObjectWarped(followTarget, delta);
            }
        }

        // Resolves the world position the camera frames the player at (player world position + TargetOffset),
        // using the best available player anchor. Returns false when no player anchor is resolvable yet.
        private bool TryResolvePlayerTargetPosition(out Vector3 targetPosition)
        {
            if (controller != null &&
                ((PreferPlayerRendererBoundsCenter &&
                        controller.TryGetPlayerVisualBoundsCenterWorldPosition(out var playerWorldPosition)) ||
                    controller.TryGetCombatantEffectAnchorWorldPosition("player", out playerWorldPosition) ||
                    controller.TryGetPlayerWorldPosition(out playerWorldPosition)))
            {
                targetPosition = playerWorldPosition + TargetOffset;
                return true;
            }

            targetPosition = default;
            return false;
        }

        private bool PreferPlayerRendererBoundsCenter =>
            profile != null ? profile.PreferPlayerRendererBoundsCenter : preferPlayerRendererBoundsCenter;

        private Vector3 TargetOffset => profile != null ? profile.TargetOffset : targetOffset;

        private Vector3 FollowOffset
        {
            get
            {
                var offset = profile != null ? profile.FollowOffset : followOffset;
                return offset.sqrMagnitude > 0.0001f ? offset : new Vector3(0f, 9.5f, -7f);
            }
        }

        private float Damping => profile != null ? profile.Damping : Mathf.Max(0f, damping);

        private float ActionFocusDamping =>
            profile != null ? profile.ActionFocusDamping : Mathf.Max(0f, actionFocusDamping);

        private float AttackEmphasisBias =>
            profile != null ? profile.AttackEmphasisBias : Mathf.Clamp01(attackEmphasisBias);
        private float ScreenX => profile != null ? profile.ScreenX : Mathf.Clamp01(screenX);
        private float ScreenY => profile != null ? profile.ScreenY : Mathf.Clamp01(screenY);
        private float DeadZoneWidth => profile != null ? profile.DeadZoneWidth : Mathf.Clamp(deadZoneWidth, 0f, 2f);
        private float DeadZoneHeight => profile != null ? profile.DeadZoneHeight : Mathf.Clamp(deadZoneHeight, 0f, 2f);
        private bool EnableRuntimeControls => profile != null ? profile.EnableRuntimeControls : enableRuntimeControls;
        private bool EnableMouseWheelZoom => profile != null ? profile.EnableMouseWheelZoom : enableMouseWheelZoom;
        private bool EnableKeyboardYaw => profile != null ? profile.EnableKeyboardYaw : enableKeyboardYaw;
        private float MinZoomDistance => profile != null ? profile.MinZoomDistance : Mathf.Max(1f, minZoomDistance);
        private float MaxZoomDistance => profile != null ? profile.MaxZoomDistance : Mathf.Max(MinZoomDistance, maxZoomDistance);
        private float MouseWheelZoomSpeed => profile != null ? profile.MouseWheelZoomSpeed : Mathf.Max(0f, mouseWheelZoomSpeed);
        private float KeyboardYawDegreesPerSecond =>
            profile != null ? profile.KeyboardYawDegreesPerSecond : Mathf.Max(0f, keyboardYawDegreesPerSecond);
        private bool SnapOrbitWhileInput => profile != null ? profile.SnapOrbitWhileInput : snapOrbitWhileInput;
        private bool EnableEdgePan => enableEdgePan;
    }
}
