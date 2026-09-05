using System;
using System.Collections.Generic;

namespace SeoulPlayup.Map.Runtime
{
    public sealed class HexVisibilityRuntime
    {
        private readonly Dictionary<HexCoord, HexCellVisibility> states = new Dictionary<HexCoord, HexCellVisibility>();
        private readonly HashSet<HexCoord> temporaryRevealed = new HashSet<HexCoord>();

        /// <summary>

        /// 영구 공개 칸(보스 결계 안 아레나 · 2026-09-05). 시야에 들어온 칸은 매 갱신마다 임시 공개 목록에 다시

        /// 실리므로, 이 집합이 없으면 <see cref="RevealPermanently"/>가 한 번 떼어낸 칸도 다음에 시야를 스치는 순간

        /// 임시가 되어 시야를 벗어날 때 Hinted로 강등된다(실플레이: 「아레나 안이 어둡다」). 세이브 왕복 대상.

        /// </summary>

        private readonly HashSet<HexCoord> permanentlyRevealed = new HashSet<HexCoord>();

        // Coords whose trap has been discovered by a Scout card. Intentionally separate from the
        // visibility <c>states</c> map: trap discovery is orthogonal to fog (it persists even after
        // the cell fades back to Hinted) and is only ever populated by ScoutReveal paths, never by
        // player vision. Add-only, so once a trap is found it stays found.
        private readonly HashSet<HexCoord> trapRevealed = new HashSet<HexCoord>();

        public HexVisibilityRuntime(HexMapData map, HexCoord start, int hintedRadius)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            RevealTemporaryArea(start, hintedRadius);
        }

        public HexMapData Map { get; }
        public IReadOnlyDictionary<HexCoord, HexCellVisibility> States => states;

        // Monotonic counter bumped only when the visibility <c>states</c> map actually changes.
        // Lets renderers cheaply detect "nothing changed since the last pass" and skip a full-map
        // rescan. Both writers (SetVisibility/ForceVisibility) funnel through here, so it is exact.
        public int Version { get; private set; }

        public HexCellVisibility GetVisibility(HexCoord coord)
        {
            return states.TryGetValue(coord, out var visibility) ? visibility : HexCellVisibility.Unknown;
        }

        // Wipes all visibility (and its temporary/trap reveal sidecars) back to fully-unknown. SetVisibility
        // is monotonic (it never lowers a cell below its current level), so a clean restore of persisted fog
        // must reset first — otherwise the constructor's start-spawn reveals leak in as additive fog. Bumps
        // Version so renderers re-scan when the map was non-empty.
        public void ResetAll()
        {
            permanentlyRevealed.Clear();
            var hadState = states.Count > 0 || temporaryRevealed.Count > 0 || trapRevealed.Count > 0;
            states.Clear();
            temporaryRevealed.Clear();
            trapRevealed.Clear();
            if (hadState)
            {
                Version++;
            }
        }

        public bool IsKnown(HexCoord coord)
        {
            return GetVisibility(coord) != HexCellVisibility.Unknown;
        }

        public bool IsTrapRevealed(HexCoord coord)
        {
            return trapRevealed.Contains(coord);
        }

        // Marks every authored trap whose coord falls inside the scouted area as discovered. Called
        // only from Scout card resolution (never from player vision), so traps stay invisible under
        // normal sight. Bumps Version so renderers re-scan when a new trap becomes visible.
        public void RevealTrapsInArea(HexCoord center, int radius)
        {
            if (radius < 0 || !Map.Contains(center))
            {
                return;
            }

            foreach (var trap in Map.TrapRefs)
            {
                if (center.DistanceTo(trap.Coord) <= radius && trapRevealed.Add(trap.Coord))
                {
                    Version++;
                }
            }
        }

        /// <summary>
        /// 좌표 하나의 함정 발견을 직접 기록한다(§21.5 보스 배치 함정 — <c>Map.TrapRefs</c>에 없는
        /// 런타임 함정은 <see cref="RevealTrapsInArea"/>의 맵 순회에 걸리지 않으므로 이 직접 경로가
        /// 필요하다). 정찰의 런타임 함정 짝(<c>CombatState.RevealRuntimeTrapsInArea</c>)과 자동
        /// 발각(골목 CCTV)이 쓴다 — 배치 시점 즉시 공개는 2026-09-03 함정 숨김 전환으로 폐지됐다.
        /// </summary>
        public void RevealTrapAt(HexCoord coord)
        {
            if (trapRevealed.Add(coord))
            {
                Version++;
            }
        }

