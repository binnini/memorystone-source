using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 상태이상 <b>바닥 링</b>(버프·디버프)의 몸집 비례 계수(2026-08-20 WS-2, 실플레이 #9 후속).
    ///
    /// <para>왜 필요한가: 링 큐는 <c>scaleMultiplier 0.5</c>·<c>positionOffset (0,−1,0)</c>을 모든 유닛에
    /// 똑같이 쓰는데, 실제 몸집은 고깔 0.6 ~ 불가살 6.1(월드 폭)로 <b>10배</b> 벌어져 있다. 그래서 큰
    /// 몬스터에선 발치 연기 수준으로 왜소하고 작은 몬스터에선 몸보다 넓게 퍼졌다. 기절(HeadTop)이
    /// 렌더러 실측으로 해결한 것과 같은 원리를 바닥 링에도 적용한다.</para>
    ///
    /// <para>🔑 기준 체구는 <b>플레이어</b>다 — 계수가 「플레이어 대비 몇 배」이므로, 플레이어 화면은
    /// 정의상 계수 1이 되어 현행 튜닝이 그대로 잠긴다. 상수 기준폭을 코드에 박으면 모델이 바뀔 때
    /// 조용히 어긋나므로 기준도 실측한다.</para>
    /// </summary>
    public static class StatusLoopBodyScale
    {
        /// <summary>
        /// 계수 하한. 고깔(0.30배)·철조각(0.39배)까지 그대로 줄이면 링이 점으로 사라져 상태이상이
        /// 안 읽힌다 — 「작아 보이는」 것과 「안 보이는」 것은 다르다.
        /// </summary>
        public const float MinFactor = 0.6f;

        /// <summary>
        /// 계수 상한. 불가살 3페이즈(2.99배)처럼 자란 보스에서 링이 아레나를 덮지 않게 막는다.
        /// </summary>
        public const float MaxFactor = 2.5f;

        /// <summary>
        /// 몸집 계수 = 대상 발자국 지름 ÷ 기준(플레이어) 발자국 지름, <see cref="MinFactor"/>~
        /// <see cref="MaxFactor"/>로 클램프. 어느 한쪽이라도 못 재면 <b>1</b>(현행 크기)로 물러난다 —
        /// 못 잰 것이 화면에서 크기 폭주로 나타나면 안 된다.
        /// </summary>
        public static float Resolve(float footprintDiameter, float referenceFootprintDiameter)
        {
            if (!(footprintDiameter > 0f) || !(referenceFootprintDiameter > 0f))
            {
                return 1f;
            }

            return Mathf.Clamp(footprintDiameter / referenceFootprintDiameter, MinFactor, MaxFactor);
        }
    }
}
