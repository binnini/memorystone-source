#if UNITY_EDITOR
using System;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Procedurally (re)builds Assets/Scenes/Dev/UiGallery.unity: a dummy camera + EventSystem, a gameplay
    /// Canvas (2200x1238) with the shipping CardLane + Deck overlay + a phase dock, a lobby Canvas
    /// (1920x1080) with ScreenLobby, a self-contained story cutscene canvas, and the driving
    /// <see cref="UiGalleryController"/>. Shipping prefabs are instantiated with their prefab link intact so
    /// gallery-side edits flow back to the assets. The scene is NOT added to EditorBuildSettings.
    ///
    /// Idempotent: rerun the menu to rebuild from scratch. See docs/ui-gallery-plan.md.
    /// </summary>
    public static class UiGallerySceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Dev/UiGallery.unity";

        private const string CardLanePrefabPath = "Assets/Prefabs/UI/Prototype/CardLane.prefab";
        private const string DeckOverlayPrefabPath = "Assets/Prefabs/UI/Prototype/Deck Pile List Overlay Root.prefab";
        private const string SidebarSystemPrefabPath = "Assets/Prefabs/UI/Prototype/SidebarSystem.prefab";
        private const string ChoiceOverlayPrefabPath = "Assets/Prefabs/UI/Prototype/Choice Overlay Root.prefab";
        // The 손패 선택 prompt/confirm panel. It lives only in the gameplay scenes, so the gallery could show a
        // hand in its selection *states* but never the panel that drives them (P6 T4).
        private const string CardSelectionOverlayPrefabPath = "Assets/Prefabs/UI/Prototype/Card Selection Overlay Root.prefab";
        private const string ScreenLobbyPrefabPath = "Assets/Prefabs/UI/Lobby/ScreenLobby.prefab";
        private const string CutsceneCanvasPrefabPath = "Assets/Prefabs/UI/Story/StoryCutsceneCanvas.prefab";
        private const string CardRewardPrefabPath = "Assets/Resources/UI/Prototype/Card Reward Overlay Root.prefab";
        private const string GameOverPrefabPath = "Assets/Prefabs/UI/Prototype/Game Over Overlay Root.prefab";
        private const string GameVictoryPrefabPath = "Assets/Prefabs/UI/Prototype/Game Victory Overlay Root.prefab";
        private const string BossHudPrefabPath = "Assets/Prefabs/UI/BossHud.prefab";

        private static readonly Vector2 GameplayReferenceResolution = new Vector2(2200f, 1238f);
        private static readonly Vector2 LobbyReferenceResolution = new Vector2(1920f, 1080f);

        [MenuItem("Seoul Playup/Dev/Create UI Gallery Scene")]
        public static void CreateOrUpdateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "UiGallery";

            CreateCamera();
            CreateEventSystem();

            var gameplayGroup = new GameObject("GameplayGroup");
            var gameplayCanvas = CreateCanvas("GameplayCanvas", GameplayReferenceResolution, gameplayGroup.transform);
            InstantiateUnder(CardLanePrefabPath, gameplayCanvas.transform);
            InstantiateUnder(DeckOverlayPrefabPath, gameplayCanvas.transform);
            InstantiateUnder(SidebarSystemPrefabPath, gameplayCanvas.transform);
            var cardSelectionOverlay = InstantiateUnder(CardSelectionOverlayPrefabPath, gameplayCanvas.transform);
            if (cardSelectionOverlay != null)
            {
                cardSelectionOverlay.SetActive(false); // BottomCardHudView activates it when a selection is live
            }
            var choiceOverlay = InstantiateUnder(ChoiceOverlayPrefabPath, gameplayCanvas.transform);
            if (choiceOverlay != null)
            {
                choiceOverlay.SetActive(false);
            }

            CreatePhaseDock(gameplayCanvas.transform);
            // 보스 HUD는 알파 0으로 시작하므로(살아있는 보스 없음) 다른 엔트리를 가리지 않는다.
            InstantiateUnder(BossHudPrefabPath, gameplayCanvas.transform);

            var lobbyGroup = new GameObject("LobbyGroup");
            var lobbyCanvas = CreateCanvas("LobbyCanvas", LobbyReferenceResolution, lobbyGroup.transform);
            InstantiateUnder(ScreenLobbyPrefabPath, lobbyCanvas.transform);
            lobbyGroup.SetActive(false);

            var cutsceneGroup = new GameObject("CutsceneGroup");
            var cutsceneInstance = InstantiateUnder(CutsceneCanvasPrefabPath, cutsceneGroup.transform);
            DisableCutsceneSceneAdvance(cutsceneInstance);
            cutsceneGroup.SetActive(false);

            var controllerObject = new GameObject("UiGallery");
            var controller = controllerObject.AddComponent<UiGalleryController>();
            WireController(controller, gameplayGroup, gameplayCanvas, lobbyGroup, cutsceneGroup, choiceOverlay);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"[UiGallery] Built UI gallery scene at {ScenePath} (not registered in build settings).");
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.10f, 0.13f, 1f);
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateEventSystem()
        {
            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();

            // The project runs the new Input System; add InputSystemUIInputModule by reflection so the Dev
            // asmdef does not need a hard reference to Unity.InputSystem.
            var moduleType = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
            if (moduleType != null)
            {
                eventSystemObject.AddComponent(moduleType);
            }
            else
            {
                eventSystemObject.AddComponent<StandaloneInputModule>();
            }
        }

        private static Canvas CreateCanvas(string name, Vector2 referenceResolution, Transform parent)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(parent, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private static void CreatePhaseDock(Transform canvasTransform)
        {
            var dockObject = new GameObject("TurnPhaseDock", typeof(RectTransform));
            dockObject.transform.SetParent(canvasTransform, false);
            dockObject.AddComponent<TurnPhaseDockView>();
        }

        private static GameObject InstantiateUnder(string prefabPath, Transform parent)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[UiGallery] Prefab not found: {prefabPath}");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.SetParent(parent, false);
            return instance;
        }

        // Prevent the cutscene from ending / switching scenes when it auto-advances to the end while previewed.
        // The controller type lives in the Flow assembly which Dev does not reference, so route through the
        // serialized property by component-name match.
        private static void DisableCutsceneSceneAdvance(GameObject cutsceneInstance)
        {
            if (cutsceneInstance == null)
            {
                return;
            }

            foreach (var component in cutsceneInstance.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component.GetType().Name != "StoryCutsceneController")
                {
                    continue;
                }

                var serialized = new SerializedObject(component);
                var advanceEndsScene = serialized.FindProperty("advanceEndsScene");
                if (advanceEndsScene != null)
                {
                    advanceEndsScene.boolValue = false;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }

                return;
            }
        }

        private static void WireController(
            UiGalleryController controller,
            GameObject gameplayGroup,
            Canvas gameplayCanvas,
            GameObject lobbyGroup,
            GameObject cutsceneGroup,
            GameObject choiceOverlay)
        {
            var cardReward = AssetDatabase.LoadAssetAtPath<GameObject>(CardRewardPrefabPath);
            var gameOver = AssetDatabase.LoadAssetAtPath<GameObject>(GameOverPrefabPath);
            var victory = AssetDatabase.LoadAssetAtPath<GameObject>(GameVictoryPrefabPath);

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("gameplayGroup").objectReferenceValue = gameplayGroup;
            serialized.FindProperty("gameplayCanvasRoot").objectReferenceValue = gameplayCanvas.transform;
            serialized.FindProperty("lobbyGroup").objectReferenceValue = lobbyGroup;
            serialized.FindProperty("cutsceneGroup").objectReferenceValue = cutsceneGroup;
            serialized.FindProperty("choiceOverlayRoot").objectReferenceValue = choiceOverlay;
            serialized.FindProperty("cardRewardPrefab").objectReferenceValue = cardReward;
            serialized.FindProperty("gameOverPrefab").objectReferenceValue = gameOver;
            serialized.FindProperty("victoryPrefab").objectReferenceValue = victory;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
