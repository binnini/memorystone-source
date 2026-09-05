#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `hex-map-validate` MCP tool. Wraps HexMapValidationReport
    // (SeoulPlayup.MapDesign.Editor) into severity-bucketed message lists for JSON return.
    public sealed class HexMapValidateResult
    {
        public string mapSourcePath = string.Empty;
        public string atlasCatalogPath = string.Empty;
        public string terrainPalettePath = string.Empty;
        public bool hasErrors;
        public bool hasWarnings;
        public int errorCount;
        public int warningCount;
        public int infoCount;
        public List<string> errors = new();
        public List<string> warnings = new();
        public List<string> info = new();
    }
}
