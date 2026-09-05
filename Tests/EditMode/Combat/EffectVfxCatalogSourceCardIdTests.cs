using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// The sourceCardId resolution tier. Effects that fire detached from the card that caused them (a field
    /// object ticking each turn, a post-action status) carry a shared behaviour ref, so before this tier the
    /// only way to give one such card its own look was to key on the shared ref — which then applied to every
    /// card using that behaviour.
    /// </summary>
    public sealed class EffectVfxCatalogSourceCardIdTests
    {
        private readonly UnityObjectScope scope = new UnityObjectScope();

        [TearDown]
        public void TearDown()
        {
            scope.Dispose();
        }

        [Test]
        public void CardScopedEntryWinsOverTheSharedSourceRefEntry()
        {
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, cueId: "shared", sourceRef: "field.damage"),
                Entry(EffectKind.Damage, cueId: "card", sourceRef: "field.damage", sourceCardId: "F05"));

            Assert.That(catalog.TryResolve(FieldDamage(sourceCardId: "F05"), out var entry), Is.True);
            Assert.That(entry.CueId, Is.EqualTo("card"));
        }

        [Test]
        public void AnotherCardOnTheSameBehaviourStillGetsTheSharedEntry()
        {
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, cueId: "shared", sourceRef: "field.damage"),
                Entry(EffectKind.Damage, cueId: "card", sourceRef: "field.damage", sourceCardId: "F05"));

            Assert.That(catalog.TryResolve(FieldDamage(sourceCardId: "F01"), out var entry), Is.True);
            Assert.That(entry.CueId, Is.EqualTo("shared"));
        }

        [Test]
        public void CardScopedEntryWithASourceRefRuleDoesNotLeakToTheCardsOtherEffects()
        {
            // F05's override is scoped to the field tick; its placement effect must keep the shared
            // field.placement cue instead of firing the tick's explosion.
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, cueId: "placement", sourceRef: "field.placement"),
                Entry(EffectKind.Damage, cueId: "card", sourceRef: "field.damage", sourceCardId: "F05"));

            var placement = new EffectResultEvent(
                EffectKind.Damage, targetUnitId: "field", sourceRef: "field.placement", sourceCardId: "F05");

            Assert.That(catalog.TryResolve(placement, out var entry), Is.True);
            Assert.That(entry.CueId, Is.EqualTo("placement"));
        }

        [Test]
        public void CardScopedEntryWithoutASourceRefRuleMatchesAnyEffectOfThatCard()
        {
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, cueId: "card", sourceCardId: "F05"));

            Assert.That(catalog.TryResolve(FieldDamage(sourceCardId: "F05"), out var entry), Is.True);
            Assert.That(entry.CueId, Is.EqualTo("card"));
        }

        [Test]
        public void CardScopedEntryIsNotUsedAsAGenericKindFallback()
        {
            // Without the kind-tier exclusion an entry keyed on sourceCardId alone would read as a generic
            // cue and fire for every card, which is the opposite of scoping it to one.
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, cueId: "card", sourceCardId: "F05"));

            Assert.That(catalog.TryResolve(FieldDamage(sourceCardId: "F01"), out _), Is.False);
            Assert.That(catalog.TryResolve(FieldDamage(sourceCardId: string.Empty), out _), Is.False);
        }

        [Test]
        public void EventWithoutASourceCardIdFallsThroughToTheExistingTiers()
        {
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, cueId: "shared", sourceRef: "field.damage"),
                Entry(EffectKind.Damage, cueId: "card", sourceRef: "field.damage", sourceCardId: "F05"));

            Assert.That(catalog.TryResolve(FieldDamage(sourceCardId: string.Empty), out var entry), Is.True);
            Assert.That(entry.CueId, Is.EqualTo("shared"));
        }

        [Test]
        public void ResolveAllReturnsOnlyTheCardScopedEntriesWhenAnyMatch()
        {
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, cueId: "shared", sourceRef: "field.damage"),
                Entry(EffectKind.Damage, cueId: "cardA", sourceRef: "field.damage", sourceCardId: "F05"),
                Entry(EffectKind.Damage, cueId: "cardB", sourceRef: "field.damage", sourceCardId: "F05"));

            var resolved = catalog.ResolveAll(FieldDamage(sourceCardId: "F05"));

            Assert.That(resolved.Length, Is.EqualTo(2));
            Assert.That(new[] { resolved[0].CueId, resolved[1].CueId }, Is.EqualTo(new[] { "cardA", "cardB" }));
        }

        private static EffectResultEvent FieldDamage(string sourceCardId)
            => new EffectResultEvent(
                EffectKind.Damage, targetUnitId: "field", sourceRef: "field.damage", sourceCardId: sourceCardId);

        private EffectVfxCatalog CatalogWith(params EffectVfxCatalog.Entry[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            scope.Track(catalog);
            catalog.SetEntries(entries);
            return catalog;
        }

        private EffectVfxCatalog.Entry Entry(
            EffectKind kind, string cueId, string sourceRef = "", string sourceCardId = "")
        {
            var prefab = new GameObject($"vfx-{cueId}");
            scope.Track(prefab);
            return new EffectVfxCatalog.Entry(
                kind,
                new[] { prefab },
                EffectVfxTargetFilter.Field,
                sourceRef: sourceRef,
                cueId: cueId,
                sourceCardId: sourceCardId);
        }
    }
}
