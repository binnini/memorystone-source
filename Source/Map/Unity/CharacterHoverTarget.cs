using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// 캐릭터 마커(몬스터·터렛·보스 기물)의 마우스 판정 상자. 상자를 대상 자기 렌더러 크기에서
    /// 뽑으므로 보스에는 보스만 한, 터렛에는 터렛만 한 상자가 생긴다 — 화면 고정 반경으로 재던
    /// 옛 방식(60px 원)이 큰 대상에서 몸통 대부분을 놓치고 작은 대상에서는 몸보다 넓게 반응하던
    /// 양방향 오차가 상수 튜닝 없이 사라진다(docs/object-outline-aura-plan.md §5.0).
    ///
    /// 🔑 상자는 <b>모델이 아니라 마커 루트</b>에 붙는다. 모델 하위는
    /// <see cref="CharacterActorVisual.ApplyRaycastSafeVisualPolicy"/>가 Ignore Raycast 레이어로
    /// 내리고 콜라이더를 전부 끄므로, 모델 쪽에 붙이면 레이캐스트에 영영 잡히지 않는다.
    /// 마커 루트는 그 정책의 재귀 범위 밖(모델의 부모)이라 손대지 않아도 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterHoverTarget : MonoBehaviour
    {
        /// <summary>모델이 없어 크기를 잴 수 없을 때 쓰는 상자 한 변(월드 유닛). 셀 하나보다 작다.</summary>
        private const float FallbackBoxSize = 1f;

        private const float MinBoxExtent = 0.01f;

        [SerializeField] private string markerId;
        [SerializeField] private BoxCollider hoverCollider;

        // 마지막으로 잰 모델과 그때의 월드 배율. 뷰 갱신마다 Resize가 불리는데 실제로 크기가
        // 달라지는 경우는 드물다(보스 페이즈 성장뿐) — 같은 값이면 렌더러 순회를 건너뛴다.
        private Transform measuredVisualRoot;
        private Vector3 measuredLossyScale;
        private bool hasMeasured;

        /// <summary>이 상자가 대변하는 마커 id(= 몬스터 id). 호버 판정이 이걸로 대상을 되찾는다.</summary>
        public string MarkerId => markerId;

        /// <summary>
        /// 마커 루트에 판정 상자를 보장하고 모델 크기에 맞춰 다시 잰다. 반복 호출이 멱등이다.
        /// </summary>
        /// <param name="markerRoot">마커 루트(모델의 부모).</param>
        /// <param name="markerId">몬스터 id.</param>
        /// <param name="visualRoot">크기를 잴 모델 루트. null이면 폴백 상자를 쓴다.</param>
        public static CharacterHoverTarget Ensure(GameObject markerRoot, string markerId, Transform visualRoot)
        {
            if (markerRoot == null)
            {
                return null;
            }

            var target = markerRoot.GetComponent<CharacterHoverTarget>();
            if (target == null)
            {
                target = markerRoot.AddComponent<CharacterHoverTarget>();
            }

            target.markerId = markerId;
            target.Resize(visualRoot);
            return target;
        }

        /// <summary>
        /// 모델의 현재 렌더러 바운즈에서 상자를 다시 잰다. 배율이 바뀌는 대상(보스 페이즈 성장)은
        /// 배율을 적용한 <b>뒤에</b> 불러야 상자가 따라온다.
        /// </summary>
        public void Resize(Transform visualRoot)
        {
            EnsureCollider();
            if (hoverCollider == null)
            {
                return;
            }

            var lossyScale = visualRoot != null ? visualRoot.lossyScale : Vector3.zero;
            if (hasMeasured
                && ReferenceEquals(measuredVisualRoot, visualRoot)
                && (measuredLossyScale - lossyScale).sqrMagnitude <= 1e-8f)
            {
                return;
            }

            measuredVisualRoot = visualRoot;
            measuredLossyScale = lossyScale;
            hasMeasured = true;

            if (!TryResolveLocalBounds(visualRoot, out var localBounds))
            {
                hoverCollider.center = Vector3.zero;
                hoverCollider.size = Vector3.one * FallbackBoxSize;
                return;
            }

            var size = localBounds.size;
            hoverCollider.center = localBounds.center;
            hoverCollider.size = new Vector3(
                Mathf.Max(MinBoxExtent, size.x),
                Mathf.Max(MinBoxExtent, size.y),
                Mathf.Max(MinBoxExtent, size.z));
        }

        private void EnsureCollider()
        {
            if (hoverCollider != null)
            {
                return;
            }

            hoverCollider = GetComponent<BoxCollider>();
            if (hoverCollider == null)
            {
                hoverCollider = gameObject.AddComponent<BoxCollider>();
            }

            // trigger로 두어 물리 충돌에는 참여하지 않게 한다. 호버 레이캐스트는
            // QueryTriggerInteraction.Collide로 쏘므로 잡힌다.
            hoverCollider.isTrigger = true;
        }

        /// <summary>
        /// 모델의 바운즈를 <b>마커 로컬 공간에서 직접</b> 모은다. 실측은
        /// <see cref="CharacterFootprint.TryResolveLocalBounds"/> 한 곳에 모여 있다 — 상태이상 바닥 링의
        /// 비례 스케일도 같은 자를 쓰므로, 재는 방법이 갈라지면 링과 판정 상자가 조용히 어긋난다.
        /// </summary>
        private bool TryResolveLocalBounds(Transform visualRoot, out Bounds localBounds)
        {
            return CharacterFootprint.TryResolveLocalBounds(transform, visualRoot, out localBounds);
        }
    }
}
