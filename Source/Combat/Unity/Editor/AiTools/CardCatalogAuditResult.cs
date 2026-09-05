#nullable enable

namespace AIGD
{
    // Structured result for the `card-catalog-audit` MCP tool. Runs the real cards.csv through
    // the CardCatalogAsset parse/validate pipeline (no scene load) and reports the outcome.
    public sealed class CardCatalogAuditResult
    {
        public string cardsCsvPath = string.Empty;
        public string choiceOptionsCsvPath = string.Empty;
        // ParseCsvText succeeded (header/format ok).
        public bool parsedOk;
        public int rowCount;
        public int choiceOptionRowCount;
        // Full CardCatalogAsset.ValidateRows pass (rows + choice options + CardCatalogDefinition.Validate).
        public bool valid;
        // First failure reason from ValidateRows; empty when valid.
        public string failureReason = string.Empty;
        // Entry count of the built CardCatalogDefinition.
        public int entryCount;
        public CardBindingEvidence bindingEvidence = new();
    }
}
