using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatOverlayStatusIconQueryTests
    {
        [Test]
        public void BuildStatusIconCellsProjectsEffectsOntoEveryAttackRangeTile()
        {
            var preview = CreatePreview(
                new HexCoord(0, 0),
                new[] { new HexCoord(1, 0), new HexCoord(2, 0) },
                StatusEffectKind.Poison, StatusEffectKind.Stun);

            var annotations = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { preview });

            Assert.That(annotations.Count, Is.EqualTo(2));
            CollectionAssert.AreEquivalent(
                new[] { new HexCoord(1, 0), new HexCoord(2, 0) },
                annotations.Select(annotation => annotation.Coord));
            foreach (var annotation in annotations)
            {
                CollectionAssert.AreEqual(new[] { StatusEffectKind.Poison, StatusEffectKind.Stun }, annotation.Effects);
            }
        }

        [Test]
        public void BuildStatusIconCellsIgnoresPreviewsWithoutStatusEffects()
        {
            var preview = CreatePreview(new HexCoord(0, 0), new[] { new HexCoord(1, 0) });

            var annotations = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { preview });

            Assert.That(annotations, Is.Empty);
        }

        [Test]
        public void BuildStatusIconCellsMergesOverlappingTilesWithoutDuplicateEffects()
        {
            var first = CreatePreview(new HexCoord(0, 0), new[] { new HexCoord(1, 0) }, StatusEffectKind.Poison);
            var second = CreatePreview(new HexCoord(3, 0), new[] { new HexCoord(1, 0) }, StatusEffectKind.Poison, StatusEffectKind.Stun);

            var annotations = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { first, second });

            Assert.That(annotations.Count, Is.EqualTo(1));
            var merged = annotations.Single();
            Assert.That(merged.Coord, Is.EqualTo(new HexCoord(1, 0)));
            CollectionAssert.AreEqual(new[] { StatusEffectKind.Poison, StatusEffectKind.Stun }, merged.Effects);
        }

        [Test]
        public void BuildStatusIconCellsReturnsEmptyForNullOrEmptyPreviews()
        {
            Assert.That(CombatOverlayQuery.BuildMonsterAttackStatusIconCells(null), Is.Empty);
            Assert.That(CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new MonsterIntentPreview[0]), Is.Empty);
        }

        [Test]
        public void BuildStatusIconCellsFlagsKnockbackEvenWithoutStatusEffects()
        {
            var preview = CreatePreview(
                new HexCoord(0, 0),
                new[] { new HexCoord(1, 0) },
                knockbackDistance: 2);

            var annotations = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { preview });

            var annotation = annotations.Single();
            Assert.That(annotation.Coord, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(annotation.Effects, Is.Empty);
            Assert.That(annotation.Knockback, Is.True);
        }

        [Test]
        public void BuildStatusIconCellsMergesKnockbackFlagAcrossOverlappingPreviews()
        {
            var withStatus = CreatePreview(new HexCoord(0, 0), new[] { new HexCoord(1, 0) }, StatusEffectKind.Poison);
            var withKnockback = CreatePreview(new HexCoord(3, 0), new[] { new HexCoord(1, 0) }, knockbackDistance: 1);

            var annotations = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { withStatus, withKnockback });

            var merged = annotations.Single();
            Assert.That(merged.Coord, Is.EqualTo(new HexCoord(1, 0)));
            CollectionAssert.AreEqual(new[] { StatusEffectKind.Poison }, merged.Effects);
            Assert.That(merged.Knockback, Is.True);
        }

        /// <summary>
        /// 끌어당김(음수 변위)은 타일까지 <b>부호가 살아서</b> 도착해야 한다 — 예전에는 예고가
        /// 음수를 0으로 깎아 "넉백 없음"이 되었고, 그 전에는 주석이 bool이라 방향이 아예 없었다.
        /// 둘 중 어느 쪽이 되돌아와도 이 테스트가 먼저 깨진다.
        /// </summary>
        [Test]
        public void BuildStatusIconCellsPreservesPullSignSoTilesCanTellPushFromPull()
        {
            var pull = CreatePreview(
                new HexCoord(0, 0),
                new[] { new HexCoord(1, 0) },
                knockbackDistance: -2);

            var annotation = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { pull }).Single();

            Assert.That(annotation.Knockback, Is.True, "끌어당김도 변위 표식을 띄운다");
            Assert.That(annotation.KnockbackDistance, Is.EqualTo(-2));
            Assert.That(annotation.IsPull, Is.True);
        }

        [Test]
        public void BuildStatusIconCellsKeepsPushSignPositive()
        {
            var push = CreatePreview(
                new HexCoord(0, 0),
                new[] { new HexCoord(1, 0) },
                knockbackDistance: 2);

            var annotation = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(new[] { push }).Single();

            Assert.That(annotation.KnockbackDistance, Is.EqualTo(2));
            Assert.That(annotation.IsPull, Is.False);
        }

        [Test]
        public void GetMonsterAttackStatusIconCellsReturnsEmptyWithoutStateOrMatchingMap()
        {
            var query = new CombatOverlayQuery();
            var map = CombatState.CreateDemoMap(4);
            var state = new CombatState(
                map,
                new HexCoord(0, 0),
                new HexCoord(4, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, actionBudget: 2, movementHandSize: 1, actionHandSize: 5));
            var mismatchedMap = new HexMapData(new[]
            {
                new HexCellData(new HexCoord(0, 0), "other", "street", 1, true, false)
            });

            Assert.That(query.GetMonsterAttackStatusIconCells(null, map), Is.Empty);
            Assert.That(query.GetMonsterAttackStatusIconCells(state, null), Is.Empty);
            Assert.That(query.GetMonsterAttackStatusIconCells(state, mismatchedMap), Is.Empty);
        }

        private static MonsterIntentPreview CreatePreview(HexCoord coord, HexCoord[] attackRange, params StatusEffectKind[] effects)
        {
            return CreatePreview(coord, attackRange, knockbackDistance: 0, effects: effects);
        }

        private static MonsterIntentPreview CreatePreview(HexCoord coord, HexCoord[] attackRange, int knockbackDistance, params StatusEffectKind[] effects)
        {
            return new MonsterIntentPreview(
                monsterId: "m-" + coord.Q + "-" + coord.R,
                definitionId: "def",
                spawnRefId: "spawn",
                currentCoord: coord,
                predictedMoveCoord: coord,
                attackRangeCoords: attackRange,
                intentType: EnemyIntentType.Attack,
                attackPatternStatusEffects: effects != null && effects.Length > 0 ? effects : null,
                attackPatternKnockbackDistance: knockbackDistance);
        }
    }
}



