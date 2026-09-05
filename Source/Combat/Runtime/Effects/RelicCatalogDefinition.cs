using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 유물·저주(영구 패시브 아이템) 카탈로그. 항목 정의는 <see cref="PlayerPermanentItemDefinition"/>를
    /// 재사용한다. <see cref="StatusEffectCatalogDefinition"/>과 같은 형태(불변 목록 + id 조회)로,
    /// <c>RelicCatalogCsv</c>가 CSV에서 생성하고 <see cref="PlayerPermanentItemCatalog"/>가 소비한다.
    /// </summary>
    public sealed class RelicCatalogDefinition
    {
        private readonly Dictionary<string, PlayerPermanentItemDefinition> byId;

        public RelicCatalogDefinition(IEnumerable<PlayerPermanentItemDefinition> entries)
        {
            Entries = (entries ?? Array.Empty<PlayerPermanentItemDefinition>()).ToList();
            byId = new Dictionary<string, PlayerPermanentItemDefinition>(StringComparer.Ordinal);
            foreach (var entry in Entries)
            {
                if (entry == null)
                {
                    continue;
                }

                byId[entry.Id] = entry;
            }
        }

        public IReadOnlyList<PlayerPermanentItemDefinition> Entries { get; }

        public bool TryGet(string id, out PlayerPermanentItemDefinition definition)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                definition = null;
                return false;
            }

            return byId.TryGetValue(id, out definition);
        }
    }
}
