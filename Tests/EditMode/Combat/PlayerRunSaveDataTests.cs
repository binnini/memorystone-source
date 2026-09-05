using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerRunSaveDataTests
    {
        [Test]
        public void RoundTripRestoresSaveSupportedPlayerStateFields()
        {
            var source = CreatePopulatedPlayerState();

            var saveData = PlayerRunSaveData.FromPlayerState(source);
            var restored = saveData.ToPlayerState();

            Assert.That(restored.Vitals.Hp, Is.EqualTo(source.Vitals.Hp));
            Assert.That(restored.Vitals.MaxHp, Is.EqualTo(source.Vitals.MaxHp));
            Assert.That(restored.Vitals.Block, Is.EqualTo(source.Vitals.Block));
            Assert.That(restored.Resources.CurrentKi, Is.EqualTo(source.Resources.CurrentKi));
            Assert.That(restored.Resources.MaxKi, Is.EqualTo(source.Resources.MaxKi));
            Assert.That(restored.Position.Coord, Is.EqualTo(source.Position.Coord));
            Assert.That(restored.Position.Phase, Is.EqualTo(source.Position.Phase));
            AssertDeck(restored.Decks.MoveDeck, source.Decks.MoveDeck);
            AssertDeck(restored.Decks.ActionDeck, source.Decks.ActionDeck);
            AssertVisibility(restored.Knowledge.Visibility, source.Knowledge.Visibility);
            Assert.That(restored.Objective.Completed, Is.EqualTo(source.Objective.Completed));
            Assert.That(restored.Objective.StatusText, Is.EqualTo(source.Objective.StatusText));
            Assert.That(restored.Inventory.RelicsAndCurses.RelicCount, Is.EqualTo(1));
            Assert.That(restored.Inventory.RelicsAndCurses.CurseCount, Is.EqualTo(1));
            // DEC-2026-08-31-02 Q4·Q5: 소모품은 스택되지 않는다(획득 관문이 언제나 새 칸에 1개를
            // 넣는다). 그래서 옛 세이브의 Count>1은 복원에서 한 칸짜리 여러 개로 펴지고, 슬롯 상한
            // (기본 3)이 복원에도 걸린다 — 토큰 3개 중 2개만 들어와 총 3칸이 된다.
            Assert.That(
                restored.Inventory.Bag.Stacks.Select(stack => stack.ItemId),
                Is.EquivalentTo(new[] { "placeholder-map-fragment", "placeholder-token", "placeholder-token" }));
            Assert.That(restored.Inventory.Bag.UsedSlotCount, Is.EqualTo(PlayerBagState.BaseSlotCount),
                "복원도 슬롯 상한을 넘지 않는다.");
            Assert.That(restored.Feedback.LastFailureReason, Is.EqualTo(source.Feedback.LastFailureReason));
            Assert.That(restored.Feedback.LastDiscardedCard, Is.EqualTo(source.Feedback.LastDiscardedCard));
            Assert.That(restored.Feedback.LastInvestigateResult, Is.EqualTo(source.Feedback.LastInvestigateResult));
        }

        [Test]
        public void SaveDataStoresAuthoredIdsWithoutDisplayText()
        {
            var inventory = new PlayerInventoryState();
            Assert.That(inventory.RelicsAndCurses.TryAdd(new PlayerPermanentItemState("relic-id", PlayerPermanentItemKind.Relic, "Human Readable Relic"), out _), Is.True);
            inventory.Bag.SetPlaceholderStack("bag-item-id", 2);
            var state = new PlayerState(inventory: inventory);

            var saveData = PlayerRunSaveData.FromPlayerState(state);
            var savedItem = saveData.Inventory.PermanentItems.Single();

            Assert.That(savedItem.Id, Is.EqualTo("relic-id"));
            Assert.That(savedItem.Kind, Is.EqualTo(PlayerPermanentItemKind.Relic));
            Assert.That(savedItem.GetType().GetProperty("DisplayName"), Is.Null);
            Assert.That(saveData.Inventory.BagStacks.Single().ItemId, Is.EqualTo("bag-item-id"));
        }

        [Test]
        public void DeckSaveDataRoundTripsCardInstances()
        {
            var deck = new PlayerDeckData(
                new[] { new PlayerCardInstanceData("move-instance", "move-2-hex", upgradeLevel: 1) },
                new[] { new PlayerCardInstanceData("action-instance", "attack-sweep", isTemporary: true) });

            var saveData = PlayerDeckSaveData.FromPlayerDeckData(deck);
            var restored = saveData.ToPlayerDeckData();

            Assert.That(saveData.MoveCards.Single().InstanceId, Is.EqualTo("move-instance"));
            Assert.That(restored.MovementCards.Single().CardId, Is.EqualTo("move-2-hex"));
            Assert.That(restored.MovementCards.Single().UpgradeLevel, Is.EqualTo(1));
            Assert.That(restored.ActionCards.Single().InstanceId, Is.EqualTo("action-instance"));
            Assert.That(restored.ActionCards.Single().IsTemporary, Is.True);
        }

        [Test]
        public void SnapshotProjectionCanRoundTripThroughPlayerStateSaveData()
        {
            var combat = CombatState.CreateDefaultDemo();
            Assert.That(combat.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            var snapshot = combat.CreatePlayerStateSnapshot();

            var playerState = PlayerState.FromSnapshot(snapshot);
            var restored = PlayerRunSaveData.FromPlayerState(playerState).ToPlayerState();

            Assert.That(restored.Vitals.Hp, Is.EqualTo(snapshot.Hp));
            Assert.That(restored.Vitals.MaxHp, Is.EqualTo(snapshot.MaxHp));
            Assert.That(restored.Resources.CurrentKi, Is.EqualTo(snapshot.CurrentKi));
            Assert.That(restored.Resources.MaxKi, Is.EqualTo(snapshot.MaxKi));
            Assert.That(restored.Position.Coord, Is.EqualTo(snapshot.Position));
            Assert.That(restored.Position.Phase, Is.EqualTo(snapshot.Phase));
            AssertDeck(restored.Decks.MoveDeck, snapshot.MoveDeck);
            AssertDeck(restored.Decks.ActionDeck, snapshot.ActionDeck);
            AssertVisibility(restored.Knowledge.Visibility, snapshot.Visibility);
            Assert.That(restored.Objective.Completed, Is.EqualTo(snapshot.ObjectiveCompleted));
            Assert.That(restored.Objective.StatusText, Is.EqualTo(snapshot.ObjectiveStatusText));
            Assert.That(restored.Feedback.LastFailureReason, Is.EqualTo(snapshot.LastFailureReason));
            Assert.That(restored.Feedback.LastDiscardedCard, Is.EqualTo(snapshot.LastDiscardedCard));
            Assert.That(restored.Feedback.LastInvestigateResult, Is.EqualTo(snapshot.LastInvestigateResult));
        }

        [Test]
        public void CombatStateProjectionCanRoundTripThroughPlayerStateSaveData()
        {
            var combat = CombatState.CreateDefaultDemo();
            Assert.That(combat.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            // DEC-2026-07-03-02: 액션 페이즈 진입은 EndAction → 몬스터 이동 해석을 거친다.
            Assert.That(combat.EndAction(), Is.True);
            combat.ResolveMonsterMovement();
            Assert.That(combat.TryPlayerDefend(), Is.True);

            var projection = combat.CreatePlayerStateProjection();
            var restored = PlayerRunSaveData.FromPlayerState(projection).ToPlayerState();

            Assert.That(restored.Resources.CurrentKi, Is.EqualTo(combat.CurrentKi));
            Assert.That(restored.Resources.MaxKi, Is.EqualTo(combat.MaxKi));
            Assert.That(restored.Position.Coord, Is.EqualTo(combat.PlayerCoord));
            Assert.That(restored.Position.Phase, Is.EqualTo(combat.Phase));
            Assert.That(restored.Feedback.LastDiscardedCard, Is.EqualTo(CombatCardKind.Defend));
            Assert.That(restored.Feedback.LastFailureReason, Is.Empty);
        }

        private static PlayerState CreatePopulatedPlayerState()
        {
            var relicsAndCurses = new PlayerRelicCurseInventory();
            Assert.That(relicsAndCurses.TryAdd(new PlayerPermanentItemState("relic-placeholder", PlayerPermanentItemKind.Relic, "Relic display text"), out _), Is.True);
            Assert.That(relicsAndCurses.TryAdd(new PlayerPermanentItemState("curse-placeholder", PlayerPermanentItemKind.Curse, "Curse display text"), out _), Is.True);

            var bag = new PlayerBagState();
            bag.AddPlaceholderStack("placeholder-map-fragment", 1);
            bag.AddPlaceholderStack("placeholder-token", 3);

            return new PlayerState(
                new PlayerVitals(7, 10, 2),
                new PlayerResources(1, 3),
                new PlayerPositionState(new HexCoord(2, -1), CombatPhase.PlayerAction),
                new PlayerDecksRuntimeState(new PlayerDeckRuntimeSummary(1, 0, 1), new PlayerDeckRuntimeSummary(2, 3, 1)),
                new PlayerKnowledgeState(new PlayerVisibilitySummary(4, 2, 3)),
                new PlayerObjectiveProgressState(true, "Objective complete: authored display text not saved"),
                new PlayerInventoryState(relicsAndCurses, bag),
                new PlayerFeedbackState("Not enough Ki remains.", CombatCardKind.Investigate, "Objective complete."));
        }

        private static void AssertDeck(PlayerDeckRuntimeSummary actual, PlayerDeckRuntimeSummary expected)
        {
            Assert.That(actual.DrawCount, Is.EqualTo(expected.DrawCount));
            Assert.That(actual.HandCount, Is.EqualTo(expected.HandCount));
            Assert.That(actual.DiscardCount, Is.EqualTo(expected.DiscardCount));
        }

        private static void AssertVisibility(PlayerVisibilitySummary actual, PlayerVisibilitySummary expected)
        {
            Assert.That(actual.UnknownCount, Is.EqualTo(expected.UnknownCount));
            Assert.That(actual.HintedCount, Is.EqualTo(expected.HintedCount));
            Assert.That(actual.RevealedCount, Is.EqualTo(expected.RevealedCount));
        }
    }
}

