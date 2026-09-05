using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// VFX 큐 하나가 놓일 최종 월드 포즈. 산출은
    /// <see cref="EffectPresentationController.ResolveSpawnPose"/> 하나뿐이며, 런타임 스폰과
    /// VFX 랩·스틸 캡처가 모두 그 함수를 통과한다 — 배치가 두 벌로 갈라지지 않게 하는 계약이다.
    /// </summary>
    public readonly struct VfxSpawnPose
    {
        public VfxSpawnPose(Vector3 position, Quaternion rotation, float uniformScale, Vector3 axisScale)
        {
            Position = position;
            Rotation = rotation;
            UniformScale = uniformScale;
            AxisScale = axisScale;
        }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        /// <summary>반지름 성장(<c>scaleWithRadius</c>)까지 반영된 균일 스케일.</summary>
        public float UniformScale { get; }

        /// <summary>
        /// 비균일 축 배율. <see cref="Vector3.one"/>이 아니면 <b>회전 없는 자식 피벗</b>에 걸어야 한다 —
        /// 회전한 트랜스폼에 그대로 곱하면 전단이 생긴다.
        /// </summary>
        public Vector3 AxisScale { get; }

        public bool HasAxisScale => AxisScale != Vector3.one;
    }
}
