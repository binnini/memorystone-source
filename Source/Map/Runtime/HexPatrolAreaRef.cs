using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    public readonly struct HexPatrolAreaRef : IEquatable<HexPatrolAreaRef>
    {
        private readonly HexCoord[] coords;

        public HexPatrolAreaRef(string id, IEnumerable<HexCoord> coords)
        {
            Id = id ?? string.Empty;
            this.coords = coords == null
                ? Array.Empty<HexCoord>()
                : coords.Distinct().OrderBy(coord => coord).ToArray();
        }

        public string Id { get; }
        public IReadOnlyList<HexCoord> Coords => coords ?? Array.Empty<HexCoord>();
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Id) && Coords.Count > 0;

        public bool Contains(HexCoord coord)
        {
            return coords != null && Array.IndexOf(coords, coord) >= 0;
        }

        public bool Equals(HexPatrolAreaRef other)
        {
            return Id == other.Id && Coords.SequenceEqual(other.Coords);
        }

        public override bool Equals(object obj)
        {
            return obj is HexPatrolAreaRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Id != null ? Id.GetHashCode() : 0;
                if (coords != null)
                {
                    foreach (var coord in coords)
                    {
                        hashCode = (hashCode * 397) ^ coord.GetHashCode();
                    }
                }

                return hashCode;
            }
        }
    }
}
