using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// Pins the action-focus frustum gate (see docs/monster-action-camera-focus-plan.md §5 P1).
    ///
    /// Two things are being protected. First, the pure projection must agree with Unity's own — a silent
    /// divergence would make every EditMode expectation here meaningless while the shipped camera did
    /// something else, so <see cref="ProjectToViewport_MatchesUnityCamera"/> compares the two against a real
    /// <see cref="Camera"/> rather than trusting them to match. Second, the shipping camera rig's measured
    /// geometry is fixed as fixtures: the rig frames roughly 5.4 world units behind the player and 9.45 to
    /// each side, which is what makes a distant field tick off-screen while an adjacent attacker never is.
    /// If someone re-poses the rig, these fail and the focus budget has to be re-measured.
    /// </summary>
    public sealed class CombatActionFocusGeometryTests
    {
        [Test]
        public void AttackEmphasisPoint_LeansFromThePlayerTowardTheAttacker()
        {
            var player = new Vector3(0f, 0f, 0f);
            var attacker = new Vector3(0f, 0f, 4f);

            // Emphasis is a blend, not a relocation: bias 0 must leave the framing exactly where it was, so
            // turning the feature down can never shift the camera off the player.
            Assert.That(
                CombatCameraController.ResolveAttackEmphasisPoint(player, attacker, 0f),
                Is.EqualTo(player));
            Assert.That(
                CombatCameraController.ResolveAttackEmphasisPoint(player, attacker, 1f),
                Is.EqualTo(attacker));
            Assert.That(
                CombatCameraController.ResolveAttackEmphasisPoint(player, attacker, 0.5f).z,
                Is.EqualTo(2f).Within(0.0001f));

            // Out-of-range bias is clamped rather than extrapolated — an authoring slip must not fling the
            // camera past the attacker.
            Assert.That(
                CombatCameraController.ResolveAttackEmphasisPoint(player, attacker, 5f),
                Is.EqualTo(attacker));
            Assert.That(
                CombatCameraController.ResolveAttackEmphasisPoint(player, attacker, -3f),
                Is.EqualTo(player));
        }

        // The shipping gameplay rig: CombatCinemachineCameraProfile followOffset (0, 7, -9), perspective
        // FOV 50, 16:9. Distance to the player is 11.40 with a 37.9-degree downward pitch.
        private const float RigFieldOfView = 50f;
        private const float RigAspect = 16f / 9f;
        private static readonly Vector3 RigFollowOffset = new Vector3(0f, 7f, -9f);

        // Hex centres are 1.732 world units apart at tileRadius 1 / spacing 1 (HexAxialProjection).
        private static readonly HexAxialProjection Layout = new HexAxialProjection(1f);

        private static CombatCameraController.CameraFrame RigFrameAtOrigin()
        {
            return RigFrameFollowing(Vector3.zero);
        }

        private static CombatCameraController.CameraFrame RigFrameFollowing(Vector3 target)
        {
            var position = target + RigFollowOffset;
            return new CombatCameraController.CameraFrame(
                position,
                Quaternion.LookRotation(target - position, Vector3.up),
                orthographic: false,
                verticalFieldOfViewDegrees: RigFieldOfView,
                orthographicSize: 0f,
                aspect: RigAspect);
        }

        private static Vector3 HexWorld(HexCoord coord)
        {
            Layout.CoordToWorld(coord, out var x, out var z);
            return new Vector3(x, 0f, z);
        }

        private static IEnumerable<HexCoord> Ring(int radius)
        {
            for (var q = -radius; q <= radius; q++)
            {
                for (var r = -radius; r <= radius; r++)
                {
                    var coord = new HexCoord(q, r);
                    if (new HexCoord(0, 0).DistanceTo(coord) == radius)
                    {
                        yield return coord;
                    }
                }
            }
        }

        // --- Divergence guard -------------------------------------------------------------------

        [Test]
        public void ProjectToViewport_MatchesUnityCamera()
        {
            var host = new GameObject("action-focus-projection-probe");
            try
            {
                var camera = host.AddComponent<Camera>();
                camera.orthographic = false;
                camera.fieldOfView = RigFieldOfView;
                camera.aspect = RigAspect;
                camera.transform.position = RigFollowOffset;
                camera.transform.rotation = Quaternion.LookRotation(-RigFollowOffset, Vector3.up);

                var frame = new CombatCameraController.CameraFrame(
                    camera.transform.position,
                    camera.transform.rotation,
                    camera.orthographic,
                    camera.fieldOfView,
                    camera.orthographicSize,
                    camera.aspect);

                // On-screen centre, both near edges, well off to one side, and behind the camera.
                var probes = new[]
                {
                    Vector3.zero,
                    new Vector3(9f, 0f, 0f),
                    new Vector3(-9f, 0f, 0f),
                    new Vector3(0f, 0f, -5f),
                    new Vector3(0f, 0f, 18f),
                    new Vector3(30f, 0f, 4f),
                    new Vector3(0f, 0f, -40f),
                };

                foreach (var probe in probes)
                {
                    var unity = camera.WorldToViewportPoint(probe);
                    var projected = CombatCameraController.TryProjectToViewport(frame, probe, out var x, out var y);

                    Assert.AreEqual(
                        unity.z > 0f,
                        projected,
                        $"in-front-of-camera verdict diverged for {probe} (Unity depth {unity.z})");

                    if (!projected)
                    {
                        continue;
                    }

                    Assert.AreEqual(unity.x, x, 0.001f, $"viewport X diverged for {probe}");
                    Assert.AreEqual(unity.y, y, 0.001f, $"viewport Y diverged for {probe}");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        // --- Measured rig geometry --------------------------------------------------------------

        [Test]
        public void IsPointFramed_FramesPlayerAndNearbyGround()
        {
            var frame = RigFrameAtOrigin();

            Assert.IsTrue(CombatCameraController.IsPointFramed(frame, Vector3.zero), "the followed player must be framed");
            // Measured extents: ~5.41 units behind the player, ~9.45 to each side.
            Assert.IsTrue(CombatCameraController.IsPointFramed(frame, new Vector3(0f, 0f, -5f)), "5 units behind is inside");
            Assert.IsTrue(CombatCameraController.IsPointFramed(frame, new Vector3(9f, 0f, 0f)), "9 units sideways is inside");
            Assert.IsTrue(CombatCameraController.IsPointFramed(frame, new Vector3(-9f, 0f, 0f)), "9 units sideways is inside");
        }

        [Test]
        public void IsPointFramed_RejectsGroundBeyondMeasuredExtents()
        {
            var frame = RigFrameAtOrigin();

            Assert.IsFalse(CombatCameraController.IsPointFramed(frame, new Vector3(0f, 0f, -6f)), "6 units behind is outside");
            Assert.IsFalse(CombatCameraController.IsPointFramed(frame, new Vector3(10f, 0f, 0f)), "10 units sideways is outside");
            Assert.IsFalse(CombatCameraController.IsPointFramed(frame, new Vector3(-10f, 0f, 0f)), "10 units sideways is outside");
        }

        [Test]
        public void IsPointFramed_NeverMovesCameraForAdjacentActors()
        {
            var frame = RigFrameAtOrigin();

            // Every monster attack pattern authored in monster_attack_patterns.csv has range 1 or 2, so an
            // attacker is always within two hexes of the player. Rings 0..3 must be fully framed or the
            // geometric gate would start firing for adjacent attacks.
            for (var radius = 0; radius <= 3; radius++)
            {
                foreach (var coord in Ring(radius))
                {
                    Assert.IsTrue(
                        CombatCameraController.IsPointFramed(frame, HexWorld(coord)),
                        $"ring {radius} cell {coord} must be framed");
                }
            }
        }

        [Test]
        public void IsPointFramed_LeavesDistantVisionRingsPartlyOffScreen()
        {
            var frame = RigFrameAtOrigin();

            // The player's vision range is 7 (player_combat_profiles.csv), and reveals detach further still
            // via field objects and scout cards. Those outer rings are exactly the case the focus feature
            // exists for, so they must NOT all be framed.
            AssertRingPartiallyFramed(frame, radius: 4, expectAtMostHalf: false);
            AssertRingPartiallyFramed(frame, radius: 7, expectAtMostHalf: true);
        }

        private static void AssertRingPartiallyFramed(
            CombatCameraController.CameraFrame frame,
            int radius,
            bool expectAtMostHalf)
        {
            var total = 0;
            var framed = 0;
            foreach (var coord in Ring(radius))
            {
                total++;
                if (CombatCameraController.IsPointFramed(frame, HexWorld(coord)))
                {
                    framed++;
                }
            }

            Assert.Greater(total, 0, $"ring {radius} fixture produced no cells");
            Assert.Less(framed, total, $"ring {radius} must not be fully framed ({framed}/{total})");
            if (expectAtMostHalf)
            {
                Assert.LessOrEqual(framed * 2, total, $"ring {radius} should be mostly off-screen ({framed}/{total})");
            }
        }

        [Test]
        public void IsPointFramed_FollowsTheCameraTarget()
        {
            // A point that is off-screen while the camera sits on the origin becomes framed once the camera
            // follows the player to it — the gate is about the live framing, not about distance from origin.
            var farPoint = new Vector3(0f, 0f, -20f);
            Assert.IsFalse(CombatCameraController.IsPointFramed(RigFrameAtOrigin(), farPoint));
            Assert.IsTrue(CombatCameraController.IsPointFramed(RigFrameFollowing(farPoint), farPoint));
        }

        // --- Margin, degenerate input -----------------------------------------------------------

        [Test]
        public void IsPointFramed_MarginShrinksTheAcceptedArea()
        {
            var frame = RigFrameAtOrigin();
            var nearEdge = new Vector3(9f, 0f, 0f);

            Assert.IsTrue(CombatCameraController.IsPointFramed(frame, nearEdge, 0f), "inside with no margin");
            Assert.IsFalse(
                CombatCameraController.IsPointFramed(frame, nearEdge, 0.1f),
                "a point clipping the edge must read as off-screen once the margin applies");
            Assert.IsTrue(
                CombatCameraController.IsPointFramed(frame, Vector3.zero, 0.1f),
                "the centre stays framed under the margin");
        }

        [Test]
        public void IsPointFramed_RejectsPointsBehindTheCamera()
        {
            var frame = RigFrameAtOrigin();

            // Behind the camera there is no viewport position at all; the rig sits at z = -9 looking toward +z.
            Assert.IsFalse(CombatCameraController.IsPointFramed(frame, new Vector3(0f, 0f, -40f)));
        }

        [Test]
        public void IsPointFramed_TreatsAnUnresolvedFrameAsNotFramed()
        {
            // A default frame means no camera was resolved. Reporting "framed" there would silently disable
            // every focus; reporting "not framed" degrades to focusing, which is the visible failure.
            Assert.IsFalse(CombatCameraController.IsPointFramed(default, Vector3.zero));
            Assert.IsFalse(CombatCameraController.TryProjectToViewport(default, Vector3.zero, out _, out _));
        }

        [Test]
        public void CameraFrame_FromNullCameraIsInvalid()
        {
            Assert.IsFalse(CombatCameraController.CameraFrame.FromCamera(null).IsValid);
        }

        [Test]
        public void IsPointFramed_SupportsOrthographicFrames()
        {
            var position = RigFollowOffset;
            var frame = new CombatCameraController.CameraFrame(
                position,
                Quaternion.LookRotation(-position, Vector3.up),
                orthographic: true,
                verticalFieldOfViewDegrees: 0f,
                orthographicSize: 5f,
                aspect: RigAspect);

            Assert.IsTrue(frame.IsValid);
            Assert.IsTrue(CombatCameraController.IsPointFramed(frame, Vector3.zero));
            // Half-width is 5 * 16/9 = 8.89, so 20 units sideways is far outside regardless of depth.
            Assert.IsFalse(CombatCameraController.IsPointFramed(frame, new Vector3(20f, 0f, 0f)));
        }

        // --- Cluster focus point ----------------------------------------------------------------

        [Test]
        public void ResolveClusterFocusPoint_ReturnsBoundingBoxCentre()
        {
            var points = new List<Vector3>
            {
                new Vector3(-4f, 0f, 2f),
                new Vector3(6f, 0f, 2f),
                new Vector3(0f, 0f, -3f),
            };

            var focus = CombatCameraController.ResolveClusterFocusPoint(points);

            Assert.AreEqual(1f, focus.x, 0.0001f);
            Assert.AreEqual(0f, focus.y, 0.0001f);
            Assert.AreEqual(-0.5f, focus.z, 0.0001f);
        }

        [Test]
        public void ResolveClusterFocusPoint_IgnoresCrowdingSoOutliersStayFramed()
        {
            // Three events bunched at one end plus a lone outlier: a centroid would sit at x = 2.5 and leave
            // the outlier near the frame edge, while the bounding-box centre sits midway at x = 5.
            var points = new List<Vector3>
            {
                Vector3.zero,
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 0f),
                new Vector3(10f, 0f, 0f),
            };

            Assert.AreEqual(5f, CombatCameraController.ResolveClusterFocusPoint(points).x, 0.0001f);
        }

        [Test]
        public void ResolveClusterFocusPoint_HandlesEmptyAndSingleGroups()
        {
            Assert.AreEqual(Vector3.zero, CombatCameraController.ResolveClusterFocusPoint(null));
            Assert.AreEqual(Vector3.zero, CombatCameraController.ResolveClusterFocusPoint(new List<Vector3>()));

            var single = new Vector3(3f, 0f, -7f);
            Assert.AreEqual(single, CombatCameraController.ResolveClusterFocusPoint(new List<Vector3> { single }));
        }
    }
}
