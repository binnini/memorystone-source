using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Coverage classification for <see cref="CardVfxCoverage"/>, plus a guard on the shipping data so a
    /// newly authored card that renders nothing (or only the generic fallback) is caught here instead of
    /// passing silently through the catalog's EffectKind fallback tier.
    /// </summary>
    public sealed class CardVfxCoverageTests
    {
        private readonly UnityObjectScope scope = new UnityObjectScope();

        [TearDown]
        public void TearDown()
        {
            scope.Dispose();
        }

        [Test]
        public void CardWithCueKeyedOnItsIdIsDedicated()
        {
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, sourceRef: "A01", cueId: "CVA01"));

            var report = CardVfxCoverage.Evaluate(Row("A01", type: "공격", damage: "3"), catalog);

            Assert.That(report.Status, Is.EqualTo(CardVfxCoverageStatus.Dedicated));
            Assert.That(report.MatchedCueIds, Is.EqualTo(new[] { "CVA01" }));
            Assert.That(report.MatchKeys, Is.EqualTo(new[] { "cardId:A01" }));
        }

        [Test]
        public void FieldCardBorrowingCueViaItsSharedTickKeyIsSharedBehavior()
        {
            // A field card's tick is raised by the field object under the shared field.damage key (its class
            // FieldKind), which is the sourceRef F01's cue is keyed on, so F05 renders F01's explosion rather
            // than a generic burst when it has no card-scoped row of its own.
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, sourceRef: "field.damage", cueId: "CVF01"));

            var report = CardVfxCoverage.Evaluate(Row("F05", type: "필드", damage: "2"), catalog);

            Assert.That(report.Status, Is.EqualTo(CardVfxCoverageStatus.SharedBehavior));
            Assert.That(report.MatchedCueIds, Is.EqualTo(new[] { "CVF01" }));
            Assert.That(report.MatchKeys, Is.EqualTo(new[] { "effectKey:field.damage" }));
        }

        [Test]
        public void CardWithOnlyGenericKindEntryIsFallbackOnly()
        {
            var catalog = CatalogWith(Entry(EffectKind.Damage));

            var report = CardVfxCoverage.Evaluate(Row("A13", type: "공격", damage: "3"), catalog);

            Assert.That(report.Status, Is.EqualTo(CardVfxCoverageStatus.FallbackOnly));
            Assert.That(report.MatchedCueIds, Is.Empty);
            Assert.That(report.FallbackKinds, Does.Contain("Damage"));
        }

        [Test]
        public void CardWithNoDerivableEffectKindIsNoVfx()
        {
            var catalog = CatalogWith(Entry(EffectKind.Damage));

            var report = CardVfxCoverage.Evaluate(Row("U01", type: "유틸리티"), catalog);

            Assert.That(report.Status, Is.EqualTo(CardVfxCoverageStatus.NoVfx));
            Assert.That(report.ProbedKinds, Is.Empty);
        }

        [Test]
        public void DeprecatedAndPrefablessEntriesDoNotCountAsCoverage()
        {
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, sourceRef: "A01", cueId: "deprecated", deprecated: true),
                Entry(EffectKind.Damage, sourceRef: "A01", cueId: "prefabless", withPrefab: false));

            var report = CardVfxCoverage.Evaluate(Row("A01", type: "공격", damage: "3"), catalog);

            Assert.That(report.Status, Is.EqualTo(CardVfxCoverageStatus.NoVfx));
            Assert.That(report.MatchedCueIds, Is.Empty);
        }

        [Test]
        public void LoopCueDoesNotCountAsOneShotCoverage()
        {
            // Loop cues resolve on a separate path (TryResolveStatusLoop) and never serve a card's one-shot
            // presentation, so counting them would overstate coverage.
            var catalog = CatalogWith(
                Entry(EffectKind.StatusEffectApplied, sourceRef: "A11", cueId: "loop", loop: true));

            var report = CardVfxCoverage.Evaluate(
                Row("A11", type: "공격", damage: "2"),
                catalog);

            Assert.That(report.MatchedCueIds, Is.Empty);
            Assert.That(report.ProbedKinds, Does.Contain("StatusEffectApplied"));
        }

        [Test]
        public void KeyedCueOfAnUnrelatedKindDoesNotCountAsCoverage()
        {
            // A status cue keyed on this card's id must not be credited to a plain damage card: the kind gate
            // is what keeps a card-id key from claiming a status cue the card never triggers.
            var catalog = CatalogWith(
                Entry(EffectKind.StatusEffectApplied, sourceRef: "A13", cueId: "player.status.hit.A13"),
                Entry(EffectKind.Damage));

            var report = CardVfxCoverage.Evaluate(Row("A13", type: "공격", damage: "3"), catalog);

            Assert.That(report.MatchedCueIds, Is.Empty);
            Assert.That(report.Status, Is.EqualTo(CardVfxCoverageStatus.FallbackOnly));
        }

        [Test]
        public void MoveCardGrantingABuffProbesStatusApplied()
        {
            // M05 추진력 is a move card whose only cue is the buff it grants; buff_debuff is the signal.
            var catalog = CatalogWith(
                Entry(EffectKind.StatusEffectApplied, sourceRef: "M05", cueId: "CVM05"));

            var report = CardVfxCoverage.Evaluate(
                Row("M05", type: "이동", buffDebuff: "Agility:2"), catalog);

            Assert.That(report.ProbedKinds, Does.Contain("StatusEffectApplied"));
            Assert.That(report.Status, Is.EqualTo(CardVfxCoverageStatus.Dedicated));
            Assert.That(report.MatchedCueIds, Is.EqualTo(new[] { "CVM05" }));
        }

        [Test]
        public void CardScopedCueCountsAsDedicatedAndIgnoresTheSharedKey()
        {
            // F05 overrides the shared field.damage look via sourceCardId. It must read as the card's own cue,
            // and must not be handed to F01 (which reaches field.damage through its shared tick key).
            var catalog = CatalogWith(
                Entry(EffectKind.Damage, sourceRef: "field.damage", cueId: "CVF01"),
                Entry(EffectKind.Damage, sourceRef: "field.damage", sourceCardId: "F05", cueId: "CVF05"));

            var f05 = CardVfxCoverage.Evaluate(Row("F05", type: "필드", damage: "2"), catalog);
            var f01 = CardVfxCoverage.Evaluate(Row("F01", type: "필드", damage: "6"), catalog);

            Assert.That(f05.Status, Is.EqualTo(CardVfxCoverageStatus.Dedicated));
            Assert.That(f05.MatchedCueIds, Is.EqualTo(new[] { "CVF05" }));
            Assert.That(f01.MatchedCueIds, Is.EqualTo(new[] { "CVF01" }));
        }

        [Test]
        public void DefendCardWithoutShieldNumberStillProbesBlock()
        {
            // D02/D03/D05 author no shield value but still raise Block when they resolve.
            var report = CardVfxCoverage.Evaluate(
                Row("D02", type: "방어"), CatalogWith(Entry(EffectKind.Block)));

            Assert.That(report.ProbedKinds, Does.Contain("Block"));
        }

        [Category("ShippingData")]
        [Test]
        public void ShippingCatalogCoversEveryCardWithSomeVfx()
        {
            var catalog = LoadShippingCatalog();
            var reports = CardVfxCoverage.EvaluateAll(LoadShippingCardRows(), catalog);

            Assert.That(reports, Is.Not.Empty, "cards.csv produced no rows.");

            // A card that renders nothing at all is a defect: the player sees no feedback for the play.
            // Utility cards are the known, accepted exception — they mutate the hand rather than the board,
            // and no effect kind is derivable from their authored columns.
            //
            // 상태 카드(C-17)도 예외다. 애초에 **사용할 수 없는 카드**라 "플레이의 피드백"이라는 것이 없다 —
            // 손패에 놓여 있는 것 자체가 효과이고, 그 표현은 카드 UI(불길한 아우라) 몫이지 VFX 큐가 아니다.
            // 깨진 유리만 턴말 피해 연출을 내는데, 그건 카드 플레이가 아니라 피해 이벤트로 나간다.
            var silent = reports
                .Where(report => report.Status == CardVfxCoverageStatus.NoVfx
                                 && report.CardType != "유틸리티"
                                 && report.CardType != "저주") // 구 '상태' — T2 개편
                .Select(report => $"{report.CardId} {report.CardName}")
                .ToList();

            Assert.That(silent, Is.Empty, $"Cards with no VFX at all: {string.Join(", ", silent)}");
        }

        /// <summary>
        /// Scout cards all reveal fog, so they should read as one card type: same reveal cue, same pacing,
        /// same resolve sound. S00/S03/S04 had no reveal cue of their own and fell through to the generic
        /// FogReveal fallback, revealing instantly while S01/S02 waited 0.7s — and they were missing from the
        /// scout-source list, so they never played card.scout.resolve either.
        /// </summary>
        /// <summary>
        /// 안개를 걷지 않는 정찰 카드의 <b>명시 목록</b>(휴리스틱 금지 — 안개를 걷지 않는 정찰 카드를
        /// 또 만드는 것은 의식적인 결정이어야 한다). 2026-08-20 #5에서 S05가 「범위 1을 탐색하고
        /// 발견된 함정을 해체합니다」로 바뀌며 진짜 정찰이 되어 목록에서 빠졌다 — 현재 비어 있다.
        /// </summary>
        private static readonly string[] NonRevealingScoutCardIds = { };

        [Category("ShippingData")]
        [Test]
        public void EveryScoutCardSharesTheSameRevealCueAndPacing()
        {
            var catalog = LoadShippingCatalog();
            var scoutIds = CardVfxCoverage.EvaluateAll(LoadShippingCardRows(), catalog)
                .Where(report => report.CardType == "정찰" && !NonRevealingScoutCardIds.Contains(report.CardId))
                .Select(report => report.CardId)
                .ToList();

            Assert.That(scoutIds, Is.Not.Empty, "cards.csv produced no 정찰 rows.");

            var delays = new List<float>();
            foreach (var cardId in scoutIds)
            {
                Assert.That(
                    CombatEffectSourceClassifier.IsScoutCardSource(cardId, ShippingCardCatalogSource.Load()),
                    Is.True,
                    $"{cardId} is a 정찰 card but is not classified as one, so it plays no scout resolve cue.");

                var reveal = catalog.Entries.SingleOrDefault(entry =>
                    entry.Kind == EffectKind.FogReveal && entry.SourceRef == cardId);
                Assert.That(reveal, Is.Not.Null, $"{cardId} has no dedicated FogReveal cue and falls back to the generic one.");
                delays.Add(reveal.PlaybackDelaySeconds);
            }

            Assert.That(
                delays.Distinct().Count(),
                Is.EqualTo(1),
                $"Scout reveals must share one pacing; found delays {string.Join(", ", delays)}.");
        }

        /// <summary>
        /// A field tick raises an area announce (on the field, no amount) plus the per-unit effects that
        /// carry the real numbers. Both belong to one beat, so both cues must share a delay — CVF03/CVF03M
        /// were authored that way and read correctly. Where the per-unit cue was simply missing, the effect
        /// fell through to the generic cue at delay 0 and its numbers appeared over a second *before* the
        /// blast that caused them (measured on F01 and F04), wearing a sword-slash VFX on a bomb field.
        /// </summary>
        [Test]
        public void FieldTickPerUnitCuesSharePacingWithTheirAreaAnnounce()
        {
            var catalog = LoadShippingCatalog();

            foreach (var tickRef in new[]
                     {
                         CardEffectRefs.FieldDamage,
                         CardEffectRefs.FieldHeal,
                         CardEffectRefs.FieldImmobilizeFlashbang,
                         CardEffectRefs.FieldLifesteal
                     })
            {
                var forRef = catalog.Entries
                    .Where(entry => !entry.Loop && entry.SourceRef == tickRef)
                    .ToList();

                var announces = forRef.Where(entry => entry.TargetFilter == EffectVfxTargetFilter.Field).ToList();
                var perUnit = forRef.Where(entry => entry.TargetFilter != EffectVfxTargetFilter.Field).ToList();

                Assert.That(announces, Is.Not.Empty, $"{tickRef} has no area announce cue.");
                Assert.That(
                    perUnit,
                    Is.Not.Empty,
                    $"{tickRef} has no per-unit cue, so its numbers take the generic cue at delay 0 and land ahead of the announce.");

                var delays = forRef.Select(entry => entry.PlaybackDelaySeconds).Distinct().ToList();
                Assert.That(
                    delays.Count,
                    Is.EqualTo(1),
                    $"{tickRef} cues must share one delay so the tick reads as one beat; found {string.Join(", ", delays)}.");
            }
        }

        /// <summary>
        /// S01 지뢰찾기 raises its reveal before its damage precisely so the play reads as
        /// reveal → area → hit (see CombatState.TryPlayerScout). Presentation can overturn that: the cues
        /// carry their own delays, and while the explosion was authored at 0 and the reveal at 0.7 the mine
        /// detonated roughly half a second *before* 밝혀짐! appeared. Ordering that the rules layer states
        /// explicitly is worth a guard on the authored data.
        /// </summary>
        [Test]
        public void MinefinderExplosionIsAuthoredToLandAfterItsReveal()
        {
            var catalog = LoadShippingCatalog();

            var reveal = catalog.Entries.Single(entry => entry.CueId == "CVS01");
            var explosion = catalog.Entries.Single(entry => entry.CueId == "CVS01D");

            Assert.That(reveal.Kind, Is.EqualTo(EffectKind.FogReveal));
            Assert.That(explosion.Kind, Is.EqualTo(EffectKind.Damage));
            Assert.That(
                explosion.PlaybackDelaySeconds,
                Is.GreaterThan(reveal.PlaybackDelaySeconds),
                "S01's damage cue must be delayed past its reveal cue, or the mine detonates before it is found.");
        }

        private static CardCatalogCsvRow Row(
            string id,
            string type = "",
            string damage = "",
            string shield = "",
            string heal = "",
            string stateEffect = "",
            string buffDebuff = "")
        {
            // CardCatalogCsvRow's fields are serialized and private, so the CSV parser is the supported way to
            // build one. Columns are paired name-to-value here so a header/value length mismatch is impossible.
            var columns = new (string Header, string Value)[]
            {
                ("id", id),
                ("name", $"test-{id}"),
                ("description", string.Empty),
                ("type", type),
                ("target", "self"),
                ("cost", "1"),
                ("range", "1"),
                ("shape", string.Empty),
                ("damage", damage),
                ("shield", shield),
                ("heal", heal),
                ("stateEffect", stateEffect),
                ("buff_debuff", buffDebuff),
                ("duration", string.Empty),
                ("hitCount", string.Empty),
                ("costMode", string.Empty),
                ("scalingMode", string.Empty),
                ("gameplayType", string.Empty),
                ("targeting", string.Empty),
                ("status", string.Empty),
                ("includeInDecks", string.Empty),
                ("visibleInCatalog", string.Empty),
                ("illustrationId", string.Empty),
                ("rarity", string.Empty),
                ("choiceTexts", string.Empty),
                ("descriptionUpgraded", string.Empty)
            };

            var header = string.Join(",", columns.Select(column => column.Header));
            var line = string.Join(",", columns.Select(column => column.Value));
            return CardCatalogAsset.ParseCsvText($"{header}\n{line}").Single();
        }

        private EffectVfxCatalog CatalogWith(params EffectVfxCatalog.Entry[] entries)
        {
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            scope.Track(catalog);
            catalog.SetEntries(entries);
            return catalog;
        }

        private EffectVfxCatalog.Entry Entry(
            EffectKind kind,
            string sourceRef = "",
            string cueId = "",
            bool deprecated = false,
            bool loop = false,
            bool withPrefab = true,
            string sourceCardId = "")
        {
            GameObject[] prefabs;
            if (withPrefab)
            {
                var prefab = new GameObject($"vfx-{cueId}-{kind}");
                scope.Track(prefab);
                prefabs = new[] { prefab };
            }
            else
            {
                prefabs = new GameObject[] { null };
            }

            return new EffectVfxCatalog.Entry(
                kind,
                prefabs,
                sourceRef: sourceRef,
                cueId: cueId,
                deprecated: deprecated,
                loop: loop,
                sourceCardId: sourceCardId);
        }

        private static EffectVfxCatalog LoadShippingCatalog()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<EffectVfxCatalog>(
                "Assets/Resources/Combat/DefaultEffectVfxCatalog.asset");
            Assert.That(catalog, Is.Not.Null, "Shipping EffectVfxCatalog asset is missing.");
            return catalog;
        }

        private static IReadOnlyList<CardCatalogCsvRow> LoadShippingCardRows()
        {
            Assert.That(File.Exists(CombatCsvPaths.CardsCsv), Is.True, $"Missing {CombatCsvPaths.CardsCsv}.");
            return CardCatalogAsset.ParseCsvText(
                File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true)));
        }
    }
}
