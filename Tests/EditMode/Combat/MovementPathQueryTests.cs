using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class MovementPathQueryTests
    {
        [Test]
        public void GetReachablePlayerMovesDelegatesToCombatStateRules()
        {
            var state = new CombatState(CreateMap(), new HexCoord(0, 0), new HexCoord(2, 0), CombatConfig.Default);
            var query = new MovementPathQuery();

            var expected = state.GetReachablePlayerMoves();
            var actual = query.GetReachablePlayerMoves(state);

            CollectionAssert.AreEquivalent(expected.Keys, actual.Keys);
            Assert.That(actual.ToDictionary(pair => pair.Key, pair => pair.Value), Is.EqualTo(expected));
        }

        [Test]
        public void GetReachablePlayerMovesIsEmptyWithoutState()
        {
            var query = new MovementPathQuery();

            Assert.That(query.GetReachablePlayerMoves(null), Is.Empty);
        }

        [Test]
        public void IsReachableDestinationChecksComputedReachableSet()
        {
            var state = new CombatState(CreateMap(), new HexCoord(0, 0), new HexCoord(2, 0), CombatConfig.Default);
            var query = new MovementPathQuery();
            var reachable = query.GetReachablePlayerMoves(state);

            Assert.That(query.IsReachableDestination(reachable, new HexCoord(1, 0)), Is.True);
            Assert.That(query.IsReachableDestination(reachable, new HexCoord(3, 0)), Is.False);
            Assert.That(query.IsReachableDestination(null, new HexCoord(1, 0)), Is.False);
        }

        [Test]
        public void ControllerStartResolutionSkipsMovementBlockedPlayerSpawnObjects()
        {
            var fallbackStart = new HexCoord(0, 0);
            var playerSpawn = new HexCoord(1, 0);
            var map = new HexMapData(
                new[]
                {
                    new HexCellData(fallbackStart, "fallback-start-cell", "street", 1, true, false),
                    new HexCellData(playerSpawn, "player-spawn-cell", "street", 1, true, false),
                    new HexCellData(new HexCoord(2, 0), "enemy-spawn-cell", "street", 1, true, false, eventId: "enemy-spawn")
                },
                objectRefs: new[]
                {
                    new HexMapObjectData("player-spawn", "PlayerSpawn", string.Empty, playerSpawn),
                    new HexMapObjectData("blocker", "Prop", "building", playerSpawn, blocksMovement: true)
                });
            var host = new GameObject("MapCombatController test host");
            try
            {
                var controller = host.AddComponent<MapCombatController>();
                var method = typeof(MapCombatController).GetMethod("ResolveStartingCoords", BindingFlags.Instance | BindingFlags.NonPublic);

                var result = ((HexCoord player, HexCoord enemy))method.Invoke(controller, new object[] { map });

                Assert.That(result.player, Is.EqualTo(fallbackStart));
                Assert.That(result.enemy, Is.EqualTo(new HexCoord(2, 0)));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static HexMapData CreateMap()
        {
            return new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "start", "street", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "near", "street", 1, true, false),
                new HexCellData(new HexCoord(2, 0), "enemy", "street", 1, true, false),
                new HexCellData(new HexCoord(3, 0), "unknown", "street", 1, true, false)
            });
        }
    }
}

