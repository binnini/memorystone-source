using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몬스터 몸의 기하(2026-09-03). 보스 원판(<see cref="Radius"/>)과 형상 footprint(<see cref="Offsets"/>)를 한 값으로 들고,
    /// 「몸에서 잰 거리」·「공격 형상의 원점」·「사거리 원판」을 여기서 유도한다 — 계획(플래너)·해소·예고 오버레이가
    /// 같은 함수를 쓰므로 예고=명중 계약이 원판과 형상 모두에서 성립한다.
    ///
    /// <para>원판: 거리 = 중심 거리 − 반경, 원점 = 조준 방향으로 반경만큼 간 가장자리 칸(P6 §13.4 C-3·C-6 그대로).
    /// 형상: 거리 = 몸통 칸 중 최솟값, 원점 = 조준 방향으로 가장 앞선 몸통 칸(같은 뜻을 원판이 아닌 칸 집합에 옮긴 것).</para>
    /// </summary>
    public readonly struct MonsterBodyShape
    {
        public MonsterBodyShape(int radius, IReadOnlyList<HexCoord> offsets)
        {
            Radius = Math.Max(0, radius);
            Offsets = offsets != null && offsets.Count > 1 && Radius == 0 ? offsets : null;
        }

        public static MonsterBodyShape Single => default;

        /// <summary>원판 반경(보스). 0이면 원판이 아니다.</summary>
        public int Radius { get; }

        /// <summary>형상 오프셋(앵커 포함). null이면 한 칸 또는 원판이다.</summary>
        public IReadOnlyList<HexCoord> Offsets { get; }

        public bool IsMultiCell => Radius > 0 || Offsets != null;

        /// <summary><paramref name="origin"/>에 선 몸에서 <paramref name="target"/>까지의 거리(가장 가까운 몸통 칸 기준).</summary>
        public int DistanceFrom(HexCoord origin, HexCoord target)
        {
            if (Offsets == null)
            {
                return Math.Max(0, origin.DistanceTo(target) - Radius);
            }

            var best = int.MaxValue;
            for (var i = 0; i < Offsets.Count; i++)
            {
                best = Math.Min(best, (origin + Offsets[i]).DistanceTo(target));
            }

            return best;
        }

        /// <summary>
        /// 조준 방향 기준 <b>가장 뒤에 있는</b> 몸통 칸(2026-09-05). 형상 footprint 전용 —
        /// 원판이나 한 칸 몸에는 「뒤」가 없으므로 <see langword="null"/>을 돌려준다.
        ///
        /// <para>🔑 <see cref="ResolveShapeOrigin"/>과 <b>같은 점수식</b>(큐브 내적)을 쓴다. 앞을 고르는 식과
        /// 뒤를 고르는 식이 갈라지면 「앞 2칸 · 뒤 1칸」이 어떤 방향에서는 둘 다 앞이 되거나 둘 다 뒤가 된다.
        /// 삼각형 몸(tri)은 여섯 방향 전부에서 최솟값이 유일해 항상 정확히 한 칸이 뒤가 된다.</para>
        ///
        /// <para>동점이면 오프셋 순서상 <b>뒤쪽</b>을 고른다(앞을 고르는 쪽이 앞선 것을 택하는 것과 대칭) —
        /// 결정적이어야 예고와 집행이 같은 칸을 뺀다.</para>
        /// </summary>
        public HexCoord? ResolveRearOffset(HexDirection attackDirection)
        {
            if (Offsets == null || Offsets.Count < 3)
            {
                return null;
            }

            var dir = HexCoord.Directions[(int)attackDirection];
            var worstScore = int.MaxValue;
            HexCoord worst = default;
            for (var i = 0; i < Offsets.Count; i++)
            {
                var offset = Offsets[i];
                var score = offset.Q * dir.Q + offset.R * dir.R + offset.S * dir.S;
                if (score <= worstScore)
                {
                    worstScore = score;
                    worst = offset;
                }
            }

            return worst;
        }

        /// <summary>공격 형상을 펼 원점. 원판은 조준 방향 가장자리 칸, 형상은 조준 방향으로 가장 앞선 몸통 칸.</summary>
        public HexCoord ResolveShapeOrigin(HexCoord origin, HexDirection attackDirection)
        {
            if (Offsets == null)
            {
                var edge = origin;
                for (var i = 0; i < Radius; i++)
                {
                    edge = edge.Neighbor(attackDirection);
                }

                return edge;
            }

            var dir = HexCoord.Directions[(int)attackDirection];
            var bestScore = int.MinValue;
            var best = origin;
            for (var i = 0; i < Offsets.Count; i++)
            {
                var offset = Offsets[i];
                // 큐브 좌표 내적: 조준 방향으로 얼마나 앞섰는가. 동점은 오프셋 순서(앵커 우선)로 결정적이다.
                var score = offset.Q * dir.Q + offset.R * dir.R + offset.S * dir.S;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = origin + offset;
                }
            }

            return best;
        }

        /// <summary><paramref name="coord"/>가 몸통 어느 칸에서 <paramref name="range"/> 안에 드는가(비-shape 사거리).</summary>
        public bool IsWithinRange(HexCoord origin, HexCoord coord, int range)
        {
            return DistanceFrom(origin, coord) <= Math.Max(0, range);
        }
    }
}
