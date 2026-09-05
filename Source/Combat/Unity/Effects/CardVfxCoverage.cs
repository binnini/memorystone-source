using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// How well a single card is covered by <see cref="EffectVfxCatalog"/>.
    ///
    /// The catalog resolves a cue in four tiers (exact sourceRef → sourceRef prefix → StatusKind →
    /// EffectKind only). The last tier means a card with no authored cue still renders *something*, so a
    /// missing cue never surfaces as an error at runtime — it silently plays the generic burst. This
    /// classification exists to make that difference visible.
    /// </summary>
    public enum CardVfxCoverageStatus
    {
        /// <summary>A catalog entry is keyed on this card's own id — the card has its own authored look.</summary>
        Dedicated,

        /// <summary>
        /// No entry is keyed on the card id, but one is keyed on its behaviorId, so the card renders a look
        /// authored for (and shared with) every other card using that behavior. Intentional for some cards,
        /// an oversight for others — human judgement.
        /// </summary>
        SharedBehavior,

        /// <summary>No sourceRef-keyed entry at all; the card falls through to the generic EffectKind tier.</summary>
        FallbackOnly,

        /// <summary>Not even a generic entry covers the kinds this card is expected to emit — nothing renders.</summary>
        NoVfx
    }

    /// <summary>One card's coverage verdict plus the evidence behind it.</summary>
    public sealed class CardVfxCoverageReport
    {
        public string CardId = string.Empty;
        public string CardName = string.Empty;
        public string CardType = string.Empty;
        public string BehaviorId = string.Empty;
        public CardVfxCoverageStatus Status = CardVfxCoverageStatus.NoVfx;

        /// <summary>Cue ids of the sourceRef-keyed entries that serve this card (empty for fallback/no-VFX).</summary>
        public List<string> MatchedCueIds = new List<string>();

        /// <summary>Which key produced each match, e.g. "cardId:A01" / "behaviorId:field.damage".</summary>
        public List<string> MatchKeys = new List<string>();

        /// <summary>EffectKinds this card is expected to emit, derived from its authored columns (heuristic).</summary>
        public List<string> ProbedKinds = new List<string>();

        /// <summary>Probed kinds served only by a generic EffectKind-tier entry.</summary>
        public List<string> FallbackKinds = new List<string>();

        /// <summary>Probed kinds with no renderable catalog entry at any tier — these render nothing.</summary>
        public List<string> UncoveredKinds = new List<string>();
    }

    /// <summary>
    /// Cross-references the authored card catalog against <see cref="EffectVfxCatalog"/> so cards that render
    /// only a generic fallback (or nothing) are reported instead of passing silently.
    ///
    /// Two things are checked, and they are deliberately independent:
    /// <list type="number">
    /// <item>Is any non-deprecated, renderable entry <b>keyed</b> on this card's id or behaviorId? This is an
    /// exact catalog query with no assumption about what the card emits at runtime.</item>
    /// <item>For cards with no keyed entry, would the generic EffectKind tier render anything? This needs the
    /// set of kinds the card emits, which is <b>derived heuristically</b> from the card's own authored
    /// columns (see <see cref="DeriveProbeKinds"/>) rather than from a hand-kept table that would drift out of
    /// sync with the rules layer. The derived set is reported as <see cref="CardVfxCoverageReport.ProbedKinds"/>
    /// so a reader can see the assumption instead of trusting it.</item>
    /// </list>
    /// Pure logic over already-loaded data: no scene, no asset loading, no side effects.
    /// </summary>
    public static class CardVfxCoverage
    {
        // Status names that, appearing in a behaviorId or postActions entry, imply the card applies a status
        // effect. "cleanse" is deliberately absent: it removes statuses rather than applying one.
        private static readonly string[] StatusApplyHints =
        {
            "immobilize", "stun", "poison", "slow", "rupture", "reflect", "agility", "strength", "plague"
        };

        public static List<CardVfxCoverageReport> EvaluateAll(
            IEnumerable<CardCatalogCsvRow> rows, EffectVfxCatalog catalog)
        {
            var reports = new List<CardVfxCoverageReport>();
            if (rows == null)
            {
                return reports;
            }

            var renderable = RenderableEntries(catalog);
            foreach (var row in rows)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Id))
                {
                    continue;
                }

                reports.Add(Evaluate(row, renderable));
            }

            return reports;
        }

        public static CardVfxCoverageReport Evaluate(CardCatalogCsvRow row, EffectVfxCatalog catalog)
            => Evaluate(row, RenderableEntries(catalog));

        private static CardVfxCoverageReport Evaluate(
            CardCatalogCsvRow row, IReadOnlyList<EffectVfxCatalog.Entry> renderable)
        {
            var cardId = Trim(row.Id);
            var behaviorId = Trim(row.BehaviorId);

            var report = new CardVfxCoverageReport
            {
                CardId = cardId,
                CardName = Trim(row.Name),
                CardType = Trim(row.Type),
                BehaviorId = behaviorId
            };

            var probeKinds = DeriveProbeKinds(row);
            report.ProbedKinds = probeKinds.Select(kind => kind.ToString()).ToList();

            // Tier 1/2 check: entries keyed on the card id win over entries keyed on the behaviorId, because
            // a card-id key is authored for this card specifically while a behaviorId key is shared.
            //
            // A keyed entry only counts when its EffectKind is one this card is expected to emit. Generic
            // behaviorIds are shared by many cards — `attack.damage` alone keys a status cue authored for
            // A11's freeze — so without this gate a plain damage card would be credited with a status cue it
            // never triggers. When no kind could be derived the gate is skipped: there is nothing to filter on.
            CollectKeyedMatches(renderable, cardId, "cardId", isCardIdKey: true, probeKinds, report);
            var dedicated = report.MatchedCueIds.Count > 0;
            if (!dedicated && !string.Equals(behaviorId, cardId, StringComparison.Ordinal))
            {
                CollectKeyedMatches(renderable, behaviorId, "behaviorId", isCardIdKey: false, probeKinds, report);
            }

            foreach (var kind in probeKinds)
            {
                if (HasGenericEntry(renderable, kind))
                {
                    report.FallbackKinds.Add(kind.ToString());
                }
                else if (!HasAnyRenderableEntry(renderable, kind))
                {
                    report.UncoveredKinds.Add(kind.ToString());
                }
            }

            if (dedicated)
            {
                report.Status = CardVfxCoverageStatus.Dedicated;
            }
            else if (report.MatchedCueIds.Count > 0)
            {
                report.Status = CardVfxCoverageStatus.SharedBehavior;
            }
            else if (report.FallbackKinds.Count > 0)
            {
                report.Status = CardVfxCoverageStatus.FallbackOnly;
            }
            else
            {
                report.Status = CardVfxCoverageStatus.NoVfx;
            }

            return report;
        }

        /// <summary>
        /// The EffectKinds a card is expected to emit, read off its own authored columns. Intentionally
        /// coarse — it exists to answer "would anything render at all", not to model the rules layer.
        /// </summary>
        private static List<EffectKind> DeriveProbeKinds(CardCatalogCsvRow row)
        {
            var kinds = new List<EffectKind>();
            var type = Trim(row.Type);
            var behaviorId = Trim(row.BehaviorId).ToLowerInvariant();
            var postActions = Trim(row.PostActions).ToLowerInvariant();

            if (type == "이동")
            {
                kinds.Add(EffectKind.Push);
            }

            if (!string.IsNullOrWhiteSpace(row.Damage))
            {
                kinds.Add(EffectKind.Damage);
            }

            // Defend cards without a shield number (D02/D03/D05) still raise Block when they resolve.
            if (!string.IsNullOrWhiteSpace(row.Shield) || type == "방어")
            {
                kinds.Add(EffectKind.Block);
            }

            if (!string.IsNullOrWhiteSpace(row.Heal))
            {
                kinds.Add(EffectKind.Heal);
            }

            if (type == "정찰")
            {
                kinds.Add(EffectKind.FogReveal);
            }

            // buff_debuff carries the granted status ("Agility:2"), so it is a direct signal — unlike the
            // duration column, which also times non-status things like a field object's lifetime.
            var appliesStatus = !string.IsNullOrWhiteSpace(row.StateEffect)
                || !string.IsNullOrWhiteSpace(row.BuffDebuff)
                || StatusApplyHints.Any(hint => behaviorId.Contains(hint) || postActions.Contains(hint));
            if (appliesStatus)
            {
                kinds.Add(EffectKind.StatusEffectApplied);
            }

            return kinds.Distinct().ToList();
        }

        private static void CollectKeyedMatches(
            IReadOnlyList<EffectVfxCatalog.Entry> renderable,
            string key,
            string keyLabel,
            bool isCardIdKey,
            IReadOnlyList<EffectKind> probeKinds,
            CardVfxCoverageReport report)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            foreach (var entry in renderable)
            {
                if (!entry.HasSourceRefRule && !entry.HasSourceCardIdRule)
                {
                    continue;
                }

                if (probeKinds.Count > 0 && !probeKinds.Contains(entry.Kind))
                {
                    continue;
                }

                // A card-scoped entry is selected by SourceCardId, so it only answers to the cardId key —
                // never to the behaviorId key, whose whole point is being shared across cards.
                bool matches;
                if (entry.HasSourceCardIdRule)
                {
                    matches = isCardIdKey && string.Equals(entry.SourceCardId, key, StringComparison.Ordinal);
                }
                else
                {
                    matches = entry.MatchSourceRefPrefix
                        ? key.StartsWith(entry.SourceRef, StringComparison.Ordinal)
                        : string.Equals(entry.SourceRef, key, StringComparison.Ordinal);
                }

                if (!matches)
                {
                    continue;
                }

                report.MatchedCueIds.Add(DescribeCue(entry));
                report.MatchKeys.Add($"{keyLabel}:{key}");
            }
        }

        // A generic (EffectKind-only) entry: the last resolution tier. Mirrors Entry.MatchesKind's gating
        // minus the target filter, which depends on the concrete event and is not known here.
        private static bool HasGenericEntry(IReadOnlyList<EffectVfxCatalog.Entry> renderable, EffectKind kind)
            => renderable.Any(entry =>
                entry.Kind == kind && !entry.HasSourceRefRule && !entry.MatchStatusKind);

        private static bool HasAnyRenderableEntry(IReadOnlyList<EffectVfxCatalog.Entry> renderable, EffectKind kind)
            => renderable.Any(entry => entry.Kind == kind);

        private static IReadOnlyList<EffectVfxCatalog.Entry> RenderableEntries(EffectVfxCatalog catalog)
        {
            if (catalog == null)
            {
                return Array.Empty<EffectVfxCatalog.Entry>();
            }

            // Loop cues are excluded: they are resolved on a separate path (TryResolveStatusLoop) and never
            // serve a card's one-shot presentation, so counting them would overstate coverage.
            return catalog.Entries
                .Where(entry => entry != null
                    && !entry.Deprecated
                    && !entry.Loop
                    && entry.Prefabs.Any(prefab => prefab != null))
                .ToList();
        }

        private static string DescribeCue(EffectVfxCatalog.Entry entry)
            => string.IsNullOrWhiteSpace(entry.CueId)
                ? $"(unnamed {entry.Kind} cue)"
                : entry.CueId;

        private static string Trim(string value) => value == null ? string.Empty : value.Trim();
    }
}
