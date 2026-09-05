using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Flow.Unity;
using SeoulPlayup.Map.Unity;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace SeoulPlayup.Editor.Flow
{
    public static class MainGameplayLobbySceneSplitBuilder
    {
        private const string PrototypeScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";
        private const string GameSceneFolder = "Assets/Scenes/Game";
        private const string DataFolder = "Assets/Data/Stages";
        private const string BootScenePath = "Assets/Scenes/Game/Boot.unity";
        private const string LobbyScenePath = "Assets/Scenes/Game/Lobby.unity";
        private const string MainGameplayScenePath = "Assets/Scenes/Game/MainGameplay.unity";
        private const string StagePath = "Assets/Data/Stages/Stage_001_Prototype.asset";
        private const string CatalogPath = "Assets/Data/Stages/StageCatalog.asset";

        [MenuItem("Seoul Playup/Flow/Rebuild Boot Lobby MainGameplay Slice")]
        public static void RebuildSlice()
        {
            EnsureFolders();
            CopyMainGameplayScene();
            var stage = CreateOrUpdateStageAssets();
            ConfigureMainGameplay(stage);
            CreateBootScene(stage);
            CreateLobbyScene(stage);
            ConfigureBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Boot/Lobby/MainGameplay scene split slice rebuilt successfully.");
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Scenes", "Game");
            EnsureFolder("Assets", "Data");
            EnsureFolder("Assets/Data", "Stages");
        }

        private static void EnsureFolder(string parent, string child)
        {
            var path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static void CopyMainGameplayScene()
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(PrototypeScenePath))
                throw new InvalidOperationException($"Missing source scene: {PrototypeScenePath}");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MainGameplayScenePath) != null)
                AssetDatabase.DeleteAsset(MainGameplayScenePath);

            if (!AssetDatabase.CopyAsset(PrototypeScenePath, MainGameplayScenePath))
                throw new InvalidOperationException($"Failed to copy {PrototypeScenePath} to {MainGameplayScenePath}");
        }

        private static StageDefinition CreateOrUpdateStageAssets()
        {
            var stage = AssetDatabase.LoadAssetAtPath<StageDefinition>(StagePath);
            if (stage == null)
            {
                stage = ScriptableObject.CreateInstance<StageDefinition>();
                AssetDatabase.CreateAsset(stage, StagePath);
            }

            var source = FindPrototypeMapSource();
            var note = source != null
                ? "Map source captured from the PrototypeTest/MainGameplay sparse map loader."
                : "TODO: mapSource was not auto-detected; MainGameplay keeps scene-authored loader defaults until wired.";
            stage.Configure(
                "stage_001_prototype",
                "Prototype Stage",
                "Initial gameplay stage based on PrototypeTest.",
                source,
                null,
                note: note);
            EditorUtility.SetDirty(stage);

            var catalog = AssetDatabase.LoadAssetAtPath<StageCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<StageCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            catalog.SetStages(new[] { stage });
            EditorUtility.SetDirty(catalog);
            return stage;
        }

        private static HexSparseMapAuthoringSource FindPrototypeMapSource()
        {
            var scene = EditorSceneManager.OpenScene(MainGameplayScenePath, OpenSceneMode.Single);
            var loader = FindFirstSceneComponent<HexSparseMapSourceLoader>(scene);
            return loader != null ? loader.SparseSource : null;
        }

        private static void ConfigureMainGameplay(StageDefinition stage)
        {
            var scene = EditorSceneManager.OpenScene(MainGameplayScenePath, OpenSceneMode.Single);

            DestroyNamed(scene, "SCREEN_TITLE", "GameTitle_TMP", "StartButton", "ExitButton");
            DisableNamed(scene, "03 Runtime Debug UI", "PlayerState Debug Panel", "CameraLightTest Runtime Panel");
            EnsureSingleActiveEventSystem(scene);

            var root = GameObject.Find("GameplayFlow") ?? new GameObject("GameplayFlow");
            var controller = root.GetComponent<MainGameplayController>() ?? root.AddComponent<MainGameplayController>();
            var loader = FindFirstSceneComponent<HexSparseMapSourceLoader>(scene);
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("mapSourceLoader").objectReferenceValue = loader;
            serialized.FindProperty("applyStageMapSource").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        private static void DestroyNamed(Scene scene, params string[] names)
        {
            foreach (var go in SceneGameObjects(scene).Where(go => names.Contains(go.name)).ToArray())
                UnityEngine.Object.DestroyImmediate(go);
        }

        private static void DisableNamed(Scene scene, params string[] names)
        {
            foreach (var go in SceneGameObjects(scene).Where(go => names.Contains(go.name)))
                go.SetActive(false);
        }

        private static void EnsureSingleActiveEventSystem(Scene scene)
        {
            var eventSystems = SceneGameObjects(scene)
                .Select(go => go.GetComponent<EventSystem>())
                .Where(es => es != null)
                .OrderBy(es => GetHierarchyPath(es.transform))
                .ToList();

            for (var i = 0; i < eventSystems.Count; i++)
                eventSystems[i].gameObject.SetActive(i == 0);
        }

        private static void CreateBootScene(StageDefinition stage)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Boot";

            var boot = new GameObject("Boot");
            var appRoot = new GameObject("AppRoot");
            appRoot.transform.SetParent(boot.transform);

            var session = appRoot.AddComponent<GameSession>();
            var flow = appRoot.AddComponent<SceneFlowController>();
            AssignObject(session, "stageCatalog", AssetDatabase.LoadAssetAtPath<StageCatalog>(CatalogPath));
            AssignObject(session, "currentStage", stage);
            AssignBool(flow, "loadLobbyOnStart", true);

            EditorSceneManager.SaveScene(scene, BootScenePath);
        }

        private static void CreateLobbyScene(StageDefinition stage)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "Lobby";

            new GameObject("Lobby");
            var camera = new GameObject("LobbyCamera");
            camera.tag = "MainCamera";
            var cam = camera.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.08f);
            camera.transform.position = new Vector3(0f, 0f, -10f);

            var canvas = new GameObject("LobbyCanvas");
            var canvasComponent = canvas.AddComponent<Canvas>();
            canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.AddComponent<CanvasScaler>();
            canvas.AddComponent<GraphicRaycaster>();

            var mainMenu = CreateRect("MainMenuPanel", canvas.transform, new Vector2(0.5f, 0.5f), new Vector2(720f, 420f));
            var title = CreateText("TitleText", mainMenu.transform, "Seoul Playup", 56, new Vector2(0f, 110f), new Vector2(620f, 90f));
            title.alignment = TextAlignmentOptions.Center;

            var startButton = CreateButton("StartButton", mainMenu.transform, "Start Prototype Stage", new Vector2(0f, 0f));
            var exitButton = CreateButton("ExitButton", mainMenu.transform, "Exit", new Vector2(0f, -86f));

            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
            eventSystem.AddComponent<InputSystemUIInputModule>();
