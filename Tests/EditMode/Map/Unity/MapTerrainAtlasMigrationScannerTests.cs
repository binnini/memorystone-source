using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.MapDesign.Editor;

namespace SeoulPlayup.Map.Unity.Tests.EditMode
{
    public sealed class MapTerrainAtlasMigrationScannerTests
    {
        private string tempRoot;

        [SetUp]
        public void SetUp()
        {
            tempRoot = Path.Combine(Path.GetTempPath(), "MapTerrainAtlasMigrationScannerTests", TestContext.CurrentContext.Test.ID);
            Directory.CreateDirectory(tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }

        [Test]
        public void ScanClassifiesMissingAndEmptyAtlasVisualIdsSeparately()
        {
            WriteAsset("Board.asset", @"%YAML 1.1
slots:
  - column: 0
    row: 0
    tileDefinitionId: street-tile
    terrainTypeId: seoul-street
  - column: 1
    row: 0
    tileDefinitionId: park-tile
    terrainTypeId: yeouido-park
    atlasVisualId: 
");

            var report = MapTerrainAtlasMigrationScanner.ScanAssets(tempRoot);

            Assert.That(report.Findings.Any(f => f.Field == "atlasVisualId" && f.Severity == MapTerrainAtlasMigrationSeverity.Migratable), Is.True);
            Assert.That(report.Findings.Any(f => f.Field == "legacy-id-only cell" && f.Severity == MapTerrainAtlasMigrationSeverity.Blocker), Is.True);
            Assert.That(report.Findings.Any(f => f.Field == "legacy-id-only cell"), Is.True);
            Assert.That(report.HasBlockers, Is.True);
        }

        [Test]
        public void ScanReportsNullCatalogAndFallbackPrefabAsBlockers()
        {
            WriteAsset("Scene.unity", @"--- !u!114 &1
MonoBehaviour:
  catalog: {fileID: 0}
  fallbackTopPrefab: {fileID: 123, guid: abcdef, type: 3}
");

            var report = MapTerrainAtlasMigrationScanner.ScanAssets(tempRoot);

            Assert.That(report.Findings.Any(f => f.Field == "AtlasTilePresentationView.catalog" && f.Severity == MapTerrainAtlasMigrationSeverity.Blocker), Is.True);
            Assert.That(report.Findings.Any(f => f.Field == "fallbackTopPrefab" && f.Severity == MapTerrainAtlasMigrationSeverity.Blocker), Is.True);
        }

        [Test]
        public void ScanDetectsStrictCatalogCandidateWithoutFallbackBlocker()
        {
            WriteAsset("AtlasTileCatalog.asset", @"%YAML 1.1
entries:
  - atlasVisualId: atlas-street
");

            var report = MapTerrainAtlasMigrationScanner.ScanAssets(tempRoot);

            Assert.That(report.CatalogCandidates, Contains.Item("Assets/AtlasTileCatalog.asset"));
            Assert.That(report.Findings.Any(f => f.Field == "fallbackEntry"), Is.False);
            Assert.That(report.Findings.Any(f => f.Field == "legacyTileDefinitionIds"), Is.False);
            Assert.That(report.Findings.Any(f => f.Field == "legacyTerrainTypeIds"), Is.False);
        }

        [Test]
        public void ScanClassifiesLegacyMappingsAsMigratableUnknownOrAmbiguous()
        {
            WriteAsset("Board.asset", @"%YAML 1.1
slots:
  - column: 0
    row: 0
    tileDefinitionId: seoul-street
    terrainTypeId: seoul-street
  - column: 1
    row: 0
    tileDefinitionId: unknown-tile
    terrainTypeId: unknown-terrain
  - column: 2
    row: 0
    tileDefinitionId: seoul-street
    terrainTypeId: yeouido-urban
");

            var report = MapTerrainAtlasMigrationScanner.ScanAssets(tempRoot);

            Assert.That(report.Findings.Any(f => f.Severity == MapTerrainAtlasMigrationSeverity.Migratable && f.MappingSource.Contains("seoul-street")), Is.True);
            Assert.That(report.Findings.Any(f => f.BlockerReason.Contains("Unknown legacy tile/terrain ID")), Is.True);
            Assert.That(report.Findings.Any(f => f.BlockerReason.Contains("multiple atlas visuals")), Is.True);
        }

        [Test]
        public void ScanDoesNotTreatTerrainPaletteEntriesAsRuntimeCells()
        {
            WriteAsset("DefaultHexTerrainPalette.asset", @"%YAML 1.1
entries:
  - terrainTypeId: seoul-street
    label: Seoul Street
");

            var report = MapTerrainAtlasMigrationScanner.ScanAssets(tempRoot);

            Assert.That(report.HasBlockers, Is.False);
        }

        [Test]
        public void ReportOutputIsStableAndContainsRequiredColumns()
        {
            WriteAsset("Board.asset", @"slots:
  - tileDefinitionId: street-tile
    terrainTypeId: seoul-street
    atlasVisualId: atlas-street
");

            var report = MapTerrainAtlasMigrationScanner.ScanAssets(tempRoot);
            var json = report.ToJson();
            var markdown = report.ToMarkdown();

            Assert.That(json, Does.Contain("\"schema\": \"map-terrain-atlas-migration-dry-run.v1\""));
            Assert.That(json, Does.Contain("\"path\""));
            Assert.That(json, Does.Contain("\"severity\""));
            Assert.That(json, Does.Contain("\"assetKind\""));
            Assert.That(json, Does.Contain("\"mappingSource\""));
            Assert.That(markdown, Does.Contain("| Severity | Path | Line | Asset kind | Field | Value | Mapping source | Proposed action | Blocker reason |"));
        }

        [Test]
        public void ScanIgnoresCodeFilesOutsideSerializedYamlScope()
        {
            WriteAsset("ScannerFalsePositive.cs", @"public sealed class ScannerFalsePositive
{
    private const string FieldName = ""atlasVisualId:"";
}");

            var report = MapTerrainAtlasMigrationScanner.ScanAssets(tempRoot);

            Assert.That(report.Findings, Is.Empty);
        }

        private void WriteAsset(string relativePath, string contents)
        {
            var path = Path.Combine(tempRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, contents.Replace("\r\n", "\n"));
        }
    }
}

