#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Writes the live <see cref="SoundCatalog"/> back out to <c>Assets/Sounds/sound_manifest.csv</c>,
    /// the reverse of <see cref="SoundCatalogCsvImporter"/>.
    ///
    /// The manifest is NOT a stale mirror of the catalog — it carries columns the catalog has no room for
    /// (<c>wiring_status</c>, <c>proposed_id</c>, <c>sound_name</c>, <c>note</c>) that record what a cue is
    /// *supposed* to become, which is how sound work gets ordered. A naive regeneration would flatten that
    /// intent, so the export merges instead of overwriting: rows are keyed by <c>cue_id</c>, only the
    /// catalog-derived columns (<c>file_path</c>, <c>file_name</c>, <c>bus</c>) are rewritten, and every
    /// other column — including columns this code does not know about — is carried through untouched.
    ///
    /// Rows whose cue_id is not in the catalog are kept as well: they are pending orders, not garbage.
    /// Catalog cues with no row yet are appended, with <c>wiring_status</c> deliberately left blank because
    /// that column is a human triage verdict, not something an exporter can derive.
    ///
    /// Rewriting file_path from the real AudioClip reference is the point of the round trip: the checked-in
    /// manifest still holds pre-reorganization paths, which is why a re-import currently resolves almost no
    /// clips. Exporting first makes a subsequent import meaningful.
    /// </summary>
    public static class SoundCatalogCsvExporter
    {
        private const string ManifestPath = "Assets/Sounds/sound_manifest.csv";
        private const string PreferredCatalogPath = "Assets/Data/Combat/Presentation/Catalogs/SoundCatalog.asset";

        private const string CueIdColumn = "cue_id";
        private const string FilePathColumn = "file_path";
        private const string FileNameColumn = "file_name";
        private const string BusColumn = "bus";
        private const string WiringStatusColumn = "wiring_status";
        private const string ProposedIdColumn = "proposed_id";
        private const string SoundNameColumn = "sound_name";
        private const string NoteColumn = "note";

        private static readonly string[] DefaultHeader =
        {
            CueIdColumn, FilePathColumn, BusColumn, WiringStatusColumn,
            ProposedIdColumn, FileNameColumn, SoundNameColumn, NoteColumn
        };

        [MenuItem("Seoul Playup/Audio/Export Sound Catalog To Manifest CSV")]
        public static void ExportToManifest()
        {
            var catalog = ResolveCatalog();
            if (catalog == null)
            {
                EditorUtility.DisplayDialog(
                    "Sound Catalog Export",
                    "No SoundCatalog asset found. Create one (Seoul Playup/Combat/Sound Catalog) first.",
                    "OK");
                return;
            }

            var summary = Export(catalog, ManifestPath);
            Debug.Log(summary, catalog);
            AssetDatabase.ImportAsset(ManifestPath);
            EditorUtility.DisplayDialog("Sound Catalog Export", summary, "OK");
        }

        /// <summary>
        /// Merges the catalog into the manifest at <paramref name="manifestPath"/> and returns a summary.
        /// Separated from the menu entry so the behavior is callable without a dialog.
        /// </summary>
        public static string Export(SoundCatalog catalog, string manifestPath)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var fullPath = Path.GetFullPath(manifestPath);
            var header = DefaultHeader.ToList();
            var rows = new List<Dictionary<string, string>>();

            if (File.Exists(fullPath))
            {
                ReadManifest(File.ReadAllLines(fullPath, new UTF8Encoding(false)), header, rows);
            }

            EnsureColumns(header);

            var rowByCueId = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                var cueId = Value(row, CueIdColumn);
                if (!string.IsNullOrWhiteSpace(cueId) && !rowByCueId.ContainsKey(cueId))
                {
                    rowByCueId[cueId] = row;
                }
            }

            int updated = 0, added = 0, clipless = 0;
            var catalogCueIds = new HashSet<string>(StringComparer.Ordinal);
            // Authored file paths that pointed at a file which does not exist and which the catalog answers
            // with a different clip. Those were requests ("this cue should get its own 기력부족 sample"), not
            // stale mirrors, and rewriting them to the shared clip that is actually wired erases the request.
            // The rewrite is still the right call — file_path is the importer's lookup key — but it is
            // reported instead of applied silently, so a superseded order can be re-raised deliberately.
            var supersededRequests = new List<string>();

            foreach (var entry in catalog.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.CueId))
                {
                    continue;
                }

                catalogCueIds.Add(entry.CueId);
                var clipPath = entry.Clip != null ? AssetDatabase.GetAssetPath(entry.Clip) : string.Empty;
                if (string.IsNullOrEmpty(clipPath))
                {
                    clipless++;
                }

                if (rowByCueId.TryGetValue(entry.CueId, out var row))
                {
                    updated++;
                }
                else
                {
                    row = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var column in header)
                    {
                        row[column] = string.Empty;
                    }

                    row[CueIdColumn] = entry.CueId;
                    row[ProposedIdColumn] = entry.CueId;
                    row[SoundNameColumn] = entry.DisplayName;
                    row[NoteColumn] = entry.DesignerNote;
                    // wiring_status stays blank on purpose: whether a cue is ready / defined-unraised /
                    // needs-cue is a triage call, and inventing one here would look authored.
                    rows.Add(row);
                    rowByCueId[entry.CueId] = row;
                    added++;
                }

                var authoredPath = Value(row, FilePathColumn);
                if (!string.IsNullOrEmpty(authoredPath)
                    && !string.Equals(authoredPath, clipPath, StringComparison.OrdinalIgnoreCase)
                    && AssetDatabase.LoadAssetAtPath<AudioClip>(authoredPath) == null)
                {
                    supersededRequests.Add(
                        $"{entry.CueId}: {authoredPath} → {(string.IsNullOrEmpty(clipPath) ? "<no clip>" : clipPath)}");
                }

                // Catalog-derived columns only. Everything else in the row is left exactly as authored.
                row[FilePathColumn] = clipPath;
                row[FileNameColumn] = string.IsNullOrEmpty(clipPath) ? string.Empty : Path.GetFileName(clipPath);
                row[BusColumn] = entry.Bus.ToString();
            }

            var orphanRows = rows.Count(row =>
            {
                var cueId = Value(row, CueIdColumn);
                return !string.IsNullOrWhiteSpace(cueId) && !catalogCueIds.Contains(cueId);
            });

            File.WriteAllText(fullPath, BuildCsv(header, rows), new UTF8Encoding(false));

            var report = new StringBuilder();
            report.AppendLine($"Sound manifest export complete: {manifestPath}");
            report.AppendLine($"  catalog entries      : {catalog.Entries.Count}");
            report.AppendLine($"  rows updated         : {updated}");
            report.AppendLine($"  rows added           : {added} (wiring_status left blank — needs triage)");
            report.AppendLine($"  rows kept, not in catalog: {orphanRows} (pending orders, preserved)");
            report.AppendLine($"  catalog cues with no clip: {clipless}");
            report.AppendLine($"  superseded file_path requests: {supersededRequests.Count}");
            foreach (var superseded in supersededRequests)
            {
                report.AppendLine($"    - {superseded}");
            }

            return report.ToString();
        }

        private static void ReadManifest(
            IReadOnlyList<string> lines, List<string> header, List<Dictionary<string, string>> rows)
        {
            if (lines.Count == 0)
            {
                return;
            }

            var parsedHeader = SplitCsvLine(lines[0]).Select(cell => cell.Trim()).ToArray();
            if (parsedHeader.Length > 0 && !string.IsNullOrWhiteSpace(parsedHeader[0]))
            {
                header.Clear();
                header.AddRange(parsedHeader);
            }

            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var cells = SplitCsvLine(lines[i]);
                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var column = 0; column < header.Count; column++)
                {
                    row[header[column]] = column < cells.Length ? cells[column] : string.Empty;
                }

                rows.Add(row);
            }
        }

        // A manifest written before a column existed still has to round-trip through this exporter.
        private static void EnsureColumns(List<string> header)
        {
            foreach (var column in DefaultHeader)
            {
                if (!header.Any(existing => string.Equals(existing, column, StringComparison.OrdinalIgnoreCase)))
                {
                    header.Add(column);
                }
            }
        }

        private static string Value(IReadOnlyDictionary<string, string> row, string column)
        {
            return row.TryGetValue(column, out var value) ? value.Trim() : string.Empty;
        }

        private static string BuildCsv(IReadOnlyList<string> header, IReadOnlyList<Dictionary<string, string>> rows)
        {
            var builder = new StringBuilder();
            builder.Append(string.Join(",", header.Select(Escape))).Append('\n');

            foreach (var row in rows)
            {
                builder
                    .Append(string.Join(",", header.Select(column => Escape(row.TryGetValue(column, out var cell) ? cell : string.Empty))))
                    .Append('\n');
            }

            return builder.ToString();
        }

        private static string Escape(string value)
        {
            value ??= string.Empty;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
            {
                return value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static SoundCatalog ResolveCatalog()
        {
            var preferred = AssetDatabase.LoadAssetAtPath<SoundCatalog>(PreferredCatalogPath);
            if (preferred != null)
            {
                return preferred;
            }

            var guids = AssetDatabase.FindAssets("t:SoundCatalog");
            return guids.Length == 0
                ? null
                : AssetDatabase.LoadAssetAtPath<SoundCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        // Mirrors SoundCatalogCsvImporter.SplitCsvLine so both directions agree on quoting.
        private static string[] SplitCsvLine(string line)
        {
            var result = new List<string>();
            var builder = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            builder.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        builder.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    result.Add(builder.ToString());
                    builder.Clear();
                }
                else
                {
                    builder.Append(c);
                }
            }

            result.Add(builder.ToString());
            return result.ToArray();
        }
    }
}
#endif
