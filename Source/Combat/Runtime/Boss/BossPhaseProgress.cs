using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 지표 → 정규화 progress 환산. 순수 함수로 떼어 둔 이유는 이것이 페이즈 시스템의 유일한 산술이고,
    /// 지표별 방향(누적 증가 / 체력 감소 / 턴 경과)을 하나의 오름차순 축으로 접는 곳이라
    /// 전투 상태 없이 지표별로 직접 검증할 수 있어야 하기 때문이다.
    /// </summary>
    public static class BossPhaseProgress
    {
        public static int Evaluate(BossPhaseMetricKind metric, int absorbedStacks, int hp, int maxHp, int overallTurnNumber)
        {
            switch (metric)
            {
                case BossPhaseMetricKind.AbsorbedStacks:
                    return Math.Max(0, absorbedStacks);
                case BossPhaseMetricKind.HpRatioBelow:
                    // 체력이 줄어드는 방향을 "잃은 비율"로 뒤집는다. maxHp<=0은 있을 수 없지만(생성자가 1로
                    // 클램프한다) 0 나눗셈을 만들지 않기 위해 완전 소진으로 본다.
                    var hpPercent = maxHp <= 0 ? 0 : Math.Max(0, hp) * 100 / maxHp;
                    return 100 - Math.Min(100, hpPercent);
                case BossPhaseMetricKind.TurnCount:
                    return Math.Max(0, overallTurnNumber - 1);
                default:
                    throw new ArgumentOutOfRangeException(nameof(metric), metric, "Unsupported boss phase metric.");
            }
        }
    }
}
