using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerStateDebugPanelTests
    {
        [Test]
        public void FormatSnapshotIncludesPlayerStateDebugSections()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            var snapshot = state.CreatePlayerStateSnapshot();

            var text = PlayerStateDebugPanel.FormatSnapshot(snapshot);

            Assert.That(text, Does.Contain("Vitals: HP"));
            Assert.That(text, Does.Contain("Ki:"));
            Assert.That(text, Does.Contain("Position:"));
            Assert.That(text, Does.Contain("Move Deck: D/H/X"));
            Assert.That(text, Does.Contain("Action Deck: D/H/X"));
            Assert.That(text, Does.Contain("Visibility: Unknown"));
            Assert.That(text, Does.Contain("Objective:"));
            Assert.That(text, Does.Contain("Relics/Curses: Relics 0, Curses 0"));
            // T4-1(RC-8 개정): 가방이 실효화되며 TBD 플레이스홀더 문구가 실상태 요약으로 바뀌었다.
            Assert.That(text, Does.Contain("Bag: Bag"));
            Assert.That(text, Does.Contain("Last Card: Move"));
        }
    }
}

