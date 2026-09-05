using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.MapDesign.Editor
{
    public static class HexSparseMapSceneViewHelper
    {
        public static bool TryGetViewportAxialRange(
            Camera camera,
            float planeY,
            HexAxialLayout layout,
            int padding,
            int maxRenderRadius,
            Matrix4x4 worldToLocal,
            out int minQ,
            out int maxQ,
            out int minR,
            out int maxR)
        {
            minQ = maxQ = minR = maxR = 0;

            if (camera == null)
                return false;

            // Reject near-parallel: camera forward nearly horizontal (y ≈ 0 means rays miss the plane)
            if (Mathf.Abs(camera.transform.forward.y) < 0.01f)
                return false;

            Vector2[] corners = new Vector2[4];
            Vector3[] viewportCorners = new Vector3[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(1f, 0f, 0f),
                new Vector3(0f, 1f, 0f),
                new Vector3(1f, 1f, 0f),
            };

            float fallbackDist = maxRenderRadius * layout.TileRadius * 2f;

            for (int i = 0; i < 4; i++)
            {
                Ray worldRay = camera.ViewportPointToRay(viewportCorners[i]);
                Ray ray = new Ray(worldToLocal.MultiplyPoint3x4(worldRay.origin), worldToLocal.MultiplyVector(worldRay.direction));
                if (TryRayPlaneIntersect(ray, planeY, out Vector3 hit))
                {
                    corners[i] = new Vector2(hit.x, hit.z);
                }
                else
                {
                    // Estimate hit using fallback distance along ray direction
                    Vector3 estimated = ray.origin + ray.direction * fallbackDist;
                    corners[i] = new Vector2(estimated.x, estimated.z);
                }
            }

            // Compute AABB of the 4 XZ hit points
            float xMin = corners[0].x, xMax = corners[0].x;
            float zMin = corners[0].y, zMax = corners[0].y;
            for (int i = 1; i < 4; i++)
            {
                if (corners[i].x < xMin) xMin = corners[i].x;
                if (corners[i].x > xMax) xMax = corners[i].x;
                if (corners[i].y < zMin) zMin = corners[i].y;
                if (corners[i].y > zMax) zMax = corners[i].y;
            }

            layout.GetAxialRangeForBounds(new Vector2(xMin, zMin), new Vector2(xMax, zMax), padding,
                out minQ, out maxQ, out minR, out maxR);

            // Clamp range to maxRenderRadius * 2 around the center
            int diameter = maxRenderRadius * 2;
            if (maxQ - minQ > diameter || maxR - minR > diameter)
            {
                // Compute center cell from camera position projected onto plane
                Ray centerWorldRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                Ray centerRay = new Ray(worldToLocal.MultiplyPoint3x4(centerWorldRay.origin), worldToLocal.MultiplyVector(centerWorldRay.direction));
                Vector3 centerHit;
                if (!TryRayPlaneIntersect(centerRay, planeY, out centerHit))
                {
                    centerHit = centerRay.origin + centerRay.direction * fallbackDist;
                }

                HexCoord centerCoord = layout.WorldToCoord(new Vector2(centerHit.x, centerHit.z));
                minQ = centerCoord.Q - maxRenderRadius;
                maxQ = centerCoord.Q + maxRenderRadius;
                minR = centerCoord.R - maxRenderRadius;
                maxR = centerCoord.R + maxRenderRadius;
            }

            return true;
        }

        private static bool TryRayPlaneIntersect(Ray ray, float planeY, out Vector3 hit)
        {
            // plane normal = Vector3.up, plane point = (0, planeY, 0)
            // t = (planeY - ray.origin.y) / ray.direction.y
            if (Mathf.Abs(ray.direction.y) < 1e-6f)
            {
                hit = default;
                return false;
            }

            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t < 0f)
            {
                hit = default;
                return false;
            }

            hit = ray.origin + t * ray.direction;
            return true;
        }
    }
}
