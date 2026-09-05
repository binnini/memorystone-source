using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 한 번의 처치가 <b>전리품 목록에 올리는</b> 것. 부적은 여기 없다 — 목록에 언제나 서는
    /// 줄이라 추첨할 것이 없다.
    /// </summary>
    public readonly struct KillDropOutcome
    {
        public KillDropOutcome(string itemId, string relicId, int moneyAmount = 0)
        {
            ItemId = itemId ?? string.Empty;
            RelicId = relicId ?? string.Empty;
            MoneyAmount = Math.Max(0, moneyAmount);
        }

        /// <summary>얹힌 소모품의 id. 비면 안 떨어졌다.</summary>
        public string ItemId { get; }

        /// <summary>얹힌 유물의 id. 비면 안 떨어졌다(일반 몬스터는 언제나 비어 있다).</summary>
        public string RelicId { get; }

        /// <summary>떨어진 엽전. 0이면 안 떨어졌다.</summary>
        public int MoneyAmount { get; }

        public bool HasItem => !string.IsNullOrEmpty(ItemId);
        public bool HasRelic => !string.IsNullOrEmpty(RelicId);
        public bool HasMoney => MoneyAmount > 0;
        public bool IsEmpty => !HasItem && !HasRelic && !HasMoney;
    }

    /// <summary>
    /// 몬스터 처치 전리품 추첨기(DEC-2026-08-31-03). 순수 C# —
    /// Unity도 표현 타입도 없다(<see cref="GachaRewardRoller"/>·<see cref="ShopInventoryRoller"/> 선례).
    ///
    /// <para>
    /// 🔑 <b>새 규칙을 만들지 않는다.</b> 유물 후보는 <see cref="GachaRewardRoller.BuildRelicPool"/>이
    /// 이미 걸러 주고(D-9 — 보유분 제외), 가방 만원의 돈 폴백(CR-10)은 지급 지점의 일이라 여기서
    /// 보지 않는다. 이 추첨기가 지키는 계약은 하나다: <b>줄 수 없는 것을 뽑지 않는다</b> —
    /// 풀이 비면 그 종류는 아예 후보에서 빠진다.
    /// </para>
    ///
    /// <para>
    /// 엽전·소모품·유물은 <b>독립</b>으로 굴린다. 보스는 셋 다 확정이고, 정예는 조합이 전부 가능하다.
    /// 🔴 굴리는 <b>순서(돈 → 소모품 → 유물)가 계약</b>이다 — 바꾸면 같은 시드가 다른 전리품을 낳는다.
    /// </para>
    /// </summary>
    public static class KillDropRoller
    {
        /// <summary>
        /// 한 번의 처치에 얹을 것을 뽑는다.
        /// </summary>
        /// <param name="tier">급. <see cref="KillDropRates.TierFromSpawnRole"/>이 <c>SpawnRole</c>에서 읽는다.</param>
        /// <param name="rates">저작된 확률표(<c>kill_drop_rates.csv</c>).</param>
        /// <param name="itemPool">소모품 후보. 균등 추첨(N1 — 등급과 무관).</param>
        /// <param name="relicPool">유물 후보. <b>보유분을 뺀</b> 것을 넘긴다(D-9).</param>
        public static KillDropOutcome Roll(
            MonsterKillTier tier,
            KillDropRates rates,
            IReadOnlyList<string> itemPool,
            IReadOnlyList<string> relicPool,
            IRewardRandom random)
        {
            if (rates == null)
            {
                throw new ArgumentNullException(nameof(rates));
            }

            if (random == null)
            {
                throw new ArgumentNullException(nameof(random));
            }

            // 🔴 굴리는 <b>순서가 계약</b>이다 — 돈 → 소모품 → 유물. 순서를 바꾸면 같은 시드가
            //    다른 전리품을 낳아 세이브 재현이 깨진다.
            var money = TryPickMoney(rates, tier, random);
            var itemId = TryPick(rates.PercentFor(tier, KillDropKind.Item), itemPool, random);
            var relicId = TryPick(rates.PercentFor(tier, KillDropKind.Relic), relicPool, random);
            return new KillDropOutcome(itemId, relicId, money);
        }

        /// <summary>
        /// 돈은 풀이 없다 — 확률만 굴리고, 맞으면 저작된 범위에서 금액을 뽑는다(<b>양 끝 포함</b>,
        /// 뽑기 금액과 같은 관례). 범위가 저작되지 않았으면 확률 판정도 건너뛴다.
        /// </summary>
        private static int TryPickMoney(KillDropRates rates, MonsterKillTier tier, IRewardRandom random)
        {
            var percent = rates.PercentFor(tier, KillDropKind.Money);
            var (min, max) = rates.MoneyRangeFor(tier);
            if (percent <= 0 || max <= 0)
            {
                return 0;
            }

            if (random.Next(100) >= percent)
            {
                return 0;
            }

            return min >= max ? max : min + random.Next(max - min + 1);
        }

        /// <summary>
        /// 확률을 굴리고, 맞으면 풀에서 하나를 균등하게 뽑는다.
        /// <para>
        /// 🔴 확률 판정을 <b>풀이 비었을 때도</b> 건너뛴다 — 안 그러면 유물 풀이 마른 판과 안 마른
        /// 판에서 난수 소비 횟수가 달라져 같은 시드가 다른 전개를 낳는다(결정성이 세이브 정합을
        /// 지키는 축이다).
        /// </para>
        /// </summary>
        private static string TryPick(int percent, IReadOnlyList<string> pool, IRewardRandom random)
        {
            if (percent <= 0 || pool == null || pool.Count == 0)
            {
                return string.Empty;
            }

            if (random.Next(100) >= percent)
            {
                return string.Empty;
            }

            return pool[random.Next(pool.Count)] ?? string.Empty;
        }
    }
}
