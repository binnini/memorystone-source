using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Drives a static VFX anchor node (a <see cref="CharacterActorVisual"/> anchor such as AttackSource)
    /// with an animated rig bone's pose, so followSourceAnchor cues can track the animation (e.g. the
    /// Bulgasal breath cone sweeping with its Head bone).
    ///
    /// <para>Deliberately a pose copy, not a SetParent under the bone: rig bones can carry scale/skew,
    /// and parenting would leak that into the anchor (and everything spawned from it). Position and
    /// rotation are copied; the anchor's scale is never touched.</para>
    ///
    /// <para>Runs at execution order -100 so the anchor is already posed when same-frame LateUpdate
    /// consumers (AnchorDeltaFollower on spawned VFX) read it.</para>
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class VfxAnchorBoneFollower : MonoBehaviour
    {
        [Tooltip("Animated rig bone to follow (e.g. the Head bone inside the model instance).")]
        [SerializeField] private Transform bone;

        [Tooltip("Offset from the bone, expressed in the bone's rotated space (scale-independent).")]
        [SerializeField] private Vector3 positionOffset;

        [Tooltip("Extra local rotation applied after the bone's rotation.")]
        [SerializeField] private Vector3 rotationOffsetEuler;

        private void LateUpdate()
        {
            if (bone == null)
            {
                return;
            }

            transform.SetPositionAndRotation(
                bone.position + bone.rotation * positionOffset,
                bone.rotation * Quaternion.Euler(rotationOffsetEuler));
        }
    }
}
