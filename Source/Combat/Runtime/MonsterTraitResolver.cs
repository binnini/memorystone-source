using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>monster_traits.csv의 traitId 상수. 저작 표(CSV)와 코드가 이 한 벌로 만난다.</summary>
    public static class MonsterTraitIds
    {
        public const string Sturdy = "sturdy";
        public const string Toughness = "toughness";
        public const string Agitation = "agitation";
        public const string StrengthDistance = "strengthDistance";
        public const string Aftermath = "aftermath";
        public const string Stealth = "stealth";
        public const string AuraSeal = "auraSeal";
        public const string Pickpocket = "pickpocket";
        public const string Advance = "advance";
        /// <summary>수호 재충전(2026-09-05 결정 1 · guardRechargeTurns 컬럼).</summary>
        public const string GuardCycle = "guardCycle";
    }

    /// <summary>
    /// 「이 몬스터가 어떤 특성을 갖는가」의 <b>단일 유도 지점</b>. 저작은 여러 컬럼에 흩어져 있고
    /// (sturdyBlock · toughnessReloadTurns · onDeathEffectRef · hiddenTraitRef · advanceAfterAttack ·
    /// 패턴의 stealMoneyAmount) 갈래를 정하는 규칙도 제각각인데, 배지 조립부·툴팁·감사 게이트가
    /// 저마다 그 조건을 베껴 두면 반드시 한쪽만 고쳐진다 — 이 저장소가 반복해서 밟은 함정이다.
    ///
    /// <para>🔑 <b>소매치기는 두 저작의 짝</b>이다: 훔치는 것은 패턴(stealMoneyAmount)이고 돌려주는 것은
    /// 뒤끝(aftermath.restore/Money)이다. 훔치기만 있으면 뒤끝 배지가 「반환」을 약속하지 못하고,
    /// 반환만 있으면 훔친 적이 없다. 그래서 <b>둘 다 있을 때만</b> 소매치기로 읽는다.</para>
    /// </summary>
    public static class MonsterTraitResolver
    {
        public static IReadOnlyList<string> CollectTraitIds(MonsterCatalogEntry entry)
        {
            // MonsterCatalogEntry는 struct라 null이 없다 — default 값은 모든 술어가 false로 떨어진다.
            var ids = new List<string>(4);
            if (entry.HasSturdyBlock)
            {
                ids.Add(MonsterTraitIds.Sturdy);
            }

            if (entry.HasToughness)
            {
                ids.Add(MonsterTraitIds.Toughness);
            }

            if (entry.HasAgitation)
            {
                // 같은 스택 카운터를 두 문법이 쓴다 — 조건 ref가 어느 쪽인지 정한다(누적형 vs 재설정형).
                ids.Add(MonsterAgitationCondition.IsStrengthDistance(entry.AgitationConditionRef)
                    ? MonsterTraitIds.StrengthDistance
                    : MonsterTraitIds.Agitation);
            }

            if (entry.HasDeathAftermath)
            {
                ids.Add(StealsMoney(entry) && RestoresMoneyOnDeath(entry)
                    ? MonsterTraitIds.Pickpocket
                    : MonsterTraitIds.Aftermath);
            }

            if (MonsterAuraSeal.IsAuraSeal(entry.HiddenTraitRef))
            {
                ids.Add(MonsterTraitIds.AuraSeal);
            }
            else if (entry.HasHiddenTrait)
            {
                ids.Add(MonsterTraitIds.Stealth);
            }

            if (entry.HasAdvanceAfterAttack)
            {
                ids.Add(MonsterTraitIds.Advance);
            }
            if (entry.HasGuardRecharge)
            {
                ids.Add(MonsterTraitIds.GuardCycle);
            }

            return ids;
        }

        /// <summary>패턴 어느 하나라도 엽전을 훔치는가.</summary>
        public static bool StealsMoney(MonsterCatalogEntry entry)
        {
            var patterns = entry.AttackPatterns;
            if (patterns == null)
            {
                return false;
            }

            for (var i = 0; i < patterns.Length; i++)
            {
                if (patterns[i].StealMoneyAmount > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>뒤끝이 「훔친 엽전 반환」 갈래인가 — 집행과 같은 파서를 지난다.</summary>
        public static bool RestoresMoneyOnDeath(MonsterCatalogEntry entry)
        {
            return entry.HasDeathAftermath
                   && MonsterDeathAftermath.TryParse(
                       entry.OnDeathEffectRef, entry.OnDeathEffectParam, out var spec, out _)
                   && spec.Kind == MonsterDeathAftermathKind.Restore
                   && spec.RestoresMoney;
        }
    }
}
