using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatSuspendEnvelopeTests
    {
        private static CombatSuspendData CreatePopulatedCombat()
        {
            var data = new CombatSuspendData
            {
                OverallTurn = 5,
                Phase = CombatPhase.PlayerAction,
                ActionCostRemaining = 2,
                RevealedFastTurtleDistance = 3,
                PendingMovementRangeBonus = 1,
                ActiveMovementRangeModifier = 2,
                LastMovedDistance = 4,
                ActionCardsUsedThisTurn = 6,
                ObjectiveCompleted = true,
                MarkedMonsterId = "spawn-primary"
            };
            data.ConsumedTrapIds.Add("trap-a");
            data.ConsumedTrapIds.Add("trap-b");
            data.ClaimedEventObjectIds.Add("evt-1");
            data.Monsters.Add(new MonsterRuntimeSaveData
            {
                Id = "spawn-primary",
                DefinitionId = "M001",
                Q = 3,
                R = 1,
                SpawnQ = 4,
                SpawnR = 0,
                Hp = 7,
                MaxHp = 30,
                Block = 2,
                ActivityState = MonsterActivityState.SimulatedBackground,
                AttackPatternIndex = 2,
                PendingAttackIntent = true,
                FsmState = MonsterFsmState.Chase,
                FsmPreAlertState = MonsterFsmState.Patrol,
                HasLastKnownPlayerCoord = true,
                LastKnownPlayerQ = 1,
                LastKnownPlayerR = 0,
                SearchTurnsRemaining = 2,
                AlertTurnsRemaining = 3,
                AlertRangeBonus = 1,
                PatrolCursor = 4,
                AttackPatternCooldowns = { new MonsterCooldownSaveData { Index = 1, Remaining = 2 } },
                PatrolArea = { new HexCoordSaveData(new HexCoord(4, 0)), new HexCoordSaveData(new HexCoord(5, 0)) }
            });
            data.ActiveEffects.Add(new ActiveEffectSaveData
            {
                Type = EffectType.Duration,
                Kind = StatusEffectKind.Poison,
                TargetUnitId = "player",
                RemainingTurns = 3,
                Amount = 2,
                SourceRef = "poison-card",
                SkipNextTick = true
            });
            data.Visibility.Add(new HexCellVisibilitySaveData(new HexCoord(5, 0), HexCellVisibility.Revealed));
            return data;
        }

        [Test]
        public void CreateNormalizesStageIdAndTurn()
        {
            var envelope = CombatSuspendEnvelope.Create("  stage-1  ", 0, new CombatSuspendData());

            Assert.That(envelope.StageId, Is.EqualTo("stage-1"));
            Assert.That(envelope.OverallTurn, Is.EqualTo(1));
            Assert.That(envelope.SchemaVersion, Is.EqualTo(CombatSuspendEnvelope.CurrentSchemaVersion));
        }

        [Test]
        public void IsValidRejectsUnsupportedSchemaVersion()
        {
            var envelope = CombatSuspendEnvelope.Create("stage-1", 2, new CombatSuspendData());
            envelope.SchemaVersion = CombatSuspendEnvelope.CurrentSchemaVersion + 1;

            Assert.That(envelope.IsValid(out var reason), Is.False);
            Assert.That(reason, Does.Contain("schema version"));
        }

        [Test]
        public void IsValidRejectsMissingStageOrPayload()
        {
            var missingStage = new CombatSuspendEnvelope { StageId = "  ", Combat = new CombatSuspendData() };
            Assert.That(missingStage.IsValid(out _), Is.False);

            var missingPayload = new CombatSuspendEnvelope { StageId = "stage-1", Combat = null };
            Assert.That(missingPayload.IsValid(out _), Is.False);
        }

        [Test]
        public void JsonUtilityRoundTripsAllPersistedFields()
        {
            var envelope = CombatSuspendEnvelope.Create("stage-1", 5, CreatePopulatedCombat());

            var json = JsonUtility.ToJson(envelope);
            var restored = JsonUtility.FromJson<CombatSuspendEnvelope>(json);

            Assert.That(restored.IsValid(out var reason), Is.True, reason);
            Assert.That(restored.StageId, Is.EqualTo("stage-1"));
            Assert.That(restored.OverallTurn, Is.EqualTo(5));

            var combat = restored.Combat;
            Assert.That(combat.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(combat.ActionCostRemaining, Is.EqualTo(2));
            Assert.That(combat.RevealedFastTurtleDistance, Is.EqualTo(3));
            Assert.That(combat.PendingMovementRangeBonus, Is.EqualTo(1));
            Assert.That(combat.ActiveMovementRangeModifier, Is.EqualTo(2));
            Assert.That(combat.LastMovedDistance, Is.EqualTo(4));
            Assert.That(combat.ActionCardsUsedThisTurn, Is.EqualTo(6));
            Assert.That(combat.ObjectiveCompleted, Is.True);
            Assert.That(combat.MarkedMonsterId, Is.EqualTo("spawn-primary"));
            Assert.That(combat.ConsumedTrapIds, Is.EquivalentTo(new[] { "trap-a", "trap-b" }));
            Assert.That(combat.ClaimedEventObjectIds, Is.EquivalentTo(new[] { "evt-1" }));

            Assert.That(combat.Monsters, Has.Count.EqualTo(1));
            var monster = combat.Monsters[0];
            Assert.That(monster.Id, Is.EqualTo("spawn-primary"));
            Assert.That(monster.Q, Is.EqualTo(3));
            Assert.That(monster.R, Is.EqualTo(1));
            Assert.That(monster.SpawnQ, Is.EqualTo(4));
            Assert.That(monster.Hp, Is.EqualTo(7));
            Assert.That(monster.Block, Is.EqualTo(2));
            Assert.That(monster.ActivityState, Is.EqualTo(MonsterActivityState.SimulatedBackground));
            Assert.That(monster.AttackPatternIndex, Is.EqualTo(2));
            Assert.That(monster.PendingAttackIntent, Is.True);
            Assert.That(monster.FsmState, Is.EqualTo(MonsterFsmState.Chase));
            Assert.That(monster.HasLastKnownPlayerCoord, Is.True);
            Assert.That(monster.LastKnownPlayerQ, Is.EqualTo(1));
            Assert.That(monster.AlertTurnsRemaining, Is.EqualTo(3));
            Assert.That(monster.PatrolCursor, Is.EqualTo(4));
            Assert.That(monster.AttackPatternCooldowns, Has.Count.EqualTo(1));
            Assert.That(monster.AttackPatternCooldowns[0].Index, Is.EqualTo(1));
            Assert.That(monster.AttackPatternCooldowns[0].Remaining, Is.EqualTo(2));
            Assert.That(monster.PatrolArea, Has.Count.EqualTo(2));

            Assert.That(combat.ActiveEffects, Has.Count.EqualTo(1));
            var effect = combat.ActiveEffects[0];
            Assert.That(effect.Kind, Is.EqualTo(StatusEffectKind.Poison));
            Assert.That(effect.TargetUnitId, Is.EqualTo("player"));
            Assert.That(effect.RemainingTurns, Is.EqualTo(3));
            Assert.That(effect.Amount, Is.EqualTo(2));
            Assert.That(effect.SourceRef, Is.EqualTo("poison-card"));
            Assert.That(effect.SkipNextTick, Is.True);

            Assert.That(combat.Visibility, Has.Count.EqualTo(1));
            Assert.That(combat.Visibility[0].Visibility, Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(combat.Visibility[0].Q, Is.EqualTo(5));
        }

        [Test]
        public void PlacementSeedRoundTripsThroughJson()
        {
            var envelope = CombatSuspendEnvelope.Create("stage-1", 5, new CombatSuspendData(), hasPlacementSeed: true, placementSeed: -987654321);

            var restored = JsonUtility.FromJson<CombatSuspendEnvelope>(JsonUtility.ToJson(envelope));

            Assert.That(restored.HasPlacementSeed, Is.True);
            Assert.That(restored.PlacementSeed, Is.EqualTo(-987654321));
        }

        [Test]
        public void RngCursorsAndRewardCursorRoundTripThroughJsonAndDefaultToZero()
        {
            // P5: 난수 커서는 additive 필드다 — 왕복하고, 없던 세이브는 0(첫 칸)으로 열린다.
            var combat = new CombatSuspendData
            {
                RngCursors = new RngCursorsSaveData { Judgement = 3, AttackPattern = 4, DamageJitter = 5, BossProps = 6, MovementShuffle = 7, ActionShuffle = 8 }
            };
            var envelope = CombatSuspendEnvelope.Create("stage-1", 5, combat, hasPlacementSeed: true, placementSeed: 1, rewardCursor: 9);

            var restored = JsonUtility.FromJson<CombatSuspendEnvelope>(JsonUtility.ToJson(envelope));

            Assert.That(restored.RewardCursor, Is.EqualTo(9));
            var cursors = restored.Combat.RngCursors;
            Assert.That(
                new[] { cursors.Judgement, cursors.AttackPattern, cursors.DamageJitter, cursors.BossProps, cursors.MovementShuffle, cursors.ActionShuffle },
                Is.EqualTo(new[] { 3, 4, 5, 6, 7, 8 }));

            const string legacyJson = "{\"SchemaVersion\":1,\"StageId\":\"stage-1\",\"OverallTurn\":3,\"HasPlacementSeed\":true,\"PlacementSeed\":1,\"Combat\":{}}";
            var legacy = JsonUtility.FromJson<CombatSuspendEnvelope>(legacyJson);
            Assert.That(legacy.IsValid(out var reason), Is.True, reason);
            Assert.That(legacy.RewardCursor, Is.Zero);
            Assert.That(legacy.Combat.RngCursors, Is.Not.Null);
            Assert.That(legacy.Combat.RngCursors.ActionShuffle, Is.Zero);
        }

        [Test]
        public void LegacyJsonWithoutSeedFieldsReadsAsNoRandomization()
        {
            // 시드 필드가 생기기 전의 세이브(placement-randomization-plan §2-2) — 스키마 버전은 그대로
            // 1이므로 IsValid를 통과하고, HasPlacementSeed 기본값 false = 「랜덤화 미적용(저작 원본)」.
            const string legacyJson = "{\"SchemaVersion\":1,\"StageId\":\"stage-1\",\"OverallTurn\":3,\"Combat\":{}}";

            var restored = JsonUtility.FromJson<CombatSuspendEnvelope>(legacyJson);

            Assert.That(restored.IsValid(out var reason), Is.True, reason);
            Assert.That(restored.HasPlacementSeed, Is.False);
        }

        [Test]
        public void RunSaveEnvelopePlacementSeedRoundTripsAndDefaultsOff()
        {
            var envelope = PlayerRunSaveEnvelope.Create("stage-1", 2, new PlayerRunSaveData(), hasPlacementSeed: true, placementSeed: 42);
            var restored = JsonUtility.FromJson<PlayerRunSaveEnvelope>(JsonUtility.ToJson(envelope));
            Assert.That(restored.HasPlacementSeed, Is.True);
            Assert.That(restored.PlacementSeed, Is.EqualTo(42));

            const string legacyJson = "{\"SchemaVersion\":1,\"StageId\":\"stage-1\",\"OverallTurn\":3,\"Player\":{}}";
            var legacy = JsonUtility.FromJson<PlayerRunSaveEnvelope>(legacyJson);
            Assert.That(legacy.IsValid(out var reason), Is.True, reason);
            Assert.That(legacy.HasPlacementSeed, Is.False);
        }
    }
}