#else
            eventSystem.AddComponent<StandaloneInputModule>();
#endif

            var controllerObject = new GameObject("LobbyController");
            var controller = controllerObject.AddComponent<LobbyController>();
            AssignObject(controller, "stageCatalog", AssetDatabase.LoadAssetAtPath<StageCatalog>(CatalogPath));
            AssignObject(controller, "defaultStage", stage);
            UnityEventTools.AddPersistentListener(startButton.onClick, controller.StartDefaultStage);
            UnityEventTools.AddPersistentListener(exitButton.onClick, controller.QuitGame);

            EditorSceneManager.SaveScene(scene, LobbyScenePath);
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 pivot, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.anchorMin = pivot;
            rect.anchorMax = pivot;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, string value, float fontSize, Vector2 position, Vector2 size)
        {
            var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), size);
            rect.anchoredPosition = position;
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = fontSize;
            text.color = Color.white;
            return text;
        }

        private static Button CreateButton(string name, Transform parent, string label, Vector2 position)
        {
            var rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(360f, 62f));
            rect.anchoredPosition = position;
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.18f, 0.28f, 1f);
            var button = rect.gameObject.AddComponent<Button>();
            var text = CreateText("Label", rect, label, 26, Vector2.zero, new Vector2(330f, 46f));
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private static void ConfigureBuildSettings()
        {
            var scenes = new[]
            {
                new EditorBuildSettingsScene(BootScenePath, true),
                new EditorBuildSettingsScene(LobbyScenePath, true),
                new EditorBuildSettingsScene(MainGameplayScenePath, true),
            };
            EditorBuildSettings.scenes = scenes;
        }

        private static T FindFirstSceneComponent<T>(Scene scene) where T : Component
        {
            return SceneGameObjects(scene).Select(go => go.GetComponent<T>()).FirstOrDefault(component => component != null);
        }

        private static IEnumerable<GameObject> SceneGameObjects(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                yield return transform.gameObject;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            var parts = new Stack<string>();
            while (transform != null)
            {
                parts.Push(transform.name);
                transform = transform.parent;
            }

            return string.Join("/", parts);
        }

        private static void AssignObject(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignBool(UnityEngine.Object target, string propertyName, bool value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
