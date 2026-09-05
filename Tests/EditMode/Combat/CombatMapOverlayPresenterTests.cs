using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatMapOverlayPresenterTests
    {
        [Test]
        public void ApplyPresentationUsesReachableLayerAndClearsAttackRange()
        {
            WithBatchedPresenter(presenter =>
            {
                var first = new HexCoord(0, 0);
                var second = new HexCoord(1, 0);
                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.AttackRange, first),
                    Layer(HexOverlayLayer.PlayerActionRange, first));

                ApplyLayers(presenter, Layer(HexOverlayLayer.Reachable, second));

                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Reachable), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.AttackRange), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Path), Is.Zero);
            });
        }

        [Test]
        public void ApplyPresentationKeepsPreMoveThreatPreview()
        {
            WithBatchedPresenter(presenter =>
            {
                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.Reachable, new HexCoord(0, 0)),
                    Layer(HexOverlayLayer.Path, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.MonsterMoveIntent, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.MonsterAttackIntent, new HexCoord(0, 0), new HexCoord(1, 0)));

                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Reachable), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Path), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.AttackRange), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterMoveIntent), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterAttackIntent), Is.EqualTo(2));
            });
        }

        [Test]
        public void ApplyPresentationCanShowChaseRangeSeparately()
        {
            WithBatchedPresenter(presenter =>
            {
                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.Reachable, new HexCoord(0, 0)),
                    Layer(HexOverlayLayer.MonsterMoveIntent, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.MonsterAttackIntent, new HexCoord(0, 0)),
                    Layer(HexOverlayLayer.MonsterChaseRange, new HexCoord(0, 0), new HexCoord(1, 0)));

                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Reachable), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterMoveIntent), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterAttackIntent), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterChaseRange), Is.EqualTo(2));
            });
        }

        [Test]
        public void ApplyPresentationUsesAttackRangeLayerAndClearsReachable()
        {
            WithBatchedPresenter(presenter =>
            {
                var first = new HexCoord(0, 0);
                var second = new HexCoord(1, 0);
                ApplyLayers(presenter, Layer(HexOverlayLayer.Reachable, first));

                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.AttackRange, second),
                    Layer(HexOverlayLayer.PlayerActionRange, second));

                Assert.That(presenter.GetActiveCount(HexOverlayLayer.AttackRange), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Reachable), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.PlayerActionRange), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterAttackIntent), Is.Zero);
            });
        }


        [Test]
        public void ApplyPresentationUsesPathAndAttackRangeWithoutReachableSelection()
        {
            WithBatchedPresenter(presenter =>
            {
                ApplyLayers(presenter, Layer(HexOverlayLayer.Reachable, new HexCoord(0, 0)));

                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.Path, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.MonsterMoveIntent, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.MonsterAttackIntent, new HexCoord(0, 0), new HexCoord(1, 0)));

                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Reachable), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Path), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.AttackRange), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterMoveIntent), Is.EqualTo(1));
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterAttackIntent), Is.EqualTo(2));
            });
        }

        [Test]
        public void MonsterIntentPreviewClearsLegacyAttackRangeSoColorsStaySemantic()
        {
            WithBatchedPresenter(presenter =>
            {
                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.AttackRange, new HexCoord(0, 0)),
                    Layer(HexOverlayLayer.PlayerActionRange, new HexCoord(0, 0)));

                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.Path, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.MonsterMoveIntent, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.MonsterAttackIntent, new HexCoord(1, 0)));

                Assert.That(presenter.GetActiveCount(HexOverlayLayer.AttackRange), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.PlayerActionRange), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterAttackIntent), Is.EqualTo(1));
            });
        }

        [Test]
        public void NullPresentationClearsReachableAndAttackRange()
        {
            WithBatchedPresenter(presenter =>
            {
                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.Reachable, new HexCoord(0, 0)),
                    Layer(HexOverlayLayer.AttackRange, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.PlayerActionRange, new HexCoord(1, 0)));

                presenter.ApplyPresentation(null);

                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Reachable), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.AttackRange), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.PlayerActionRange), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterMoveIntent), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterAttackIntent), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterChaseRange), Is.Zero);
            });
        }


        [Test]
        public void ApplyPresentationAtomicallyClearsOmittedLayers()
        {
            WithBatchedPresenter(presenter =>
            {
                ApplyLayers(
                    presenter,
                    Layer(HexOverlayLayer.Reachable, new HexCoord(0, 0)),
                    Layer(HexOverlayLayer.MonsterMoveIntent, new HexCoord(1, 0)),
                    Layer(HexOverlayLayer.MonsterAttackIntent, new HexCoord(0, 0)),
                    Layer(HexOverlayLayer.MonsterChaseRange, new HexCoord(1, 0)));

                presenter.ApplyPresentation(new CombatOverlayPresentation(new[]
                {
                    new CombatOverlayLayerState(
                        HexOverlayLayer.PlayerActionRange,
                        new[] { new HexCoord(1, 0) },
                        CombatOverlayTheme.ResolveDefaultStyle(HexOverlayLayer.PlayerActionRange))
                }));

                Assert.That(presenter.GetActiveCount(HexOverlayLayer.Reachable), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterMoveIntent), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterAttackIntent), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.MonsterChaseRange), Is.Zero);
                Assert.That(presenter.GetActiveCount(HexOverlayLayer.PlayerActionRange), Is.EqualTo(1));
            });
        }

        [Test]
        public void BatchedRendererTracksCountsAndAvoidsUnchangedRebuild()
        {
            var root = new GameObject("BatchedRendererTest");
            try
            {
                var renderer = root.AddComponent<BatchedMeshCombatOverlayRenderer>();
                renderer.Configure(new FakeProjector());
                var style = CombatOverlayTheme.ResolveDefaultStyle(HexOverlayLayer.MonsterChaseRange);

                renderer.ShowLayer(HexOverlayLayer.MonsterChaseRange, new[] { new HexCoord(0, 0), new HexCoord(1, 0) }, style);
                var firstRebuildCount = renderer.RebuildCount;
                Assert.That(renderer.GetActiveCount(HexOverlayLayer.MonsterChaseRange), Is.EqualTo(2));

                renderer.ShowLayer(HexOverlayLayer.MonsterChaseRange, new[] { new HexCoord(1, 0), new HexCoord(0, 0) }, style);
                Assert.That(renderer.RebuildCount, Is.EqualTo(firstRebuildCount));

                renderer.ClearLayer(HexOverlayLayer.MonsterChaseRange);
                Assert.That(renderer.GetActiveCount(HexOverlayLayer.MonsterChaseRange), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BatchedRendererInvalidationForcesSameLayerRebuildAfterProjectionChange()
        {
            var root = new GameObject("BatchedRendererInvalidationTest");
            try
            {
                var renderer = root.AddComponent<BatchedMeshCombatOverlayRenderer>();
                var projector = new FakeProjector();
                renderer.Configure(projector);
                var style = CombatOverlayTheme.ResolveDefaultStyle(HexOverlayLayer.Reachable);
                var coords = new[] { new HexCoord(0, 0), new HexCoord(1, 0) };

                renderer.ShowLayer(HexOverlayLayer.Reachable, coords, style);
                var firstRebuildCount = renderer.RebuildCount;

                projector.TopOffset = 0.75f;
                renderer.InvalidateGeometry();
                renderer.ShowLayer(HexOverlayLayer.Reachable, coords, style);

                Assert.That(renderer.RebuildCount, Is.EqualTo(firstRebuildCount + 1));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }




        [Test]
        public void BatchedRendererUsesSharedOrderingForGeometryAndMaterials()
        {
            var root = new GameObject("BatchedRendererOrderingTest");
            try
            {
                var renderer = root.AddComponent<BatchedMeshCombatOverlayRenderer>();
                var projector = new FakeProjector();
                renderer.Configure(projector);
                // AttackRange draws both a fill and a boundary, so it exercises the shared geometry
                // ordering for both channels (MonsterChaseRange is now a border-only layer).
                var style = CombatOverlayTheme.ResolveDefaultStyle(HexOverlayLayer.AttackRange);

                renderer.ShowLayer(HexOverlayLayer.AttackRange, new[] { new HexCoord(0, 0) }, style);

                var fill = root.GetComponentsInChildren<MeshRenderer>(true)
                    .Single(r => r.transform.parent != null && r.transform.parent.name == "CombatOverlay_AttackRange" && r.name == "Fill");
                var boundary = root.GetComponentsInChildren<MeshRenderer>(true)
                    .Single(r => r.transform.parent != null && r.transform.parent.name == "CombatOverlay_AttackRange" && r.name == "Boundary");

                var expectedQueue = HexOverlayRenderOrder.CombatOverlayRenderQueue +
                    CombatOverlayTheme.ResolveRenderPriority(HexOverlayLayer.AttackRange);
                Assert.That(fill.sharedMaterial.renderQueue, Is.EqualTo(expectedQueue));
                Assert.That(boundary.sharedMaterial.renderQueue, Is.EqualTo(expectedQueue));
                Assert.That(fill.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(fill.receiveShadows, Is.False);
                Assert.That(boundary.shadowCastingMode, Is.EqualTo(ShadowCastingMode.Off));
                Assert.That(boundary.receiveShadows, Is.False);
                Assert.That(fill.GetComponent<MeshFilter>().sharedMesh.bounds.center.y, Is.EqualTo(projector.TopOffset + HexOverlayRenderOrder.CombatFillLift).Within(0.001f));
                Assert.That(boundary.GetComponent<MeshFilter>().sharedMesh.bounds.center.y, Is.EqualTo(projector.TopOffset + HexOverlayRenderOrder.CombatBoundaryLift).Within(0.001f));
                Assert.That(fill.GetComponent<MeshFilter>().sharedMesh.bounds.center.y, Is.GreaterThan(projector.TopOffset + HexOverlayRenderOrder.VisibilityFogLift));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BatchedRendererTileFilterDropsRejectedCoords()
        {
            var root = new GameObject("BatchedRendererFilterTest");
            try
            {
                var renderer = root.AddComponent<BatchedMeshCombatOverlayRenderer>();
                renderer.Configure(new FakeProjector());
                var excluded = new HexCoord(1, 0);
                renderer.SetTileFilter(coord => !coord.Equals(excluded));

                var style = CombatOverlayTheme.ResolveDefaultStyle(HexOverlayLayer.Reachable);
                renderer.ShowLayer(HexOverlayLayer.Reachable, new[] { new HexCoord(0, 0), excluded }, style);

                // The rejected coord (e.g. a water tile) is dropped from the layer entirely.
                Assert.That(renderer.GetActiveCount(HexOverlayLayer.Reachable), Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void BatchedRendererSnapsFadeToFullOutsidePlayMode()
        {
            var root = new GameObject("BatchedRendererFadeTest");
            try
            {
                var renderer = root.AddComponent<BatchedMeshCombatOverlayRenderer>();
                renderer.Configure(new FakeProjector());
                var style = CombatOverlayTheme.ResolveDefaultStyle(HexOverlayLayer.AttackRange);

                renderer.ShowLayer(HexOverlayLayer.AttackRange, new[] { new HexCoord(0, 0) }, style);

                var fill = root.GetComponentsInChildren<MeshRenderer>(true)
                    .Single(r => r.transform.parent != null && r.transform.parent.name == "CombatOverlay_AttackRange" && r.name == "Fill");
                Assert.That(fill.sharedMaterial.HasProperty("_Fade"), Is.True, "Overlay shader exposes a _Fade multiplier.");
                // Outside play mode the show/hide fade snaps instantly so edit-mode visuals stay full.
                Assert.That(fill.sharedMaterial.GetFloat("_Fade"), Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ControllerOverlayBackendDefaultsToBatchedWhenProjectorExists()
        {
            WithRenderedView(view =>
            {
                var root = new GameObject("OverlayBackendDefaultTest");
                try
                {
                    var controller = root.AddComponent<MapCombatController>();
                    controller.ConfigureForTests(view, null, null);

                    Assert.That(controller.ActiveOverlayRendererBackendForTests, Is.EqualTo(CombatOverlayRendererBackend.BatchedMesh));
                    Assert.That(view.transform.Find("Batched Combat Overlay Renderer"), Is.Not.Null);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            });
        }


        [Test]
        public void ControllerAtlasBackendValueDegeneratesToBatchedMesh()
        {
            // 2026-07-02 unification (권고안 A): the Atlas enum value survives only for scene
            // serialization compatibility; it must no longer select the Atlas tactical renderer.
            WithRenderedView(view =>
            {
                var root = new GameObject("OverlayBackendExplicitAtlasTest");
                try
                {
                    var controller = root.AddComponent<MapCombatController>();
                    controller.ConfigureOverlayRendererBackendForTests(CombatOverlayRendererBackend.Atlas);
                    controller.ConfigureForTests(view, null, null);

                    Assert.That(controller.ActiveOverlayRendererBackendForTests, Is.EqualTo(CombatOverlayRendererBackend.BatchedMesh));
                    Assert.That(view.transform.Find("Batched Combat Overlay Renderer"), Is.Not.Null);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            });
        }

        [Test]
        public void ControllerAutoBackendUsesBatchedWhenAtlasProjectorExists()
        {
            WithRenderedView(view =>
            {
                var root = new GameObject("OverlayBackendAutoTest");
                try
                {
                    var controller = root.AddComponent<MapCombatController>();
                    controller.ConfigureOverlayRendererBackendForTests(CombatOverlayRendererBackend.Auto);
                    controller.ConfigureForTests(view, null, null);

                    Assert.That(controller.ActiveOverlayRendererBackendForTests, Is.EqualTo(CombatOverlayRendererBackend.BatchedMesh));
                    Assert.That(view.transform.Find("Batched Combat Overlay Renderer"), Is.Not.Null);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            });
        }

        [Test]
        public void ControllerOverlayBackendReconfigureUpdatesActiveRendererAndStatusText()
        {
            WithRenderedView(view =>
            {
                var root = new GameObject("OverlayBackendReconfigureTest");
                try
                {
                    var controller = root.AddComponent<MapCombatController>();
                    controller.ConfigureForTests(view, null, null);
                    Assert.That(controller.ActiveOverlayRendererBackendForTests, Is.EqualTo(CombatOverlayRendererBackend.BatchedMesh));
                    Assert.That(controller.OverlayRendererStatusText, Is.EqualTo("Renderer: BatchedMesh (Auto)"));

                    controller.ConfigureOverlayRendererBackendForTests(CombatOverlayRendererBackend.Atlas);

                    Assert.That(controller.ActiveOverlayRendererBackendForTests, Is.EqualTo(CombatOverlayRendererBackend.BatchedMesh));
                    Assert.That(controller.OverlayRendererStatusText, Is.EqualTo("Renderer: BatchedMesh"));
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }
            });
        }


        [Test]
        public void ControllerWithoutViewKeepsBatchedBackendWithoutAtlasFallback()
        {
            // No Atlas fallback survives the unification: with no presentation view the presenter
            // simply has no renderer (safe no-op) while the backend stays BatchedMesh.
            var root = new GameObject("OverlayBackendFallbackTest");
            try
            {
                var controller = root.AddComponent<MapCombatController>();
                controller.ConfigureOverlayRendererBackendForTests(CombatOverlayRendererBackend.BatchedMesh);
                controller.ConfigureForTests(null, null, null);

                Assert.That(controller.ActiveOverlayRendererBackendForTests, Is.EqualTo(CombatOverlayRendererBackend.BatchedMesh));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RuntimeOverlayMaterialUsesPipelineCompatibleShaderAndColorProperties()
        {
            var color = new Color(0.08f, 0.82f, 0.95f, 0.42f);
            var material = AtlasTilePresentationView.CreateRuntimeMaterialForTests("Overlay Material Test", color);
            try
            {
                Assert.That(material.shader, Is.Not.Null);
                if (Shader.Find("Universal Render Pipeline/Unlit") != null || Shader.Find("Universal Render Pipeline/Lit") != null)
                {
                    Assert.That(material.shader.name, Does.StartWith("Universal Render Pipeline/"));
                }

                if (material.HasProperty("_BaseColor"))
                {
                    AssertColorApproximately(material.GetColor("_BaseColor"), color);
                }

                if (material.HasProperty("_Color"))
                {
                    AssertColorApproximately(material.GetColor("_Color"), color);
                }

                AssertColorApproximately(material.color, color);
                Assert.That(material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent));
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void NullViewIsSafeNoOp()
        {
            var presenter = new CombatMapOverlayPresenter();

            Assert.DoesNotThrow(() => presenter.ApplyVisibility(_ => HexVisibilitySafeCellInfo.Missing(new HexCoord(0, 0))));
            Assert.DoesNotThrow(() => ApplyLayers(presenter, Layer(HexOverlayLayer.Reachable, new HexCoord(0, 0))));
            Assert.DoesNotThrow(() => ApplyLayers(
                presenter,
                Layer(HexOverlayLayer.Path, new HexCoord(1, 0)),
                Layer(HexOverlayLayer.MonsterAttackIntent, new HexCoord(0, 0))));
            Assert.DoesNotThrow(() => presenter.ApplyPresentation(null));
        }

        private static void ApplyLayers(CombatMapOverlayPresenter presenter, params CombatOverlayLayerState[] layers)
        {
            presenter.ApplyPresentation(new CombatOverlayPresentation(layers));
        }

        private static CombatOverlayLayerState Layer(HexOverlayLayer layer, params HexCoord[] coords)
        {
            return new CombatOverlayLayerState(layer, coords, CombatOverlayTheme.ResolveDefaultStyle(layer));
        }

        private static void WithBatchedPresenter(System.Action<CombatMapOverlayPresenter> assertion)
        {
            var root = new GameObject("CombatMapOverlayPresenterBatchedTest");
            try
            {
                var renderer = root.AddComponent<BatchedMeshCombatOverlayRenderer>();
                renderer.Configure(new FakeProjector());
                assertion(new CombatMapOverlayPresenter(renderer));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void WithRenderedView(System.Action<AtlasTilePresentationView> assertion)
        {
            var root = new GameObject("CombatMapOverlayPresenterTest");
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            prefab.name = "CombatOverlayPresenterTopPrefab";
            try
            {
                var view = root.AddComponent<AtlasTilePresentationView>();
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-test", prefab) });
                view.ConfigureForTests(catalog);
                view.Render(new HexMapData(new[]
                {
                    Cell(new HexCoord(0, 0)),
                    Cell(new HexCoord(1, 0))
                }));

                assertion(view);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }



        private sealed class FakeProjector : IHexMapWorldProjector
        {
            public float TopOffset { get; set; } = 0.25f;
            public float TileRadius => 1f;
            public Vector3 Project(HexCoord coord) => new Vector3(coord.Q, 0f, coord.R);
            public Vector3 ProjectTop(HexCoord coord) => Project(coord) + Vector3.up * TopOffset;
        }

        private static void AssertColorApproximately(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }

        private static HexCellData Cell(HexCoord coord)
        {
            return new HexCellData(coord, "tile", "street", 1, true, false, atlasVisualId: "atlas-test");
        }
    }
}

