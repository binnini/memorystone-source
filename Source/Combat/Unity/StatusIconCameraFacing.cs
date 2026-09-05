using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Keeps a tile status-icon readable from the current camera. Two modes:
    /// <list type="bullet">
    /// <item><b>faceCamera off(출하값)</b> — 아이콘은 타일에 눕힌 데칼로 남되, 매 프레임 카메라 yaw에
    /// 맞춰 바닥 회전만 따라간다(2026-08-20 #15). 예전에는 월드 고정 오일러(90,0,0)로 박아 두어
    /// ①카메라를 돌리면 화면상 회전되어 보였고 ②앞면이 바닥을 향해 뒷면(좌우 반전)이 보였다 —
    /// 셰이더 Cull Off가 그 반전을 가려 주지도 않았다. 지금은 앞면을 위로 세우고(LookRotation up)
    /// 화면 위쪽 = 카메라 수평 전방으로 정렬하므로 어느 yaw에서도 항상 정립이다.</item>
    /// <item><b>faceCamera on</b> — 카메라 회전을 통째로 따라가는 빌보드(레거시 경로, 출하 씬 미사용).</item>
    /// </list>
    /// Works with both the legacy camera and a Cinemachine-driven camera: an explicitly wired camera
    /// (<see cref="SetCamera"/> — 호버 레이캐스트와 같은 카메라) wins, then <see cref="Camera.main"/>,
    /// then <see cref="Camera.current"/>.
    /// </summary>
    public sealed class StatusIconCameraFacing : MonoBehaviour
    {
        private bool enabledFacing;
        private Vector3 fallbackEulerAngles;
        private Camera explicitCamera;

        public void Configure(bool faceCamera, Vector3 eulerAngles)
        {
            enabledFacing = faceCamera;
            fallbackEulerAngles = eulerAngles;
            AlignNow();
        }

        /// <summary>호버 판정과 같은 카메라를 쓰도록 명시 배선(없으면 Camera.main 폴백).</summary>
        public void SetCamera(Camera camera)
        {
            explicitCamera = camera;
            AlignNow();
        }

        /// <summary>스폰 프레임에 한 프레임짜리 잘못된 방향이 보이지 않도록 즉시 정렬.</summary>
        public void AlignNow()
        {
            var cam = ResolveCamera();
            if (cam == null)
            {
                // 카메라가 아직 없으면 최소한 앞면이 위를 보는 눕힘으로 — 옛 (90,0,0)은 뒷면이 보였다.
                transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                return;
            }

            Align(cam);
        }

        private void LateUpdate()
        {
            var cam = ResolveCamera();
            if (cam == null)
            {
                return;
            }

            Align(cam);
        }

        private void Align(Camera cam)
        {
            if (enabledFacing)
            {
                transform.rotation = cam.transform.rotation;
                return;
            }

            var flatForward = cam.transform.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f)
            {
                // 수직 부감(전방 수평 성분 0)에서는 yaw가 정의되지 않는다 — 저작 폴백 각으로 핀.
                transform.rotation = Quaternion.Euler(fallbackEulerAngles);
                return;
            }

            // 앞면(+Z)을 위로, 아이콘의 위(+Y)를 카메라 수평 전방으로: 부감 카메라의 화면 위쪽은
            // 지면에서 카메라 전방 수평이므로, 어느 yaw에서도 아이콘이 화면 기준 정립으로 읽힌다.
            transform.rotation = Quaternion.LookRotation(Vector3.up, flatForward.normalized);
        }

        private Camera ResolveCamera()
        {
            if (explicitCamera != null)
            {
                return explicitCamera;
            }

            return Camera.main != null ? Camera.main : Camera.current;
        }
    }
}
