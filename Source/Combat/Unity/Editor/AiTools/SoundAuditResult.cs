#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `sound-audit` MCP tool. Three-way cross-reference of
    // cue (SoundCatalog entry) x raise site (source scan) x clip (audio asset), which is the only way a
    // silently-unused cue or a silently-unheard sound becomes visible: the runtime tolerates both a
    // missing cue and a null clip by design, so neither shows up as an error.
    public sealed class SoundAuditResult
    {
        public string catalogPath = string.Empty;
        public int totalEntries;
        public int deprecatedCount;

        // --- Catalog-internal integrity ---
        // Cue ids used by more than one entry. Only the first wins at lookup; the rest are unreachable.
        public List<string> duplicateCueIds = new();
        // Non-deprecated entries with no clip assigned — requested at runtime, silent in practice.
        public List<string> entriesWithoutClip = new();

        // --- Cue x raise site ---
        // Non-deprecated cues no scanned source file mentions at all (defect class B-2: authored but dead).
        public List<string> cuesNeverRaised = new();
        // Cues mentioned in source, but never on a line that looks like an emission (only comparisons,
        // trace records, serialized defaults, lookup tables). Weaker signal than never-raised: the
        // emission-context test is a line-window keyword heuristic, so verify before acting.
        public List<string> cuesReferencedButNotEmitted = new();
        // Cue ids raised from code (AudioCueIds constants and the monster.* helper families) that have no
        // catalog entry — they resolve to MissingCue and are silently dropped.
        public List<string> raisedCuesMissingFromCatalog = new();

        // --- Cue x clip ---
        // Raised cues still pointing at a placeholder clip: they play, but they play the stand-in.
        public List<string> placeholderCuesInUse = new();
        // Placeholder clips no catalog entry and no CSV references — leftovers from earlier passes.
        public List<string> unreferencedPlaceholderClips = new();
        // Audio assets outside the placeholder folder that nothing references (catalog, scene/prefab
        // serialization, or a data CSV). Candidates for deletion or for wiring up.
        public List<string> unreferencedAudioAssets = new();
        // Non-audio files sitting inside the audio asset roots (e.g. a Windows .lnk shortcut), which Unity
        // imports as DefaultAsset and which no pipeline can use.
        public List<string> foreignFilesInAudioRoots = new();
        // One clip serving several cues. Legitimate when the moments are meant to sound alike, a defect
        // when two distinct moments become indistinguishable — the audit reports the pairing and leaves
        // that call to a human.
        public List<string> clipsSharedByMultipleCues = new();

        // --- Scan provenance (so a reader can check the scan itself, not just trust it) ---
        public List<string> scannedScriptRoots = new();
        public List<string> scannedAudioRoots = new();
        public int scannedScriptFileCount;
        public int scannedAudioAssetCount;
        // Files excluded from the raise-site scan: the cue-id declaration file and the runtime fallback
        // catalog builder both mention every cue id without raising any of them.
        public List<string> excludedFromRaiseScan = new();

        // Per-cue detail with the evidence behind each verdict.
        public List<SoundCueAuditRow> cues = new();
    }

    // One catalog cue, with how it is raised and what it plays.
    public sealed class SoundCueAuditRow
    {
        public string cueId = string.Empty;
        public string displayName = string.Empty;
        public string bus = string.Empty;
        public bool deprecated;
        public string clipPath = string.Empty;
        public bool clipIsPlaceholder;
        public float volume;
        public float pitchMin;
        public float pitchMax;
        public float cooldownSeconds;
        // Constant | MonsterFamily | Literal | None — how the scan found this cue in source.
        public string raiseKind = "None";
        public int raiseSiteCount;
        public int emissionSiteCount;
        // "path:line  <trimmed source line>", capped per cue so the result stays readable.
        public List<string> raiseSites = new();
    }
}
