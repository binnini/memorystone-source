#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// One-button transfer of the *confirmed* art direction from the ArtLookdev scene (treated as the
    /// art SoT) into the canonical MainGameplay scene. Copies only the scene-local values that cannot be
    /// shared through assets: the environment <see cref="RenderSettings"/> subset (ambient, fog, skybox,
    /// reflection) and the directional "NightRig" moon-light rig. Everything else the two scenes need in
    /// common — post-processing Volume Profile, terrain/monster materials, prop point-lights — already
    /// lives in shared assets/prefabs and reflects automatically, so it is intentionally left untouched.
    ///
    /// Deliberately an explicit menu action, not a live prefab link: ArtLookdev is a throwaway staging
    /// scene where half-finished experiments live. Syncing on demand means only values the designer has
    /// committed to reach the shipping scene.
    ///
    /// Idempotent: re-running replaces the previously synced rig instead of stacking duplicates.
    ///
    /// NOTE (look-preset system, 2026-07-22): the gameplay-authoritative look is now the stage's
    /// <see cref="EnvironmentLookPreset"/> (see <see cref="LookPresetAuthoring"/>), applied at stage entry.
    /// This sync is kept as a convenience for refreshing MainGameplay's <i>scene-authored</i> values — the
    /// fallback look when a scene is opened directly or run without a stage/preset. It is no longer the
    /// primary path for shipping a look; capture into a preset instead.
    /// </summary>
    public static class LookdevEnvSync
    {
        private const string LookdevScenePath = "Assets/Scenes/Dev/ArtLookdev.unity";
        private const string GameScenePath = "Assets/Scenes/Game/MainGameplay.unity";
        private const string NightRigName = "NightRig";
        private const string SyncedRootName = "── ENV LIGHTING (synced from Lookdev) ──";
        private const string MainCameraName = "Main Camera";
        private const string SkyboxCameraName = "Skybox Camera";

        [MenuItem("Seoul Playup/Dev/Sync Lookdev Env+Lighting → MainGameplay")]
        public static void SyncToMainGameplay()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.Log("[LookdevEnvSync] Cancelled — unsaved scenes were not saved.");
                return;
            }

            var gameScene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var lookdevScene = EditorSceneManager.OpenScene(LookdevScenePath, OpenSceneMode.Additive);

            // --- Capture from the SoT (ArtLookdev) ---
            SceneManager.SetActiveScene(lookdevScene);
            var env = EnvSettings.Capture();
            var cameras = CameraLookSettings.Capture(lookdevScene);

            var nightRig = lookdevScene.GetRootGameObjects()
                .Select(go => FindByName(go.transform, NightRigName))
                .FirstOrDefault(t => t != null);
            if (nightRig == null)
            {
                EditorSceneManager.CloseScene(lookdevScene, removeScene: true);
                Debug.LogError($"[LookdevEnvSync] Could not find a '{NightRigName}' GameObject in {LookdevScenePath}. Aborted; MainGameplay untouched.");
                return;
            }

            var rigClone = Object.Instantiate(nightRig.gameObject);
            rigClone.name = NightRigName; // strip the "(Clone)" suffix
            SceneManager.MoveGameObjectToScene(rigClone, gameScene);

            // --- Apply into the canonical scene (MainGameplay) ---
            SceneManager.SetActiveScene(gameScene);
            env.Apply();

            // Replace any previously synced rig so repeated runs stay idempotent.
            var oldRoot = gameScene.GetRootGameObjects().FirstOrDefault(go => go.name == SyncedRootName);
            if (oldRoot != null)
            {
                Object.DestroyImmediate(oldRoot);
            }

            var syncedRoot = new GameObject(SyncedRootName);
            SceneManager.MoveGameObjectToScene(syncedRoot, gameScene);
            syncedRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rigClone.transform.SetParent(syncedRoot.transform, worldPositionStays: true);

            // The synced NightRig is now the authoritative directional lighting. Remove any stray
            // directional lights outside the synced root (e.g. the scene's legacy standalone moon) so
            // the game matches the lookdev rig exactly rather than double-lighting.
            var removed = new List<string>();
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type != LightType.Directional) continue;
                if (light.transform.IsChildOf(syncedRoot.transform)) continue;
                if (light.gameObject.scene != gameScene) continue;
                removed.Add(light.gameObject.name);
                Object.DestroyImmediate(light.gameObject);
            }

            var cameraNote = cameras.ApplyTo(gameScene);
            var visibilityNote = SyncVisibilityPresentation(lookdevScene, gameScene);

            EditorSceneManager.CloseScene(lookdevScene, removeScene: true);
            EditorSceneManager.MarkSceneDirty(gameScene);
            EditorSceneManager.SaveScene(gameScene);

            var syncedMoons = syncedRoot.GetComponentsInChildren<Light>(true)
                .Count(l => l.type == LightType.Directional);
            var removedNote = removed.Count == 0 ? "none" : string.Join(", ", removed);
            Debug.Log($"[LookdevEnvSync] Synced ArtLookdev → MainGameplay. " +
                      $"RenderSettings(ambient/fog/skybox/reflection) applied; {syncedMoons} directional moon light(s) placed under '{SyncedRootName}'. " +
                      $"Removed stray directional lights: {removedNote}. Cameras: {cameraNote}. Visibility: {visibilityNote}. Scene saved.");
        }

        // 암시야(visibility) 표현의 정본은 이제 공유 VisibilityPresentationSettings 에셋이다(양 씬이
        // 같은 에셋을 visibilitySettings 슬롯으로 참조 — Awake에 로컬 필드로 복사됨). 아래 목록은
        // ① 그 슬롯 참조 자체와 ② 폴백으로 남는 로컬 필드 복사본까지 정렬해 어느 경로로도 드리프트가
        // 남지 않게 한다. 플레이어 마커(playerMarkerPrefab/offset/scale)는 게임플레이 배선이라 제외.
        private static readonly string[] VisibilityLookFields =
        {
            "visibilitySettings",
            "visibilityUnknownColor",
            "visibilityHintedColor",
            "visibilityOverlayLift",
            "fogUnknownTint",
            "fogHintedTint",
            "unknownFogAmount",
            "hintedFogAmount",
            "visibilityPresentationMode",
            "visibilityLightingShader",
            "visibilityUnknownLighting",
            "visibilityHintedLighting",
            "visibilityRevealedLighting",
            "visibilityEmissionFloor",
            "visibilityLightingMaskResolution",
            "visibilityLightingMaskBlurPasses",
            // #1(2026-08-20): hardEdge는 원래 이 목록에서 빠져 있었다(드리프트 구멍) — 그라데이션·육각
            // 스냅 손잡이와 함께 등재.
            "visibilityLightingHardEdge",
            "visibilityLightingEdgeGradation",
            "visibilityLightingHexSnap",
            "visibilityRevealedFadeMaxSteps",
            "visibilityRevealedFadeFloorAlpha",
            "visibilityRevealedFadeColor"
        };

        private static string SyncVisibilityPresentation(Scene lookdevScene, Scene gameScene)
        {
            var source = FindPresentationView(lookdevScene);
            var target = FindPresentationView(gameScene);
            if (source == null || target == null)
            {
                return $"view not found (lookdev={(source != null)}, game={(target != null)}) — skipped";
            }

            var sourceSerialized = new SerializedObject(source);
            var targetSerialized = new SerializedObject(target);
            var copied = 0;
            var missing = new List<string>();
            foreach (var field in VisibilityLookFields)
            {
                var property = sourceSerialized.FindProperty(field);
                if (property == null)
                {
                    missing.Add(field); // field renamed/removed — surface instead of silently skipping
                    continue;
                }

                targetSerialized.CopyFromSerializedProperty(property);
                copied++;
            }

            targetSerialized.ApplyModifiedPropertiesWithoutUndo();
            var missingNote = missing.Count == 0 ? string.Empty : $", MISSING fields: {string.Join(", ", missing)}";
            return $"{copied}/{VisibilityLookFields.Length} fields copied{missingNote}";
        }

        private static AtlasTilePresentationView FindPresentationView(Scene scene)
        {
            return scene.GetRootGameObjects()
                .Select(go => go.GetComponentInChildren<AtlasTilePresentationView>(true))
                .FirstOrDefault(view => view != null);
        }

        private static Transform FindByName(Transform root, string name)
        {
            if (root.name == name) return root;
            for (var i = 0; i < root.childCount; i++)
            {
                var hit = FindByName(root.GetChild(i), name);
                if (hit != null) return hit;
            }

            return null;
        }

        /// <summary>Snapshot of the transferable environment RenderSettings, captured from the active scene.</summary>
        private readonly struct EnvSettings
        {
            private readonly AmbientMode ambientMode;
            private readonly Color ambientSky;
            private readonly Color ambientEquator;
            private readonly Color ambientGround;
            private readonly Color ambientLight;
            private readonly float ambientIntensity;
            private readonly bool fog;
            private readonly Color fogColor;
            private readonly FogMode fogMode;
            private readonly float fogDensity;
            private readonly float fogStart;
            private readonly float fogEnd;
            private readonly Material skybox;
            private readonly float reflectionIntensity;
            private readonly Color subtractiveShadow;

            private EnvSettings(bool _)
            {
                ambientMode = RenderSettings.ambientMode;
                ambientSky = RenderSettings.ambientSkyColor;
                ambientEquator = RenderSettings.ambientEquatorColor;
                ambientGround = RenderSettings.ambientGroundColor;
                ambientLight = RenderSettings.ambientLight;
                ambientIntensity = RenderSettings.ambientIntensity;
                fog = RenderSettings.fog;
                fogColor = RenderSettings.fogColor;
                fogMode = RenderSettings.fogMode;
                fogDensity = RenderSettings.fogDensity;
                fogStart = RenderSettings.fogStartDistance;
                fogEnd = RenderSettings.fogEndDistance;
                skybox = RenderSettings.skybox;
                reflectionIntensity = RenderSettings.reflectionIntensity;
                subtractiveShadow = RenderSettings.subtractiveShadowColor;
            }

            public static EnvSettings Capture() => new EnvSettings(true);

            public void Apply()
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientSkyColor = ambientSky;
                RenderSettings.ambientEquatorColor = ambientEquator;
                RenderSettings.ambientGroundColor = ambientGround;
                RenderSettings.ambientLight = ambientLight;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.fog = fog;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogMode = fogMode;
                RenderSettings.fogDensity = fogDensity;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
                RenderSettings.skybox = skybox;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                RenderSettings.subtractiveShadowColor = subtractiveShadow;
            }
        }

        /// <summary>
        /// Look-relevant camera configuration captured from the lookdev scene. The two scenes share a
        /// two-camera composite (a post-processed main camera over a non-post-processed skybox camera),
        /// and only "how it renders" transfers: the game's main-camera *pose* is gameplay-driven
        /// (follow/offset), so its transform is never touched, and nearClipPlane stays per-scene
        /// (lookdev uses 0.1 for close-up material inspection). farClipPlane *is* synced — the map
        /// visibly clips at low camera angles below ~600 and that value has drifted in the game scene
        /// before. The skybox camera renders nothing but sky (culling 0), so its rotation is pure art
        /// framing and transfers too.
        /// </summary>
        private readonly struct CameraLookSettings
        {
            private readonly bool hasMain;
            private readonly float mainFov;
            private readonly CameraClearFlags mainClearFlags;
            private readonly Color mainBackground;
            private readonly float mainFar;
            private readonly bool mainRenderPost;

            private readonly bool hasSkybox;
            private readonly float skyFov;
            private readonly CameraClearFlags skyClearFlags;
            private readonly Color skyBackground;
            private readonly float skyDepth;
            private readonly int skyCullingMask;
            private readonly float skyNear;
            private readonly float skyFar;
            private readonly bool skyRenderPost;
            private readonly Quaternion skyRotation;

            private CameraLookSettings(Camera main, Camera skybox)
            {
                hasMain = main != null;
                mainFov = hasMain ? main.fieldOfView : 0f;
                mainClearFlags = hasMain ? main.clearFlags : CameraClearFlags.Skybox;
                mainBackground = hasMain ? main.backgroundColor : Color.black;
                mainFar = hasMain ? main.farClipPlane : 0f;
                mainRenderPost = hasMain && main.GetUniversalAdditionalCameraData().renderPostProcessing;

                hasSkybox = skybox != null;
                skyFov = hasSkybox ? skybox.fieldOfView : 0f;
                skyClearFlags = hasSkybox ? skybox.clearFlags : CameraClearFlags.Skybox;
                skyBackground = hasSkybox ? skybox.backgroundColor : Color.black;
                skyDepth = hasSkybox ? skybox.depth : 0f;
                skyCullingMask = hasSkybox ? skybox.cullingMask : 0;
                skyNear = hasSkybox ? skybox.nearClipPlane : 0.3f;
                skyFar = hasSkybox ? skybox.farClipPlane : 1000f;
                skyRenderPost = hasSkybox && skybox.GetUniversalAdditionalCameraData().renderPostProcessing;
                skyRotation = hasSkybox ? skybox.transform.rotation : Quaternion.identity;
            }

            public static CameraLookSettings Capture(Scene lookdevScene)
            {
                return new CameraLookSettings(
                    FindCamera(lookdevScene, MainCameraName),
                    FindCamera(lookdevScene, SkyboxCameraName));
            }

            public string ApplyTo(Scene gameScene)
            {
                var notes = new List<string>();
                var gameMain = FindCamera(gameScene, MainCameraName);
                var gameSkybox = FindCamera(gameScene, SkyboxCameraName);

                if (!hasMain)
                {
                    notes.Add($"lookdev '{MainCameraName}' not found — main camera untouched");
                }
                else if (gameMain == null)
                {
                    notes.Add($"game '{MainCameraName}' not found — skipped");
                }
                else
                {
                    gameMain.fieldOfView = mainFov;
                    gameMain.clearFlags = mainClearFlags;
                    gameMain.backgroundColor = mainBackground;
                    gameMain.farClipPlane = mainFar;
                    gameMain.GetUniversalAdditionalCameraData().renderPostProcessing = mainRenderPost;
                    notes.Add($"main FOV {mainFov:0.#}/clear {mainClearFlags}/far {mainFar:0.#}/post {(mainRenderPost ? "on" : "off")}");
                }

                if (!hasSkybox)
                {
                    notes.Add($"lookdev '{SkyboxCameraName}' not found — skybox camera untouched");
                }
                else
                {
                    if (gameSkybox == null)
                    {
                        var skyboxObject = new GameObject(SkyboxCameraName);
                        SceneManager.MoveGameObjectToScene(skyboxObject, gameScene);
                        gameSkybox = skyboxObject.AddComponent<Camera>();
                        notes.Add("skybox camera created");
                    }

                    gameSkybox.transform.rotation = skyRotation;
                    gameSkybox.fieldOfView = skyFov;
                    gameSkybox.clearFlags = skyClearFlags;
                    gameSkybox.backgroundColor = skyBackground;
                    gameSkybox.depth = skyDepth;
                    gameSkybox.cullingMask = skyCullingMask;
                    gameSkybox.nearClipPlane = skyNear;
                    gameSkybox.farClipPlane = skyFar;
                    gameSkybox.GetUniversalAdditionalCameraData().renderPostProcessing = skyRenderPost;
                    notes.Add($"skybox FOV {skyFov:0.#}/depth {skyDepth:0.#}/post {(skyRenderPost ? "on" : "off")}");
                }

                // URP camera stacking: Skybox = Base, Main = Overlay on its stack. Two Base cameras
                // compositing through clear=Nothing is NOT guaranteed by URP — the main camera's
                // intermediate target starts uninitialized in play mode (shows as a garbage solid
                // color), so the stack relationship is enforced on every sync.
                if (gameMain != null && gameSkybox != null)
                {
                    var mainData = gameMain.GetUniversalAdditionalCameraData();
                    mainData.renderType = CameraRenderType.Overlay;
                    var stack = gameSkybox.GetUniversalAdditionalCameraData().cameraStack;
                    if (!stack.Contains(gameMain))
                    {
                        stack.Add(gameMain);
                    }

                    notes.Add("stack ok (skybox base + main overlay)");
                }

                return string.Join("; ", notes);
            }

            private static Camera FindCamera(Scene scene, string name)
            {
                return scene.GetRootGameObjects()
                    .Select(go => FindByName(go.transform, name))
                    .Where(t => t != null)
                    .Select(t => t.GetComponent<Camera>())
                    .FirstOrDefault(c => c != null);
            }
        }
    }
}
#endif
