using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.MapDesign.Editor;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class HexMapValidationUtilityTests
    {
        private static readonly string[] KnownTestTerrainIds =
        {
            "seoul-start-neighborhood",
            "yeouido-landmark",
            "seoul-street"
        };

        [Test]
        public void ValidateReportsErrorWhenObjectiveLandmarkIsDuplicated()
        {
            var source = CreateSparseSource(
                new[]
                {
                    Cell(0, 0, "start", "seoul-start-neighborhood"),
                    Cell(1, 0, "objective-a", "yeouido-landmark", landmarkId: "finish-landmark"),
                    Cell(2, 0, "objective-b", "yeouido-landmark", landmarkId: "finish-landmark")
                },
                objectRefs: new[]
                {
                    PlayerSpawn(0, 0),
                    ObjectiveMarker("reach-finish", "finish-landmark", 1, 0)
                });
            try
            {
                var report = ValidateSource(source);

                Assert.That(ErrorMessages(report), Has.Some.Contains("exactly one `finish-landmark`"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateReportsErrorWhenObjectiveLandmarkIsMissing()
        {
            var source = CreateSparseSource(
                new[] { Cell(0, 0, "start", "seoul-start-neighborhood") },
                objectRefs: new[]
                {
                    PlayerSpawn(0, 0),
                    ObjectiveMarker("reach-finish", "finish-landmark", 0, 0)
                });
            try
            {
                var report = ValidateSource(source);

                Assert.That(ErrorMessages(report), Has.Some.Contains("targets missing landmark id 'finish-landmark'"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateReportsErrorWhenObjectiveIsNotReachableFromStart()
        {
            var source = CreateSparseSource(
                new[]
                {
                    Cell(0, 0, "start", "seoul-start-neighborhood"),
                    Cell(2, 0, "finish", "yeouido-landmark", landmarkId: "finish-landmark")
                },
                objectRefs: new[]
                {
                    PlayerSpawn(0, 0),
                    ObjectiveMarker("reach-finish", "finish-landmark", 2, 0)
                });
            try
            {
                var report = ValidateSource(source);

                Assert.That(ErrorMessages(report), Has.Some.Contains("not reachable"));
                Assert.That(ErrorMessages(report), Has.Some.Contains("(0,0)"));
                Assert.That(ErrorMessages(report), Has.Some.Contains("(2,0)"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateReportsErrorWhenObjectiveIsNotReachableFromGenericStartCandidate()
        {
            var source = CreateSparseSource(
                new[]
                {
                    Cell(0, 0, "street", "seoul-street"),
                    Cell(2, 0, "finish", "yeouido-landmark", landmarkId: "finish-landmark")
                },
                objectRefs: new[] { ObjectiveMarker("reach-finish", "finish-landmark", 2, 0) });
            try
            {
                var report = ValidateSource(source);

                Assert.That(WarningMessages(report), Has.Some.Contains("No PlayerSpawn objectRef"));
                Assert.That(ErrorMessages(report), Has.Some.Contains("not reachable from any walkable start candidate"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateReportsWarningForUnknownTerrainIds()
        {
            var source = CreateSparseSource(
                Cell(0, 0, "mvp-start", "seoul-start-neighborhood", "mvp-start"),
                Cell(1, 0, "mvp-landmark-63", "volcanic-ash", landmarkId: "landmark-63"));
            try
            {
                var report = ValidateSource(source);

                Assert.That(report.HasErrors, Is.False);
                Assert.That(WarningMessages(report), Has.Some.Contains("volcanic-ash"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateAcceptsPaletteDerivedKnownTerrainIds()
        {
            var source = CreateSparseSource(
                Cell(0, 0, "mvp-start", "seoul-start-neighborhood", "mvp-start"),
                Cell(1, 0, "mvp-landmark-63", "guide-empty", landmarkId: "landmark-63"));
            var palette = ScriptableObject.CreateInstance<HexTerrainPalette>();
            try
            {
                palette.ConfigureForTests(new[]
                {
                    new HexTerrainPalette.Entry("guide-empty", "Guide Empty"),
                    new HexTerrainPalette.Entry("seoul-start-neighborhood", "Start")
                });

                var report = HexMapValidationUtility.Validate(source, palette);

                Assert.That(report.HasErrors, Is.False);
                Assert.That(WarningMessages(report).Where(message => message.Contains("Unknown terrain")), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(palette);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateStillWarnsWhenAuthoredTerrainIsNotInPalette()
        {
            var source = CreateSparseSource(
                Cell(0, 0, "mvp-start", "seoul-start-neighborhood", "mvp-start"),
                Cell(1, 0, "mvp-landmark-63", "volcanic-ash", landmarkId: "landmark-63"));
            var palette = ScriptableObject.CreateInstance<HexTerrainPalette>();
            try
            {
                palette.ConfigureForTests(new[]
                {
                    new HexTerrainPalette.Entry("guide-empty", "Guide Empty"),
                    new HexTerrainPalette.Entry("seoul-start-neighborhood", "Start")
                });

                var report = HexMapValidationUtility.Validate(source, palette);

                Assert.That(report.HasErrors, Is.False);
                Assert.That(WarningMessages(report), Has.Some.Contains("volcanic-ash"));
            }
            finally
            {
                Object.DestroyImmediate(palette);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateReportsWarningForIsolatedWalkableIsland()
        {
            var source = CreateSparseSource(
                Cell(0, 0, "mvp-start", "seoul-start-neighborhood", "mvp-start"),
                Cell(1, 0, "mvp-landmark-63", "yeouido-landmark", landmarkId: "landmark-63"),
                Cell(3, 0, "street", "seoul-street"));
            try
            {
                var report = ValidateSource(source);

                Assert.That(report.HasErrors, Is.False);
                Assert.That(WarningMessages(report), Has.Some.Contains("disconnected walkable islands"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateAcceptsConfiguredSmokeSparseSource()
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            try
            {
                source.ConfigureForTests(
                    new[]
                    {
                        Cell(0, 0, "start", "seoul-start-neighborhood"),
                        Cell(1, 0, "finish", "yeouido-landmark", landmarkId: "finish-landmark")
                    },
                    HexMapPurpose.SmokeMap,
                    objectRefs: new[]
                    {
                        PlayerSpawn(0, 0),
                        ObjectiveMarker("reach-finish", "finish-landmark", 1, 0),
                        new HexMapObjectRef("enemy-spawn", HexMapObjectType.MonsterSpawn, "M001", 0, 0, "primary_pressure", HexMapPurpose.SmokeMap)
                    });

                var report = ValidateSource(source);

                Assert.That(ErrorMessages(report), Is.Empty);
                Assert.That(InfoMessages(report), Has.Some.Contains("Objective binding: reach-finish -> finish-landmark"));
                Assert.That(WarningMessages(report).Where(message => message.Contains("Unknown terrain")), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateAcceptsMemoryStoneObjectiveWithoutAuthoredBinding()
        {
            var source = CreateSparseSource(
                new[]
                {
                    Cell(0, 0, "start", "seoul-start-neighborhood"),
                    Cell(1, 0, "stone", "seoul-street")
                },
                objectRefs: new[]
                {
                    PlayerSpawn(0, 0),
                    MemoryStone("memory-main", 1, 0)
                });
            try
            {
                var report = ValidateSource(source);

                Assert.That(ErrorMessages(report), Is.Empty);
                Assert.That(InfoMessages(report), Has.Some.Contains("MemoryStone object `memory-main`"));
                Assert.That(WarningMessages(report).Where(message => message.Contains("objective binding or MemoryStone")), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateReportsErrorWhenMemoryStoneObjectiveIsNotReachable()
        {
            var source = CreateSparseSource(
                new[]
                {
                    Cell(0, 0, "start", "seoul-start-neighborhood"),
                    Cell(2, 0, "stone", "seoul-street")
                },
                objectRefs: new[]
                {
                    PlayerSpawn(0, 0),
                    MemoryStone("memory-main", 2, 0)
                });
            try
            {
                var report = ValidateSource(source);

                Assert.That(ErrorMessages(report), Has.Some.Contains("MemoryStone objective"));
                Assert.That(ErrorMessages(report), Has.Some.Contains("not reachable"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateWarnsWhenNeitherAuthoredBindingNorMemoryStoneIsConfigured()
        {
            var source = CreateSparseSource(
                new[]
                {
                    Cell(0, 0, "start", "seoul-start-neighborhood"),
                    Cell(1, 0, "street", "seoul-street")
                },
                objectRefs: new[] { PlayerSpawn(0, 0) });
            try
            {
                var report = ValidateSource(source);

                Assert.That(report.HasErrors, Is.False);
                Assert.That(WarningMessages(report), Has.Some.Contains("No authored objective binding or MemoryStone object is configured"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateReportsWarningWhenBoardPurposeIsUnspecified()
        {
            var source = CreateSparseSource(
                Cell(0, 0, "mvp-start", "seoul-start-neighborhood", "mvp-start"),
                Cell(1, 0, "mvp-landmark-63", "yeouido-landmark", landmarkId: "landmark-63"));
            try
            {
                var report = ValidateSource(source);

                Assert.That(report.HasErrors, Is.False);
                Assert.That(WarningMessages(report), Has.Some.Contains("Board purpose is unspecified"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateWithAtlasCatalogBlocksUnresolvableVisualsButKeepsTerrainValidationSeparate()
        {
            var source = CreateSparseSource(
                Cell(0, 0, "mvp-start", "seoul-start-neighborhood", "mvp-start", atlasVisualId: "atlas-known"),
                Cell(1, 0, "mvp-landmark-63", "yeouido-landmark", landmarkId: "landmark-63", atlasVisualId: "missing-atlas"));
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-known", prefab) });

                var report = HexMapValidationUtility.Validate(source, catalog);

                Assert.That(ErrorMessages(report), Has.Some.Contains("missing-atlas"));
                Assert.That(ErrorMessages(report), Has.None.Contains("terrain"));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void ValidateWithAtlasCatalogBlocksNullCatalog()
        {
            var source = CreateSparseSource(
                Cell(0, 0, "mvp-start", "seoul-start-neighborhood", "mvp-start", "landmark-63", atlasVisualId: "atlas-known"));
            try
            {
                var report = HexMapValidationUtility.Validate(source, (AtlasTileCatalog)null);

                Assert.That(ErrorMessages(report), Has.Some.Contains("AtlasTileCatalog reference"));
            }
            finally
            {
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void ValidateWithAtlasCatalogReportsMissingTopPrefabSeparatelyFromTerrainValidation()
        {
            var source = CreateSparseSource(
                Cell(0, 0, "mvp-start", "seoul-start-neighborhood", "mvp-start", atlasVisualId: "atlas-no-top"),
                Cell(1, 0, "goal", "yeouido-landmark", landmarkId: "landmark-63", atlasVisualId: "atlas-no-top"));
            var catalog = ScriptableObject.CreateInstance<AtlasTileCatalog>();
            try
            {
                catalog.ConfigureForTests(new[] { new AtlasTileCatalog.Entry("atlas-no-top") });

                var report = HexMapValidationUtility.Validate(source, catalog);

                Assert.That(ErrorMessages(report), Has.Some.Contains("has no top prefab"));
                Assert.That(ErrorMessages(report), Has.None.Contains("Unknown terrain"));
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(catalog);
            }
        }

        private static string[] ErrorMessages(HexMapValidationReport report)
        {
            return report.Errors.Select(item => item.Message).ToArray();
        }

        private static HexMapValidationReport ValidateSource(HexSparseMapAuthoringSource source)
        {
            return HexMapValidationUtility.Validate(source, KnownTestTerrainIds);
        }

        private static string[] WarningMessages(HexMapValidationReport report)
        {
            return report.Warnings.Select(item => item.Message).ToArray();
        }

        private static string[] InfoMessages(HexMapValidationReport report)
        {
            return report.Items.Where(item => item.Severity == HexMapValidationSeverity.Info).Select(item => item.Message).ToArray();
        }

        private static HexSparseMapAuthoringSource CreateSparseSource(params HexSparseMapAuthoringCell[] cells)
        {
            return CreateSparseSource(cells, null);
        }

        private static HexSparseMapAuthoringSource CreateSparseSource(
            IEnumerable<HexSparseMapAuthoringCell> cells,
            IEnumerable<HexMapObjectRef> objectRefs)
        {
            var source = ScriptableObject.CreateInstance<HexSparseMapAuthoringSource>();
            source.ConfigureForTests(cells, objectRefs: objectRefs);
            return source;
        }

        private static HexMapObjectRef PlayerSpawn(int q, int r)
        {
            return new HexMapObjectRef($"player-spawn-{q}-{r}", HexMapObjectType.PlayerSpawn, string.Empty, q, r);
        }

        private static HexMapObjectRef MemoryStone(string objectId, int q, int r, string role = "main")
        {
            return new HexMapObjectRef(objectId, HexMapObjectType.MemoryStone, "memorystone", q, r, role, interactable: true);
        }

        private static HexMapObjectRef ObjectiveMarker(string objectiveId, string landmarkId, int q, int r)
        {
            return new HexMapObjectRef(objectiveId, HexMapObjectType.ObjectiveMarker, landmarkId, q, r, "Finish", interactable: true);
        }

        private static HexSparseMapAuthoringCell Cell(
            int q,
            int r,
            string tilePresetId,
            string terrainTypeId,
            string eventId = "",
            string landmarkId = "",
            string atlasVisualId = "atlas-test")
        {
            return new HexSparseMapAuthoringCell(
                new HexCoord(q, r),
                tilePresetId,
                terrainTypeId,
                atlasVisualId,
                eventId: eventId,
                landmarkId: landmarkId);
        }
    }
}

