using NUnit.Framework;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class AtlasTileCatalogTests
    {
        [Test]
        public void ResolveRequiresExplicitAtlasVisualId()
        {
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            try
            {
                var explicitEntry = new AtlasTileCatalog.Entry("atlas-plaza");
                catalog.ConfigureForTests(new[] { explicitEntry });

                var resolution = catalog.Resolve(new HexCellData(new HexCoord(0, 0), "legacy-tile", "legacy-terrain", 1, true, false, atlasVisualId: "atlas-plaza"));

                Assert.That(resolution.Entry, Is.SameAs(explicitEntry));
                Assert.That(resolution.Source, Is.EqualTo(AtlasTileCatalogResolutionSource.AtlasVisualId));
                Assert.That(resolution.IsFallback, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ResolveDoesNotUseLegacyTileOrTerrainIdsWhenAtlasIdMissing()
        {
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            try
            {
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-street") });

                var byTile = catalog.Resolve(new HexCellData(new HexCoord(0, 0), "atlas-street", "missing-terrain", 1, true, false));
                var byTerrain = catalog.Resolve(new HexCellData(new HexCoord(1, 0), "missing-tile", "atlas-street", 1, true, false));

                Assert.That(byTile.Entry, Is.Null);
                Assert.That(byTile.Source, Is.EqualTo(AtlasTileCatalogResolutionSource.MissingAtlasVisualId));
                Assert.That(byTile.IsFallback, Is.False);
                Assert.That(byTerrain.Entry, Is.Null);
                Assert.That(byTerrain.Source, Is.EqualTo(AtlasTileCatalogResolutionSource.MissingAtlasVisualId));
                Assert.That(byTerrain.IsFallback, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ResolveMissingReturnsNoFallbackEntry()
        {
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            try
            {
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-known") });

                var resolution = catalog.Resolve(new HexCellData(new HexCoord(0, 0), "missing-tile", "missing-terrain", 1, true, false));

                Assert.That(resolution.Entry, Is.Null);
                Assert.That(resolution.Source, Is.EqualTo(AtlasTileCatalogResolutionSource.MissingAtlasVisualId));
                Assert.That(resolution.IsFallback, Is.False);
                Assert.That(resolution.HasTopPrefab, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void CurrentTilePrefabCanBeUsedAsAtlasTopPrefab()
        {
            var topPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Tile/g02_base.prefab");
            var entry = new AtlasTileCatalog.Entry("atlas-g02-base", topPrefab);

            Assert.That(topPrefab, Is.Not.Null);
            Assert.That(entry.TopPrefab, Is.SameAs(topPrefab));
            Assert.That(entry.SideVisualsDeferred, Is.True);
        }

        [Test]
        public void ValidateReportsDuplicateAndEmptyAtlasIds()
        {
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            try
            {
                catalog.ConfigureForTests(new[]
                {
                    new AtlasTileCatalog.Entry("atlas-duplicate"),
                    new AtlasTileCatalog.Entry("atlas-duplicate"),
                    new AtlasTileCatalog.Entry(" ")
                });

                var messages = catalog.Validate().ToArray();

                Assert.That(messages.Any(message => message.Contains("duplicate Atlas visual ID 'atlas-duplicate'")), Is.True);
                Assert.That(messages.Any(message => message.Contains("missing an Atlas visual ID")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void SideVisualModesDescribeDeferredGeneratedAndPrefabContracts()
        {
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var material = new Material(Shader.Find("Standard"));
            try
            {
                var deferred = new AtlasTileCatalog.Entry("atlas-deferred");
                var generated = new AtlasTileCatalog.Entry("atlas-generated", sideVisualMode: AtlasSideVisualMode.Generated, sideMaterial: material);
                var prefabBacked = new AtlasTileCatalog.Entry("atlas-prefab", sideVisualMode: AtlasSideVisualMode.Prefab, sidePrefab: prefab);

                Assert.That(deferred.SideVisualMode, Is.EqualTo(AtlasSideVisualMode.Deferred));
                Assert.That(deferred.SideVisualsDeferred, Is.True);
                Assert.That(generated.SideVisualMode, Is.EqualTo(AtlasSideVisualMode.Generated));
                Assert.That(generated.SideVisualsDeferred, Is.False);
                Assert.That(generated.SideMaterial, Is.SameAs(material));
                Assert.That(prefabBacked.SideVisualMode, Is.EqualTo(AtlasSideVisualMode.Prefab));
                Assert.That(prefabBacked.SidePrefab, Is.SameAs(prefab));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void ValidateReportsIncompletePrefabSideContract()
        {
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            try
            {
                catalog.ConfigureForTests(new[]
                {
                    new AtlasTileCatalog.Entry("atlas-prefab-side", sideVisualMode: AtlasSideVisualMode.Prefab)
                });

                var messages = catalog.Validate().ToArray();

                Assert.That(messages.Any(message => message.Contains("uses prefab side visuals but has no side prefab")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ValidateCellReportsUnsupportedRotationPolicy()
        {
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            try
            {
                catalog.ConfigureForTests(new[]
                {
                    new AtlasTileCatalog.Entry("atlas-fixed", supportsRotationSteps: false)
                });
                var cell = new HexCellData(new HexCoord(0, 0), "tile", "terrain", 1, true, false, atlasVisualId: "atlas-fixed", rotationSteps: 2);

                var messages = catalog.ValidateCell(cell).ToArray();

                Assert.That(messages.Any(message => message.Contains("does not support rotation steps")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ValidateCellReportsUnresolvableExplicitAtlasVisualId()
        {
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            try
            {
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-known") });
                var cell = new HexCellData(new HexCoord(0, 0), "tile", "terrain", 1, true, false, atlasVisualId: "atlas-missing");

                var messages = catalog.ValidateCell(cell).ToArray();

                Assert.That(messages.Any(message => message.Contains("has no entry for explicit Atlas visual ID 'atlas-missing'")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
            }
        }
    }
}

