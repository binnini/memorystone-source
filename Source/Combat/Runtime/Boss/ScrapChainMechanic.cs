using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 철조각 사슬 기믹(§21.8 제안 2). 철조각이 판에 있는 동안 보스가 살아있는 철조각 각각으로
    /// <b>스포크(직선) 전격</b>을 건다: 한 턴 예고 → 다음 몬스터 페이즈에 경로 칸 피해 + 속박.
    ///
    /// <list type="bullet">
    /// <item>🔑 <b>발동 창 = 철조각 생존 창(살포 T ~ 흡수 T+3)</b>. R1(§21.7 양방향 배타)이 이 창에서
    ///       전멸기를 구조적으로 배제하므로 두 기믹의 예고가 겹치지 않는다 — 별도 양보 술어가 필요 없다.</item>
    /// <item>🔴 개시 판정은 살아있는 목록이 아니라 <b>턴 시작 래치</b>(<see cref="BossMechanicContext.HadLivingPropsAtMonsterPhaseStart"/>)를
    ///       함께 읽는다. 살포 턴(T)에 iron-scrap보다 뒤에 돌면 "이번 턴에 깔린 기물"이 보이는데,
    ///       앞에 돌면 안 보인다 — 래치가 개시를 T+1로 고정해 <c>mechanicId</c> 순서 독립이 된다(§20.3).</item>
    /// <item><b>파괴 보상</b>: 예고 후 철조각이 죽으면 그 가닥만 무효(가닥이 기물 유닛 id를 문다).
    ///       예고 오버레이와 명중이 같은 생존 필터를 쓰므로 화면과 규칙이 함께 꺼진다.</item>
    /// <item>사슬은 <b>부가 압박</b>이다 — 살포·배치와 달리 그 턴의 행동이 아니므로 일반 공격을 막지 않는다.
    ///       막으면 철조각 창 3턴이 통째로 빈 턴이 된다.</item>
    /// </list>
    /// </summary>
    internal sealed class ScrapChainMechanic : IBossMechanic
    {
        public string MechanicId => "scrap-chain";

        public IReadOnlyList<string> RequiredParamKeys { get; } = new[]
        {
            ScrapChainMechanicParams.PropId,
            ScrapChainMechanicParams.PhaseMin,
            ScrapChainMechanicParams.IntervalTurns,
            ScrapChainMechanicParams.Damage,
            ScrapChainMechanicParams.RootTurns
        };

        public void ValidateParams(IReadOnlyDictionary<string, string> mechanicParams, int phaseCount)
        {
            if (!mechanicParams.TryGetValue(ScrapChainMechanicParams.PropId, out var propId)
                || string.IsNullOrWhiteSpace(propId))
            {
                throw new ArgumentException($"mechanicParams '{ScrapChainMechanicParams.PropId}' is required.");
            }

            var phaseMin = RequireInt(mechanicParams, ScrapChainMechanicParams.PhaseMin);
            if (phaseMin < 1 || phaseMin > phaseCount)
            {
                throw new ArgumentException(
                    $"mechanicParams '{ScrapChainMechanicParams.PhaseMin}' ({phaseMin}) must be within 1..{phaseCount} (phase count).");
            }

            if (RequireInt(mechanicParams, ScrapChainMechanicParams.IntervalTurns) < 1)
            {
                throw new ArgumentException($"'{ScrapChainMechanicParams.IntervalTurns}' must be a positive integer.");
            }

            if (RequireInt(mechanicParams, ScrapChainMechanicParams.Damage) < 1)
            {
                throw new ArgumentException(
                    $"'{ScrapChainMechanicParams.Damage}' must be at least 1 — 피해 없는 사슬은 예고(붉은 칸)가 거짓말이 된다.");
            }

            if (RequireInt(mechanicParams, ScrapChainMechanicParams.RootTurns) < 0)
            {
                throw new ArgumentException($"'{ScrapChainMechanicParams.RootTurns}' cannot be negative.");
            }
        }

        public void Resolve(BossMechanicContext context)
        {
            // 1) 예고가 걸려 있으면 이번 페이즈에 명중한다. 칸은 예고 시점 저장본 그대로(예고=명중),
            //    죽은 철조각의 가닥만 걸러진다(파괴 보상). 명중 후 쿨다운이 다음 주기를 센다.
            if (context.HasScrapChainTelegraph)
            {
                context.ResolveScrapChainHit(
                    context.Profile.GetMechanicInt(ScrapChainMechanicParams.Damage),
                    context.Profile.GetMechanicInt(ScrapChainMechanicParams.RootTurns));
                context.ScrapChainCooldownTurns =
                    context.Profile.GetMechanicInt(ScrapChainMechanicParams.IntervalTurns) - 1;
                return;
            }

            if (context.CurrentPhase < context.Profile.GetMechanicInt(ScrapChainMechanicParams.PhaseMin))
            {
                return;
            }

            if (context.ScrapChainCooldownTurns > 0)
            {
                context.ScrapChainCooldownTurns--;
                return;
            }

            // 2) 개시: 페이즈 시작에 철조각이 있던 턴(래치)에만 — 순서 독립(§20.3). 판이 비어 있으면
            //    쿨다운 0에 머물며 다음 살포를 기다린다(전멸기와 같은 공짜 대기).
            if (!context.HadLivingPropsAtMonsterPhaseStart)
            {
                return;
            }

            context.BeginScrapChainTelegraph(context.Profile.GetMechanicString(ScrapChainMechanicParams.PropId));
        }

        /// <summary>사슬은 부가 압박이다 — 어떤 턴에도 보스의 일반 공격을 막지 않는다.</summary>
        public bool SuppressesMonsterAttackThisTurn(BossMechanicContext context)
        {
            return false;
        }

        private static int RequireInt(IReadOnlyDictionary<string, string> mechanicParams, string key)
        {
            if (!mechanicParams.TryGetValue(key, out var raw)
                || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                throw new ArgumentException($"mechanicParams '{key}' must be an integer.");
            }

            return value;
        }
    }
}
