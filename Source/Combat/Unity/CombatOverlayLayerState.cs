using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Unity
{
    public readonly struct CombatOverlayLayerState
    {
        public CombatOverlayLayerState(HexOverlayLayer layer, IEnumerable<HexCoord> coords, CombatOverlayStyle style)
        {
            Layer = layer;
            Coords = coords != null
                ? coords.Distinct().OrderBy(coord => coord).ToArray()
                : Array.Empty<HexCoord>();
            Style = style;
        }

        public HexOverlayLayer Layer { get; }
        public IReadOnlyList<HexCoord> Coords { get; }
        public CombatOverlayStyle Style { get; }
    }
}
