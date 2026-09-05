using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    // 작업 6: 필드 카드로 생성된 오브젝트가 차지하는 원점 타일을 플레이어·몬스터 모두 진입할 수 없는 장애물로
    // 만들고, 오브젝트가 소멸하면 장애물도 함께 사라지는지 검증한다. 차단은 CombatState.UpdateOccupancy가
    // 활성 필드 오브젝트의 원점을 runtimeStates[..].TemporaryBlocked로 마킹하고, HexPathfinder.CanEnter가
    // 그 단일 소스를 읽어 reachable/경로 양쪽에 적용한다.
    public sealed class FieldObjectObstacleTests
    {
        private const string FieldCardId = "F-WALL";

        [Test]
        public void ActiveFieldObjectBlocksPlayerReachabilityThenReleasesOnExpiry()
        {
            var origin = new HexCoord(1, 0);
            var state = CreateStateWithFieldCard(new HexCoord(8, 0));

            // Turn 1: reach the action phase, then install a 2-turn field object at (1,0).
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.TryPlayerFieldObject(origin, FieldCardId), Is.True);
            Assert.That(state.PendingFieldObjects.Objects, Is.Empty);
            Assert.That(state.FieldObjects.Objects.Single().Position, Is.EqualTo(origin));
            Assert.That(state.GetReachablePlayerMoves().ContainsKey(origin), Is.False,
                "A field object's origin tile is blocked immediately after installation.");

            // Turn 1 -> Turn 2: active object ticks 2 -> 1, still alive.
            Assert.That(state.EndAction(), Is.True); // PlayerAction -> MonsterAction
            state.ResolveMonsterAction();
            Assert.That(state.PendingFieldObjects.Objects, Is.Empty);
            Assert.That(state.FieldObjects.Objects.Single().RemainingTurns, Is.EqualTo(1));

            // While the object lives its origin is an obstacle the move card cannot enter.
            Assert.That(state.PlayerCoord, Is.EqualTo(new HexCoord(0, 0)));
            Assert.That(state.GetReachablePlayerMoves().ContainsKey(origin), Is.False,
                "A field object's origin tile is blocked for the player while the object lives.");

            // Turn 2 -> Turn 3: object ticks 1 -> 0 and expires.
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.EndAction(), Is.True); // PlayerAction -> MonsterAction
            state.ResolveMonsterAction();

            // The obstacle is gone, so the tile is traversable again — no extra cleanup needed.
            Assert.That(state.GetReachablePlayerMoves().ContainsKey(origin), Is.True,
                "When the object expires its tile becomes reachable again.");
        }

        [Test]
        public void FieldObjectCannotBePlacedOnAnOccupiedTile()
        {
            var monsterCoord = new HexCoord(2, 0);
            var state = CreateStateWithFieldCard(monsterCoord);

            // Enter the action phase so field-object placement is allowed.
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction

            // The player's own tile is occupied — placing an obstacle there would trap the player.
            Assert.That(state.TryPlayerFieldObject(new HexCoord(0, 0), FieldCardId), Is.False);
            // A living monster's tile is occupied too.
            Assert.That(state.TryPlayerFieldObject(monsterCoord, FieldCardId), Is.False);

            // A free, in-range, walkable tile still accepts the object.
            Assert.That(state.TryPlayerFieldObject(new HexCoord(1, 0), FieldCardId), Is.True);
        }

        [Test]
        public void BlockedMonsterApproachesAsFarAsPossibleInsteadOfFreezing()
        {
            // 길목이 완전히 막혀 플레이어까지 경로가 끊기면(여기서는 결정적 검증을 위해 정적 벽 타일 사용 —
            // 필드 오브젝트도 동일하게 runtimeStates.TemporaryBlocked로 막아 같은 ChooseGreedyApproachStep
            // 폴백을 탄다) 몬스터는 제자리에 멈추지 않고 도달 가능한 칸 중 플레이어에 가장 가까운 칸으로 다가선다.
            var wall = new HexCoord(2, 0);
            var map = CreateLineMapWithWall(wall);
            var config = new CombatConfig(20, 10, 2, 1, 4, 4, 8, 1, 3, actionBudget: 4, movementHandSize: 1, actionHandSize: 4, playerVisionRange: 7);
            var state = new CombatState(map, new HexCoord(0, 0), new HexCoord(4, 0), config);

            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Chase));

            // Turn 1: the only route is walled, so the monster steps to (3,0) — the reachable tile nearest
            // the player — rather than standing still at (4,0). Monster movement resolves in the
            // MonsterMovement phase.
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.Monsters[0].Coord, Is.EqualTo(new HexCoord(3, 0)),
                "A walled-off monster presses toward the player instead of freezing.");
            Assert.That(state.EndAction(), Is.True); // PlayerAction -> MonsterAction
            state.ResolveMonsterAction(); // -> next turn PlayerMovement

            // Turn 2: now pinned against the wall, every closer tile is blocked, so it holds position
            // (the greedy fallback declines moves that do not reduce distance — no jitter).
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True); // PlayerMovement -> MonsterMovement
            state.ResolveMonsterMovement(); // -> PlayerAction
            Assert.That(state.Monsters[0].Coord, Is.EqualTo(new HexCoord(3, 0)),
                "Pressed against the wall it stays put rather than stepping away from the player.");
        }

        private static HexMapData CreateLineMapWithWall(HexCoord wall)
        {
            var cells = new List<HexCellData>();
            for (var q = -1; q <= 8; q++)
            {
                var coord = new HexCoord(q, 0);
                var walkable = coord != wall;
                cells.Add(new HexCellData(coord, $"cell-{q}", "street", 1, walkable, false));
            }

            return new HexMapData(cells);
        }

        private static CombatState CreateStateWithFieldCard(HexCoord enemyCoord)
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 1, actionHandSize: 15, playerVisionRange: 2);
            var catalog = new CardCatalogDefinition(
                ApprovedCardCatalogFactory.SourceId + ".field-obstacle-test",
                "Field obstacle test catalog",
                new[]
                {
                    new CardCatalogEntry(ApprovedCardCatalogFactory.MoveBasicId, "Move", CardCategory.Movement, CardEffectType.Move, 1, 2, 2, CardEffectRefs.MoveBasic, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(FieldCardId, "Wall Field", CardCategory.Action, CardEffectType.FieldObject, 1, 5, 0, CardEffectRefs.FieldFogReveal, "walkable_map_cell", areaRadius: 0, fieldObjectKind: CardFieldObjectKind.FogReveal, durationTurns: 2, status: CardCatalogStatus.Approved)
                });
            return new CombatState(CreateLineMap(), new HexCoord(0, 0), enemyCoord, config, cardCatalog: catalog);
        }

        private static HexMapData CreateLineMap()
        {
            var cells = new List<HexCellData>();
            for (var q = -1; q <= 8; q++)
            {
                cells.Add(new HexCellData(new HexCoord(q, 0), $"cell-{q}", "street", 1, true, false));
            }

            return new HexMapData(cells);
        }
    }
}
