using System;

namespace SeoulPlayup.Map.Runtime
{
    [Serializable]
    public readonly struct HexTerrainStats
    {
        public HexTerrainStats(int defaultMoveCost, int combatDefenseBonus, int combatAttackBonus)
        {
            DefaultMoveCost = Math.Max(1, defaultMoveCost);
            CombatDefenseBonus = Math.Max(0, combatDefenseBonus);
            CombatAttackBonus = Math.Max(0, combatAttackBonus);
        }

        public int DefaultMoveCost { get; }
        public int CombatDefenseBonus { get; }
        public int CombatAttackBonus { get; }

        public static HexTerrainStats Default => new HexTerrainStats(1, 0, 0);
    }
}
