using System;
using System.Collections.Generic;

namespace SeoulPlayup.Map.Runtime
{
    [Serializable]
    public readonly struct HexCoord : IEquatable<HexCoord>, IComparable<HexCoord>
    {
        private static readonly HexCoord[] DirectionOffsets =
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1)
        };

        public HexCoord(int q, int r)
        {
            Q = q;
            R = r;
        }

        public int Q { get; }
        public int R { get; }
        public int S => -Q - R;

        public static IReadOnlyList<HexCoord> Directions => DirectionOffsets;

        public HexCoord Neighbor(HexDirection direction) => this + DirectionOffsets[(int)direction];

        public IEnumerable<HexCoord> NeighborsInDirectionOrder()
        {
            for (var i = 0; i < DirectionOffsets.Length; i++)
            {
                yield return this + DirectionOffsets[i];
            }
        }

        public int DistanceTo(HexCoord other)
        {
            return (Math.Abs(Q - other.Q) + Math.Abs(R - other.R) + Math.Abs(S - other.S)) / 2;
        }

        public HexCoord RotateSteps(int rotationSteps)
        {
            var steps = Math.Max(0, Math.Min(5, rotationSteps));
            var q = Q;
            var r = R;
            for (var i = 0; i < steps; i++)
            {
                var nextQ = -r;
                var nextR = q + r;
                q = nextQ;
                r = nextR;
            }

            return new HexCoord(q, r);
        }

        // Returns the one of the 6 hex directions that best points from this coord toward target.
        // Uses cube-space dot product; ties are broken by direction enum order.
        public HexDirection ApproximateDirection(HexCoord target)
        {
            var dq = target.Q - Q;
            var dr = target.R - R;
            var ds = -dq - dr;
            var best = HexDirection.East;
            var bestDot = int.MinValue;
            for (var i = 0; i < DirectionOffsets.Length; i++)
            {
                var d = DirectionOffsets[i];
                var dot = dq * d.Q + dr * d.R + ds * (-d.Q - d.R);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = (HexDirection)i;
                }
            }
            return best;
        }

        public int CompareTo(HexCoord other)
        {
            var qCompare = Q.CompareTo(other.Q);
            return qCompare != 0 ? qCompare : R.CompareTo(other.R);
        }

        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord other && Equals(other);
        public override int GetHashCode() => (Q * 397) ^ R;
        public override string ToString() => $"({Q},{R})";

        public static HexCoord operator +(HexCoord left, HexCoord right) => new HexCoord(left.Q + right.Q, left.R + right.R);
        public static bool operator ==(HexCoord left, HexCoord right) => left.Equals(right);
        public static bool operator !=(HexCoord left, HexCoord right) => !left.Equals(right);
    }
}
