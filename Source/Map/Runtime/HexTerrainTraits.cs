using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// Central, extensible registry of per-terrain movement and selection traits.
    /// This is the single source of truth for "which terrain types may be moved into
    /// or selected", plus the global height-level traversal rule. Add new restricted
    /// terrain ids to <see cref="DefaultImpassableTerrainIds"/> /
    /// <see cref="DefaultUnselectableTerrainIds"/> (or build a custom instance, e.g. from
    /// an authoring palette) so future tiles can opt into the same attributes without
    /// touching pathfinding or selection code.
    /// </summary>
    public sealed class HexTerrainTraits
    {
        /// <summary>
        /// Maximum height-level difference that may be traversed between two adjacent
        /// cells. A difference strictly greater than this blocks movement, so the default
        /// of 1 means 0->1 is allowed but 0->2 is not.
        /// </summary>
        public const int DefaultMaxTraversableHeightDifference = 1;

        private readonly HashSet<string> impassableTerrainIds;
        private readonly HashSet<string> unselectableTerrainIds;

        public HexTerrainTraits(
            IEnumerable<string> impassableTerrainIds = null,
            IEnumerable<string> unselectableTerrainIds = null,
            int maxTraversableHeightDifference = DefaultMaxTraversableHeightDifference)
        {
            this.impassableTerrainIds = BuildSet(impassableTerrainIds);
            this.unselectableTerrainIds = BuildSet(unselectableTerrainIds);
            MaxTraversableHeightDifference = Math.Max(0, maxTraversableHeightDifference);
        }

        public int MaxTraversableHeightDifference { get; }

        public IReadOnlyCollection<string> ImpassableTerrainIds => impassableTerrainIds;
        public IReadOnlyCollection<string> UnselectableTerrainIds => unselectableTerrainIds;

        /// <summary>Water terrain ids that are blocked for movement out of the box.</summary>
        public static IReadOnlyList<string> DefaultImpassableTerrainIds { get; } = new[]
        {
            "hanriver-water",
            "river",
            "lake",
            "water"
        };

        /// <summary>Terrain ids that cannot be clicked/selected out of the box (water).</summary>
        public static IReadOnlyList<string> DefaultUnselectableTerrainIds { get; } = DefaultImpassableTerrainIds;

        /// <summary>
        /// Shared default ruleset: water tiles cannot be moved into or selected, and the
        /// height rule blocks moves across a difference of 2+ levels.
        /// </summary>
        public static HexTerrainTraits Default { get; } =
            new HexTerrainTraits(DefaultImpassableTerrainIds, DefaultUnselectableTerrainIds);

        public bool IsWalkable(string terrainTypeId)
        {
            return !impassableTerrainIds.Contains(Normalize(terrainTypeId));
        }

        public bool IsSelectable(string terrainTypeId)
        {
            return !unselectableTerrainIds.Contains(Normalize(terrainTypeId));
        }

        /// <summary>
        /// Whether a unit may step between two adjacent cells given their height levels.
        /// Movement is blocked once the absolute difference exceeds the configured maximum.
        /// </summary>
        public bool IsHeightTraversable(int fromHeightLevel, int toHeightLevel)
        {
            return Math.Abs(fromHeightLevel - toHeightLevel) <= MaxTraversableHeightDifference;
        }

        private static HashSet<string> BuildSet(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (ids == null)
            {
                return set;
            }

            foreach (var id in ids.Where(id => !string.IsNullOrWhiteSpace(id)))
            {
                set.Add(id.Trim());
            }

            return set;
        }

        private static string Normalize(string terrainTypeId)
        {
            return string.IsNullOrWhiteSpace(terrainTypeId) ? string.Empty : terrainTypeId.Trim();
        }
    }
}
