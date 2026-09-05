using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class EnemyIntentTests
    {
        [Test]
        public void EnemyIntentUsesMapRuntimeDistanceThresholds()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.Monsters[0].Intent.Type, Is.EqualTo(EnemyIntentType.Chase));

            Assert.That(state.TryPlayerMove(new HexCoord(0, 0)), Is.True);
            Assert.That(state.Monsters[0].Coord.DistanceTo(state.PlayerCoord), Is.LessThanOrEqualTo(2));
            Assert.That(state.Monsters[0].Intent.Type == EnemyIntentType.Chase || state.Monsters[0].Intent.Type == EnemyIntentType.Attack, Is.True);
        }

        // A-5 (test-index): EnemyIntent.EnemyCoord 커밋 좌표 보존을 A-2/A-3 잠금 테스트의
        // 간접 감시 대신 구조체 필드 계약으로 직접 명문화한다.

        [Test]
        public void EnemyIntentPreservesEnemyCommitCoordinateDistinctFromPlayer()
        {
            var enemyCoord = new HexCoord(3, -1);
            var playerCoord = new HexCoord(0, 2);
            var intent = new EnemyIntent(EnemyIntentType.Attack, enemyCoord.DistanceTo(playerCoord), enemyCoord, playerCoord);

            Assert.That(intent.EnemyCoord, Is.EqualTo(enemyCoord),
                "EnemyCoord must round-trip the enemy's committed cell exactly.");
            Assert.That(intent.PlayerCoord, Is.EqualTo(playerCoord));
            Assert.That(intent.EnemyCoord, Is.Not.EqualTo(intent.PlayerCoord),
                "EnemyCoord is the enemy's own cell — never conflated with PlayerCoord.");
        }

        [Test]
        public void MonsterIntentEnemyCoordSnapshotsCommittedMonsterCell()
        {
            var state = new CombatState(
                CombatState.CreateDemoMap(3),
                new HexCoord(0, 0),
                new HexCoord(2, 0),
                CombatConfig.Default);

            var monster = state.Monsters[0];
            Assert.That(monster.Intent.EnemyCoord, Is.EqualTo(monster.Coord),
                "Committed intent snapshots the monster's own coordinate, not the player's.");
            Assert.That(monster.Intent.PlayerCoord, Is.EqualTo(state.PlayerCoord));
            Assert.That(monster.Intent.EnemyCoord, Is.Not.EqualTo(monster.Intent.PlayerCoord));
        }
    }
}

