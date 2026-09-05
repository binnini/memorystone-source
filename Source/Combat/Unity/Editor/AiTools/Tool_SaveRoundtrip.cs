#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_SaveRoundtrip
    {
        private const int MaxDiffs = 60;
        private const int MaxDepth = 12;

        [AiTool
        (
            "save-roundtrip-check",
            Title = "Save / Roundtrip Check",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("PlayerRunSaveEnvelope를 임시 폴더 스토어에 저장→로드 왕복시켜 직렬화 필드 누락 회귀를 검출한다. " +
            "기본은 합성 엔벨로프(모든 원시 필드에 고유값)를 사용하고, 실제 세이브가 있으면 그것을 소스로 왕복한다. " +
            "실제 세이브 파일은 절대 쓰지 않는(읽기만) 안전한 읽기 전용 감사.")]
        public SaveRoundtripResult Check
        (
            [Description("실제 세이브를 소스로 쓸 디렉터리(비우면 Application.persistentDataPath). 세이브가 없으면 합성 엔벨로프 사용.")]
            string sourceSaveDirectory = "",
            [Description("왕복 후 임시 파일/폴더 삭제 여부. 기본 true.")]
            bool deleteTempAfter = true
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var result = new SaveRoundtripResult();

                // Source: prefer the real on-disk save (read-only), else a synthetic populated envelope.
                var realDir = string.IsNullOrWhiteSpace(sourceSaveDirectory)
                    ? Application.persistentDataPath
                    : sourceSaveDirectory;
                var realStore = new PlayerRunSaveStore(realDir);
                PlayerRunSaveEnvelope source;
                if (realStore.HasSave && realStore.TryLoad(out var loadedReal, out _))
                {
                    source = loadedReal;
                    result.sourceHadRealSave = true;
                    result.sourcePath = realStore.FilePath;
                }
                else
                {
                    source = BuildSyntheticEnvelope();
                    result.sourceHadRealSave = false;
                }

                // Round-trip through a throwaway temp directory — never the real slot.
                var tempDir = Path.Combine(Path.GetTempPath(), "spu-save-roundtrip-" + Guid.NewGuid().ToString("N"));
                var tempStore = new PlayerRunSaveStore(tempDir);
                result.tempFilePath = tempStore.FilePath;

                result.saved = tempStore.TrySave(source, out var saveReason);
                if (!result.saved)
                {
                    result.reason = saveReason;
                    return result;
                }

                result.loaded = tempStore.TryLoad(out var reloaded, out var loadReason);
                if (!result.loaded)
                {
                    result.reason = loadReason;
                    if (deleteTempAfter)
                    {
                        SafeDeleteDir(tempStore, tempDir);
                    }

                    return result;
                }

                Diff(string.Empty, source, reloaded, result.diffFields, 0);
                result.roundTripEqual = result.diffFields.Count == 0;

                if (deleteTempAfter)
                {
                    SafeDeleteDir(tempStore, tempDir);
                }

                return result;
            });
        }

        private static void SafeDeleteDir(PlayerRunSaveStore store, string dir)
        {
            store.Delete();
            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
            catch
            {
                // Best-effort cleanup; a leftover temp dir is harmless.
            }
        }

        private static PlayerRunSaveEnvelope BuildSyntheticEnvelope()
        {
            var envelope = new PlayerRunSaveEnvelope { Player = new PlayerRunSaveData() };
            Populate(envelope, 0);
            // Keep the envelope structurally valid (IsValid: schema==Current, StageId set, Player!=null).
            envelope.SchemaVersion = PlayerRunSaveEnvelope.CurrentSchemaVersion;
            if (string.IsNullOrWhiteSpace(envelope.StageId))
            {
                envelope.StageId = "save-roundtrip-audit";
            }

            if (envelope.Player == null)
            {
                envelope.Player = new PlayerRunSaveData();
            }

            return envelope;
        }

        // Fills every public primitive/string/enum field with a distinctive non-default value and
        // recurses into nested [Serializable] class fields. Collections are left at their defaults —
        // populating them generically is unnecessary to surface a dropped scalar field.
        private static int seed;

        private static void Populate(object? target, int depth)
        {
            if (target == null || depth > MaxDepth)
            {
                return;
            }

            foreach (var field in target.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var type = field.FieldType;
                if (type == typeof(int))
                {
                    field.SetValue(target, ++seed);
                }
                else if (type == typeof(long))
                {
                    field.SetValue(target, (long)(++seed));
                }
                else if (type == typeof(float))
                {
                    field.SetValue(target, ++seed + 0.5f);
                }
                else if (type == typeof(double))
                {
                    field.SetValue(target, ++seed + 0.5d);
                }
                else if (type == typeof(bool))
                {
                    field.SetValue(target, true);
                }
                else if (type == typeof(string))
                {
                    field.SetValue(target, "v" + (++seed));
                }
                else if (type.IsEnum)
                {
                    var values = Enum.GetValues(type);
                    if (values.Length > 0)
                    {
                        field.SetValue(target, values.GetValue(values.Length - 1));
                    }
                }
                else if (type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type))
                {
                    // Skip collections.
                }
                else if (type.IsClass)
                {
                    var child = field.GetValue(target);
                    if (child == null && type.GetConstructor(Type.EmptyTypes) != null)
                    {
                        child = Activator.CreateInstance(type);
                        field.SetValue(target, child);
                    }

                    Populate(child, depth + 1);
                }
            }
        }

        private static void Diff(string path, object? a, object? b, List<string> diffs, int depth)
        {
            if (diffs.Count >= MaxDiffs || depth > MaxDepth)
            {
                return;
            }

            if (a == null && b == null)
            {
                return;
            }

            if (a == null || b == null)
            {
                diffs.Add($"{path} (null mismatch: {(a == null ? "null" : "set")} vs {(b == null ? "null" : "set")})");
                return;
            }

            var type = a.GetType();
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                if (!a.Equals(b))
                {
                    diffs.Add($"{path}: {a} != {b}");
                }

                return;
            }

            if (a is IEnumerable enumerableA && b is IEnumerable enumerableB)
            {
                var listA = enumerableA.Cast<object>().ToList();
                var listB = enumerableB.Cast<object>().ToList();
                if (listA.Count != listB.Count)
                {
                    diffs.Add($"{path}.Count: {listA.Count} != {listB.Count}");
                    return;
                }

                for (var i = 0; i < listA.Count; i++)
                {
                    Diff($"{path}[{i}]", listA[i], listB[i], diffs, depth + 1);
                }

                return;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Diff(
                    string.IsNullOrEmpty(path) ? field.Name : $"{path}.{field.Name}",
                    field.GetValue(a),
                    field.GetValue(b),
                    diffs,
                    depth + 1);
            }
        }
    }
}
