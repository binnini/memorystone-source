using System;
using System.Collections.Generic;
using System.Globalization;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 전멸기 기믹(§20-B — §13.5의 개편). 주기마다 보스가 <b>아레나 중앙으로 점프</b>하고, 플레이어
    /// 주변에 안전지대 <b>후보 3곳</b>을 세우며, 예고 턴이 지나면 후보 중 진짜 2곳을 뺀 아레나 전체를
    /// 쓸어낸다.
    ///
    /// 핵심 계약(사용자 확정): <b>반드시 피할 수 있어야 한다.</b>
    /// <list type="bullet">
    /// <item>후보 3 · 진짜 2 · 가짜 1 → <b>정찰 1장 + 이동 1회면 회피 확률 100%</b>(§20-B-2).
    ///       진짜로 판명되면 거기로 가고, 가짜로 판명되면 남은 둘이 전부 진짜다.</item>
    /// <item>후보 3곳을 세울 수 없으면 발동을 통째로 포기한다 — <b>점프도 하지 않는다</b>.</item>
    /// <item>플레이어의 현재 칸은 후보에서 제외한다 — 제자리에 서 있으면 맞는다(이동 강제 퍼즐).</item>
    /// <item>폭발은 예고 시점에 저장된 칸 집합을 그대로 쓴다(예고=명중).</item>
    /// </list>
    ///
    /// 카운터·예고·후보 상태는 전부 <see cref="BossPhaseTrack"/>의 전용 필드에 있고 서스펜드
    /// 왕복된다 — 왕복하지 않으면 저장/재개로 진위를 다시 굴리는 세이브 스컴이 된다.
    /// </summary>
    internal sealed class AnnihilationMechanic : IBossMechanic
    {
        public string MechanicId => "annihilation";

        public IReadOnlyList<string> RequiredParamKeys { get; } = new[]
        {
            AnnihilationMechanicParams.PhaseMin,
            AnnihilationMechanicParams.IntervalTurns,
            AnnihilationMechanicParams.TelegraphTurns,
            AnnihilationMechanicParams.Damage,
            AnnihilationMechanicParams.CandidateCells,
            AnnihilationMechanicParams.RealSafeCells,
            AnnihilationMechanicParams.CandidateSpacing,
            AnnihilationMechanicParams.SafeReach,
            AnnihilationMechanicParams.FallbackRadius,
            AnnihilationMechanicParams.LandingBlastRadius,
            AnnihilationMechanicParams.LandingBlastDamage
        };

        public void ValidateParams(IReadOnlyDictionary<string, string> mechanicParams, int phaseCount)
        {
            var phaseMin = RequireInt(mechanicParams, AnnihilationMechanicParams.PhaseMin);
            if (phaseMin < 1 || phaseMin > phaseCount)
            {
                throw new ArgumentException(
                    $"mechanicParams '{AnnihilationMechanicParams.PhaseMin}' ({phaseMin}) must be within 1..{phaseCount} (phase count).");
            }

            var interval = RequireInt(mechanicParams, AnnihilationMechanicParams.IntervalTurns);
            var telegraph = RequireInt(mechanicParams, AnnihilationMechanicParams.TelegraphTurns);

            // 2 = 점프 턴 + 반응 턴. 1이면 점프한 그 턴 안에 폭발하므로 정찰도 이동도 낄 자리가 없다.
            if (telegraph < 2)
            {
                throw new ArgumentException(
                    $"mechanicParams '{AnnihilationMechanicParams.TelegraphTurns}' must be at least 2 — 점프 턴 + 반응 턴이 모두 있어야 회피가 성립한다.");
            }

            if (interval <= telegraph)
            {
                throw new ArgumentException(
                    $"mechanicParams '{AnnihilationMechanicParams.IntervalTurns}' ({interval}) must be greater than '{AnnihilationMechanicParams.TelegraphTurns}' ({telegraph}) — 겹치면 예고가 끝나기 전에 다음 예고가 시작된다.");
            }

            if (RequireInt(mechanicParams, AnnihilationMechanicParams.Damage) < 1)
            {
                throw new ArgumentException($"mechanicParams '{AnnihilationMechanicParams.Damage}' must be at least 1.");
            }

            var candidates = RequireInt(mechanicParams, AnnihilationMechanicParams.CandidateCells);
            var real = RequireInt(mechanicParams, AnnihilationMechanicParams.RealSafeCells);

            // 진짜 0 = 회피 불가능한 전멸기. 저작으로 이 계약을 깰 수 없어야 한다(사용자 요건).
            if (real < 1)
            {
                throw new ArgumentException(
                    $"mechanicParams '{AnnihilationMechanicParams.RealSafeCells}' must be at least 1 — 안전지대 없는 전멸기는 저작할 수 없다.");
            }

            if (candidates <= real)
            {
                throw new ArgumentException(
                    $"mechanicParams '{AnnihilationMechanicParams.CandidateCells}' ({candidates}) must be greater than '{AnnihilationMechanicParams.RealSafeCells}' ({real}) — 가짜가 하나도 없으면 정찰이 답할 질문이 없다.");
            }

            // 🔴 회피 100% 보장의 계약 가드(§20-B-2). 가짜가 <b>정확히 하나</b>일 때만
            // "가짜로 판명 → 남은 둘은 전부 진짜"가 성립한다. 가짜가 둘이면 정찰 한 장으로는
            // 확정할 수 없고, "정찰 1장이면 무조건 회피"라는 사용자 확정 계약이 CSV 한 줄로 조용히 깨진다.
            if (candidates - real > 1)
            {
                throw new ArgumentException(
                    $"'{AnnihilationMechanicParams.CandidateCells}' - '{AnnihilationMechanicParams.RealSafeCells}' must be at most 1 " +
                    $"(got {candidates - real}) — 가짜가 둘 이상이면 정찰 1장으로 회피가 확정되지 않는다.");
            }

            if (RequireInt(mechanicParams, AnnihilationMechanicParams.CandidateSpacing) < 1)
            {
                throw new ArgumentException($"mechanicParams '{AnnihilationMechanicParams.CandidateSpacing}' must be at least 1.");
            }

            if (RequireInt(mechanicParams, AnnihilationMechanicParams.SafeReach) < 1)
            {
                throw new ArgumentException($"mechanicParams '{AnnihilationMechanicParams.SafeReach}' must be at least 1.");
            }

            if (RequireInt(mechanicParams, AnnihilationMechanicParams.FallbackRadius) < 2)
            {
                throw new ArgumentException(
                    $"mechanicParams '{AnnihilationMechanicParams.FallbackRadius}' must be at least 2 — 반경 1이면 폭발 영역이 보스 footprint 안에 갇힌다.");
            }

            if (RequireInt(mechanicParams, AnnihilationMechanicParams.LandingBlastDamage) < 0)
            {
                throw new ArgumentException($"mechanicParams '{AnnihilationMechanicParams.LandingBlastDamage}' cannot be negative.");
            }

            // 💬 착지 원판과 철조각 살포 밴드가 서로소라는 불변식(ringRadius - 1 > footprintRadius ·
            // §20-B-3)은 여기서 검증할 수 없다: footprintRadius는 페이즈 행 저작이고 이 훅에는
            // 페이즈 <b>개수</b>만 들어온다. 대신 EditMode 테스트로 핀 박는다.
        }

        public void Resolve(BossMechanicContext context)
        {
            // 1) 예고가 진행 중이면 카운트다운 → 0에서 폭발. 폭발은 예고 시점에 저장된 칸을 그대로 쓴다.
            if (context.HasAnnihilationTelegraph)
            {
                // 폭발 턴까지 포함해 이 턴은 전멸기 시퀀스가 점유한다(철조각 살포 양보의 원천).
                // 래치를 폭발 <b>전에</b> 찍는 이유는, 폭발이 예고를 지우고 쿨다운을 되감기 때문이다 —
                // 결의 뒤에 물으면 "진행 중 아님"이 되어 같은 턴에 살포가 끼어든다.
                context.MarkAnnihilationOccupiedThisTurn();
                context.AnnihilationTelegraphTurnsRemaining -= 1;
                if (context.AnnihilationTelegraphTurnsRemaining > 0)
                {
                    return;
                }

                context.ResolveAnnihilationBlast(context.Profile.GetMechanicInt(AnnihilationMechanicParams.Damage));
                // 폭발한 페이즈 자신이 주기의 첫 턴이다(철조각 볼리와 같은 off-by-one 교훈 · §10-6).
                context.AnnihilationCooldownTurns =
                    context.Profile.GetMechanicInt(AnnihilationMechanicParams.IntervalTurns) - 1;
                return;
            }

            // 2) 활성 페이즈 이전이거나 쿨다운 중이면 대기.
            if (context.CurrentPhase < context.Profile.GetMechanicInt(AnnihilationMechanicParams.PhaseMin))
            {
                return;
            }

            if (context.AnnihilationCooldownTurns > 0)
            {
                context.AnnihilationCooldownTurns -= 1;
                return;
            }

            // 2.5) §21.7 양방향 배타: 판에 철조각이 있으면(턴 시작 래치 기준) 새 점프를 시작하지 않는다.
            //    쿨다운 감소(2) <b>뒤</b>에 있으므로 대기 중 쿨다운은 0에 머문다 — 대기가 공짜고,
            //    판이 비는 다음 턴에 곧바로 나간다. 굶지 않음은 철조각 저작 가드(interval > maturity)가
            //    보증한다(최대 대기 = maturity 턴). <see cref="WillJumpThisTurn"/>과 같은 래치를 읽으므로
            //    예고 커밋 시점의 질의와 여기 결의가 갈라질 수 없다.
            if (context.HadLivingPropsAtMonsterPhaseStart)
            {
                return;
            }

            // 3) 중앙 점프 + 후보 산출 + 예고 개시. 후보 3곳을 세울 수 없으면(0 반환) 발동을 포기하고
            //    다음 페이즈에 다시 시도한다 — 이때 점프도 일어나지 않는다.
            var telegraphedCells = context.BeginAnnihilation(BuildRequest(context));
            if (telegraphedCells <= 0)
            {
                return;
            }

            context.AnnihilationTelegraphTurnsRemaining =
                context.Profile.GetMechanicInt(AnnihilationMechanicParams.TelegraphTurns);
            context.MarkAnnihilationJumpedThisTurn();
            context.MarkAnnihilationOccupiedThisTurn();
        }

        /// <summary>
        /// 점프 턴에는 보스가 일반 공격을 하지 않는다(§20-B-3-1 — 철조각 살포 턴 선례와 동일).
        /// 예고 턴·폭발 턴에는 평소대로 공격한다.
        ///
        /// <para>🔴 쿨다운으로 "점프 턴"을 판정하면 <b>같은 턴 안에서 답이 뒤집힌다</b>: 공격 예고는
        /// 플레이어 이동 종료에 커밋되는데 기믹 결의는 그 뒤에 돌면서 쿨다운을 다음 주기로 되감는다
        /// (예고 시점 "점프함" → 공격 결의 시점 "점프 안 함" → 예고 없이 공격이 나간다).
        /// 앞의 항은 "이번 턴에 점프할 것이다", 뒤의 항은 "이번 턴에 점프했다"(래치)이며,
        /// 상태를 바꾸지 않는 순수 질의다.</para>
        /// </summary>
        public bool SuppressesMonsterAttackThisTurn(BossMechanicContext context)
        {
            return context.DidAnnihilationJumpThisTurn || WillJumpThisTurn(context);
        }

        /// <summary>
        /// 전멸기 시퀀스가 이번 몬스터 페이즈를 점유하는가 = <b>철조각 살포가 양보해야 하는가</b>(§20-B-9).
        /// "진행 중"의 정의는 점프 턴 ~ 폭발 턴(총 3턴)이다.
        ///
        /// <para>🔑 이 술어를 전멸기 로직 <b>옆에 한 번만</b> 두는 것이 요점이다. 정합성을 CSV
        /// <c>mechanicId</c>의 <c>|</c> 순서에 걸면 누가 순서를 되돌렸을 때 조용히 깨진다 —
        /// 세 항(래치 · 예고 진행 중 · 이번 턴 점프 예정) 중 하나는 결의 전에, 하나는 결의 후에 참이라
        /// 어느 순서로 돌아도 답이 같다.</para>
        /// </summary>
        internal static bool WillOccupyThisTurn(BossMechanicContext context)
        {
            if (!HasAnnihilationAuthored(context))
            {
                return false;
            }

            return context.DidAnnihilationOccupyThisTurn
                   || context.HasAnnihilationTelegraph
                   || WillJumpThisTurn(context);
        }

        /// <summary>
        /// 이번 몬스터 페이즈에 <b>점프가 새로 일어날 것인가</b>(결의 전 질의). 실제 발동과 같은 판정
        /// 경로(후보 산출 드라이런)를 쓴다 — 중복 판정은 곧 드리프트다.
        /// </summary>
        private static bool WillJumpThisTurn(BossMechanicContext context)
        {
            return HasAnnihilationAuthored(context)
                   && !context.HasAnnihilationTelegraph
                   && context.CurrentPhase >= context.Profile.GetMechanicInt(AnnihilationMechanicParams.PhaseMin)
                   && context.AnnihilationCooldownTurns <= 0
                   // §21.7 양방향 배타. 살아있는 값이 아니라 턴 시작 래치를 읽어야 흡수 턴(T+3)에
                   // mechanicId 문자열 순서와 무관하게 같은 답이 나온다(§20.3).
                   && !context.HadLivingPropsAtMonsterPhaseStart
                   && context.CanBeginAnnihilation(BuildRequest(context));
        }

        /// <summary>
        /// 이 보스가 전멸기를 <b>저작하고 있는가</b>. 없으면 <c>GetMechanicInt</c>가 전부 0을 돌려주어
        /// "1페이즈부터 쿨다운 0" = 항상 발동 예정으로 읽히고, 철조각이 영원히 양보하게 된다.
        /// </summary>
        private static bool HasAnnihilationAuthored(BossMechanicContext context)
        {
            foreach (var id in context.Profile.MechanicIds)
            {
                if (string.Equals(id, "annihilation", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static CombatState.BossAnnihilationRequest BuildRequest(BossMechanicContext context)
        {
            return new CombatState.BossAnnihilationRequest(
                context.Profile.GetMechanicInt(AnnihilationMechanicParams.CandidateCells),
                context.Profile.GetMechanicInt(AnnihilationMechanicParams.RealSafeCells),
                context.Profile.GetMechanicInt(AnnihilationMechanicParams.CandidateSpacing),
                context.Profile.GetMechanicInt(AnnihilationMechanicParams.SafeReach),
                context.Profile.GetMechanicInt(AnnihilationMechanicParams.FallbackRadius),
                context.Profile.GetMechanicInt(AnnihilationMechanicParams.LandingBlastRadius),
                context.Profile.GetMechanicInt(AnnihilationMechanicParams.LandingBlastDamage));
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
