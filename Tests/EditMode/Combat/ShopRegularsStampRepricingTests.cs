using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// N3(DEC-2026-08-31-01): <b>단골 도장을 사면 그 상점의 남은 재고가 그 자리에서 싸진다.</b>
    /// <para>
    /// 종전에는 진입 시 가격을 확정했으므로 같은 방문에서 산 도장은 그 방문에 아무 효과가 없었고,
    /// 「상점 전 품목 20% 할인」이라는 문안이 <b>거짓</b>이었다. 이 결정의 값은 그 문장이 참이 되는
    /// 것이다.
    /// </para>
    /// <para>
    /// 🔴 지켜야 하는 계약은 둘이다: ①재계산은 <b>추첨을 다시 돌리지 않는다</b>(같은 물건이 같은
    /// 자리에 남는다) ②진열가와 결제가가 <b>같은 표</b>에서 나온다 — 재계산 뒤에도 갈라지면 안 된다.
    /// </para>
    /// </summary>
    public sealed class ShopRegularsStampRepricingTests
    {
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
                var value = values[Math.Min(index, values.Length - 1)];
                index++;
                return value % maxExclusive;
            }
        }

        private static readonly IReadOnlyList<CardRewardCandidate> Candidates = new[]
        {
            new CardRewardCandidate("card-rare", CardRarity.Rare),
            new CardRewardCandidate("card-epic", CardRarity.Epic),
            new CardRewardCandidate("card-legend", CardRarity.Legendary)
        };

        private static readonly IReadOnlyList<string> RelicPool = new[] { "relic-a", "relic-b" };
        private static readonly IReadOnlyList<string> ItemPool = new[] { "item-a", "item-b" };

        private static ShopInventory RollFullShop(ShopPrices prices) =>
            ShopInventoryRoller.Roll(
                Candidates, CardRewardRarityWeights.Default, prices, RelicPool, new ScriptedRandom(0), ItemPool);

        [Test]
        public void RepricingKeepsTheSameStockInTheSameSlotsAndOnlyChangesThePrices()
        {
            var inventory = RollFullShop(ShopPrices.Default);
            var repriced = ShopInventoryRoller.Reprice(inventory, ShopPrices.Default.WithDiscountPercent(20), Candidates);

            // ①추첨을 다시 돌리지 않는다.
            Assert.That(
                repriced.Cards.Select(stock => stock.ItemId).ToArray(),
                Is.EqualTo(inventory.Cards.Select(stock => stock.ItemId).ToArray()),
                "재계산이 카드 재고를 다시 뽑았다 — 값만 바뀌어야 한다.");
            Assert.That(
                repriced.Relics.Select(stock => stock.ItemId).ToArray(),
                Is.EqualTo(inventory.Relics.Select(stock => stock.ItemId).ToArray()));
            Assert.That(
                repriced.Items.Select(stock => stock.ItemId).ToArray(),
                Is.EqualTo(inventory.Items.Select(stock => stock.ItemId).ToArray()));
            Assert.That(repriced.CardRemovalOffered, Is.EqualTo(inventory.CardRemovalOffered));

            // ②값은 전부 내려간다.
            for (var i = 0; i < inventory.Cards.Count; i++)
            {
                Assert.That(repriced.Cards[i].Price, Is.LessThan(inventory.Cards[i].Price));
            }

            Assert.That(repriced.CardRemovalPrice, Is.LessThan(inventory.CardRemovalPrice));
        }

        [Test]
        public void RepricedValuesComeFromTheSameTableARollWouldHaveUsed()
        {
            // 진열가와 결제가가 같은 표에서 나온다는 계약의 못. "20% 깎인 표로 처음부터 뽑았다면
            // 나왔을 값"과 "진입 뒤 재계산한 값"이 한 푼도 다르면 안 된다 — 다르면 어느 한쪽이
            // 자기만의 계산식을 갖고 있다는 뜻이다.
            var discounted = ShopPrices.Default.WithDiscountPercent(20);

            var rolledWithDiscount = RollFullShop(discounted);
            var repriced = ShopInventoryRoller.Reprice(RollFullShop(ShopPrices.Default), discounted, Candidates);

            AssertSamePrices(rolledWithDiscount.Cards, repriced.Cards, "카드");
            AssertSamePrices(rolledWithDiscount.Relics, repriced.Relics, "유물");
            AssertSamePrices(rolledWithDiscount.Items, repriced.Items, "소모품");
            Assert.That(repriced.CardRemovalPrice, Is.EqualTo(rolledWithDiscount.CardRemovalPrice));
        }

        [Test]
        public void RepricingWithTheSameTableIsANoOp()
        {
            // 할인 유물이 아닌 것을 샀을 때 값이 흔들리면 안 된다.
            var inventory = RollFullShop(ShopPrices.Default);
            var repriced = ShopInventoryRoller.Reprice(inventory, ShopPrices.Default, Candidates);

            AssertSamePrices(inventory.Cards, repriced.Cards, "카드");
            AssertSamePrices(inventory.Relics, repriced.Relics, "유물");
            AssertSamePrices(inventory.Items, repriced.Items, "소모품");
            Assert.That(repriced.CardRemovalPrice, Is.EqualTo(inventory.CardRemovalPrice));
        }

        [Test]
        public void CardsWhoseRarityIsUnknownKeepTheirCurrentPriceInsteadOfBeingInvented()
        {
            var inventory = RollFullShop(ShopPrices.Default);
            var repriced = ShopInventoryRoller.Reprice(inventory, ShopPrices.Default.WithDiscountPercent(50), cardCandidates: null);

            AssertSamePrices(inventory.Cards, repriced.Cards, "등급 미상 카드");
            // 유물·소모품은 카탈로그가 등급을 아는 축이라 후보 목록 없이도 재계산된다.
            Assert.That(
                repriced.Relics.Select(stock => stock.Price),
                Is.All.LessThan(inventory.Relics[0].Price),
                "유물은 카탈로그에서 등급이 해소되므로 후보 목록과 무관하게 깎여야 한다.");
        }

        [Test]
        [Category("ShippingData")]
        public void TheShippedRegularsStampStillCarriesTheDiscountEffectItsTextPromises()
        {
            // 문안과 규칙이 갈라지면 N3가 고친 거짓말이 되돌아온다.
            Assert.That(PlayerPermanentItemCatalog.TryGet("relic-regulars-stamp", out var definition), Is.True,
                "단골 도장이 저작에서 사라졌다 — N3의 대상 자체가 없다.");
            Assert.That(definition.EffectKind, Is.EqualTo(PlayerPermanentItemEffectKind.ShopDiscountPercent));
            Assert.That(definition.EffectAmount, Is.GreaterThan(0));
            StringAssert.Contains($"{definition.EffectAmount}%", definition.Description,
                "문안의 할인율과 저작된 수치가 다르다.");
        }

        private static void AssertSamePrices(
            IReadOnlyList<ShopStockItem> expected,
            IReadOnlyList<ShopStockItem> actual,
            string label)
        {
            Assert.That(actual.Count, Is.EqualTo(expected.Count), $"{label} 칸 수가 달라졌다.");
            for (var i = 0; i < expected.Count; i++)
            {
                Assert.That(actual[i].ItemId, Is.EqualTo(expected[i].ItemId), $"{label} {i}번 칸의 물건이 바뀌었다.");
                Assert.That(actual[i].Price, Is.EqualTo(expected[i].Price),
                    $"{label} «{expected[i].ItemId}»의 값이 다른 계산식에서 나왔다.");
            }
        }
    }
}
