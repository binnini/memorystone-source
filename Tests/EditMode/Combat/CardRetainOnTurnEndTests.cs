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
    /// T5-2 「유지」 감사 — P3부터 유지는 카드 클래스의 <see cref="CardBehavior.RetainOnTurnEnd"/> 선언(D07·M07)이고
    /// cards.csv `retainOnTurnEnd` 컬럼은 은퇴했다. 픽스처는 출하 id로 만들어 레지스트리가 규칙을 준다. 우리 턴 구조는 턴말 손패 전량 버림 + 매턴 재드로우라
    /// 유지는 곧 "다음 턴 손패 1장 확정 예약"이다.
    ///
    /// <para>🔴🔴 <b>확정 규칙이 바뀌었다</b>(2026-09-02 #6 · 사용자 확정). 예전 규칙(2026-08-07,
    /// 2026-08-19 라운드 #11에서 「버그 아님」으로 재확인)은 <b>유지 카드가 손패 자리를 차지하고
    /// 드로우는 상한까지만 채운다</b>였고, 신규 유입 −1이 유지의 기회비용이었다. 지금은 뒤집혔다 —
    /// <b>유지 카드는 정원 밖이며 다음 턴 드로우를 깎지 않는다</b>: 유지하면 손이 「유지분 + 정원」이 된다.</para>
    ///
    /// <para>아래 두 시험은 <b>지우지 않고 갱신</b>했다 — 같은 자리에서 규칙이 뒤집힌 것이므로
    /// 새 파일을 만들면 옛 규칙을 재는 시험이 어딘가에 남는다.</para>
    /// </summary>
    public sealed class CardRetainOnTurnEndTests
    {
        private static readonly HexCoord PlayerCoord = new HexCoord(0, 0);
        private const int ActionHandSize = 3;
        private const int MovementHandSize = 2;

        [Test]
        public void RetainedActionCardSurvivesTurnEndAndDoesNotEatTheDrawQuota()
        {
            var state = CreateState();
            RunFullTurnWithoutPlaying(state);

            Assert.That(state.ActionDeck.Hand.Any(card => card.Id == CardIds.FullyPrepared), Is.True,
                "유지 카드는 턴이 끝나도 손에 남아야 한다.");
            Assert.That(state.ActionDeck.DiscardPile.Any(card => card.Id == CardIds.FullyPrepared), Is.False);
            Assert.That(state.ActionDeck.HandCount, Is.EqualTo(ActionHandSize + 1),
                "유지 카드는 정원 밖이다(2026-09-02 #6) — 정원만큼 새로 뽑고 그 위에 유지분이 얹힌다.");
        }

        [Test]
        public void RetainedMovementCardSurvivesTurnEndAndDoesNotEatTheDrawQuota()
        {
            var state = CreateState();
            RunFullTurnWithoutPlaying(state);

            Assert.That(state.MovementDeck.Hand.Any(card => card.Id == CardIds.Shortcut), Is.True,
                "이동 덱 유지(봐 둔 길 문법)도 같은 게이트를 지나야 한다.");
            Assert.That(state.MovementDeck.HandCount, Is.EqualTo(MovementHandSize + 1),
                "이동 덱도 같은 규칙이다 — 「지름길을 남겼더니 이동 카드가 한 장 줄었다」가 #6의 증상이었다.");
        }

        [Test]
        public void PlainCardsAreStillDiscardedAtTurnEnd()
        {
            var state = CreateState();
            var plainActionCardsInHand = state.ActionDeck.Hand.Where(card => !CardBehaviorRegistry.Resolve(card).RetainOnTurnEnd).Select(card => card.InstanceId).ToList();
            Assert.That(plainActionCardsInHand, Is.Not.Empty, "픽스처: 일반 카드가 손에 있어야 회귀를 검증한다.");

            RunFullTurnWithoutPlaying(state);

            Assert.That(state.ActionDeck.Hand.Select(card => card.InstanceId).Intersect(plainActionCardsInHand), Is.Empty,
                "유지가 아닌 카드의 턴말 전량 버림은 회귀 없이 유지돼야 한다.");
        }

        [Test]
        public void PlayedRetainCardDoesNotComeBack()
        {
            // 유지는 "안 썼을 때"의 규칙이다 — 쓰면 다른 카드처럼 버림 더미로 간다.
            var state = CreateState();
            AdvanceToPlayerAction(state);
            Assert.That(state.TryPlayerDefend(CardIds.FullyPrepared), Is.True, state.LastFailureReason);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();

            Assert.That(state.ActionDeck.Hand.Any(card => card.Id == CardIds.FullyPrepared), Is.False,
                "사용한 유지 카드가 손에 남으면 무한 방어가 된다.");
        }

        /// <summary>유지 카드 집합은 클래스 선언이 정본이다 — T5-2의 두 장(D07 만반의 준비·M07 지름길) 그대로.</summary>
        [Test]
        public void OnlyTheTwoAuthoredCardsDeclareRetain()
        {
            var retaining = CardBehaviorRegistry.RegisteredIds
                .Where(id => CardBehaviorRegistry.Get(id).RetainOnTurnEnd)
                .ToList();

            Assert.That(retaining, Is.EquivalentTo(new[] { CardIds.FullyPrepared, CardIds.Shortcut }),
                "유지 선언 카드 집합이 바뀌면 규칙 변경이다.");
        }

        /// <summary>유지를 선언한 출하 카드의 설명에는 「유지」 문안이 있어야 키워드 툴팁이 걸린다.</summary>
        [Test]
        [Category("ShippingData")]
        public void ShippingRetainAuthoredCardsMustSayKeyword()
        {
            var rows = CardCatalogAsset.ParseCsvText(
                File.ReadAllText(CombatCsvPaths.CardsCsv, new UTF8Encoding(false, true)));

            var retainRows = rows
                .Where(row => CardBehaviorRegistry.TryGet(row.Id, out var behavior) && behavior.RetainOnTurnEnd)
                .ToList();
            Assert.That(retainRows.Select(row => row.Id), Is.EquivalentTo(new[] { CardIds.FullyPrepared, CardIds.Shortcut }),
                "T5-2 유지 카드 2장(만반의 준비·지름길)이 출하 CSV에 있어야 한다.");

            foreach (var row in retainRows)
            {
                Assert.That(row.Description, Does.Contain("유지"),
                    $"{row.Id}: 유지를 선언한 카드의 설명에는 「유지」가 있어야 키워드 툴팁이 걸린다.");
            }
        }

        // ------------------------------------------------------------------ helpers

        private static CombatState CreateState()
        {
            var config = TestCombatConfigs.Standard(
                actionBudget: 4, movementHandSize: MovementHandSize, actionHandSize: ActionHandSize);
            return new CombatState(
                CombatState.CreateDemoMap(3),
                PlayerCoord,
                new HexCoord(3, 0),
                config,
                cardCatalog: CreateCatalog());
        }

        private static CardCatalogDefinition CreateCatalog()
        {
            // 유지는 카드 클래스 선언이라 유지 카드는 출하 id(D07·M07)로, 일반 카드는 카탈로그 밖 id로 만든다.
            CardCatalogEntry Move(string id)
                => new CardCatalogEntry(
                    id, id, CardCategory.Movement, CardEffectType.Move,
                    1, 1, 1, "reachable_hex",
                    status: CardCatalogStatus.Approved);
            CardCatalogEntry Defend(string id)
                => new CardCatalogEntry(
                    id, id, CardCategory.Action, CardEffectType.Defend,
                    1, 0, 3, "self",
                    status: CardCatalogStatus.Approved);

            // 덱을 손패 상한보다 넉넉히 채워 "상한까지만 채움"이 실제로 관측되게 한다.
            return new CardCatalogDefinition(
                "retain-turn-end-test",
                "Retain-on-turn-end test catalog",
                new[]
                {
                    Move(CardIds.Shortcut),
                    Move("M-A"),
                    Move("M-B"),
                    Move("M-C"),
                    Defend(CardIds.FullyPrepared),
                    Defend("D-A"),
                    Defend("D-B"),
                    Defend("D-C"),
                    Defend("D-D"),
                    // 사용-후-미복귀 테스트가 버림 더미 재셔플 없이 다음 턴을 채울 만큼 넉넉히.
                    Defend("D-E"),
                    Defend("D-F"),
                });
        }

        private static void AdvanceToPlayerAction(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerAction));
        }

        /// <summary>이동/액션 페이즈를 아무것도 내지 않고 넘겨 다음 턴 드로우까지 굴린다.</summary>
        private static void RunFullTurnWithoutPlaying(CombatState state)
        {
            AdvanceToPlayerAction(state);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
            Assert.That(state.Phase, Is.EqualTo(CombatPhase.PlayerMovement));
        }
    }
}
