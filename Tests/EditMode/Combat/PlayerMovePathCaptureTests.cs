using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerMovePathCaptureTests
    {
        [Test]
        public void TryPlayerMove_CapturesOrderedAdjacentRoute()
        {
            var state = CombatState.CreateDefaultDemo();
            var start = state.PlayerCoord;

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);

            var path = state.LastPlayerMovePath;
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(2), "A real move should yield a multi-tile route.");
            Assert.That(path[0], Is.EqualTo(start), "Route starts at the pre-move tile.");
            Assert.That(path[path.Count - 1], Is.EqualTo(state.PlayerCoord), "Route ends at the post-move tile.");
            for (var i = 1; i < path.Count; i++)
            {
                Assert.That(path[i - 1].DistanceTo(path[i]), Is.EqualTo(1), "Consecutive route tiles must be adjacent.");
            }
        }

        [Test]
        public void TryPlayerMove_FailedMove_LeavesEmptyRoute()
        {
            var state = CombatState.CreateDefaultDemo();
            // Far out-of-range destination cannot be reached; route stays empty.
            Assert.That(state.TryPlayerMove(new HexCoord(999, 999)), Is.False);
            Assert.That(state.LastPlayerMovePath.Count, Is.EqualTo(0));
        }
    }
}
