namespace SeoulPlayup.Combat.Runtime
{
    public enum CombatCardKind
    {
        Move,
        Attack,
        Defend,
        Scout,
        Investigate,
        FieldObject,
        Buff,
        Utility
    }

    public static class CombatCardHandOrder
    {
        /// <summary>
        /// Hand display grouping priority (lower shows first, left to right):
        /// Move, then the recon/utility group (Scout + Utility), then every other action card.
        /// Call sites break ties with <see cref="CombatCardKind"/> then card id for stable order.
        /// </summary>
        public static int HandSortPriority(CombatCardKind kind)
        {
            switch (kind)
            {
                case CombatCardKind.Move:
                    return 0;
                case CombatCardKind.Scout:
                case CombatCardKind.Utility:
                    return 1;
                default:
                    return 2;
            }
        }
    }
}
