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
    /// Editor utility that wires AudioClips into a <see cref="SoundCatalog"/> asset from
    /// <c>Assets/Sounds/sound_manifest.csv</c>. The CSV's <c>cue_id</c> column is the
    /// authoritative key (it must match the IDs raised in code via <see cref="AudioCueIds"/>).
    ///
    /// Merge semantics: existing entries keep their tuned volume/pitch/cooldown/displayName and
    /// only have their clip (and, optionally, bus) refreshed. Rows whose cue_id is not yet in the
    /// catalog are appended with sensible defaults. Nothing is ever deleted, so the import is
    /// re-runnable and non-destructive to designer tuning.
    /// </summary>
    public static class SoundCatalogCsvImporter
    {
        private const string ManifestPath = "Assets/Sounds/sound_manifest.csv";
        private const string PreferredCatalogPath = "Assets/Data/Combat/Presentation/Catalogs/SoundCatalog.asset";

        // When true, also overwrite the bus column on existing entries from the CSV.
        // Clip is always refreshed; bus is treated as authoritative-from-CSV here because the
        // manifest is the single source of truth for routing.
        private const bool OverwriteBusOnExisting = true;

        [MenuItem("Seoul Playup/Audio/Import Sound Catalog From Manifest CSV")]
        public static void ImportFromManifest()
        {
            var catalog = ResolveCatalog();
            if (catalog == null)
            {
                EditorUtility.DisplayDialog(
                    "Sound Catalog Import",
                    "No SoundCatalog asset found. Create one (Seoul Playup/Combat/Sound Catalog) first.",
                    "OK");
                return;
            }

            var manifestFullPath = Path.GetFullPath(ManifestPath);
            if (!File.Exists(manifestFullPath))
            {
                EditorUtility.DisplayDialog("Sound Catalog Import", $"Manifest not found:\n{ManifestPath}", "OK");
                return;
            }

            var rows = ParseManifest(File.ReadAllLines(manifestFullPath, new UTF8Encoding(false)));
            if (rows.Count == 0)
            {
                EditorUtility.DisplayDialog("Sound Catalog Import", "Manifest has no data rows.", "OK");
                return;
            }

            var serialized = new SerializedObject(catalog);
            var entriesProp = serialized.FindProperty("entries");
            if (entriesProp == null || !entriesProp.isArray)
            {
                Debug.LogError("SoundCatalog 'entries' property not found or not an array.", catalog);
                return;
            }

            var indexByCueId = BuildCueIndex(entriesProp);

            int updated = 0, added = 0, missingClip = 0;
            var missingClipCues = new List<string>();

            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.CueId))
                {
                    continue;
                }

                AudioClip clip = null;
                if (!string.IsNullOrWhiteSpace(row.FilePath))
                {
                    clip = AssetDatabase.LoadAssetAtPath<AudioClip>(row.FilePath);
                    if (clip == null)
                    {
                        missingClip++;
                        missingClipCues.Add($"{row.CueId} -> {row.FilePath}");
                    }
                }

                SerializedProperty entry;
                if (indexByCueId.TryGetValue(row.CueId, out var existingIndex))
                {
                    entry = entriesProp.GetArrayElementAtIndex(existingIndex);
                    if (clip != null)
                    {
                        entry.FindPropertyRelative("clip").objectReferenceValue = clip;
                    }
                    if (OverwriteBusOnExisting)
                    {
                        entry.FindPropertyRelative("bus").enumValueIndex = (int)row.Bus;
                    }
                    updated++;
                }
                else
                {
                    entriesProp.arraySize++;
                    entry = entriesProp.GetArrayElementAtIndex(entriesProp.arraySize - 1);
                    InitializeNewEntry(entry, row, clip);
                    indexByCueId[row.CueId] = entriesProp.arraySize - 1;
                    added++;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();

            var summary = new StringBuilder();
            summary.AppendLine($"Sound catalog import complete: {AssetDatabase.GetAssetPath(catalog)}");
            summary.AppendLine($"  rows processed : {rows.Count}");
            summary.AppendLine($"  entries updated: {updated}");
            summary.AppendLine($"  entries added  : {added}");
            summary.AppendLine($"  clips missing  : {missingClip}");
            if (missingClipCues.Count > 0)
            {
                summary.AppendLine("  missing clip paths:");
                foreach (var m in missingClipCues)
                {
                    summary.AppendLine($"    - {m}");
                }
            }

            Debug.Log(summary.ToString(), catalog);
            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
        }

        private static SoundCatalog ResolveCatalog()
        {
            var preferred = AssetDatabase.LoadAssetAtPath<SoundCatalog>(PreferredCatalogPath);
            if (preferred != null)
            {
                return preferred;
            }

            var guids = AssetDatabase.FindAssets("t:SoundCatalog");
            if (guids.Length == 0)
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<SoundCatalog>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static Dictionary<string, int> BuildCueIndex(SerializedProperty entriesProp)
        {
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < entriesProp.arraySize; i++)
            {
                var cueId = entriesProp.GetArrayElementAtIndex(i).FindPropertyRelative("cueId").stringValue;
                if (!string.IsNullOrWhiteSpace(cueId) && !index.ContainsKey(cueId))
                {
                    index[cueId] = i;
                }
            }

            return index;
        }

        private static void InitializeNewEntry(SerializedProperty entry, ManifestRow row, AudioClip clip)
        {
            entry.FindPropertyRelative("cueId").stringValue = row.CueId;
            entry.FindPropertyRelative("displayName").stringValue = row.SoundName ?? string.Empty;
            entry.FindPropertyRelative("category").stringValue = string.Empty;
            entry.FindPropertyRelative("tags").arraySize = 0;
            entry.FindPropertyRelative("designerNote").stringValue = row.Note ?? string.Empty;
            entry.FindPropertyRelative("deprecated").boolValue = false;
            entry.FindPropertyRelative("clip").objectReferenceValue = clip;
            entry.FindPropertyRelative("bus").enumValueIndex = (int)row.Bus;
            entry.FindPropertyRelative("volume").floatValue = 1f;
            entry.FindPropertyRelative("pitchMin").floatValue = 1f;
            entry.FindPropertyRelative("pitchMax").floatValue = 1f;
            entry.FindPropertyRelative("cooldownSeconds").floatValue = DefaultCooldownForBus(row.Bus);
            // EditorOnlyWarning surfaces unwired cues during play-testing without spamming builds.
            entry.FindPropertyRelative("missingClipBehavior").enumValueIndex = (int)MissingClipBehavior.EditorOnlyWarning;
        }

        private static float DefaultCooldownForBus(SoundBus bus)
        {
            switch (bus)
            {
                case SoundBus.Ui:
                    return 0.1f;
                case SoundBus.Ambience:
                case SoundBus.Music:
                    return 0.5f;
                default:
                    return 0.05f;
            }
        }

        private static SoundBus ParseBus(string raw)
        {
            if (Enum.TryParse(raw?.Trim(), ignoreCase: true, out SoundBus bus))
            {
                return bus;
            }

            return SoundBus.Sfx;
        }

        private static List<ManifestRow> ParseManifest(IReadOnlyList<string> lines)
        {
            var rows = new List<ManifestRow>();
            if (lines == null || lines.Count == 0)
            {
                return rows;
            }

            var header = DevEditorCsv.SplitLine(lines[0]);
            int idx(string name) => Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));

            int cueIdCol = idx("cue_id");
            int filePathCol = idx("file_path");
            int busCol = idx("bus");
            int soundNameCol = idx("sound_name");
            int noteCol = idx("note");

            if (cueIdCol < 0 || filePathCol < 0 || busCol < 0)
            {
                Debug.LogError("Manifest header missing required columns (cue_id, file_path, bus).");
                return rows;
            }

            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var cells = DevEditorCsv.SplitLine(lines[i]);
                string cell(int col) => col >= 0 && col < cells.Length ? cells[col].Trim() : string.Empty;

                rows.Add(new ManifestRow
                {
                    CueId = cell(cueIdCol),
                    FilePath = cell(filePathCol),
                    Bus = ParseBus(cell(busCol)),
                    SoundName = cell(soundNameCol),
                    Note = cell(noteCol)
                });
            }

            return rows;
        }

        private struct ManifestRow
        {
            public string CueId;
            public string FilePath;
            public SoundBus Bus;
            public string SoundName;
            public string Note;
        }
    }
}
#endif
