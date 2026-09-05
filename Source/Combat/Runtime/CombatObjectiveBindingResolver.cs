using SeoulPlayup.Map.Runtime;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    internal static class CombatObjectiveBindingResolver
    {
        public static HexObjectiveBinding Resolve(HexMapData map, out HexCoord? targetCoord)
        {
            if (TryResolveMemoryStone(map, out var memoryStoneBinding, out targetCoord))
            {
                return memoryStoneBinding;
            }

            foreach (var binding in map.ObjectiveBindings)
            {
                if (!binding.IsConfigured)
                {
                    continue;
                }

                foreach (var cell in map.AllCells)
                {
                    if (cell.LandmarkId == binding.LandmarkId)
                    {
                        targetCoord = cell.Coord;
                        return binding;
                    }
                }
            }

            targetCoord = null;
            return default;
        }

        private static bool TryResolveMemoryStone(HexMapData map, out HexObjectiveBinding binding, out HexCoord? targetCoord)
        {
            var memoryStone = map.ObjectRefs
                .Where(IsMemoryStoneObject)
                .OrderByDescending(obj => IsPrimaryMemoryStoneRole(obj.Role))
                .ThenBy(obj => obj.Coord)
                .FirstOrDefault();
            if (memoryStone.IsConfigured)
            {
                targetCoord = memoryStone.Coord;
                var displayName = string.IsNullOrWhiteSpace(memoryStone.Role) ||
                                  IsPrimaryMemoryStoneRole(memoryStone.Role)
                    ? "\uAE30\uC5B5\uACB0"
                    : memoryStone.Role;
                binding = new HexObjectiveBinding(
                    memoryStone.ObjectId,
                    memoryStone.ObjectRef,
                    displayName,
                    "Interact",
                    0);
                return true;
            }

            targetCoord = null;
            binding = default;
            return false;
        }

        private static bool IsMemoryStoneObject(HexMapObjectData obj)
        {
            return obj.IsConfigured &&
                   string.Equals(obj.ObjectType, "MemoryStone", System.StringComparison.Ordinal);
        }

        private static bool IsPrimaryMemoryStoneRole(string role)
        {
            return string.Equals(role, "main", System.StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(role, "objective", System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
