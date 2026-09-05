using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.MapDesign.Editor;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class AtlasTilePresentationViewTests
    {
        [Test]
        public void RendersArbitraryHexSetWithHeightAndRotationSteps()
        {
            var root = new GameObject("AtlasPresentationTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasTestTopPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f);
                var map = new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test", heightLevel: 0),
                    Cell(new HexCoord(2, -1), "atlas-test", heightLevel: 2, rotationSteps: 3),
                    Cell(new HexCoord(-1, 2), "atlas-test", heightLevel: 1, rotationSteps: 5)
                });

                view.Render(map);

                Assert.That(view.TopVisualCount, Is.EqualTo(3));
                Assert.That(view.SideVisualCount, Is.EqualTo(18));
                Assert.That(view.SideVisualsDeferred, Is.False);
                var raised = FindChild(root.transform, "Atlas_Top_2_-1_AtlasTestTopPrefab");
                Assert.That(raised, Is.Not.Null);
                Assert.That(raised.transform.localPosition.y, Is.EqualTo(1f).Within(0.001f));
                Assert.That(Mathf.DeltaAngle(raised.transform.localEulerAngles.y, 180f), Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ChunkSafePrefabDetectionAcceptsSingleRendererWithoutCollider()
        {
            var prefab = CreateChunkSafePrefab("AtlasChunkSafeDetectionPrefab");
            try
            {
                Assert.That(AtlasTilePresentationView.IsTopPrefabChunkSafeForTests(prefab), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ChunkSafePrefabDetectionRejectsColliderAnywhereInHierarchy()
        {
            var prefab = CreateChunkSafePrefab("AtlasChunkUnsafeColliderPrefab");
            var child = new GameObject("ColliderChild");
            child.transform.SetParent(prefab.transform, false);
            child.AddComponent<BoxCollider>();
            try
            {
                Assert.That(AtlasTilePresentationView.IsTopPrefabChunkSafeForTests(prefab), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ChunkSafeTilesRenderAsSplitTopChunksWithoutPerCellTopChildren()
        {
            var root = new GameObject("AtlasChunkedTopTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasChunkSafeTopPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-test", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f, renderSideVisuals: false, useTopChunkMeshes: true, topChunkMaxCells: 4);
                var map = new HexMapData(Enumerable.Range(0, 10).Select(index => Cell(new HexCoord(index, 0), "tile-test", heightLevel: index % 3, rotationSteps: index % 6)));

                view.Render(map);

                Assert.That(view.TopVisualCount, Is.EqualTo(10));
                Assert.That(root.transform.Cast<Transform>().Count(child => child.name.StartsWith("Atlas_TopChunk_")), Is.EqualTo(3));
                Assert.That(root.transform.Cast<Transform>().Any(child => child.name.StartsWith("Atlas_Top_")), Is.False);
                Assert.That(root.transform.Cast<Transform>().Any(child => child.name.StartsWith("Atlas_ClickCollider_")), Is.False);
                Assert.That(root.GetComponentsInChildren<Renderer>(true).Length, Is.EqualTo(3));
                Physics.SyncTransforms();
                var projected = view.transform.TransformPoint(view.ProjectTop(new HexCoord(4, 0)));
                var ray = new Ray(projected + Vector3.up * 3f + Vector3.forward * 0.02f, Vector3.down);
                Assert.That(view.TryRaycastHex(ray, out var coord), Is.True);
                Assert.That(coord, Is.EqualTo(new HexCoord(4, 0)));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ChunkSafePlayerMarkerUsesRenderedTopSurface()
        {
            var root = new GameObject("AtlasChunkedPlayerSurfaceTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasChunkPlayerSurfacePrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-test", prefab) });
                view.ConfigureForTests(catalog, renderSideVisuals: false, useTopChunkMeshes: true);
                var destination = new HexCoord(1, -1);

                view.Render(new HexMapData(new[] { Cell(destination, "tile-test") }));
                view.SetPlayerPosition(destination);

                AssertVector3(view.ProjectOverlaySurface(destination) + Vector3.up * 0.32f, view.PlayerMarkerLocalPosition);
                Assert.That(view.ProjectOverlaySurface(destination).y, Is.GreaterThan(view.ProjectTop(destination).y));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void PlayerMarkerPrefabReplacesPrimitiveMarkerWithActorVisual()
        {
            var root = new GameObject("AtlasPlayerPrefabMarkerTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var tilePrefab = CreateChunkSafePrefab("AtlasPlayerPrefabMarkerTile");
            var playerPrefab = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerPrefab.name = "AtlasPlayerPrefabMarkerVisual";
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-test", tilePrefab) });
                view.ConfigureForTests(catalog, playerMarkerPrefab: playerPrefab);
                var destination = new HexCoord(0, 0);

                view.Render(new HexMapData(new[] { Cell(destination, "tile-test") }));
                view.SetPlayerPosition(destination);

                AssertVector3(view.ProjectOverlaySurface(destination) + Vector3.up * 0.32f, view.PlayerMarkerLocalPosition);
                var visual = root.GetComponentInChildren<CharacterActorVisual>(true);
                Assert.That(visual, Is.Not.Null);
                Assert.That(visual.GetComponentsInChildren<Collider>(true).Any(collider => collider.enabled), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(tilePrefab);
                Object.DestroyImmediate(playerPrefab);
            }
        }

        [Test]
        public void TopChunkMeshesCanBeDisabledForLegacyTopChildren()
        {
            var root = new GameObject("AtlasTopChunkDisabledTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasChunkDisabledTopPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", prefab) });
                view.ConfigureForTests(catalog, renderSideVisuals: false, useTopChunkMeshes: false);

                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-test"), Cell(new HexCoord(1, 0), "atlas-test") }));

                Assert.That(view.TopVisualCount, Is.EqualTo(2));
                Assert.That(root.transform.Cast<Transform>().Count(child => child.name.StartsWith("Atlas_Top_")), Is.EqualTo(2));
                Assert.That(root.transform.Cast<Transform>().Any(child => child.name.StartsWith("Atlas_TopChunk_")), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ChunkedVisibilityUsesStateChunkOverlayWithoutPerCellPrimitive()
        {
            var root = new GameObject("AtlasChunkedVisibilityTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasChunkVisibilityTopPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-test", prefab) });
                view.ConfigureForTests(catalog, renderSideVisuals: false, useTopChunkMeshes: true);
                var coord = new HexCoord(0, 0);
                view.Render(new HexMapData(new[] { Cell(coord, "tile-test") }));

                view.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Unknown(coord));

                Assert.That(view.FogMaterialRefreshCount, Is.EqualTo(1));
                Assert.That(view.FoggedRendererCount, Is.EqualTo(0));
                Assert.That(view.VisibilityChunkRendererCount, Is.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<Transform>(true).Any(child => child.name == "Visibility"), Is.False);
                var visibility = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(child => child.name.StartsWith("Atlas_VisibilityChunk_Unknown_"));
                Assert.That(visibility, Is.Not.Null);
                Assert.That(visibility.GetComponent<Collider>(), Is.Null);
                AssertOverlayDoesNotAffectLighting(visibility.GetComponent<Renderer>());
                var mesh = visibility.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.normals.Select(normal => normal.y), Has.All.GreaterThan(0f), "Visibility chunk faces must point upward so fog is visible from the gameplay camera.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void LightingMaskModeUsesWorldMaskWithoutOverlayChunksAndCanSwitchBack()
        {
            var root = new GameObject("AtlasLightingMaskTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasLightingMaskTopPrefab");
            try
            {
                var shader = Shader.Find("SeoulPlayup/Map/Visibility Lit");
                Assert.That(shader, Is.Not.Null, "The visibility-lighting shader must be imported before the presentation test runs.");

                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-test", prefab) });
                view.ConfigureForTests(catalog, renderSideVisuals: false, useTopChunkMeshes: true);
                view.ConfigureVisibilityPresentationForTests(VisibilityPresentationMode.LightingMask, shader, maskResolution: 128, blurPasses: 0);
                var unknown = new HexCoord(0, 0);
                var hinted = new HexCoord(1, 0);
                var revealed = new HexCoord(2, 0);
                view.Render(new HexMapData(new[]
                {
                    Cell(unknown, "tile-test"),
                    Cell(hinted, "tile-test"),
                    Cell(revealed, "tile-test")
                }));

                HexVisibilitySafeCellInfo Visibility(HexCoord coord)
                {
                    if (coord == hinted)
                    {
                        return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Hinted, true, true, false, "tile", "street", 1, true, false, string.Empty, string.Empty);
                    }

                    if (coord == revealed)
                    {
                        return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Revealed, true, true, true, "tile", "street", 1, true, false, string.Empty, string.Empty);
                    }

                    return HexVisibilitySafeCellInfo.Unknown(coord);
                }

                view.ApplyVisibility(Visibility);

                Assert.That(view.VisibilityLightingMaskTexture, Is.Not.Null);
                Assert.That(view.VisibilityChunkRendererCount, Is.EqualTo(0));
                Assert.That(root.transform.Cast<Transform>().Any(child => child.name.StartsWith("Atlas_VisibilityChunk_")), Is.False);
                Assert.That(root.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.shader == shader), Is.True);
                Assert.That(view.SampleVisibilityLightingMaskForTests(unknown), Is.LessThan(view.SampleVisibilityLightingMaskForTests(hinted)));
                Assert.That(view.SampleVisibilityLightingMaskForTests(hinted), Is.LessThan(view.SampleVisibilityLightingMaskForTests(revealed)));

                view.SetVisibilityPresentationMode(VisibilityPresentationMode.OverlayTint);
                view.ApplyVisibility(Visibility);

                Assert.That(view.VisibilityChunkRendererCount, Is.EqualTo(2));
                Assert.That(root.GetComponentsInChildren<Renderer>(true).Any(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.shader == shader), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        /// <summary>
        /// 물은 시야에 들어와도 밝아지지 않는다(2026-09-05 실플레이 #6). 땅은 블러된 조명 마스크로
        /// 부드럽게 밝아지지만 물은 칸 단위 색 스케일이라 강 한가운데 밝은 육각 조각이 떴다 — 물의
        /// 색 스케일은 시야 값과 무관하게 미지 조도 하나로 고정된다.
        /// </summary>
        [Test]
        public void LightingMaskKeepsWaterAtUnknownLightingEvenWhenRevealed()
        {
            var root = new GameObject("AtlasWaterRevealedLightingTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasWaterRevealedLightingTopPrefab");
            Material waterMaterial = null;
            try
            {
                var visibilityShader = Shader.Find("SeoulPlayup/Map/Visibility Lit");
                var waterShader = Shader.Find("Shader Graphs/WaterVolume-URP");
                Assert.That(visibilityShader, Is.Not.Null);
                Assert.That(waterShader, Is.Not.Null, "The project water Shader Graph must be imported before this test runs.");

                waterMaterial = new Material(waterShader);
                var surfaceColor = new Color(0.4f, 0.6f, 0.8f, 0.7f);
                var depthColor = new Color(0.2f, 0.3f, 0.5f, 0.6f);
                waterMaterial.SetColor("Color_F01C36BF", surfaceColor);
                waterMaterial.SetColor("Color_7D9A58EC", depthColor);
                prefab.GetComponent<Renderer>().sharedMaterial = waterMaterial;

                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-water", prefab) });
                view.ConfigureForTests(catalog, renderSideVisuals: true, useTopChunkMeshes: true);
                view.ConfigureVisibilityPresentationForTests(VisibilityPresentationMode.LightingMask, visibilityShader);
                var coord = new HexCoord(0, 0);
                view.Render(new HexMapData(new[] { Cell(coord, "tile-water") }));

                var revealed = new HexVisibilitySafeCellInfo(
                    coord, HexCellVisibility.Revealed, true, true, true, "tile-water", "water", 1, false, false, string.Empty, string.Empty);
                view.ApplyVisibility(_ => revealed);

                var waterRenderer = root.GetComponentsInChildren<Renderer>(true)
                    .Single(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.shader == waterShader);
                var propertyBlock = new MaterialPropertyBlock();
                waterRenderer.GetPropertyBlock(propertyBlock);
                // 0.1 = 뷰 기본 미지 조도. 시야가 밝힌 칸(1.0)이어도 물은 여기 머문다.
                AssertColorScaled(surfaceColor, propertyBlock.GetColor("Color_F01C36BF"), 0.1f);
                AssertColorScaled(depthColor, propertyBlock.GetColor("Color_7D9A58EC"), 0.1f);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
                if (waterMaterial != null) Object.DestroyImmediate(waterMaterial);
            }
        }

        [Test]
        public void LightingMaskKeepsAnimatedWaterShaderAndAppliesTileLighting()
        {
            var root = new GameObject("AtlasWaterLightingMaskTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasWaterLightingMaskTopPrefab");
            Material waterMaterial = null;
            try
            {
                var visibilityShader = Shader.Find("SeoulPlayup/Map/Visibility Lit");
                var waterShader = Shader.Find("Shader Graphs/WaterVolume-URP");
                Assert.That(visibilityShader, Is.Not.Null);
                Assert.That(waterShader, Is.Not.Null, "The project water Shader Graph must be imported before this test runs.");

                waterMaterial = new Material(waterShader);
                var surfaceColor = new Color(0.4f, 0.6f, 0.8f, 0.7f);
                var depthColor = new Color(0.2f, 0.3f, 0.5f, 0.6f);
                waterMaterial.SetColor("Color_F01C36BF", surfaceColor);
                waterMaterial.SetColor("Color_7D9A58EC", depthColor);
                prefab.GetComponent<Renderer>().sharedMaterial = waterMaterial;

                Assert.That(AtlasTilePresentationView.IsTopPrefabChunkSafeForTests(prefab), Is.False,
                    "Animated water must stay per-tile so different visibility values do not share one property block.");

                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-water", prefab) });
                view.ConfigureForTests(catalog, renderSideVisuals: true, useTopChunkMeshes: true);
                view.ConfigureVisibilityPresentationForTests(VisibilityPresentationMode.LightingMask, visibilityShader);
                var coord = new HexCoord(0, 0);

                Assert.DoesNotThrow(() => view.Render(new HexMapData(new[] { Cell(coord, "tile-water") })),
                    "Water materials without _BaseColor/_Color must not abort map initialization.");
                view.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Unknown(coord));

                var waterRenderer = root.GetComponentsInChildren<Renderer>(true)
                    .Single(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.shader == waterShader);
                Assert.That(waterRenderer.sharedMaterial, Is.SameAs(waterMaterial));

                var propertyBlock = new MaterialPropertyBlock();
                waterRenderer.GetPropertyBlock(propertyBlock);
                AssertColorScaled(surfaceColor, propertyBlock.GetColor("Color_F01C36BF"), 0.1f);
                AssertColorScaled(depthColor, propertyBlock.GetColor("Color_7D9A58EC"), 0.1f);

                view.SetVisibilityPresentationMode(VisibilityPresentationMode.OverlayTint);
                view.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Unknown(coord));
                waterRenderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("Color_F01C36BF"), Is.EqualTo(default(Color)));
                Assert.That(propertyBlock.GetColor("Color_7D9A58EC"), Is.EqualTo(default(Color)));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(waterMaterial);
            }
        }

        [Test]
        public void ChunkedOverlaysAreDestroyedOnRenderRefresh()
        {
            var root = new GameObject("AtlasChunkedOverlayCleanupTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasChunkOverlayCleanupPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-test", prefab) });
                view.ConfigureForTests(catalog, renderSideVisuals: false, useTopChunkMeshes: true);
                var coord = new HexCoord(0, 0);
                var map = new HexMapData(new[] { Cell(coord, "tile-test") });
                view.Render(map);

                view.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Unknown(coord));
                Assert.That(root.transform.Cast<Transform>().Count(child => child.name.StartsWith("Atlas_VisibilityChunk_Unknown_")), Is.EqualTo(1));

                view.Render(map);

                Assert.That(root.transform.Cast<Transform>().Any(child => child.name.StartsWith("Atlas_VisibilityChunk_")), Is.False);
                Assert.That(root.transform.Cast<Transform>().Count(child => child.name.StartsWith("Atlas_TopChunk_")), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ChunkedVisibilitySplitsUnknownAndHintedChunksAndSkipsRevealed()
        {
            var root = new GameObject("AtlasChunkedVisibilityStateSplitTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreateChunkSafePrefab("AtlasChunkVisibilityStateTopPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("tile-test", prefab) });
                view.ConfigureForTests(catalog, renderSideVisuals: false, useTopChunkMeshes: true);
                var unknown = new HexCoord(0, 0);
                var hinted = new HexCoord(1, 0);
                var revealed = new HexCoord(0, 1);
                view.Render(new HexMapData(new[]
                {
                    Cell(unknown, "tile-test"),
                    Cell(hinted, "tile-test"),
                    Cell(revealed, "tile-test")
                }));

                view.ApplyVisibility(coord =>
                {
                    if (coord == hinted)
                    {
                        return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Hinted, true, true, false, "tile-b", "street", 1, true, false, string.Empty, string.Empty);
                    }

                    if (coord == revealed)
                    {
                        return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Revealed, true, true, true, "tile-c", "street", 1, true, false, string.Empty, string.Empty);
                    }

                    return HexVisibilitySafeCellInfo.Unknown(coord);
                });

                Assert.That(view.VisibilityHiddenCount, Is.EqualTo(1));
                Assert.That(view.VisibilityExploredCount, Is.EqualTo(1));
                Assert.That(view.VisibilityVisibleCount, Is.EqualTo(1));
                Assert.That(view.VisibilityChunkRendererCount, Is.EqualTo(2));
                Assert.That(root.transform.Cast<Transform>().Count(child => child.name.StartsWith("Atlas_VisibilityChunk_Unknown_")), Is.EqualTo(1));
                Assert.That(root.transform.Cast<Transform>().Count(child => child.name.StartsWith("Atlas_VisibilityChunk_Hinted_")), Is.EqualTo(1));
                Assert.That(root.GetComponentsInChildren<Transform>(true).Any(child => child.name == "Visibility"), Is.False);
                Assert.That(view.GetLastFogAmount(unknown), Is.GreaterThan(view.GetLastFogAmount(hinted)));
                Assert.That(view.GetLastFogAmount(hinted), Is.GreaterThan(0f));
                Assert.That(view.GetLastFogAmount(revealed), Is.EqualTo(0f));

                view.ApplyVisibility(coord => new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Revealed, true, true, true, "tile-c", "street", 1, true, false, string.Empty, string.Empty));

                Assert.That(view.VisibilityChunkRendererCount, Is.EqualTo(0));
                Assert.That(root.transform.Cast<Transform>().Any(child => child.name.StartsWith("Atlas_VisibilityChunk_")), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

#if UNITY_EDITOR
        [Test]
        public void RealCatalogHasChunkSafeNonLegacyTilePrefab()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AtlasTileCatalog>("Assets/Data/Map/Catalogs/AtlasTileCatalog.asset");
            Assert.That(catalog, Is.Not.Null);
            var tileEntries = catalog.Entries
                .Where(entry => entry.AtlasVisualId.StartsWith("tile-"))
                .ToArray();
            // Animated water is refused by TopChunkDescriptor.TryCreate on purpose (it needs a per-tile
            // property block for visibility lighting), so it is an intentional exclusion, not a regression.
            var unsafeEntries = tileEntries
                .Where(entry => !AtlasTilePresentationView.IsTopPrefabChunkSafeForTests(entry.TopPrefab) &&
                                !AtlasTilePresentationView.IsAnimatedWaterTopPrefabForTests(entry.TopPrefab))
                .Select(entry => entry.AtlasVisualId)
                .ToArray();

            Assert.That(tileEntries, Has.Length.GreaterThan(0), "The real catalog should contain current non-legacy tile-* entries.");
            Assert.That(unsafeEntries, Is.Empty, "All current non-legacy tile-* prefabs should remain chunk-safe/readable: " + string.Join(", ", unsafeEntries));

            // Lock in the design: water entries in the real catalog must stay OUT of chunking.
            var chunkedWaterEntries = tileEntries
                .Where(entry => AtlasTilePresentationView.IsAnimatedWaterTopPrefabForTests(entry.TopPrefab) &&
                                AtlasTilePresentationView.IsTopPrefabChunkSafeForTests(entry.TopPrefab))
                .Select(entry => entry.AtlasVisualId)
                .ToArray();
            Assert.That(chunkedWaterEntries, Is.Empty,
                "Animated water must never become chunk-eligible (per-tile property block required): " + string.Join(", ", chunkedWaterEntries));
        }
