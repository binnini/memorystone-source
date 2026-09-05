using System.Linq;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Renders a real shipped map source inside the ArtLookdev scene using the actual game map
    /// renderer (<see cref="AtlasTilePresentationView"/>) — tiles, chunk batching, map objects and
    /// the visibility/fog path are all the real pipeline. Rendering happens on play-mode Start so
    /// the scene file stays small (the real game also generates its map at runtime).
    ///
    /// Two play-mode toggles (on-screen buttons) let art tuning inspect the map without running
    /// combat: a fog toggle (all-revealed for pure look tuning vs. fogged for atmosphere), and a
    /// player-vision toggle that spawns the Player prefab and reveals only the cells within
    /// <see cref="playerVisionRadius"/> of it — everything else falls to the fog/암시야 tiles, exactly
    /// like the real game's start-of-run visibility.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArtLookdevRealMapController : MonoBehaviour
    {
        [SerializeField] private AtlasTilePresentationView view;
        [SerializeField] private HexSparseMapAuthoringSource mapSource;
        [Header("암시야 (플레이어 없이 맵 중심 기준)")]
        [Tooltip("플레이어가 없을 때 암시야가 켜지면 맵 중심에서 이 반경까지 밝힌다.")]
        [SerializeField] private int centerRevealRadius = 6;
        [SerializeField] private int centerHintedRingWidth = 2;
        [Header("플레이어 시야 모드")]
        [SerializeField] private GameObject playerPrefab;
        [Tooltip("플레이어 소환 시 밝혀지는 시야 반경(칸). 실제 게임 시작 시야와 맞춘 기본값 2.")]
        [SerializeField] private int playerVisionRadius = 2;
        [Tooltip("플레이어 시야 바깥에 Unknown 앞 Hinted 링을 몇 칸 둘지(0=바로 Unknown).")]
        [SerializeField] private int playerHintedRingWidth = 1;

        private HexMapData mapData;
        private HexCoord centerCoord;
        private bool rendered;

        // 암시야 토글: false면 전부 Revealed(순수 룩 튜닝), true면 시야 규칙 적용.
        private bool fogEnabled;
        private GameObject playerInstance;
        private HexCoord playerCoord;

        private bool PlayerActive => playerInstance != null;

        private void Start()
        {
            if (view == null || mapSource == null)
            {
                Debug.LogWarning("ArtLookdevRealMapController: view/mapSource 미지정 — 실제 맵 렌더를 건너뜀.");
                return;
            }

            if (!mapSource.TryToHexMapData(out mapData, out var error))
            {
                Debug.LogWarning($"ArtLookdevRealMapController: 맵 변환 실패 — {error}");
                return;
            }

            view.Render(mapData);
            centerCoord = ComputeCentroid(mapData);
            playerCoord = ResolvePlayerStartCoord();
            rendered = true;
            ApplyVisibility();
            FrameMainCameraOnMap();
        }

        private void OnGUI()
        {
            if (!rendered)
            {
                return;
            }

            const float width = 220f;
            const float height = 34f;
            var x = Screen.width - width - 12f;
            var style = new GUIStyle(GUI.skin.button) { fontSize = 13 };

            var fogLabel = fogEnabled ? "암시야: 켜짐 → 끄기" : "암시야: 꺼짐 → 켜기";
            if (GUI.Button(new Rect(x, 12f, width, height), fogLabel, style))
            {
                fogEnabled = !fogEnabled;
                ApplyVisibility();
            }

            var playerLabel = PlayerActive ? "플레이어 해제" : "플레이어 소환";
            var visibilityModeLabel = view.VisibilityPresentationMode == VisibilityPresentationMode.OverlayTint
                ? "Visibility: Overlay -> Lighting Mask"
                : "Visibility: Lighting Mask -> Overlay";
            if (GUI.Button(new Rect(x, 12f + height + 6f, width, height), visibilityModeLabel, style))
            {
                ToggleVisibilityPresentation();
            }

            if (GUI.Button(new Rect(x, 12f + (height + 6f) * 2f, width, height), playerLabel, style))
            {
                TogglePlayer();
            }

            if (PlayerActive)
            {
                var hint = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.UpperRight };
                GUI.Label(new Rect(x - 40f, 12f + (height + 6f) * 3f, width + 40f, 20f),
                    $"플레이어 주위 {playerVisionRadius}칸 시야 (암시야 자동 ON)", hint);
            }
        }

        /// <summary>Fog-of-war (암시야) state. Exposed for the A/B cost-attribution sweep.</summary>
        public bool FogEnabled => fogEnabled;

        /// <summary>The real map renderer, so the sweep can read the active visibility mode.</summary>
        public AtlasTilePresentationView View => view;

        /// <summary>Scenario hook (A/B sweep): set fog-of-war and re-apply visibility. No-op until rendered.</summary>
        public void SetFogEnabled(bool on)
        {
            if (fogEnabled == on)
            {
                return;
            }

            fogEnabled = on;
            ApplyVisibility();
        }

        [ContextMenu("Toggle Visibility Presentation")]
        public void ToggleVisibilityPresentation()
        {
            if (view == null)
            {
                return;
            }

            var nextMode = view.VisibilityPresentationMode == VisibilityPresentationMode.OverlayTint
                ? VisibilityPresentationMode.LightingMask
                : VisibilityPresentationMode.OverlayTint;
            view.SetVisibilityPresentationMode(nextMode);
            ApplyVisibility();
        }

        [ContextMenu("Toggle Player")]
        public void TogglePlayer()
        {
            if (PlayerActive)
            {
                Destroy(playerInstance);
                playerInstance = null;
                ApplyVisibility();
                return;
            }

            if (playerPrefab == null)
            {
                Debug.LogWarning("ArtLookdevRealMapController: playerPrefab 미지정 — 플레이어 소환 불가.");
                return;
            }

            playerInstance = Instantiate(playerPrefab, view.transform);
            playerInstance.name = "ArtLookdev Player";
            playerInstance.transform.position = view.transform.TransformPoint(view.ProjectTop(playerCoord));

            // 플레이어를 소환하면 시야를 보여주는 게 목적이므로 암시야를 자동으로 켠다.
            fogEnabled = true;
            ApplyVisibility();
            FrameMainCameraOnPlayer();
        }

        [ContextMenu("Apply Visibility")]
        public void ApplyVisibility()
        {
            if (!rendered)
            {
                return;
            }

            view.ApplyVisibility(ResolveCellInfo);
        }

        [ContextMenu("Frame Main Camera On Map")]
        public void FrameMainCameraOnMap()
        {
            FrameCamera(rendered ? view.transform.TransformPoint(view.Project(centerCoord)) : view.transform.position, 30f, 28f);
        }

        private void FrameMainCameraOnPlayer()
        {
            if (rendered)
            {
                FrameCamera(view.transform.TransformPoint(view.ProjectTop(playerCoord)), 14f, 12f);
            }
        }

        private void FrameCamera(Vector3 focus, float up, float back)
        {
            var camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            camera.transform.SetPositionAndRotation(
                focus + new Vector3(0f, up, -back),
                Quaternion.Euler(45f, 0f, 0f));
        }

        private HexCoord ResolvePlayerStartCoord()
        {
            // 실제 맵의 PlayerSpawn을 우선 사용하고, 없으면 중심에서 가장 가까운 걷기 가능 셀.
            // HexMapObjectData는 struct라 null 비교 대신 Any/First로 존재를 확인한다.
            var objectRefs = mapData.ObjectRefs;
            if (objectRefs != null && objectRefs.Any(objectRef => objectRef.IsPlayerSpawn))
            {
                return objectRefs.First(objectRef => objectRef.IsPlayerSpawn).Coord;
            }

            HexCoord best = centerCoord;
            var bestDistance = int.MaxValue;
            var found = false;
            foreach (var cell in mapData.AllCells)
            {
                if (!cell.BaseWalkable)
                {
                    continue;
                }

                var distance = cell.Coord.DistanceTo(centerCoord);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = cell.Coord;
                    found = true;
                }
            }

            return found ? best : centerCoord;
        }

        private static HexCoord ComputeCentroid(HexMapData map)
        {
            long q = 0;
            long r = 0;
            long count = 0;
            foreach (var cell in map.AllCells)
            {
                q += cell.Coord.Q;
                r += cell.Coord.R;
                count++;
            }

            return count == 0 ? new HexCoord(0, 0) : new HexCoord((int)(q / count), (int)(r / count));
        }

        private HexVisibilitySafeCellInfo ResolveCellInfo(HexCoord coord)
        {
            if (mapData == null || !mapData.TryGetCell(coord, out var cell))
            {
                return HexVisibilitySafeCellInfo.Missing(coord);
            }

            var visibility = ResolveVisibility(coord);
            return new HexVisibilitySafeCellInfo(
                coord,
                visibility,
                true,
                visibility != HexCellVisibility.Unknown,
                visibility == HexCellVisibility.Revealed,
                cell.TileDefinitionId,
                cell.TerrainTypeId,
                cell.BaseMoveCost,
                cell.BaseWalkable,
                cell.BaseBlocksVision,
                cell.EventId,
                cell.LandmarkId,
                cell.VisualFloor);
        }

        private HexCellVisibility ResolveVisibility(HexCoord coord)
        {
            if (!fogEnabled)
            {
                return HexCellVisibility.Revealed;
            }

            if (PlayerActive)
            {
                return RingVisibility(coord.DistanceTo(playerCoord), playerVisionRadius, playerHintedRingWidth);
            }

            return RingVisibility(coord.DistanceTo(centerCoord), centerRevealRadius, centerHintedRingWidth);
        }

        private static HexCellVisibility RingVisibility(int distance, int revealRadius, int hintedRingWidth)
        {
            if (distance <= revealRadius)
            {
                return HexCellVisibility.Revealed;
            }

            return distance <= revealRadius + Mathf.Max(0, hintedRingWidth)
                ? HexCellVisibility.Hinted
                : HexCellVisibility.Unknown;
        }
    }
}
