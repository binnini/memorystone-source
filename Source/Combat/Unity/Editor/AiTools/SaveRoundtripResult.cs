#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `save-roundtrip-check` MCP tool. Saves a source envelope to a TEMP
    // store, loads it back, and deep-diffs — catching serialization field drops without touching the
    // real save slot.
    public sealed class SaveRoundtripResult
    {
        // True when the source was the real on-disk save; false when a synthetic envelope was used.
        public bool sourceHadRealSave;
        public string sourcePath = string.Empty;
        public string tempFilePath = string.Empty;
        public bool saved;
        public bool loaded;
        // No field differences survived the save→load round-trip.
        public bool roundTripEqual;
        // "field.path: before != after" for each difference (capped).
        public List<string> diffFields = new();
        // Failure reason from TrySave/TryLoad; empty on success.
        public string reason = string.Empty;
    }
}
