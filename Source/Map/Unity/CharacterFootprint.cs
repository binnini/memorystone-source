using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// 캐릭터 모델의 「몸집」 실측 한 곳(2026-08-20 WS-2). 호버 판정 상자
    /// (<see cref="CharacterHoverTarget"/>)와 상태이상 바닥 링의 비례 스케일이 <b>같은 자로</b> 재도록
    /// 측정 코드를 여기 하나에 모은다 — 각자 재면 반드시 어긋난다(보스 아우라 링이
    /// <c>CombatActorMarkerPresenter.SetMarkerAuraRing</c>에서 판정 상자를 재사용하는 것과 같은 이유).
    ///
    /// <para>🔑 월드 AABB(<c>Renderer.bounds</c>)를 쓰면 안 된다. 캐릭터는 바라보는 방향으로 도는데,
    /// 비스듬히 선 모델의 월드 AABB는 이미 부풀어 있다 — 길쭉한 사족보행 몬스터에서 링이 실루엣보다
    /// 눈에 띄게 커진다. 렌더러 로컬 바운즈의 8코너를 측정 공간으로 옮겨 감싸면 회전과 무관하게 딱 맞는다.</para>
    /// </summary>
    public static class CharacterFootprint
    {
        /// <summary>
        /// 모델의 바운즈를 <paramref name="measureSpace"/>의 로컬 공간에서 직접 모은다.
        ///
        /// <para>🔴 스킨드 메시의 <c>localBounds</c>는 렌더러가 아니라 <b>루트 본</b> 공간이다. 렌더러
        /// 행렬을 쓰면 리그의 본 스케일(FBX는 0.01 같은 값이 흔하다)만큼 상자가 쪼그라들어 모델이 통째로
        /// 사라진다(실제로 밟았다 — 전 몬스터 호버 0% 반응).</para>
        /// </summary>
        public static bool TryResolveLocalBounds(Transform measureSpace, Transform visualRoot, out Bounds localBounds)
        {
            localBounds = default;
            if (measureSpace == null || visualRoot == null)
            {
                return false;
            }

            var renderers = visualRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0)
            {
                return false;
            }

            var toMeasureSpace = measureSpace.worldToLocalMatrix;
            var hasBounds = false;
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                var localToWorld = renderer is SkinnedMeshRenderer skinned && skinned.rootBone != null
                    ? skinned.rootBone.localToWorldMatrix
                    : renderer.localToWorldMatrix;
                var matrix = toMeasureSpace * localToWorld;
                var bounds = renderer.localBounds;
                var center = bounds.center;
                var extents = bounds.extents;
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = matrix.MultiplyPoint3x4(new Vector3(
                        center.x + ((corner & 1) == 0 ? -extents.x : extents.x),
                        center.y + ((corner & 2) == 0 ? -extents.y : extents.y),
                        center.z + ((corner & 4) == 0 ? -extents.z : extents.z)));

                    if (!hasBounds)
                    {
                        localBounds = new Bounds(point, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                    {
                        localBounds.Encapsulate(point);
                    }
                }
            }

            return hasBounds;
        }

        /// <summary>
        /// 모델의 <b>발자국 지름</b>(월드 단위) = 가로 두 축 중 큰 쪽 × 월드 배율. 보스 페이즈 배율처럼
        /// 트랜스폼에 얹힌 확대도 그대로 들어온다(그게 화면에 보이는 크기이므로).
        /// </summary>
        public static bool TryResolveDiameter(Transform visualRoot, out float diameter)
        {
            diameter = 0f;
            if (visualRoot == null || !TryResolveLocalBounds(visualRoot, visualRoot, out var bounds))
            {
                return false;
            }

            var lossy = visualRoot.lossyScale;
            var widest = Mathf.Max(bounds.size.x * Mathf.Abs(lossy.x), bounds.size.z * Mathf.Abs(lossy.z));
            if (!(widest > 0f))
            {
                return false;
            }

            diameter = widest;
            return true;
        }
    }
}
