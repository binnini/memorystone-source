#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Per-domain outcome of `csv-catalog-integrity`: did the real shipping CSV convert cleanly.
    public sealed class CsvDomainResult
    {
        public string name = string.Empty;
        // The domain's converter ran without throwing.
        public bool convertOk;
        // Entries produced by the converter (-1 when unknown or convert failed).
        public int entryCount = -1;
        // Exception type + message when convertOk is false; empty otherwise.
        public string error = string.Empty;
        // CSV files / directories the domain reads (existence is checked before converting).
        public List<string> sourcePaths = new();

        // --- Bake staleness (only meaningful for baked domains) ---
        // The baked ScriptableObject `.asset` the runtime loads for this domain. Empty when the domain
        // is read directly from the source CSV at runtime (TextAsset-backed) and therefore cannot go stale.
        public string bakedAssetPath = string.Empty;
        // True when bakedAssetPath is non-empty and the file exists on disk.
        public bool bakedAssetExists;
        // True when at least one source CSV has a newer last-write time than the baked asset, i.e. the
        // baked asset is stale and the bake menu must be re-run. Always false for non-baked domains.
        public bool bakedAssetStale;
        // Human-readable staleness note (which source is newer, or why the check was skipped).
        public string stalenessNote = string.Empty;
    }
}
