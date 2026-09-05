#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEditor;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_SceneBindingAudit
    {
        private static readonly string[] DefaultFields =
        {
            "cardCatalogAsset",
            "terrainPalette",
            "catalogTextAssetSource",
            "startingDeckAsset",
            "soundCatalog",
            "cameraProfile",
        };

        [AiTool
        (
            "scene-binding-audit",
            Title = "Scene Binding / Audit",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("한 컨트롤러의 직렬화 에셋 참조가 두 씬 사이에서 일치하는지 씬 YAML을 스크래핑해 비교한다" +
            "(씬 로드 없음). 출하 씬 드리프트 검출용 읽기 전용 감사. 참고: 정본 씬 대 legacy 샌드박스 비교의 " +
            "divergence는 정상일 수 있다(해석은 사용자 몫).")]
        public SceneBindingAuditResult Audit
        (
            [Description("컨트롤러 C# 타입명. 기본 MapCombatController.")]
            string controllerTypeName = "MapCombatController",
            [Description("씬 A 경로(정본).")]
            string sceneA = "Assets/Scenes/Game/MainGameplay.unity",
            [Description("씬 B 경로(비교 대상).")]
            string sceneB = "Assets/Scenes/Dev/PrototypeTest.unity",
            [Description("비교할 직렬화 필드(쉼표 구분). 비우면 기본 6종.")]
            string fields = ""
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var result = new SceneBindingAuditResult
                {
                    controllerTypeName = controllerTypeName,
                    sceneA = sceneA,
                    sceneB = sceneB
                };

                var scriptGuid = ResolveScriptGuid(controllerTypeName);
                if (scriptGuid == null)
                {
                    throw new InvalidOperationException(
                        $"Could not resolve a MonoScript guid for type '{controllerTypeName}'.");
                }

                result.controllerScriptGuid = scriptGuid;

                if (!File.Exists(sceneA))
                {
                    throw new InvalidOperationException($"Scene A not found: '{sceneA}'.");
                }

                if (!File.Exists(sceneB))
                {
                    throw new InvalidOperationException($"Scene B not found: '{sceneB}'.");
                }

                var needle = "guid: " + scriptGuid;
                var blockA = ExtractYamlDocumentContaining(sceneA, needle);
                var blockB = ExtractYamlDocumentContaining(sceneB, needle);
                result.sceneAHasController = blockA != null;
                result.sceneBHasController = blockB != null;

                if (blockA == null)
                {
                    result.notes.Add($"Scene A has no {controllerTypeName} component; nothing to compare.");
                }

                if (blockB == null)
                {
                    result.notes.Add($"Scene B has no {controllerTypeName} component; nothing to compare.");
                }

                if (blockA == null || blockB == null)
                {
                    return result;
                }

                var fieldList = string.IsNullOrWhiteSpace(fields)
                    ? DefaultFields
                    : fields.Split(',').Select(field => field.Trim()).Where(field => field.Length > 0).ToArray();

                foreach (var field in fieldList)
                {
                    var guidA = ExtractReferenceGuid(blockA, field);
                    var guidB = ExtractReferenceGuid(blockB, field);

                    if (string.Equals(guidA, guidB, StringComparison.OrdinalIgnoreCase))
                    {
                        result.matched.Add($"{field}: {Describe(guidA)}");
                    }
                    else
                    {
                        result.diverged.Add($"{field}: A={Describe(guidA)} vs B={Describe(guidB)}");
                    }
                }

                return result;
            });
        }

        private static string? ResolveScriptGuid(string typeName)
        {
            foreach (var guid in AssetDatabase.FindAssets($"t:MonoScript {typeName}"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass()?.Name == typeName)
                {
                    return guid;
                }
            }

            return null;
        }

        private static string Describe(string? guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return "(none / scene-local)";
            }

            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? $"{guid} (missing asset)" : $"{guid} ({path})";
        }

        // Mirrors MainGameplaySceneAuthorityTests.ExtractYamlDocumentContaining: returns the YAML
        // document (--- !u! delimited) that contains the needle, or null.
        private static string? ExtractYamlDocumentContaining(string assetPath, string needle)
        {
            using var reader = new StreamReader(assetPath);
            var current = new StringBuilder();
            string? match = null;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("--- !u!"))
                {
                    if (match == null && current.Length > 0 && current.ToString().Contains(needle))
                    {
                        match = current.ToString();
                    }

                    current.Clear();
                }

                current.AppendLine(line);
            }

            if (match == null && current.ToString().Contains(needle))
            {
                match = current.ToString();
            }

            return match;
        }

        // Mirrors MainGameplaySceneAuthorityTests.ExtractReferenceGuid: captures the referenced asset
        // guid for a field, or null when the reference is scene-local / unset (fileID: 0, no guid).
        private static string? ExtractReferenceGuid(string yamlBlock, string fieldName)
        {
            var match = Regex.Match(
                yamlBlock,
                $@"^\s*{Regex.Escape(fieldName)}:\s*\{{fileID:\s*-?\d+(?:,\s*guid:\s*([0-9a-f]+))?",
                RegexOptions.Multiline);
            if (!match.Success)
            {
                return null;
            }

            return match.Groups[1].Success ? match.Groups[1].Value : null;
        }
    }
}
