using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    // Core ② full-snapshot fidelity guard (RS-11): a mutated combat state is snapshotted, restored into a
    // freshly built state, and every persisted field is asserted individually. Both element counts AND
    // element contents are checked so a dropped list item (which the existing save-roundtrip-check cannot
    // catch for collections) fails the test.
    //
    // The monster is placed far from the player and given tiny detection ranges so it classifies as Dormant
    // after restore. RefreshMonsterIntentStep (run at the tail of restore, mirroring the constructor) skips
    // the intent/attack-pattern recompute for Dormant monsters, so the restored coordinate/HP/FSM/cooldown/
    // attack-pattern-index are observed exactly as persisted rather than being re-derived.
    public sealed class CombatSuspendRoundtripTests
    {
        // vision=1, chase=1, attackRange=1 → simulatedDistance = max(1+1, 1*2) = 2. A monster > 2 tiles away
        // and unrevealed is Dormant.
        private static CombatConfig NarrowRangeConfig() =>
            new CombatConfig(80, 30, 2, 1, 4, 4, 1, 1, 5, 4, 1, 5, 1, 1);

        private static HexMapData CreateLineMap()
        {
            var cells = Enumerable.Range(0, 7)
                .Select(q => new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false))
                .ToArray();
            return new HexMapData(
                cells,
                monsterSpawnRefs: new[] { new HexMonsterSpawnRef("spawn-primary", "M001", new HexCoord(4, 0), "primary_pressure") });
        }

        private static CombatState CreateState(HexMapData map, CombatConfig config)
        {
            var catalog = CombatState.CreateMonsterCatalog(config);
            var monsterConfigs = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, catalog);
            return CombatStateFixture.OnMap(map)
                .WithConfig(config)
                .WithMonsters(monsterConfigs.ToArray())
                .WithMonsterCatalog(catalog)
                .Build();
        }

        // Mirrors the resume boot path in MapCombatController.InitializeIntegration: the state is built with
        // drawOpeningHands:false (which arms playerTurnDrawPending), then RestoreFromSuspend rebuilds it.
        private static CombatState CreateResumeState(HexMapData map, CombatConfig config)
        {
            var catalog = CombatState.CreateMonsterCatalog(config);
            var monsterConfigs = CombatState.ResolveMonsterConfigsFromBoardSpawns(map, config, catalog);
            return CombatStateFixture.OnMap(map)
                .WithConfig(config)
                .WithMonsters(monsterConfigs.ToArray())
                .WithMonsterCatalog(catalog)
                .WithoutOpeningHands()
                .Build();
        }

        // Regression guard for the resume double-draw defect: the resumed state must NOT append a second
        // opening draw on top of the restored hand when the start-of-turn sequence runs. Exercises the real
        // trigger (StartPlayerTurn) that CombatSuspendRoundtripTests bypasses, and asserts both the pile
        // counts and the exact card order of both decks equal the persisted zones after the redraw point.
        [Test]
        public void ResumeStartPlayerTurnDoesNotRedrawOverRestoredHand()
        {
            var config = NarrowRangeConfig();
            var map = CreateLineMap();

            var source = CreateState(map, config);
            var snapshot = source.CreateSuspendSnapshot();

            var expectedMove = FlattenZones(snapshot.Player.Decks.MovementZones);
            var expectedAction = FlattenZones(snapshot.Player.Decks.ActionZones);
            var expectedMoveHand = snapshot.Player.Decks.MovementZones.Hand.Count;
            var expectedActionHand = snapshot.Player.Decks.ActionZones.Hand.Count;
            // Guard the guard: the source must actually hold opening hands, otherwise a double-draw could
            // not be observed.
            Assert.That(expectedMoveHand + expectedActionHand, Is.GreaterThan(0), "source state should have opening hands");

            var resume = CreateResumeState(map, config);
            resume.RestoreFromSuspend(snapshot);
            // The real start-of-turn redraw trigger. With the fix this is a no-op for hands; without it,
            // DrawNewTurnHands would append MovementHandSize/ActionHandSize cards on top of the restored hand.
            resume.StartPlayerTurn();

            Assert.That(resume.MovementDeck.HandCount, Is.EqualTo(expectedMoveHand));
            Assert.That(resume.ActionDeck.HandCount, Is.EqualTo(expectedActionHand));

            var after = resume.CreateSuspendSnapshot();
            Assert.That(FlattenZones(after.Player.Decks.MovementZones), Is.EqualTo(expectedMove));
            Assert.That(FlattenZones(after.Player.Decks.ActionZones), Is.EqualTo(expectedAction));
        }

        [Test]
        public void RestoreFromSuspendReproducesMutatedState()
        {
            var config = NarrowRangeConfig();
            var map = CreateLineMap();

            var source = CreateState(map, config);
            var snapshot = source.CreateSuspendSnapshot();
            var monsterId = snapshot.Monsters.Single().Id;

            // --- Mutate the persisted payload to values distinct from a fresh construction. ---
            snapshot.OverallTurn = 7;
            snapshot.Phase = CombatPhase.PlayerAction;
            snapshot.ActionCostRemaining = 2;
            snapshot.PendingMovementRangeBonus = 1;
            snapshot.ActiveMovementRangeModifier = 2;
            snapshot.LastMovedDistance = 4;
            snapshot.ActionCardsUsedThisTurn = 6;
            snapshot.ObjectiveCompleted = true;
            snapshot.MarkedMonsterId = monsterId;
            snapshot.ConsumedTrapIds = new List<string> { "trap-a", "trap-b" };
            snapshot.ClaimedEventObjectIds = new List<string> { "evt-1" };

            snapshot.Player.Vitals.Hp = 50;
            snapshot.Player.Vitals.Block = 5;
            snapshot.Player.Position.Q = 2;
            snapshot.Player.Position.R = 0;

            var monster = snapshot.Monsters.Single();
            var persistedMaxHp = monster.MaxHp;
            // Far from the restored player at (2,0): distance 4 > simulatedDistance 2 → Dormant, so the
            // intent recompute at the end of restore leaves the monster's persisted fields untouched.
            monster.Q = 6;
            monster.R = 0;
            monster.Hp = 7;
            monster.Block = 2;
            monster.AttackPatternIndex = 2;
            monster.FsmState = MonsterFsmState.Chase;
            monster.FsmPreAlertState = MonsterFsmState.Patrol;
            monster.HasLastKnownPlayerCoord = true;
            monster.LastKnownPlayerQ = 1;
            monster.LastKnownPlayerR = 0;
            monster.SearchTurnsRemaining = 2;
            monster.AlertTurnsRemaining = 3;
            monster.AlertRangeBonus = 1;
            monster.PatrolCursor = 4;
            monster.AttackPatternCooldowns = new List<MonsterCooldownSaveData>
            {
                new MonsterCooldownSaveData { Index = 0, Remaining = 2 }
            };

            snapshot.ActiveEffects = new List<ActiveEffectSaveData>
            {
                new ActiveEffectSaveData { Type = EffectType.Duration, Kind = StatusEffectKind.Poison, TargetUnitId = "player", RemainingTurns = 3, Amount = 2 },
                new ActiveEffectSaveData { Type = EffectType.Duration, Kind = StatusEffectKind.Stun, TargetUnitId = monsterId, RemainingTurns = 1 }
            };

            snapshot.Visibility.Add(new HexCellVisibilitySaveData(new HexCoord(5, 0), HexCellVisibility.Revealed));

            var expectedMoveOrder = FlattenZones(snapshot.Player.Decks.MovementZones);
            var expectedActionOrder = FlattenZones(snapshot.Player.Decks.ActionZones);

            // --- Restore into a brand-new state built from the same board. ---
            var restored = CreateState(map, config);
            restored.RestoreFromSuspend(snapshot);
            var after = restored.CreateSuspendSnapshot();

            // Context.
            Assert.That(restored.OverallTurnNumber, Is.EqualTo(7));
            Assert.That(restored.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(restored.CurrentKi, Is.EqualTo(2));
            Assert.That(restored.ObjectiveCompleted, Is.True);
            Assert.That(after.PendingMovementRangeBonus, Is.EqualTo(1));
            Assert.That(after.ActiveMovementRangeModifier, Is.EqualTo(2));
            Assert.That(after.LastMovedDistance, Is.EqualTo(4));
            Assert.That(after.ActionCardsUsedThisTurn, Is.EqualTo(6));
            Assert.That(restored.ConsumedTrapIds, Is.EquivalentTo(new[] { "trap-a", "trap-b" }));
            Assert.That(restored.ClaimedEventObjectIds, Is.EquivalentTo(new[] { "evt-1" }));

            // Player unit.
            Assert.That(restored.Player.Hp, Is.EqualTo(50));
            Assert.That(restored.Player.Block, Is.EqualTo(5));
            Assert.That(restored.PlayerCoord, Is.EqualTo(new HexCoord(2, 0)));

            // Marked monster rebinding by id.
            Assert.That(restored.MarkedMonsterCoord, Is.EqualTo(new HexCoord(6, 0)));

            // Monster (recompute-stable because Dormant).
            var aMon = after.Monsters.Single(m => m.Id == monsterId);
            Assert.That(aMon.Q, Is.EqualTo(6));
            Assert.That(aMon.R, Is.EqualTo(0));
            Assert.That(aMon.Hp, Is.EqualTo(7));
            Assert.That(aMon.Block, Is.EqualTo(2));
            Assert.That(aMon.MaxHp, Is.EqualTo(persistedMaxHp));
            Assert.That(aMon.AttackPatternIndex, Is.EqualTo(2));
            Assert.That(aMon.FsmState, Is.EqualTo(MonsterFsmState.Chase));
            Assert.That(aMon.FsmPreAlertState, Is.EqualTo(MonsterFsmState.Patrol));
            Assert.That(aMon.HasLastKnownPlayerCoord, Is.True);
            Assert.That(aMon.LastKnownPlayerQ, Is.EqualTo(1));
            Assert.That(aMon.SearchTurnsRemaining, Is.EqualTo(2));
            Assert.That(aMon.AlertTurnsRemaining, Is.EqualTo(3));
            Assert.That(aMon.AlertRangeBonus, Is.EqualTo(1));
            Assert.That(aMon.PatrolCursor, Is.EqualTo(4));
            Assert.That(aMon.AttackPatternCooldowns, Has.Count.EqualTo(1));
            Assert.That(aMon.AttackPatternCooldowns[0].Index, Is.EqualTo(0));
            Assert.That(aMon.AttackPatternCooldowns[0].Remaining, Is.EqualTo(2));

            // Active effects (player + monster, both count and content).
            Assert.That(restored.ActiveEffects, Has.Count.EqualTo(2));
            Assert.That(restored.ActiveEffects.Any(e => e.Kind == StatusEffectKind.Poison && e.TargetUnitId == "player" && e.RemainingTurns == 3 && e.Amount == 2), Is.True);
            Assert.That(restored.ActiveEffects.Any(e => e.Kind == StatusEffectKind.Stun && e.TargetUnitId == monsterId), Is.True);

            // Visibility memory.
            Assert.That(restored.GetVisibility(new HexCoord(5, 0)), Is.EqualTo(HexCellVisibility.Revealed));

            // Card zone ORDER preservation across all four piles of both decks.
            Assert.That(FlattenZones(after.Player.Decks.MovementZones), Is.EqualTo(expectedMoveOrder));
            Assert.That(FlattenZones(after.Player.Decks.ActionZones), Is.EqualTo(expectedActionOrder));
            Assert.That(expectedMoveOrder.Count + expectedActionOrder.Count, Is.GreaterThan(0));
        }

        [Test]
        public void RestoreFromSuspendReproducesFieldObjectsAndReblocksTiles()
        {
            var config = NarrowRangeConfig();
            var map = CreateLineMap();

            var source = CreateState(map, config);
            // Two active field objects (deployed field-effect tiles) + one pending object.
            source.FieldObjects.Add(new FieldObject(new HexCoord(3, 0), 1, 2, FieldObjectKind.FieldDamage, 4, "player", "F02", hitsPerTick: 2));
            source.FieldObjects.Add(new FieldObject(new HexCoord(5, 0), 0, 1, FieldObjectKind.FogReveal, 0, "monster-1", ""));
            source.PendingFieldObjects.Add(new FieldObject(new HexCoord(4, 0), 2, 3, FieldObjectKind.MassImmobilize, 1, "player", "F07"));

            var snapshot = source.CreateSuspendSnapshot();

            var restored = CreateState(map, config);
            restored.RestoreFromSuspend(snapshot);

            Assert.That(restored.FieldObjects.Objects, Has.Count.EqualTo(2));
            var first = restored.FieldObjects.Objects.Single(f => f.Position.Equals(new HexCoord(3, 0)));
            Assert.That(first.Radius, Is.EqualTo(1));
            Assert.That(first.RemainingTurns, Is.EqualTo(2));
            Assert.That(first.Kind, Is.EqualTo(FieldObjectKind.FieldDamage));
            Assert.That(first.Value, Is.EqualTo(4));
            Assert.That(first.SourceUnitId, Is.EqualTo("player"));
            Assert.That(first.VisualRef, Is.EqualTo("F02"));
            // F05's hits-per-tick must survive suspend/resume, or a resumed 콩콩탄탄 quietly halves itself.
            Assert.That(first.HitsPerTick, Is.EqualTo(2));

            var second = restored.FieldObjects.Objects.Single(f => f.Position.Equals(new HexCoord(5, 0)));
            Assert.That(second.Kind, Is.EqualTo(FieldObjectKind.FogReveal));
            Assert.That(second.RemainingTurns, Is.EqualTo(1));
            Assert.That(second.SourceUnitId, Is.EqualTo("monster-1"));
            Assert.That(second.HitsPerTick, Is.EqualTo(1), "A field that authored no hitCount resumes as a single-hit tick.");

            Assert.That(restored.PendingFieldObjects.Objects, Has.Count.EqualTo(1));
            var pending = restored.PendingFieldObjects.Objects.Single();
            Assert.That(pending.Position, Is.EqualTo(new HexCoord(4, 0)));
            Assert.That(pending.Kind, Is.EqualTo(FieldObjectKind.MassImmobilize));
            Assert.That(pending.Radius, Is.EqualTo(2));
            Assert.That(pending.RemainingTurns, Is.EqualTo(3));
            Assert.That(pending.VisualRef, Is.EqualTo("F07"));

            // UpdateOccupancy (run at the tail of restore) must re-derive the movement-blocking tiles from
            // the restored active field objects.
            Assert.That(restored.RuntimeStates.TryGetValue(new HexCoord(3, 0), out var blockedTile), Is.True);
            Assert.That(blockedTile.TemporaryBlocked, Is.True);
        }

        [Test]
        public void RestoreFromSuspendReplacesVisibilityInsteadOfAddingToIt()
        {
            var config = NarrowRangeConfig();
            var map = CreateLineMap();

            var source = CreateState(map, config);
            var snapshot = source.CreateSuspendSnapshot();
            // Persist only a single far cell as Revealed; everything else (including the start-spawn cells a
            // fresh boot reveals) is Unknown in this snapshot.
            snapshot.Visibility.Clear();
            snapshot.Visibility.Add(new HexCellVisibilitySaveData(new HexCoord(5, 0), HexCellVisibility.Revealed));

            var restored = CreateState(map, config);
            // Fresh boot reveals the start-spawn area — a cell the snapshot marks Unknown.
            Assert.That(restored.GetVisibility(new HexCoord(0, 0)), Is.EqualTo(HexCellVisibility.Revealed), "fresh boot should reveal the start cell");

            restored.RestoreFromSuspend(snapshot);

            // Clean replace: the start-spawn reveals are gone (not additive), and only the persisted cell remains.
            Assert.That(restored.GetVisibility(new HexCoord(0, 0)), Is.EqualTo(HexCellVisibility.Unknown));
            Assert.That(restored.GetVisibility(new HexCoord(1, 0)), Is.EqualTo(HexCellVisibility.Unknown));
            Assert.That(restored.GetVisibility(new HexCoord(5, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(restored.VisibilityStates.Count, Is.EqualTo(1));
        }

        // Draw|hand|discard|removed flattened to a zone-tagged instance-id sequence, so a reordering,
        // a dropped card, or a card that migrated between piles all change the result.
        private static List<string> FlattenZones(PlayerDeckZoneSaveData zones)
        {
            var result = new List<string>();
            AppendZone(result, "draw", zones.DrawPile);
            AppendZone(result, "hand", zones.Hand);
            AppendZone(result, "discard", zones.DiscardPile);
            AppendZone(result, "removed", zones.RemovedPile);
            return result;
        }

        private static void AppendZone(List<string> result, string tag, List<PlayerCardInstanceSaveData> pile)
        {
            foreach (var card in pile)
            {
                result.Add($"{tag}:{card.InstanceId}:{card.CardId}");
            }
        }
    }
}
