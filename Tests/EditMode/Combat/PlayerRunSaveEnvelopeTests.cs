using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerRunSaveEnvelopeTests
    {
        [Test]
        public void CreateNormalizesInputsAndProducesValidEnvelope()
        {
            var envelope = PlayerRunSaveEnvelope.Create("  stage-1  ", 0, new PlayerRunSaveData());

            Assert.That(envelope.SchemaVersion, Is.EqualTo(PlayerRunSaveEnvelope.CurrentSchemaVersion));
            Assert.That(envelope.StageId, Is.EqualTo("stage-1"));
            Assert.That(envelope.OverallTurn, Is.EqualTo(1));
            Assert.That(envelope.IsValid(out var reason), Is.True, reason);
        }

        [Test]
        public void CreateRejectsMissingStageIdOrPlayer()
        {
            Assert.That(() => PlayerRunSaveEnvelope.Create(" ", 1, new PlayerRunSaveData()), Throws.ArgumentException);
            Assert.That(() => PlayerRunSaveEnvelope.Create("stage-1", 1, null), Throws.ArgumentNullException);
        }

        [Test]
        public void IsValidRejectsUnsupportedSchemaMissingStageAndMissingPlayer()
        {
            var wrongVersion = new PlayerRunSaveEnvelope { SchemaVersion = 999, StageId = "stage-1" };
            Assert.That(wrongVersion.IsValid(out var versionReason), Is.False);
            Assert.That(versionReason, Does.Contain("schema version"));

            var missingStage = new PlayerRunSaveEnvelope { StageId = " " };
            Assert.That(missingStage.IsValid(out var stageReason), Is.False);
            Assert.That(stageReason, Does.Contain("Stage id"));

            var missingPlayer = new PlayerRunSaveEnvelope { StageId = "stage-1", Player = null };
            Assert.That(missingPlayer.IsValid(out var playerReason), Is.False);
            Assert.That(playerReason, Does.Contain("Player"));
        }

        [Test]
        public void JsonRoundTripPreservesRunPayload()
        {
            var player = new PlayerRunSaveData
            {
                Vitals = new PlayerVitalsSaveData { Hp = 37, MaxHp = 80, Block = 4 },
                Resources = new PlayerResourcesSaveData { CurrentKi = 2, MaxKi = 3 },
                Position = new PlayerPositionSaveData { Q = 5, R = -2, Phase = CombatPhase.PlayerAction },
                Feedback = new PlayerFeedbackSaveData
                {
                    LastFailureReason = "reason",
                    HasLastDiscardedCard = true,
                    LastDiscardedCard = CombatCardKind.Defend,
                    LastInvestigateResult = "result"
                }
            };
            player.Decks.CardCatalogSourceId = "catalog-src";
            player.Decks.MoveCards.Add(new PlayerCardInstanceSaveData
            {
                InstanceId = "move-instance",
                CardId = "move-2-hex",
                UpgradeLevel = 1,
                IsTemporary = false
            });
            player.Decks.MovementZones.DrawPile.Add(new PlayerCardInstanceSaveData
            {
                InstanceId = "move-instance",
                CardId = "move-2-hex"
            });
            player.Inventory.PermanentItems.Add(new PlayerPermanentItemSaveData
            {
                Id = "relic-id",
                Kind = PlayerPermanentItemKind.Curse
            });
            player.Inventory.BagStacks.Add(new PlayerBagItemStackSaveData { ItemId = "bag-item", Count = 2 });

            var json = JsonUtility.ToJson(PlayerRunSaveEnvelope.Create("stage-1", 7, player));
            var restored = JsonUtility.FromJson<PlayerRunSaveEnvelope>(json);

            Assert.That(restored.IsValid(out var reason), Is.True, reason);
            Assert.That(restored.StageId, Is.EqualTo("stage-1"));
            Assert.That(restored.OverallTurn, Is.EqualTo(7));
            Assert.That(restored.Player.Vitals.Hp, Is.EqualTo(37));
            Assert.That(restored.Player.Vitals.MaxHp, Is.EqualTo(80));
            Assert.That(restored.Player.Vitals.Block, Is.EqualTo(4));
            Assert.That(restored.Player.Resources.CurrentKi, Is.EqualTo(2));
            Assert.That(restored.Player.Position.Q, Is.EqualTo(5));
            Assert.That(restored.Player.Position.R, Is.EqualTo(-2));
            Assert.That(restored.Player.Position.Phase, Is.EqualTo(CombatPhase.PlayerAction));
            Assert.That(restored.Player.Decks.CardCatalogSourceId, Is.EqualTo("catalog-src"));
            Assert.That(restored.Player.Decks.MoveCards, Has.Count.EqualTo(1));
            Assert.That(restored.Player.Decks.MoveCards[0].CardId, Is.EqualTo("move-2-hex"));
            Assert.That(restored.Player.Decks.MoveCards[0].UpgradeLevel, Is.EqualTo(1));
            Assert.That(restored.Player.Decks.MovementZones.DrawPile, Has.Count.EqualTo(1));
            Assert.That(restored.Player.Inventory.PermanentItems[0].Id, Is.EqualTo("relic-id"));
            Assert.That(restored.Player.Inventory.PermanentItems[0].Kind, Is.EqualTo(PlayerPermanentItemKind.Curse));
            Assert.That(restored.Player.Inventory.BagStacks[0].ItemId, Is.EqualTo("bag-item"));
            Assert.That(restored.Player.Inventory.BagStacks[0].Count, Is.EqualTo(2));
            Assert.That(restored.Player.Feedback.HasLastDiscardedCard, Is.True);
            Assert.That(restored.Player.Feedback.LastDiscardedCard, Is.EqualTo(CombatCardKind.Defend));
        }

        [Test]
        public void FeedbackWithoutDiscardedCardRoundTripsAsNull()
        {
            var source = new PlayerFeedbackState("fail", null, "investigate");
            var restored = PlayerFeedbackSaveData.FromState(source).ToState();

            Assert.That(restored.LastDiscardedCard, Is.Null);

            var withCard = new PlayerFeedbackState("fail", CombatCardKind.Move, "investigate");
            var restoredWithCard = PlayerFeedbackSaveData.FromState(withCard).ToState();

            Assert.That(restoredWithCard.LastDiscardedCard, Is.EqualTo(CombatCardKind.Move));
        }

        [Test]
        public void RestorePlayerRunHpClampsIntoLivingRange()
        {
            var state = CombatState.CreateDefaultDemo();
            var maxHp = state.Player.MaxHp;

            state.RestorePlayerRunHp(10);
            Assert.That(state.Player.Hp, Is.EqualTo(10));

            state.RestorePlayerRunHp(0);
            Assert.That(state.Player.Hp, Is.EqualTo(1), "corrupt/zero HP must clamp to a living player");

            state.RestorePlayerRunHp(maxHp + 500);
            Assert.That(state.Player.Hp, Is.EqualTo(maxHp));
        }
    }
}
