using System;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 힘(스택) 카운터가 <b>어떻게 움직이는가</b>의 등록소. 스택 자체(저장·피해 보너스 +1/스택·배지·툴팁·세이브)는
    /// 하나뿐이고, 이 파일은 「무엇을 보고 움직이는가」만 갈아 끼운다.
    ///
    /// <para>🔑 이 분리가 없으면 특성 하나마다 스택 배관 한 벌이 복제된다. 2026-09-04 리워크로
    /// 누적형 조건 둘(겁먹음 agitation.dread·홀림 agitation.solitude)은 폐기됐다 — 겁먹음은
    /// <b>재설정형</b> 「담력 시험」(<see cref="StrengthDistanceRef"/>)으로, 홀림은 오라 봉인
    /// (<see cref="MonsterAuraSeal"/>·hiddenTraitRef 슬롯)으로 갈아탔다.</para>
    ///
    /// <para>🔴 미등록 값은 <b>임포트 시점에 거부</b>된다(뒤끝·은신 파서와 같은 규약) — 오타가
    /// "저작은 됐는데 아무 일도 안 일어나는" 죽은 컬럼으로 새지 않는다.</para>
    ///
    /// <para>⚠️ 담력 시험은 <b>격앙(누적+리셋) 문법이 아니다</b> — 매 턴 값을 통째로 다시 계산하는
    /// 재설정형이라 전용 틱(<c>TickDistanceStrength</c>)을 탄다. 기본 약오름 분기에 끼우면
    /// 어느 쪽 문법인지 못 읽는다(어둠 먹기가 약오름 배관을 빌려 쓰다 표기가 샌 선례).</para>
    /// </summary>
    internal static class MonsterAgitationCondition
    {
        /// <summary>
        /// 담력 시험(거구귀 · 2026-09-04) — 매 턴 힘 = clamp((플레이어와의 몸 거리 − 1) × 2, 0, 상한).
        /// <b>재설정형</b>(누적 아님): 멀어질수록 강해지고 붙으면(거리 1) 그 자리에서 0이다.
        /// 설화 번역 — 달아난 벗들은 당하고 곧장 걸어 들어간 신숙주는 얻었다.
        /// </summary>
        public const string StrengthDistanceRef = "strength.distance";

        /// <summary>담력 시험의 거리 1당 힘(= 피해 +2/거리). 상한은 저작(agitationMaxStacks)이 쥔다.</summary>
        public const int StrengthPerDistance = 2;

        /// <summary>빈 값(기본 조건)이거나 등록된 ref인가.</summary>
        public static bool IsRegistered(string conditionRef)
        {
            var normalized = (conditionRef ?? string.Empty).Trim();
            return normalized.Length == 0
                   || string.Equals(normalized, StrengthDistanceRef, StringComparison.Ordinal);
        }

        public static bool IsStrengthDistance(string conditionRef)
        {
            return string.Equals((conditionRef ?? string.Empty).Trim(), StrengthDistanceRef, StringComparison.Ordinal);
        }
    }
}
