using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class TurnSequenceTests
    {
        [Test]
        public void PhaseOrderSplitsMonsterMovementBeforePlayerAction()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.TryPlayerAttack(), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("Action cards"));

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.CurrentKi, Is.EqualTo(state.MaxKi - 1));
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterMovement));

            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));

            Assert.That(state.TryPlayerDefend(), Is.True);
            Assert.That(state.Player.Block, Is.EqualTo(5));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterAction));

            state.ResolveMonsterAction();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.Player.Block, Is.EqualTo(0));
        }

        [Test]
        public void MonsterMovesBeforePlayerAction()
        {
            var state = CombatStateFixture.Arena(4).WithEnemyEastAt(3).Build();

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(3, 0)));
            Assert.That(state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().PredictedMoveCoord, Is.EqualTo(new HexCoord(2, 0)));

            AdvanceToPlayerActionAfterMonsterMovement(state);
            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        [Test]
        public void IntentPreviewStepRecordsActiveAttackPatternAfterMove()
        {
            var state = CombatStateFixture.Arena(2).WithEnemyEastAt(1).Build();

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            Assert.That(state.Monsters.Single().HasAttackIntent, Is.True);
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Attack));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        [Test]
        public void MonsterIntentPreviewStaysCommittedAfterPlayerMoves()
        {
            var state = CombatStateFixture.Arena(4).WithEnemyEastAt(3).Build();
            var previewBeforeMove = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();
            Assert.That(previewBeforeMove.IntentType, Is.EqualTo(EnemyIntentType.Chase));
            Assert.That(previewBeforeMove.PredictedMoveCoord, Is.EqualTo(new HexCoord(2, 0)));

            Assert.That(state.TryPlayerMove(new HexCoord(-1, 0)), Is.True);

            var previewAfterMove = state.GetMonsterIntentPreviews(includeUnrevealed: true).Single();
            Assert.That(previewAfterMove.IntentType, Is.EqualTo(previewBeforeMove.IntentType));
            Assert.That(previewAfterMove.PredictedMoveCoord, Is.EqualTo(previewBeforeMove.PredictedMoveCoord));
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Chase));
        }

        [Test]
        public void CommittedAttackIntentCanBeDodgedByMovementWithoutRetargeting()
        {
            var state = CombatStateFixture.Arena(3).WithEnemyEastAt(1).Build();
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Attack));

            // The committed pattern covers ring-1 around the monster plus the facing cell (-1,0),
            // so the dodge must land outside the telegraphed cells: (-1,1) is reachable and safe.
            Assert.That(
                state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().AttackRangeCoords,
                Has.No.Member(new HexCoord(-1, 1)));
            Assert.That(state.TryPlayerMove(new HexCoord(-1, 1)), Is.True);
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Attack));
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }

        [Test]
        public void MonsterAttackFullyAbsorbedByBlockRaisesDamageBlockedEffect()
        {
            var state = CombatStateFixture.Arena(2).WithEnemyEastAt(1).Build();

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Attack));

            state.Player.AddBlock(100);
            var hpBefore = state.Player.Hp;

            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
            Assert.That(effects.Any(effect => effect.Kind == EffectKind.DamageBlocked), Is.True);
            Assert.That(
                effects.Any(effect => effect.Kind == EffectKind.Damage && effect.TargetUnitId == "player"),
                Is.False);
        }

        [Test]
        public void TurnStartFieldDamageResolvesAfterPreviousBlockClears()
        {
            // Chase range 0 keeps the monster parked at (4,0), outside every shipping pattern's reach
            // (line-3 reaches 3 since the WS-H #19 rebalance) — otherwise its blocked attack would
            // pollute the field-damage isolation.
            var state = CombatStateFixture.Arena(4)
                .WithConfig(TestCombatConfigs.Standard())
                .WithEnemyEastAt(4)
                .Build();
            state.FieldObjects.Add(new FieldObject(state.PlayerCoord, radius: 0, remainingTurns: 1, FieldObjectKind.FieldDamage, value: 3, sourceUnitId: "normal-enemy"));
            state.Player.AddBlock(100);
            var hpBefore = state.Player.Hp;

            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore - 3));
            Assert.That(
                effects.Any(effect => effect.Kind == EffectKind.DamageBlocked && effect.TargetUnitId == "player"),
                Is.False);
            Assert.That(
                effects.Any(effect => effect.Kind == EffectKind.Damage && effect.TargetUnitId == "player"),
                Is.True);
        }

        [Test]
        public void NullifiedMonsterAttackRaisesDamageBlockedEffect()
        {
            var state = CombatStateFixture.Arena(2).WithEnemyEastAt(1).Build();

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Attack));
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            // Simulate '보호구역 안에서 도발' (defend.zero_then_double): zero out this monster action's damage.
            PendingEffectsProbe.NullifyIncomingDamageThisMonsterAction(state);

            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;
            state.ResolveMonsterAction();

            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
            Assert.That(effects.Any(effect => effect.Kind == EffectKind.DamageBlocked && effect.TargetUnitId == "player"), Is.True);
            Assert.That(effects.Any(effect => effect.Kind == EffectKind.Damage && effect.TargetUnitId == "player"), Is.False);
        }


        [Test]
        public void FieldObjectsTickAtNextTurnStartAndExpireAfterApplyingDamage()
        {
            var state = CombatStateFixture.Arena(2).WithEnemyEastAt(1).Build();
            state.FieldObjects.Add(new FieldObject(new HexCoord(1, 0), radius: 0, remainingTurns: 1, FieldObjectKind.FieldDamage, value: 99));

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Monsters.Single().Hp, Is.EqualTo(0));
            Assert.That(state.FieldObjects.Objects, Is.Empty);
            Assert.That(state.RuntimeStates.Values.Any(cell => cell.OccupyingUnitId == state.Monsters.Single().Id), Is.False);
        }

        [Test]
        public void MonsterResolutionRaisesOverallTurnEndThenNextTurnStart()
        {
            var state = CombatStateFixture.Arena(4).WithEnemyEastAt(3).Build();
            var events = new List<string>();
            state.OverallTurnEnded += turn => events.Add($"end:{turn}");
            state.OverallTurnStarted += turn => events.Add($"start:{turn}");

            Assert.That(state.OverallTurnNumber, Is.EqualTo(1));
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.OverallTurnNumber, Is.EqualTo(2));
            Assert.That(events, Is.EqualTo(new[] { "end:1", "start:2" }));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }
        [Test]
        public void MonsterResolutionCanDeferPlayerTurnDrawForSeparateHudDiffs()
        {
            var state = CombatState.CreateDefaultDemo();
            var beforeTurnEnd = CombatCardZoneSnapshot.Build(state);

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction(drawPlayerTurnHands: false);

            var afterDiscard = CombatCardZoneSnapshot.Build(state);
            var discardDiff = CardZoneDiff.Compute(beforeTurnEnd, afterDiscard);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
            Assert.That(state.GetHandCards(), Is.Empty, "Turn-end cleanup should discard the old hand before the player-turn draw.");
            Assert.That(discardDiff.Transitions.Any(t => t.IsDiscard), Is.True);
            Assert.That(discardDiff.Transitions.Any(t => t.To == CardZone.Hand), Is.False,
                "The discard HUD refresh must not also see new hand cards entering hand.");

            state.StartPlayerTurn();

            var drawDiff = CardZoneDiff.Compute(afterDiscard, CombatCardZoneSnapshot.Build(state));
            Assert.That(state.GetHandCards(), Is.Not.Empty);
            Assert.That(drawDiff.Transitions.Any(t => t.To == CardZone.Hand), Is.True);
            Assert.That(drawDiff.Transitions.Any(t => t.IsDiscard), Is.False,
                "The draw HUD refresh should be a separate diff from turn-end discard cleanup.");
        }

        [Test]
        public void MassImmobilizeFieldObjectBlocksMonsterMovementForThatTurnAndThenExpires()
        {
            var state = CombatStateFixture.Arena(4).WithEnemyEastAt(3).Build();
            Assert.That(state.GetMonsterIntentPreviews(includeUnrevealed: true).Single().PredictedMoveCoord, Is.EqualTo(new HexCoord(2, 0)));
            state.FieldObjects.Add(new FieldObject(new HexCoord(3, 0), radius: 0, remainingTurns: 1, FieldObjectKind.MassImmobilize, value: 1));

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.Monsters.Single().Coord, Is.EqualTo(new HexCoord(3, 0)));
            Assert.That(state.FieldObjects.Objects, Is.Empty);
        }

        [Test]
        public void MovementRespectsReachabilityBlockedCellsAndEnemyOccupancy()
        {
            var state = CombatStateFixture.Arena(2).WithEnemyEastAt(1).Build();
            Assert.That(state.TryPlayerMove(state.Monsters[0].Coord), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("blocked, occupied, or out of move range"));
            Assert.That(state.TryPlayerMove(new HexCoord(1, -1)), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("blocked"));
            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(0, 0)));
            Assert.That(state.TryPlayerMove(new HexCoord(0, 1)), Is.True);
            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(0, 1)));
        }

        [Test]
        public void CollisionKnockbackUsesNearestAvailableOneHexRing()
        {
            var state = CreateSparseKnockbackState(new HexCoord(1, 0), new HexCoord(2, 0));

            Assert.That(InvokeCollisionKnockback(state), Is.True);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(1, 0)));
        }

        [Test]
        public void CollisionKnockbackFallsBackToTwoHexRingWhenOneHexRingHasNoSpace()
        {
            var state = CreateSparseKnockbackState(new HexCoord(2, 0));
            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;

            Assert.That(InvokeCollisionKnockback(state), Is.True);

            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(effects, Has.Count.EqualTo(1));
            Assert.That(effects[0].Kind, Is.EqualTo(EffectKind.Knockback));
            Assert.That(effects[0].SourceRef, Is.EqualTo("knockback"));
            Assert.That(effects[0].SourceUnitId, Is.EqualTo("normal-enemy"));
            Assert.That(effects[0].TargetUnitId, Is.EqualTo("player"));
            Assert.That(effects[0].Amount, Is.EqualTo(2));
        }

        [Test]
        public void CombatCardsExposeHandDeckDiscardCostAndFailureStates()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.MovementDeck.HandCount, Is.EqualTo(1));
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(5));
            AssertCard(state, CombatCardKind.Move, usable: true, discarded: false, status: CombatCardStatusText.Usable);
            AssertCard(state, CombatCardKind.Attack, usable: false, discarded: false, status: CombatCardStatusText.Waiting);
            // Scout cards are usable in both the movement and action phases, so they read as usable here in movement.
            AssertCard(state, CombatCardKind.Scout, usable: true, discarded: false, status: CombatCardStatusText.Usable);

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            Assert.That(state.LastDiscardedCard, Is.EqualTo(CombatCardKind.Move));
            Assert.That(state.MovementDeck.DiscardCount, Is.EqualTo(1));
            AssertCard(state, CombatCardKind.Move, usable: false, discarded: true, status: "Discard");

            AssertCard(state, CombatCardKind.Defend, usable: false, discarded: false, status: CombatCardStatusText.Waiting);

            AdvanceToPlayerActionAfterMonsterMovement(state);
            AssertCard(state, CombatCardKind.Defend, usable: true, discarded: false, status: CombatCardStatusText.Usable);

            Assert.That(state.TryPlayerDefend(), Is.True);
            Assert.That(state.LastDiscardedCard, Is.EqualTo(CombatCardKind.Defend));
            Assert.That(state.CurrentKi, Is.EqualTo(state.MaxKi - 2));
            AssertCard(state, CombatCardKind.Defend, usable: false, discarded: true, status: "Discard");

            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterAction));
            state.ResolveMonsterAction();
            AssertCard(state, CombatCardKind.Move, usable: true, discarded: false, status: CombatCardStatusText.Usable);
        }

        [Test]
        public void ActionCostAllowsMultipleAffordableCardsAndRejectsUnaffordableMutation()
        {
            var state = CombatStateFixture.Arena(2).WithEnemyEastAt(1).Build();
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            var startingHp = state.Monsters[0].Hp;
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackSweepId), Is.True);
            Assert.That(state.Monsters[0].Hp, Is.LessThan(startingHp));
            Assert.That(state.CurrentKi, Is.EqualTo(state.MaxKi - 2));
            Assert.That(state.TryPlayerDefend(), Is.True);
            Assert.That(state.CurrentKi, Is.EqualTo(state.MaxKi - 3));
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0), ApprovedCardCatalogFactory.AttackSweepId), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("No matching action card").Or.Contain("Not enough Ki"));
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        [Test]
        public void DefeatingNormalEnemyDoesNotTriggerObjectiveVictory()
        {
            var state = CombatStateFixture.Arena(1)
                .WithConfig(new CombatConfig(20, 1, 1, 1, 4, 4, 5, 1, 3))
                .WithEnemyEastAt(1)
                .Build();

            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);
            Assert.That(state.TryPlayerAttack(new HexCoord(1, 0)), Is.True);

            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(state.IsTerminal, Is.False);
            Assert.That(state.ObjectiveCompleted, Is.False);
            Assert.That(state.RuntimeStates.ContainsKey(state.Monsters[0].Coord), Is.False);
        }

        [Test]
        public void RepresentativeSuccessPathDefeatsNormalEnemy()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);
            Assert.That(state.TryPlayerDefend(), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            var safety = 0;
            while (!state.Monsters[0].IsDead && safety++ < 16)
            {
                if (state.Phase == CombatPhase.PlayerMovement)
                {
                    var destination = state.GetReachablePlayerMoves()
                        .Keys
                        .OrderBy(coord => coord.DistanceTo(state.Monsters[0].Coord))
                        .ThenBy(coord => coord.Q)
                        .ThenBy(coord => coord.R)
                        .First();
                    Assert.That(state.TryPlayerMove(destination), Is.True);
                    AdvanceToPlayerActionAfterMonsterMovement(state);
                }

                if (state.Phase == CombatPhase.PlayerAction)
                {
                    if (!state.TryPlayerAttack())
                    {
                        Assert.That(state.LastFailureReason, Does.Contain("range").Or.Contain("No matching action card").Or.Contain("Not enough Ki"));
                    }

                    if (!state.Monsters[0].IsDead)
                    {
                        Assert.That(state.EndAction(), Is.True);
                        state.ResolveMonsterAction();
                    }
                }
            }

            Assert.That(state.Monsters[0].IsDead, Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(state.IsTerminal, Is.False);
            Assert.That(state.RuntimeStates.ContainsKey(state.Monsters[0].Coord), Is.False);
        }

        [Test]
        public void ScoutCardEntersActionPipeline()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.GetCombatCards().Any(card => card.Kind == CombatCardKind.Scout && card.Name == "Minefinder"), Is.True);

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            AssertCard(state, CombatCardKind.Scout, usable: true, discarded: false, status: CombatCardStatusText.Usable);
        }

        [Test]
        public void ScoutValidationReturnsExplicitSuccessAndFailureReasons()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            var valid = state.ValidateScoutTarget(new HexCoord(2, -1));
            Assert.That(valid.IsValid, Is.True);
            Assert.That(valid.FailureReason, Is.Empty);

            var missing = state.ValidateScoutTarget(new HexCoord(9, 9));
            Assert.That(missing.IsValid, Is.False);
            Assert.That(missing.FailureReason, Does.Contain("not on the map"));

            var blocked = state.ValidateScoutTarget(new HexCoord(1, -1));
            Assert.That(blocked.IsValid, Is.False);
            Assert.That(blocked.FailureReason, Does.Contain("walkable"));
        }

        [Test]
        public void ScoutResolveRevealsRadiusForCurrentTurnAndConsumesCard()
        {
            var state = CreateScoutRevealState();
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            var revealedBeforeScout = state.PlayerCoord;
            var target = new HexCoord(3, 0);
            var radiusUnknown = new HexCoord(4, 0);
            Assert.That(state.GetVisibility(target), Is.EqualTo(HexCellVisibility.Unknown));
            Assert.That(state.GetVisibility(radiusUnknown), Is.EqualTo(HexCellVisibility.Unknown));
            Assert.That(state.TryPlayerScout(target), Is.True);

            Assert.That(state.LastDiscardedCard, Is.EqualTo(CombatCardKind.Scout));
            Assert.That(state.GetVisibility(target), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.GetVisibility(radiusUnknown), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.GetVisibility(revealedBeforeScout), Is.EqualTo(HexCellVisibility.Revealed));
            AssertCard(state, CombatCardKind.Scout, usable: false, discarded: true, status: "Discard");
        }

        [Test]
        public void ScoutRevealExpiresToHintedOnNextPlayerTurnOutsidePlayerSight()
        {
            var state = CreateScoutRevealState();
            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            var scoutTarget = new HexCoord(3, 0);
            var scoutEdge = new HexCoord(4, 0);
            Assert.That(state.TryPlayerScout(scoutTarget), Is.True);
            Assert.That(state.GetVisibility(scoutTarget), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(state.GetVisibility(scoutEdge), Is.EqualTo(HexCellVisibility.Revealed));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.GetVisibility(scoutTarget), Is.EqualTo(HexCellVisibility.Hinted));
            Assert.That(state.GetVisibility(scoutEdge), Is.EqualTo(HexCellVisibility.Hinted));
            Assert.That(state.GetVisibility(state.PlayerCoord), Is.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void InvalidScoutTargetKeepsCardInHandWithExplicitFailureReason()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            Assert.That(state.TryPlayerScout(new HexCoord(1, -1)), Is.False);

            Assert.That(state.LastFailureReason, Does.Contain("walkable"));
            Assert.That(state.LastDiscardedCard, Is.EqualTo(CombatCardKind.Move));
            AssertCard(state, CombatCardKind.Scout, usable: true, discarded: false, status: CombatCardStatusText.Usable);
        }

        [Test]
        public void InvestigateValidationReturnsReadyWhenCardIsAvailable()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            var result = state.ValidateInvestigateCard();
            Assert.That(result.IsValid, Is.True);
        }

        [Test]
        public void TryPlayerInvestigateFailsForNonObjectiveTarget()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            AdvanceToPlayerActionAfterMonsterMovement(state);

            Assert.That(state.TryPlayerInvestigate(state.PlayerCoord), Is.False);
            Assert.That(state.LastFailureReason, Does.Contain("No objective target"));
        }

        private static void AdvanceToPlayerActionAfterMonsterMovement(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.MonsterMovement));
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        private static CombatState CreateScoutRevealState()
        {
            // Pin vision to 1 (the legacy "vision == move points" radius) so the scout-reveal
            // expectations stay deterministic now that default vision is decoupled at 7.
            return CombatStateFixture.Arena(4)
                .WithConfig(new CombatConfig(20, 10, 1, 1, 4, 4, 5, 1, 3, playerVisionRange: 1))
                .WithEnemyEastAt(4)
                .Build();
        }

        private static CombatState CreateSparseKnockbackState(params HexCoord[] extraWalkableCells)
        {
            var cells = new List<HexCellData>
            {
                WalkableCell(new HexCoord(0, 0)),
                WalkableCell(new HexCoord(3, 0))
            };
            cells.AddRange(extraWalkableCells.Select(WalkableCell));
            return CombatStateFixture.OnMap(new HexMapData(cells))
                .WithEnemyEastAt(3)
                .Build();
        }

        private static HexCellData WalkableCell(HexCoord coord)
        {
            return new HexCellData(coord, "ground", "demo", 1, true, false);
        }

        private static bool InvokeCollisionKnockback(CombatState state)
        {
            var method = typeof(CombatState).GetMethod(
                "TryKnockbackPlayerFromCollision",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (bool)method.Invoke(state, new object[] { "normal-enemy" });
        }

        private static void AssertCard(CombatState state, CombatCardKind kind, bool usable, bool discarded, string status)
        {
            var card = state.GetCombatCards().First(snapshot => snapshot.Kind == kind && snapshot.IsDiscarded == discarded);
            Assert.That(card.IsUsable, Is.EqualTo(usable), $"{kind} usability");
            Assert.That(card.Status, Is.EqualTo(status), $"{kind} status");
        }
    }
}
