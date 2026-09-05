#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    public static class VfxAnimationLabSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Dev/VfxAnimationLab.unity";
        private const string CatalogPath = "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset";
        private const string PlayerPath = "Assets/Art/Characters/player/Prefabs/Player.prefab";

        private const string MonsterCatalogCsv = CombatCsvPaths.MonsterDirectory + "/monster_catalog.csv";
        private const string VisualPrefabPathColumn = "visualPrefabPath";

        /// <summary>
        /// 카탈로그에 없는 시각 프리팹. 랩에만 있고 몬스터로 출하되지 않는 것만 여기 산다.
        /// </summary>
        private static readonly string[] ExtraVisualPaths =
        {
            "Assets/Art/Characters/TinyRex/Prefabs/EnemyTinyRex.prefab"
        };

        /// <summary>
        /// 랩 패널이 prev/next로 순회할 몬스터 시각 프리팹을 <b>monster_catalog.csv에서 유도</b>한다.
        ///
        /// <para>🔴 예전에는 여기 경로가 손으로 박혀 있었고 <b>실제로 두 번 갈라졌다</b>:
        /// 터렛 3종은 씬에만 손으로 들어가 목록엔 없었고(재생성하면 빠진다), 요괴 6종은 반입 후
        /// 아무도 채우지 않아 랩에서 볼 방법이 없었다(2026-09-03). 카탈로그가 곧 「출하되는 몬스터
        /// 시각」의 정본이므로 그쪽에서 끌어오면 드리프트가 구조적으로 불가능해진다 —
        /// 새 몬스터를 반입하면 카탈로그 한 줄만 채우면 랩에 저절로 들어온다.</para>
        ///
        /// <para>씬은 재생성하지 않아도 <c>Sync VFX Lab Monster List</c> 메뉴로 맞출 수 있고,
        /// 씬과 이 유도가 갈라지면 EditMode 게이트(<c>VfxAnimationLabMonsterListTests</c>)가 문다.</para>
        /// </summary>
        public static IReadOnlyList<string> ResolveMonsterVisualPaths()
        {
            var paths = new List<string>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var path in ReadCatalogVisualPrefabPaths())
            {
                // 저작이 아직 안 붙었거나 에셋이 지워진 행은 조용히 건너뛴다 — 여기서 막을 일이 아니다
                // (그건 카탈로그 무결성 감사의 몫이고, 랩이 안 열리는 쪽이 더 나쁘다).
                if (!seen.Add(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
                {
                    continue;
                }

                paths.Add(path);
            }

            foreach (var path in ExtraVisualPaths)
            {
                if (seen.Add(path) && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                {
                    paths.Add(path);
                }
            }

            return paths;
        }

        private static IEnumerable<string> ReadCatalogVisualPrefabPaths()
        {
            if (!File.Exists(MonsterCatalogCsv))
            {
                Debug.LogWarning($"VFX lab monster list: {MonsterCatalogCsv} not found — falling back to extras only.");
                yield break;
            }

            var lines = File.ReadAllLines(MonsterCatalogCsv);
            if (lines.Length == 0)
            {
                yield break;
            }

            var header = SplitCsvLine(lines[0]);
            var column = System.Array.FindIndex(
                header,
                name => string.Equals(name.Trim().TrimStart('﻿'), VisualPrefabPathColumn, System.StringComparison.Ordinal));
            if (column < 0)
            {
                Debug.LogWarning($"VFX lab monster list: '{VisualPrefabPathColumn}' column missing in {MonsterCatalogCsv}.");
                yield break;
            }

            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var cells = SplitCsvLine(lines[i]);
                if (column >= cells.Length)
                {
                    continue;
                }

                var value = cells[column].Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    yield return value;
                }
            }
        }

        /// <summary>designerNote가 따옴표로 쉼표를 감싸므로 단순 Split으로는 컬럼이 밀린다.</summary>
        private static string[] SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var cell = new System.Text.StringBuilder();
            var quoted = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            cell.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        cell.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else
                {
                    cell.Append(c);
                }
            }

            cells.Add(cell.ToString());
            return cells.ToArray();
        }

        /// <summary>
        /// 씬을 통째로 재생성하지 않고 <c>monsterPrefabs</c> 배열만 카탈로그 유도값으로 맞춘다.
        /// 재생성(<see cref="CreateOrUpdateScene"/>)은 씬을 새로 만들기 때문에 손으로 넣은 다른 저작을
        /// 전부 날린다 — 몬스터 목록만 갱신하고 싶을 때는 반드시 이쪽을 쓸 것.
        /// </summary>
        [MenuItem("Seoul Playup/Dev/Sync VFX Lab Monster List")]
        public static void SyncMonsterListInScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var controller = Object.FindAnyObjectByType<VfxAnimationLabController>();
            if (controller == null)
            {
                Debug.LogError($"VFX lab monster list: no VfxAnimationLabController in {ScenePath}.");
                return;
            }

            var desired = ResolveMonsterVisualPaths();
            var serialized = new SerializedObject(controller);
            var array = serialized.FindProperty("monsterPrefabs");
            array.arraySize = desired.Count;
            for (var i = 0; i < desired.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(desired[i]);
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"VFX lab monster list synced: {desired.Count} visuals from {MonsterCatalogCsv}.");
        }

        [MenuItem("Seoul Playup/Dev/Create VFX Animation Lab Scene")]
        public static void CreateOrUpdateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "VfxAnimationLab";

            CreateCamera();
            CreateLight();
            CreateGround();

            var labRoot = new GameObject("VfxAnimationLab");
            var playerSlot = CreateSlot("PlayerSlot", new Vector3(-1.6f, 0f, 0f), labRoot.transform);
            var monsterSlot = CreateSlot("MonsterSlot", new Vector3(1.6f, 0f, 0f), labRoot.transform);
            var fieldCenter = CreateSlot("FieldCenter", Vector3.zero, labRoot.transform);

            var effectRoot = new GameObject("EffectPresentationController");
            var effectPresentation = effectRoot.AddComponent<EffectPresentationController>();
            var effectRuntimeRoot = new GameObject("Effect Presentation Runtime Root");
            effectRuntimeRoot.transform.SetParent(effectRoot.transform, false);

            var controller = labRoot.AddComponent<VfxAnimationLabController>();
            ConfigureEffectPresentation(effectPresentation, effectRuntimeRoot.transform);
            ConfigureController(controller, playerSlot, monsterSlot, fieldCenter, effectPresentation);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();
            Debug.Log($"Created VFX Animation Lab scene at {ScenePath}");
        }

        private static void CreateCamera()
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 4.4f, -5.6f);
            cameraObject.transform.rotation = Quaternion.Euler(50f, 0f, 0f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 45f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreateLight()
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
        }

        private static void CreateGround()
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Lab Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(0.7f, 1f, 0.7f);
            var renderer = ground.GetComponent<Renderer>();
            if (renderer != null)
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                material.color = new Color(0.18f, 0.2f, 0.22f, 1f);
                renderer.sharedMaterial = material;
            }

            var grid = new GameObject("Ground Grid");
            for (var i = -4; i <= 4; i++)
            {
                CreateLine($"Grid X {i}", new Vector3(i, 0.015f, -4f), new Vector3(i, 0.015f, 4f), grid.transform);
                CreateLine($"Grid Z {i}", new Vector3(-4f, 0.015f, i), new Vector3(4f, 0.015f, i), grid.transform);
            }
        }

        private static void CreateLine(string name, Vector3 start, Vector3 end, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.SetPosition(0, start);
            line.SetPosition(1, end);
            line.widthMultiplier = 0.01f;
            line.useWorldSpace = false;
            line.sharedMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default"))
            {
                color = new Color(0.45f, 0.5f, 0.55f, 0.65f)
            };
        }

        private static Transform CreateSlot(string name, Vector3 position, Transform parent)
        {
            var slot = new GameObject(name);
            slot.transform.SetParent(parent, false);
            slot.transform.position = position;
            return slot.transform;
        }

        private static void ConfigureEffectPresentation(EffectPresentationController effectPresentation, Transform effectRoot)
        {
            var serialized = new SerializedObject(effectPresentation);
            serialized.FindProperty("effectRoot").objectReferenceValue = effectRoot;
            serialized.FindProperty("vfxCatalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(CatalogPath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigureController(
            VfxAnimationLabController controller,
            Transform playerSlot,
            Transform monsterSlot,
            Transform fieldCenter,
            EffectPresentationController effectPresentation)
        {
            var monsters = new List<GameObject>();
            foreach (var path in ResolveMonsterVisualPaths())
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab != null)
                {
                    monsters.Add(prefab);
                }
            }

            var serialized = new SerializedObject(controller);
            serialized.FindProperty("playerSlot").objectReferenceValue = playerSlot;
            serialized.FindProperty("monsterSlot").objectReferenceValue = monsterSlot;
            serialized.FindProperty("fieldCenter").objectReferenceValue = fieldCenter;
            serialized.FindProperty("effectPresentation").objectReferenceValue = effectPresentation;
            serialized.FindProperty("playerPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            serialized.FindProperty("vfxCatalog").objectReferenceValue = AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(CatalogPath);
            serialized.FindProperty("showAnchors").boolValue = true;
            serialized.FindProperty("spawnOnStart").boolValue = true;
            serialized.FindProperty("playerSpawnPosition").vector3Value = playerSlot.position;
            serialized.FindProperty("monsterSpawnPosition").vector3Value = monsterSlot.position;

            var monsterArray = serialized.FindProperty("monsterPrefabs");
            monsterArray.arraySize = monsters.Count;
            for (var i = 0; i < monsters.Count; i++)
            {
                monsterArray.GetArrayElementAtIndex(i).objectReferenceValue = monsters[i];
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
