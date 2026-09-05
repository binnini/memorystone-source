#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Editor.DocsValidation
{
    /// <summary>
    /// Safe, report-only validation hooks for the OpenClaw UVCS docs workflow.
    /// These methods intentionally avoid PlayMode, scene switching, AssetDatabase.Refresh,
    /// and other disruptive operations unless future callers add explicit exclusive-mode APIs.
    /// </summary>
    public static class DocsWorkflowValidator
    {
        [Serializable]
        private sealed class ValidationReport
        {
            public string status = "passed";
            public bool sideEffects = false;
            public string mode = "safe";
            public string generatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            public List<AssetSummary> assets = new List<AssetSummary>();
            public List<string> warnings = new List<string>();
            public List<string> errors = new List<string>();
        }

        [Serializable]
        private sealed class AssetSummary
        {
            public string path;
            public string guid;
            public string type;
            public bool exists;
            public bool isScene;
            public bool isPrefab;
            public bool isScriptableObject;
        }

        public static string ValidateChangedAssetsJson(string newlineSeparatedAssetPaths)
        {
            var report = BuildReport(SplitPaths(newlineSeparatedAssetPaths));
            return JsonUtility.ToJson(report, true);
        }

        public static void ValidateChangedAssetsFile(string inputPath, string outputPath)
        {
            var paths = File.Exists(inputPath) ? File.ReadAllLines(inputPath) : Array.Empty<string>();
            var report = BuildReport(paths);
            File.WriteAllText(outputPath, JsonUtility.ToJson(report, true));
        }

        private static ValidationReport BuildReport(IEnumerable<string> assetPaths)
        {
            var report = new ValidationReport();
            foreach (var assetPath in assetPaths.Select(NormalizeAssetPath).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct())
            {
                var summary = new AssetSummary
                {
                    path = assetPath,
                    guid = AssetDatabase.AssetPathToGUID(assetPath),
                    exists = !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)),
                    isScene = assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase),
                    isPrefab = assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                };
                var assetType = AssetDatabase.GetMainAssetTypeAtPath(assetPath);
                summary.type = assetType != null ? assetType.FullName : "unknown";
                summary.isScriptableObject = assetType != null && typeof(ScriptableObject).IsAssignableFrom(assetType);
                if (!summary.exists) report.warnings.Add($"Asset path is not resolved by AssetDatabase: {assetPath}");
                report.assets.Add(summary);
            }
            if (report.warnings.Count > 0) report.status = "warning";
            return report;
        }

        private static IEnumerable<string> SplitPaths(string newlineSeparatedAssetPaths)
        {
            return (newlineSeparatedAssetPaths ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string NormalizeAssetPath(string path)
        {
            path = (path ?? string.Empty).Trim().Replace('\\', '/');
            var index = path.IndexOf("Assets/", StringComparison.Ordinal);
            return index >= 0 ? path.Substring(index) : path;
        }
    }
}
#endif
