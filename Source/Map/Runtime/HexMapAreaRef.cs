using System;
using System.Collections.Generic;
using System.Linq;

namespace SeoulPlayup.Map.Runtime
{
    /// <summary>
    /// 목적이 붙은 저작 영역(셀 집합). <see cref="HexPatrolAreaRef"/>와 같은 모양이지만 순찰 전용이 아니라
    /// <see cref="Purpose"/>로 용도를 구분하고, 그 용도의 주체(현재는 보스 스폰 ref)와 묶인다.
    ///
    /// 보스 아레나가 1호 용도다: 어느 맵에든 "보스 스폰 ref + 아레나 영역" 세트를 저작하면 결계가 성립하므로,
    /// 특정 보스를 아는 코드 없이 다중 보스를 수용한다.
    /// </summary>
    public readonly struct HexMapAreaRef : IEquatable<HexMapAreaRef>
    {
        /// <summary>보스 아레나 용도. <see cref="Purpose"/> 비교는 대소문자를 무시한다.</summary>
        public const string BossArenaPurpose = "boss-arena";

        private readonly HexCoord[] coords;

        public HexMapAreaRef(string id, IEnumerable<HexCoord> coords, string purpose = null, string bossSpawnRefId = null)
        {
            Id = id ?? string.Empty;
            this.coords = coords == null
                ? Array.Empty<HexCoord>()
                : coords.Distinct().OrderBy(coord => coord).ToArray();
            Purpose = purpose ?? string.Empty;
            BossSpawnRefId = bossSpawnRefId ?? string.Empty;
        }

        public string Id { get; }
        public IReadOnlyList<HexCoord> Coords => coords ?? Array.Empty<HexCoord>();

        /// <summary>용도 문자열(저작). 빈 값은 "용도 없음"이며 어떤 소비자도 집지 않는다.</summary>
        public string Purpose { get; }

        /// <summary>이 영역이 묶인 보스 스폰 ref의 id. 보스 아레나에서만 의미가 있다.</summary>
        public string BossSpawnRefId { get; }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Id) && Coords.Count > 0;

        public bool IsBossArena => string.Equals(Purpose, BossArenaPurpose, StringComparison.OrdinalIgnoreCase);

        public bool Contains(HexCoord coord)
        {
            return coords != null && Array.IndexOf(coords, coord) >= 0;
        }

        /// <summary>
        /// 결계 링: 영역 <b>바깥</b>에서 영역 셀과 맞닿은 셀들. 결계는 이 링을 막아 안팎을 동시에 봉쇄한다.
        ///
        /// 영역 안쪽 테두리가 아니라 바깥 링을 막는 이유: 안쪽을 막으면 아레나 내부가 한 칸 좁아지고
        /// 보스·플레이어가 테두리에 서 있던 경우 상태가 모순된다. 바깥 링을 막으면 아레나 전체가
        /// 그대로 싸움터로 남고, 나가는 모든 경로가 닫히며(플레이어 봉쇄), 들어오는 모든 경로도
        /// 같은 셀로 닫힌다(몬스터 봉쇄) — 대칭이 공짜로 성립한다.
        /// </summary>
        public IEnumerable<HexCoord> EnumerateBoundaryRing()
        {
            if (coords == null || coords.Length == 0)
            {
                yield break;
            }

            var inside = new HashSet<HexCoord>(coords);
            var emitted = new HashSet<HexCoord>();
            foreach (var coord in coords)
            {
                foreach (var neighbor in coord.NeighborsInDirectionOrder())
                {
                    if (!inside.Contains(neighbor) && emitted.Add(neighbor))
                    {
                        yield return neighbor;
                    }
                }
            }
        }

        /// <summary>
        /// 영역의 중심 셀. 저작 좌표의 무게중심을 큐브 반올림하고, 그 칸이 영역 밖이면(도넛·초승달 모양
        /// 영역) 영역 안에서 가장 가까운 칸으로 스냅한다 — 반환값이 항상 영역 안이라는 보장이 있어야
        /// 이 값을 기준으로 뭔가를 배치하는 쪽이 다시 검사하지 않는다.
        ///
        /// 동거리 후보는 좌표 순으로 자른다(결정적). 빈 영역은 원점을 돌려준다.
        /// </summary>
        public HexCoord GetCenter()
        {
            if (coords == null || coords.Length == 0)
            {
                return new HexCoord(0, 0);
            }

            double sumQ = 0;
            double sumR = 0;
            foreach (var coord in coords)
            {
                sumQ += coord.Q;
                sumR += coord.R;
            }

            var rounded = CubeRound(sumQ / coords.Length, sumR / coords.Length);
            if (Contains(rounded))
            {
                return rounded;
            }

            return coords
                .OrderBy(coord => coord.DistanceTo(rounded))
                .ThenBy(coord => coord)
                .First();
        }

        /// <summary>분수 축 좌표를 가장 가까운 헥스로 반올림한다(q+r+s=0을 유지하는 표준 큐브 반올림).</summary>
        private static HexCoord CubeRound(double q, double r)
        {
            var s = -q - r;
            var roundedQ = Math.Round(q, MidpointRounding.AwayFromZero);
            var roundedR = Math.Round(r, MidpointRounding.AwayFromZero);
            var roundedS = Math.Round(s, MidpointRounding.AwayFromZero);

            var deltaQ = Math.Abs(roundedQ - q);
            var deltaR = Math.Abs(roundedR - r);
            var deltaS = Math.Abs(roundedS - s);

            if (deltaQ > deltaR && deltaQ > deltaS)
            {
                roundedQ = -roundedR - roundedS;
            }
            else if (deltaR > deltaS)
            {
                roundedR = -roundedQ - roundedS;
            }

            return new HexCoord((int)roundedQ, (int)roundedR);
        }

        public bool Equals(HexMapAreaRef other)
        {
            return Id == other.Id &&
                   Purpose == other.Purpose &&
                   BossSpawnRefId == other.BossSpawnRefId &&
                   Coords.SequenceEqual(other.Coords);
        }

        public override bool Equals(object obj)
        {
            return obj is HexMapAreaRef other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Id != null ? Id.GetHashCode() : 0;
                hashCode = (hashCode * 397) ^ (Purpose != null ? Purpose.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (BossSpawnRefId != null ? BossSpawnRefId.GetHashCode() : 0);
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
