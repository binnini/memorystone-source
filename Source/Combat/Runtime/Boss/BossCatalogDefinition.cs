using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 보스 프로필 카탈로그. 보스가 없는 전투(대부분)에서는 <see cref="Empty"/>를 쓰므로
    /// 보스 데이터를 모르는 호출부는 아무것도 바꾸지 않아도 된다.
    /// </summary>
    public sealed class BossCatalogDefinition
    {
        private readonly Dictionary<string, BossProfileDefinition> profilesById;
        private readonly List<BossProfileDefinition> profiles;

        public BossCatalogDefinition(string sourceId, string displayName, IEnumerable<BossProfileDefinition> profiles)
        {
            SourceId = string.IsNullOrWhiteSpace(sourceId)
                ? throw new ArgumentException("Boss catalog source id is required.", nameof(sourceId))
                : sourceId;
            DisplayName = displayName ?? string.Empty;
            this.profiles = (profiles ?? Array.Empty<BossProfileDefinition>())
                .OrderBy(profile => profile.BossId, StringComparer.Ordinal)
                .ToList();
            profilesById = this.profiles.ToDictionary(profile => profile.BossId, StringComparer.Ordinal);
        }

        public static BossCatalogDefinition Empty { get; } =
            new BossCatalogDefinition("empty-boss-catalog", "No Boss Profiles", Array.Empty<BossProfileDefinition>());

        public string SourceId { get; }
        public string DisplayName { get; }
        public IReadOnlyList<BossProfileDefinition> Profiles => profiles;

        public bool TryGetProfile(string bossId, out BossProfileDefinition profile)
        {
            if (string.IsNullOrWhiteSpace(bossId))
            {
                profile = null;
                return false;
            }

            return profilesById.TryGetValue(bossId, out profile);
        }
    }
}
