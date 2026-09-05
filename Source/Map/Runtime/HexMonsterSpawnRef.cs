using System;

namespace SeoulPlayup.Map.Runtime
{
    public readonly struct HexMonsterSpawnRef : IEquatable<HexMonsterSpawnRef>
    {
        public HexMonsterSpawnRef(string id, string monsterId, HexCoord coord, string spawnRole = null, HexMapPurpose enabledForPurpose = HexMapPurpose.Unspecified, string patrolAreaId = "")
        {
            Id = id ?? string.Empty;
            MonsterId = monsterId ?? string.Empty;
            Coord = coord;
            SpawnRole = spawnRole ?? string.Empty;
            EnabledForPurpose = enabledForPurpose;
            PatrolAreaId = patrolAreaId ?? string.Empty;
        }

        public string Id { get; }
        public string MonsterId { get; }
        public HexCoord Coord { get; }
        public string SpawnRole { get; }
        public HexMapPurpose EnabledForPurpose { get; }
        public string PatrolAreaId { get; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Id) && !string.IsNullOrWhiteSpace(MonsterId);

        public bool IsEnabledFor(HexMapPurpose purpose)
        {
            return EnabledForPurpose == HexMapPurpose.Unspecified ||
                   purpose == HexMapPurpose.Unspecified ||
                   EnabledForPurpose == purpose;
        }

        public bool Equals(HexMonsterSpawnRef other)
        {
            return Id == other.Id &&
                   MonsterId == other.MonsterId &&
                   Coord.Equals(other.Coord) &&
                   SpawnRole == other.SpawnRole &&
                   EnabledForPurpose == other.EnabledForPurpose &&
                   PatrolAreaId == other.PatrolAreaId;
        }

        public override bool Equals(object obj)
        {
            return obj is HexMonsterSpawnRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Id != null ? Id.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (MonsterId != null ? MonsterId.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ Coord.GetHashCode();
                hashCode = (hashCode * 397) ^ (SpawnRole != null ? SpawnRole.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)EnabledForPurpose;
                hashCode = (hashCode * 397) ^ (PatrolAreaId != null ? PatrolAreaId.GetHashCode() : 0);
                return hashCode;
            }
        }
    }
}
