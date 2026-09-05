using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 처치 보상의 급. <b>급이 곧 규칙이다</b>(DEC-2026-08-31-03) — 유물이 정예·보스에서만
    /// 떨어지기 때문에 「정예를 찾아 싸운다」는 이유가 생긴다.
    /// <para>
    /// 급은 카탈로그 컬럼이 아니라 <b>배치 시점의 승격</b>이고, 런타임에는 몬스터의
    /// <c>SpawnRole</c>이 그 표시를 들고 있다(<see cref="MonsterSpawnRoles"/>).
    /// </para>
    /// </summary>
    public enum MonsterKillTier
    {
        Normal,
        Elite,
        Boss
    }

    /// <summary>
    /// 처치 전리품으로 떨어질 수 있는 것. <b>부적(카드)은 여기 없다</b> — 부적은 확률이 아니라
    /// 전리품 목록에 언제나 서는 한 줄이고, 고르는 것이지 떨어지는 것이 아니다.
    /// </summary>
    public enum KillDropKind
    {
        /// <summary>소모품(가방 아이템). 가방 만원이면 지급 지점이 돈으로 폴백한다(CR-10).</summary>
        Item,

        /// <summary>유물. 보유분은 추첨 전에 풀에서 빠진다(D-9).</summary>
        Relic,

        /// <summary>엽전. 금액은 <see cref="KillDropRateEntry.MinAmount"/>~<see cref="KillDropRateEntry.MaxAmount"/>에서 뽑는다(양 끝 포함).</summary>
        Money
    }

    /// <summary>저작된 한 행: 급 × 종류 → 확률(퍼센트), 그리고 돈이면 금액 범위.</summary>
    public readonly struct KillDropRateEntry
    {
        public KillDropRateEntry(MonsterKillTier tier, KillDropKind kind, int percent, int minAmount = 0, int maxAmount = 0)
        {
            Tier = tier;
            Kind = kind;
            Percent = Math.Min(100, Math.Max(0, percent));
            MinAmount = Math.Max(0, minAmount);
            MaxAmount = Math.Max(MinAmount, maxAmount);
        }

        public MonsterKillTier Tier { get; }
        public KillDropKind Kind { get; }

        /// <summary>0 = 이 급에서는 떨어지지 않는다. 100 = 확정.</summary>
        public int Percent { get; }

        /// <summary>금액 하한. <see cref="KillDropKind.Money"/>일 때만 의미가 있다(양 끝 포함).</summary>
        public int MinAmount { get; }

        /// <summary>금액 상한(양 끝 포함).</summary>
        public int MaxAmount { get; }
    }

    /// <summary>
    /// 몬스터 처치 시 <b>전리품 목록</b>에 오를 것의 확률표(DEC-2026-08-31-03 — D-2 재개정).
    ///
    /// <para>
    /// 🔑 <b>부적은 이 표에 없다.</b> 부적은 떨어지는 것이 아니라 목록에 언제나 서는 줄이고,
    /// 확률로 다루는 것은 그 위에 <b>더</b> 오르는 것(엽전·소모품·유물)뿐이다.
    /// </para>
    ///
    /// <para>
    /// 🔴 확률은 <b>저작</b>이다(<c>kill_drop_rates.csv</c>). 코드 상수로 박으면 밸런스 손잡이가
    /// 코드 리뷰를 거쳐야 돌아간다 — <see cref="Default"/>는 CSV를 못 읽는 컨텍스트의 폴백일 뿐이고
    /// 출하 값과 일치해야 한다(테스트가 감시).
    /// </para>
    /// </summary>
    public sealed class KillDropRates
    {
        private readonly KillDropRateEntry[] entries;

        public KillDropRates(IReadOnlyList<KillDropRateEntry> rateEntries)
        {
            if (rateEntries == null)
            {
                throw new ArgumentNullException(nameof(rateEntries));
            }

            var copy = new KillDropRateEntry[rateEntries.Count];
            var seen = new HashSet<(MonsterKillTier, KillDropKind)>();
            for (var i = 0; i < rateEntries.Count; i++)
            {
                if (!seen.Add((rateEntries[i].Tier, rateEntries[i].Kind)))
                {
                    throw new ArgumentException(
                        $"Duplicate kill drop rate for '{rateEntries[i].Tier}/{rateEntries[i].Kind}'.", nameof(rateEntries));
                }

                copy[i] = rateEntries[i];
            }

            entries = copy;
        }

        /// <summary>코드 폴백. <c>kill_drop_rates.csv</c>의 출하 값과 일치해야 한다(테스트가 감시).</summary>
        public static KillDropRates Default { get; } = new KillDropRates(
            new[]
            {
                new KillDropRateEntry(MonsterKillTier.Normal, KillDropKind.Money, 100, 30, 50),
                new KillDropRateEntry(MonsterKillTier.Normal, KillDropKind.Item, 8),
                new KillDropRateEntry(MonsterKillTier.Normal, KillDropKind.Relic, 0),
                new KillDropRateEntry(MonsterKillTier.Elite, KillDropKind.Money, 100, 60, 90),
                new KillDropRateEntry(MonsterKillTier.Elite, KillDropKind.Item, 60),
                new KillDropRateEntry(MonsterKillTier.Elite, KillDropKind.Relic, 25),
                new KillDropRateEntry(MonsterKillTier.Boss, KillDropKind.Money, 100, 150, 250),
                new KillDropRateEntry(MonsterKillTier.Boss, KillDropKind.Item, 100),
                new KillDropRateEntry(MonsterKillTier.Boss, KillDropKind.Relic, 100)
            });

        public IReadOnlyList<KillDropRateEntry> Entries => entries;

        /// <summary>
        /// 이 급에서 이 종류가 떨어질 확률(퍼센트). 저작에 행이 없으면 <b>0</b>이다 —
        /// 안 적힌 것은 안 떨어진다는 뜻이고, 그것이 저작자가 읽는 대로다.
        /// </summary>
        public int PercentFor(MonsterKillTier tier, KillDropKind kind)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Tier == tier && entries[i].Kind == kind)
                {
                    return entries[i].Percent;
                }
            }

            return 0;
        }

        /// <summary>이 급의 돈 금액 범위(양 끝 포함). 저작이 없으면 (0,0)이다.</summary>
        public (int Min, int Max) MoneyRangeFor(MonsterKillTier tier)
        {
            for (var i = 0; i < entries.Length; i++)
            {
                if (entries[i].Tier == tier && entries[i].Kind == KillDropKind.Money)
                {
                    return (entries[i].MinAmount, entries[i].MaxAmount);
                }
            }

            return (0, 0);
        }

        /// <summary>
        /// 런타임 몬스터의 <c>SpawnRole</c>에서 급을 읽는다. 급은 배치 시점의 승격이고 그 표시가
        /// 여기 남아 있다 — 카탈로그에는 이 축이 없다.
        /// </summary>
        public static MonsterKillTier TierFromSpawnRole(string spawnRole)
        {
            if (MonsterSpawnRoles.IsBoss(spawnRole))
            {
                return MonsterKillTier.Boss;
            }

            return MonsterSpawnRoles.IsElite(spawnRole) ? MonsterKillTier.Elite : MonsterKillTier.Normal;
        }
    }
}
