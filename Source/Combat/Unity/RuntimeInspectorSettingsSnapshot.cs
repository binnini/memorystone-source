using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    internal readonly struct RuntimeInspectorSettingsSnapshot
    {
        public RuntimeInspectorSettingsSnapshot(
            HexSparseMapAuthoringSource sparseSource,
            AtlasTilePresentationView atlasTilePresentationView,
            HexMapInputController inputController,
            Camera prototype3DCamera,
            HexCoord playerStart,
            HexCoord enemyStart,
            string playerCombatProfileId,
            int playerHp,
            int enemyHp,
            int playerMovePoints,
            int playerVisionRange,
            int playerAttackDamage,
            int playerBlock,
            int actionHandSize,
            int enemyChaseRange,
            int enemyAttackDamage,
            Color enemyMarkerColor,
            float enemyMarkerRadius,
            float enemyMarkerHeightOffset,
            bool revealAllMapCellsInDebugMode,
            bool showOverlayDebugControls,
            bool showPlayerMovementOverlay,
            bool showPlayerActionOverlay,
            bool showMonsterMoveOverlay,
            bool showMonsterAttackOverlay,
            bool showMonsterChaseOverlay,
            CombatOverlayRendererBackend overlayRendererBackend,
            bool keepCameraCenteredOnPlayer,
            Vector3 cameraPlayerOffset,
            bool autoCalculateCameraPlayerOffset,
            float cameraFollowDistance,
            Vector2 cameraFramingOffset,
            Vector3 cameraEulerAngles,
            float cameraOrthographicSize,
            float minCameraOrthographicSize,
            float maxCameraOrthographicSize,
            float cameraFollowSmoothing)
        {
            SparseSource = sparseSource;
            AtlasTilePresentationView = atlasTilePresentationView;
            InputController = inputController;
            Prototype3DCamera = prototype3DCamera;
            PlayerStart = playerStart;
            EnemyStart = enemyStart;
            PlayerCombatProfileId = playerCombatProfileId ?? string.Empty;
            PlayerHp = playerHp;
            EnemyHp = enemyHp;
            PlayerMovePoints = playerMovePoints;
            PlayerVisionRange = playerVisionRange;
            PlayerAttackDamage = playerAttackDamage;
            PlayerBlock = playerBlock;
            ActionHandSize = actionHandSize;
            EnemyChaseRange = enemyChaseRange;
            EnemyAttackDamage = enemyAttackDamage;
            EnemyMarkerColor = enemyMarkerColor;
            EnemyMarkerRadius = enemyMarkerRadius;
            EnemyMarkerHeightOffset = enemyMarkerHeightOffset;
            RevealAllMapCellsInDebugMode = revealAllMapCellsInDebugMode;
            ShowOverlayDebugControls = showOverlayDebugControls;
            ShowPlayerMovementOverlay = showPlayerMovementOverlay;
            ShowPlayerActionOverlay = showPlayerActionOverlay;
            ShowMonsterMoveOverlay = showMonsterMoveOverlay;
            ShowMonsterAttackOverlay = showMonsterAttackOverlay;
            ShowMonsterChaseOverlay = showMonsterChaseOverlay;
            OverlayRendererBackend = overlayRendererBackend;
            KeepCameraCenteredOnPlayer = keepCameraCenteredOnPlayer;
            CameraPlayerOffset = cameraPlayerOffset;
            AutoCalculateCameraPlayerOffset = autoCalculateCameraPlayerOffset;
            CameraFollowDistance = cameraFollowDistance;
            CameraFramingOffset = cameraFramingOffset;
            CameraEulerAngles = cameraEulerAngles;
            CameraOrthographicSize = cameraOrthographicSize;
            MinCameraOrthographicSize = minCameraOrthographicSize;
            MaxCameraOrthographicSize = maxCameraOrthographicSize;
            CameraFollowSmoothing = cameraFollowSmoothing;
        }

        public HexSparseMapAuthoringSource SparseSource { get; }
        public AtlasTilePresentationView AtlasTilePresentationView { get; }
        public HexMapInputController InputController { get; }
        public Camera Prototype3DCamera { get; }
        public HexCoord PlayerStart { get; }
        public HexCoord EnemyStart { get; }
        public string PlayerCombatProfileId { get; }
        public int PlayerHp { get; }
        public int EnemyHp { get; }
        public int PlayerMovePoints { get; }
        public int PlayerVisionRange { get; }
        public int PlayerAttackDamage { get; }
        public int PlayerBlock { get; }
        public int ActionHandSize { get; }
        public int EnemyChaseRange { get; }
        public int EnemyAttackDamage { get; }
        public Color EnemyMarkerColor { get; }
        public float EnemyMarkerRadius { get; }
        public float EnemyMarkerHeightOffset { get; }
        public bool RevealAllMapCellsInDebugMode { get; }
        public bool ShowOverlayDebugControls { get; }
        public bool ShowPlayerMovementOverlay { get; }
        public bool ShowPlayerActionOverlay { get; }
        public bool ShowMonsterMoveOverlay { get; }
        public bool ShowMonsterAttackOverlay { get; }
        public bool ShowMonsterChaseOverlay { get; }
        public CombatOverlayRendererBackend OverlayRendererBackend { get; }
        public bool KeepCameraCenteredOnPlayer { get; }
        public Vector3 CameraPlayerOffset { get; }
        public bool AutoCalculateCameraPlayerOffset { get; }
        public float CameraFollowDistance { get; }
        public Vector2 CameraFramingOffset { get; }
        public Vector3 CameraEulerAngles { get; }
        public float CameraOrthographicSize { get; }
        public float MinCameraOrthographicSize { get; }
        public float MaxCameraOrthographicSize { get; }
        public float CameraFollowSmoothing { get; }
    }
}
