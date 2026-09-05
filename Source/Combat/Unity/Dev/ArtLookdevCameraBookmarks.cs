using System;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Named camera bookmarks for the ArtLookdev scene. Apply/capture via the component
    /// context menu (right-click the component header) — no play mode or input needed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArtLookdevCameraBookmarks : MonoBehaviour
    {
        [Serializable]
        public struct Bookmark
        {
            public string label;
            public Vector3 position;
            public Vector3 eulerAngles;
        }

        [SerializeField] private Camera targetCamera;
        [SerializeField] private Bookmark[] bookmarks = Array.Empty<Bookmark>();

        public void Configure(Camera camera, Bookmark[] configuredBookmarks)
        {
            targetCamera = camera;
            bookmarks = configuredBookmarks ?? Array.Empty<Bookmark>();
        }

        [ContextMenu("Apply 1")] private void Apply1() { Apply(0); }
        [ContextMenu("Apply 2")] private void Apply2() { Apply(1); }
        [ContextMenu("Apply 3")] private void Apply3() { Apply(2); }
        [ContextMenu("Apply 4")] private void Apply4() { Apply(3); }
        [ContextMenu("Apply 5")] private void Apply5() { Apply(4); }
        [ContextMenu("Apply 6")] private void Apply6() { Apply(5); }
        [ContextMenu("Apply 7")] private void Apply7() { Apply(6); }
        [ContextMenu("Capture Current To 1")] private void Capture1() { Capture(0); }
        [ContextMenu("Capture Current To 2")] private void Capture2() { Capture(1); }
        [ContextMenu("Capture Current To 3")] private void Capture3() { Capture(2); }
        [ContextMenu("Capture Current To 4")] private void Capture4() { Capture(3); }
        [ContextMenu("Capture Current To 5")] private void Capture5() { Capture(4); }
        [ContextMenu("Capture Current To 6")] private void Capture6() { Capture(5); }
        [ContextMenu("Capture Current To 7")] private void Capture7() { Capture(6); }

        public void Apply(int index)
        {
            var camera = ResolveCamera();
            if (camera == null || index < 0 || index >= bookmarks.Length)
            {
                return;
            }

            camera.transform.SetPositionAndRotation(
                bookmarks[index].position,
                Quaternion.Euler(bookmarks[index].eulerAngles));
        }

        private void Capture(int index)
        {
            var camera = ResolveCamera();
            if (camera == null || index < 0 || index >= bookmarks.Length)
            {
                return;
            }

            bookmarks[index].position = camera.transform.position;
            bookmarks[index].eulerAngles = camera.transform.rotation.eulerAngles;
        }

        private Camera ResolveCamera()
        {
            return targetCamera != null ? targetCamera : Camera.main;
        }
    }
}
