using System;
using System.IO;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Single-slot disk persistence for <see cref="CombatSuspendEnvelope"/> — the ② full-snapshot suspend
    /// slot (default location: <c>Application.persistentDataPath</c>), physically separate from the ①
    /// <see cref="PlayerRunSaveStore"/>. Writes serialize with JsonUtility into a temp file that atomically
    /// replaces the slot, so a crash mid-write never corrupts an existing save. Reads defend against
    /// missing/corrupt/incompatible files by returning false with a reason; a broken file is left in place
    /// for inspection until the next successful save or delete. Callers own user-facing logging.
    /// </summary>
    public sealed class CombatSuspendStore
    {
        public const string DefaultFileName = "combat-suspend-save.json";

        private readonly string filePath;

        public CombatSuspendStore() : this(Application.persistentDataPath) { }

        public CombatSuspendStore(string directory, string fileName = DefaultFileName)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Directory is required.", nameof(directory));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("File name is required.", nameof(fileName));
            filePath = Path.Combine(directory, fileName);
        }

        public string FilePath => filePath;
        private string TempFilePath => filePath + ".tmp";

        public bool HasSave => File.Exists(filePath);

        public bool TrySave(CombatSuspendEnvelope envelope, out string reason)
        {
            if (envelope == null)
            {
                reason = "Envelope is null.";
                return false;
            }

            if (!envelope.IsValid(out reason))
            {
                return false;
            }

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(TempFilePath, JsonUtility.ToJson(envelope));
                if (File.Exists(filePath))
                {
                    File.Replace(TempFilePath, filePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(TempFilePath, filePath);
                }

                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                reason = $"Save failed: {exception.Message}";
                return false;
            }
        }

        public bool TryLoad(out CombatSuspendEnvelope envelope, out string reason)
        {
            envelope = null;
            if (!File.Exists(filePath))
            {
                reason = "No save file exists.";
                return false;
            }

            try
            {
                var parsed = JsonUtility.FromJson<CombatSuspendEnvelope>(File.ReadAllText(filePath));
                if (parsed == null)
                {
                    reason = "Save file parsed to nothing.";
                    return false;
                }

                if (!parsed.IsValid(out reason))
                {
                    return false;
                }

                envelope = parsed;
                reason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                reason = $"Load failed: {exception.Message}";
                return false;
            }
        }

        public void Delete()
        {
            TryDeleteFile(TempFilePath);
            TryDeleteFile(filePath);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"CombatSuspendStore could not delete '{path}': {exception.Message}");
            }
        }
    }
}
