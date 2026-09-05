using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct PlayerCombatProfile
    {
        public PlayerCombatProfile(
            string profileId,
            string displayName,
            int maxHp,
            int movePoints,
            int attackRange,
            int attackDamage,
            int defenseBlock,
            int actionBudget,
            int movementHandSize,
            int actionHandSize,
            int visionRange,
            string status = "",
            string designerNote = "")
        {
            ProfileId = string.IsNullOrWhiteSpace(profileId)
                ? throw new ArgumentException("Player combat profile id is required.", nameof(profileId))
                : profileId.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? ProfileId : displayName.Trim();
            MaxHp = RequirePositive(maxHp, nameof(maxHp));
            MovePoints = RequireNonNegative(movePoints, nameof(movePoints));
            AttackRange = RequirePositive(attackRange, nameof(attackRange));
            AttackDamage = RequireNonNegative(attackDamage, nameof(attackDamage));
            DefenseBlock = RequireNonNegative(defenseBlock, nameof(defenseBlock));
            ActionBudget = RequirePositive(actionBudget, nameof(actionBudget));
            MovementHandSize = RequirePositive(movementHandSize, nameof(movementHandSize));
            ActionHandSize = RequirePositive(actionHandSize, nameof(actionHandSize));
            VisionRange = RequireNonNegative(visionRange, nameof(visionRange));
            Status = status?.Trim() ?? string.Empty;
            DesignerNote = designerNote?.Trim() ?? string.Empty;
        }

        public string ProfileId { get; }
        public string DisplayName { get; }
        public int MaxHp { get; }
        public int MovePoints { get; }
        public int AttackRange { get; }
        public int AttackDamage { get; }
        public int DefenseBlock { get; }
        public int ActionBudget { get; }
        public int MovementHandSize { get; }
        public int ActionHandSize { get; }
        public int VisionRange { get; }
        public string Status { get; }
        public string DesignerNote { get; }

        public CombatConfig ToCombatConfig(
            int enemyMaxHp,
            int enemyChaseRange,
            int enemyAttackRange,
            int enemyAttackDamage,
            int enemyDisengageRange = 4,
            int eliteHpPercent = 100,
            int eliteDamagePercent = 100)
        {
            return new CombatConfig(
                MaxHp,
                enemyMaxHp,
                MovePoints,
                AttackRange,
                AttackDamage,
                DefenseBlock,
                enemyChaseRange,
                enemyAttackRange,
                enemyAttackDamage,
                ActionBudget,
                MovementHandSize,
                ActionHandSize,
                VisionRange,
                enemyDisengageRange,
                eliteHpPercent,
                eliteDamagePercent);
        }

        public static PlayerCombatProfile Default => new PlayerCombatProfile(
            PlayerCombatProfileCatalog.DefaultProfileId,
            "Seorin",
            80,
            2,
            1,
            4,
            4,
            4,
            3,
            5,
            7,
            "fallback",
            "Code fallback matching player_combat_profiles.csv P001.");

        private static int RequirePositive(int value, string name)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} must be greater than zero.");
            }

            return value;
        }

        private static int RequireNonNegative(int value, string name)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(name, value, $"{name} must be zero or greater.");
            }

            return value;
        }
    }

    public sealed class PlayerCombatProfileCatalog
    {
        public const string DefaultProfileId = "P001";
        public const string PrototypeProfileId = "P001_PROTOTYPE";

        private readonly List<PlayerCombatProfile> profiles;

        public PlayerCombatProfileCatalog(string sourceId, string displayName, IEnumerable<PlayerCombatProfile> profiles)
        {
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? "player-combat-profiles" : sourceId.Trim();
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Player Combat Profiles" : displayName.Trim();
            this.profiles = profiles == null ? new List<PlayerCombatProfile>() : profiles.ToList();
        }

        public string SourceId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<PlayerCombatProfile> Profiles => profiles;

        public bool TryGetProfile(string profileId, out PlayerCombatProfile profile)
        {
            var id = string.IsNullOrWhiteSpace(profileId) ? DefaultProfileId : profileId.Trim();
            foreach (var candidate in profiles)
            {
                if (string.Equals(candidate.ProfileId, id, StringComparison.Ordinal))
                {
                    profile = candidate;
                    return true;
                }
            }

            profile = default;
            return false;
        }

        public PlayerCombatProfile GetProfileOrDefault(string profileId)
        {
            return TryGetProfile(profileId, out var profile) ? profile : PlayerCombatProfile.Default;
        }
    }
}
