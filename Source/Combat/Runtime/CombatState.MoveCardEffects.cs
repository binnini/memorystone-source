namespace SeoulPlayup.Combat.Runtime
{
    public sealed partial class CombatState
    {
        internal void ClearPendingMovementRangeBonus()
        {
            // M05 grants next-turn Agility and no longer locks out same-turn movement.
            // Do not clear the pending bonus just because another movement card resolves afterward.
        }
    }
}
