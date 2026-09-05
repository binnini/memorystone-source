using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 몸 형상 footprint(2026-09-03). 보스 원판(<see cref="GetMonsterFootprintRadius"/>)이 「반경」 한 수로 표현되는 것과
    /// 달리, 삼각형 정예(거구귀·두억시니)는 <b>앵커 기준 오프셋 목록</b>으로 표현한다. 점유·히트·거리·클리어런스는
    /// 이 목록 하나에서 유도되므로 원판과 형상이 같은 술어(<see cref="IsMonsterOccupying"/>)를 지난다.
    ///
    /// <para>🔑 반경 축은 건드리지 않는다: AI 거리 보정·공격 원점 이동·소환 거리 등 「반경」을 쓰는 아홉 군데는
    /// 삼각형에서 0 그대로다(앵커가 곧 규칙상 위치). 형상이 주는 것은 점유 칸·히트 범위·통과 클리어런스·넉백 면역뿐이다.</para>
    /// </summary>
    public sealed partial class CombatState
    {
        private static readonly Dictionary<int, HexCoord[]> diskOffsetsByRadius = new Dictionary<int, HexCoord[]>();

        /// <summary>앵커 기준 점유 오프셋(앵커 포함). 보스 원판 반경이 있으면 원판, 아니면 형상, 둘 다 없으면 한 칸.</summary>
        internal IReadOnlyList<HexCoord> GetMonsterFootprintOffsets(MonsterRuntime monster)
        {
            if (monster == null)
            {
                return MonsterFootprints.SingleOffsets;
            }

            var radius = GetMonsterFootprintRadius(monster);
            if (radius > 0)
            {
                return GetDiskOffsets(radius);
            }

            // 형상은 유도 술어를 지난다 — 보스는 페이즈 저작(P2 tri · 2026-09-04 §12)이 카탈로그 형상을 덮는다.
            return MonsterFootprints.OffsetsOf(GetMonsterFootprintShape(monster));
        }

        /// <summary>몸 기하 한 값(원판 반경 + 형상 오프셋). 해소·예고가 플래너와 같은 유도 함수를 쓰게 한다.</summary>
        internal MonsterBodyShape GetMonsterBody(MonsterRuntime monster)
        {
            return new MonsterBodyShape(GetMonsterFootprintRadius(monster), GetMonsterFootprintOffsets(monster));
        }

        /// <summary>몸이 두 칸 이상인가(원판이든 형상이든). 넉백 면역·점유 등록·오버레이가 이 술어로 갈린다.</summary>
        internal bool IsMultiCellMonster(MonsterRuntime monster)
        {
            return GetMonsterFootprintOffsets(monster).Count > 1;
        }

        /// <summary>
        /// 형상 footprint(원판 아님)를 가진 몬스터인가. 표현 계층이 모델을 앵커 칸이 아니라 <b>칸들의 무게중심</b>(삼각형이면
        /// 공유 꼭짓점)에 세울지 정하는 데 쓴다 — 원판은 앵커가 곧 무게중심이라 해당 없다.
        /// </summary>
        public IReadOnlyList<HexCoord> GetMonsterFootprintShapeOffsets(string monsterUnitId)
        {
            var monster = monsters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, monsterUnitId, StringComparison.Ordinal) && !candidate.Combatant.IsDead);
            if (monster == null || GetMonsterFootprintRadius(monster) > 0)
            {
                return MonsterFootprints.SingleOffsets;
            }

            return MonsterFootprints.OffsetsOf(GetMonsterFootprintShape(monster));
        }

        private static HexCoord[] GetDiskOffsets(int radius)
        {
            if (!diskOffsetsByRadius.TryGetValue(radius, out var offsets))
            {
                offsets = HexArea.CellsWithin(new HexCoord(0, 0), radius).ToArray();
                diskOffsetsByRadius[radius] = offsets;
            }

            return offsets;
        }

        /// <summary>
        /// 이 몬스터의 몸 <b>형상</b> — 카탈로그 형상(삼각형 정예)에 더해 보스는 <b>페이즈 저작</b>
        /// (boss_phases.footprintShape · 2026-09-04 §12)이 형상을 덮어쓸 수 있다(불가살 P2 tri).
        /// 점유·히트·거리·둘레(body-ring)·취약 부위·모델 앵커가 전부 이 술어에서 유도되므로
        /// <c>monster.FootprintShape</c>를 직접 읽는 유도 지점이 남으면 페이즈 형상이 그 축에서만 샌다.
        /// </summary>
        internal MonsterFootprintShape GetMonsterFootprintShape(MonsterRuntime monster)
        {
            if (monster == null)
            {
                return MonsterFootprintShape.Single;
            }

            var phaseShape = Boss.GetPhaseFootprintShapeOverride(monster);
            return phaseShape != MonsterFootprintShape.Single ? phaseShape : monster.FootprintShape;
        }
        // ── 점유 멤버십·거리·점유 칸 열거(3-A · 2026-09-04): 옛 CombatState.BossFootprint.cs에서 본문 무변경 이동.
        //    보스 원판과 형상 footprint를 같은 술어로 지나는 범용 규칙이라 「보스」 파일에 있을 이유가 없었다(코어 호출 20+곳).

        /// <summary>이 몬스터가 해당 칸을 점유하는가(footprint 원판 멤버십). 반경 0이면 중심 비교와 같다.</summary>
        internal bool IsMonsterOccupying(MonsterRuntime monster, HexCoord coord)
        {
            if (monster == null)
            {
                return false;
            }

            if (monster.Coord == coord)
            {
                return true;
            }

            var radius = GetMonsterFootprintRadius(monster);
            if (radius > 0)
            {
                return monster.Coord.DistanceTo(coord) <= radius;
            }

            // 형상 footprint(삼각형 정예·P2 tri 보스): 오프셋 목록 멤버십. 한 칸 몬스터는 위의 앵커 비교에서 이미 끝났다.
            var shape = GetMonsterFootprintShape(monster);
            if (shape != MonsterFootprintShape.Single)
            {
                var offsets = MonsterFootprints.OffsetsOf(shape);
                for (var i = 0; i < offsets.Count; i++)
                {
                    if (monster.Coord + offsets[i] == coord)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 이 필드 오브젝트(장판·폭탄류)가 몬스터의 <b>몸 어딘가</b>와 겹치는가.
        ///
        /// 중심 칸만 보면 7칸짜리 보스가 장판을 절반 깔고 서 있어도 중심이 밖이면 아무 일도 일어나지
        /// 않는다 — "가운데를 정확히 맞춰야만 폭탄이 통한다"로 체감된다. 히트테스트(<see cref="FindLivingMonsterAt"/>)와
        /// 사거리(<see cref="GetDistanceToMonster"/>)는 이미 원판 기준인데 장판만 중심 기준으로 남아 있었다.
        /// 반경 0 몬스터는 첫 줄에서 곧바로 끝나므로 기존 동작·비용 그대로다.
        /// </summary>
        internal bool DoesFieldObjectOverlapMonster(FieldObject fieldObject, MonsterRuntime monster)
        {
            if (monster == null)
            {
                return false;
            }

            if (fieldObject.Contains(monster.Coord))
            {
                return true;
            }

            if (!IsMultiCellMonster(monster))
            {
                return false;
            }

            foreach (var coord in EnumerateMonsterOccupiedCoords(monster))
            {
                if (fieldObject.Contains(coord))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 이 칸에서 몬스터의 <b>가장 가까운 점유 칸</b>까지의 거리. 사거리 판정은 중심이 아니라 이 값을
        /// 써야 사거리 1 카드로 보스 가장자리를 때릴 수 있다(계획 §13.4 C-3).
        /// </summary>
        internal int GetDistanceToMonster(MonsterRuntime monster, HexCoord coord)
        {
            var radius = GetMonsterFootprintRadius(monster);
            if (radius > 0 || GetMonsterFootprintShape(monster) == MonsterFootprintShape.Single)
            {
                return Math.Max(0, coord.DistanceTo(monster.Coord) - radius);
            }

            // 형상 footprint: 원판처럼 반경 뺄셈으로는 안 되고(중심 칸이 없다) 점유 칸 중 최솟값이다.
            var best = int.MaxValue;
            foreach (var occupied in EnumerateMonsterOccupiedCoords(monster))
            {
                best = Math.Min(best, coord.DistanceTo(occupied));
            }

            return best == int.MaxValue ? coord.DistanceTo(monster.Coord) : best;
        }

        /// <summary>몬스터의 점유 칸 전부(중심 포함). 반경 0이면 중심 한 칸이다.</summary>
        internal IEnumerable<HexCoord> EnumerateMonsterOccupiedCoords(MonsterRuntime monster)
        {
            var radius = GetMonsterFootprintRadius(monster);
            if (radius <= 0 && GetMonsterFootprintShape(monster) == MonsterFootprintShape.Single)
            {
                yield return monster.Coord;
                yield break;
            }

            // 원판이든 형상이든 앵커 기준 오프셋 목록 하나로 유도한다(원판은 반경별로 캐시된 오프셋).
            var offsets = GetMonsterFootprintOffsets(monster);
            for (var i = 0; i < offsets.Count; i++)
            {
                yield return monster.Coord + offsets[i];
            }
        }

        /// <summary>
        /// 몬스터 하나가 <b>어느 칸에 서 있는가</b>(중심 포함, 보스는 원판 전체). 자기부여 예고
        /// 오버레이(#10)가 "제자리 footprint"를 그리는 데 쓴다 — 점유 판정과 같은 함수를 공유해야
        /// 오버레이와 실제 몸이 갈라지지 않는다. 맵 밖 칸은 허공에 뜨므로 걸러 낸다.
        /// </summary>
        public IReadOnlyList<HexCoord> GetMonsterOccupiedCoords(string monsterUnitId)
        {
            var monster = monsters.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, monsterUnitId, StringComparison.Ordinal) && !candidate.Combatant.IsDead);
            if (monster == null)
            {
                return Array.Empty<HexCoord>();
            }

            return EnumerateMonsterOccupiedCoords(monster)
                .Where(coord => Map.TryGetCell(coord, out _))
                .ToList();
        }
    }
}
