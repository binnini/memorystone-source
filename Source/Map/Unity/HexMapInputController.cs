using System;
using SeoulPlayup.Map.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace SeoulPlayup.Map.Unity
{
    public sealed class HexMapInputController : MonoBehaviour
    {
        public enum PresentationMode
        {
            ThreeD
        }

        [SerializeField] private Camera prototype3DCamera;
        [SerializeField] private AtlasTilePresentationView atlasTilePresentationView;
        [SerializeField] private PresentationMode activeMode = PresentationMode.ThreeD;
        private bool pointerPollingEnabled = true;

        public event Action<HexCoord> HexHovered;
        public event Action HoverCleared;
        public event Action<HexCoord> HexClicked;

        public void Configure3D(Camera camera, AtlasTilePresentationView atlasView)
        {
            prototype3DCamera = camera;
            atlasTilePresentationView = atlasView;
        }

        public void SetPresentationMode(PresentationMode mode)
        {
            activeMode = mode;
        }

        public void ConfigurePointerPollingForTests(bool enabled)
        {
            pointerPollingEnabled = enabled;
        }

        private void Update()
        {
            if (!TryPointerHex(out var coord))
            {
                HoverCleared?.Invoke();
                return;
            }

            HexHovered?.Invoke(coord);
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && !IsPointerOverUi())
            {
                HexClicked?.Invoke(coord);
            }
        }

        private static bool IsPointerOverUi()
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject(Mouse.current.deviceId))
            {
                return true;
            }

            return EventSystem.current.IsPointerOverGameObject();
        }

        public bool TryPointerHex(out HexCoord coord)
        {
            coord = default;
            if (!pointerPollingEnabled || Mouse.current == null)
            {
                return false;
            }

            var screen = Mouse.current.position.ReadValue();
            return TryRightPointerHex(screen, out coord);
        }

        private bool TryRightPointerHex(Vector2 screen, out HexCoord coord)
        {
            coord = default;
            if (prototype3DCamera == null || !prototype3DCamera.pixelRect.Contains(screen))
            {
                return false;
            }

            return atlasTilePresentationView != null && atlasTilePresentationView.TryScreenToHex(prototype3DCamera, screen, out coord);
        }
    }
}