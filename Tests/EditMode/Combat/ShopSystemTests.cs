using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    // 상점(팝업 스토어): 가격표 CSV + 재고 추첨기(순수) + 거래 트랜잭션 + 덱 전체 카드 제거.
    // UI는 결과를 그릴 뿐이므로, 무엇이 진열되고 어떻게 결제되는지의 계약은 전부 여기서 감시된다.
    public sealed class ShopSystemTests
    {
        /// <summary>미리 정한 값을 순서대로 돌려주는 결정적 난수. 다 쓰면 마지막 값을 반복한다.</summary>
        private sealed class ScriptedRandom : IRewardRandom
        {
            private readonly int[] values;
            private int index;

            public ScriptedRandom(params int[] values)
            {
                this.values = values != null && values.Length > 0 ? values : new[] { 0 };
            }

            public int Next(int maxExclusive)
            {
                Assert.That(maxExclusive, Is.GreaterThan(0), "추첨기는 maxExclusive <= 0을 넘기면 안 된다.");
                var value = values[Math.Min(index, values.Length - 1)];
                index++;
                return value % maxExclusive;
            }
        }

        // ── 가격표 CSV ───────────────────────────────────────────────────────────────

        [Category("ShippingData")]
        [Test]
        public void ShippedShopPriceCsvMatchesTheCodeFallbackAndTheAgreedPrices()
        {
            var shipped = ShopPricesCsvConverter.ConvertFile(CombatCsvPaths.ShopPricesCsv);

            AssertCardPrice(shipped, CardRarity.Rare, 50);
            AssertCardPrice(shipped, CardRarity.Epic, 80);
            AssertCardPrice(shipped, CardRarity.Legendary, 120);
            // DEC-2026-08-31-01 Q1: 유물도 등급 기준가 + 행별 priceDelta로 간다(종전 단일가 100).
            AssertPrice(shipped, ShopItemKind.Relic, CardRarity.Rare, 70);
            AssertPrice(shipped, ShopItemKind.Relic, CardRarity.Epic, 100);
            AssertPrice(shipped, ShopItemKind.Relic, CardRarity.Legendary, 140);
            AssertPrice(shipped, ShopItemKind.CardRemoval, 60);

            foreach (var fallback in ShopPrices.Default.Entries)
            {
                // 등급형(카드·유물·소모품)은 등급까지 맞춰 조회한다 — 종류만으로 물으면 가장 싼
                // 티어가 돌아와 Epic·Legendary 행이 비교에서 통째로 빠진다.
                var found = ShopPrices.IsTiered(fallback.Kind)
                    ? shipped.TryGetPrice(fallback.Kind, fallback.Rarity, out var price)
                    : shipped.TryGetPrice(fallback.Kind, out price);
                Assert.That(found, Is.True, $"출하 CSV에 {fallback.Kind}/{fallback.Rarity} 행이 없다.");
                Assert.That(price, Is.EqualTo(fallback.Price), $"{fallback.Kind}/{fallback.Rarity} 가격이 코드 폴백과 다르다.");
            }
        }

        [Test]
        public void AuthoringCannotPriceBasicCardsCursesOrFreeItems()
        {
            Assert.That(
                () => ShopPricesCsvConverter.ConvertText(Csv("Card,Basic,10,기본 등급을 팔려는 시도")),
                Throws.TypeOf<FormatException>(),
                "기본 등급은 상점에 나오지 않는다 — 게임 규칙이라 저작으로 뚫려선 안 된다(CR-2 선례).");

            Assert.That(
                () => ShopPricesCsvConverter.ConvertText(Csv("Curse,,50,저주를 팔려는 시도")),
                Throws.TypeOf<FormatException>(),
                "저주는 표현 자체가 불가능해야 한다 — ShopItemKind에 Curse가 없다.");

            // 유물·소모품은 이제 등급형이다(DEC-2026-08-31-01·-02) — 등급이 없는 것은 카드 제거뿐이고,
            // 거기 등급을 적는 것이 이제 저작 실수다.
            Assert.That(
                () => ShopPricesCsvConverter.ConvertText(Csv("CardRemoval,Rare,60,단일가 품목에 등급을 적은 실수")),
                Throws.TypeOf<FormatException>());

            Assert.That(
                () => ShopPricesCsvConverter.ConvertText(Csv("Relic,,100,등급형 품목에 등급을 안 적은 실수")),
                Throws.TypeOf<FormatException>());

            Assert.That(
                () => ShopPricesCsvConverter.ConvertText(Csv("Relic,Rare,0,공짜 유물")),
                Throws.TypeOf<FormatException>(),
                "0가격은 공짜 판매가 아니라 저작 실수다 — TrySpend가 0 이하를 거부해 판매 시점에 터진다.");

            Assert.That(
                () => ShopPricesCsvConverter.ConvertText(Csv("Card,Rare,50,중복 등급\nCard,Rare,60,중복 등급")),
                Throws.TypeOf<FormatException>());
        }

        // ── 재고 추첨기 ─────────────────────────────────────────────────────────────

        private static readonly IReadOnlyList<CardRewardCandidate> FourCandidates = new[]
        {
            new CardRewardCandidate("card-rare", CardRarity.Rare),
            new CardRewardCandidate("card-epic", CardRarity.Epic),
            new CardRewardCandidate("card-legend", CardRarity.Legendary),
            new CardRewardCandidate("card-basic", CardRarity.Basic)
        };

        private static readonly IReadOnlyList<string> ThreeRelics = new[] { "relic-a", "relic-b", "relic-c" };

        [Test]
        public void RollFillsCardRelicAndRemovalSlotsWithDistinctPurchasableStock()
        {
            var inventory = ShopInventoryRoller.Roll(
                FourCandidates, CardRewardRarityWeights.Default, ShopPrices.Default, ThreeRelics, new ScriptedRandom(0));

            Assert.That(inventory.Cards.Count, Is.EqualTo(ShopInventoryRoller.CardSlotCount));
            Assert.That(inventory.Cards.Select(stock => stock.ItemId).Distinct().Count(), Is.EqualTo(inventory.Cards.Count),
                "같은 카드가 두 칸에 진열되면 안 된다.");
            Assert.That(inventory.Cards.Select(stock => stock.ItemId), Does.Not.Contain("card-basic"),
                "기본 등급은 가격이 없으므로 재고 후보에서 빠져야 한다.");

            foreach (var stock in inventory.Cards)
            {
                var expected = stock.ItemId == "card-rare" ? 50 : stock.ItemId == "card-epic" ? 80 : 120;
                Assert.That(stock.Price, Is.EqualTo(expected), $"{stock.ItemId} 가격이 가격표와 다르다.");
            }

            Assert.That(inventory.Relics.Count, Is.EqualTo(ShopInventoryRoller.RelicSlotCount));
            Assert.That(inventory.Relics.Select(stock => stock.ItemId).Distinct().Count(), Is.EqualTo(inventory.Relics.Count));
            Assert.That(inventory.Relics.All(stock => ThreeRelics.Contains(stock.ItemId)), Is.True,
                "진열된 유물은 항상 (보유분을 뺀) 풀에서 나와야 한다 — 사고 나서 지급이 거부되면 안 된다.");
            // ThreeRelics는 카탈로그에 없는 합성 id다 — 등급이 해소되지 않으므로 문서화된 폴백
            // (그 종류의 가장 싼 티어 = Rare 70)으로 값이 매겨진다. 출하 id는 이 경로를 타지 않는다.
            Assert.That(inventory.Relics.All(stock => stock.Price == 70), Is.True);

            Assert.That(inventory.CardRemovalOffered, Is.True);
            Assert.That(inventory.CardRemovalPrice, Is.EqualTo(60));
        }

        [Test]
        public void AnEmptyRelicPoolLeavesTheRelicShelfEmptyInsteadOfSellingDuplicates()
        {
            var inventory = ShopInventoryRoller.Roll(
                FourCandidates, CardRewardRarityWeights.Default, ShopPrices.Default, Array.Empty<string>(), new ScriptedRandom(0));

            Assert.That(inventory.Relics, Is.Empty);
            Assert.That(inventory.Cards, Is.Not.Empty, "유물 풀이 말라도 카드 진열은 계속된다.");
        }

        [Test]
        public void ThePriceTableIsTheAllowList()
        {
            // 유물·제거 가격이 저작되지 않은 표: 그 칸 자체가 진열되지 않아야 한다.
            var cardOnly = new ShopPrices(new[] { new ShopPriceEntry(ShopItemKind.Card, CardRarity.Rare, 50) });

            var inventory = ShopInventoryRoller.Roll(
                FourCandidates, CardRewardRarityWeights.Default, cardOnly, ThreeRelics, new ScriptedRandom(0));

            Assert.That(inventory.Relics, Is.Empty, "가격 없는 유물이 진열되면 살 수 없는 재고가 된다.");
            Assert.That(inventory.CardRemovalOffered, Is.False);
            Assert.That(inventory.Cards.Select(stock => stock.ItemId), Is.EquivalentTo(new[] { "card-rare" }),
                "가격표에 있는 등급(희귀)만 후보가 된다 — 표가 곧 허용 목록이다.");
        }

        // ── 거래 트랜잭션 ────────────────────────────────────────────────────────────

        [Test]
        public void PurchasingACardDebitsTheWalletAndPutsTheCardInTheCurrentHand()
        {
            var state = CreateApprovedCatalogState();
            state.PlayerInventory.Wallet.Add(100);
            var cardId = ApprovedCardCatalogFactory.FieldFlashbangId;
            var beforeHand = CountHandCards(state, cardId);
            var beforeDeckData = TotalPlayerDeckInstances(state);

            Assert.That(state.TryPurchaseShopCard(cardId, 80, out var reason), Is.True, reason);

            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(20));
            Assert.That(CountHandCards(state, cardId), Is.EqualTo(beforeHand + 1), "산 카드는 즉시 현재 손패로 들어간다(CR-8).");
            Assert.That(TotalPlayerDeckInstances(state), Is.EqualTo(beforeDeckData + 1), "산 카드는 영구 덱에도 편입된다.");
        }

        [Test]
        public void AFailedPurchaseNeverTouchesTheWallet()
        {
            var state = CreateApprovedCatalogState();
            state.PlayerInventory.Wallet.Add(50);

            Assert.That(state.TryPurchaseShopCard(ApprovedCardCatalogFactory.FieldFlashbangId, 80, out var poor), Is.False);
            Assert.That(poor, Does.Contain("Not enough"));
            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(50), "잔액 부족 실패는 차감하지 않는다.");

            Assert.That(state.TryPurchaseShopCard("card-does-not-exist", 30, out var unknown), Is.False);
            Assert.That(unknown, Is.Not.Empty);
            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(50), "지급 실패는 환불된다 — 돈이 증발하면 안 된다.");
        }

        [Test]
        public void PurchasingARelicGoesThroughThePermanentItemGate()
        {
            var state = CreateApprovedCatalogState();
            state.PlayerInventory.Wallet.Add(250);
            var maxHpBefore = state.Player.MaxHp;

            // 수문장 조끼(MaxHpBonus +10): 인벤토리 직행이 아니라 TryGrantPermanentItem을 지나야만
            // 최대 체력이 실제로 오른다(RC-4b). 이 구매가 그 관문을 지나는지가 이 테스트의 핵심이다.
            Assert.That(state.TryPurchaseShopRelic("relic-gwanghwamun-armor", 100, out var reason), Is.True, reason);

            Assert.That(state.Player.MaxHp, Is.EqualTo(maxHpBefore + 10), "구매가 지급 관문을 우회하면 최대 체력 유물만 조용히 무효가 된다.");
            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(150));

            // 같은 유물을 다시 사려는 시도: 중복은 관문이 거부하고 전액 환불된다.
            Assert.That(state.TryPurchaseShopRelic("relic-gwanghwamun-armor", 100, out var duplicate), Is.False);
            Assert.That(duplicate, Is.Not.Empty);
            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(150), "중복 거부는 환불된다.");
        }

        [Test]
        public void PurchasingACardRemovalRemovesFromAnyPileAndTheRunDeck()
        {
            var state = CreateApprovedCatalogState();
            state.PlayerInventory.Wallet.Add(60);

            // 손패가 아니라 뽑을 더미의 카드를 고른다 — 손패 한정이던 기존 제거 API와의 차이가 계약이다.
            var target = state.GetDrawPileCards().FirstOrDefault();
            Assert.That(target.SelectionKey, Is.Not.Empty, "테스트 전제: 뽑을 더미에 카드가 있어야 한다.");
            var beforeDeckData = TotalPlayerDeckInstances(state);
            var beforeRemoved = state.GetExilePileCards().Count;

            Assert.That(state.TryPurchaseShopCardRemoval(target.SelectionKey, 60, out var reason), Is.True, reason);

            Assert.That(state.PlayerInventory.Wallet.Balance, Is.Zero);
            Assert.That(state.GetExilePileCards().Count, Is.EqualTo(beforeRemoved + 1), "제거된 카드는 소멸 더미로 간다.");
            Assert.That(state.GetDeckListCards().Select(snapshot => snapshot.SelectionKey), Does.Not.Contain(target.SelectionKey));
            Assert.That(TotalPlayerDeckInstances(state), Is.EqualTo(beforeDeckData - 1), "런 지속 덱에서도 지워져야 세이브 복원 후 제거가 유지된다.");

            // 이미 소멸된 카드를 다시 지우려는 시도는 실패하고 환불된다.
            state.PlayerInventory.Wallet.Add(60);
            Assert.That(state.TryPurchaseShopCardRemoval(target.SelectionKey, 60, out _), Is.False);
            Assert.That(state.PlayerInventory.Wallet.Balance, Is.EqualTo(60));
        }

        [Test]
        public void ConsumingAShopObjectIsOneShotAndLandsInTheClaimedLedger()
        {
            var state = CreateApprovedCatalogState();

            Assert.That(state.TryConsumeShopObject("shop-test-1", out var reason), Is.True, reason);
            Assert.That(state.ClaimedEventObjectIds, Does.Contain("shop-test-1"),
                "소비 대장을 공유해야 세이브 중단→재개에서도 소비가 유지된다.");

            Assert.That(state.TryConsumeShopObject("shop-test-1", out var again), Is.False, "1회 방문 소비 — 두 번 소비되면 안 된다.");
            Assert.That(again, Is.Not.Empty);
        }

        // ── 덱 전체 제거 (CardCore) ──────────────────────────────────────────────────

        [Test]
        public void PermanentRemoveAnywhereReachesEveryPileButNeverTheRemovedPile()
        {
            var first = new CardDefinition("first", "First", CardCategory.Movement, CardEffectType.Move, 0, 1, 1);
            var second = new CardDefinition("second", "Second", CardCategory.Movement, CardEffectType.Move, 0, 2, 2);
            var third = new CardDefinition("third", "Third", CardCategory.Movement, CardEffectType.Move, 0, 3, 3);
            var deck = new CardDeckState(new[] { first, second, third });

            // 뽑을 더미에서 제거.
            Assert.That(deck.PermanentRemoveAnywhere(first), Is.True);
            Assert.That(deck.RemovedPile, Does.Contain(first));

            // 버림 더미에서 제거.
            deck.Draw(1);
            var drawn = deck.Hand[0];
            deck.DiscardFromHand(drawn);
            Assert.That(deck.PermanentRemoveAnywhere(drawn), Is.True);

            // 이미 소멸된 카드는 다시 제거할 수 없다 — 소멸 더미는 대상이 아니다.
            Assert.That(deck.PermanentRemoveAnywhere(first), Is.False);
            Assert.That(deck.PermanentRemoveAnywhere(null), Is.False);
            Assert.That(deck.RemovedCount, Is.EqualTo(2));
        }

        [Test]
        public void PlayerDeckDataRemoveCardTargetsExactlyOneInstanceById()
        {
            var catalog = ApprovedCardCatalogFactory.CreateApprovedCatalog(CombatConfig.Default);
            var deck = PlayerDeckData.FromCatalog(catalog);
            var target = deck.ActionCards.FirstOrDefault() ?? deck.MovementCards.First();
            var before = deck.MovementCards.Count + deck.ActionCards.Count;

            var removed = deck.RemoveCard(target.InstanceId);

            Assert.That(removed.MovementCards.Count + removed.ActionCards.Count, Is.EqualTo(before - 1));
            Assert.That(removed.MovementCards.Concat(removed.ActionCards).Select(card => card.InstanceId),
                Does.Not.Contain(target.InstanceId));
            Assert.That(deck.MovementCards.Count + deck.ActionCards.Count, Is.EqualTo(before), "원본은 불변이다.");
            Assert.That(ReferenceEquals(deck.RemoveCard("no-such-instance"), deck), Is.True, "없는 인스턴스면 같은 덱을 돌려준다.");
        }

        // ── 헬퍼 ───────────────────────────────────────────────────────────────────

        private static CombatState CreateApprovedCatalogState()
        {
            return new CombatState(
                CombatState.CreateDemoMap(2),
                new SeoulPlayup.Map.Runtime.HexCoord(-1, 0),
                new SeoulPlayup.Map.Runtime.HexCoord(2, 0),
                CombatConfig.Default,
                cardCatalog: ApprovedCardCatalogFactory.CreateApprovedCatalog(CombatConfig.Default));
        }

        private static int CountHandCards(CombatState state, string cardId)
        {
            return state.GetHandCards().Count(snapshot => snapshot.Id == cardId);
        }

        private static int TotalPlayerDeckInstances(CombatState state)
        {
            return state.PlayerDeck.MovementCards.Count + state.PlayerDeck.ActionCards.Count;
        }

        private static void AssertCardPrice(ShopPrices prices, CardRarity rarity, int expected)
        {
            Assert.That(prices.TryGetCardPrice(rarity, out var price), Is.True, $"{rarity} 카드 가격 행이 없다.");
            Assert.That(price, Is.EqualTo(expected));
        }

        private static void AssertPrice(ShopPrices prices, ShopItemKind kind, CardRarity rarity, int expected)
        {
            Assert.That(prices.TryGetPrice(kind, rarity, out var price), Is.True, $"{kind}/{rarity} 가격 행이 있어야 한다.");
            Assert.That(price, Is.EqualTo(expected), $"{kind}/{rarity} 기준가.");
        }

        private static void AssertPrice(ShopPrices prices, ShopItemKind kind, int expected)
        {
            Assert.That(prices.TryGetPrice(kind, out var price), Is.True, $"{kind} 가격 행이 없다.");
            Assert.That(price, Is.EqualTo(expected));
        }

        private static string Csv(string rows)
        {
            return "itemKind,rarity,price,designerNote\n" + rows + "\n";
        }
    }
}
