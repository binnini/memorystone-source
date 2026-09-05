using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public readonly struct MonsterConfig
    {
        public MonsterConfig(
            string id,
            HexCoord startCoord,
            int maxHp = 0,
            string catalogSourceId = "",
            string definitionId = "",
            string spawnRefId = "",
            string spawnRole = "",
            IEnumerable<HexCoord> patrolArea = null)
        {
            Id = string.IsNullOrEmpty(id) ? "monster" : id;
            StartCoord = startCoord;
            MaxHp = maxHp <= 0 ? 10 : maxHp;
            CatalogSourceId = catalogSourceId ?? string.Empty;
            DefinitionId = string.IsNullOrWhiteSpace(definitionId) ? Id : definitionId;
            SpawnRefId = spawnRefId ?? string.Empty;
            SpawnRole = spawnRole ?? string.Empty;
            PatrolArea = patrolArea == null ? new List<HexCoord>() : patrolArea.Distinct().OrderBy(coord => coord).ToList();
        }

        public string Id { get; }
        public HexCoord StartCoord { get; }
        public int MaxHp { get; }
        public string CatalogSourceId { get; }
        public string DefinitionId { get; }
        public string SpawnRefId { get; }
        public string SpawnRole { get; }
        public IReadOnlyList<HexCoord> PatrolArea { get; }
    }
}
