using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 🔴 <b><see cref="CombatStateFixture"/>가 손으로 쓴 생성과 <u>같은 상태</u>를 만든다</b>는 계약.
    ///
    /// 왜 필요한가(2026-08-31 T5): 픽스처의 값어치는 전부 「기존 테스트를 옮겨도 결과가 안 바뀐다」에
    /// 걸려 있다. 그게 참이 아니면 옮기는 순간 스위트가 조용히 다른 것을 재기 시작한다 — 셋업만
    /// 바꿨다고 믿는 채로. 그래서 <b>옮기기 전에</b> 이 동치를 먼저 잠근다.
    ///
    /// 🔑 비교 대상은 몇 개 골라 본 속성이 아니라 <b>세이브 스냅샷 전체</b>
    /// (<see cref="CombatState.CreateSuspendSnapshot"/>)다. 턴·페이즈·기·예약 효과 8필드·몬스터
    /// 목록·상태이상·시야·필드 오브젝트·플레이어 적재까지 한 번에 들어 있어서, 픽스처가 인자
    /// 하나를 흘리면 문자열이 달라진다. 속성 몇 개만 봤다면 놓쳤을 것이다.
    ///
    /// 여기서 잠그는 형태는 <b>실제로 마이그레이션에 쓰는 것들</b>이다. 새 편의 메서드를 추가하면
    /// 여기에 짝을 하나 더 만드는 것이 규칙이다.
    /// </summary>
    public sealed class CombatStateFixtureEquivalenceTests
    {
        [Test]
        public void ArenaWithEastEnemyMatchesTheHandWrittenConstructor()
        {
            AssertSameState(
                () => new CombatState(CombatState.CreateDemoMap(4), new HexCoord(0, 0), new HexCoord(3, 0), CombatConfig.Default),
                () => CombatStateFixture.Arena(4).WithEnemyEastAt(3).Build());
        }

        [Test]
        public void ExplicitPlayerAndEnemyCoordsMatchTheHandWrittenConstructor()
        {
            AssertSameState(
                () => new CombatState(CombatState.CreateDemoMap(3), new HexCoord(-1, 0), new HexCoord(2, 0), CombatConfig.Default),
                () => CombatStateFixture.Arena(3)
                    .WithPlayerAt(new HexCoord(-1, 0))
                    .WithEnemyAt(new HexCoord(2, 0))
                    .Build());
        }

        [Test]
        public void CustomConfigMatchesTheHandWrittenConstructor()
        {
            var config = new CombatConfig(20, 1, 1, 1, 4, 4, 5, 1, 3);
            AssertSameState(
                () => new CombatState(CombatState.CreateDemoMap(1), new HexCoord(0, 0), new HexCoord(1, 0), config),
                () => CombatStateFixture.Arena(1).WithConfig(config).WithEnemyEastAt(1).Build());
        }

        [Test]
        public void MonsterCatalogArgumentMatchesTheHandWrittenConstructor()
        {
            var catalog = CombatState.CreateMonsterCatalog(CombatConfig.Default);
            AssertSameState(
                () => new CombatState(
                    CombatState.CreateDemoMap(3), new HexCoord(0, 0), new HexCoord(1, 0), CombatConfig.Default,
                    monsterCatalog: catalog),
                () => CombatStateFixture.Arena(3).WithEnemyEastAt(1).WithMonsterCatalog(catalog).Build());
        }

        [Test]
        public void MonsterConfigListMatchesTheHandWrittenConstructor()
        {
            var monsters = new[]
            {
                new MonsterConfig("fixture-target", new HexCoord(1, 0), 20),
                new MonsterConfig("fixture-neighbour", new HexCoord(2, 0), 20)
            };

            AssertSameState(
                () => new CombatState(CombatState.CreateDemoMap(4), new HexCoord(0, 0), monsters, CombatConfig.Default),
                () => CombatStateFixture.Arena(4).WithMonsters(monsters).Build());
        }

        [Test]
        public void NoMonstersMatchesTheHandWrittenConstructor()
        {
            AssertSameState(
                () => new CombatState(
                    CombatState.CreateDemoMap(3), new HexCoord(0, 0), System.Array.Empty<MonsterConfig>(), CombatConfig.Default),
                () => CombatStateFixture.Arena(3).WithMonsters().Build());
        }

        [Test]
        public void SuppressedOpeningHandsMatchesTheHandWrittenConstructor()
        {
            AssertSameState(
                () => new CombatState(
                    CombatState.CreateDemoMap(3), new HexCoord(0, 0), new HexCoord(2, 0), CombatConfig.Default,
                    drawOpeningHands: false),
                () => CombatStateFixture.Arena(3).WithEnemyEastAt(2).WithoutOpeningHands().Build());
        }

        [Test]
        public void MovementHandMatchesTheHandWrittenDeckState()
        {
            AssertSameState(
                () => new CombatState(
                    CombatState.CreateDemoMap(3), new HexCoord(0, 0), new HexCoord(2, 0), CombatConfig.Default,
                    movementDeck: new CardDeckState(
                        drawPile: null,
                        hand: new[] { MoveCard() },
                        discardPile: null,
                        removedPile: null)),
                () => CombatStateFixture.Arena(3).WithEnemyEastAt(2).WithMovementHand(MoveCard()).Build());
        }

        [Test]
        public void CustomMapMatchesTheHandWrittenConstructor()
        {
            AssertSameState(
                () => new CombatState(TestMaps.Line(7), new HexCoord(0, 0), new HexCoord(3, 0), CombatConfig.Default),
                () => CombatStateFixture.OnMap(TestMaps.Line(7)).WithEnemyEastAt(3).Build());
            AssertSameState(
                () => new CombatState(TestMaps.Line(7), new HexCoord(0, 0), new HexCoord(3, 0), CombatConfig.Default),
                () => CombatStateFixture.Corridor(7).WithEnemyEastAt(3).Build());
        }

        /// <summary>
        /// 출하 카탈로그 입구도 「손으로 읽어 넘긴 것」과 같아야 한다 — 픽스처가 다른 파싱 경로를
        /// 쓰기 시작하면 저작을 보는 테스트가 조용히 다른 저작을 보게 된다.
        /// </summary>
        [Test]
        [Category("ShippingData")]
        public void ShippingCardCatalogEntryMatchesPassingItByHand()
        {
            AssertSameState(
                () => new CombatState(
                    CombatState.CreateDemoMap(3), new HexCoord(0, 0), new HexCoord(2, 0), CombatConfig.Default,
                    cardCatalog: ShippingCardCatalogSource.Load()),
                () => CombatStateFixture.Arena(3).WithEnemyEastAt(2).WithShippingCardCatalog().Build());
        }

        /// <summary>
        /// 🔴 <b>이 스위트가 실제로 무는지</b>를 스스로 증명한다. 서로 달라야 하는 두 상태를 넣었을 때
        /// 통과해 버리면 위의 동치 단언들은 전부 무의미하다(스냅샷이 차이를 못 보는 것이므로).
        /// </summary>
        [Test]
        public void TheComparisonItselfDetectsADifferentState()
        {
            var left = CombatStateFixture.Arena(4).WithEnemyEastAt(3).Build();
            var right = CombatStateFixture.Arena(4).WithEnemyEastAt(2).Build();

            Assert.That(
                Snapshot(right), Is.Not.EqualTo(Snapshot(left)),
                "적 위치가 다른데 스냅샷이 같다면 이 파일의 동치 단언은 아무것도 증명하지 못한다.");
        }

        // ------------------------------------------------------------------ 비교

        private static void AssertSameState(System.Func<CombatState> handWritten, System.Func<CombatState> viaFixture)
        {
            Assert.That(Snapshot(viaFixture()), Is.EqualTo(Snapshot(handWritten())));
        }

        /// <summary>세이브 스냅샷 전체를 문자열로 — 필드가 하나라도 다르면 여기서 갈린다.</summary>
        private static string Snapshot(CombatState state)
        {
            return JsonUtility.ToJson(state.CreateSuspendSnapshot(), true);
        }

        private static CardDefinition MoveCard()
        {
            return new CardDefinition(
                "fixture-move",
                "fixture-move",
                CardCategory.Movement,
                CardEffectType.Move,
                cost: 0,
                range: 2,
                amount: 0,
                status: CardCatalogStatus.Approved);
        }
    }
}
