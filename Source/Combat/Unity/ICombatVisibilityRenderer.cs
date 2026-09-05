using System;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    public interface ICombatVisibilityRenderer
    {
        void ApplyVisibility(Func<HexCoord, HexVisibilitySafeCellInfo> visibilityProvider, long? stateEpoch = null);
    }
}
