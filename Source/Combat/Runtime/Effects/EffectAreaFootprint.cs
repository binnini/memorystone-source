using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Shared area-effect footprint resolution: the event's committed <see cref="EffectResultEvent.AreaCoords"/>
    /// verbatim (map-filtered, authored order preserved), else the Center+Radius hex disk fallback.
    /// The tile flash and per-tile VFX both resolve through here, so the tiles that flash and the tiles
    /// that spawn effects can never drift apart (same structural guarantee as AttackCoverage for weak spots).
    /// </summary>
    public static class EffectAreaFootprint
    {
        /// <summary>
        /// Fills <paramref name="results"/> with the effect's footprint tiles that exist on
        /// <paramref name="map"/>, preserving the order the rules authored them in (shape offsets /
        /// center-out disk) — per-tile stagger direction rides on this order.
        /// </summary>
        public static void Resolve(EffectResultEvent resultEvent, HexMapData map, List<HexCoord> results)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            if (resultEvent.AreaCoords != null)
            {
                foreach (var coord in resultEvent.AreaCoords)
                {
                    if (map.TryGetCell(coord, out _))
                    {
                        results.Add(coord);
                    }
                }
            }

            if (results.Count > 0 || !resultEvent.Center.HasValue)
            {
                return;
            }

            foreach (var coord in HexArea.CellsWithin(resultEvent.Center.Value, Math.Max(0, resultEvent.Radius)))
            {
                if (map.TryGetCell(coord, out _))
                {
                    results.Add(coord);
                }
            }
        }
    }
}
