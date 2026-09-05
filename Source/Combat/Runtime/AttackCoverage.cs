using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 플레이어 공격 한 번이 <b>보드에서 덮은 칸</b>. 칸 단위 판정(취약 부위)이 필요해지면서
    /// 생겼다 — 그전까지 <see cref="CombatState.ResolvePlayerAttackDamageTo"/>는 "어느 칸을 겨눴는지"를
    /// 몰랐고, 공격 경로가 네 갈래(단일·범위·형상·희생)라 각 경로가 좌표를 따로 들고 다니면
    /// 그중 하나에서 반드시 빠진다.
    ///
    /// 모드를 값으로 들고 다니는 이유: 범위 공격은 칸 목록을 <b>만들지 않고</b> 거리 비교로 판정하므로
    /// (핫패스에서 원판을 매번 물질화하지 않는다) 칸 컬렉션 하나로 통일할 수 없다.
    /// </summary>
    internal readonly struct AttackCoverage
    {
        private enum Mode
        {
            /// <summary>덮은 칸 없음(기본값). 취약 부위 판정이 항상 거짓이 되어 기존 동작 그대로다.</summary>
            None = 0,
            Disk,
            Cells
        }

        private readonly Mode mode;
        private readonly HexCoord center;
        private readonly int radius;
        private readonly IReadOnlyList<HexCoord> cells;

        private AttackCoverage(Mode mode, HexCoord center, int radius, IReadOnlyList<HexCoord> cells)
        {
            this.mode = mode;
            this.center = center;
            this.radius = radius;
            this.cells = cells;
        }

        /// <summary>좌표를 알 수 없는 경로(디버그·폴백)용. 취약타가 되지 않는다.</summary>
        public static AttackCoverage None => default;

        public static AttackCoverage AtCell(HexCoord coord) => new AttackCoverage(Mode.Disk, coord, 0, null);

        public static AttackCoverage Disk(HexCoord center, int radius) =>
            new AttackCoverage(Mode.Disk, center, Math.Max(0, radius), null);

        public static AttackCoverage Cells(IReadOnlyList<HexCoord> cells) =>
            cells == null || cells.Count == 0 ? None : new AttackCoverage(Mode.Cells, default, 0, cells);

        public bool Covers(HexCoord coord)
        {
            switch (mode)
            {
                case Mode.Disk:
                    return center.DistanceTo(coord) <= radius;
                case Mode.Cells:
                    for (var i = 0; i < cells.Count; i++)
                    {
                        if (cells[i] == coord)
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    return false;
            }
        }
    }
}
