using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>Scene-only gizmo overlay for validating CharacterActorVisual VFX anchors.</summary>
    public sealed class VfxAnchorDebugView : MonoBehaviour
    {
        [SerializeField] private CharacterActorVisual visual;
        [SerializeField] private bool showAnchors = true;
        [SerializeField] private float sphereRadius = 0.08f;
        [SerializeField] private float forwardLineLength = 0.55f;

        public bool ShowAnchors
        {
            get => showAnchors;
            set => showAnchors = value;
        }

        public CharacterActorVisual Visual
        {
            get => ResolveVisual();
            set => visual = value;
        }

        private void Reset()
        {
            visual = GetComponentInChildren<CharacterActorVisual>();
        }

        private void OnValidate()
        {
            if (sphereRadius <= 0f)
            {
                sphereRadius = 0.08f;
            }

            if (forwardLineLength <= 0f)
            {
                forwardLineLength = 0.55f;
            }
        }

        private void OnDrawGizmos()
        {
            if (!showAnchors)
            {
                return;
            }

            var target = ResolveVisual();
            if (target == null)
            {
                return;
            }

            DrawAnchor(target, CharacterVfxAnchorKind.Root, Color.white, "Root");
            DrawAnchor(target, CharacterVfxAnchorKind.Ground, Color.green, "Ground");
            DrawAnchor(target, CharacterVfxAnchorKind.HitCenter, Color.red, "HitCenter");
            DrawAnchor(target, CharacterVfxAnchorKind.Head, Color.blue, "Head");
            DrawAnchor(target, CharacterVfxAnchorKind.AttackSource, Color.yellow, "AttackSource", drawForward: true);
        }

        private CharacterActorVisual ResolveVisual()
        {
            if (visual == null)
            {
                visual = GetComponentInChildren<CharacterActorVisual>();
            }

            return visual;
        }

        private void DrawAnchor(CharacterActorVisual target, CharacterVfxAnchorKind kind, Color color, string label, bool drawForward = false)
        {
            if (!target.TryGetVfxAnchor(kind, out var anchor) || anchor == null)
            {
                return;
            }

            Gizmos.color = color;
            Gizmos.DrawSphere(anchor.position, sphereRadius);
            Gizmos.DrawWireSphere(anchor.position, sphereRadius * 1.8f);

            if (drawForward)
            {
                Gizmos.DrawLine(anchor.position, anchor.position + anchor.forward * forwardLineLength);
            }

#if UNITY_EDITOR
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.Label(anchor.position + Vector3.up * (sphereRadius * 2.5f), label);
#endif
        }
    }
}