#endif

        [Test]
        public void RendersSparseAuthoringMapWithHeightAndRotation()
        {
            var root = new GameObject("AtlasSparsePresentationTest");
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasSparseTopPrefab");
            try
            {
                source.ConfigureForTests(new[]
                {
                    new HexSparseMapAuthoringCell(new HexCoord(0, 0), "tile-a", "street", "atlas-sparse", heightLevel: 2, rotationSteps: 2)
                });
                Assert.That(source.TryToHexMapData(out var map, out var error), Is.True, error);

                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-sparse", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f);

                view.Render(map);

                var top = FindChild(root.transform, "Atlas_Top_0_0_AtlasSparseTopPrefab");
                Assert.That(top, Is.Not.Null);
                Assert.That(top.localPosition.y, Is.EqualTo(1f).Within(0.001f));
                Assert.That(Mathf.DeltaAngle(top.localEulerAngles.y, 120f), Is.EqualTo(0f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void RendersRuntimeMapObjectsWithPrefabAndRotation()
        {
            var root = new GameObject("AtlasObjectPresentationTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
            var tilePrefab = CreatePrefab("AtlasObjectTopPrefab");
            var objectPrefab = CreatePrefab("RuntimeObjectPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", tilePrefab) });
                ConfigureObjectCatalog(objectCatalog, new RuntimeMapObjectPrefabCatalog.Entry("bench-a", objectPrefab));
                view.ConfigureForTests(catalog, heightStep: 0.5f, mapObjectCatalogSet: objectCatalog);
                var coord = new HexCoord(1, 0);
                var map = new HexMapData(
                    new[] { Cell(coord, "atlas-test", heightLevel: 2) },
                    objectRefs: new[]
                    {
                        new HexMapObjectData("building-a", "Building", "bench-a", coord, rotationSteps: 3, visualScaleX: 1.5f, visualScaleY: 2f, visualScaleZ: 0.5f)
                    });

                view.Render(map);

                Assert.That(view.MapObjectVisualCount, Is.EqualTo(1));
                var objectVisual = FindChild(root.transform, "Map_Object_1_0_building-a_RuntimeObjectPrefab");
                Assert.That(objectVisual, Is.Not.Null);
                Assert.That(objectVisual.localPosition.y, Is.GreaterThan(1f));
                Assert.That(Mathf.DeltaAngle(objectVisual.localEulerAngles.y, 180f), Is.EqualTo(0f).Within(0.001f));
                Assert.That(objectVisual.localScale, Is.EqualTo(new Vector3(1.5f, 2f, 0.5f)));
                Assert.That(objectVisual.GetComponent<MapObjectVisualController>(), Is.Not.Null);
                Assert.That(objectVisual.GetComponent<MapObjectFadeTarget>(), Is.Not.Null);
                Assert.That(objectVisual.GetComponent<BoxCollider>(), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                DestroyObjectCatalog(objectCatalog);
                Object.DestroyImmediate(tilePrefab);
                Object.DestroyImmediate(objectPrefab);
            }
        }

        [Test]
        public void ApplyVisibilityShowsBuildingsInUnknownAndAllDiscoveredObjectsFromHinted()
        {
            var root = new GameObject("AtlasObjectVisibilityTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
            var tilePrefab = CreatePrefab("AtlasObjectVisibilityTopPrefab");
            var treePrefab = CreatePrefab("RuntimeTreePrefab");
            var chestPrefab = CreatePrefab("RuntimeChestPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                var coord = new HexCoord(1, 0);
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", tilePrefab) });
                ConfigureObjectCatalog(objectCatalog,
                    new RuntimeMapObjectPrefabCatalog.Entry("tree-a", treePrefab),
                    new RuntimeMapObjectPrefabCatalog.Entry("chest-a", chestPrefab));
                view.ConfigureForTests(catalog, mapObjectCatalogSet: objectCatalog);
                view.Render(new HexMapData(
                    new[] { Cell(coord, "atlas-test") },
                    objectRefs: new[]
                    {
                        new HexMapObjectData("tree-a", "Building", "tree-a", coord, interactable: true),
                        new HexMapObjectData("chest-a", "TreasureChest", "chest-a", coord, interactable: true)
                    }));

                var tree = FindChild(root.transform, "Map_Object_1_0_tree-a_RuntimeTreePrefab");
                var chest = FindChild(root.transform, "Map_Object_1_0_chest-a_RuntimeChestPrefab");
                var treeBlock = new MaterialPropertyBlock();
                tree.GetComponent<Renderer>().GetPropertyBlock(treeBlock);
                var normalTreeColor = treeBlock.GetColor("_Color");

                view.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Unknown(coord));

                Assert.That(view.ActiveMapObjectVisualCount, Is.EqualTo(1));
                Assert.That(tree.gameObject.activeSelf, Is.True);
                Assert.That(chest.gameObject.activeSelf, Is.False);
                tree.GetComponent<Renderer>().GetPropertyBlock(treeBlock);
                Assert.That(treeBlock.GetColor("_Color").r, Is.LessThan(normalTreeColor.r), "Unknown visible building objects should be darkened like hinted objects.");

                view.ApplyVisibility(_ => new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Hinted, true, true, false, string.Empty, "street", 1, true, false, string.Empty, string.Empty));

                // Hinted = previously explored. Every discovered object stays visible through the fog,
                // including interactables (the chest), darkened to match the fogged tiles.
                Assert.That(view.ActiveMapObjectVisualCount, Is.EqualTo(2));
                Assert.That(tree.gameObject.activeSelf, Is.True);
                Assert.That(chest.gameObject.activeSelf, Is.True);
                tree.GetComponent<Renderer>().GetPropertyBlock(treeBlock);
                Assert.That(treeBlock.GetColor("_Color").r, Is.LessThan(normalTreeColor.r), "Hinted visible map objects should be darkened.");

                view.ApplyVisibility(_ => new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Revealed, true, true, true, "tile", "street", 1, true, false, string.Empty, string.Empty));

                Assert.That(view.ActiveMapObjectVisualCount, Is.EqualTo(2));
                tree.GetComponent<Renderer>().GetPropertyBlock(treeBlock);
                Assert.That(treeBlock.GetColor("_Color").r, Is.EqualTo(normalTreeColor.r).Within(0.001f), "Revealed map objects should restore their normal tint.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                DestroyObjectCatalog(objectCatalog);
                Object.DestroyImmediate(tilePrefab);
                Object.DestroyImmediate(treePrefab);
                Object.DestroyImmediate(chestPrefab);
            }
        }

        [Test]
        public void ApplyVisibilityKeepsMemoryStoneVisibleAndUndarkenedInUnknown()
        {
            var root = new GameObject("AtlasMemoryStoneVisibilityTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
            var tilePrefab = CreatePrefab("AtlasMemoryStoneTopPrefab");
            var memoryStonePrefab = CreatePrefab("RuntimeMemoryStonePrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                var coord = new HexCoord(1, 0);
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", tilePrefab) });
                ConfigureObjectCatalog(objectCatalog, new RuntimeMapObjectPrefabCatalog.Entry("memorystone-a", memoryStonePrefab));
                view.ConfigureForTests(catalog, mapObjectCatalogSet: objectCatalog);
                view.Render(new HexMapData(
                    new[] { Cell(coord, "atlas-test") },
                    objectRefs: new[]
                    {
                        new HexMapObjectData("memory-a", "MemoryStone", "memorystone-a", coord, interactable: true)
                    }));

                var memoryStone = FindChild(root.transform, "Map_Object_1_0_memory-a_RuntimeMemoryStonePrefab");
                Assert.That(memoryStone, Is.Not.Null);

                var block = new MaterialPropertyBlock();
                memoryStone.GetComponent<Renderer>().GetPropertyBlock(block);
                var normalColor = block.GetColor("_Color");

                view.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Unknown(coord));

                Assert.That(view.ActiveMapObjectVisualCount, Is.EqualTo(1));
                Assert.That(memoryStone.gameObject.activeSelf, Is.True);
                memoryStone.GetComponent<Renderer>().GetPropertyBlock(block);
                Assert.That(block.GetColor("_Color").r, Is.EqualTo(normalColor.r).Within(0.001f));
                Assert.That(FindChild(memoryStone, "MemoryStone Always Visible Highlight"), Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                DestroyObjectCatalog(objectCatalog);
                Object.DestroyImmediate(tilePrefab);
                Object.DestroyImmediate(memoryStonePrefab);
            }
        }

        [Test]
        public void RenderNullClearsGeneratedChildrenAfterDictionaryStateIsLost()
        {
            var root = new GameObject("AtlasStalePreviewCleanupTest");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                new GameObject("Atlas_Top_0_0_Stale").transform.SetParent(root.transform, false);
                new GameObject("Atlas_Side_0_0_E_0").transform.SetParent(root.transform, false);
                new GameObject("Atlas Player Marker").transform.SetParent(root.transform, false);
                new GameObject("Designer Authored Child").transform.SetParent(root.transform, false);

                view.Render((HexMapData)null);

                Assert.That(FindChild(root.transform, "Atlas_Top_0_0_Stale"), Is.Null);
                Assert.That(FindChild(root.transform, "Atlas_Side_0_0_E_0"), Is.Null);
                Assert.That(FindChild(root.transform, "Atlas Player Marker"), Is.Null);
                Assert.That(FindChild(root.transform, "Designer Authored Child"), Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ProjectUsesLegacyFlatTopHexLayoutConvention()
        {
            var root = new GameObject("AtlasProjectionTest");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                view.ConfigureForTests(tileRadius: 1f, tileSpacing: 1f);

                var projected = view.Project(new HexCoord(1, 1));

                Assert.That(projected.x, Is.EqualTo(Mathf.Sqrt(3f) * 1.5f).Within(0.001f));
                Assert.That(projected.z, Is.EqualTo(-1.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RaycastAgainstProjectedTopResolvesExpectedHex()
        {
            var root = new GameObject("AtlasRaycastTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasRaycastPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f);
                var destination = new HexCoord(0, 1);
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test"),
                    Cell(destination, "atlas-test", heightLevel: 2)
                }));
                Physics.SyncTransforms();
                var projected = root.transform.TransformPoint(view.ProjectTop(destination));
                var ray = new Ray(projected + Vector3.up * 3f + Vector3.forward * 0.02f, Vector3.down);

                Assert.That(view.TryRaycastHex(ray, out var coord), Is.True);
                Assert.That(coord, Is.EqualTo(destination));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void RaycastIgnoresPhysicsCollidersInRayPath()
        {
            var root = new GameObject("AtlasRaycastProjectionOnlyTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasRaycastProjectionOnlyPrefab");
            var strayColliderObject = new GameObject("Stray Physics Collider", typeof(BoxCollider));
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f);
                var projectedCoord = new HexCoord(0, 0);
                view.Render(new HexMapData(new[]
                {
                    Cell(projectedCoord, "atlas-test"),
                    Cell(new HexCoord(3, -1), "atlas-test")
                }));

                strayColliderObject.transform.position = view.transform.TransformPoint(view.ProjectTop(projectedCoord)) + Vector3.up * 1.5f;
                strayColliderObject.GetComponent<BoxCollider>().size = Vector3.one * 0.5f;
                Physics.SyncTransforms();

                var ray = new Ray(strayColliderObject.transform.position + Vector3.up * 3f, Vector3.down);

                Assert.That(view.TryRaycastHex(ray, out var coord), Is.True);
                Assert.That(coord, Is.EqualTo(projectedCoord),
                    "Hex picking is pure math projection; physics colliders in the ray path must not affect the result.");
            }
            finally
            {
                Object.DestroyImmediate(strayColliderObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void MissingCatalogOrAtlasVisualIdDoesNotRenderFallbackTop()
        {
            var root = new GameObject("AtlasStrictMissingTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasStrictPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), string.Empty) }));
                Assert.That(view.TopVisualCount, Is.EqualTo(0));
                Assert.That(view.MissingVisualDiagnostics.Single(), Does.Contain("no catalog"));

                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-known", prefab) });
                view.ConfigureForTests(catalog);
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-missing") }));
                Assert.That(view.TopVisualCount, Is.EqualTo(0));
                Assert.That(view.MissingVisualDiagnostics.Single(), Does.Contain("atlas-missing"));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void GameplayPresentationFeaturesUseHeightAwareTopCoordinates()
        {
            var root = new GameObject("AtlasGameplayPresentationTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasGameplayTopPrefab");
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f);
                var destination = new HexCoord(1, 0);
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test"),
                    Cell(destination, "atlas-test", heightLevel: 2)
                }));

                view.SetPlayerPosition(destination);
                view.ApplyVisibility(coord => HexVisibilitySafeCellInfo.Missing(coord));

                AssertVector3(view.ProjectOverlaySurface(destination) + Vector3.up * 0.32f, view.PlayerMarkerLocalPosition);
                Assert.That(view.VisibilityRefreshCount, Is.EqualTo(1));
                Assert.That(view.LastVisibilityRefreshWasNoOp, Is.False);
                Assert.That(view.VisibilityMissingCount, Is.EqualTo(2));
                Assert.That(view.SideVisualsDeferred, Is.False);
                Assert.That(view.SideVisualCount, Is.EqualTo(12));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void VisibilityOverlaysAreCreatedLazilyOnApply()
        {
            WithStrictView("AtlasLazyVisibilityOverlayTest", view =>
            {
                var first = new HexCoord(0, 0);
                var second = new HexCoord(1, 0);
                view.Render(new HexMapData(new[]
                {
                    Cell(first, "atlas-test"),
                    Cell(second, "atlas-test")
                }));

                var firstTop = FindChild(view.transform, "Atlas_Top_0_0_AtlasStrictTopPrefab");
                Assert.That(firstTop.Find("Visibility"), Is.Null, "Visibility overlay should not be allocated during initial render.");
                Assert.That(view.GetComponentsInChildren<Renderer>(includeInactive: true).Length, Is.EqualTo(2));

                view.ApplyVisibility(coord => coord == first
                    ? HexVisibilitySafeCellInfo.Unknown(coord)
                    : new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Revealed, true, true, true, "tile", "street", 1, true, false, string.Empty, string.Empty));

                Assert.That(firstTop.Find("Visibility"), Is.Not.Null, "Unknown cell allocates its fog overlay lazily on the first visibility pass.");
                Assert.That(firstTop.Find("Visibility").localPosition.y, Is.GreaterThan(0.5f), "Visibility overlay should sit above the cube top surface.");
                var secondTop = FindChild(view.transform, "Atlas_Top_1_0_AtlasStrictTopPrefab");
                Assert.That(secondTop.Find("Visibility"), Is.Null, "Revealed cells do not allocate a fog overlay.");
            });
        }

        [Test]
        public void HeightZeroCellRendersTopOnlyWithoutSideVisuals()
        {
            WithStrictView("AtlasHeightZeroSideTest", view =>
            {
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-test", heightLevel: 0) }));

                Assert.That(view.TopVisualCount, Is.EqualTo(1));
                Assert.That(view.SideVisualCount, Is.EqualTo(0));
                Assert.That(view.SideVisualsDeferred, Is.False);
            });
        }

        [Test]
        public void NegativeHeightCellRendersBelowGroundWithoutExteriorSideVisuals()
        {
            WithStrictView("AtlasNegativeHeightSideTest", view =>
            {
                view.ConfigureForTests(viewCatalog, heightStep: 0.5f, useTopChunkMeshes: false);
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-test", heightLevel: -1) }));

                Assert.That(view.TopVisualCount, Is.EqualTo(1));
                Assert.That(view.SideVisualCount, Is.EqualTo(0));
                var lowered = FindChild(view.transform, "Atlas_Top_0_0_AtlasStrictTopPrefab");
                Assert.That(lowered, Is.Not.Null);
                Assert.That(lowered.transform.localPosition.y, Is.EqualTo(-0.5f).Within(0.001f));
            });
        }

        [Test]
        public void SingleRaisedCellCreatesSixGeneratedSideVisuals()
        {
            WithStrictView("AtlasSingleRaisedSideTest", view =>
            {
                view.ConfigureForTests(viewCatalog, heightStep: 0.5f);
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-test", heightLevel: 1) }));

                Assert.That(view.SideVisualCount, Is.EqualTo(6));
                Assert.That(view.SideVisualsDeferred, Is.False);
                var chunk = FindChild(view.transform, "Atlas_SideChunk_Generated");
                Assert.That(chunk, Is.Not.Null);
                Assert.That(chunk.GetComponent<Collider>(), Is.Null);
                var mesh = chunk.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(mesh.vertexCount, Is.EqualTo(24));
                Assert.That(mesh.GetIndexCount(0), Is.EqualTo(72));
            });
        }

        [Test]
        public void GroundNextToNegativeHeightCreatesOneSharedSideSegment()
        {
            WithStrictView("AtlasNegativeTransitionSideTest", view =>
            {
                view.ConfigureForTests(viewCatalog, tileRadius: 1f, tileSpacing: 1f, heightStep: 0.5f, generatedSideRadiusScale: 1f);
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test", heightLevel: 0),
                    Cell(new HexCoord(1, 0), "atlas-test", heightLevel: -1)
                }));

                Assert.That(view.SideVisualCount, Is.EqualTo(1));
                var chunk = FindChild(view.transform, "Atlas_SideChunk_Generated");
                Assert.That(chunk, Is.Not.Null);
                var vertices = chunk.GetComponent<MeshFilter>().sharedMesh.vertices;
                var sharedEdgeCenter = (vertices[0] + vertices[1] + vertices[2] + vertices[3]) * 0.25f;
                Assert.That(sharedEdgeCenter.x, Is.EqualTo(0.8660254f).Within(0.001f));
                Assert.That(sharedEdgeCenter.z, Is.EqualTo(0f).Within(0.001f));
                Assert.That(sharedEdgeCenter.y, Is.EqualTo(-0.25f).Within(0.001f));
            });
        }

        [Test]
        public void MaximumToNegativeHeightTransitionCreatesFullExposedDifference()
        {
            WithStrictView("AtlasMaximumNegativeTransitionSideTest", view =>
            {
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test", heightLevel: 3),
                    Cell(new HexCoord(1, 0), "atlas-test", heightLevel: -1)
                }));

                Assert.That(view.SideVisualCount, Is.EqualTo(19));
            });
        }

        private static AtlasTileCatalog viewCatalog;

        [Test]
        public void GeneratedSideVisualsAlignToHexEdges()
        {
            WithStrictView("AtlasSideAlignmentTest", view =>
            {
                view.ConfigureForTests(viewCatalog, tileRadius: 1f, tileSpacing: 1f, heightStep: 0.5f, generatedSideRadiusScale: 1f, generatedSideCornerOverlap: 0.02f);
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-test", heightLevel: 1) }));

                var chunk = FindChild(view.transform, "Atlas_SideChunk_Generated");
                Assert.That(chunk, Is.Not.Null);
                var vertices = chunk.GetComponent<MeshFilter>().sharedMesh.vertices;
                var eastCenter = (vertices[0] + vertices[1] + vertices[2] + vertices[3]) * 0.25f;
                var northEastCenter = (vertices[4] + vertices[5] + vertices[6] + vertices[7]) * 0.25f;

                Assert.That(eastCenter.x, Is.EqualTo(0.8660254f).Within(0.001f));
                Assert.That(eastCenter.z, Is.EqualTo(0f).Within(0.001f));
                Assert.That(eastCenter.y, Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(vertices[0].x, Is.EqualTo(0.8660254f).Within(0.001f));
                Assert.That(vertices[0].z, Is.EqualTo(-0.52f).Within(0.001f));
                Assert.That(vertices[3].x, Is.EqualTo(0.8660254f).Within(0.001f));
                Assert.That(vertices[3].z, Is.EqualTo(0.52f).Within(0.001f));

                Assert.That(northEastCenter.x, Is.EqualTo(0.4330127f).Within(0.001f));
                Assert.That(northEastCenter.z, Is.EqualTo(0.75f).Within(0.001f));
                Assert.That(northEastCenter.y, Is.EqualTo(0.25f).Within(0.001f));

                var eastWidth = Vector3.Distance(vertices[0], vertices[3]);
                Assert.That(eastWidth, Is.EqualTo(1.04f).Within(0.001f));
                var uvs = chunk.GetComponent<MeshFilter>().sharedMesh.uv;
                Assert.That(uvs, Has.Length.EqualTo(vertices.Length));
                Assert.That(uvs[0], Is.EqualTo(new Vector2(0f, 0f)));
                Assert.That(uvs[1], Is.EqualTo(new Vector2(0f, 1f)));
                Assert.That(uvs[2], Is.EqualTo(new Vector2(1f, 1f)));
                Assert.That(uvs[3], Is.EqualTo(new Vector2(1f, 0f)));
            });
        }

        [Test]
        public void GeneratedSideVisualsUsePerTileSideMaterialsAsChunkSubmeshes()
        {
            var root = new GameObject("AtlasPerTileSideMaterialTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefabA = CreatePrefab("AtlasSideMaterialPrefabA");
            var prefabB = CreatePrefab("AtlasSideMaterialPrefabB");
            var materialA = AtlasTilePresentationView.CreateRuntimeMaterialForTests("SideMaterialA", Color.red);
            var materialB = AtlasTilePresentationView.CreateRuntimeMaterialForTests("SideMaterialB", Color.blue);
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[]
                {
                    new AtlasTileCatalog.Entry("atlas-side-a", prefabA, sideMaterial: materialA),
                    new AtlasTileCatalog.Entry("atlas-side-b", prefabB, sideMaterial: materialB)
                });
                view.ConfigureForTests(catalog);
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-side-a", heightLevel: 1),
                    Cell(new HexCoord(3, 0), "atlas-side-b", heightLevel: 1)
                }));

                var chunk = FindChild(root.transform, "Atlas_SideChunk_Generated");
                Assert.That(chunk, Is.Not.Null);
                var renderer = chunk.GetComponent<MeshRenderer>();
                var mesh = chunk.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(renderer.sharedMaterials, Is.EqualTo(new[] { materialA, materialB }));
                Assert.That(mesh.subMeshCount, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefabA);
                Object.DestroyImmediate(prefabB);
                Object.DestroyImmediate(materialA);
                Object.DestroyImmediate(materialB);
            }
        }

        [Test]
        public void GeneratedSideVisualsFallbackMaterialUsesDarkenedTopColorAndOptionalSideTexture()
        {
            var root = new GameObject("AtlasFallbackSideMaterialTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasFallbackSidePrefab");
            var topMaterial = AtlasTilePresentationView.CreateRuntimeMaterialForTests("TopMaterial", new Color(0.8f, 0.5f, 0.25f, 1f));
            var texture = new Texture2D(1, 1);
            try
            {
                prefab.GetComponent<Renderer>().sharedMaterial = topMaterial;
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[]
                {
                    new AtlasTileCatalog.Entry("atlas-side-fallback", prefab, sideTexture: texture)
                });
                view.ConfigureForTests(catalog);
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-side-fallback", heightLevel: 1) }));

                var chunk = FindChild(root.transform, "Atlas_SideChunk_Generated");
                Assert.That(chunk, Is.Not.Null);
                var sideMaterial = chunk.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(sideMaterial.mainTexture, Is.SameAs(texture));
                Assert.That(sideMaterial.color.r, Is.EqualTo(0.8f * 0.72f).Within(0.001f));
                Assert.That(sideMaterial.color.g, Is.EqualTo(0.5f * 0.72f).Within(0.001f));
                Assert.That(sideMaterial.color.b, Is.EqualTo(0.25f * 0.72f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(topMaterial);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void GeneratedSideVisualsFallbackMaterialCanTuneTopBasedTextureTint()
        {
            var root = new GameObject("AtlasFallbackSideTintTuningTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasFallbackSideTintPrefab");
            var topMaterial = AtlasTilePresentationView.CreateRuntimeMaterialForTests("TopTintSourceMaterial", new Color(0.8f, 0.5f, 0.25f, 1f));
            var topTexture = new Texture2D(1, 1);
            try
            {
                topMaterial.mainTexture = topTexture;
                prefab.GetComponent<Renderer>().sharedMaterial = topMaterial;
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[]
                {
                    new AtlasTileCatalog.Entry("atlas-side-tint-tuning", prefab)
                });
                view.ConfigureForTests(
                    catalog,
                    generatedSideTopColorTint: new Color(0.5f, 1f, 0.25f, 1f),
                    generatedSideTopColorMultiplier: 0.5f);
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-side-tint-tuning", heightLevel: 1) }));

                var chunk = FindChild(root.transform, "Atlas_SideChunk_Generated");
                Assert.That(chunk, Is.Not.Null);
                var sideMaterial = chunk.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(sideMaterial.mainTexture, Is.SameAs(topTexture));
                Assert.That(sideMaterial.color.r, Is.EqualTo(0.8f * 0.5f * 0.5f).Within(0.001f));
                Assert.That(sideMaterial.color.g, Is.EqualTo(0.5f * 1f * 0.5f).Within(0.001f));
                Assert.That(sideMaterial.color.b, Is.EqualTo(0.25f * 0.25f * 0.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(topMaterial);
                Object.DestroyImmediate(topTexture);
            }
        }

        [Test]
        public void GeneratedSideVisualsFallbackMaterialReusesTopTextureWhenSideTextureIsUnset()
        {
            var root = new GameObject("AtlasTopTextureSideMaterialTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasTopTextureSidePrefab");
            var topMaterial = AtlasTilePresentationView.CreateRuntimeMaterialForTests("TopTexturedMaterial", Color.white);
            var topTexture = new Texture2D(1, 1);
            try
            {
                topMaterial.mainTexture = topTexture;
                prefab.GetComponent<Renderer>().sharedMaterial = topMaterial;
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[]
                {
                    new AtlasTileCatalog.Entry("atlas-top-texture-side-fallback", prefab)
                });
                view.ConfigureForTests(catalog);
                view.Render(new HexMapData(new[] { Cell(new HexCoord(0, 0), "atlas-top-texture-side-fallback", heightLevel: 1) }));

                var chunk = FindChild(root.transform, "Atlas_SideChunk_Generated");
                Assert.That(chunk, Is.Not.Null);
                var sideMaterial = chunk.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(sideMaterial.mainTexture, Is.SameAs(topTexture));
                Assert.That(sideMaterial.color.r, Is.EqualTo(0.72f).Within(0.001f));
                Assert.That(sideMaterial.color.g, Is.EqualTo(0.72f).Within(0.001f));
                Assert.That(sideMaterial.color.b, Is.EqualTo(0.72f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(topMaterial);
                Object.DestroyImmediate(topTexture);
            }
        }

        [Test]
        public void EqualHeightAdjacencyDoesNotCreateInternalDuplicateSides()
        {
            WithStrictView("AtlasEqualHeightSideTest", view =>
            {
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test", heightLevel: 1),
                    Cell(new HexCoord(1, 0), "atlas-test", heightLevel: 1)
                }));

                Assert.That(view.SideVisualCount, Is.EqualTo(10));
            });
        }

        [Test]
        public void HeightTransitionCreatesOnlyExposedDifferenceOnSharedEdge()
        {
            WithStrictView("AtlasHeightTransitionSideTest", view =>
            {
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test", heightLevel: 2),
                    Cell(new HexCoord(1, 0), "atlas-test", heightLevel: 1)
                }));

                Assert.That(view.SideVisualCount, Is.EqualTo(16));
                var chunk = FindChild(view.transform, "Atlas_SideChunk_Generated");
                Assert.That(chunk, Is.Not.Null);
                Assert.That(chunk.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.EqualTo(64));
            });
        }

        [Test]
        public void ApplyVisibilityUpdatesCountersAndPerCellOverlayState()
        {
            WithStrictView("AtlasVisibilityStateTest", view =>
            {
                var hidden = new HexCoord(0, 0);
                var explored = new HexCoord(1, 0);
                var visible = new HexCoord(0, 1);
                view.Render(new HexMapData(new[]
                {
                    Cell(hidden, "atlas-test"),
                    Cell(explored, "atlas-test"),
                    Cell(visible, "atlas-test")
                }));

                view.ApplyVisibility(coord =>
                {
                    if (coord == explored)
                    {
                        return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Hinted, true, true, false, "tile-b", "street", 1, true, false, string.Empty, string.Empty);
                    }

                    if (coord == visible)
                    {
                        return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Revealed, true, true, true, "tile-c", "street", 1, true, false, string.Empty, string.Empty);
                    }

                    return HexVisibilitySafeCellInfo.Unknown(coord);
                });

                Assert.That(view.VisibilityRefreshCount, Is.EqualTo(1));
                Assert.That(view.LastVisibilityRefreshWasNoOp, Is.False);
                Assert.That(view.VisibilityHiddenCount, Is.EqualTo(1));
                Assert.That(view.VisibilityExploredCount, Is.EqualTo(1));
                Assert.That(view.VisibilityVisibleCount, Is.EqualTo(1));
                Assert.That(view.VisibilityMissingCount, Is.EqualTo(0));
                Assert.That(FindChild(view.transform, "Atlas_Top_0_0_AtlasStrictTopPrefab").Find("Visibility").gameObject.activeSelf, Is.True);
                Assert.That(FindChild(view.transform, "Atlas_Top_1_0_AtlasStrictTopPrefab").Find("Visibility").gameObject.activeSelf, Is.True);
                Assert.That(FindChild(view.transform, "Atlas_Top_0_1_AtlasStrictTopPrefab").Find("Visibility"), Is.Null, "Revealed cells do not need a visibility overlay allocation.");
            });
        }


        [Test]
        public void VisibilityOverlayDoesNotCastOrReceiveShadows()
        {
            WithStrictView("AtlasOverlayShadowStateTest", view =>
            {
                var coord = new HexCoord(0, 0);
                view.Render(new HexMapData(new[] { Cell(coord, "atlas-test") }));
                view.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Unknown(coord));

                var top = FindChild(view.transform, "Atlas_Top_0_0_AtlasStrictTopPrefab");
                var visibility = top.Find("Visibility").GetComponent<Renderer>();

                AssertOverlayDoesNotAffectLighting(visibility);
            });
        }

        [Test]
        public void VisibilityOverlayStaysTileSizedWhenTopPrefabHasLargeScale()
        {
            var root = new GameObject("AtlasScaledOverlayTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasScaledTopPrefab");
            prefab.transform.localScale = Vector3.one * 100f;
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-scaled", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f);
                var coord = new HexCoord(0, 0);

                view.Render(new HexMapData(new[] { Cell(coord, "atlas-scaled") }));
                view.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Unknown(coord));

                var top = FindChild(root.transform, "Atlas_Top_0_0_AtlasScaledTopPrefab");
                Assert.That(top, Is.Not.Null);
                Assert.That(top.localScale.x, Is.EqualTo(100f).Within(0.001f));

                Assert.That(top.Find("Visibility").GetComponent<Renderer>().bounds.size.x, Is.LessThan(2f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ApplyVisibilityWritesMaterialFogStatePerTileRenderer()
        {
            WithStrictView("AtlasMaterialFogStateTest", view =>
            {
                var hidden = new HexCoord(0, 0);
                var hinted = new HexCoord(1, 0);
                var revealed = new HexCoord(0, 1);
                view.Render(new HexMapData(new[]
                {
                    Cell(hidden, "atlas-test"),
                    Cell(hinted, "atlas-test"),
                    Cell(revealed, "atlas-test")
                }));

                view.ApplyVisibility(coord =>
                {
                    if (coord == hinted)
                    {
                        return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Hinted, true, true, false, "tile-b", "street", 1, true, false, string.Empty, string.Empty);
                    }

                    if (coord == revealed)
                    {
                        return new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Revealed, true, true, true, "tile-c", "street", 1, true, false, string.Empty, string.Empty);
                    }

                    return HexVisibilitySafeCellInfo.Unknown(coord);
                });

                Assert.That(view.GetLastFogAmount(hidden), Is.GreaterThan(view.GetLastFogAmount(hinted)));
                Assert.That(view.GetLastFogAmount(hinted), Is.GreaterThan(0f));
                Assert.That(view.GetLastFogAmount(revealed), Is.EqualTo(0f));
                Assert.That(view.FoggedRendererCount, Is.EqualTo(2));

                var hiddenRenderer = FindChild(view.transform, "Atlas_Top_0_0_AtlasStrictTopPrefab").GetComponent<Renderer>();
                var block = new MaterialPropertyBlock();
                hiddenRenderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat(Shader.PropertyToID("_FogAmount")), Is.EqualTo(view.GetLastFogAmount(hidden)).Within(0.001f));
            });
        }


        [Test]
        public void RaycastHexUsesMathProjectionWithoutGeneratedTopColliders()
        {
            var root = new GameObject("AtlasMathPickingTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasNoColliderTopPrefab");
            var prefabCollider = prefab.GetComponent<Collider>();
            if (prefabCollider != null)
            {
                Object.DestroyImmediate(prefabCollider);
            }

            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f);
                var destination = new HexCoord(1, 0);
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test"),
                    Cell(destination, "atlas-test", heightLevel: 2)
                }));

                Assert.That(root.transform.Cast<Transform>().Any(child => child.name.StartsWith("Atlas_ClickCollider_")), Is.False);
                Physics.SyncTransforms();
                var projected = view.transform.TransformPoint(view.ProjectTop(destination));
                var ray = new Ray(projected + Vector3.up * 3f + Vector3.forward * 0.02f, Vector3.down);

                Assert.That(view.TryRaycastHex(ray, out var coord), Is.True);
                Assert.That(coord, Is.EqualTo(destination));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void RaycastRemainsValidAfterVisibility()
        {
            WithStrictView("AtlasVisibilityRaycastTest", view =>
            {
                var destination = new HexCoord(0, 1);
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0), "atlas-test"),
                    Cell(destination, "atlas-test")
                }));

                view.ApplyVisibility(coord => new HexVisibilitySafeCellInfo(coord, HexCellVisibility.Revealed, true, true, true, "tile", "street", 1, true, false, string.Empty, string.Empty));
                Physics.SyncTransforms();
                var projected = view.transform.TransformPoint(view.ProjectTop(destination));
                var ray = new Ray(projected + Vector3.up * 3f + Vector3.forward * 0.02f, Vector3.down);

                Assert.That(view.TryRaycastHex(ray, out var coord), Is.True);
                Assert.That(coord, Is.EqualTo(destination));
            });
        }

        [Test]
        public void MapObjectFadeKeepsOcclusionFadeAfterHoverExit()
        {
            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            try
            {
                material.color = Color.white;
                targetObject.GetComponent<Renderer>().sharedMaterial = material;
                var fadeTarget = targetObject.AddComponent<MapObjectFadeTarget>();
                fadeTarget.SetPointerHoveredForTests(true);
                fadeTarget.ApplyFade(true);

                fadeTarget.SetPointerHoveredForTests(false);

                var propertyBlock = new MaterialPropertyBlock();
                var renderer = targetObject.GetComponent<Renderer>();
                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.LessThan(1f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void MapObjectFadeDoesNotLatchHoverAsOcclusionFade()
        {
            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            try
            {
                material.color = Color.white;
                targetObject.GetComponent<Renderer>().sharedMaterial = material;
                var fadeTarget = targetObject.AddComponent<MapObjectFadeTarget>();

                fadeTarget.SetPointerHoveredForTests(true);
                fadeTarget.ApplyFade(false);
                fadeTarget.SetPointerHoveredForTests(false);

                var propertyBlock = new MaterialPropertyBlock();
                var renderer = targetObject.GetComponent<Renderer>();
                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(material);
            }
        }


        [Test]
        public void MapObjectFadeTargetPreparesTransparentMaterialForAlphaFade()
        {
            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            try
            {
                material.color = Color.white;
                targetObject.GetComponent<Renderer>().sharedMaterial = material;
                var fadeTarget = targetObject.AddComponent<MapObjectFadeTarget>();

                fadeTarget.ApplyFade(true);

                var renderer = targetObject.GetComponent<Renderer>();
                var runtimeMaterial = renderer.sharedMaterial;
                Assert.That(runtimeMaterial.renderQueue, Is.EqualTo((int)RenderQueue.Transparent));
                var propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.LessThan(1f));

                fadeTarget.ApplyFade(false);

                Assert.That(renderer.sharedMaterial.renderQueue, Is.EqualTo(material.renderQueue));
                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(material);
            }
        }


        [Test]
        public void MapObjectVisualControllerPropagatesHoverToChildFadeTargets()
        {
            var root = new GameObject("MapObjectRoot");
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            try
            {
                child.transform.SetParent(root.transform, false);
                child.GetComponent<Renderer>().sharedMaterial = material;
                var childFade = child.AddComponent<MapObjectFadeTarget>();
                var controller = MapObjectVisualController.Ensure(root);

                controller.SetPointerHovered(true);

                Assert.That(childFade.IsPointerHovered, Is.True);
                var propertyBlock = new MaterialPropertyBlock();
                child.GetComponent<Renderer>().GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.LessThan(1f));

                controller.SetPointerHovered(false);

                Assert.That(childFade.IsPointerHovered, Is.False);
                child.GetComponent<Renderer>().GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(material);
            }
        }


        [Test]
        public void MapObjectVisualControllerClearsHoverWhenDisabled()
        {
            var root = new GameObject("MapObjectRoot");
            var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            try
            {
                child.transform.SetParent(root.transform, false);
                child.GetComponent<Renderer>().sharedMaterial = material;
                var childFade = child.AddComponent<MapObjectFadeTarget>();
                var controller = MapObjectVisualController.Ensure(root);
                controller.SetPointerHovered(true);

                root.SetActive(false);
                root.SetActive(true);

                Assert.That(childFade.IsPointerHovered, Is.False);
                var propertyBlock = new MaterialPropertyBlock();
                child.GetComponent<Renderer>().GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(material);
            }
        }


        [Test]
        public void RefreshMapObjectHoverRayFadesHoveredObjectAndClearsMiss()
        {
            var root = new GameObject("AtlasObjectHoverTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
            var tilePrefab = CreatePrefab("AtlasHoverTopPrefab");
            var objectPrefab = CreatePrefab("RuntimeHoverObjectPrefab");
            var objectMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            try
            {
                objectPrefab.GetComponent<Renderer>().sharedMaterial = objectMaterial;
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", tilePrefab) });
                ConfigureObjectCatalog(objectCatalog, new RuntimeMapObjectPrefabCatalog.Entry("building-a", objectPrefab));
                view.ConfigureForTests(catalog, mapObjectCatalogSet: objectCatalog);
                var coord = new HexCoord(0, 0);
                view.Render(new HexMapData(
                    new[] { Cell(coord, "atlas-test") },
                    objectRefs: new[] { new HexMapObjectData("building-a", "Building", "building-a", coord) }));
                var objectVisual = FindChild(root.transform, "Map_Object_0_0_building-a_RuntimeHoverObjectPrefab");
                Assert.That(objectVisual, Is.Not.Null);
                var controller = objectVisual.GetComponent<MapObjectVisualController>();
                var renderer = objectVisual.GetComponent<Renderer>();
                Physics.SyncTransforms();

                view.RefreshMapObjectHoverRay(new Ray(objectVisual.position + Vector3.up * 5f, Vector3.down));

                Assert.That(controller.IsPointerHovered, Is.True);
                var propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.LessThan(1f));

                view.RefreshMapObjectHoverRay(new Ray(objectVisual.position + Vector3.right * 20f + Vector3.up * 5f, Vector3.down));

                Assert.That(controller.IsPointerHovered, Is.False);
                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                DestroyObjectCatalog(objectCatalog);
                Object.DestroyImmediate(tilePrefab);
                Object.DestroyImmediate(objectPrefab);
                Object.DestroyImmediate(objectMaterial);
            }
        }

        [Test]
        public void RefreshMapObjectFadeUsesCameraRayBoundsOcclusionForActorTargets()
        {
            var root = new GameObject("AtlasObjectOcclusionTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
            var tilePrefab = CreatePrefab("AtlasOcclusionTopPrefab");
            var objectPrefab = CreatePrefab("RuntimeOccludingObjectPrefab");
            var objectMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var cameraObject = new GameObject("AtlasObjectOcclusionCamera");
            try
            {
                objectPrefab.GetComponent<Renderer>().sharedMaterial = objectMaterial;
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", tilePrefab) });
                ConfigureObjectCatalog(objectCatalog, new RuntimeMapObjectPrefabCatalog.Entry("building-a", objectPrefab));
                view.ConfigureForTests(catalog, mapObjectCatalogSet: objectCatalog);
                var objectCoord = new HexCoord(0, 0);
                view.Render(new HexMapData(
                    new[] { Cell(objectCoord, "atlas-test") },
                    objectRefs: new[] { new HexMapObjectData("building-a", "Building", "building-a", objectCoord) }));

                var objectVisual = FindChild(root.transform, "Map_Object_0_0_building-a_RuntimeOccludingObjectPrefab");
                Assert.That(objectVisual, Is.Not.Null);
                var renderer = objectVisual.GetComponent<Renderer>();
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.transform.position = objectVisual.position + Vector3.back * 10f;
                camera.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
                Physics.SyncTransforms();

                var actorBehindObject = new AtlasTilePresentationView.MapObjectOcclusionTarget(
                    new HexCoord(0, 1),
                    view.transform.InverseTransformPoint(objectVisual.position + Vector3.forward * 5f));
                view.RefreshMapObjectFade(camera, new[] { actorBehindObject });

                var propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.LessThan(1f));

                var actorBesideObject = new AtlasTilePresentationView.MapObjectOcclusionTarget(
                    new HexCoord(1, 0),
                    view.transform.InverseTransformPoint(objectVisual.position + Vector3.right * 5f + Vector3.forward * 5f));
                view.RefreshMapObjectFade(camera, new[] { actorBesideObject });

                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                DestroyObjectCatalog(objectCatalog);
                Object.DestroyImmediate(tilePrefab);
                Object.DestroyImmediate(objectPrefab);
                Object.DestroyImmediate(objectMaterial);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void RefreshMapObjectFadeCanUseAnyActorTargetNotOnlyPlayerCoord()
        {
            var root = new GameObject("AtlasObjectMonsterOcclusionTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var objectCatalog = ScriptableObject.CreateInstance<MapObjectCatalogSet>();
            var tilePrefab = CreatePrefab("AtlasMonsterOcclusionTopPrefab");
            var objectPrefab = CreatePrefab("RuntimeMonsterOccludingObjectPrefab");
            var objectMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            var cameraObject = new GameObject("AtlasObjectMonsterOcclusionCamera");
            try
            {
                objectPrefab.GetComponent<Renderer>().sharedMaterial = objectMaterial;
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", tilePrefab) });
                ConfigureObjectCatalog(objectCatalog, new RuntimeMapObjectPrefabCatalog.Entry("building-a", objectPrefab));
                view.ConfigureForTests(catalog, mapObjectCatalogSet: objectCatalog);
                var objectCoord = new HexCoord(0, 0);
                view.Render(new HexMapData(
                    new[] { Cell(objectCoord, "atlas-test") },
                    objectRefs: new[] { new HexMapObjectData("building-a", "Building", "building-a", objectCoord) }));

                var objectVisual = FindChild(root.transform, "Map_Object_0_0_building-a_RuntimeMonsterOccludingObjectPrefab");
                Assert.That(objectVisual, Is.Not.Null);
                var renderer = objectVisual.GetComponent<Renderer>();
                var camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.transform.position = objectVisual.position + Vector3.left * 10f;
                camera.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                Physics.SyncTransforms();

                var missedPlayer = new AtlasTilePresentationView.MapObjectOcclusionTarget(
                    new HexCoord(4, 0),
                    view.transform.InverseTransformPoint(objectVisual.position + Vector3.forward * 5f));
                var occludedMonster = new AtlasTilePresentationView.MapObjectOcclusionTarget(
                    new HexCoord(1, 0),
                    view.transform.InverseTransformPoint(objectVisual.position + Vector3.right * 5f));
                view.RefreshMapObjectFade(camera, new[] { missedPlayer, occludedMonster });

                var propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock);
                Assert.That(propertyBlock.GetColor("_Color").a, Is.LessThan(1f));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                DestroyObjectCatalog(objectCatalog);
                Object.DestroyImmediate(tilePrefab);
                Object.DestroyImmediate(objectPrefab);
                Object.DestroyImmediate(objectMaterial);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        public void MapObjectFadeTargetRestoresRendererMaterialsOnDestroy()
        {
            var targetObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            try
            {
                var renderer = targetObject.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                var fadeTarget = targetObject.AddComponent<MapObjectFadeTarget>();

                fadeTarget.ApplyFade(true);
                Assert.That(renderer.sharedMaterial, Is.Not.SameAs(material));

                Object.DestroyImmediate(fadeTarget);

                Assert.That(renderer.sharedMaterial, Is.SameAs(material));
            }
            finally
            {
                Object.DestroyImmediate(targetObject);
                Object.DestroyImmediate(material);
            }
        }

        private static void WithStrictView(string name, System.Action<AtlasTilePresentationView> assertion)
        {
            var root = new GameObject(name);
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = CreatePrefab("AtlasStrictTopPrefab");
            try
            {
                viewCatalog = catalog;
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", prefab) });
                view.ConfigureForTests(catalog, heightStep: 0.5f);
                assertion(view);
            }
            finally
            {
                viewCatalog = null;
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        private static HexCellData Cell(HexCoord coord, string atlasVisualId, int heightLevel = 0, int rotationSteps = 0)
        {
            return new HexCellData(coord, "tile", "street", 1, true, false, atlasVisualId: atlasVisualId, heightLevel: heightLevel, rotationSteps: rotationSteps);
        }

        private static GameObject CreateChunkSafePrefab(string name)
        {
            var prefab = CreatePrefab(name);
            var collider = prefab.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            return prefab;
        }

        private sealed class ChunkUnsafeMarker : MonoBehaviour
        {
        }

        private static void ConfigureObjectCatalog(MapObjectCatalogSet set, params RuntimeMapObjectPrefabCatalog.Entry[] entries)
        {
            var typedCatalog = ScriptableObject.CreateInstance<MapObjectTypedCatalog>();
            typedCatalog.ConfigureForTests(HexMapObjectType.Building, entries);
            set.ConfigureForTests(new[] { typedCatalog });
        }

        private static void DestroyObjectCatalog(MapObjectCatalogSet set)
        {
            if (set == null)
            {
                return;
            }

            foreach (var typedCatalog in set.Catalogs)
            {
                Object.DestroyImmediate(typedCatalog);
            }

            Object.DestroyImmediate(set);
        }

        private static GameObject CreatePrefab(string name)
        {
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = name;
            return prefab;
        }

        private static void AssertVector3(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.001f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.001f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.001f));
        }

        private static void AssertColorScaled(Color source, Color actual, float multiplier)
        {
            Assert.That(actual.r, Is.EqualTo(source.r * multiplier).Within(0.001f));
            Assert.That(actual.g, Is.EqualTo(source.g * multiplier).Within(0.001f));
            Assert.That(actual.b, Is.EqualTo(source.b * multiplier).Within(0.001f));
            Assert.That(actual.a, Is.EqualTo(source.a).Within(0.001f));
        }


        private static void AssertOverlayDoesNotAffectLighting(Renderer renderer)
        {
            Assert.That(renderer, Is.Not.Null);
            Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
            Assert.That(renderer.receiveShadows, Is.False);
        }

        private static Transform FindChild(Transform root, string name)
        {
            return root.Cast<Transform>().SingleOrDefault(child => child.name == name);
        }
    }
}
