#nullable enable
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using AIGD;
using com.IvanMurzak.McpPlugin;
using com.IvanMurzak.ReflectorNet.Utils;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Editor.AiTools
{
    [AiToolType]
    public partial class Tool_NamingLint
    {
        [AiTool
        (
            "naming-lint",
            Title = "Naming / Lint",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [Description("tools/naming-lint/lint.py를 실행해 네이밍 규칙(csv-id 포맷·파일명·금지 패턴) 위반을 JSON으로 반환한다. " +
            "baseline 허용목록은 통과, 신규 위반만 보고하는 읽기 전용 게이트 래퍼. python3 필요.")]
        public NamingLintResult Lint
        (
            [Description("python 인터프리터. 기본 python3.")]
            string pythonExecutable = "python3"
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var result = new NamingLintResult();

                var projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                var scriptPath = Path.Combine(projectRoot, "tools", "naming-lint", "lint.py");
                if (!File.Exists(scriptPath))
                {
                    result.summary = $"lint.py not found at '{scriptPath}'.";
                    return result;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExecutable,
                    Arguments = $"\"{scriptPath}\"",
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                var output = new StringBuilder();
                try
                {
                    using var process = new Process { StartInfo = startInfo };
                    process.Start();
                    output.Append(process.StandardOutput.ReadToEnd());
                    var stderr = process.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(stderr))
                    {
                        output.AppendLine(stderr);
                    }

                    if (!process.WaitForExit(30000))
                    {
                        process.Kill();
                        result.summary = "naming-lint timed out after 30s.";
                        result.rawOutput = output.ToString().Trim();
                        return result;
                    }

                    result.toolRan = true;
                    result.exitCode = process.ExitCode;
                }
                catch (Exception ex)
                {
                    result.summary = $"Could not run '{pythonExecutable}': {ex.Message}";
                    return result;
                }

                result.rawOutput = output.ToString().Trim();
                ParseOutput(result);
                result.rulesPassed = result.exitCode == 0 && result.violationCount == 0;
                return result;
            });
        }

        // lint.py prints a summary line plus "  - <id>: <path> — <reason>" per violation.
        private static void ParseOutput(NamingLintResult result)
        {
            foreach (var rawLine in result.rawOutput.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("- "))
                {
                    result.violations.Add(trimmed.Substring(2).Trim());
                }
                else if (trimmed.StartsWith("naming-lint:") && string.IsNullOrEmpty(result.summary))
                {
                    result.summary = trimmed;
                }
            }

            result.violationCount = result.violations.Count;
        }
    }
}
