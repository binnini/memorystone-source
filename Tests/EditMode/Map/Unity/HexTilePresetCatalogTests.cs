using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexTilePresetCatalogTests
    {
        [Test]
        public void ResolveCombinesAtlasVisualAndTerrainMetadata()
        {
            var atlas = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var terrain = ScriptableObject.CreateInstance<HexTerrainPalette>();
            var presets = ScriptableObject.CreateInstance<HexTilePresetCatalog>();
            try
            {
                var atlasEntry = new AtlasTileCatalog.Entry("atlas-street");
                var terrainEntry = new HexTerrainPalette.Entry("street", defaultMoveCost: 2, combatDefenseBonus: 1);
                atlas.ConfigureForTests(new[] { atlasEntry });
                terrain.ConfigureForTests(new[] { terrainEntry });
                presets.ConfigureForTests(atlas, terrain, new[]
                {
                    new HexTilePresetCatalog.Entry("street-tile", "atlas-street", "street", heightLevel: 1, rotationSteps: 2, edgeConnectionMask: 3)
                });

                var resolved = presets.Resolve("STREET-TILE");

                Assert.That(resolved.IsResolved, Is.True);
                Assert.That(resolved.Entry.TilePresetId, Is.EqualTo("street-tile"));
                Assert.That(resolved.AtlasEntry, Is.SameAs(atlasEntry));
                Assert.That(resolved.TerrainEntry, Is.SameAs(terrainEntry));

                var authored = resolved.Entry.ToAuthoringCell(new HexCoord(-1, 2));
                Assert.That(authored.Coord, Is.EqualTo(new HexCoord(-1, 2)));
                Assert.That(authored.TilePresetId, Is.EqualTo("street-tile"));
                Assert.That(authored.AtlasVisualId, Is.EqualTo("atlas-street"));
                Assert.That(authored.TerrainTypeId, Is.EqualTo("street"));
                Assert.That(authored.HeightLevel, Is.EqualTo(1));
                Assert.That(authored.RotationSteps, Is.EqualTo(2));
                Assert.That(authored.EdgeConnectionMask, Is.EqualTo(3));
            }
            finally
            {
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(terrain);
                Object.DestroyImmediate(presets);
            }
        }

        [Test]
        public void ResolveReportsMissingAtlasVisualWithoutMutatingAtlasCatalogOwnership()
        {
            var terrain = ScriptableObject.CreateInstance<HexTerrainPalette>();
            var presets = ScriptableObject.CreateInstance<HexTilePresetCatalog>();
            try
            {
                terrain.ConfigureForTests(new[] { new HexTerrainPalette.Entry("street") });
                presets.ConfigureForTests(null, terrain, new[]
                {
                    new HexTilePresetCatalog.Entry("street-tile", "missing-atlas", "street")
                });

                var resolved = presets.Resolve("street-tile");

                Assert.That(resolved.IsResolved, Is.False);
                Assert.That(resolved.Entry, Is.Not.Null);
                Assert.That(resolved.AtlasEntry, Is.Null);
                Assert.That(resolved.TerrainEntry, Is.Not.Null);
                Assert.That(resolved.Errors.Any(error => error.Contains("missing Atlas visual ID 'missing-atlas'")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(terrain);
                Object.DestroyImmediate(presets);
            }
        }

        [Test]
        public void ResolveReportsMissingTerrainMetadata()
        {
            var atlas = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var presets = ScriptableObject.CreateInstance<HexTilePresetCatalog>();
            try
            {
                atlas.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-street") });
                presets.ConfigureForTests(atlas, null, new[]
                {
                    new HexTilePresetCatalog.Entry("street-tile", "atlas-street", "missing-terrain")
                });

                var resolved = presets.Resolve("street-tile");

                Assert.That(resolved.IsResolved, Is.False);
                Assert.That(resolved.AtlasEntry, Is.Not.Null);
                Assert.That(resolved.TerrainEntry, Is.Null);
                Assert.That(resolved.Errors.Any(error => error.Contains("missing terrain type ID 'missing-terrain'")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(presets);
            }
        }

        [Test]
        public void ValidateReportsDuplicatePresetIdsAndDoesNotRequireObjectFields()
        {
            var atlas = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var terrain = ScriptableObject.CreateInstance<HexTerrainPalette>();
            var presets = ScriptableObject.CreateInstance<HexTilePresetCatalog>();
            try
            {
                atlas.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-street") });
                terrain.ConfigureForTests(new[] { new HexTerrainPalette.Entry("street") });
                presets.ConfigureForTests(atlas, terrain, new[]
                {
                    new HexTilePresetCatalog.Entry("street-tile", "atlas-street", "street"),
                    new HexTilePresetCatalog.Entry("street-tile", "atlas-street", "street")
                });

                var messages = presets.Validate().ToArray();

                Assert.That(messages.Any(message => message.Contains("duplicate tile preset ID 'street-tile'")), Is.True);
                Assert.That(messages.Any(message => message.Contains("event")), Is.False);
                Assert.That(messages.Any(message => message.Contains("object")), Is.False);
                Assert.That(messages.Any(message => message.Contains("spawn")), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(atlas);
                Object.DestroyImmediate(terrain);
                Object.DestroyImmediate(presets);
            }
        }
    }
}

