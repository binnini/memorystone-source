using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    internal sealed class DeferredMonsterActionState
    {
        private readonly Dictionary<string, DeferredMonsterActionSnapshot> attackSnapshots =
            new Dictionary<string, DeferredMonsterActionSnapshot>(StringComparer.Ordinal);

        public DeferredMonsterActionState(
            int playerHp,
            int playerBlock,
            HexCoord playerCoord,
            CombatPhase phase,
            IReadOnlyList<ActiveEffect> activeEffects)
        {
            Initial = new DeferredMonsterActionSnapshot(playerHp, playerBlock, playerCoord, phase, activeEffects);
        }

        public DeferredMonsterActionSnapshot Initial { get; }
        public DeferredMonsterActionSnapshot Final { get; private set; }
        public int FinalPlayerHp => Final?.PlayerHp ?? Initial.PlayerHp;

        public void RecordAttackSnapshot(string presentationGroupId, DeferredMonsterActionSnapshot after)
        {
            if (string.IsNullOrEmpty(presentationGroupId) || after == null)
            {
                return;
            }

            attackSnapshots[presentationGroupId] = after;
        }

        public bool TryGetAttackSnapshot(string presentationGroupId, out DeferredMonsterActionSnapshot snapshot)
        {
            return attackSnapshots.TryGetValue(presentationGroupId, out snapshot);
        }

        public void CaptureFinal(
            int playerHp,
            int playerBlock,
            HexCoord playerCoord,
            CombatPhase phase,
            IReadOnlyList<ActiveEffect> activeEffects)
        {
            Final = new DeferredMonsterActionSnapshot(playerHp, playerBlock, playerCoord, phase, activeEffects);
        }
    }

    internal sealed class DeferredMonsterActionSnapshot
    {
        public DeferredMonsterActionSnapshot(
            int playerHp,
            int playerBlock,
            HexCoord playerCoord,
            CombatPhase phase,
            IReadOnlyList<ActiveEffect> activeEffects)
        {
            PlayerHp = playerHp;
            PlayerBlock = playerBlock;
            PlayerCoord = playerCoord;
            Phase = phase;
            ActiveEffects = activeEffects == null
                ? new List<ActiveEffect>()
                : activeEffects.ToList();
        }

        public int PlayerHp { get; }
        public int PlayerBlock { get; }
        public HexCoord PlayerCoord { get; }
        public CombatPhase Phase { get; }
        public List<ActiveEffect> ActiveEffects { get; }
    }
}
