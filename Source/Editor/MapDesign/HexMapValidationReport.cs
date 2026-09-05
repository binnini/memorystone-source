using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.MapDesign.Editor
{
    public enum HexMapValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct HexMapValidationItem
    {
        public HexMapValidationItem(HexMapValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message ?? string.Empty;
        }

        public HexMapValidationSeverity Severity { get; }
        public string Message { get; }
    }

    public sealed class HexMapValidationReport
    {
        private readonly List<HexMapValidationItem> items = new List<HexMapValidationItem>();

        public IReadOnlyList<HexMapValidationItem> Items => items;
        public IEnumerable<HexMapValidationItem> Errors => items.Where(item => item.Severity == HexMapValidationSeverity.Error);
        public IEnumerable<HexMapValidationItem> Warnings => items.Where(item => item.Severity == HexMapValidationSeverity.Warning);
        public bool HasErrors => Errors.Any();
        public bool HasWarnings => Warnings.Any();

        public void AddInfo(string message) => items.Add(new HexMapValidationItem(HexMapValidationSeverity.Info, message));
        public void AddWarning(string message) => items.Add(new HexMapValidationItem(HexMapValidationSeverity.Warning, message));
        public void AddError(string message) => items.Add(new HexMapValidationItem(HexMapValidationSeverity.Error, message));
    }
}
