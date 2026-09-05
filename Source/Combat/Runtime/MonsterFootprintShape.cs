using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몬스터 몸이 차지하는 칸의 <b>형상</b>(2026-09-03). 보스의 원판(<c>boss_phases.footprintRadius</c>)과 별개 축이다 —
    /// 원판은 중심 칸이 있고 방향이 없지만, 삼각형은 서로 인접한 세 칸이라 중심 칸이 없다(모델은 세 칸이
    /// 공유하는 꼭짓점 위에 선다). 저작은 <c>monster_catalog.csv</c>의 <c>footprint</c> 컬럼(빈 값 = 한 칸, <c>tri</c> = 삼각형).
    /// </summary>
    public enum MonsterFootprintShape
    {
        Single = 0,
        Triangle = 1,
    }

    public static class MonsterFootprints
    {
        public const string TriangleToken = "tri";

        private static readonly HexCoord[] single = { new HexCoord(0, 0) };

        /// <summary>
        /// 삼각형 오프셋(앵커 기준). 앵커·동쪽·남동쪽 — 세 칸이 서로 인접해 꼭짓점 하나를 공유한다.
        /// 방향은 <b>고정</b>이다(회전 없음): 회전을 주면 이동마다 몸이 돌아 점유가 예고와 어긋나고,
        /// 맵 오브젝트의 FootprintOffsets+회전 모델을 되살리게 된다(§13.4가 부결한 방향).
        /// </summary>
        private static readonly HexCoord[] triangle = { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1) };

        public static IReadOnlyList<HexCoord> SingleOffsets => single;
        public static IReadOnlyList<HexCoord> TriangleOffsets => triangle;

        public static IReadOnlyList<HexCoord> OffsetsOf(MonsterFootprintShape shape)
        {
            return shape == MonsterFootprintShape.Triangle ? triangle : single;
        }

        public static bool TryParse(string token, out MonsterFootprintShape shape, out string error)
        {
            error = string.Empty;
            var value = (token ?? string.Empty).Trim();
            if (value.Length == 0)
            {
                shape = MonsterFootprintShape.Single;
                return true;
            }

            if (string.Equals(value, TriangleToken, StringComparison.OrdinalIgnoreCase))
            {
                shape = MonsterFootprintShape.Triangle;
                return true;
            }

            shape = MonsterFootprintShape.Single;
            error = $"footprint '{value}' is not recognized (known: '' or '{TriangleToken}').";
            return false;
        }
    }
}
