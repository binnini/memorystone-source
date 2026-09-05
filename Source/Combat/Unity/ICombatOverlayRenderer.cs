using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;

namespace SeoulPlayup.Combat.Unity
{
    public interface ICombatOverlayRenderer
    {
        void ShowLayer(HexOverlayLayer layer, IEnumerable<HexCoord> coords, CombatOverlayStyle style);
        void ClearLayer(HexOverlayLayer layer);
        int GetActiveCount(HexOverlayLayer layer);
    }
}