        /// <summary>
        /// 발견된 함정 좌표 전체. 세이브 왕복용(GAP-2) — 이 집합은 정찰로만 쌓이므로 판에서 재유도할 수
        /// 없다. 저장하지 않으면 재개 후 정찰로 찾아 둔 함정이 도로 사라진다.
        /// </summary>
        public IReadOnlyCollection<HexCoord> RevealedTrapCoords => trapRevealed;

        /// <summary>영구 공개 칸 전체(세이브 왕복용). 판에서 재유도할 수 없다 — 저장하지 않으면 재개 뒤 아레나가 다시 어두워진다.</summary>
        public IReadOnlyCollection<HexCoord> PermanentlyRevealedCoords => permanentlyRevealed;

        /// <summary>세이브에서 영구 공개 기록을 복원한다(가시성 상태 자체는 별도 목록이 되살린다).</summary>
        public void RestorePermanentReveals(IEnumerable<HexCoord> coords)
        {
            permanentlyRevealed.Clear();
            if (coords == null)
            {
                return;
            }

            foreach (var coord in coords)
            {
                if (Map.Contains(coord))
                {
                    permanentlyRevealed.Add(coord);
                    temporaryRevealed.Remove(coord);
                }
            }
        }

        /// <summary>
        /// 세이브에서 발견 기록을 복원한다. <see cref="RevealTrapsInArea"/>와 달리 반경 탐색이 아니라
        /// 좌표를 그대로 되살린다 — 저장 시점에 어떤 반경으로 발견했는지는 남아 있지 않기 때문이다.
        /// </summary>
        public void RestoreTrapReveals(IEnumerable<HexCoord> coords)
        {
            trapRevealed.Clear();
            if (coords != null)
            {
                foreach (var coord in coords)
                {
                    trapRevealed.Add(coord);
                }
            }

            Version++;
        }

        // Undoes a single trap reveal (e.g. a trap consumed/disarmed), bumping Version so renderers re-scan.
        public void ClearTrapReveal(HexCoord coord)
        {
            if (trapRevealed.Remove(coord))
            {
                Version++;
            }
        }

        public void SetVisibility(HexCoord coord, HexCellVisibility visibility)
        {
            if (!Map.Contains(coord))
            {
                return;
            }

            if (visibility == HexCellVisibility.Unknown)
            {
                var removed = states.Remove(coord);
                temporaryRevealed.Remove(coord);
                if (removed)
                {
                    Version++;
                }

                return;
            }

            if (states.TryGetValue(coord, out var current) && current >= visibility)
            {
                return;
            }

            states[coord] = visibility;
            Version++;
        }

        public void Reveal(HexCoord coord)
        {
            SetVisibility(coord, HexCellVisibility.Revealed);
        }

        /// <summary>
        /// 되돌릴 수 없는 공개. <see cref="Reveal"/>과 달리 임시 공개 사이드카에서도 이 셀을 떼어 낸다.
        ///
        /// 왜 필요한가: <see cref="Reveal"/>은 단조 승격이지만, 그 셀이 이미 임시 공개 목록에 들어 있으면
        /// (플레이어 시야에 들어와 있던 칸) 다음 <see cref="RefreshTemporaryRevealedCells"/>에서
        /// <c>ForceVisibility</c>로 Hinted까지 <b>강등</b>된다 — 영구 공개가 조용히 취소된다.
        /// "한 번 밝히면 영원히 밝다"를 보장해야 하는 소비자(보스 결계 안 아레나)는 이쪽을 쓴다.
        /// </summary>
        public void RevealPermanently(HexCoord coord)
        {
            if (!Map.Contains(coord))
            {
                return;
            }

            SetVisibility(coord, HexCellVisibility.Revealed);
            temporaryRevealed.Remove(coord);
            permanentlyRevealed.Add(coord);
        }

        public void RevealTemporaryArea(HexCoord center, int radius)
        {
            if (radius < 0 || !Map.Contains(center))
            {
                return;
            }

            foreach (var cell in Map.AllCells)
            {
                if (center.DistanceTo(cell.Coord) <= radius)
                {
                    ForceVisibility(cell.Coord, HexCellVisibility.Revealed);
                    temporaryRevealed.Add(cell.Coord);
                }
            }
        }

        public void RefreshTemporaryRevealArea(HexCoord center, int radius)
        {
            if (radius < 0 || !Map.Contains(center))
            {
                ExpireTemporaryReveals();
                return;
            }

            var nextRevealed = new HashSet<HexCoord>();
            foreach (var cell in Map.AllCells)
            {
                if (center.DistanceTo(cell.Coord) <= radius)
                {
                    nextRevealed.Add(cell.Coord);
                }
            }

            RefreshTemporaryRevealedCells(nextRevealed);
        }

