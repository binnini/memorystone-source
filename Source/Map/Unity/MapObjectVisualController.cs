using System.Collections.Generic;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class MapObjectVisualController : MonoBehaviour
    {
        private readonly List<MapObjectFadeTarget> fadeTargets = new List<MapObjectFadeTarget>();

        private void OnMouseEnter()
        {
            SetPointerHovered(true);
        }

        private void OnDisable()
        {
            SetPointerHovered(false);
        }

        private void OnMouseExit()
        {
            SetPointerHovered(false);
        }

        public bool IsPointerHovered { get; private set; }

        public IReadOnlyList<MapObjectFadeTarget> FadeTargets
        {
            get
            {
                EnsureFadeTargets();
                return fadeTargets;
            }
        }

        public static MapObjectVisualController Ensure(GameObject root, bool expandHoverToCell = false)
        {
            if (root == null)
            {
                return null;
            }

            if (!root.TryGetComponent<MapObjectVisualController>(out var controller))
            {
                controller = root.AddComponent<MapObjectVisualController>();
            }

            controller.ConfigureRuntimePresentation(expandHoverToCell);
            return controller;
        }

        /// <summary>
        /// 호버 판정 상자의 <b>최소 가로·세로</b>(월드 단위). 셀 중심 간 거리가 약 1.73이므로
        /// 1.2는 이웃 칸을 침범하지 않으면서 한 칸을 거의 채운다(육각 내접 정사각형 한 변 ≈ 1.5).
        ///
        /// <para>🔴 필요한 이유(2026-09-05 실플레이): 생성되는 상자는 <b>렌더러 바운즈에 딱 맞는다</b>.
        /// 그런데 잡화점은 0.97×0.66, 캠핑카는 0.76×1.00으로 셀보다 한참 작아서, 플레이어는
        /// 「저 칸의 상점」이 아니라 <b>모델 자체</b>를 정확히 겨눠야 했다 — 그게 「호버가 가끔
        /// 안 된다」의 정체다. 높이는 늘리지 않는다(위에서 겨누는 카메라라 세로는 판정에 안 쓰이고,
        /// 늘리면 뒤쪽 칸의 물건을 가린다).</para>
        /// </summary>
        private const float MinimumHoverFootprint = 1.2f;

        public void ConfigureRuntimePresentation()
        {
            ConfigureRuntimePresentation(expandHoverToCell: false);
        }

        /// <param name="expandHoverToCell">
        /// 호버 상자를 <see cref="MinimumHoverFootprint"/>까지 넓힌다. 툴팁·상호작용이 걸린 물건
        /// (서비스·기억석·상자)만 켠다 — 배경 소품까지 넓히면 뒤에 선 진짜 대상의 호버를 가로챈다.
        /// </param>
        public void ConfigureRuntimePresentation(bool expandHoverToCell)
        {
            EnsureFadeTargets();
            EnsureHoverCollider(expandHoverToCell);
        }

        public void ApplyOcclusionFade(bool fade)
        {
            EnsureFadeTargets();
            foreach (var fadeTarget in fadeTargets)
            {
                if (fadeTarget != null)
                {
                    fadeTarget.ApplyFade(fade);
                }
            }
        }

        public void ApplyVisibilityDarkening(bool darken)
        {
            EnsureFadeTargets();
            foreach (var fadeTarget in fadeTargets)
            {
                if (fadeTarget != null)
                {
                    fadeTarget.ApplyVisibilityDarkening(darken);
                }
            }
        }

        public void SetPointerHovered(bool hovered)
        {
            IsPointerHovered = hovered;
            EnsureFadeTargets();
            foreach (var fadeTarget in fadeTargets)
            {
                if (fadeTarget != null)
                {
                    fadeTarget.SetPointerHovered(hovered);
                }
            }
        }

        private void EnsureFadeTargets()
        {
            fadeTargets.RemoveAll(target => target == null);
            if (fadeTargets.Count == 0)
            {
                GetComponentsInChildren(includeInactive: true, fadeTargets);
            }

            if (fadeTargets.Count == 0)
            {
                fadeTargets.Add(gameObject.AddComponent<MapObjectFadeTarget>());
            }
        }

        private void EnsureHoverCollider(bool expandHoverToCell)
        {
            if (GetComponent<Collider>() != null)
            {
                return;
            }

            var bounds = ResolveRendererBounds();
            var box = gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            if (!bounds.HasValue)
            {
                box.center = Vector3.zero;
                box.size = Vector3.one;
                return;
            }

            var worldBounds = bounds.Value;
            var localCenter = transform.InverseTransformPoint(worldBounds.center);
            var localMin = transform.InverseTransformPoint(worldBounds.min);
            var localMax = transform.InverseTransformPoint(worldBounds.max);
            box.center = localCenter;
            box.size = new Vector3(
                Mathf.Max(0.01f, Mathf.Abs(localMax.x - localMin.x)),
                Mathf.Max(0.01f, Mathf.Abs(localMax.y - localMin.y)),
                Mathf.Max(0.01f, Mathf.Abs(localMax.z - localMin.z)));

            if (!expandHoverToCell)
            {
                return;
            }

            // 🔑 <b>로컬</b> 최소치는 월드 배율로 나눠서 구한다 — 저작 스케일이 걸린 물건에
            //   로컬 1.2를 그대로 박으면 화면에서 배율만큼 커지거나 작아진다.
            var lossy = transform.lossyScale;
            var minX = Mathf.Approximately(lossy.x, 0f) ? box.size.x : MinimumHoverFootprint / Mathf.Abs(lossy.x);
            var minZ = Mathf.Approximately(lossy.z, 0f) ? box.size.z : MinimumHoverFootprint / Mathf.Abs(lossy.z);
            box.size = new Vector3(
                Mathf.Max(box.size.x, minX),
                box.size.y,
                Mathf.Max(box.size.z, minZ));
        }

        private Bounds? ResolveRendererBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0)
            {
                return null;
            }

            var hasBounds = false;
            var bounds = default(Bounds);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds ? (Bounds?)bounds : null;
        }
    }
}
