using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    // A monster on a tile the player cannot see (Hinted/Unknown) must not give itself away by blocking
    // movement. The player advances onto the unseen monster's tile, contact reveals it, then the player is
    // knocked back one tile to the cell travelled immediately before the collision.
    public sealed class HiddenMonsterMoveKnockbackTests
    {
        private const int VisionBlind = 0;
        private const int VisionWide = 7;

        [Test]
        public void HiddenMonsterTileIsSelectableAsMoveDestination()
        {
            var state = CreateState(VisionBlind);

            var reachable = state.GetReachablePlayerMoves();

            Assert.That(reachable.ContainsKey(MonsterCoord), Is.True,
                "An out-of-vision monster tile should remain a selectable move destination.");
        }

        [Test]
        public void MovingOntoHiddenMonsterKnocksPlayerBackOneTile()
        {
            var state = CreateState(VisionBlind);
            var start = state.PlayerCoord;
            var effects = new List<EffectResultEvent>();
            state.EffectResolved += effects.Add;

            var moved = state.TryPlayerMove(MonsterCoord);

            Assert.That(moved, Is.True, "Movement onto an unseen monster tile should be allowed.");
            Assert.That(state.PlayerCoord, Is.EqualTo(start),
                "After colliding with the hidden monster the player retreats to the previous tile.");
            Assert.That(state.GetMonsterAtCoord(MonsterCoord), Is.Not.Null,
                "The monster stays on its tile; only the player is knocked back.");
            Assert.That(effects.Any(effect => effect.Kind == EffectKind.Knockback && effect.SourceRef == "knockback"), Is.True,
                "Hidden monster collision should emit the same knockback cue used by monster knockback attacks so 밀려남 floating text can appear.");
        }

        [Test]
        public void VisibleMonsterTileStaysBlocked()
        {
            var state = CreateState(VisionWide);
            var start = state.PlayerCoord;

            Assert.That(state.GetReachablePlayerMoves().ContainsKey(MonsterCoord), Is.False,
                "A monster the player can see must not be offered as a move destination.");

            var moved = state.TryPlayerMove(MonsterCoord);

            Assert.That(moved, Is.False, "Moving onto a visible monster stays blocked.");
            Assert.That(state.LastFailureReason, Does.Contain("blocked, occupied, or out of move range"));
            Assert.That(state.PlayerCoord, Is.EqualTo(start));
        }

        private static readonly HexCoord PlayerCoord = new HexCoord(0, 0);
        private static readonly HexCoord MonsterCoord = new HexCoord(1, 0);

        private static CombatState CreateState(int visionRange)
        {
            var map = new HexMapData(new[]
            {
                new HexCellData(PlayerCoord, "player", "street", 1, true, false),
                new HexCellData(MonsterCoord, "monster", "street", 1, true, false),
                new HexCellData(new HexCoord(0, 1), "free-a", "street", 1, true, false),
                new HexCellData(new HexCoord(-1, 0), "free-b", "street", 1, true, false)
            });

            // Default tuning except for the configurable vision range that decides whether the monster is hidden.
            var config = new CombatConfig(80, 30, 2, 1, 4, 4, 5, 1, 5, 4, 1, 5, visionRange);
            return new CombatState(map, PlayerCoord, MonsterCoord, config);
        }
    }
}
