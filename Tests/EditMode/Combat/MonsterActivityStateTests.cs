#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class MonsterActivityStateTests
    {
        [Test]
        public void DormantMonsterDoesNotMoveOrDamagePlayer()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(15),
                new HexCoord(0, 0),
                new HexCoord(13, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 2));
            EnterActionPhase(state);
            var monsterBefore = state.Monsters.Single();
            var hpBefore = state.Player.Hp;

            Assert.That(monsterBefore.ActivityState, Is.EqualTo(MonsterActivityState.Dormant));
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            var monsterAfter = state.Monsters.Single();
            Assert.That(monsterAfter.Coord, Is.EqualTo(monsterBefore.Coord));
            Assert.That(state.Player.Hp, Is.EqualTo(hpBefore));
            Assert.That(state.LastMonsterActionRecords.Single().ActivityBefore, Is.EqualTo(MonsterActivityState.Dormant));
            Assert.That(state.LastMonsterActionRecords.Single().ShouldPresent, Is.False);
        }

        [Test]
        public void SimulatedBackgroundMonsterEnteringVisibilityIsPresentationRelevant()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(5),
                new HexCoord(0, 0),
                new HexCoord(3, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 2));

            // Sample the background state before EnterActionPhase: the chase step now resolves in the
            // MonsterMovement phase, which already carries the monster into vision (ActiveThreat).
            Assert.That(state.Monsters.Single().ActivityState, Is.EqualTo(MonsterActivityState.SimulatedBackground));
            EnterActionPhase(state);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            var record = state.LastMonsterActionRecords.Single();
            // ClassifyMonsterActivity promotes the monster to ActiveThreat at movement-commit time:
            // its planned step (2,0) telegraphs an attack covering the player (PendingAttackIntent),
            // so the record's ActivityBefore is already ActiveThreat even though the monster was
            // SimulatedBackground while idle.
            Assert.That(record.ActivityBefore, Is.EqualTo(MonsterActivityState.ActiveThreat));
            Assert.That(record.BeforeCoord, Is.EqualTo(new HexCoord(3, 0)));
            Assert.That(record.AfterCoord, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(record.WasVisibleBefore, Is.False);
            Assert.That(record.IsVisibleAfter, Is.True);
            Assert.That(record.ShouldPresent, Is.True);
        }

        [Test]
        public void DormantMonsterWakesForSameMonsterPhaseAfterPlayerApproaches()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(15),
                new HexCoord(0, 0),
                new HexCoord(13, 0),
                new CombatConfig(20, 10, 12, 1, 4, 4, 5, 1, 3, playerVisionRange: 2));

            Assert.That(state.Monsters.Single().ActivityState, Is.EqualTo(MonsterActivityState.Dormant));
            Assert.That(state.TryDebugMovePlayer(new HexCoord(10, 0)), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // the woken monster moves in this same monster phase
            Assert.That(state.EndAction(), Is.True); // PlayerAction -> MonsterAction
            state.ResolveMonsterAction();

            var record = state.LastMonsterActionRecords.Single();
            Assert.That(record.ActivityBefore, Is.Not.EqualTo(MonsterActivityState.Dormant));
            Assert.That(record.Moved, Is.True);
        }

        [Test]
        public void MonsterActionRecordsIdentifyActualAttacker()
        {
            // far-monster must sit beyond detection (Default.EnemyChaseRange), otherwise it chases
            // into pattern reach during the MonsterMovement phase and attacks too, breaking isolation.
            var farCoord = new HexCoord(CombatConfig.Default.EnemyChaseRange + 2, 0);
            var state = new CombatState(
                CombatState.CreateDemoMap(CombatConfig.Default.EnemyChaseRange + 2),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("far-monster", farCoord, 10),
                    new MonsterConfig("attacker", new HexCoord(1, 0), 10)
                },
                CombatConfig.Default);
            EnterActionPhase(state);
            var hpBefore = state.Player.Hp;

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            var attackerRecord = state.LastMonsterActionRecords.Single(record => record.MonsterId == "attacker");
            Assert.That(attackerRecord.AttackedPlayer, Is.True);
            Assert.That(attackerRecord.AffectedPlayer, Is.True);
            Assert.That(attackerRecord.DamageToPlayer, Is.EqualTo(hpBefore - state.Player.Hp));
            Assert.That(MapCombatController.ResolvePresentableAttackingMonsterIdForTests(state.LastMonsterActionRecords), Is.EqualTo("attacker"));
        }

        [Test]
        public void MultipleMonsterActionRecordsKeepAttackPresentationOrder()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("east-attacker", new HexCoord(1, 0), 10),
                    new MonsterConfig("north-attacker", new HexCoord(0, 1), 10)
                },
                CombatConfig.Default);
            EnterActionPhase(state);

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            var attacks = MapCombatController.GetPresentableMonsterAttackRecordsForTests(state.LastMonsterActionRecords);
            Assert.That(attacks.Select(record => record.MonsterId), Is.EqualTo(new[] { "north-attacker", "east-attacker" }));
            Assert.That(attacks.Select(record => record.AttackOrder), Is.EqualTo(new[] { 0, 1 }));
            Assert.That(attacks, Has.All.Matches<MonsterActionResolutionRecord>(record => record.AttackedPlayer));
        }

        [Test]
        public void BufferedMonsterAttackEffectsFlushByPresentationGroup()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new[]
                {
                    new MonsterConfig("east-attacker", new HexCoord(1, 0), 10),
                    new MonsterConfig("north-attacker", new HexCoord(0, 1), 10)
                },
                CombatConfig.Default);
            EnterActionPhase(state);

            var resolved = new List<EffectResultEvent>();
            state.EffectResolved += resolved.Add;
            state.BeginEffectBuffering();

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(resolved, Is.Empty);

            var attacks = MapCombatController.GetPresentableMonsterAttackRecordsForTests(state.LastMonsterActionRecords);
            Assert.That(attacks, Has.Count.EqualTo(2));
            Assert.That(attacks.Select(record => record.PresentationGroupId), Has.All.Not.Empty);
            Assert.That(attacks.Select(record => record.PresentationGroupId).Distinct().Count(), Is.EqualTo(2));

            var firstGroup = attacks[0].PresentationGroupId;
            state.FlushBufferedEffects(effect => effect.PresentationGroupId == firstGroup);

            Assert.That(resolved, Has.Count.EqualTo(1));
            Assert.That(resolved[0].SourceUnitId, Is.EqualTo(attacks[0].MonsterId));
            Assert.That(resolved[0].SourceActorKind, Is.EqualTo("monster"));
            Assert.That(resolved[0].TargetActorKind, Is.EqualTo("player"));
            Assert.That(resolved[0].PresentationGroupId, Is.EqualTo(firstGroup));
            Assert.That(state.IsBufferingEffects, Is.True);

            state.FlushBufferedEffects();

            Assert.That(resolved, Has.Count.EqualTo(2));
            Assert.That(resolved[1].SourceUnitId, Is.EqualTo(attacks[1].MonsterId));
            Assert.That(state.IsBufferingEffects, Is.False);
        }

        [Test]
        public void PresentableAttackRecordFilterUsesRecordedAttackOrder()
        {
            var records = new[]
            {
                new MonsterActionResolutionRecord("late", MonsterActivityState.ActiveThreat, new HexCoord(1, 0), new HexCoord(1, 0), true, true, true, true, 2, attackOrder: 2),
                new MonsterActionResolutionRecord("move-only", MonsterActivityState.ActiveThreat, new HexCoord(2, 0), new HexCoord(1, 0), true, true, false, false, 0),
                new MonsterActionResolutionRecord("first", MonsterActivityState.ActiveThreat, new HexCoord(0, 1), new HexCoord(0, 1), true, true, true, true, 3, attackOrder: 0),
                new MonsterActionResolutionRecord("second", MonsterActivityState.ActiveThreat, new HexCoord(1, -1), new HexCoord(1, -1), true, true, true, false, 0, attackOrder: 1)
            };

            var presentable = MapCombatController.GetPresentableMonsterAttackRecordsForTests(records);

            Assert.That(presentable.Select(record => record.MonsterId), Is.EqualTo(new[] { "first", "second", "late" }));
            Assert.That(MapCombatController.ResolvePresentableAttackingMonsterIdForTests(records), Is.EqualTo("first"));
        }

        [Test]
        public void PresentableActionRecordFilterInterleavesMoveAndAttackByActionOrder()
        {
            var records = new[]
            {
                new MonsterActionResolutionRecord("third", MonsterActivityState.ActiveThreat, new HexCoord(3, 0), new HexCoord(2, 0), true, true, false, false, 0, actionOrder: 2),
                new MonsterActionResolutionRecord("first", MonsterActivityState.ActiveThreat, new HexCoord(1, 0), new HexCoord(1, 0), true, true, true, true, 3, actionOrder: 0, attackOrder: 0),
                new MonsterActionResolutionRecord("second", MonsterActivityState.ActiveThreat, new HexCoord(2, 1), new HexCoord(1, 1), true, true, true, false, 0, actionOrder: 1, attackOrder: 1),
                new MonsterActionResolutionRecord("idle", MonsterActivityState.ActiveThreat, new HexCoord(3, 1), new HexCoord(3, 1), true, true, false, false, 0, actionOrder: 3)
            };

            var presentable = MapCombatController.GetPresentableMonsterActionRecordsForTests(records);

            Assert.That(presentable.Select(record => record.MonsterId), Is.EqualTo(new[] { "first", "second", "third" }));
        }

        [Test]
        public void PresentableMoveRecordFilterIsDeterministic()
        {
            var records = new[]
            {
                new MonsterActionResolutionRecord("invisible", MonsterActivityState.SimulatedBackground, new HexCoord(4, 0), new HexCoord(3, 0), false, false, false, false, 0),
                new MonsterActionResolutionRecord("visible-before", MonsterActivityState.SimulatedBackground, new HexCoord(2, 0), new HexCoord(3, 0), true, false, false, false, 0),
                new MonsterActionResolutionRecord("visible-after", MonsterActivityState.SimulatedBackground, new HexCoord(3, 0), new HexCoord(2, 0), false, true, false, false, 0),
                new MonsterActionResolutionRecord("affected", MonsterActivityState.SimulatedBackground, new HexCoord(3, 1), new HexCoord(2, 1), false, false, false, true, 0)
            };

            var presentable = MapCombatController.GetPresentableMonsterMoveRecordsForTests(records);

            Assert.That(presentable.Select(record => record.MonsterId), Is.EqualTo(new[] { "affected", "visible-after", "visible-before" }));
        }

        private static void EnterActionPhase(CombatState state)
        {
            // DEC-2026-07-03-02: 확정된 턴 계약 — 이동만으로는 페이즈가 유지되고,
            // EndAction()이 MonsterMovement로 전환하며 몬스터 이동 해석 후 PlayerAction에 도달한다.
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            if (state.Phase == CombatPhase.PlayerMovement)
            {
                Assert.That(state.EndAction(), Is.True);
            }

            if (state.Phase == CombatPhase.MonsterMovement)
            {
                state.ResolveMonsterMovement();
            }

            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }
    }
}
#endif

