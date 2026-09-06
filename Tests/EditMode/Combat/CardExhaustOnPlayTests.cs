using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 「소멸」 문법의 감사(P3 · 카드 클래스 전환). 사용된 카드 자신이 버림 더미 대신 소멸 더미로 가는지는
    /// 카드 클래스의 <see cref="CardBehavior.Disposal"/> 선언(상태 의존은 <see cref="CardBehavior.DisposeAfterPlay"/>)이
    /// 정하고, <c>CombatState.ConsumePlayedCard</c> 한 곳이 그것을 묻는다. 옛 cards.csv `exhaustOnPlay` 컬럼(저작 0/59)은 은퇴했다.
    /// 소멸 더미로 가는 카드 집합은 P3 전후로 같아야 한다 — 빚 문서(X04)뿐.
    /// </summary>
    public sealed class CardExhaustOnPlayTests
    {
        private static readonly HexCoord PlayerCoord = new HexCoord(0, 0);

        [Test]
        public void DebtNoteExilesItselfWhenPlayed()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);
            var debtNote = state.ActionDeck.Hand.Single(card => card.Id == CardIds.DebtNote);

            Assert.That(state.TryPlayerUtility(debtNote.InstanceId), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.RemovedPile.Any(card => card.Id == CardIds.DebtNote), Is.True,
                "빚 문서는 사용 후 소멸 더미로 가야 한다 — 회수(U02)·소멸 스케일(A13)의 재료다.");
            Assert.That(state.ActionDeck.DiscardPile.Any(card => card.Id == CardIds.DebtNote), Is.False,
                "소멸 카드가 버림 더미에 남으면 재셔플로 돌아온다.");
        }

        [Test]
        public void PlainDefendCardStillGoesToDiscardPile()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerDefend(CardIds.BasicBlock), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.DiscardPile.Any(card => card.Id == CardIds.BasicBlock), Is.True,
                "일반 카드의 버림 경로는 회귀 없이 유지돼야 한다.");
            Assert.That(state.ActionDeck.RemovedPile.Any(card => card.Id == CardIds.BasicBlock), Is.False);
        }

        [Test]
        public void PlainMoveCardStillGoesToDiscardPile()
        {
            // 이동 덱도 같은 배출 지점을 지난다 — 액션 덱만 게이트하면 절반이 샌다.
            var state = CreateState();

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True, state.LastFailureReason);

            Assert.That(state.MovementDeck.DiscardPile.Any(card => card.Id == CardIds.Move1Hex), Is.True);
            Assert.That(state.MovementDeck.RemovedPile.Any(card => card.Id == CardIds.Move1Hex), Is.False);
        }

        /// <summary>
        /// 소멸 더미로 가는 카드 집합은 클래스 선언이 정본이다 — 옛 컬럼(저작 0/59)과 X04 전용 경로를 합친 집합 그대로.
        /// </summary>
        [Test]
        public void OnlyTheDebtNoteDeclaresExile()
        {
            var exiling = CardBehaviorRegistry.RegisteredIds
                .Where(id => CardBehaviorRegistry.Get(id).Disposal == CardDisposal.Exile)
                .ToList();

            Assert.That(exiling, Is.EquivalentTo(new[] { CardIds.DebtNote }),
                "소멸 선언 카드 집합이 바뀌면 규칙 변경이다 — 밸런스 결정 없이 늘리지 않는다.");
        }

        /// <summary>
        /// 키워드 바인딩은 설명 텍스트 substring 매칭이라(CardKeywordTextBindingTests와 같은 이유),
        /// 소멸을 선언하고 문안에 「소멸」을 빠뜨리면 툴팁이 조용히 끊긴다 — 데이터 게이트.
        /// </summary>
        [Test]
        [Category("ShippingData")]
        public void ShippingExileDeclaredCardsMustSayKeyword()
        {
            var rows = CardCatalogAsset.ParseCsvText(
                File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true)));

            foreach (var row in rows)
            {
                if (!CardBehaviorRegistry.TryGet(row.Id, out var behavior) || behavior.Disposal != CardDisposal.Exile)
                {
                    continue;
                }

                Assert.That(row.Description, Does.Contain("소멸"),
                    $"{row.Id}: 소멸을 선언한 카드의 설명에는 「소멸」이 있어야 키워드 툴팁이 걸린다.");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static CombatState CreateState()
        {
            var config = TestCombatConfigs.Standard(actionBudget: 4, movementHandSize: 2, actionHandSize: 4);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                PlayerCoord,
                new HexCoord(3, 0),
                config,
                cardCatalog: CreateCatalog());
        }

        /// <summary>출하 id로 만든 픽스처 — 규칙은 레지스트리가 카드 id로 준다(카탈로그 밖 id는 기본 동작만 받는다).</summary>
        private static CardCatalogDefinition CreateCatalog()
        {
            return new CardCatalogDefinition(
                "exhaust-on-play-test",
                "Exhaust-on-play test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        CardIds.Move1Hex, "1칸 이동", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, "reachable_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        CardIds.BasicBlock, "방어의 기초", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, "self", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        CardIds.DebtNote, "빚 문서", CardCategory.Action, CardEffectType.Status,
                        1, 0, 0, "self", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "D-FILL", "채움", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, "self", status: CardCatalogStatus.Approved),
                });
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }
    }
}
