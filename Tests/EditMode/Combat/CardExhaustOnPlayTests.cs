using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// T5-1 「소멸」 통일 컬럼(exhaustOnPlay) 감사. 소멸은 원래 3가지 분산 경로(additionalCost·
    /// 전용 behaviorId·isTemporary)로만 표현됐는데, 신규 저작은 이 컬럼 하나로 수렴한다 —
    /// 사용된 카드가 버림 더미 대신 소멸 더미로 가는 단일 배출 지점(ConsumePlayedCard)이 계약이다.
    /// </summary>
    public sealed class CardExhaustOnPlayTests
    {
        private static readonly HexCoord PlayerCoord = new HexCoord(0, 0);

        [Test]
        public void ExhaustOnPlayDefendCardGoesToRemovedPile()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerDefend("D-EX"), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.RemovedPile.Any(card => card.Id == "D-EX"), Is.True,
                "exhaustOnPlay 카드는 사용 후 소멸 더미로 가야 한다.");
            Assert.That(state.ActionDeck.DiscardPile.Any(card => card.Id == "D-EX"), Is.False,
                "exhaustOnPlay 카드가 버림 더미에 남으면 소멸이 아니다.");
        }

        [Test]
        public void PlainDefendCardStillGoesToDiscardPile()
        {
            var state = CreateState();
            AdvanceToPlayerAction(state);

            Assert.That(state.TryPlayerDefend("D00"), Is.True, state.LastFailureReason);

            Assert.That(state.ActionDeck.DiscardPile.Any(card => card.Id == "D00"), Is.True,
                "일반 카드의 버림 경로는 회귀 없이 유지돼야 한다.");
            Assert.That(state.ActionDeck.RemovedPile.Any(card => card.Id == "D00"), Is.False);
        }

        [Test]
        public void ExhaustOnPlayMoveCardGoesToRemovedPile()
        {
            // 이동 덱도 같은 배출 지점을 지난다 — 봐 둔 길(T5-2)·전력 질주(T5-3)가 이동 덱 저작이라
            // 액션 덱만 게이트하면 절반이 샌다.
            var state = CreateState();

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True, state.LastFailureReason);

            Assert.That(state.MovementDeck.RemovedPile.Any(card => card.Id == "M-EX"), Is.True,
                "exhaustOnPlay 이동 카드는 사용 후 소멸 더미로 가야 한다.");
            Assert.That(state.MovementDeck.DiscardPile.Any(card => card.Id == "M-EX"), Is.False);
        }

        /// <summary>
        /// 키워드 바인딩은 설명 텍스트 substring 매칭이라(CardKeywordTextBindingTests와 같은 이유),
        /// exhaustOnPlay를 저작하고 문안에 「소멸」을 빠뜨리면 툴팁이 조용히 끊긴다 — 데이터 게이트.
        /// </summary>
        [Test]
        [Category("ShippingData")]
        public void ShippingExhaustAuthoredCardsMustSayKeyword()
        {
            var rows = CardCatalogAsset.ParseCsvText(
                File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true)));

            foreach (var row in rows)
            {
                if (!bool.TryParse((row.ExhaustOnPlay ?? string.Empty).Trim(), out var exhaust) || !exhaust)
                {
                    continue;
                }

                Assert.That(row.Description, Does.Contain("소멸"),
                    $"{row.Id}: exhaustOnPlay 저작 카드의 설명에는 「소멸」이 있어야 키워드 툴팁이 걸린다.");
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

        private static CardCatalogDefinition CreateCatalog()
        {
            return new CardCatalogDefinition(
                "exhaust-on-play-test",
                "Exhaust-on-play test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        "M-EX", "소멸 이동", CardCategory.Movement, CardEffectType.Move,
                        1, 1, 1, CardEffectRefs.MoveBasic, "reachable_hex",
                        status: CardCatalogStatus.Approved, exhaustOnPlay: true),
                    new CardCatalogEntry(
                        "D00", "방어의 기초", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, CardEffectRefs.DefendBlock, "self", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "D-EX", "소멸 방어", CardCategory.Action, CardEffectType.Defend,
                        1, 0, 3, CardEffectRefs.DefendBlock, "self",
                        status: CardCatalogStatus.Approved, exhaustOnPlay: true),
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
