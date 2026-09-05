using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class MonsterCatalogBindingEvidence
    {
        public MonsterCatalogBindingEvidence(
            string catalogSourceId,
            IEnumerable<string> definitionIds,
            IEnumerable<string> activeDefinitionIds,
            IEnumerable<string> spawnRefIds,
            bool isValid,
            string failureReason)
        {
            CatalogSourceId = catalogSourceId ?? string.Empty;
            DefinitionIds = (definitionIds ?? Array.Empty<string>()).ToArray();
            ActiveDefinitionIds = (activeDefinitionIds ?? Array.Empty<string>()).ToArray();
            SpawnRefIds = (spawnRefIds ?? Array.Empty<string>()).ToArray();
            IsValid = isValid;
            FailureReason = failureReason ?? string.Empty;
        }

        public string CatalogSourceId { get; }
        public IReadOnlyList<string> DefinitionIds { get; }
        public IReadOnlyList<string> ActiveDefinitionIds { get; }
        public IReadOnlyList<string> SpawnRefIds { get; }
        public bool IsValid { get; }
        public string FailureReason { get; }

        public string ToEvidenceText()
        {
            var status = IsValid ? "valid" : $"invalid: {FailureReason}";
            return $"MonsterCatalog[{CatalogSourceId}] {status}; Definitions=[{string.Join(",", DefinitionIds)}]; ActiveDefinitions=[{string.Join(",", ActiveDefinitionIds)}]; SpawnRefs=[{string.Join(",", SpawnRefIds)}]";
        }
    }
}
