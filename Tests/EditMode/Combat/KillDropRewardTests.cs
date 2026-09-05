using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 몬스터 처치 전리품(DEC-2026-08-31-03 — D-2 재개정).
    /// <para>
    /// 「부적 추가」는 목록에 <b>언제나</b> 서고 그 위에 엽전·소모품·유물이 오르거나 안 오른다. 이 스위트가
    /// 지키는 것은 급별 규칙(유물은 정예·보스에서만)과 추첨의 결정성이다 — 지급 규칙(CR-10 돈
    /// 폴백 · D-9 보유분 제외)은 기존 것을 재사용하므로 여기서 새로 정의하지 않는다.
    /// </para>
    /// </summary>
    public sealed class KillDropRewardTests
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

            public int Calls => index;

            public int Next(int maxExclusive)
            {
                Assert.That(maxExclusive, Is.GreaterThan(0));
                var value = values[Math.Min(index, values.Length - 1)];
                index++;
                return value % maxExclusive;
            }
        }

        private static readonly IReadOnlyList<string> ItemPool = new[] { "item-a", "item-b" };
        private static readonly IReadOnlyList<string> RelicPool = new[] { "relic-a", "relic-b" };

        /// <summary>
        /// 출하 표와 같은 소모품·유물 확률이되 <b>돈이 없는</b> 표. 돈은 가장 먼저 굴러 난수를
        /// 소비하므로, 소모품·유물 축의 경계·독립성을 볼 때는 그 소비를 빼고 봐야 시험이 읽힌다.
        /// </summary>
        private static readonly KillDropRates NoMoneyRates = new KillDropRates(new[]
        {
            new KillDropRateEntry(MonsterKillTier.Normal, KillDropKind.Item, 8),
            new KillDropRateEntry(MonsterKillTier.Normal, KillDropKind.Relic, 0),
            new KillDropRateEntry(MonsterKillTier.Elite, KillDropKind.Item, 60),
            new KillDropRateEntry(MonsterKillTier.Elite, KillDropKind.Relic, 25)
        });

        // ── 저작 ─────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void ShippedKillDropCsvMatchesTheCodeFallbackAndTheAgreedRates()
        {
            var shipped = KillDropRatesCsvConverter.ConvertFile(CombatCsvPaths.KillDropRatesCsv);

            Assert.That(shipped.PercentFor(MonsterKillTier.Normal, KillDropKind.Item), Is.EqualTo(8));
            Assert.That(shipped.PercentFor(MonsterKillTier.Normal, KillDropKind.Relic), Is.EqualTo(0),
                "일반 몬스터가 유물을 떨구면 「정예를 찾아 싸운다」는 이유가 사라진다.");
            Assert.That(shipped.PercentFor(MonsterKillTier.Elite, KillDropKind.Item), Is.EqualTo(60));
            Assert.That(shipped.PercentFor(MonsterKillTier.Elite, KillDropKind.Relic), Is.EqualTo(25));
            Assert.That(shipped.PercentFor(MonsterKillTier.Boss, KillDropKind.Item), Is.EqualTo(100));
            Assert.That(shipped.PercentFor(MonsterKillTier.Boss, KillDropKind.Relic), Is.EqualTo(100));

            // 엽전(DEC-2026-08-31-03). ⚠️ 수치는 잠정치라 실플레이 후 잡화점 가격과 함께 재조정한다 —
            // 여기서 고정하는 것은 "저작과 폴백이 갈라지지 않는다"이지 밸런스가 아니다.
            // 2026-09-01 사용자 확정: 일반 처치 엽전은 확률이 아니라 기본 보상이다(35%·5~12 → 100%·30~50).
            // 정예·보스는 급이 뒤집히지 않도록 함께 올렸다 — 일반보다 적게 주는 정예는 급이 아니다.
            Assert.That(shipped.PercentFor(MonsterKillTier.Normal, KillDropKind.Money), Is.EqualTo(100));
            Assert.That(shipped.MoneyRangeFor(MonsterKillTier.Normal), Is.EqualTo((30, 50)));
            Assert.That(shipped.MoneyRangeFor(MonsterKillTier.Elite), Is.EqualTo((60, 90)));
            Assert.That(shipped.MoneyRangeFor(MonsterKillTier.Boss), Is.EqualTo((150, 250)));

            // 급의 서열은 수치보다 오래 간다 — 한쪽만 올리다 뒤집히는 것이 실제로 일어난 실수다.
            Assert.That(shipped.MoneyRangeFor(MonsterKillTier.Elite).Min,
                Is.GreaterThan(shipped.MoneyRangeFor(MonsterKillTier.Normal).Max),
                "정예 최소가 일반 최대보다 커야 급이 읽힌다.");
            Assert.That(shipped.MoneyRangeFor(MonsterKillTier.Boss).Min,
                Is.GreaterThan(shipped.MoneyRangeFor(MonsterKillTier.Elite).Max),
                "보스 최소가 정예 최대보다 커야 급이 읽힌다.");

            foreach (var fallback in KillDropRates.Default.Entries)
            {
                Assert.That(
                    shipped.PercentFor(fallback.Tier, fallback.Kind),
                    Is.EqualTo(fallback.Percent),
                    $"{fallback.Tier}/{fallback.Kind} 확률이 코드 폴백과 다르다 — 둘 중 하나가 낡았다.");
            }

            foreach (var tier in new[] { MonsterKillTier.Normal, MonsterKillTier.Elite, MonsterKillTier.Boss })
            {
                Assert.That(
                    shipped.MoneyRangeFor(tier),
                    Is.EqualTo(KillDropRates.Default.MoneyRangeFor(tier)),
                    $"{tier} 금액 범위가 코드 폴백과 다르다.");
            }
        }

        [Test]
        public void TheConverterRefusesRatesThatCannotMeanAnything()
        {
            const string head = "tier,dropKind,percent,minAmount,maxAmount,designerNote\n";
            Assert.Throws<FormatException>(() => KillDropRatesCsvConverter.ConvertText(
                head + "normal,Item,101,0,0,"), "100을 넘는 확률은 저작 실수다.");
            Assert.Throws<FormatException>(() => KillDropRatesCsvConverter.ConvertText(
                head + "normal,Item,-1,0,0,"));
            Assert.Throws<FormatException>(() => KillDropRatesCsvConverter.ConvertText(
                head + "legendary,Item,50,0,0,"), "없는 급은 조용히 무시하면 안 된다.");
            Assert.Throws<FormatException>(() => KillDropRatesCsvConverter.ConvertText(
                head + "normal,Card,50,0,0,"), "부적은 확률이 아니라 목록에 언제나 서는 줄이다.");
            Assert.Throws<FormatException>(() => KillDropRatesCsvConverter.ConvertText(
                head + "normal,Item,8,0,0,\nnormal,Item,20,0,0,"), "같은 칸을 두 번 저작하면 어느 쪽이 이기는지 알 수 없다.");
            Assert.Throws<FormatException>(() => KillDropRatesCsvConverter.ConvertText(
                head + "normal,Money,50,0,0,"), "떨어지는데 0엽전인 돈 행은 저작 실수다.");
            Assert.Throws<FormatException>(() => KillDropRatesCsvConverter.ConvertText(
                head + "normal,Money,50,30,10,"), "상한이 하한보다 낮으면 안 된다.");
            Assert.Throws<FormatException>(() => KillDropRatesCsvConverter.ConvertText(
                head + "normal,Item,8,5,10,"), "금액이 안 쓰이는 종류에 적혀 있으면 조용히 무시하지 않는다.");
        }

        // ── 급 판별 ──────────────────────────────────────────────────────

        [Test]
        public void TheTierComesFromTheSpawnRoleThePlacementStamped()
        {
            // 급은 카탈로그 컬럼이 아니라 배치 시점의 승격이고, 런타임에는 SpawnRole이 그 표시다.
            Assert.That(KillDropRates.TierFromSpawnRole(MonsterSpawnRoles.Boss), Is.EqualTo(MonsterKillTier.Boss));
            Assert.That(KillDropRates.TierFromSpawnRole(MonsterSpawnRoles.Elite), Is.EqualTo(MonsterKillTier.Elite));
            Assert.That(KillDropRates.TierFromSpawnRole(MonsterSpawnRoles.NormalEnemy), Is.EqualTo(MonsterKillTier.Normal));
            Assert.That(KillDropRates.TierFromSpawnRole(null), Is.EqualTo(MonsterKillTier.Normal),
                "역할이 비어 있으면 일반이다 — 모르는 것을 정예로 승격시키지 않는다.");
        }

        // ── 추첨 ─────────────────────────────────────────────────────────

        [Test]
        public void BossKillsAlwaysDropBothAndNormalKillsNeverDropARelic()
        {
            var boss = KillDropRoller.Roll(
                MonsterKillTier.Boss, KillDropRates.Default, ItemPool, RelicPool, new ScriptedRandom(99));
            Assert.That(boss.HasItem, Is.True, "보스는 소모품 확정이다.");
            Assert.That(boss.HasRelic, Is.True, "보스는 유물 확정이다.");

            // 난수가 무엇이 나오든 일반 몬스터에서 유물이 나오면 안 된다(확률 0).
            foreach (var roll in new[] { 0, 1, 50, 99 })
            {
                var normal = KillDropRoller.Roll(
                    MonsterKillTier.Normal, KillDropRates.Default, ItemPool, RelicPool, new ScriptedRandom(roll));
                Assert.That(normal.HasRelic, Is.False, $"일반 몬스터가 유물을 떨궜다(난수 {roll}).");
            }
        }

        [Test]
        public void ThePercentIsAStrictThresholdAtBothEdges()
        {
            // 8%면 0~7이 맞고 8은 빗나간다. 경계가 한 칸 밀리면 확률이 조용히 달라진다.
            Assert.That(RollNormalItem(7).HasItem, Is.True, "7 < 8 이므로 떨어져야 한다.");
            Assert.That(RollNormalItem(8).HasItem, Is.False, "8 >= 8 이므로 빗나가야 한다.");
        }

        [Test]
        public void EliteKillsCanProduceEveryCombinationOfTheTwoIndependentRolls()
        {
            // 정예(소모품 60 · 유물 25)는 넷이 다 가능해야 한다 — 두 축이 독립이라는 뜻이다.
            var both = KillDropRoller.Roll(MonsterKillTier.Elite, NoMoneyRates, ItemPool, RelicPool, new ScriptedRandom(0, 0, 0, 0));
            Assert.That(both.HasItem && both.HasRelic, Is.True);

            // 소모품 판정 0(맞음) → 아이템 뽑기 → 유물 판정 90(빗나감)
            var itemOnly = KillDropRoller.Roll(MonsterKillTier.Elite, NoMoneyRates, ItemPool, RelicPool, new ScriptedRandom(0, 0, 90));
            Assert.That(itemOnly.HasItem, Is.True);
            Assert.That(itemOnly.HasRelic, Is.False);

            // 소모품 판정 90(빗나감) → 유물 판정 0(맞음) → 유물 뽑기
            var relicOnly = KillDropRoller.Roll(MonsterKillTier.Elite, NoMoneyRates, ItemPool, RelicPool, new ScriptedRandom(90, 0, 0));
            Assert.That(relicOnly.HasItem, Is.False);
            Assert.That(relicOnly.HasRelic, Is.True);

            var neither = KillDropRoller.Roll(MonsterKillTier.Elite, NoMoneyRates, ItemPool, RelicPool, new ScriptedRandom(90));
            Assert.That(neither.IsEmpty, Is.True);
        }

        [Test]
        public void AnEmptyPoolConsumesNoRandomnessSoTheSameSeedStaysTheSameRun()
        {
            // 🔴 유물 풀이 마른 판과 안 마른 판에서 난수 소비 횟수가 달라지면 같은 시드가 다른
            //    전개를 낳는다 — 결정성이 세이브 정합을 지키는 축이다.
            var withPool = new ScriptedRandom(0);
            KillDropRoller.Roll(MonsterKillTier.Boss, KillDropRates.Default, ItemPool, RelicPool, withPool);

            var withoutRelics = new ScriptedRandom(0);
            var outcome = KillDropRoller.Roll(
                MonsterKillTier.Boss, KillDropRates.Default, ItemPool, Array.Empty<string>(), withoutRelics);

            Assert.That(outcome.HasRelic, Is.False, "풀이 비면 줄 수 없는 것을 뽑지 않는다.");
            Assert.That(withoutRelics.Calls, Is.EqualTo(withPool.Calls - 2),
                "빈 풀은 확률 판정과 추첨 두 번을 통째로 건너뛰어야 한다.");
        }

        [Test]
        public void MoneyIsRolledFirstAndLandsInsideTheAuthoredRangeIncludingBothEnds()
        {
            // 🔴 굴리는 순서(돈 → 소모품 → 유물)가 계약이다 — 바뀌면 같은 시드가 다른 전리품을 낳는다.
            // 🔑 범위는 <b>저작에서 읽는다</b>. 숫자를 여기 박아 두면 밸런스를 바꿀 때마다 규칙 테스트가
            //    빨개져, 「범위 안에 든다」가 아니라 「그때 그 숫자였다」를 재게 된다(2026-09-01 상향에서 실제로 밟았다).
            var (min, max) = KillDropRates.Default.MoneyRangeFor(MonsterKillTier.Elite);
            for (var seed = 0; seed < 12; seed++)
            {
                var outcome = KillDropRoller.Roll(
                    MonsterKillTier.Elite, KillDropRates.Default, ItemPool, RelicPool, new ScriptedRandom(seed));
                if (!outcome.HasMoney)
                {
                    continue;
                }

                Assert.That(outcome.MoneyAmount, Is.InRange(min, max), $"정예 엽전이 저작 범위 밖이다(시드 {seed}).");
            }

            // 양 끝이 실제로 나온다(뽑기 금액과 같은 관례).
            var atMin = KillDropRoller.Roll(
                MonsterKillTier.Elite, KillDropRates.Default, ItemPool, RelicPool, new ScriptedRandom(0));
            Assert.That(atMin.MoneyAmount, Is.EqualTo(min));
        }

        [Test]
        public void AnUnauthoredMoneyRangeConsumesNoRandomnessEither()
        {
            // 금액 범위가 없으면 확률 판정도 건너뛴다 — 빈 풀과 같은 결정성 규칙이다.
            var rates = new KillDropRates(new[]
            {
                new KillDropRateEntry(MonsterKillTier.Normal, KillDropKind.Money, 100),
                new KillDropRateEntry(MonsterKillTier.Normal, KillDropKind.Item, 100)
            });

            var random = new ScriptedRandom(0);
            var outcome = KillDropRoller.Roll(MonsterKillTier.Normal, rates, ItemPool, Array.Empty<string>(), random);

            Assert.That(outcome.HasMoney, Is.False);
            Assert.That(random.Calls, Is.EqualTo(2), "소모품 확률 판정 + 추첨 두 번만 써야 한다.");
        }

        [Test]
        public void DropsAlwaysComeOutOfThePoolsThatWerePassedIn()
        {
            // 줄 수 없는 것을 뽑지 않는다는 계약 — 보유 유물 제외(D-9)는 풀을 만드는 쪽이 지킨다.
            for (var seed = 0; seed < 8; seed++)
            {
                var outcome = KillDropRoller.Roll(
                    MonsterKillTier.Boss, KillDropRates.Default, ItemPool, RelicPool, new ScriptedRandom(seed));
                Assert.That(ItemPool.Contains(outcome.ItemId), Is.True);
                Assert.That(RelicPool.Contains(outcome.RelicId), Is.True);
            }
        }

        private static KillDropOutcome RollNormalItem(int rollValue) =>
            KillDropRoller.Roll(
                MonsterKillTier.Normal,
                NoMoneyRates,
                ItemPool,
                Array.Empty<string>(),
                new ScriptedRandom(rollValue, 0));
    }
}
