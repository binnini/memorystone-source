#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `naming-lint` MCP tool: a thin wrapper over tools/naming-lint/lint.py
    // that surfaces the naming-rule violations as JSON.
    public sealed class NamingLintResult
    {
        // The python interpreter and lint script were found and executed.
        public bool toolRan;
        public int exitCode;
        // No new violations (lint exit code 0).
        public bool rulesPassed;
        public int violationCount;
        public List<string> violations = new();
        // The lint's own summary line (e.g. "naming-lint: OK ...").
        public string summary = string.Empty;
        // Full stdout+stderr (trimmed) for diagnosis.
        public string rawOutput = string.Empty;
    }
}
