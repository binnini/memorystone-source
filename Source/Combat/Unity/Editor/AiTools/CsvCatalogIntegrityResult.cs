#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `csv-catalog-integrity` MCP tool. Converts each shipping combat CSV
    // domain through its real converter and reports whether it still converts cleanly (convert core),
    // plus a bake-staleness check for domains that bake to a ScriptableObject `.asset`. Id-format
    // validation stays out of scope here — that overlaps naming-lint's csv-id rules (use naming-lint).
    public sealed class CsvCatalogIntegrityResult
    {
        public string requestedDomain = string.Empty;
        public int domainsChecked;
        public int domainsFailed;
        public bool allOk;
        // Number of baked domains whose baked `.asset` is older than its source CSV(s).
        public int staleCount;
        // True when any checked baked domain is stale. Reported independently of allOk (convert core).
        public bool anyStale;
        public List<CsvDomainResult> domains = new();
    }
}
