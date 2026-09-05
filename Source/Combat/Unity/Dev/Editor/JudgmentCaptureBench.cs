#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Codex;
using SeoulPlayup.Flow.Unity;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// 시각 판정용 스크린샷을 <b>에디트 모드에서</b> 뽑는 벤치(2026-08-30 신설).
    ///
    /// <para>🔴🔴 <b>왜 에디트 모드인가</b> — 플레이 모드에 들어가면 MCP 브리지가 죽어서
    /// (2026-08-10·08-30 두 번 재현, 사람이 플레이모드를 꺼야 복귀) 에이전트가 게임뷰를 못 찍는다.
    /// 그래서 "실플레이로 봐 주세요"를 사람에게 넘기는 대신, <b>실제 씬·실제 카메라 스택·실제 출하
    /// 코드</b>를 에디트 모드에서 그대로 태워 화면을 만든다. 목업이 아니다 — 색·레이아웃·스케일을
    /// 전부 출하 코드가 계산한다.</para>
    ///
    /// <para>🔴 <b>씬을 저장하지 않는다.</b> 세 메뉴 모두 만진 것을 finally에서 되돌리지만,
    /// 저장은 어느 경로에서도 하지 않는다(<c>tests-run</c>의 전제가 「모든 씬 저장됨」이라
    /// dirty로 남기면 다음 사람의 테스트가 막힌다). 캡처 후 <c>cm status</c>로 확인할 것.</para>
    ///
    /// 산출물은 <c>&lt;프로젝트&gt;/output/judgment-capture/</c> — ignore.conf가 걷어내는 경로다
    /// (판정이 끝나면 버리는 물건이고, 판정 결과는 문서와 아티팩트에 남는다).
    /// </summary>
    public static class JudgmentCaptureBench
    {
        private const string MapSourcePath = "Assets/Data/Map/Authoring/Stage_1_Source.asset";
        private const string VisibilitySettingsPath = "Assets/Data/Map/VisibilityPresentationSettings.asset";
        private const string DebuffRingPrefabPath = "Assets/Prefabs/Vfx/Combat/Status/StatusVfx_Debuff.prefab";
        private const string BuffRingPrefabPath = "Assets/Prefabs/Vfx/Combat/Status/StatusVfx_Buff.prefab";

        /// <summary>실게임 프레이밍 실측(cs:1195) — Cinemachine followOffset (0,7,−9)에서 나온 값이다.</summary>
        private const float GameCameraDistance = 11.40f;
        private const float GameCameraPitch = 37.9f;
        private const float GameCameraFov = 50f;

        private static string OutputRoot
        {
            get
            {
                var dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "output", "judgment-capture");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        // ── 몬스터 예고 오버레이 ────────────────────────────────────────────────
        [MenuItem("Seoul Playup/Dev/판정 캡처/몬스터 예고 오버레이 4종")]
        public static void CaptureIntentOverlays()
        {
            var view = UnityEngine.Object.FindFirstObjectByType<AtlasTilePresentationView>(FindObjectsInactive.Include);
            var renderer = UnityEngine.Object.FindFirstObjectByType<BatchedMeshCombatOverlayRenderer>(FindObjectsInactive.Include);
            if (view == null || renderer == null)
            {
                Debug.LogError("[판정 캡처] MainGameplay 씬을 먼저 열 것 — AtlasTilePresentationView / BatchedMeshCombatOverlayRenderer를 못 찾았다.");
                return;
            }

            if (!TryPrepareMap(view, out var map))
            {
                return;
            }

            // 오버레이 색을 보려면 안개가 걷혀 있어야 한다
            view.ApplyVisibility(coord =>
            {
                var cell = map.GetCellOrDefault(coord);
                return new HexVisibilitySafeCellInfo(
                    coord, HexCellVisibility.Revealed, true, true, true,
                    cell.TileDefinitionId ?? string.Empty, cell.TerrainTypeId ?? string.Empty,
                    1, true, false, string.Empty, string.Empty);
            });

            renderer.Configure(view);
            renderer.InvalidateGeometry();

            var center = new HexCoord(102, -54);
            var ring1 = Disc(center, 1);
            var ring2 = Disc(center, 2).Where(c => !ring1.Contains(c)).ToList();

            var shots = new (string Id, HexOverlayLayer[] Layers)[]
            {
                ("1-selfbuff", new[] { HexOverlayLayer.MonsterSelfBuffIntent }),
                ("2-attack", new[] { HexOverlayLayer.MonsterAttackIntent }),
                ("3-move", new[] { HexOverlayLayer.MonsterMoveIntent }),
                ("4-summon", new[] { HexOverlayLayer.MonsterSummonIntent }),
                ("5-together", new[] { HexOverlayLayer.MonsterSelfBuffIntent, HexOverlayLayer.MonsterAttackIntent, HexOverlayLayer.MonsterMoveIntent }),
            };

            using (var cam = SceneCameraLease.Acquire())
            {
                if (!cam.Valid)
                {
                    return;
                }

                cam.FrameAt(view.Project(center), GameCameraDistance + 1.6f, 45f, GameCameraFov);
                foreach (var shot in shots)
                {
                    ClearAllLayers(renderer);
                    foreach (var layer in shot.Layers)
                    {
                        var coords = layer == HexOverlayLayer.MonsterAttackIntent ? ring2
                            : layer == HexOverlayLayer.MonsterMoveIntent ? new List<HexCoord> { center }
                            : ring1;
                        // 🔑 색을 손으로 칠하지 않는다 — 출하 정본 테마가 계산한 스타일을 그대로 쓴다.
                        renderer.ShowLayer(layer, coords, CombatOverlayTheme.ResolveDefaultStyle(layer));
                    }

                    cam.Capture(Path.Combine(OutputRoot, "overlay_" + shot.Id + ".png"), 1280, 720);
                }

                ClearAllLayers(renderer);
            }

            Done("몬스터 예고 오버레이");
        }

        // ── 잡화점 진열 ─────────────────────────────────────────────────────────
        [MenuItem("Seoul Playup/Dev/판정 캡처/잡화점 진열")]
        public static void CaptureNightMarket()
        {
            var layersGo = GameObject.Find("Gameplay UI Layers");
            var canvas = layersGo != null ? layersGo.GetComponentInParent<Canvas>() : null;
            if (layersGo == null || canvas == null)
            {
                Debug.LogError("[판정 캡처] MainGameplay 씬을 먼저 열 것 — 'Gameplay UI Layers' / Canvas를 못 찾았다.");
                return;
            }

            var view = ShopPopupView.FindOrCreate(layersGo.GetComponent<RectTransform>());
            if (view == null)
            {
                Debug.LogError("[판정 캡처] ShopPopupView 생성 실패.");
                return;
            }

            // 🔴 유물·소모품 칸은 <b>실제 카탈로그 id</b>로 짓는다 — 합성 id("R01")는 iconId가 없어
            //    복주머니 폴백만 찍히고, 그러면 아이콘 배선을 판정할 수 없다(P1 판정용, 2026-08-31).
            var offers = new List<ShopOfferSlotModel>
            {
                new ShopOfferSlotModel(ShopItemKind.Card, "A01", "휘둘러치기", "적에게 피해", 35),
                new ShopOfferSlotModel(ShopItemKind.Card, "A00", "공격의 기초", "적에게 피해", 30),
                new ShopOfferSlotModel(ShopItemKind.Card, "M03", "3칸 이동", "최대 3칸 이동", 40),
            };
            AddCatalogRelicOffer(offers, "relic-water-mill", 80);
            AddCatalogRelicOffer(offers, "relic-blue-lantern", 75);
            AddCatalogItemOffer(offers, "item-ward-flask", 25);
            AddCatalogItemOffer(offers, "item-veil-bead", 25);
            var removals = new List<ShopRemovalCandidate>
            {
                new ShopRemovalCandidate("c1", "찌르기"), new ShopRemovalCandidate("c2", "웅크리기"),
                new ShopRemovalCandidate("c3", "달아나기"), new ShopRemovalCandidate("c4", "부적 던지기"),
            };

            // 🔴 실물 진열(좌우 2단)은 <b>스냅샷 통로가 있어야</b> 선다 — null이면 종전 텍스트 목록으로
            //    통째 폴백해서 "세로로 누운 글자"가 찍힌다(RefreshSlots의 realShelf 게이트).
            CombatState state;
            try
            {
                var demoMap = CombatState.CreateDemoMap(6);
                state = new CombatState(demoMap, new HexCoord(0, 0), new HexCoord(4, 0),
                    new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, actionBudget: 2, movementHandSize: 1, actionHandSize: 5));
            }
            catch (Exception e)
            {
                Debug.LogError("[판정 캡처] 카드 스냅샷용 CombatState 생성 실패: " + e.Message);
                return;
            }

            view.Show(offers, () => 120, _ => false, () => removals, _ => false, () => { },
                cardId => state.TryCreateCatalogCardSnapshot(cardId, out var snapshot) ? snapshot : (CombatCardSnapshot?)null);

            // 🔴 ScreenSpaceOverlay 캔버스는 Camera.Render()에 잡히지 않는다 — 캡처 동안만 Camera 모드로 바꾼다.
            var prevMode = canvas.renderMode;
            var prevCam = canvas.worldCamera;
            var prevPlane = canvas.planeDistance;
            var camGo = new GameObject("__JudgmentCaptureUiCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.09f, 1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;

            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 100f;

                // 🔴 에디트 모드는 레이아웃이 자동으로 돌지 않고, 중첩 LayoutGroup은 한 번에 수렴하지 않는다.
                for (var i = 0; i < 4; i++)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var rect in view.GetComponentsInChildren<RectTransform>(true))
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    }
                }

                RenderCameraToFile(cam, Path.Combine(OutputRoot, "nightmarket.png"), 1600, 900);
            }
            finally
            {
                canvas.renderMode = prevMode;
                canvas.worldCamera = prevCam;
                canvas.planeDistance = prevPlane;
                view.gameObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(camGo);
                Canvas.ForceUpdateCanvases();
            }

            Done("잡화점 진열");
        }

        // ── 상태 링 몸집 비례 ───────────────────────────────────────────────────
        [MenuItem("Seoul Playup/Dev/판정 캡처/상태 링 몸집 비례")]
        public static void CaptureStatusRingScale()
        {
            var bodies = new (string Id, string Path)[]
            {
                ("player", "Assets/Art/Characters/player/Prefabs/Player.prefab"),
                ("pig", "Assets/Art/Characters/monster/Pig/Prefabs/Pig.prefab"),
                ("bull", "Assets/Art/Characters/monster/Bull/Prefabs/Bull.prefab"),
                ("bulgasal", "Assets/Art/Characters/monster/Bulgasal/Prefabs/Bulgasal.prefab"),
            };
            var rings = new (string Id, string Path)[]
            {
                ("debuff", DebuffRingPrefabPath),
                ("buff", BuffRingPrefabPath),
            };

            // 🔴🔴 플레이어 몸집은 프리팹 스케일이 아니다 — 런타임이 playerVisualLocalScale(씬 저작 2.5)을
            //     덧곱한다. 이 값을 빼먹으면 기준 체구가 작아져 <b>다른 몸이 전부 계수 상한(2.5)에 걸린다</b>.
            //     하드코딩 금지: 씬 저작이 바뀌면 벤치가 조용히 거짓말을 한다.
            var playerExtra = Vector3.one;
            var atlas = UnityEngine.Object.FindFirstObjectByType<AtlasTilePresentationView>(FindObjectsInactive.Include);
            if (atlas != null)
            {
                var field = typeof(AtlasTilePresentationView).GetField(
                    "playerVisualLocalScale", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    playerExtra = (Vector3)field.GetValue(atlas);
                }
            }

            var root = new GameObject("__JudgmentCaptureRingBench");
            root.transform.position = new Vector3(0f, 0f, -400f); // 씬 배경이 프레임에 끼지 않게 멀찍이
            var lightGo = new GameObject("__JudgmentCaptureLight", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            var camGo = new GameObject("__JudgmentCaptureRingCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.10f, 1f);
            cam.fieldOfView = 40f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 800f;

            var spawned = new List<GameObject>();
            var report = new List<string>();
            try
            {
                foreach (var ring in rings)
                {
                    foreach (var go in spawned)
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                    }

                    spawned.Clear();

                    var ringPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ring.Path);
                    if (ringPrefab == null)
                    {
                        Debug.LogWarning("[판정 캡처] 링 프리팹 없음: " + ring.Path);
                        continue;
                    }

                    var placed = new List<(GameObject Body, float Diameter)>();
                    var referenceDiameter = 0f;
                    var x = 0f;
                    foreach (var body in bodies)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(body.Path);
                        if (prefab == null)
                        {
                            Debug.LogWarning("[판정 캡처] 몸 프리팹 없음: " + body.Path);
                            continue;
                        }

                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                        instance.transform.localPosition = new Vector3(x, 0f, 0f);
                        instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                        if (body.Id == "player")
                        {
                            instance.transform.localScale = Vector3.Scale(instance.transform.localScale, playerExtra);
                        }

                        spawned.Add(instance);

                        // 🔴🔴 measureSpace와 visualRoot에 <b>같은 트랜스폼</b>을 넘기면 그 인스턴스 자신의
                        //     localScale이 측정에서 빠진다(= 프리팹 루트 스케일이 통째로 무시된다).
                        //     부모 공간에서 재야 출하 실측(플레이어 2.042 · 깡패 돼지 2.586)과 일치한다.
                        var diameter = 2f;
                        if (CharacterFootprint.TryResolveLocalBounds(root.transform, instance.transform, out var localBounds))
                        {
                            diameter = Mathf.Max(localBounds.size.x, localBounds.size.z);
                        }

                        if (body.Id == "player")
                        {
                            referenceDiameter = diameter;
                        }

                        placed.Add((instance, diameter));
                        x += 7f;
                    }

                    foreach (var entry in placed)
                    {
                        // 계수도 출하 코드가 계산한다 — 벤치가 따로 셈하면 화면과 규칙이 갈라진다.
                        var scale = StatusLoopBodyScale.Resolve(
                            entry.Diameter, referenceDiameter > 0.01f ? referenceDiameter : entry.Diameter);
                        var ringInstance = (GameObject)PrefabUtility.InstantiatePrefab(ringPrefab, root.transform);
                        ringInstance.transform.position = entry.Body.transform.position + new Vector3(0f, -0.5f * scale, 0f);
                        ringInstance.transform.localScale = new Vector3(0.5f * scale, 1f * scale, 0.5f * scale);
                        spawned.Add(ringInstance);
                        foreach (var particles in ringInstance.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            particles.Simulate(1.2f, true, true, false);
                        }

                        report.Add($"{ring.Id}/{entry.Body.name} 지름={entry.Diameter:0.00} 계수={scale:0.00}");
                    }

                    var mid = (x - 7f) * 0.5f;
                    var distance = Mathf.Max(x, 8f) * 0.34f / Mathf.Tan(20f * Mathf.Deg2Rad);
                    cam.transform.position = root.transform.position + new Vector3(mid, distance * 0.22f, -distance);
                    cam.transform.LookAt(root.transform.position + new Vector3(mid, 1.1f, 0f));
                    RenderCameraToFile(cam, Path.Combine(OutputRoot, "ring_" + ring.Id + ".png"), 1600, 700);
                }
            }
            finally
            {
                foreach (var go in spawned)
                {
                    if (go != null)
                    {
                        UnityEngine.Object.DestroyImmediate(go);
                    }
                }

                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(lightGo);
                UnityEngine.Object.DestroyImmediate(camGo);
            }

            Debug.Log("[판정 캡처] 상태 링 실측 — " + string.Join(" | ", report));
            Done("상태 링 몸집 비례");
        }

        // ── 보스 페이즈 몸집(2026-09-05 후속 #4) ─────────────────────────────────
        [MenuItem("Seoul Playup/Dev/판정 캡처/보스 페이즈 몸집 3종")]
        public static void CaptureBossPhaseScales()
        {
            CaptureBossPhaseScales(null);
        }

        /// <summary>
        /// 불가살을 페이즈별 <c>visualScale</c>(출하 boss_phases.csv 직독)로 세 벌 세우고, 발밑에 그 페이즈의 footprint
        /// (P1 1칸 · P2 tri 3칸 · P3 반경1 7칸)를 타일 반경 그대로 원판으로 깔아 「몸이 점유 칸을 얼마나 덮는가」를
        /// 한 장에 담는다. 카메라는 실게임 피치. <paramref name="scaleOverrides"/>를 주면 그 값으로(재튜닝 후보 비교용).
        /// </summary>
        public static void CaptureBossPhaseScales(float[] scaleOverrides)
        {
            const string bossPrefabPath = "Assets/Art/Characters/monster/Bulgasal/Prefabs/Bulgasal.prefab";
            const string phasesCsvPath = "Assets/Data/Combat/Monsters/Source/boss_phases.csv";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(bossPrefabPath);
            var phasesCsv = AssetDatabase.LoadAssetAtPath<TextAsset>(phasesCsvPath);
            if (prefab == null || phasesCsv == null)
            {
                Debug.LogError("[판정 캡처] 보스 프리팹 또는 boss_phases.csv 없음");
                return;
            }

            // 출하 저작 직독: visualScale(6열)·footprintRadius(7열)·footprintShape(8열). 벤치가 값을 들고 있으면 낡는다.
            var phases = new List<(int Phase, float Scale, int Radius, string Shape)>();
            foreach (var raw in phasesCsv.text.Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("M002,", StringComparison.Ordinal))
                {
                    continue;
                }

                var cols = line.Split(',');
                phases.Add((
                    int.Parse(cols[1]),
                    float.Parse(cols[6], System.Globalization.CultureInfo.InvariantCulture),
                    int.Parse(cols[7]),
                    cols[8].Trim()));
            }

            var enemyExtra = Vector3.one;
            var controller = UnityEngine.Object.FindFirstObjectByType<MapCombatController>(FindObjectsInactive.Include);
            if (controller != null)
            {
                var field = typeof(MapCombatController).GetField("enemyVisualLocalScale", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null && field.GetValue(controller) is Vector3 extra)
                {
                    enemyExtra = extra;
                }
            }

            var atlas = UnityEngine.Object.FindFirstObjectByType<AtlasTilePresentationView>(FindObjectsInactive.Include);
            var tileRadius = atlas != null ? atlas.TileRadius : 1f;
            var layout = new HexAxialLayout(tileRadius);

            var root = new GameObject("__JudgmentCaptureBossScale");
            root.transform.position = new Vector3(0f, 0f, -420f);
            var lightGo = new GameObject("__JudgmentCaptureLight", typeof(Light));
            var light = lightGo.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var camGo = new GameObject("__JudgmentCaptureBossCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.05f, 0.06f, 0.10f, 1f);
            cam.fieldOfView = GameCameraFov;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 800f;

            var tileMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Standard"));
            var tileColor = new Color(0.22f, 0.36f, 0.62f, 1f);
            tileMaterial.color = tileColor;
            if (tileMaterial.HasProperty("_BaseColor"))
            {
                tileMaterial.SetColor("_BaseColor", tileColor);
            }

            var report = new List<string>();
            try
            {
                var x = 0f;
                const float slotWidth = 9f;
                for (var i = 0; i < phases.Count; i++)
                {
                    var phase = phases[i];
                    var scale = scaleOverrides != null && i < scaleOverrides.Length ? scaleOverrides[i] : phase.Scale;
                    var slot = new GameObject($"P{phase.Phase}");
                    slot.transform.SetParent(root.transform, false);
                    slot.transform.localPosition = new Vector3(x, 0f, 0f);

                    // footprint 원판(출하 오프셋 규약 그대로: tri = (0,0)(1,0)(0,1) · 원판 = 반경 r).
                    var offsets = new List<HexCoord>();
                    if (string.Equals(phase.Shape, MonsterFootprints.TriangleToken, StringComparison.OrdinalIgnoreCase))
                    {
                        offsets.AddRange(MonsterFootprints.TriangleOffsets);
                    }
                    else
                    {
                        offsets.AddRange(Disc(new HexCoord(0, 0), phase.Radius));
                    }

                    foreach (var offset in offsets)
                    {
                        var tile = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                        UnityEngine.Object.DestroyImmediate(tile.GetComponent<Collider>());
                        tile.name = $"cell {offset.Q},{offset.R}";
                        tile.transform.SetParent(slot.transform, false);
                        var xz = layout.CoordToWorld(offset);
                        tile.transform.localPosition = new Vector3(xz.x, -0.05f, xz.y);
                        // Unity Cylinder는 반지름 0.5 → 지름 1. 타일 반경(꼭짓점 반경)의 0.92배 원판이면 인접 원판이 살짝 겹치지 않는다.
                        tile.transform.localScale = new Vector3(tileRadius * 0.92f * 2f, 0.05f, tileRadius * 0.92f * 2f);
                        tile.GetComponent<Renderer>().sharedMaterial = tileMaterial;
                    }

                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, slot.transform);
                    instance.transform.localPosition = Vector3.zero;
                    instance.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                    instance.transform.localScale = Vector3.Scale(instance.transform.localScale, Vector3.Scale(enemyExtra, Vector3.one * scale));

                    var width = 0f;
                    if (CharacterFootprint.TryResolveLocalBounds(slot.transform, instance.transform, out var bounds))
                    {
                        width = Mathf.Max(bounds.size.x, bounds.size.z);
                    }

                    var footprintWidth = offsets.Count == 1
                        ? tileRadius * 2f
                        : (string.Equals(phase.Shape, "tri", StringComparison.OrdinalIgnoreCase) ? tileRadius * 2f + tileRadius * Mathf.Sqrt(3f) * 0.5f : tileRadius * 2f * (phase.Radius * 2 + 1) * 0.87f);
                    report.Add($"P{phase.Phase} scale={scale:0.##} 모델폭={width:0.00}u(칸 {width / (tileRadius * Mathf.Sqrt(3f)):0.00}) footprint={offsets.Count}칸(폭≈{footprintWidth:0.00}u)");
                    x += slotWidth;
                }

                var mid = (x - slotWidth) * 0.5f;
                var distance = GameCameraDistance * 1.35f;
                var pitch = GameCameraPitch;
                var target = root.transform.position + new Vector3(mid, 0.6f, 0f);
                var dir = Quaternion.Euler(pitch, 0f, 0f) * Vector3.forward;
                cam.transform.position = target - dir * distance;
                cam.transform.LookAt(target);
                var suffix = scaleOverrides == null ? "authored" : string.Join("-", scaleOverrides.Select(s => s.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
                RenderCameraToFile(cam, Path.Combine(OutputRoot, $"boss_phase_scale_{suffix}.png"), 1800, 760);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(lightGo);
                UnityEngine.Object.DestroyImmediate(camGo);
                UnityEngine.Object.DestroyImmediate(tileMaterial);
            }

            Debug.Log("[판정 캡처] 보스 페이즈 몸집 — " + string.Join(" | ", report));
            Done("보스 페이즈 몸집 3종");
        }

        // ── 공용 ────────────────────────────────────────────────────────────────
        private static bool TryPrepareMap(AtlasTilePresentationView view, out HexMapData map)
        {
            map = null;
            var source = AssetDatabase.LoadAssetAtPath<HexSparseMapAuthoringSource>(MapSourcePath);
            if (source == null)
            {
                Debug.LogError("[판정 캡처] 맵 소스 없음: " + MapSourcePath);
                return false;
            }

            if (!source.TryToHexMapData(out map, out var error))
            {
                Debug.LogError("[판정 캡처] 맵 변환 실패: " + error);
                return false;
            }

            view.Render(map);

            // 🔴 에디트 모드는 Awake를 건너뛴다 — 설정 에셋을 명시적으로 먹이지 않으면 씬에 직렬화된
            //    낡은 값으로 찍힌다(2026-08-20에 이걸로 캡처 한 벌을 통째로 버렸다).
            var settings = AssetDatabase.LoadAssetAtPath<VisibilityPresentationSettings>(VisibilitySettingsPath);
            if (settings != null)
            {
                view.ApplyVisibilitySettings(settings);
            }

            return true;
        }

        private static void ClearAllLayers(BatchedMeshCombatOverlayRenderer renderer)
        {
            foreach (HexOverlayLayer layer in Enum.GetValues(typeof(HexOverlayLayer)))
            {
                renderer.ClearLayer(layer);
            }
        }

        private static List<HexCoord> Disc(HexCoord center, int radius)
        {
            var result = new List<HexCoord>();
            for (var dq = -radius; dq <= radius; dq++)
            {
                for (var dr = -radius; dr <= radius; dr++)
                {
                    var ds = -dq - dr;
                    if (Mathf.Max(Mathf.Abs(dq), Mathf.Max(Mathf.Abs(dr), Mathf.Abs(ds))) > radius)
                    {
                        continue;
                    }

                    result.Add(new HexCoord(center.Q + dq, center.R + dr));
                }
            }

            return result;
        }

        // ── P1·P4 판정 캡처 (유물·소모품 아이콘 / 처치 추가 보상) ─────────────────
        //
        // 🔑 여기서 만드는 화면은 목업이 아니다 — 실제 뷰 클래스가 실제 카탈로그(relics.csv ·
        //    consumable_items.csv)를 읽고, 아이콘은 출하 경로(RuntimeUiAssetCatalog.LoadItemIcon)
        //    한 곳에서 해소된다. 플레이 모드에 들어가지 않는 이유는 이 파일 머리 주석과 같다.

        private static void AddCatalogRelicOffer(List<ShopOfferSlotModel> offers, string relicId, int price)
        {
            if (!PlayerPermanentItemCatalog.TryGet(relicId, out var definition))
            {
                Debug.LogWarning($"[판정 캡처] 유물 '{relicId}'을 카탈로그에서 못 찾았다 — 칸을 건너뛴다.");
                return;
            }

            offers.Add(new ShopOfferSlotModel(
                ShopItemKind.Relic, definition.Id, definition.DisplayName, definition.Description, price, definition.IconId));
        }

        private static void AddCatalogItemOffer(List<ShopOfferSlotModel> offers, string itemId, int price)
        {
            if (!ConsumableItemCatalog.TryGet(itemId, out var definition))
            {
                Debug.LogWarning($"[판정 캡처] 소모품 '{itemId}'을 카탈로그에서 못 찾았다 — 칸을 건너뛴다.");
                return;
            }

            offers.Add(new ShopOfferSlotModel(
                ShopItemKind.Item, definition.Id, definition.DisplayName, definition.Description, price, definition.IconId));
        }

        /// <summary>저작된 유물을 순서대로 담은 실전 인벤토리 — 칩에 실제 iconId가 흐르게 한다.</summary>
        private static CombatState CreateStateWithRealRelics(int count)
        {
            var inventory = new PlayerRelicCurseInventory();
            foreach (var definition in PlayerPermanentItemCatalog.Definitions.Take(count))
            {
                inventory.TryAdd(definition.ToState(), out _);
            }

            return new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default,
                playerInventory: new PlayerInventoryState(inventory, new PlayerBagState()));
        }

        /// <summary>
        /// 🔴 ScreenSpaceOverlay 캔버스는 <c>Camera.Render()</c>에 잡히지 않는다 — 캡처 동안만
        /// Camera 모드로 바꾸고 되돌린다(잡화점 캡처와 같은 수법). 에디트 모드는 레이아웃이
        /// 자동으로 돌지 않고 중첩 LayoutGroup은 한 번에 수렴하지 않으므로 네 번 강제한다.
        /// </summary>
        private static void CaptureCanvas(Canvas canvas, Component subject, string fileName, int width, int height)
        {
            var prevMode = canvas.renderMode;
            var prevCam = canvas.worldCamera;
            var prevPlane = canvas.planeDistance;
            var camGo = new GameObject("__JudgmentCaptureUiCam", typeof(Camera));
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.05f, 0.09f, 1f);
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;

            try
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 100f;

                for (var i = 0; i < 4; i++)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var rect in subject.GetComponentsInChildren<RectTransform>(true))
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    }
                }

                RenderCameraToFile(cam, Path.Combine(OutputRoot, fileName), width, height);
            }
            finally
            {
                canvas.renderMode = prevMode;
                canvas.worldCamera = prevCam;
                canvas.planeDistance = prevPlane;
                UnityEngine.Object.DestroyImmediate(camGo);
                Canvas.ForceUpdateCanvases();
            }
        }

        [MenuItem("Seoul Playup/Dev/판정 캡처/유물 칩 아이콘 (사이드바)")]
        public static void CaptureRelicChips()
        {
            var panel = UnityEngine.Object.FindFirstObjectByType<SidebarRelicCursePanelView>(FindObjectsInactive.Include);
            if (panel == null)
            {
                Debug.LogError("[판정 캡처] MainGameplay 씬을 먼저 열 것 — SidebarRelicCursePanelView를 못 찾았다.");
                return;
            }

            var canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("[판정 캡처] 유물 패널의 Canvas를 못 찾았다.");
                return;
            }

            // 패널과 그 조상들이 꺼져 있다 — 캡처 동안만 켜고 원래대로 되돌린다.
            var reactivated = new List<GameObject>();
            for (var t = panel.transform; t != null; t = t.parent)
            {
                if (!t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(true);
                    reactivated.Add(t.gameObject);
                }
            }

            try
            {
                panel.AutoBindFromHierarchy();
                panel.Refresh(CreateStateWithRealRelics(8), null);
                CaptureCanvas(canvas, panel, "relic-chips.png", 1600, 900);
                // 58x58 칩의 가독성은 등배로는 판정할 수 없다 — 같은 화면을 3배 해상도로 한 장 더 낸다
                // (확대가 아니라 더 촘촘히 렌더한 것이라 실제 픽셀이 늘어난다).
                CaptureCanvas(canvas, panel, "relic-chips-3x.png", 4800, 2700);
            }
            finally
            {
                foreach (var go in reactivated)
                {
                    go.SetActive(false);
                }
            }

            Done("유물 칩 아이콘");
        }

        [MenuItem("Seoul Playup/Dev/판정 캡처/처치 전리품 목록")]
        public static void CaptureLootRewards()
        {
            var layersGo = GameObject.Find("Gameplay UI Layers");
            var layers = layersGo != null ? layersGo.GetComponent<RectTransform>() : null;
            var canvas = layersGo != null ? layersGo.GetComponentInParent<Canvas>() : null;
            if (layers == null || canvas == null)
            {
                Debug.LogError("[판정 캡처] MainGameplay 씬을 먼저 열 것 — 'Gameplay UI Layers' / Canvas를 못 찾았다.");
                return;
            }

            var view = LootRewardPopupView.FindOrCreate(layers);
            if (view == null)
            {
                Debug.LogError("[판정 캡처] LootRewardPopupView 생성 실패.");
                return;
            }

            // 정예 처치 = 엽전 + 소모품 + 유물 + 부적 추가. 가장 붐비는 목록이라 여기가 판정면이다.
            var rows = new List<LootRewardRow>();
            var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
            rows.Add(new LootRewardRow(
                LootRewardRowKind.Money, "money", string.Empty,
                catalog != null ? catalog.CoinLightSprite : null, 24));

            if (ConsumableItemCatalog.TryGet("item-thunder-bead", out var item))
            {
                rows.Add(new LootRewardRow(
                    LootRewardRowKind.Item, item.Id, item.DisplayName,
                    SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadItemIcon(item.IconId)));
            }

            if (PlayerPermanentItemCatalog.TryGet("relic-haetae-statue", out var relic))
            {
                rows.Add(new LootRewardRow(
                    LootRewardRowKind.Relic, relic.Id, relic.DisplayName,
                    SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadItemIcon(relic.IconId)));
            }

            rows.Add(new LootRewardRow(LootRewardRowKind.Card, "card", "부적 추가"));

            // 🔴 앞선 캡처가 켜 둔 다른 오버레이가 프레임에 겹친다(실측: 유물 칩 캡처의 사이드바
            //    패널과 씬의 확인 대화상자가 함께 찍혔다) — 이 캡처 동안만 내려 둔다.
            var hidden = new List<GameObject>();
            for (var i = 0; i < layers.childCount; i++)
            {
                var sibling = layers.GetChild(i).gameObject;
                if (sibling == view.gameObject || !sibling.activeSelf)
                {
                    continue;
                }

                sibling.SetActive(false);
                hidden.Add(sibling);
            }

            // 🔴 이 뷰는 캡처가 <b>만들어 낸</b> 것이다 — 씬에 남기면 다음 씬 저장 때 3,000줄짜리
            //    가짜 변경이 되어 이력에 들어간다(2026-08-31 실측: 실제로 저장됐다). 찍고 지운다.
            try
            {
                view.Show(rows, _ => false, () => { }, () => { });
                CaptureCanvas(canvas, view, "loot-rewards.png", 1600, 900);
                CaptureCanvas(canvas, view, "loot-rewards-2x.png", 3200, 1800);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
                foreach (var go in hidden)
                {
                    go.SetActive(true);
                }

                Canvas.ForceUpdateCanvases();
            }

            Done("처치 전리품 목록");
        }

        [MenuItem("Seoul Playup/Dev/판정 캡처/도감 썸네일 (로비)")]
        public static void CaptureCodexThumbnails()
        {
            // 🔴 도감은 로비 씬에서만 산다 — 열기 전에 현재 씬을 저장해 둔다(안 하면 남의 작업이 날아간다).
            var previousScenePath = SceneManager.GetActiveScene().path;
            if (SceneManager.GetActiveScene().isDirty && !EditorSceneManager.SaveOpenScenes())
            {
                Debug.LogError("[판정 캡처] 현재 씬을 저장하지 못했다 — 로비를 열지 않는다.");
                return;
            }

            EditorSceneManager.OpenScene(LobbyScenePath, OpenSceneMode.Single);
            try
            {
                CaptureCodexInOpenLobby();
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousScenePath))
                {
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
                }
            }

            Done("도감 썸네일");
        }

        private const string LobbyScenePath = "Assets/Scenes/Game/Lobby.unity";

        private static void CaptureCodexInOpenLobby()
        {
            var canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>(FindObjectsInactive.Include);
            if (canvas == null)
            {
                Debug.LogError("[판정 캡처] 로비 씬에서 Canvas를 못 찾았다.");
                return;
            }

            var icons = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
            var domains = new List<ICodexDomain>
            {
                new CodexRelicDomain(
                    RelicCatalogCsv.ConvertFile(CombatCsvPaths.RelicsCsv),
                    new Color(0.898f, 0.769f, 0.404f, 1f),
                    icons),
                new CodexConsumableItemDomain(
                    ConsumableItemCatalogCsv.ConvertFile(CombatCsvPaths.ConsumableItemsCsv),
                    new Color(0.451f, 0.812f, 0.686f, 1f),
                    icons),
            };

            // 잠긴 칸은 실루엣으로 뜬다 — 판정 대상은 «전용 아트가 격자에서 어떻게 보이는가»이므로
            // 전부 해금한 진행도로 연다(저장하지 않는 임시 객체다).
            var progress = new CodexProgress();
            foreach (var domain in domains)
            {
                foreach (var entry in domain.Entries)
                {
                    progress.MarkSeen(domain.Id, entry.Id);
                }
            }

            var host = new GameObject("__JudgmentCodexHost");
            var overlay = host.AddComponent<CodexOverlayView>();
            try
            {
                overlay.Configure(domains, progress);
                overlay.Open(canvas.transform);

                for (var i = 0; i < 4; i++)
                {
                    Canvas.ForceUpdateCanvases();
                    foreach (var rect in overlay.GetComponentsInChildren<RectTransform>(true))
                    {
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
                    }
                }

                foreach (var skin in overlay.GetComponentsInChildren<SeoulPlayup.Combat.Unity.UiProceduralPanel>(true))
                {
                    skin.ResyncRectSize();
                }

                CaptureCanvas(canvas, overlay, "codex-thumbnails.png", 1600, 900);
                CaptureCanvas(canvas, overlay, "codex-thumbnails-2x.png", 3200, 1800);
            }
            finally
            {
                overlay.Close();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static void RenderCameraToFile(Camera camera, string path, int width, int height)
        {
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Texture2D texture = null;
            try
            {
                camera.targetTexture = rt;
                camera.Render();
                camera.targetTexture = null;

                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                if (texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(texture);
                }

                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }
        }

        private static void Done(string what)
        {
            AssetDatabase.Refresh();
            Debug.Log($"[판정 캡처] {what} → {OutputRoot}\n" +
                      "⚠️ 씬은 저장하지 않았다. 저장하지 말 것 — 캡처가 만진 상태가 이력에 들어간다.");
            EditorUtility.RevealInFinder(OutputRoot);
        }

        /// <summary>
        /// 🔴🔴 MainGameplay의 <c>Main Camera</c>는 Base가 아니라 <b>Overlay</b>다 —
        /// <c>Skybox Camera</c>(depth −10 · Base)의 URP 스택에 얹혀 있다. 그래서 캡처는 <b>Skybox
        /// 쪽에</b> targetTexture를 걸고 그쪽을 Render해야 배경 합성까지 나온다. Main을 직접 Render하면
        /// 검은 프레임이 나온다(2026-08-20 실증). Skybox는 정적 플레이트라 위치를 건드리지 않는다.
        /// </summary>
        private sealed class SceneCameraLease : IDisposable
        {
            private Camera skybox;
            private GameObject mainGo;
            private Camera mainCam;
            private Vector3 savedPosition;
            private Quaternion savedRotation;
            private float savedFov;
            private RenderTexture savedTarget;

            public bool Valid { get; private set; }

            public static SceneCameraLease Acquire()
            {
                var lease = new SceneCameraLease();
                var skyboxGo = GameObject.Find("Skybox Camera");
                lease.mainGo = GameObject.Find("Main Camera");
                if (skyboxGo == null || lease.mainGo == null)
                {
                    Debug.LogError("[판정 캡처] 'Skybox Camera' / 'Main Camera'를 못 찾았다 — MainGameplay 씬인지 확인할 것.");
                    return lease;
                }

                lease.skybox = skyboxGo.GetComponent<Camera>();
                lease.mainCam = lease.mainGo.GetComponent<Camera>();
                if (lease.skybox == null || lease.mainCam == null)
                {
                    Debug.LogError("[판정 캡처] Camera 컴포넌트를 못 찾았다.");
                    return lease;
                }

                lease.savedPosition = lease.mainGo.transform.position;
                lease.savedRotation = lease.mainGo.transform.rotation;
                lease.savedFov = lease.mainCam.fieldOfView;
                lease.savedTarget = lease.skybox.targetTexture;
                lease.Valid = true;
                return lease;
            }

            public void FrameAt(Vector3 target, float distance, float pitch, float fov)
            {
                mainCam.fieldOfView = fov;
                var rotation = Quaternion.Euler(pitch, 0f, 0f);
                mainGo.transform.rotation = rotation;
                mainGo.transform.position = target - (rotation * Vector3.forward) * distance;
            }

            public void Capture(string path, int width, int height)
            {
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Texture2D texture = null;
                try
                {
                    skybox.targetTexture = rt;
                    skybox.Render();
                    skybox.targetTexture = savedTarget;

                    var previous = RenderTexture.active;
                    RenderTexture.active = rt;
                    texture = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    texture.Apply();
                    RenderTexture.active = previous;
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally
                {
                    if (texture != null)
                    {
                        UnityEngine.Object.DestroyImmediate(texture);
                    }

                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }

            public void Dispose()
            {
                if (!Valid)
                {
                    return;
                }

                skybox.targetTexture = savedTarget;
                mainGo.transform.position = savedPosition;
                mainGo.transform.rotation = savedRotation;
                mainCam.fieldOfView = savedFov;
            }
        }
    }
}
#endif
