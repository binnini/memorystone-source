using System;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct FieldObjectTarget
    {
        public FieldObjectTarget(CombatantState combatant, HexCoord position, FieldObjectTargetKind kind)
        {
            Combatant = combatant ?? throw new ArgumentNullException(nameof(combatant));
            Position = position;
            Kind = kind;
        }

        public CombatantState Combatant { get; }
        public HexCoord Position { get; }
        public FieldObjectTargetKind Kind { get; }
    }
}
