using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Query seam for movement overlay data. Runtime movement/pathfinding rules remain owned by CombatState.
    /// </summary>
    public sealed class MovementPathQuery
    {
        public IReadOnlyDictionary<HexCoord, int> GetReachablePlayerMoves(CombatState state)
        {
            return GetReachablePlayerMoves(state, string.Empty);
        }

        public IReadOnlyDictionary<HexCoord, int> GetReachablePlayerMoves(CombatState state, string cardId)
        {
            return state == null
                ? EmptyReachable
                : state.GetReachablePlayerMoves(cardId);
        }

        public bool IsReachableDestination(IReadOnlyDictionary<HexCoord, int> reachable, HexCoord destination)
        {
            return reachable != null && reachable.ContainsKey(destination);
        }

        private static readonly IReadOnlyDictionary<HexCoord, int> EmptyReachable = new Dictionary<HexCoord, int>();
    }
}
