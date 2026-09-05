using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerStateTests
    {
        [Test]
        public void FromSnapshotCopiesProjectionWithoutOwningCombatStateAuthority()
        {
            var combat = CombatState.CreateDefaultDemo();
            Assert.That(combat.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            // DEC-2026-07-03-02: 액션 페이즈 진입은 EndAction → 몬스터 이동 해석을 거친다.
            Assert.That(combat.EndAction(), Is.True);
            combat.ResolveMonsterMovement();
            Assert.That(combat.TryPlayerDefend(), Is.True);
            var snapshot = combat.CreatePlayerStateSnapshot();

            var player = PlayerState.FromSnapshot(snapshot);

            Assert.That(player.Vitals.Hp, Is.EqualTo(snapshot.Hp));
            Assert.That(player.Vitals.MaxHp, Is.EqualTo(snapshot.MaxHp));
            Assert.That(player.Vitals.Block, Is.EqualTo(snapshot.Block));
            Assert.That(player.Resources.CurrentKi, Is.EqualTo(snapshot.CurrentKi));
            Assert.That(player.Resources.MaxKi, Is.EqualTo(snapshot.MaxKi));
            Assert.That(player.Position.Coord, Is.EqualTo(snapshot.Position));
            Assert.That(player.Position.Phase, Is.EqualTo(snapshot.Phase));
            Assert.That(player.Decks.MoveDeck.DiscardCount, Is.EqualTo(snapshot.MoveDeck.DiscardCount));
            Assert.That(player.Knowledge.Visibility.TotalCount, Is.EqualTo(snapshot.Visibility.TotalCount));
            Assert.That(player.Objective.StatusText, Is.EqualTo(snapshot.ObjectiveStatusText));
            Assert.That(player.Inventory.RelicCurseStatusText, Does.Contain("Relics 0, Curses 0"));
            Assert.That(player.Inventory.BagStatusText, Does.Contain("Bag"));
            Assert.That(player.Feedback.LastDiscardedCard, Is.EqualTo(snapshot.LastDiscardedCard));
        }

        [Test]
        public void MutableSkeletonClampsVitalsAndKiWithoutGameplayEffects()
        {
            var player = new PlayerState(
                vitals: new PlayerVitals(hp: 5, maxHp: 10, block: -2),
                resources: new PlayerResources(currentKi: 7, maxKi: 3));

            Assert.That(player.Vitals.Block, Is.EqualTo(0));
            Assert.That(player.Resources.CurrentKi, Is.EqualTo(3));
            Assert.That(player.Resources.TrySpendKi(2), Is.True);
            Assert.That(player.Resources.CurrentKi, Is.EqualTo(1));
            Assert.That(player.Resources.TrySpendKi(2), Is.False);
            Assert.That(player.Resources.CurrentKi, Is.EqualTo(1));
            player.Resources.RefillKi();
            Assert.That(player.Resources.CurrentKi, Is.EqualTo(3));

            player.Vitals.SetHp(-10);
            Assert.That(player.Vitals.IsDead, Is.True);
            player.Vitals.SetMaxHp(4, preserveHp: false);
            Assert.That(player.Vitals.Hp, Is.EqualTo(4));
        }
    }
}

