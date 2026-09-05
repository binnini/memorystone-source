using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Locks the 카드 샌드박스 rules surface (docs/card-sandbox-scene-plan.md). These APIs only ever run from the
    /// editor-only debug panel, so nothing else would notice them rotting; the contracts that matter are that they
    /// set up the situation they claim to, and that they stay out of the way of everything else in the combat
    /// (the hand, map-authored monsters, shipped play).
    /// </summary>
    public sealed class CardSandboxDebugApiTests
    {
        // MonsterCatalogEntry is a struct, so an empty catalog is detected by count rather than by a null entry.
        private static string FirstMonsterDefinitionId(CombatState state)
        {
            var entries = state.MonsterCatalog?.Entries;
            if (entries == null || entries.Count == 0)
            {
                return "sandbox-dummy";
            }

            return string.IsNullOrWhiteSpace(entries[0].Id) ? "sandbox-dummy" : entries[0].Id;
        }

        [Test]
        public void SpawnPlacesLiveMonsterOnFreeTileAndTracksIt()
        {
            var state = CombatState.CreateDefaultDemo();
            var before = state.Monsters.Count;

            Assert.That(state.DebugTryFindFreeCoordNearPlayer(1, out var coord), Is.True, "Demo map must offer a free tile.");
            Assert.That(state.DebugSandboxSpawnMonsterAt(FirstMonsterDefinitionId(state), coord, 25, out var spawnedId), Is.True);

            Assert.That(state.Monsters.Count, Is.EqualTo(before + 1));
            Assert.That(state.DebugSandboxMonsterIds, Does.Contain(spawnedId));

            var spawned = state.Monsters.Single(monster => monster.Id == spawnedId);
            Assert.That(spawned.Coord, Is.EqualTo(coord));
            Assert.That(spawned.MaxHp, Is.EqualTo(25), "An explicit HP must win over the catalog value.");
            Assert.That(spawned.IsDead, Is.False);
        }

        [Test]
        public void SpawnRefusesOccupiedAndOffMapTiles()
        {
            var state = CombatState.CreateDefaultDemo();
            var definitionId = FirstMonsterDefinitionId(state);

            Assert.That(
                state.DebugSandboxSpawnMonsterAt(definitionId, state.PlayerCoord, 10, out _),
                Is.False,
                "The player's own tile is occupied.");

            var offMap = new SeoulPlayup.Map.Runtime.HexCoord(99, 99);
            Assert.That(state.DebugSandboxSpawnMonsterAt(definitionId, offMap, 10, out _), Is.False);
            Assert.That(state.DebugSandboxMonsterIds, Is.Empty, "A refused spawn must not be tracked.");
        }

        [Test]
        public void RemoveTakesBackOnlySandboxSpawnedMonsters()
        {
            var state = CombatState.CreateDefaultDemo();
            var authored = state.Monsters.Select(monster => monster.Id).ToList();
            Assert.That(authored, Is.Not.Empty, "The demo state ships with one monster to protect.");

            Assert.That(state.DebugTryFindFreeCoordNearPlayer(1, out var coord), Is.True);
            Assert.That(state.DebugSandboxSpawnMonsterAt(FirstMonsterDefinitionId(state), coord, 10, out _), Is.True);

            var removed = state.DebugRemoveSandboxMonsters();

            Assert.That(removed, Is.EqualTo(1));
            Assert.That(state.DebugSandboxMonsterIds, Is.Empty);
            Assert.That(
                state.Monsters.Select(monster => monster.Id),
                Is.EquivalentTo(authored),
                "Map-authored monsters must survive a sandbox cleanup.");
        }

        [Test]
        public void StatusInjectionReachesPlayerAndEveryLivingMonster()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.DebugApplyStatusToPlayer(StatusEffectKind.Poison, 3, 2), Is.True);
            Assert.That(
                state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Poison && effect.TargetUnitId == state.Player.Id),
                Is.True);

            var living = state.Monsters.Count(monster => !monster.IsDead);
            Assert.That(state.DebugApplyStatusToAllMonsters(StatusEffectKind.Stun, 2, 0), Is.EqualTo(living));
            foreach (var monster in state.Monsters.Where(monster => !monster.IsDead))
            {
                Assert.That(
                    state.ActiveEffects.Any(effect => effect.Kind == StatusEffectKind.Stun && effect.TargetUnitId == monster.Id),
                    Is.True,
                    $"{monster.Id} should be stunned.");
            }
        }

        [Test]
        public void ExileFillGrowsThePileWithoutTouchingTheHand()
        {
            var state = CombatState.CreateDefaultDemo();
            var handBefore = state.ActionDeck.Hand.Select(card => card.InstanceId).ToList();
            var pileBefore = state.GetExilePileCards().Count;

            var added = state.DebugFillExilePile(4);

            Assert.That(added, Is.EqualTo(4));
            Assert.That(state.GetExilePileCards().Count, Is.EqualTo(pileBefore + 4));
            Assert.That(
                state.ActionDeck.Hand.Select(card => card.InstanceId),
                Is.EqualTo(handBefore),
                "Filler cards are injected and exiled in one step; the hand must come out unchanged.");
        }

        [Test]
        public void PlayerDamageGoesThroughBlockFirst()
        {
            var state = CombatState.CreateDefaultDemo();
            var hpBefore = state.Player.Hp;

            var applied = state.DebugDamagePlayer(7);

            Assert.That(applied, Is.GreaterThan(0));
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - applied));
            Assert.That(state.DebugDamagePlayer(0), Is.Zero, "Non-positive damage is a no-op.");
        }

        [Test]
        public void HandlerRegistrationReadoutMatchesTheCardClassRegistry()
        {
            var state = CombatState.CreateDefaultDemo();

            // A12 전염병 overrides its attack-damage hook; plain attack/defend deliberately run the generic path.
            Assert.That(state.DebugIsCardHandled("A12"), Is.True);
            Assert.That(state.DebugIsCardHandled("A00"), Is.False);
            Assert.That(state.DebugIsCardHandled("D00"), Is.False);
            Assert.That(state.DebugIsCardHandled(string.Empty), Is.False);

            var handled = state.DebugHandledCardIds;
            Assert.That(handled, Does.Contain("A12"));
            Assert.That(handled, Does.Contain("S03"));
            Assert.That(handled, Does.Contain("U03"));
            Assert.That(
                handled,
                Is.EqualTo(handled.OrderBy(id => id, System.StringComparer.Ordinal).ToList()),
                "The readout is displayed as-is, so it must come out sorted.");
        }

        /// <summary>
        /// 설치(field) cards are the dispatcher's second keying scheme: they resolve by CardFieldObjectKind, not by
        /// effect ref. Reading them through the effect-ref check alone reported every field card as unhandled — a
        /// false alarm that would have made the whole readout untrustworthy exactly where P4 added F04/F05.
        /// </summary>
        [Test]
        public void FieldCardsAreReportedThroughTheirFieldObjectKind()
        {
            var state = CombatState.CreateDefaultDemo();
            var fieldCards = state.CardCatalog.Entries
                .Where(entry => entry.FieldObjectKind != CardFieldObjectKind.None)
                .ToList();

            Assume.That(fieldCards, Is.Not.Empty, "The catalog under test should ship at least one 설치 card.");

            foreach (var entry in fieldCards)
            {
                var label = state.DebugDescribeCardHandling(entry);
                Assert.That(label, Does.StartWith("설치 핸들러"), $"{entry.Id} should be judged by its field kind.");
                Assert.That(
                    label,
                    Does.Not.Contain("없음"),
                    $"{entry.Id} declares {entry.FieldObjectKind} but no handler is registered for it; " +
                    "resolving that card would throw at runtime.");
            }
        }

        [Test]
        public void NonFieldCardsFallBackToTheCardClassReadout()
        {
            var state = CombatState.CreateDefaultDemo();
            var plague = state.CardCatalog.Entries.FirstOrDefault(entry => entry.Id == "A12");
            var scoutBasics = state.CardCatalog.Entries.FirstOrDefault(entry => entry.Id == "S00");

            if (plague != null)
            {
                Assert.That(state.DebugDescribeCardHandling(plague), Is.EqualTo("핸들러"));
            }

            if (scoutBasics != null)
            {
                // S00 정찰의 기초 intentionally has no handler: revealing is all it does.
                Assert.That(state.DebugDescribeCardHandling(scoutBasics), Is.EqualTo("기본"));
            }

            Assert.That(state.DebugDescribeCardHandling(null), Is.EqualTo("(없음)"));
        }

        // ── Card tuning replay (timing lab): put a card in hand and pick a legal target for it ──

        [Test]
        public void PrepareForTuningPutsTheCardInHandAndRefundsTheActionPhase()
        {
            var state = CombatState.CreateDefaultDemo();
            var scoutId = state.CardCatalog.Entries
                .First(entry => entry.ActionType == CardEffectType.Scout).Id;

            Assert.That(state.DebugPrepareCardForTuning(scoutId, out var card, out var reason), Is.True, reason);

            Assert.That(card.Id, Is.EqualTo(scoutId));
            Assert.That(state.ActionDeck.Hand, Does.Contain(card));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(state.ActionCostRemaining, Is.EqualTo(state.Config.ActionBudget));
        }

        [Test]
        public void PrepareForTuningLeavesTheRunDeckAlone()
        {
            // Unlike a reward grant, auditioning a card in the lab must not change what the run owns.
            var state = CombatState.CreateDefaultDemo();
            var cardId = state.CardCatalog.Entries.First(entry => entry.ActionType == CardEffectType.Scout).Id;
            var deckBefore = state.PlayerDeck;

            Assert.That(state.DebugPrepareCardForTuning(cardId, out _, out _), Is.True);

            Assert.That(state.PlayerDeck, Is.SameAs(deckBefore), "Tuning replay must not grant the card to the run.");
        }

        [Test]
        public void PrepareForTuningReusesACardAlreadyInHandInsteadOfDuplicating()
        {
            var state = CombatState.CreateDefaultDemo();
            var cardId = state.CardCatalog.Entries.First(entry => entry.ActionType == CardEffectType.Scout).Id;

            Assert.That(state.DebugPrepareCardForTuning(cardId, out var first, out _), Is.True);
            var handCountAfterFirst = state.ActionDeck.Hand.Count;

            Assert.That(state.DebugPrepareCardForTuning(cardId, out var second, out _), Is.True);

            Assert.That(second, Is.SameAs(first));
            Assert.That(state.ActionDeck.Hand.Count, Is.EqualTo(handCountAfterFirst));
        }

        [Test]
        public void PrepareForTuningEntersTheMovementPhaseForAMovementCard()
        {
            // Move cards are gated to PlayerMovement; the tuning restore lands on PlayerAction, so without the
            // phase switch a move card would be injected and then refused ("can only be used during movement").
            var state = CombatState.CreateDefaultDemo();
            var moveId = state.CardCatalog.Entries
                .First(entry => entry.PhaseAvailability == CardUsePhase.Movement).Id;

            Assert.That(state.DebugPrepareCardForTuning(moveId, out var card, out var reason), Is.True, reason);

            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.MovementDeck.Hand, Does.Contain(card));
        }

        [Test]
        public void PrepareForTuningRejectsAnUnknownCard()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.DebugPrepareCardForTuning("NOPE-999", out var card, out var reason), Is.False);
            Assert.That(card, Is.Null);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void TargetPickerReturnsOnlyCellsTheValidatorAccepts()
        {
            var state = CombatState.CreateDefaultDemo();
            var scoutId = state.CardCatalog.Entries.First(entry => entry.ActionType == CardEffectType.Scout).Id;
            Assert.That(state.DebugPrepareCardForTuning(scoutId, out _, out _), Is.True);

            Assert.That(
                state.DebugTryPickTargetForTuning(4, coord => state.ValidateScoutTarget(coord, scoutId).IsValid, out var target),
                Is.True,
                "The demo map must offer at least one legal scout target.");
            Assert.That(state.ValidateScoutTarget(target, scoutId).IsValid, Is.True);
        }

        [Test]
        public void TargetPickerFailsRatherThanReturningAnIllegalCell()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.DebugTryPickTargetForTuning(3, _ => false, out _), Is.False);
            Assert.That(state.DebugTryPickTargetForTuning(3, null, out _), Is.False);
        }

        [Test]
        public void CatalogCardChoicesAreScopedToTheRequestedEffectType()
        {
            var state = CombatState.CreateDefaultDemo();

            var scouts = state.DebugGetCatalogCardChoices(CardEffectType.Scout);

            Assert.That(scouts, Is.Not.Empty);
            foreach (var choice in scouts)
            {
                var entry = state.CardCatalog.Entries.Single(candidate => candidate.Id == choice.Key);
                Assert.That(entry.ActionType, Is.EqualTo(CardEffectType.Scout));
            }
        }
    }
}
