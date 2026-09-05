using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 수호 기믹(DEC-2026-09-03-03): 페이즈에 <b>진입할 때마다</b> 보스에게 수호 충전
    /// (<see cref="StatusEffectKind.Guard"/> — 해로운 상태이상 1회 무효)을 저작량만큼 부여한다.
    /// 플레이어 수호(수호 부적·벽사 호리병)와 <b>같은 상태·같은 소비 관문</b>을 쓴다 —
    /// 상태이상 부여 관문의 유닛 일반화(2026-09-03)가 이 기믹의 전제다.
    ///
    /// 매 턴 재충전이 아니라 <b>페이즈당 1회</b>인 이유: 재충전이면 상태이상 축이 보스에게 사실상
    /// 봉인된다. "페이즈 초입의 첫 몇 장을 흘려보낸다"가 의도이고, 충전을 소모시킨 뒤의 상태이상은
    /// 그대로 통한다 — 취약을 없애는 것이 아니라 덜 취약하게 만드는 노브다.
    ///
    /// 부여는 행동이 아니다(살포·함정 배치와 달리 공격을 대체하지 않는다) — 몸이 굳는 방어막
    /// (trapVolleyGuardBlock)과 같은 부수 효과 문법이다.
    /// </summary>
    internal sealed class GuardMechanic : IBossMechanic
    {
        public string MechanicId => GuardMechanicParams.MechanicId;

        public IReadOnlyList<string> RequiredParamKeys { get; } = new[]
        {
            GuardMechanicParams.ChargesByPhase
        };

        public void ValidateParams(IReadOnlyDictionary<string, string> mechanicParams, int phaseCount)
        {
            mechanicParams.TryGetValue(GuardMechanicParams.ChargesByPhase, out var raw);
            var chargesByPhase = BossProfileDefinition.ParseIntList(raw, GuardMechanicParams.ChargesByPhase);
            if (chargesByPhase.Count != phaseCount)
            {
                throw new ArgumentException(
                    $"'{GuardMechanicParams.ChargesByPhase}' must have exactly one entry per phase " +
                    $"(expected {phaseCount}, got {chargesByPhase.Count}).");
            }

            for (var i = 0; i < chargesByPhase.Count; i++)
            {
                if (chargesByPhase[i] < 0)
                {
                    throw new ArgumentException(
                        $"'{GuardMechanicParams.ChargesByPhase}' phase {i + 1} charge count cannot be negative.");
                }
            }
        }

        public void Resolve(BossMechanicContext context)
        {
            // 부여의 정본은 페이즈 <b>진입</b> 지점(CombatState.ApplyBossPhaseEntry)이다 — 기믹 결의는
            // 페이즈 평가보다 먼저 돌아, 여기서만 부여하면 전환 턴의 몫이 한 턴 늦는다. 이 호출은
            // 조우 후 <b>첫 결의</b>의 캐치업이다: 조우 시점의 현재 페이즈는 "진입"을 지난 적이 없다.
            // 두 지점 모두 같은 래치(GuardChargesGrantedPhase)를 지나므로 이중 부여는 없다.
            context.GrantPendingGuardCharges();
        }

        /// <summary>부여는 행동이 아니다 — 이 기믹은 보스의 일반 공격을 대체하지 않는다.</summary>
        public bool SuppressesMonsterAttackThisTurn(BossMechanicContext context)
        {
            return false;
        }
    }
}