        // Sets the temporary-revealed area to exactly <paramref name="revealedCells"/>: every listed
        // cell becomes Revealed, and any previously temporary-revealed cell not in the set decays back
        // to Hinted. Callers (e.g. CombatState player-vision refresh) union all live reveal sources —
        // player sight plus active field objects — so a source whose cells drop out of the set on the
        // next refresh (a field object that expired, the player walking away) fades to Hinted.
        public void RefreshTemporaryRevealedCells(IEnumerable<HexCoord> revealedCells)
        {
            var nextRevealed = new HashSet<HexCoord>();
            if (revealedCells != null)
            {
                foreach (var coord in revealedCells)
                {
                    if (Map.Contains(coord))
                    {
                        nextRevealed.Add(coord);
                    }
                }
            }

            ExpireTemporaryRevealsExcept(nextRevealed);
            foreach (var coord in nextRevealed)
            {
                ForceVisibility(coord, HexCellVisibility.Revealed);
                // 영구 공개 칸은 임시 목록에 싣지 않는다 — 실리면 시야를 벗어날 때 강등된다.
                if (!permanentlyRevealed.Contains(coord))
                {
                    temporaryRevealed.Add(coord);
                }
            }
        }

        public void ExpireTemporaryReveals()
        {
            ExpireTemporaryRevealsExcept(null);
        }

        public void HintNeighborhood(HexCoord center, int radius)
        {
            if (radius < 0 || !Map.Contains(center))
            {
                return;
            }

            foreach (var cell in Map.AllCells)
            {
                if (center.DistanceTo(cell.Coord) <= radius)
                {
                    SetVisibility(cell.Coord, cell.Coord == center ? HexCellVisibility.Revealed : HexCellVisibility.Hinted);
                }
            }
        }

        public void ScoutReveal(HexCoord center, int hintRadius)
        {
            if (hintRadius < 0 || !Map.Contains(center))
            {
                return;
            }

            RevealTemporaryArea(center, hintRadius);
        }

        private void ExpireTemporaryRevealsExcept(HashSet<HexCoord> keepRevealed)
        {
            if (temporaryRevealed.Count == 0)
            {
                return;
            }

            var expired = new List<HexCoord>();
            foreach (var coord in temporaryRevealed)
            {
                if (keepRevealed != null && keepRevealed.Contains(coord))
                {
                    continue;
                }

                if (permanentlyRevealed.Contains(coord))
                {
                    // 영구 공개는 강등하지 않는다(임시 목록에서만 뺀다).
                    expired.Add(coord);
                    continue;
                }

                ForceVisibility(coord, HexCellVisibility.Hinted);
                expired.Add(coord);
            }

            foreach (var coord in expired)
            {
                temporaryRevealed.Remove(coord);
            }
        }

        private void ForceVisibility(HexCoord coord, HexCellVisibility visibility)
        {
            if (!Map.Contains(coord))
            {
                return;
            }

            if (visibility == HexCellVisibility.Unknown)
            {
                var removed = states.Remove(coord);
                temporaryRevealed.Remove(coord);
                if (removed)
                {
                    Version++;
                }

                return;
            }

            if (states.TryGetValue(coord, out var existing) && existing == visibility)
            {
                return;
            }

            states[coord] = visibility;
            Version++;
        }

        public HexVisibilitySafeCellInfo GetSafeCellInfo(HexCoord coord)
        {
            if (!Map.TryGetCell(coord, out var cell))
            {
                return HexVisibilitySafeCellInfo.Missing(coord);
            }

            var visibility = GetVisibility(coord);
            if (visibility == HexCellVisibility.Unknown)
            {
                return HexVisibilitySafeCellInfo.Unknown(coord);
            }

            var isTrapRevealed = trapRevealed.Contains(coord);
            if (visibility == HexCellVisibility.Hinted)
            {
                return new HexVisibilitySafeCellInfo(
                    coord,
                    visibility,
                    true,
                    true,
                    false,
                    string.Empty,
                    cell.TerrainTypeId,
                    cell.BaseMoveCost,
                    cell.BaseWalkable,
                    cell.BaseBlocksVision,
                    string.Empty,
                    string.Empty,
                    cell.VisualFloor,
                    isTrapRevealed);
            }

            return new HexVisibilitySafeCellInfo(
                coord,
                visibility,
                true,
                true,
                true,
                cell.TileDefinitionId,
                cell.TerrainTypeId,
                cell.BaseMoveCost,
                cell.BaseWalkable,
                cell.BaseBlocksVision,
                cell.EventId,
                cell.LandmarkId,
                cell.VisualFloor,
                isTrapRevealed);
        }
    }
}
