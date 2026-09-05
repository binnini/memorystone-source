#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `presentation-duration-audit` MCP tool. Re-measures the actual assets and
    // compares them against the baked length data (docs/presentation-duration-data-plan.md P3).
    //
    // Compared by CONTENT, never by file timestamp: mtime is unreliable across VCS operations, which is the
    // same reason Tool_CsvCatalogIntegrity compares baked catalogs by content signature.
    public sealed class PresentationDurationAuditResult
    {
        public string vfxCatalogPath = string.Empty;
        public string soundCatalogPath = string.Empty;

        public bool allOk;

        // --- VFX: catalog vs a fresh measurement of each prefab ---
        public int vfxEntries;
        public int vfxMeasured;
        public int vfxResolvable;

        // Entries whose stored measuredLengthSeconds disagrees with a fresh measurement of the prefab.
        // Means the prefab changed (or a new one was assigned) and the bake was not re-run.
        public List<string> vfxStaleMeasurements = new();

        // Entries carrying a measuredLengthSeconds the baker could never have written, because the asset is
        // not measurable (loop cue / no ParticleSystem / contains a looping system). A non-zero value here
        // was typed by hand, which is exactly what the measured column must never contain.
        public List<string> vfxHandEditedMeasurements = new();

        // Entries where nothing can answer "how long is this?" — no authored length, nothing measurable, and
        // no destroy timer. These are the cues a consumer will have to guess about, so they need an authored
        // value. Excludes loop cues (see loopCuesWithoutAuthoredLength) and unreachable entries (see
        // vfxUnreachableKindTierEntries) — asking anyone to author a length for a cue that can never play
        // would be busywork, and it is what made this list read as 7 items when only 5 were real.
        public List<string> vfxWithoutAnyLength = new();

        // Kind-tier entries shadowed by an earlier entry with the same (EffectKind, targetFilter) key.
        // TryResolve/ResolveAll return the FIRST match in that tier, so these can never be selected at
        // runtime no matter what is authored on them. Informational here (a catalog-shape problem, not a
        // duration problem) but excluded from the authoring list above.
        public List<string> vfxUnreachableKindTierEntries = new();

        // Loop cues with no authored length. INFORMATIONAL, and deliberately excluded from allOk: a loop cue
        // plays until its status is removed, so it has no length to state — and the one-shot resolution paths
        // (TryResolve/ResolveAll) exclude loop cues entirely, so no consumer can ever ask one for a duration.
        // Requiring an authored value here would mean inventing numbers nothing reads.
        public List<string> loopCuesWithoutAuthoredLength = new();

        // --- CSV vs catalog (a rebuild would overwrite the catalog from these rows) ---
        public List<string> csvCatalogMismatches = new();

        // --- Sound: catalog vs a fresh measurement of each clip ---
        public int soundEntries;
        public int soundMeasured;
        public List<string> soundStaleMeasurements = new();
        public List<string> soundWithoutAnyLength = new();
        // Cues whose pitch is randomized, so their stored length is an upper bound and not an exact value.
        // Informational, not a defect — consumers needing a tight number must check HasDeterministicLength.
        public List<string> soundInexactByPitchRange = new();

        // --- Animation: CSV columns vs a fresh measurement of the FBX clips ---
        public int animationClipsMeasured;
        public List<string> animationStaleMeasurements = new();
        // Rows naming a clip that no referenced FBX contains.
        public List<string> animationMissingClips = new();

        // Human-readable one-line summary, so a caller can log a verdict without walking every list.
        public string summary = string.Empty;
    }
}
