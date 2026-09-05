using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 취약 부위 기믹(§20-A). 매 몬스터 페이즈에 판명 카운터를 1 줄이고, 0인 동안에는 자리를
    /// 다시 뽑는다. 피해 배율·정찰 판명·표현은 규칙 계층(<c>CombatState.BossWeakSpot</c>)이 갖고,
    /// 이 클래스는 <b>턴 진행</b>이라는 한 가지 일만 한다.
    ///
    /// <para>저작 노브가 없다: 판명 지속(2턴)도 배수(2배)도 사용자 확정 상수이므로
    /// <c>mechanicParams</c>에 추가할 키가 <b>하나도 없다</b>. 그래도 기믹으로 만드는 것이 맞은 이유는
    /// 매 턴 <see cref="Resolve"/>가 필요하고 상태가 서스펜드 왕복되어야 하기 때문이며,
    /// <see cref="IBossMechanic"/>이 정확히 그 모양이다.</para>
    ///
    /// <para>💬 나중에 판명 지속을 튜닝하고 싶어지면 그때 <c>weakSpotKnownTurns</c> 키를 추가한다.
    /// 지금 넣지 않는 이유는, 노브가 생기는 순간 <c>0</c>(= 정찰해도 그 턴에 안 먹힘) 같은 계약 파괴
    /// 저작이 가능해져 또 가드를 붙여야 하기 때문이다.</para>
    /// </summary>
    internal sealed class WeakSpotMechanic : IBossMechanic
    {
        public string MechanicId => "weak-spot";

        public IReadOnlyList<string> RequiredParamKeys { get; } = System.Array.Empty<string>();

        public void ValidateParams(IReadOnlyDictionary<string, string> mechanicParams, int phaseCount)
        {
            // 저작 키가 없으므로 검증할 것도 없다.
        }

        public void Resolve(BossMechanicContext context)
        {
            context.AdvanceWeakSpot();
        }

        /// <summary>취약 부위는 보스의 행동이 아니라 몸의 성질이다 — 공격을 대체하지 않는다.</summary>
        public bool SuppressesMonsterAttackThisTurn(BossMechanicContext context) => false;
    }
}
