using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.MapDesign.Editor
{
    public static class MapTerrainAtlasMigrationScanner
    {
        private static readonly string[] SerializedTextExtensions =
        {
            ".asset",
            ".unity",
            ".prefab",
            ".mat",
            ".controller",
            ".overrideController",
            ".anim",
            ".playable",
            ".yaml",
            ".yml"
        };

        private static readonly IReadOnlyDictionary<string, string> ApprovedLegacyAtlasMappings =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mvp-street"] = "mvp-street",
                ["seoul-street"] = "seoul-street",
                ["mvp-hanriver-water"] = "mvp-hanriver-water",
                ["hanriver-water"] = "mvp-hanriver-water",
                ["mvp-riverside-bypass"] = "mvp-riverside-bypass",
                ["riverside-bypass"] = "mvp-riverside-bypass",
                ["mvp-yeouido-urban"] = "mvp-yeouido-urban",
                ["yeouido-urban"] = "yeouido-urban",
                ["mvp-hanriver-bridge"] = "mvp-hanriver-bridge",
                ["hanriver-bridge"] = "mvp-hanriver-bridge",
                ["mvp-primary-route"] = "mvp-primary-route",
                ["hanriver-primary-route"] = "mvp-primary-route",
                ["mvp-start-neighborhood"] = "mvp-start-neighborhood",
                ["seoul-start-neighborhood"] = "mvp-start-neighborhood",
                ["mvp-goal-adjacent"] = "mvp-goal-adjacent",
                ["yeouido-goal-approach"] = "mvp-goal-adjacent",
                ["mvp-start"] = "mvp-start",
                ["mvp-landmark-63"] = "mvp-landmark-63",
                ["yeouido-landmark"] = "mvp-landmark-63",
                ["atlas-scale-test-base"] = "atlas-scale-test-base"
            };

        public static MapTerrainAtlasMigrationReport ScanAssets(string assetsRootPath)
        {
            if (string.IsNullOrWhiteSpace(assetsRootPath))
            {
                throw new ArgumentException("Assets root path is required.", nameof(assetsRootPath));
            }

            var fullRoot = Path.GetFullPath(assetsRootPath);
            var findings = new List<MapTerrainAtlasMigrationFinding>();
            var catalogCandidates = new List<string>();

            if (!Directory.Exists(fullRoot))
            {
                findings.Add(MapTerrainAtlasMigrationFinding.Blocker(
                    "Assets",
                    0,
                    "AssetsRoot",
                    string.Empty,
                    "Assets root does not exist.",
                    "Cannot scan missing Assets root."));
                return new MapTerrainAtlasMigrationReport(findings, catalogCandidates);
            }

            foreach (var path in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(p => ToAssetPath(fullRoot, p), StringComparer.OrdinalIgnoreCase))
            {
                var assetPath = ToAssetPath(fullRoot, path);
                if (ShouldSkip(assetPath))
                {
                    continue;
                }

                if (!IsTextSerializedCandidate(path))
                {
                    continue;
                }

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(path);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    findings.Add(MapTerrainAtlasMigrationFinding.Blocker(
                        assetPath,
                        0,
                        "ReadError",
                        ex.GetType().Name,
                        "Manual review required; scanner could not read asset.",
                        ex.Message));
                    continue;
                }

                ScanFile(assetPath, lines, findings, catalogCandidates);
            }

            return new MapTerrainAtlasMigrationReport(
                findings,
                catalogCandidates.Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        }

        public static void WriteReports(MapTerrainAtlasMigrationReport report, string outputDirectory, string baseFileName)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
            }

            if (string.IsNullOrWhiteSpace(baseFileName))
            {
                throw new ArgumentException("Base file name is required.", nameof(baseFileName));
            }

            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, baseFileName + ".json"), report.ToJson());
            File.WriteAllText(Path.Combine(outputDirectory, baseFileName + ".md"), report.ToMarkdown());
        }

        [MenuItem("Tools/Seoul Playup/Map/Run Terrain Atlas Migration Dry Run")]
        private static void RunDryRunFromMenu()
        {
            RunDryRun();
        }

        public static void RunDryRunForCommandLine()
        {
            var report = RunDryRun();
            EditorApplication.Exit(report.HasBlockers ? 2 : 0);
        }

        private static MapTerrainAtlasMigrationReport RunDryRun()
        {
            var repoRoot = Directory.GetParent(Application.dataPath)?.FullName;
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                Debug.LogError("Could not resolve repository root from Application.dataPath.");
                return new MapTerrainAtlasMigrationReport(new[]
                {
                    MapTerrainAtlasMigrationFinding.Blocker(
                        "Assets",
                        0,
                        "AssetsRoot",
                        string.Empty,
                        "Resolve repository root.",
                        "Could not resolve repository root from Application.dataPath.")
                }, Array.Empty<string>());
            }

            var report = ScanAssets(Path.Combine(repoRoot, "Assets"));
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var outputDirectory = Path.Combine(repoRoot, ".omx", "reports");
            var baseFileName = "mapeditor-terrain-runtime-cleanup-dry-run-" + stamp;
            WriteReports(report, outputDirectory, baseFileName);
            Debug.Log($"Map terrain atlas migration dry-run complete: {report.Findings.Count} finding(s), {report.BlockerCount} blocker(s). Reports: {Path.Combine(outputDirectory, baseFileName)}.json/.md");
            return report;
        }

        private static void ScanFile(string assetPath, string[] lines, ICollection<MapTerrainAtlasMigrationFinding> findings, ICollection<string> catalogCandidates)
        {
            var containsEntries = false;
            var containsAtlasVisualId = false;

            for (var i = 0; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();
                var fieldLine = StripYamlListPrefix(trimmed);
                if (fieldLine.StartsWith("fallbackEntry:", StringComparison.Ordinal))
                {
                    findings.Add(MapTerrainAtlasMigrationFinding.Blocker(
                        assetPath,
                        i + 1,
                        "fallbackEntry",
                        ReadYamlValue(fieldLine),
                        "Catalog fallback entry must be removed or proven unused before deletion.",
                        "fallbackEntry is legacy fallback content."));
                }

                if (fieldLine.StartsWith("entries:", StringComparison.Ordinal))
                {
                    containsEntries = true;
                }

                if (fieldLine.StartsWith("atlasVisualId:", StringComparison.Ordinal))
                {
                    containsAtlasVisualId = true;
                    var value = ReadYamlValue(fieldLine);
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        findings.Add(MapTerrainAtlasMigrationFinding.Blocker(
                            assetPath,
                            i + 1,
                            "atlasVisualId",
                            value,
                            "Populate explicit atlasVisualId through deterministic migration.",
                            "atlasVisualId is present but empty."));
                    }
                    else
                    {
                        findings.Add(MapTerrainAtlasMigrationFinding.Info(
                            assetPath,
                            i + 1,
                            "atlasVisualId",
                            value,
                            "Explicit atlas visual id is already authored."));
                    }
                }

                if (fieldLine.StartsWith("legacyTileDefinitionIds:", StringComparison.Ordinal))
                {
                    findings.Add(MapTerrainAtlasMigrationFinding.Info(
                        assetPath,
                        i + 1,
                        "legacyTileDefinitionIds",
                        ReadYamlValue(fieldLine),
                        "Legacy tile-id mapping must not be used by final runtime resolution."));
                }

                if (fieldLine.StartsWith("legacyTerrainTypeIds:", StringComparison.Ordinal))
                {
                    findings.Add(MapTerrainAtlasMigrationFinding.Info(
                        assetPath,
                        i + 1,
                        "legacyTerrainTypeIds",
                        ReadYamlValue(fieldLine),
                        "Legacy terrain-id mapping must not be used by final runtime resolution."));
                }

                if (fieldLine.StartsWith("catalog:", StringComparison.Ordinal) && IsNullFileReference(fieldLine))
                {
                    findings.Add(MapTerrainAtlasMigrationFinding.Blocker(
                        assetPath,
                        i + 1,
                        "AtlasTilePresentationView.catalog",
                        trimmed,
                        "Assign the authoritative AtlasTileCatalog reference before fallback deletion.",
                        "Runtime presentation catalog reference is null."));
                }

                if (fieldLine.StartsWith("fallbackTopPrefab:", StringComparison.Ordinal) && !IsNullFileReference(fieldLine))
                {
                    findings.Add(MapTerrainAtlasMigrationFinding.Blocker(
                        assetPath,
                        i + 1,
                        "fallbackTopPrefab",
                        trimmed,
                        "Prove fallbackTopPrefab is unused in strict tests before deletion.",
                        "Runtime presentation still has fallback prefab dependency."));
                }
            }

            if (containsEntries && containsAtlasVisualId)
            {
                catalogCandidates.Add(assetPath);
            }

            ScanSlotBlocks(assetPath, lines, findings);
        }

        private static void ScanSlotBlocks(string assetPath, string[] lines, ICollection<MapTerrainAtlasMigrationFinding> findings)
        {
            var blockStart = -1;
            var block = new List<string>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (IsSerializedListItemStart(lines[i]) && block.Count > 0)
                {
                    ScanSlotBlock(assetPath, blockStart, block, findings);
                    block.Clear();
                    blockStart = -1;
                }

                if (block.Count > 0 || IsSerializedListItemStart(lines[i]))
                {
                    if (block.Count == 0)
                    {
                        blockStart = i;
                    }

                    block.Add(lines[i]);
                }
            }

            if (block.Count > 0)
            {
                ScanSlotBlock(assetPath, blockStart, block, findings);
            }
        }

        private static void ScanSlotBlock(string assetPath, int zeroBasedStartLine, IList<string> block, ICollection<MapTerrainAtlasMigrationFinding> findings)
        {
            var hasTileDefinition = false;
            var hasTerrainType = false;
            var hasAtlasField = false;
            var hasCellCoordinate = false;
            var atlasValue = string.Empty;
            var tileValue = string.Empty;
            var terrainValue = string.Empty;

            foreach (var raw in block)
            {
                var trimmed = raw.Trim();
                var fieldLine = StripYamlListPrefix(trimmed);
                if (fieldLine.StartsWith("tileDefinitionId:", StringComparison.Ordinal))
                {
                    hasTileDefinition = true;
                    tileValue = ReadYamlValue(fieldLine);
                }
                else if (fieldLine.StartsWith("terrainTypeId:", StringComparison.Ordinal))
                {
                    hasTerrainType = true;
                    terrainValue = ReadYamlValue(fieldLine);
                }
                else if (fieldLine.StartsWith("atlasVisualId:", StringComparison.Ordinal))
                {
                    hasAtlasField = true;
                    atlasValue = ReadYamlValue(fieldLine);
                }
                else if (fieldLine.StartsWith("column:", StringComparison.Ordinal)
                         || fieldLine.StartsWith("row:", StringComparison.Ordinal)
                         || fieldLine.StartsWith("q:", StringComparison.Ordinal)
                         || fieldLine.StartsWith("r:", StringComparison.Ordinal))
                {
                    hasCellCoordinate = true;
                }
            }

            if ((!hasTileDefinition && !hasTerrainType) || !hasCellCoordinate)
            {
                return;
            }

            var value = $"tileDefinitionId={tileValue}; terrainTypeId={terrainValue}; atlasVisualId={atlasValue}";
            if (!hasAtlasField)
            {
                AddLegacyIdFinding(assetPath, zeroBasedStartLine, "atlasVisualId", value, tileValue, terrainValue, findings);
                return;
            }

            if (string.IsNullOrWhiteSpace(atlasValue))
            {
                AddLegacyIdFinding(assetPath, zeroBasedStartLine, "legacy-id-only cell", value, tileValue, terrainValue, findings);
            }
        }

        private static void AddLegacyIdFinding(string assetPath, int zeroBasedStartLine, string field, string value, string tileValue, string terrainValue, ICollection<MapTerrainAtlasMigrationFinding> findings)
        {
            var candidates = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            AddMappingCandidate(tileValue, candidates);
            AddMappingCandidate(terrainValue, candidates);

            if (candidates.Count == 1)
            {
                var target = candidates.Single();
                findings.Add(MapTerrainAtlasMigrationFinding.Migratable(
                    assetPath,
                    zeroBasedStartLine + 1,
                    field,
                    value,
                    $"Populate atlasVisualId with approved catalog entry '{target}'.",
                    "approved-legacy-id-map:" + target));
                return;
            }

            findings.Add(MapTerrainAtlasMigrationFinding.Blocker(
                assetPath,
                zeroBasedStartLine + 1,
                field,
                value,
                candidates.Count == 0
                    ? "Add an approved legacy-id to atlasVisualId mapping before apply."
                    : "Resolve ambiguous legacy-id mapping before apply: " + string.Join(", ", candidates),
                candidates.Count == 0
                    ? "Unknown legacy tile/terrain ID has no approved atlas mapping."
                    : "Legacy tile/terrain IDs map to multiple atlas visuals.",
                candidates.Count == 0 ? "unmapped-legacy-id" : "ambiguous-legacy-id-map"));
        }

        private static void AddMappingCandidate(string legacyId, ISet<string> candidates)
        {
            if (!string.IsNullOrWhiteSpace(legacyId) && ApprovedLegacyAtlasMappings.TryGetValue(legacyId.Trim(), out var atlasVisualId))
            {
                candidates.Add(atlasVisualId);
            }
        }

        private static bool IsSerializedListItemStart(string line)
        {
            var trimmedStart = line.TrimStart();
            return trimmedStart.StartsWith("- ", StringComparison.Ordinal) || trimmedStart == "-";
        }

        private static bool IsNullFileReference(string value)
        {
            return value.IndexOf("fileID: 0", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ReadYamlValue(string trimmedLine)
        {
            var colon = trimmedLine.IndexOf(':');
            if (colon < 0 || colon + 1 >= trimmedLine.Length)
            {
                return string.Empty;
            }

            return trimmedLine.Substring(colon + 1).Trim().Trim('"');
        }

        private static string StripYamlListPrefix(string trimmedLine)
        {
            return trimmedLine.StartsWith("- ", StringComparison.Ordinal)
                ? trimmedLine.Substring(2).TrimStart()
                : trimmedLine;
        }

        private static bool IsTextSerializedCandidate(string path)
        {
            var extension = System.IO.Path.GetExtension(path);
            return SerializedTextExtensions.Any(textExtension => string.Equals(textExtension, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static bool ShouldSkip(string assetPath)
        {
            return assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static string ToAssetPath(string fullRoot, string fullPath)
        {
            var relative = Path.GetFullPath(fullPath)
                .Substring(Path.GetFullPath(fullRoot).Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return "Assets/" + relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
        }
    }

    public sealed class MapTerrainAtlasMigrationReport
    {
        public MapTerrainAtlasMigrationReport(IEnumerable<MapTerrainAtlasMigrationFinding> findings, IEnumerable<string> catalogCandidates)
        {
            Findings = findings == null
                ? new List<MapTerrainAtlasMigrationFinding>()
                : findings.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Line)
                    .ThenBy(f => f.Field, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            CatalogCandidates = catalogCandidates == null
                ? new List<string>()
                : catalogCandidates.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public IReadOnlyList<MapTerrainAtlasMigrationFinding> Findings { get; }
        public IReadOnlyList<string> CatalogCandidates { get; }
        public int BlockerCount => Findings.Count(f => f.Severity == MapTerrainAtlasMigrationSeverity.Blocker);
        public bool HasBlockers => BlockerCount > 0;

        public string ToJson()
        {
            var builder = new StringBuilder();
            builder.AppendLine("{");
            builder.AppendLine("  \"schema\": \"map-terrain-atlas-migration-dry-run.v1\",");
            builder.AppendLine($"  \"generatedAtUtc\": \"{Escape(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))}\",");
            builder.AppendLine($"  \"findingCount\": {Findings.Count},");
            builder.AppendLine($"  \"blockerCount\": {BlockerCount},");
            builder.AppendLine("  \"catalogCandidates\": [");
            for (var i = 0; i < CatalogCandidates.Count; i++)
            {
                builder.Append("    \"").Append(Escape(CatalogCandidates[i])).Append("\"");
                builder.AppendLine(i + 1 == CatalogCandidates.Count ? string.Empty : ",");
            }

            builder.AppendLine("  ],");
            builder.AppendLine("  \"findings\": [");
            for (var i = 0; i < Findings.Count; i++)
            {
                var finding = Findings[i];
                builder.AppendLine("    {");
                builder.AppendLine($"      \"path\": \"{Escape(finding.Path)}\",");
                builder.AppendLine($"      \"line\": {finding.Line},");
                builder.AppendLine($"      \"assetKind\": \"{Escape(finding.AssetKind)}\",");
                builder.AppendLine($"      \"field\": \"{Escape(finding.Field)}\",");
                builder.AppendLine($"      \"value\": \"{Escape(finding.Value)}\",");
                builder.AppendLine($"      \"mappingSource\": \"{Escape(finding.MappingSource)}\",");
                builder.AppendLine($"      \"severity\": \"{finding.Severity.ToString().ToLowerInvariant()}\",");
                builder.AppendLine($"      \"proposedAction\": \"{Escape(finding.ProposedAction)}\",");
                builder.AppendLine($"      \"blockerReason\": \"{Escape(finding.BlockerReason)}\"");
                builder.Append("    }");
                builder.AppendLine(i + 1 == Findings.Count ? string.Empty : ",");
            }

            builder.AppendLine("  ]");
            builder.AppendLine("}");
            return builder.ToString();
        }

        public string ToMarkdown()
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Map Terrain Atlas Migration Dry-Run Report");
            builder.AppendLine();
            builder.AppendLine($"Generated UTC: `{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}`");
            builder.AppendLine($"Findings: `{Findings.Count}`");
            builder.AppendLine($"Blockers: `{BlockerCount}`");
            builder.AppendLine();
            builder.AppendLine("## Catalog candidates");
            builder.AppendLine();
            if (CatalogCandidates.Count == 0)
            {
                builder.AppendLine("- None detected. Phase 2 catalog bootstrap is required before migration can continue.");
            }
            else
            {
                foreach (var candidate in CatalogCandidates)
                {
                    builder.AppendLine("- `" + candidate + "`");
                }
            }

            builder.AppendLine();
            builder.AppendLine("## Findings");
            builder.AppendLine();
            builder.AppendLine("| Severity | Path | Line | Asset kind | Field | Value | Mapping source | Proposed action | Blocker reason |");
            builder.AppendLine("|---|---|---:|---|---|---|---|---|---|");
            foreach (var finding in Findings)
            {
                builder.Append("| ").Append(finding.Severity).Append(" | `").Append(finding.Path).Append("` | ").Append(finding.Line).Append(" | `").Append(finding.AssetKind).Append("` | `").Append(finding.Field).Append("` | `").Append(EscapeMarkdown(finding.Value)).Append("` | `").Append(EscapeMarkdown(finding.MappingSource)).Append("` | ").Append(EscapeMarkdown(finding.ProposedAction)).Append(" | ").Append(EscapeMarkdown(finding.BlockerReason)).AppendLine(" |");
            }

            return builder.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string EscapeMarkdown(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        }
    }

    public sealed class MapTerrainAtlasMigrationFinding
    {
        private MapTerrainAtlasMigrationFinding(string path, int line, string field, string value, MapTerrainAtlasMigrationSeverity severity, string proposedAction, string blockerReason, string mappingSource = "")
        {
            Path = path ?? string.Empty;
            Line = line;
            AssetKind = ResolveAssetKind(Path);
            Field = field ?? string.Empty;
            Value = value ?? string.Empty;
            Severity = severity;
            ProposedAction = proposedAction ?? string.Empty;
            BlockerReason = blockerReason ?? string.Empty;
            MappingSource = mappingSource ?? string.Empty;
        }

        public string Path { get; }
        public int Line { get; }
        public string AssetKind { get; }
        public string Field { get; }
        public string Value { get; }
        public MapTerrainAtlasMigrationSeverity Severity { get; }
        public string ProposedAction { get; }
        public string BlockerReason { get; }
        public string MappingSource { get; }

        public static MapTerrainAtlasMigrationFinding Info(string path, int line, string field, string value, string proposedAction)
        {
            return new MapTerrainAtlasMigrationFinding(path, line, field, value, MapTerrainAtlasMigrationSeverity.Info, proposedAction, string.Empty);
        }

        public static MapTerrainAtlasMigrationFinding Migratable(string path, int line, string field, string value, string proposedAction, string mappingSource)
        {
            return new MapTerrainAtlasMigrationFinding(path, line, field, value, MapTerrainAtlasMigrationSeverity.Migratable, proposedAction, string.Empty, mappingSource);
        }

        public static MapTerrainAtlasMigrationFinding Blocker(string path, int line, string field, string value, string proposedAction, string blockerReason, string mappingSource = "")
        {
            return new MapTerrainAtlasMigrationFinding(path, line, field, value, MapTerrainAtlasMigrationSeverity.Blocker, proposedAction, blockerReason, mappingSource);
        }

        private static string ResolveAssetKind(string path)
        {
            var extension = System.IO.Path.GetExtension(path);
            if (string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase))
            {
                return "scene";
            }

            if (string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
            {
                return "prefab";
            }

            if (string.Equals(extension, ".asset", StringComparison.OrdinalIgnoreCase))
            {
                return "asset";
            }

            return string.IsNullOrWhiteSpace(extension) ? "unknown" : extension.TrimStart('.').ToLowerInvariant();
        }
    }

    public enum MapTerrainAtlasMigrationSeverity
    {
        Info,
        Migratable,
        Blocker
    }
}
