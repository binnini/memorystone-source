using System.Collections.Generic;
namespace SeoulPlayup.Map.Runtime
{
    public readonly struct MovementQuery
    {
        public MovementQuery(
            HexCoord start,
            int movePoints,
            bool includeStart = false,
            string unitId = null,
            int footprintRadius = 0,
            IReadOnlyList<HexCoord> footprintOffsets = null)
        {
            Start = start;
            MovePoints = movePoints < 0 ? 0 : movePoints;
            IncludeStart = includeStart;
            UnitId = unitId ?? string.Empty;
            FootprintRadius = footprintRadius < 0 ? 0 : footprintRadius;
            FootprintOffsets = footprintOffsets;
        }

        public HexCoord Start { get; }
        public int MovePoints { get; }
        public bool IncludeStart { get; }
        public string UnitId { get; }

        /// <summary>
        /// 이 유닛의 물리 점유 반경(원판 클리어런스, 계획서 §21.3). 0 = 단일 칸이며 기존 호출부는
        /// 전부 여기에 해당한다 — 그래서 이 필드는 <b>순수 가산</b>이고 기본값 경로의 동작은 변하지 않는다.
        ///
        /// <para>0보다 크면 <see cref="HexPathfinder.CanEnter"/>가 "중심 칸 하나"가 아니라
        /// <b>중심 기준 반경 R 원판 전체</b>가 통과 가능한지를 묻는다. 멀티셀 보스가 자기가 지나갈 수
        /// 없는 틈으로 걸어 들어가는 사고를 막는 것이 목적이다(그 사고를 막으려고 §14.3이 걷기 자체를
        /// 껐고, 이 필드가 그 게이트를 대체한다).</para>
        ///
        /// <para>🔑 자기 몸이 이미 깔고 있는 칸은 자동으로 통과한다 — 점유 판정이
        /// <c>OccupyingUnitId != UnitId</c>일 때만 막기 때문이다. 별도 예외 처리가 필요 없다.</para>
        /// </summary>
        public int FootprintRadius { get; }

        /// <summary>
        /// 원판이 아닌 <b>형상 footprint</b>(2026-09-03 · 3칸 삼각형 정예)의 중심 기준 오프셋 목록. null 또는
        /// 한 칸이면 단일 칸이다. <see cref="FootprintRadius"/>와 같은 클리어런스 계약을 따르되 칸 집합만
        /// 다르다 — 원판은 반경 하나로 표현되고 여기 안 들어온다(둘 다 주면 둘 다 검사한다).
        /// </summary>
        public IReadOnlyList<HexCoord> FootprintOffsets { get; }
    }
}
