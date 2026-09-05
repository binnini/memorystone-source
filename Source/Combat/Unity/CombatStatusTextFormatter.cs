using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    internal static class CombatStatusTextFormatter
    {
        public static string FormatModelStatus(
            CombatPhase phase,
            bool isSequencePlaying,
            string resolvingStatusText,
            string selectedBoardEvidenceText)
        {
            var boardText = string.IsNullOrEmpty(selectedBoardEvidenceText) ? string.Empty : $" | {selectedBoardEvidenceText}";
            return isSequencePlaying && !string.IsNullOrEmpty(resolvingStatusText)
                ? $"Phase: {phase} | Resolving...{boardText}"
                : $"Phase: {phase}{boardText}";
        }
    }
}
