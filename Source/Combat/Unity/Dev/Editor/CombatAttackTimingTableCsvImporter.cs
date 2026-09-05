#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    /// <summary>
    /// Editor utility that syncs a <see cref="CombatAttackTimingTable"/> asset with
    /// <c>Assets/Data/Combat/Presentation/Source/combat_attack_timing.csv</c>. The CSV is the authoring
    /// source of truth: each row keys an attack by <c>id</c> (cardId for player attacks, patternId for
    /// monster attacks), and every timing column is optional — a blank cell means "inherit the global
    /// CombatTimingProfile value" (stored as an unset <see cref="CombatAttackTimingTable.OptionalFloat"/>).
    ///
    /// Import is re-runnable: rows already present in the asset are overwritten field-by-field from the CSV,
    /// rows that are new are appended, and rows present only in the asset are left untouched (non-destructive).
    /// Export writes the current asset back out to the CSV so the timing lab's live edits can be persisted.
    /// </summary>
    public static class CombatAttackTimingTableCsvImporter
    {
        private const string CsvPath = "Assets/Data/Combat/Presentation/Source/combat_attack_timing.csv";
        private const string PreferredAssetPath = "Assets/Data/Combat/Presentation/Catalogs/CombatAttackTimingTable.asset";

        private static readonly string[] Header =
        {
            "id", "note",
            "windup", "impact", "death",
            "player_hitstop", "monster_hitstop", "lethal_hitstop",
            "visual_offset", "shake_offset", "hitstop_offset"
        };

        [MenuItem("Seoul Playup/Combat/Import Attack Timing Table From CSV")]
        public static void ImportFromCsv()
        {
            var table = ResolveOrCreateTable();
            if (table == null)
            {
                EditorUtility.DisplayDialog("Attack Timing Import", "Could not resolve or create a CombatAttackTimingTable asset.", "OK");
                return;
            }

            var csvFullPath = Path.GetFullPath(CsvPath);
            if (!File.Exists(csvFullPath))
            {
                EditorUtility.DisplayDialog("Attack Timing Import", $"CSV not found:\n{CsvPath}", "OK");
                return;
            }

            var rows = ParseCsv(File.ReadAllLines(csvFullPath, new UTF8Encoding(false)));
            int updated = 0, added = 0;
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Id))
                {
                    continue;
                }

                var existed = table.TryGet(row.Id, out _);
                var entry = table.GetOrCreate(row.Id);
                entry.note = row.Note ?? string.Empty;
                Apply(ref entry.windupDelay, row.Windup);
                Apply(ref entry.impactDelay, row.Impact);
                Apply(ref entry.deathDelay, row.Death);
                Apply(ref entry.playerHitStopSeconds, row.PlayerHitStop);
                Apply(ref entry.monsterHitStopSeconds, row.MonsterHitStop);
                Apply(ref entry.lethalHitStopSeconds, row.LethalHitStop);
                Apply(ref entry.visualImpactOffset, row.VisualOffset);
                Apply(ref entry.shakeImpactOffset, row.ShakeOffset);
                Apply(ref entry.hitStopImpactOffset, row.HitStopOffset);

                if (existed)
                {
                    updated++;
                }
                else
                {
                    added++;
                }
            }

            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();

            Debug.Log($"Attack timing import complete: {AssetDatabase.GetAssetPath(table)}\n  rows processed: {rows.Count}\n  updated: {updated}\n  added: {added}", table);
            Selection.activeObject = table;
            EditorGUIUtility.PingObject(table);
        }

        [MenuItem("Seoul Playup/Combat/Export Attack Timing Table To CSV")]
        public static void ExportToCsv()
        {
            var table = ResolveOrCreateTable();
            if (table == null)
            {
                EditorUtility.DisplayDialog("Attack Timing Export", "No CombatAttackTimingTable asset to export.", "OK");
                return;
            }

            WriteCsv(table);
            Debug.Log($"Attack timing exported to {CsvPath} ({table.Entries.Count} rows).", table);
        }

        /// <summary>Write the asset's current rows out to the CSV (used by the lab's "save" button).</summary>
        public static void WriteCsv(CombatAttackTimingTable table)
        {
            if (table == null)
            {
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Header));
            foreach (var entry in table.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id))
                {
                    continue;
                }

                var cells = new[]
                {
                    Escape(entry.id),
                    Escape(entry.note),
                    Format(entry.windupDelay),
                    Format(entry.impactDelay),
                    Format(entry.deathDelay),
                    Format(entry.playerHitStopSeconds),
                    Format(entry.monsterHitStopSeconds),
                    Format(entry.lethalHitStopSeconds),
                    Format(entry.visualImpactOffset),
                    Format(entry.shakeImpactOffset),
                    Format(entry.hitStopImpactOffset)
                };
                sb.AppendLine(string.Join(",", cells));
            }

            var fullPath = Path.GetFullPath(CsvPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, sb.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(CsvPath);
        }

        public static CombatAttackTimingTable ResolveOrCreateTable()
        {
            var preferred = AssetDatabase.LoadAssetAtPath<CombatAttackTimingTable>(PreferredAssetPath);
            if (preferred != null)
            {
                return preferred;
            }

            var guids = AssetDatabase.FindAssets("t:CombatAttackTimingTable");
            if (guids.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<CombatAttackTimingTable>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }

            var dir = Path.GetDirectoryName(PreferredAssetPath);
            if (!AssetDatabase.IsValidFolder(dir))
            {
                Directory.CreateDirectory(Path.GetFullPath(dir));
                AssetDatabase.Refresh();
            }

            var created = ScriptableObject.CreateInstance<CombatAttackTimingTable>();
            AssetDatabase.CreateAsset(created, PreferredAssetPath);
            AssetDatabase.SaveAssets();
            return created;
        }

        private static void Apply(ref CombatAttackTimingTable.OptionalFloat field, string cell)
        {
            if (TryParse(cell, out var value))
            {
                field.Set(value);
            }
            else
            {
                field.Clear();
            }
        }

        private static bool TryParse(string cell, out float value)
        {
            value = 0f;
            return !string.IsNullOrWhiteSpace(cell)
                && float.TryParse(cell.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Format(CombatAttackTimingTable.OptionalFloat field)
        {
            return field.HasValue ? field.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string Escape(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }

            return raw.Contains(",") || raw.Contains("\"")
                ? "\"" + raw.Replace("\"", "\"\"") + "\""
                : raw;
        }

        private static List<Row> ParseCsv(IReadOnlyList<string> lines)
        {
            var rows = new List<Row>();
            if (lines == null || lines.Count == 0)
            {
                return rows;
            }

            var header = DevEditorCsv.SplitLine(lines[0]);
            int Idx(string name) => Array.FindIndex(header, h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));

            int idCol = Idx("id");
            if (idCol < 0)
            {
                Debug.LogError("combat_attack_timing.csv header missing required 'id' column.");
                return rows;
            }

            int noteCol = Idx("note");
            int windupCol = Idx("windup");
            int impactCol = Idx("impact");
            int deathCol = Idx("death");
            int playerHsCol = Idx("player_hitstop");
            int monsterHsCol = Idx("monster_hitstop");
            int lethalHsCol = Idx("lethal_hitstop");
            int visualCol = Idx("visual_offset");
            int shakeCol = Idx("shake_offset");
            int hitStopCol = Idx("hitstop_offset");

            for (var i = 1; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var cells = DevEditorCsv.SplitLine(lines[i]);
                string Cell(int col) => col >= 0 && col < cells.Length ? cells[col].Trim() : string.Empty;

                rows.Add(new Row
                {
                    Id = Cell(idCol),
                    Note = Cell(noteCol),
                    Windup = Cell(windupCol),
                    Impact = Cell(impactCol),
                    Death = Cell(deathCol),
                    PlayerHitStop = Cell(playerHsCol),
                    MonsterHitStop = Cell(monsterHsCol),
                    LethalHitStop = Cell(lethalHsCol),
                    VisualOffset = Cell(visualCol),
                    ShakeOffset = Cell(shakeCol),
                    HitStopOffset = Cell(hitStopCol)
                });
            }

            return rows;
        }

        private struct Row
        {
            public string Id;
            public string Note;
            public string Windup;
            public string Impact;
            public string Death;
            public string PlayerHitStop;
            public string MonsterHitStop;
            public string LethalHitStop;
            public string VisualOffset;
            public string ShakeOffset;
            public string HitStopOffset;
        }
    }
}
#endif
