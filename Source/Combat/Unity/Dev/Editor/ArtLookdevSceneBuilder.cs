#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Procedurally builds the ArtLookdev scene: a designer-facing lookdev rig that puts the
    /// real game assets (tile catalog, monsters) under one consistent lighting setup with
    /// calibration references, visibility-fog previews and a live perf HUD. The scene is a
    /// throwaway stage — confirmed values migrate back to game assets (see ArtLookdev_README.md).
    /// Re-run the menu item any time; the scene and its support assets are regenerated in place.
    /// </summary>
    public static class ArtLookdevSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Dev/ArtLookdev.unity";
        private const string CatalogPath = "Assets/Data/Map/Catalogs/AtlasTileCatalog.asset";
        private const string PlayerPrefabPath = "Assets/Art/Characters/player/Prefabs/Player.prefab";
        private const string RealMapSourcePath = "Assets/Data/Map/Authoring/EastSeoulSource.asset";
        private const int FieldObjectMaxColumns = 6;
        private const float FieldMinCell = 3.5f;
        private const float FieldCellPadding = 2f;
        private const float FieldRowGap = 3f;
        private const string MainGameplaySkyboxGuid = "82b660520bdecce4daf45d270ec93fe4";
        private const string LookdevFolder = "Assets/Art/Lookdev";
        private const string MaterialsFolder = LookdevFolder + "/Materials";
        private const string TexturesFolder = LookdevFolder + "/Textures";
        private const string VolumeProfilePath = LookdevFolder + "/ArtLookdevVolumeProfile.asset";
        private const string NightPresetPath = "Assets/Data/Lighting/LookPresets/SeoulNight.asset";
        private const string LightingSettingsPath = LookdevFolder + "/ArtLookdevLightingSettings.lighting";
        private const float TileSpacing = 2.2f;

        // 실측 기본값과 동일한 암시야 프리뷰 색 (AtlasTilePresentationView.visibilityUnknownColor/HintedColor).
        private static readonly Color FogUnknownColor = new Color(0.02f, 0.025f, 0.03f, 0.78f);
        private static readonly Color FogHintedColor = new Color(0.18f, 0.22f, 0.26f, 0.52f);

        private static readonly string[] MonsterPaths =
        {
            "Assets/Art/Characters/monster/ThreeEyeDog/Prefabs/ThreeEyeDog.prefab",
            "Assets/Art/Characters/monster/Bulgasal/Prefabs/Bulgasal.prefab",
            "Assets/Art/Characters/monster/Bull/Prefabs/Bull.prefab",
            "Assets/Art/Characters/monster/LionMask/Prefabs/LionMask.prefab",
            "Assets/Art/Characters/monster/Pig/Prefabs/Pig.prefab",
            "Assets/Art/Characters/monster/tiger/Prefabs/Tiger.prefab",
            "Assets/Art/Characters/TinyRex/Prefabs/EnemyTinyRex.prefab"
        };

        [MenuItem("Seoul Playup/Dev/Create Art Lookdev Scene")]
        public static void CreateOrUpdateScene()
        {
            EnsureFolders();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "ArtLookdev";

            var ground = CreateGround();
            CreateLightingRig(out var keyLight);
            ConfigureEnvironment(keyLight);
            var calibrationRoot = CreateCalibrationGroup();
            var paletteRoot = CreateTerrainPaletteGroup(out var showcaseEntry);
            var visibilityRoot = CreateVisibilityPreviewGroup(showcaseEntry);
            var monstersRoot = CreateMonstersGroup();
            var fieldObjectsRoot = CreateFieldObjectsGroup();
            var miniSceneRoot = CreateMiniSceneGroup();
            var realMapRoot = CreateRealMapGroup();
            CreatePostFxGroup();
            CreatePerfHudGroup();
            CreateCameraWithBookmarks(
                calibrationRoot, paletteRoot, visibilityRoot, monstersRoot, fieldObjectsRoot, miniSceneRoot, realMapRoot);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"Created Art Lookdev scene at {ScenePath} (ground={ground.name})");
        }

        private static void EnsureFolders()
        {
            if (!AssetDatabase.IsValidFolder(LookdevFolder))
            {
                AssetDatabase.CreateFolder("Assets/Art", "Lookdev");
            }

            if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            {
                AssetDatabase.CreateFolder(LookdevFolder, "Materials");
            }

            if (!AssetDatabase.IsValidFolder(TexturesFolder))
            {
                AssetDatabase.CreateFolder(LookdevFolder, "Textures");
            }
        }

        // ── 환경/조명 ────────────────────────────────────────────────────────────

        // ── 야경 리그 값 (딥 인디고 베이스 + 쿨 문라이트 — 2부 T2 확정) ──────────────
        // 야경 ≠ 웜 플러드: 전역 웜 방향광을 깔면 "해질녘 노을"이 됨(실패했던 방향).
        // 진짜 야경 = 어둡고 차가운 딥 인디고 앰비언트 + 약한 쿨 문라이트 하나.
        // 온기·색은 국소 emissive/point(창문·간판 등, 별도 트랙)에서만 나온다.
        // 여기가 "야경 톤"의 정본. 값 확정 시 MainGameplay 씬 라이트/환경으로 이관.
        private static readonly Color CoolMoonColor = new Color(0.6824f, 0.7686f, 0.9098f);   // #AEC4E8
        private static readonly Color NightAmbientSky = new Color(0.0549f, 0.0824f, 0.1490f); // #0E1526
        private static readonly Color NightAmbientEquator = new Color(0.1059f, 0.1412f, 0.2196f); // #1B2438
        private static readonly Color NightAmbientGround = new Color(0.0314f, 0.0431f, 0.0784f); // #080B14
        private const float NightAmbientIntensity = 0.8f;

        private static GameObject CreateLightingRig(out Light keyLight)
        {
            var root = new GameObject("── LIGHTING RIG ──");

            // 조명 정본 = 룩 프리셋의 리그 프리팹. 룩덱 씬은 그 프리팹의 *인스턴스*를 띄운다 —
            // 씬 로컬 복제본을 만들면 여기서 튜닝한 조명이 프리팹/프리셋으로 돌아갈 길이 없다.
            // 앵커를 따로 두는 이유: 프리셋 교체는 앵커의 자식을 비우고 다시 심으므로, StudioRig가
            // 형제로 있으면 같이 지워진다.
            var nightRig = new GameObject(LookPresetApplier.PresetRigAnchorName);
            nightRig.transform.SetParent(root.transform, false);
            keyLight = InstantiatePresetRig(nightRig.transform);

            var studioRig = new GameObject("StudioRig");
            studioRig.transform.SetParent(root.transform, false);
            CreateDirectionalLight(
                "Studio Key",
                studioRig.transform,
                new Vector3(50f, -30f, 0f),
                Color.white,
                1.1f,
                castShadows: true);
            CreateDirectionalLight(
                "Studio Fill",
                studioRig.transform,
                new Vector3(30f, 140f, 0f),
                Color.white,
                0.35f,
                castShadows: false);
            studioRig.SetActive(false);

            var toggle = root.AddComponent<ArtLookdevStudioToggle>();
            var serialized = new SerializedObject(toggle);
            serialized.FindProperty("nightRigRoot").objectReferenceValue = nightRig;
            serialized.FindProperty("studioRigRoot").objectReferenceValue = studioRig;
            serialized.FindProperty("nightAmbientSky").colorValue = NightAmbientSky;
            serialized.FindProperty("nightAmbientEquator").colorValue = NightAmbientEquator;
            serialized.FindProperty("nightAmbientGround").colorValue = NightAmbientGround;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            return root;
        }

        /// <summary>
        /// Instantiates the night preset's rig prefab under <paramref name="anchor"/> and returns the light
        /// that should drive <c>RenderSettings.sun</c>. Falls back to the hand-built moon rig if the preset
        /// asset is missing, so the builder still produces a lit scene on a fresh clone.
        /// </summary>
        private static Light InstantiatePresetRig(Transform anchor)
        {
            var preset = AssetDatabase.LoadAssetAtPath<EnvironmentLookPreset>(NightPresetPath);
            if (preset != null && preset.LightRigPrefab != null)
            {
                LookPresetApplier.ApplyLightRig(preset, anchor);
                var brightest = anchor.GetComponentsInChildren<Light>(includeInactive: true)
                    .Where(l => l.type == LightType.Directional)
                    .OrderByDescending(l => l.intensity)
                    .FirstOrDefault();
                if (brightest != null)
                {
                    return brightest;
                }

                Debug.LogWarning($"[ArtLookdevSceneBuilder] '{NightPresetPath}' rig has no directional light — using the fallback moon rig.");
            }
            else
            {
                Debug.LogWarning($"[ArtLookdevSceneBuilder] Could not load a rig prefab from '{NightPresetPath}' — using the fallback moon rig.");
            }

            // 메인 방향광 = 쿨 문라이트 하나만. 딥 인디고 앰비언트 위에 약하고 차가운 면광.
            // 웜 키·시안 필·마젠타 림은 전부 제거(전역 웜광 = 노을처럼 보여 실패했던 방향).
            var fallback = CreateDirectionalLight(
                "Moon (Cool)",
                anchor,
                new Vector3(42f, -150f, 0f),
                CoolMoonColor,
                0.28f,
                castShadows: true);
            fallback.shadowStrength = 0.4f;
            return fallback;
        }

        private static Light CreateDirectionalLight(
            string name, Transform parent, Vector3 euler, Color color, float intensity, bool castShadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.intensity = intensity;
            light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            return light;
        }

        private static void ConfigureEnvironment(Light keyLight)
        {
            var skyboxPath = AssetDatabase.GUIDToAssetPath(MainGameplaySkyboxGuid);
            var skybox = string.IsNullOrEmpty(skyboxPath) ? null : AssetDatabase.LoadAssetAtPath<Material>(skyboxPath);
            if (skybox != null)
            {
                RenderSettings.skybox = skybox;
            }

            RenderSettings.sun = keyLight;
            RenderSettings.fog = false;
            // 딥 인디고 트릴라이트 앰비언트(야경 톤). 스튜디오 토글이 night/studio를 전환하므로
            // 여기 초기값은 ArtLookdevStudioToggle의 night 값과 일치시킨다.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = NightAmbientSky;
            RenderSettings.ambientEquatorColor = NightAmbientEquator;
            RenderSettings.ambientGroundColor = NightAmbientGround;
            RenderSettings.ambientIntensity = NightAmbientIntensity;

            var lightingSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingSettingsPath);
            if (lightingSettings == null)
            {
                lightingSettings = new LightingSettings
                {
                    name = "ArtLookdevLightingSettings",
                    bakedGI = false,
                    realtimeGI = false
                };
                AssetDatabase.CreateAsset(lightingSettings, LightingSettingsPath);
            }

            Lightmapping.lightingSettings = lightingSettings;
        }

        private static GameObject CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Lookdev Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            var material = LoadOrCreateLitMaterial("ArtLookdev_Ground", new Color(0.10f, 0.11f, 0.13f), 0f, 0.15f);
            ground.GetComponent<Renderer>().sharedMaterial = material;
            return ground;
        }

        // ── CALIBRATION ─────────────────────────────────────────────────────────

        private static GameObject CreateCalibrationGroup()
        {
            var root = new GameObject("── CALIBRATION ──");
            root.transform.position = new Vector3(0f, 0f, 10f);

            var grey = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            grey.name = "GreySphere18";
            grey.transform.SetParent(root.transform, false);
            grey.transform.localPosition = new Vector3(-2f, 0.5f, 0f);
            grey.GetComponent<Renderer>().sharedMaterial =
                LoadOrCreateLitMaterial("ArtLookdev_Grey18", new Color(0.4663f, 0.4663f, 0.4663f), 0f, 0.25f);

            var chrome = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            chrome.name = "ChromeSphere";
            chrome.transform.SetParent(root.transform, false);
            chrome.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            chrome.GetComponent<Renderer>().sharedMaterial =
                LoadOrCreateLitMaterial("ArtLookdev_Chrome", new Color(0.9f, 0.9f, 0.9f), 1f, 0.95f);

            var checker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            checker.name = "ColorCheckerCard";
            checker.transform.SetParent(root.transform, false);
            checker.transform.localPosition = new Vector3(2.4f, 0.8f, 0f);
            checker.transform.localScale = new Vector3(2.25f, 1.5f, 1f);
            checker.GetComponent<Renderer>().sharedMaterial = LoadOrCreateColorCheckerMaterial();

            CreateLabel("CALIBRATION — GreySphere 18% / Chrome / ColorChecker", new Vector3(0f, 2.2f, 0f), root.transform);
            return root;
        }

        // ── TERRAIN PALETTE ─────────────────────────────────────────────────────

        private static GameObject CreateTerrainPaletteGroup(out AtlasTileCatalog.Entry showcaseEntry)
        {
            showcaseEntry = null;
            var root = new GameObject("── TERRAIN PALETTE ──");
            root.transform.position = new Vector3(-26f, 0f, 2f);

            var catalog = AssetDatabase.LoadAssetAtPath<AtlasTileCatalog>(CatalogPath);
            if (catalog == null)
            {
                Debug.LogWarning($"AtlasTileCatalog not found at {CatalogPath}; terrain palette left empty.");
                return root;
            }

            // 같은 top 프리팹을 공유하는 엔트리(회전 변형 등)는 하나만 전시한다.
            var groups = catalog.Entries
                .Where(entry => entry != null && entry.TopPrefab != null)
                .GroupBy(entry => entry.TopPrefab)
                .Select(byPrefab => byPrefab.First())
                .GroupBy(entry => ResolveTerrainPrefix(entry.TopPrefab.name))
                .OrderBy(group => group.Key, System.StringComparer.Ordinal)
                .ToList();

            var row = 0;
            foreach (var group in groups)
            {
                var rowRoot = new GameObject($"Row {group.Key}");
                rowRoot.transform.SetParent(root.transform, false);
                rowRoot.transform.localPosition = new Vector3(0f, 0f, -row * TileSpacing);
                CreateLabel(group.Key, new Vector3(-TileSpacing, 0.9f, 0f), rowRoot.transform, 1.4f);

                var column = 0;
                foreach (var entry in group.OrderBy(item => item.TopPrefab.name, System.StringComparer.Ordinal))
                {
                    var instance = InstantiateTilePrefab(entry.TopPrefab, rowRoot.transform, new Vector3(column * TileSpacing, 0f, 0f));
                    CreateLabel(entry.TopPrefab.name, new Vector3(column * TileSpacing, -0.1f, -1.3f), rowRoot.transform, 0.7f);
                    if (showcaseEntry == null && instance != null && entry.TopPrefab.name.Contains("hanriver"))
                    {
                        showcaseEntry = entry;
                    }

                    column++;
                }

                row++;
            }

            if (showcaseEntry == null)
            {
                showcaseEntry = catalog.Entries.FirstOrDefault(entry => entry != null && entry.TopPrefab != null);
            }

            CreateLabel("TERRAIN PALETTE (실제 AtlasTileCatalog — 여기서의 머티리얼 튜닝이 곧 게임 반영)",
                new Vector3(6f, 2.4f, TileSpacing * 1.2f), root.transform, 1.2f);
            return root;
        }

        private static string ResolveTerrainPrefix(string prefabName)
        {
            var end = 0;
            while (end < prefabName.Length && char.IsLetter(prefabName[end]))
            {
                end++;
            }

            return end == 0 ? "etc" : prefabName.Substring(0, end);
        }

        private static GameObject InstantiateTilePrefab(GameObject prefab, Transform parent, Vector3 localPosition)
        {
            if (PrefabUtility.InstantiatePrefab(prefab, parent) is not GameObject instance)
            {
                return null;
            }

            instance.transform.localPosition = localPosition;
            instance.transform.localRotation = Quaternion.identity;
            return instance;
        }

        private static Bounds? MeasureWorldBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return null;
            }

            var bounds = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }

        // 프리팹의 피벗이 치우쳐 있어도 렌더 바운드 중심을 셀 중심 XZ에 맞추고 바닥(y=groundWorldY)에 앉힌다.
        private static float GroundAndCenter(GameObject instance, Vector3 cellCenterWorldXZ, float groundWorldY)
        {
            var measured = MeasureWorldBounds(instance);
            if (measured == null)
            {
                instance.transform.position = new Vector3(cellCenterWorldXZ.x, groundWorldY, cellCenterWorldXZ.z);
                return 1f;
            }

            var bounds = measured.Value;
            instance.transform.position += new Vector3(
                cellCenterWorldXZ.x - bounds.center.x,
                groundWorldY - bounds.min.y,
                cellCenterWorldXZ.z - bounds.center.z);
            return Mathf.Max(bounds.size.x, bounds.size.z);
        }

        // ── VISIBILITY PREVIEW ──────────────────────────────────────────────────

        private static GameObject CreateVisibilityPreviewGroup(AtlasTileCatalog.Entry showcaseEntry)
        {
            var root = new GameObject("── VISIBILITY PREVIEW ──");
            root.transform.position = new Vector3(0f, 0f, 2f);

            if (showcaseEntry == null || showcaseEntry.TopPrefab == null)
            {
                Debug.LogWarning("No showcase tile available; visibility preview left empty.");
                return root;
            }

            CreateVisibilityColumn(root.transform, showcaseEntry.TopPrefab, "Revealed", new Vector3(-3f, 0f, 0f), null);
            CreateVisibilityColumn(root.transform, showcaseEntry.TopPrefab, "Hinted", new Vector3(0f, 0f, 0f),
                LoadOrCreateFogMaterial("ArtLookdev_FogHinted", FogHintedColor));
            CreateVisibilityColumn(root.transform, showcaseEntry.TopPrefab, "Hidden", new Vector3(3f, 0f, 0f),
                LoadOrCreateFogMaterial("ArtLookdev_FogUnknown", FogUnknownColor));

            CreateLabel("VISIBILITY PREVIEW — 게임과 같은 오버레이 방식(청크 경로), 색 기본값 동일",
                new Vector3(0f, 2.4f, 0f), root.transform);
            return root;
        }

        private static void CreateVisibilityColumn(
            Transform parent, GameObject tilePrefab, string stateName, Vector3 localPosition, Material fogMaterial)
        {
            var column = new GameObject($"State {stateName}");
            column.transform.SetParent(parent, false);
            column.transform.localPosition = localPosition;

            var tile = InstantiateTilePrefab(tilePrefab, column.transform, Vector3.zero);
            var topY = tile != null ? ResolveTopWorldY(tile) - column.transform.position.y : 0.3f;

            if (fogMaterial != null)
            {
                // 게임(AtlasTilePresentationView.CreateVisibilityOverlay)과 같은 실린더 오버레이.
                var overlay = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                overlay.name = "Visibility Overlay";
                overlay.transform.SetParent(column.transform, false);
                overlay.transform.localPosition = new Vector3(0f, topY + 0.03f, 0f);
                overlay.transform.localScale = new Vector3(0.82f, 0.01f, 0.82f);
                var collider = overlay.GetComponent<Collider>();
                if (collider != null)
                {
                    Object.DestroyImmediate(collider);
                }

                var renderer = overlay.GetComponent<Renderer>();
                renderer.sharedMaterial = fogMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            CreateLabel(stateName, new Vector3(0f, -0.1f, -1.4f), column.transform, 0.9f);
        }

        private static float ResolveTopWorldY(GameObject instance)
        {
            var maxY = 0.1f;
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>())
            {
                maxY = Mathf.Max(maxY, renderer.bounds.max.y);
            }

            return maxY;
        }

        // ── MONSTERS ────────────────────────────────────────────────────────────

        private static GameObject CreateMonstersGroup()
        {
            var root = new GameObject("── MONSTERS ──");
            root.transform.position = new Vector3(0f, 0f, -6f);

            var column = 0;
            foreach (var path in MonsterPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning($"Monster prefab missing: {path}");
                    continue;
                }

                var slot = new GameObject($"Slot {prefab.name}");
                slot.transform.SetParent(root.transform, false);
                slot.transform.localPosition = new Vector3((column - (MonsterPaths.Length - 1) * 0.5f) * 2.5f, 0f, 0f);
                InstantiateTilePrefab(prefab, slot.transform, Vector3.zero);
                CreateLabel(prefab.name, new Vector3(0f, -0.1f, -1.4f), slot.transform, 0.8f);
                column++;
            }

            CreateLabel("MONSTERS — 격리 확인은 Slot GameObject를 개별 on/off", new Vector3(0f, 2.6f, 1.6f), root.transform);
            return root;
        }

        // ── FIELD OBJECTS ───────────────────────────────────────────────────────

        private static GameObject CreateFieldObjectsGroup()
        {
            var root = new GameObject("── FIELD OBJECTS ──");
            root.transform.position = new Vector3(0f, 0f, -18f);

            var catalogSet = AssetDatabase.LoadAssetAtPath<MapObjectCatalogSet>(MapObjectCatalogSet.DefaultCatalogAssetPath);
            var zCursor = 0f;
            GameObject fadeShowcasePrefab = null;
            if (catalogSet == null)
            {
                Debug.LogWarning($"MapObjectCatalogSet not found at {MapObjectCatalogSet.DefaultCatalogAssetPath}; field objects left empty.");
            }
            else
            {
                foreach (var typedCatalog in catalogSet.Catalogs.OrderBy(catalog => catalog.ObjectType.ToString(), System.StringComparer.Ordinal))
                {
                    var entries = typedCatalog.Entries
                        .Where(entry => entry != null && entry.Prefab != null)
                        .GroupBy(entry => entry.Prefab)
                        .Select(byPrefab => byPrefab.First())
                        .OrderBy(entry => entry.ObjectRef, System.StringComparer.Ordinal)
                        .ToList();
                    if (entries.Count == 0)
                    {
                        continue;
                    }

                    var items = entries.Select(entry => (entry.ObjectRef, entry.Prefab)).ToList();
                    zCursor = LayoutFieldRow(root.transform, typedCatalog.ObjectType.ToString(), items, zCursor);
                    if (fadeShowcasePrefab == null && typedCatalog.ObjectType == HexMapObjectType.Building)
                    {
                        fadeShowcasePrefab = entries[0].Prefab;
                    }
                }

                if (fadeShowcasePrefab == null)
                {
                    fadeShowcasePrefab = catalogSet.Entries.FirstOrDefault(entry => entry != null && entry.Prefab != null)?.Prefab;
                }
            }

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab != null)
            {
                zCursor = LayoutFieldRow(root.transform, "Player",
                    new System.Collections.Generic.List<(string, GameObject)> { ("Player", playerPrefab) }, zCursor);
            }

            if (fadeShowcasePrefab != null)
            {
                zCursor = LayoutFadePreviewRow(root.transform, fadeShowcasePrefab, zCursor);
            }

            CreateLabel("FIELD OBJECTS — 설치형 오브젝트 전체(MapObjectCatalogSet) + Player",
                new Vector3(10f, 3.4f, FieldRowGap), root.transform, 1.3f);
            return root;
        }

        // 한 도메인(또는 Player)을 실측 바운드 기반 그리드로 정렬한다: 셀 크기 = 그 행 최대 풋프린트 + 패딩,
        // 각 인스턴스는 GroundAndCenter로 셀 중심에 바닥 정렬(피벗 치우침·크기 편차 보정). zCursor를 전진시켜 반환.
        private static float LayoutFieldRow(
            Transform parent, string header, System.Collections.Generic.List<(string label, GameObject prefab)> items, float zStart)
        {
            var rowRoot = new GameObject($"Row {header}");
            rowRoot.transform.SetParent(parent, false);
            rowRoot.transform.localPosition = new Vector3(0f, 0f, -zStart);

            var instances = new System.Collections.Generic.List<GameObject>(items.Count);
            var maxFootprint = 0f;
            foreach (var item in items)
            {
                var instance = InstantiateTilePrefab(item.prefab, rowRoot.transform, new Vector3(0f, -1000f, 0f));
                instances.Add(instance);
                var bounds = instance != null ? MeasureWorldBounds(instance) : null;
                if (bounds != null)
                {
                    maxFootprint = Mathf.Max(maxFootprint, Mathf.Max(bounds.Value.size.x, bounds.Value.size.z));
                }
            }

            var cell = Mathf.Max(FieldMinCell, maxFootprint + FieldCellPadding);
            CreateLabel(header, new Vector3(-cell * 0.85f, 0.9f, 0f), rowRoot.transform, 1.4f);

            for (var i = 0; i < instances.Count; i++)
            {
                var column = i % FieldObjectMaxColumns;
                var subRow = i / FieldObjectMaxColumns;
                var cellLocal = new Vector3(column * cell, 0f, -subRow * cell);
                if (instances[i] != null)
                {
                    var cellWorld = rowRoot.transform.TransformPoint(cellLocal);
                    GroundAndCenter(instances[i], new Vector3(cellWorld.x, 0f, cellWorld.z), rowRoot.transform.position.y);
                }

                CreateLabel(items[i].label, cellLocal + new Vector3(0f, -0.1f, -cell * 0.42f), rowRoot.transform, 0.7f);
            }

            var subRowCount = (Mathf.Max(1, instances.Count) + FieldObjectMaxColumns - 1) / FieldObjectMaxColumns;
            return zStart + subRowCount * cell + FieldRowGap;
        }

        private static float LayoutFadePreviewRow(Transform parent, GameObject prefab, float zStart)
        {
            var rowRoot = new GameObject("Row FadePreview");
            rowRoot.transform.SetParent(parent, false);
            rowRoot.transform.localPosition = new Vector3(0f, 0f, -zStart);

            var modes = new (string label, ArtLookdevFadeStatePreview.FadeStateMode mode)[]
            {
                ("Normal", ArtLookdevFadeStatePreview.FadeStateMode.Normal),
                ("Darkened", ArtLookdevFadeStatePreview.FadeStateMode.VisibilityDarkened),
                ("Faded (플레이 중)", ArtLookdevFadeStatePreview.FadeStateMode.OcclusionFaded)
            };

            // 세 열이 같은 프리팹이므로 풋프린트를 한 번만 재서 셀 크기를 정한다.
            var cell = FieldMinCell;
            var probe = InstantiateTilePrefab(prefab, rowRoot.transform, new Vector3(0f, -1000f, 0f));
            if (probe != null)
            {
                var bounds = MeasureWorldBounds(probe);
                if (bounds != null)
                {
                    cell = Mathf.Max(cell, Mathf.Max(bounds.Value.size.x, bounds.Value.size.z) + FieldCellPadding);
                }

                Object.DestroyImmediate(probe);
            }

            CreateLabel("FADE PREVIEW — 맵 오브젝트 암시야/가림 상태", new Vector3(-cell * 0.85f, 0.9f, 0f), rowRoot.transform, 1f);

            for (var i = 0; i < modes.Length; i++)
            {
                var column = new GameObject($"Fade {modes[i].mode}");
                column.transform.SetParent(rowRoot.transform, false);
                var cellLocal = new Vector3(i * cell, 0f, 0f);
                var instance = InstantiateTilePrefab(prefab, column.transform, Vector3.zero);
                if (instance != null)
                {
                    var cellWorld = rowRoot.transform.TransformPoint(cellLocal);
                    GroundAndCenter(instance, new Vector3(cellWorld.x, 0f, cellWorld.z), rowRoot.transform.position.y);

                    // 게임에선 MapObjectVisualController가 런타임에 붙이는 컴포넌트 — 프리뷰도 같은 타깃을 쓴다.
                    var fadeTarget = instance.AddComponent<MapObjectFadeTarget>();
                    var preview = column.AddComponent<ArtLookdevFadeStatePreview>();
                    preview.Configure(fadeTarget, modes[i].mode);
                }

                CreateLabel(modes[i].label, cellLocal + new Vector3(0f, -0.1f, -cell * 0.42f), rowRoot.transform, 0.8f);
            }

            return zStart + cell + FieldRowGap;
        }

        // ── REAL MAP ────────────────────────────────────────────────────────────

        private static GameObject CreateRealMapGroup()
        {
            var root = new GameObject("── REAL MAP ──");
            root.transform.position = new Vector3(70f, 0f, 0f);

            var viewObject = new GameObject("RealMapView");
            viewObject.transform.SetParent(root.transform, false);
            var view = viewObject.AddComponent<AtlasTilePresentationView>();
            var serializedView = new SerializedObject(view);
            serializedView.FindProperty("catalog").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AtlasTileCatalog>(CatalogPath);
            serializedView.FindProperty("mapObjectCatalogSet").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<MapObjectCatalogSet>(MapObjectCatalogSet.DefaultCatalogAssetPath);
            serializedView.FindProperty("visibilityLightingShader").objectReferenceValue =
                Shader.Find("SeoulPlayup/Map/Visibility Lit");
            serializedView.FindProperty("visibilityUnknownLighting").floatValue = 0.10f;
            serializedView.FindProperty("visibilityHintedLighting").floatValue = 0.55f;
            serializedView.FindProperty("visibilityRevealedLighting").floatValue = 1f;
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            var controller = root.AddComponent<ArtLookdevRealMapController>();
            var serializedController = new SerializedObject(controller);
            serializedController.FindProperty("view").objectReferenceValue = view;
            serializedController.FindProperty("mapSource").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(RealMapSourcePath);
            serializedController.FindProperty("playerPrefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            CreateLabel("REAL MAP — 플레이 모드 진입 시 실제 출하 맵(EastSeoul)을 게임 파이프라인으로 렌더\n(화면 우상단 버튼: 암시야 ON/OFF · 플레이어 소환/해제)",
                new Vector3(0f, 3f, 0f), root.transform, 1.4f);
            return root;
        }

        // ── MINI SCENE ──────────────────────────────────────────────────────────

        private static GameObject CreateMiniSceneGroup()
        {
            var root = new GameObject("── MINI SCENE ──");
            root.transform.position = new Vector3(18f, 0f, 2f);

            var catalog = AssetDatabase.LoadAssetAtPath<AtlasTileCatalog>(CatalogPath);
            if (catalog == null)
            {
                return root;
            }

            var byPrefix = catalog.Entries
                .Where(entry => entry != null && entry.TopPrefab != null)
                .GroupBy(entry => ResolveTerrainPrefix(entry.TopPrefab.name))
                .ToDictionary(group => group.Key, group => group.First());
            var fallback = catalog.Entries.FirstOrDefault(entry => entry != null && entry.TopPrefab != null);
            if (fallback == null)
            {
                return root;
            }

            var projection = new HexAxialProjection(1f);
            for (var q = -2; q <= 2; q++)
            {
                for (var r = -2; r <= 2; r++)
                {
                    if (Mathf.Abs(q + r) > 2)
                    {
                        continue;
                    }

                    var ring = (Mathf.Abs(q) + Mathf.Abs(r) + Mathf.Abs(q + r)) / 2;
                    var entry = ResolveMiniSceneEntry(byPrefix, fallback, ring, q + r);
                    projection.CoordToWorld(new HexCoord(q, r), out var x, out var z);
                    InstantiateTilePrefab(entry.TopPrefab, root.transform, new Vector3(x, 0f, z));
                }
            }

            CreateLabel("MINI SCENE — 대표 지형 조합(실전 맥락 확인)", new Vector3(0f, 2.4f, 4f), root.transform);
            return root;
        }

        private static AtlasTileCatalog.Entry ResolveMiniSceneEntry(
            IReadOnlyDictionary<string, AtlasTileCatalog.Entry> byPrefix,
            AtlasTileCatalog.Entry fallback,
            int ring,
            int parity)
        {
            var preferred = ring switch
            {
                0 => new[] { "w" },
                1 => new[] { "r", "s" },
                _ => parity % 2 == 0 ? new[] { "g", "b" } : new[] { "b", "g" }
            };

            foreach (var prefix in preferred)
            {
                if (byPrefix.TryGetValue(prefix, out var entry))
                {
                    return entry;
                }
            }

            return fallback;
        }

        // ── POST FX / PERF HUD / CAMERA ────────────────────────────────────────

        private static void CreatePostFxGroup()
        {
            var root = new GameObject("── POST FX ──");
            var volume = root.AddComponent<Volume>();
            volume.isGlobal = true;

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(VolumeProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, VolumeProfilePath);

                var bloom = profile.Add<Bloom>(true);
                bloom.intensity.value = 0.8f;
                bloom.threshold.value = 1.1f;

                var tonemapping = profile.Add<Tonemapping>(true);
                tonemapping.mode.value = TonemappingMode.ACES;

                var colorAdjustments = profile.Add<ColorAdjustments>(true);
                colorAdjustments.contrast.value = 10f;
                colorAdjustments.saturation.value = -8f;

                var vignette = profile.Add<Vignette>(true);
                vignette.intensity.value = 0.22f;

                EditorUtility.SetDirty(profile);
                AssetDatabase.SaveAssetIfDirty(profile);
            }

            volume.sharedProfile = profile;
        }

        private static void CreatePerfHudGroup()
        {
            var root = new GameObject("── PERF HUD ──");
            root.AddComponent<ScenePerfHud>();
        }

        private static void CreateCameraWithBookmarks(
            GameObject calibrationRoot,
            GameObject paletteRoot,
            GameObject visibilityRoot,
            GameObject monstersRoot,
            GameObject fieldObjectsRoot,
            GameObject miniSceneRoot,
            GameObject realMapRoot)
        {
            var skyboxCamera = CreateSkyboxCamera();

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            // 게임(MainGameplay)과 동일한 카메라 리그 재현: Skybox Camera(Base)가 하늘을 깔고 Main(Overlay)이
            // 그 위에 씬을 그린다. URP 정식 카메라 스태킹 — Base 카메라 2개 + clear=Nothing 백버퍼 합성은
            // URP가 보장하지 않아 플레이 모드에서 미초기화 버퍼(노란 화면)가 노출된다(2026-07-21 실측).
            camera.clearFlags = CameraClearFlags.Nothing;
            camera.fieldOfView = 50f;
            camera.nearClipPlane = 0.1f;
            var mainCameraData = camera.GetUniversalAdditionalCameraData();
            mainCameraData.renderPostProcessing = true;
            mainCameraData.renderType = CameraRenderType.Overlay;
            skyboxCamera.GetUniversalAdditionalCameraData().cameraStack.Add(camera);
            // 맵 대각선 ~190유닛 + 카메라 후퇴·저각도 여유를 감안해 far를 넉넉히 잡는다(원본 게임과 동일 기준).
            camera.farClipPlane = 600f;
            cameraObject.AddComponent<AudioListener>();

            var bookmarks = new[]
            {
                new ArtLookdevCameraBookmarks.Bookmark
                {
                    label = "Overview",
                    position = new Vector3(0f, 24f, -22f),
                    eulerAngles = new Vector3(45f, 0f, 0f)
                },
                new ArtLookdevCameraBookmarks.Bookmark
                {
                    label = "Calibration",
                    position = calibrationRoot.transform.position + new Vector3(0f, 2.6f, -6f),
                    eulerAngles = new Vector3(18f, 0f, 0f)
                },
                new ArtLookdevCameraBookmarks.Bookmark
                {
                    label = "Terrain Palette",
                    position = paletteRoot.transform.position + new Vector3(8f, 12f, -10f),
                    eulerAngles = new Vector3(50f, 0f, 0f)
                },
                new ArtLookdevCameraBookmarks.Bookmark
                {
                    label = "Visibility + Monsters",
                    position = visibilityRoot.transform.position + new Vector3(0f, 7f, -10f),
                    eulerAngles = new Vector3(35f, 0f, 0f)
                },
                new ArtLookdevCameraBookmarks.Bookmark
                {
                    label = "Mini Scene",
                    position = miniSceneRoot.transform.position + new Vector3(0f, 9f, -9f),
                    eulerAngles = new Vector3(45f, 0f, 0f)
                },
                new ArtLookdevCameraBookmarks.Bookmark
                {
                    label = "Field Objects",
                    position = fieldObjectsRoot.transform.position + new Vector3(8f, 14f, -12f),
                    eulerAngles = new Vector3(48f, 0f, 0f)
                },
                new ArtLookdevCameraBookmarks.Bookmark
                {
                    label = "Real Map (플레이 중)",
                    position = realMapRoot.transform.position + new Vector3(0f, 30f, -28f),
                    eulerAngles = new Vector3(45f, 0f, 0f)
                }
            };

            var bookmarkComponent = cameraObject.AddComponent<ArtLookdevCameraBookmarks>();
            bookmarkComponent.Configure(camera, bookmarks);
            bookmarkComponent.Apply(0);
        }

        // 게임(MainGameplay)의 배경 카메라와 동일 구성. 스카이박스만 그리므로(culling 0) 위치는 렌더와
        // 무관하고 회전만 하늘 프레이밍을 정한다 — 게임 배치값을 그대로 쓴다. Main 카메라가 이 카메라의
        // 스택에 Overlay로 얹힌다(호출부 참조).
        private static Camera CreateSkyboxCamera()
        {
            var skyboxObject = new GameObject("Skybox Camera");
            skyboxObject.transform.SetPositionAndRotation(
                new Vector3(-5.22f, 8.98f, -21.42f),
                Quaternion.Euler(-20f, -30f, 0f));
            var skyboxCamera = skyboxObject.AddComponent<Camera>();
            skyboxCamera.clearFlags = CameraClearFlags.Skybox;
            skyboxCamera.backgroundColor = new Color(0.19215687f, 0.3019608f, 0.4745098f, 0f);
            skyboxCamera.fieldOfView = 60f;
            skyboxCamera.depth = -10f;
            skyboxCamera.cullingMask = 0;
            skyboxCamera.farClipPlane = 1000f;
            skyboxCamera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            return skyboxCamera;
        }

        // ── 지원 에셋 ───────────────────────────────────────────────────────────

        private static Material LoadOrCreateLitMaterial(string name, Color color, float metallic, float smoothness)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", metallic);
                material.SetFloat("_Smoothness", smoothness);
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        private static Material LoadOrCreateFogMaterial(string name, Color color)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = name };
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Surface", 1f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        private static Material LoadOrCreateColorCheckerMaterial()
        {
            var texture = LoadOrCreateColorCheckerTexture();
            var path = $"{MaterialsFolder}/ArtLookdev_ColorChecker.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "ArtLookdev_ColorChecker" };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Texture2D LoadOrCreateColorCheckerTexture()
        {
            var path = $"{TexturesFolder}/ArtLookdev_ColorChecker.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
            {
                return existing;
            }

            // Macbeth ColorChecker classic 24패치 (sRGB).
            var patches = new Color32[]
            {
                new Color32(115, 82, 68, 255), new Color32(194, 150, 130, 255), new Color32(98, 122, 157, 255),
                new Color32(87, 108, 67, 255), new Color32(133, 128, 177, 255), new Color32(103, 189, 170, 255),
                new Color32(214, 126, 44, 255), new Color32(80, 91, 166, 255), new Color32(193, 90, 99, 255),
                new Color32(94, 60, 108, 255), new Color32(157, 188, 64, 255), new Color32(224, 163, 46, 255),
                new Color32(56, 61, 150, 255), new Color32(70, 148, 73, 255), new Color32(175, 54, 60, 255),
                new Color32(231, 199, 31, 255), new Color32(187, 86, 149, 255), new Color32(8, 133, 161, 255),
                new Color32(243, 243, 242, 255), new Color32(200, 200, 200, 255), new Color32(160, 160, 160, 255),
                new Color32(122, 122, 122, 255), new Color32(85, 85, 85, 255), new Color32(52, 52, 52, 255)
            };

            const int patchSize = 60;
            const int border = 6;
            const int columns = 6;
            const int rows = 4;
            var width = columns * patchSize + (columns + 1) * border;
            var height = rows * patchSize + (rows + 1) * border;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(20, 20, 20, 255);
            }

            for (var rowIndex = 0; rowIndex < rows; rowIndex++)
            {
                for (var columnIndex = 0; columnIndex < columns; columnIndex++)
                {
                    var patch = patches[rowIndex * columns + columnIndex];
                    var startX = border + columnIndex * (patchSize + border);
                    // 텍스처 y는 아래에서 위 — 첫 행(dark skin)이 위로 가게 뒤집는다.
                    var startY = border + (rows - 1 - rowIndex) * (patchSize + border);
                    for (var y = startY; y < startY + patchSize; y++)
                    {
                        for (var x = startX; x < startX + patchSize; x++)
                        {
                            pixels[y * width + x] = patch;
                        }
                    }
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.sRGBTexture = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static void CreateLabel(string text, Vector3 localPosition, Transform parent, float scale = 1f)
        {
            var go = new GameObject($"Label {text}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(45f, 0f, 0f);
            go.transform.localScale = Vector3.one * scale;

            var textMesh = go.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.characterSize = 0.12f;
            textMesh.fontSize = 48;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.color = new Color(0.85f, 0.88f, 0.92f);

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            textMesh.font = font;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
        }
    }
}
#endif
