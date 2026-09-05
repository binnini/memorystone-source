using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    public static class CombatFacingUtility
    {
        public const float DefaultHexSideYawOffset = 90f;

        public static Quaternion ResolveHexSideRotation(Vector3 direction, bool snapToHexSides = true, float hexFacingYawOffset = DefaultHexSideYawOffset)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return Quaternion.identity;
            }

            direction.Normalize();
            if (snapToHexSides)
            {
                direction = SnapToNearestHexSideDirection(direction, hexFacingYawOffset);
            }

            return Quaternion.LookRotation(direction, Vector3.up);
        }

        public static Quaternion ResolveHexSideRotation(Vector3 fromWorld, Vector3 toWorld, bool snapToHexSides = true, float hexFacingYawOffset = DefaultHexSideYawOffset)
        {
            var direction = toWorld - fromWorld;
            direction.y = 0f;
            return ResolveHexSideRotation(direction, snapToHexSides, hexFacingYawOffset);
        }

        public static Vector3 SnapToNearestHexSideDirection(Vector3 direction, float hexFacingYawOffset = DefaultHexSideYawOffset)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return Vector3.forward;
            }

            direction.Normalize();
            var best = Vector3.forward;
            var bestDot = float.NegativeInfinity;
            for (var i = 0; i < 6; i++)
            {
                var yaw = hexFacingYawOffset - i * 60f;
                var candidate = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
                var dot = Vector3.Dot(direction, candidate);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = candidate;
                }
            }

            return best;
        }
    }
}
