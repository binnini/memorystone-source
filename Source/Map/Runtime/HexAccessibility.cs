namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// 「이 칸에 유닛이 접근할 수 있는가」의 단일 술어(2026-08-20 #4). 판정 = ①서 있을 수 있고
    /// (BaseWalkable + 비수역 — <see cref="HexTerrainTraits.IsWalkable"/>), ②어느 이웃에서든 걸어
    /// 들어올 수 있다(높이차 ≤ <see cref="HexTerrainTraits.MaxTraversableHeightDifference"/>인 이웃이
    /// 하나라도 존재).
    ///
    /// <para>🔴 배치 필터·오버레이 필터·보행 BFS가 <b>이 같은 함수</b>를 공유해야 한다 — 각자 다른
    /// 판정을 들고 있으면 반드시 다시 어긋난다(물 타일이 BaseWalkable=1로 저작돼 있어 BaseWalkable만
    /// 보는 필터를 전부 통과하던 것이 이 버그의 뿌리다). 전역 연결성(고립 섬)은 이 지역 술어가 아니라
    /// <see cref="HexWalkDistances"/> 기반 도달성 스윕이 잡는다.</para>
    /// </summary>
    public static class HexAccessibility
    {
        /// <summary>칸이 존재하고, 서 있을 수 있고, 이웃 어느 한 칸에서라도 걸어 들어올 수 있다.</summary>
        public static bool IsAccessible(HexMapData map, HexCoord coord, HexTerrainTraits traits)
        {
            return map != null &&
                   map.TryGetCell(coord, out var cell) &&
                   IsStandable(cell, traits) &&
                   HasTraversableNeighbor(map, cell, traits);
        }

        /// <summary>유닛이 이 칸 위에 서 있을 수 있다 = BaseWalkable이고 수역(통행 불가 지형)이 아니다.</summary>
        public static bool IsStandable(HexCellData cell, HexTerrainTraits traits)
        {
            return cell.BaseWalkable && (traits == null || traits.IsWalkable(cell.TerrainTypeId));
        }

        /// <summary>
        /// 사방 이웃 중 서 있을 수 있고 높이차가 통행 한계 이내인 칸이 하나라도 있는가.
        /// 전부 2단계 이상 차이 나면(또는 이웃이 전부 수역이면) 걸어 들어올 방법이 없다.
        /// </summary>
        public static bool HasTraversableNeighbor(HexMapData map, HexCellData cell, HexTerrainTraits traits)
        {
            var effectiveTraits = traits ?? HexTerrainTraits.Default;
            foreach (var direction in HexCoord.Directions)
            {
                if (map.TryGetCell(cell.Coord + direction, out var neighbor) &&
                    IsStandable(neighbor, effectiveTraits) &&
                    effectiveTraits.IsHeightTraversable(neighbor.HeightLevel, cell.HeightLevel))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
