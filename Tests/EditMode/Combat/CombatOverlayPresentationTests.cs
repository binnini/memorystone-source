using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatOverlayPresentationTests
    {
        /// <summary>
        /// 🔴 <b>결계만 장애물 위에 그린다</b>(2026-09-02 사용자 요구).
        ///
        /// <para>아레나 경계 링은 본래 벽을 따라 서므로 그 칸 상당수가 건물·울타리다(출하 Stage_1은
        /// 30칸 중 10칸). 깊이 검사를 그대로 두면 담장의 3분의 1이 건물 뒤로 사라져 「여기는 열려 있나?」로
        /// 읽힌다 — 장애물 칸이야말로 결계가 말해야 하는 칸이다.</para>
        ///
        /// <para>🔑 <b>다른 층까지 함께 보는 이유</b>: 전부에 켜면 이동·공격 범위가 건물을 뚫고 나와
        /// 「저 칸에 갈 수 있다」는 거짓말이 된다. 「켜졌다」만 재면 전부 켜도 통과하므로,
        /// <b>꺼져 있어야 하는 층</b>을 같은 시험에서 함께 못 박는다.</para>
        /// </summary>
        [Test]
        public void OnlyTheBossArenaBarrierDrawsOverObstacles()
        {
            Assert.That(
                CombatOverlayTheme.ResolveDefaultStyle(SeoulPlayup.Map.Unity.HexOverlayLayer.BossArenaBoundary).DrawOverObstacles,
                Is.True,
                "결계가 장애물 뒤로 가려진다 — 벽을 따라 선 경계 칸이 통째로 안 보인다.");

            var mustNotDraw = new[]
            {
                SeoulPlayup.Map.Unity.HexOverlayLayer.Reachable,
                SeoulPlayup.Map.Unity.HexOverlayLayer.AttackRange,
                SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterAttackIntent,
                SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterMoveIntent,
                SeoulPlayup.Map.Unity.HexOverlayLayer.BossFootprint
            };
            foreach (var layer in mustNotDraw)
            {
                Assert.That(
                    CombatOverlayTheme.ResolveDefaultStyle(layer).DrawOverObstacles,
                    Is.False,
                    $"{layer}가 장애물 위에 그려진다 — 갈 수 없는 칸이 갈 수 있어 보인다.");
            }
        }

        /// <summary>
        /// 회귀 고정(§13.5 결함): 같은 레이어가 두 번 들어오면 <b>합쳐져야</b> 한다. 예전에는 마지막
        /// 하나만 남기고 나머지를 조용히 버렸고, 그 바람에 전멸기 예고 51칸이 몬스터 공격 예고 12칸에
        /// 덮여 화면에 아예 도달하지 못했다(규칙은 정확한데 렌더만 비어 있어 EditMode가 못 잡았다).
        /// </summary>
        [Test]
        public void DuplicateLayerEntriesMergeCoordsInsteadOfKeepingOnlyTheLast()
        {
            var style = CombatOverlayTheme.ResolveDefaultStyle(SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterAttackIntent);
            var dangerCells = new[] { new HexCoord(5, 0), new HexCoord(6, 0) };
            var attackCells = new[] { new HexCoord(1, 0), new HexCoord(2, 0) };

            var presentation = new CombatOverlayPresentation(new[]
            {
                new CombatOverlayLayerState(SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterAttackIntent, dangerCells, style),
                new CombatOverlayLayerState(SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterAttackIntent, attackCells, style)
            });

            Assert.That(
                presentation.Layers.Count(layer => layer.Layer == SeoulPlayup.Map.Unity.HexOverlayLayer.MonsterAttackIntent),
                Is.EqualTo(1),
                "레이어당 엔트리는 하나여야 한다 — 렌더러가 레이어당 비주얼 하나를 다시 만들기 때문이다.");
            Assert.That(
                presentation.Layers.Single().Coords,
                Is.EquivalentTo(dangerCells.Concat(attackCells)),
                "두 소스의 칸이 전부 살아남아야 한다.");
        }

        /// <summary>
        /// 전멸기 경고 주석(§13.5): 예고 칸은 평범한 공격 예고와 같은 붉은 해치를 공유하므로, 화면에서
        /// 둘을 가르는 것은 이 경고 아이콘뿐이다. 상태이상 아이콘이 이미 붙은 칸에서도 경고가 함께
        /// 살아남아야 한다(같은 칸에 그룹을 둘 만들면 아이콘이 겹쳐 그려진다).
        /// </summary>
        [Test]
        public void AnnihilationWarningRidesAlongsideStatusIconsOnTheSameTile()
        {
            var warningOnly = new CombatOverlayIconAnnotation(
                new HexCoord(3, 0),
                Array.Empty<ActiveEffect>(),
                knockbackDistance: 0,
                annihilationTurnsRemaining: 2,
                annihilationDamage: 9);
            var withStatus = new CombatOverlayIconAnnotation(
                new HexCoord(4, 0),
                new[] { StatusEffectKind.Poison },
                knockbackDistance: 1,
                annihilationTurnsRemaining: 1,
                annihilationDamage: 9);

            Assert.That(warningOnly.HasAnnihilationWarning, Is.True);
            Assert.That(warningOnly.AnnihilationTurnsRemaining, Is.EqualTo(2));
            Assert.That(warningOnly.AnnihilationDamage, Is.EqualTo(9));
            Assert.That(warningOnly.Effects, Is.Empty);

            Assert.That(withStatus.HasAnnihilationWarning, Is.True);
            Assert.That(withStatus.Knockback, Is.True);
            Assert.That(withStatus.Effects, Is.EquivalentTo(new[] { StatusEffectKind.Poison }));

            var plain = new CombatOverlayIconAnnotation(new HexCoord(5, 0), new[] { StatusEffectKind.Stun });
            Assert.That(plain.HasAnnihilationWarning, Is.False, "예고가 없으면 경고 아이콘도 없어야 한다.");
        }

        [Test]
        public void LayersOnlyConstructorYieldsEmptyAnnotationsChannel()
        {
            var presentation = new CombatOverlayPresentation(Array.Empty<CombatOverlayLayerState>());

            Assert.That(presentation.Annotations, Is.Not.Null);
            Assert.That(presentation.Annotations, Is.Empty);
        }

        [Test]
        public void NullAnnotationsAreNormalizedToEmptyChannel()
        {
            var presentation = new CombatOverlayPresentation(Array.Empty<CombatOverlayLayerState>(), null);

            Assert.That(presentation.Annotations, Is.Not.Null);
            Assert.That(presentation.Annotations, Is.Empty);
        }

        [Test]
        public void AnnotationsChannelCarriesPerTileIconPayloads()
        {
            var annotations = new[]
            {
                new CombatOverlayIconAnnotation(new HexCoord(1, 0), new[] { StatusEffectKind.Poison, StatusEffectKind.Slow }),
                new CombatOverlayIconAnnotation(new HexCoord(2, 0), new[] { StatusEffectKind.Stun })
            };

            var presentation = new CombatOverlayPresentation(Array.Empty<CombatOverlayLayerState>(), annotations);

            Assert.That(presentation.Annotations.Count, Is.EqualTo(2));
            Assert.That(presentation.Annotations.Select(a => a.Coord),
                Is.EquivalentTo(new[] { new HexCoord(1, 0), new HexCoord(2, 0) }));
            Assert.That(presentation.Annotations.First().Effects,
                Is.EquivalentTo(new[] { StatusEffectKind.Poison, StatusEffectKind.Slow }));
        }
    }
}



