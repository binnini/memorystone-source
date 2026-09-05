using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    // 인형뽑기 추첨기(순수) + 스테이지 한정 재화. 연출은 이 결과를 재생만 하므로, 무엇이 나오는지의
    // 계약은 전부 여기서 감시된다.
    public sealed class GachaRewardRollerTests
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

        private static readonly IReadOnlyList<string> TwoRelics = new[] { "relic-a", "relic-b" };
        private static readonly IReadOnlyList<string> TwoItems = new[] { "item-a", "item-b" };

        /// <summary>
        /// 추첨기 자체를 재는 픽스처. 종전 출하 분포(50/35/10/5)를 <b>테스트가 직접</b> 들고 있다 —
        /// 2026-09-01 출하 분포가 「부적 하나」(100/0/0/0)로 굳으면서, 가중치 배분·재정규화·금액 범위를
        /// 출하 표로 재면 규칙이 아니라 밸런스 결정을 재게 되기 때문이다. 출하 값의 감시는
        /// <see cref="ShippedGachaCsvMatchesTheCodeFallbackAndTheAgreedDistribution"/> 하나가 맡는다.
        /// </summary>
        private static readonly GachaRewardWeights MixedWeights = new GachaRewardWeights(
            new[]
            {
                new GachaRewardWeight(GachaRewardKind.CardPack, 50),
                new GachaRewardWeight(GachaRewardKind.Money, 35, 15, 40),
                new GachaRewardWeight(GachaRewardKind.Relic, 10),
                new GachaRewardWeight(GachaRewardKind.Item, 5)
            });

        [Category("ShippingData")]
        [Test]
        public void ShippedGachaCsvMatchesTheCodeFallbackAndTheAgreedDistribution()
        {
            var shipped = GachaRewardWeightsCsvConverter.ConvertFile(CombatCsvPaths.GachaRewardsCsv);

            // T4-3(2026-08-07): 50/40/10 → 50/35/10/5.
            // 2026-09-01 사용자 확정: 100/0/0/0 — 보상뽑기는 「부적 추가」 하나로 고정한다.
            AssertWeight(shipped, GachaRewardKind.CardPack, 100);
            AssertWeight(shipped, GachaRewardKind.Money, 0);
            AssertWeight(shipped, GachaRewardKind.Relic, 0);
            AssertWeight(shipped, GachaRewardKind.Item, 0);

            // 가중치만 보고 넘어가면 「0인데도 나온다」를 놓친다(풀이 비면 항목이 빠지고 남은 것으로
            // 재분배되는 규칙이 있어, 0 행이 유일한 후보가 되는 경로가 실제로 존재한다).
            // 그래서 결과 쪽에서도 못을 박는다: 유물·소모품 풀이 다 차 있어도 나오는 것은 부적뿐이다.
            for (var roll = 0; roll < 100; roll++)
            {
                Assert.That(GachaRewardRoller.Roll(shipped, TwoRelics, new ScriptedRandom(roll, 0), TwoItems).Kind,
                    Is.EqualTo(GachaRewardKind.CardPack), $"롤 {roll}에서 부적이 아닌 것이 나왔다.");
            }

            foreach (var fallback in GachaRewardWeights.Default.Entries)
            {
                Assert.That(shipped.TryGet(fallback.Kind, out var actual), Is.True, $"출하 CSV에 {fallback.Kind} 행이 없다.");
                Assert.That(actual.Weight, Is.EqualTo(fallback.Weight), $"{fallback.Kind} 가중치가 코드 폴백과 다르다.");
                Assert.That(actual.MinAmount, Is.EqualTo(fallback.MinAmount));
                Assert.That(actual.MaxAmount, Is.EqualTo(fallback.MaxAmount));
            }
        }

        [Test]
        public void RollLandsInEachBandAccordingToTheCumulativeWeights()
        {
            // 누적 구간은 CardPack [0,50), Money [50,85), Relic [85,95), Item [95,100).
            Assert.That(RollAt(0).Kind, Is.EqualTo(GachaRewardKind.CardPack));
            Assert.That(RollAt(49).Kind, Is.EqualTo(GachaRewardKind.CardPack));
            Assert.That(RollAt(50).Kind, Is.EqualTo(GachaRewardKind.Money));
            Assert.That(RollAt(84).Kind, Is.EqualTo(GachaRewardKind.Money));
            Assert.That(RollAt(85).Kind, Is.EqualTo(GachaRewardKind.Relic));
            Assert.That(RollAt(94).Kind, Is.EqualTo(GachaRewardKind.Relic));
            Assert.That(RollAt(95).Kind, Is.EqualTo(GachaRewardKind.Item));
            Assert.That(RollAt(99).Kind, Is.EqualTo(GachaRewardKind.Item));
        }

        [Test]
        public void AnEmptyRelicPoolRemovesRelicAndRenormalisesTheRest()
        {
            // 유물이 빠지면 총합이 90이 되어 CardPack [0,50), Money [50,85), Item [85,90)이 된다.
            var weights = MixedWeights;

            Assert.That(GachaRewardRoller.Roll(weights, Array.Empty<string>(), new ScriptedRandom(84), TwoItems).Kind,
                Is.EqualTo(GachaRewardKind.Money));
            Assert.That(GachaRewardRoller.Roll(weights, null, new ScriptedRandom(0), TwoItems).Kind,
                Is.EqualTo(GachaRewardKind.CardPack));
            Assert.That(GachaRewardRoller.Roll(weights, Array.Empty<string>(), new ScriptedRandom(85, 0), TwoItems).Kind,
                Is.EqualTo(GachaRewardKind.Item));

            // 어떤 롤값에서도 유물은 나오지 않아야 한다.
            for (var roll = 0; roll < 90; roll++)
            {
                Assert.That(GachaRewardRoller.Roll(weights, Array.Empty<string>(), new ScriptedRandom(roll, 0), TwoItems).Kind,
                    Is.Not.EqualTo(GachaRewardKind.Relic));
            }
        }

        [Test]
        public void AnEmptyItemPoolRemovesItemAndRenormalisesTheRest()
        {
            // 소모품이 빠지면 총합 95: CardPack [0,50), Money [50,85), Relic [85,95). 어떤 롤값에서도
            // Item은 나오지 않아야 한다(itemPool 미전달 = 구 호출부 호환).
            for (var roll = 0; roll < 95; roll++)
            {
                Assert.That(GachaRewardRoller.Roll(MixedWeights, TwoRelics, new ScriptedRandom(roll, 0)).Kind,
                    Is.Not.EqualTo(GachaRewardKind.Item));
            }
        }

        [Test]
        public void AnItemOutcomeAlwaysNamesAnItemFromThePool()
        {
            var outcome = GachaRewardRoller.Roll(MixedWeights, TwoRelics, new ScriptedRandom(95, 1), TwoItems);

            Assert.That(outcome.Kind, Is.EqualTo(GachaRewardKind.Item));
            Assert.That(TwoItems, Does.Contain(outcome.ItemId),
                "결과는 항상 지급 가능해야 한다 — 풀 밖 아이템이 나오면 안 된다.");
        }

        [Test]
        public void ARelicOutcomeAlwaysNamesARelicFromThePool()
        {
            var outcome = GachaRewardRoller.Roll(MixedWeights, TwoRelics, new ScriptedRandom(90, 1));

            Assert.That(outcome.Kind, Is.EqualTo(GachaRewardKind.Relic));
            Assert.That(TwoRelics, Does.Contain(outcome.RelicId),
                "연출을 다 보고 나서 지급할 유물이 없으면 안 된다 — 결과는 항상 지급 가능해야 한다.");
        }

        [Test]
        public void MoneyAmountStaysInsideTheAuthoredRangeIncludingBothEnds()
        {
            // 금액 범위는 15~40. 두 번째 난수가 금액 오프셋이다(span+1 = 26).
            Assert.That(RollMoneyWithOffset(0).Amount, Is.EqualTo(15));
            Assert.That(RollMoneyWithOffset(25).Amount, Is.EqualTo(40), "최댓값이 뽑힐 수 있어야 한다(양 끝 포함).");

            for (var offset = 0; offset < 26; offset++)
            {
                var amount = RollMoneyWithOffset(offset).Amount;
                Assert.That(amount, Is.InRange(15, 40));
            }
        }

        [Test]
        public void BuildRelicPoolDropsOwnedRelicsAndNeverOffersCurses()
        {
            var owned = new PlayerRelicCurseInventory();
            Assert.That(owned.TryAddDefinition(PlayerPermanentItemCatalog.TigerBadgeRelicId, out var reason), Is.True, reason);

            var pool = GachaRewardRoller.BuildRelicPool(PlayerPermanentItemCatalog.Definitions, owned);

            Assert.That(pool, Is.Not.Empty);
            Assert.That(pool, Does.Not.Contain(PlayerPermanentItemCatalog.TigerBadgeRelicId), "보유한 유물은 후보에서 빠져야 한다(D-9).");
            Assert.That(pool, Does.Not.Contain(PlayerPermanentItemCatalog.CrackedMemoryCurseId), "저주는 뽑기에서 나오지 않는다(D-8).");
        }

        [Test]
        public void BuildRelicPoolGoesEmptyOnceEveryRelicIsOwned()
        {
            var owned = new PlayerRelicCurseInventory();
            foreach (var definition in PlayerPermanentItemCatalog.Definitions.Where(d => d.Kind == PlayerPermanentItemKind.Relic))
            {
                Assert.That(owned.TryAddDefinition(definition.Id, out var reason), Is.True, reason);
            }

            Assert.That(GachaRewardRoller.BuildRelicPool(PlayerPermanentItemCatalog.Definitions, owned), Is.Empty);
        }

        [Test]
        public void AuthoringCannotPutACurseOrAnAmountWhereItDoesNotBelong()
        {
            Assert.That(
                () => GachaRewardWeightsCsvConverter.ConvertText(Csv("Curse,10,0,0,저주를 뽑기에 넣으려는 시도")),
                Throws.TypeOf<FormatException>(),
                "저주는 표현 자체가 불가능해야 한다 — 게임 규칙이라 저작으로 뚫려선 안 된다.");

            Assert.That(
                () => GachaRewardWeightsCsvConverter.ConvertText(Csv("CardPack,50,5,10,금액 없는 종류에 금액")),
                Throws.TypeOf<FormatException>());

            Assert.That(
                () => GachaRewardWeightsCsvConverter.ConvertText(Csv("Money,40,0,0,최댓값 0이면 항상 0원")),
                Throws.TypeOf<FormatException>());

            Assert.That(
                () => GachaRewardWeightsCsvConverter.ConvertText(Csv("Money,40,10,5,범위 역전")),
                Throws.TypeOf<FormatException>());
        }

        [Test]
        public void WalletAddsGrantsAndRefusesToOverspend()
        {
            var wallet = new PlayerWalletState();
            Assert.That(wallet.Balance, Is.Zero, "재화는 스테이지마다 0에서 시작한다.");

            Assert.That(wallet.Add(30), Is.EqualTo(30));
            Assert.That(wallet.Add(-5), Is.Zero, "음수 지급은 무시된다.");
            Assert.That(wallet.Balance, Is.EqualTo(30));

            Assert.That(wallet.TrySpend(31, out var tooMuch), Is.False);
            Assert.That(tooMuch, Does.Contain("Not enough"));
            Assert.That(wallet.Balance, Is.EqualTo(30), "실패한 지불은 잔액을 건드리지 않는다.");

            Assert.That(wallet.TrySpend(30, out _), Is.True);
            Assert.That(wallet.Balance, Is.Zero);
        }

        [Test]
        public void WalletBalanceSurvivesTheSuspendResumeRoundTrip()
        {
            var inventory = new PlayerInventoryState();
            inventory.Wallet.Add(123);

            var restored = PlayerInventorySaveData.FromState(inventory).ToState();

            Assert.That(restored.Wallet.Balance, Is.EqualTo(123),
                "이 세이브는 같은 전투의 중단→재개용이다. 잔액이 0으로 돌아가면 번 돈이 사라진다.");
        }

        private static GachaOutcome RollAt(int roll)
        {
            return GachaRewardRoller.Roll(MixedWeights, TwoRelics, new ScriptedRandom(roll, 0), TwoItems);
        }

        private static GachaOutcome RollMoneyWithOffset(int offset)
        {
            var outcome = GachaRewardRoller.Roll(MixedWeights, TwoRelics, new ScriptedRandom(50, offset));
            Assert.That(outcome.Kind, Is.EqualTo(GachaRewardKind.Money));
            return outcome;
        }

        private static void AssertWeight(GachaRewardWeights weights, GachaRewardKind kind, int expected)
        {
            Assert.That(weights.TryGet(kind, out var entry), Is.True, $"{kind} 행이 없다.");
            Assert.That(entry.Weight, Is.EqualTo(expected));
        }

        private static string Csv(string row)
        {
            return "outcomeKind,weight,minAmount,maxAmount,designerNote\n" + row + "\n";
        }
    }
}
