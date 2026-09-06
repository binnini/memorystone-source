#nullable enable
using System.Collections.Generic;

namespace AIGD
{
    // Structured result for the `combat-vfx-audit` MCP tool. Catalog-internal integrity of
    // EffectVfxCatalog: broken/missing prefab references, unusable loop cues, duplicate cue ids, plus a
    // cross-reference of emittable EffectKind/StatusEffectKind against renderable catalog coverage.
    public sealed class CombatVfxAuditResult
    {
        public string catalogPath = string.Empty;
        public int totalEntries;
        public int deprecatedCount;
        // Non-deprecated entries whose prefab array has one or more null slots (broken references).
        public List<string> entriesWithNullPrefabSlot = new();
        // Non-deprecated entries with no usable prefab at all.
        public List<string> entriesWithoutPrefab = new();
        // Loop cues with no usable prefab — they can never render.
        public List<string> loopEntriesWithoutPrefab = new();
        // Cue ids used by more than one entry.
        public List<string> duplicateCueIds = new();

        // --- Emission cross-reference (catalog coverage of emittable effects) ---
        // EffectKind values combat can raise that have no non-deprecated, renderable catalog entry
        // (emitted but no VFX). Some may be intentionally VFX-less — a finding for human judgement.
        public List<string> uncoveredEffectKinds = new();
        // StatusEffectKind values with no non-deprecated, renderable status-apply/loop cue.
        public List<string> uncoveredStatusEffectKinds = new();
        // Note: the reverse direction — flagging catalog entries whose sourceRef matches no emittable id
        // (dead entries) — is out of scope here. It needs a reliable editor-time set of the SourceRef values
        // combat actually emits; without that, entries would be false-flagged as dead, so it is deferred.

        // --- Card VFX coverage (cards.csv x catalog) ---
        // Whether each authored card has its own cue, borrows one via its behaviorId, renders only the
        // generic EffectKind fallback, or renders nothing. The fallback tier means a missing cue never
        // errors at runtime, so without this section an unauthored card passes silently.
        public string cardsCsvPath = string.Empty;
        public int cardsTotal;
        public int cardsDedicated;
        public int cardsSharedBehavior;
        public int cardsFallbackOnly;
        public int cardsNoVfx;
        // Cards that render nothing at all — highest severity, almost always a defect.
        public List<string> cardsWithoutAnyVfx = new();
        // Cards rendering only the generic burst for their kind — usually "not authored yet".
        public List<string> cardsOnFallbackOnly = new();
        // Cards borrowing a cue keyed on their behaviorId (shared look). May be intentional.
        public List<string> cardsSharingBehaviorCue = new();
        // Per-card detail, including the heuristically derived probe kinds behind each verdict.
        public List<CardVfxCoverageRow> cardVfxCoverage = new();
    }

    // One row of the card VFX coverage table. Mirrors CardVfxCoverageReport as a flat serializable shape.
    public sealed class CardVfxCoverageRow
    {
        public string cardId = string.Empty;
        public string cardName = string.Empty;
        public string cardType = string.Empty;
        public string sharedEffectKey = string.Empty;
        public string status = string.Empty;
        public List<string> matchedCueIds = new();
        public List<string> matchKeys = new();
        // EffectKinds derived from the card's authored columns — the assumption behind the verdict, exposed
        // on purpose so a reader can check it rather than trust it.
        public List<string> probedKinds = new();
        public List<string> fallbackKinds = new();
        public List<string> uncoveredKinds = new();
    }
}
