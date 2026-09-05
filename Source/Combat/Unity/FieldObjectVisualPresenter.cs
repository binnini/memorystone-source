using System;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEngine;
using UnityEngine.Rendering;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Inspector-friendly mapping of a field object's <see cref="FieldObject.VisualRef"/> (typically the
    /// originating card id, e.g. "F02" = 신성한 램프) to a dedicated prefab. Unmapped field objects fall
    /// back to a Kind-coloured cylinder marker.
    /// </summary>
    [Serializable]
    public sealed class FieldObjectVisualMapping
    {
        // Field names are kept as-is: scenes/prefabs serialize these by name.
        [SerializeField] private string visualRef;
        [SerializeField] private GameObject prefab;

        public string VisualRef => visualRef;
        public GameObject Prefab => prefab;
    }

    /// <summary>
    /// Spawns and maintains one persistent visual marker per placed field object, keyed by tile coord.
    /// Pure projection of <see cref="FieldObjectRegistry"/>: <see cref="Sync"/> diffs the live registry
    /// against spawned markers (spawn new, despawn expired/removed, refresh metadata on survivors), so the
    /// runtime registry stays the single source of truth. Mapped visualRefs (신성한 램프 → Lantern) use a
    /// prefab; everything else uses a runtime cylinder tinted by <see cref="FieldObjectKind"/>.
    /// </summary>
    internal sealed class FieldObjectVisualPresenter : MonoBehaviour
    {
        private sealed class Marker
        {
            public GameObject Root;
            public FieldObjectMarkerView View;
            public string VisualRef;
            public FieldObjectKind Kind;
        }

        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        private AtlasTilePresentationView atlasView;
        private readonly Dictionary<string, GameObject> prefabByVisualRef =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<HexCoord, Marker> markers = new Dictionary<HexCoord, Marker>();
        private readonly Dictionary<FieldObjectKind, Material> cylinderMaterials =
            new Dictionary<FieldObjectKind, Material>();
        private readonly List<HexCoord> removalScratch = new List<HexCoord>();

        internal static bool TryResolveDefaultObjectRef(string visualRef, out string objectRef)
        {
            objectRef = null;
            if (string.IsNullOrWhiteSpace(visualRef))
            {
                return false;
            }

            switch (visualRef.Trim())
            {
                case ApprovedCardCatalogFactory.FieldFirebombId:
                    objectRef = "bomb";
                    return true;
                case ApprovedCardCatalogFactory.FieldSacredCampfireId:
                    objectRef = "Lantern_5000";
                    return true;
                case ApprovedCardCatalogFactory.FieldFlashbangId:
                    objectRef = "flashbang";
                    return true;
                default:
                    return false;
            }
        }

        public void Configure(AtlasTilePresentationView view, IEnumerable<KeyValuePair<string, GameObject>> mappings)
        {
            atlasView = view;
            prefabByVisualRef.Clear();
            if (mappings != null)
            {
                foreach (var pair in mappings)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key) && pair.Value != null)
                    {
                        prefabByVisualRef[pair.Key.Trim()] = pair.Value;
                    }
                }
            }
        }

        public void Sync(IReadOnlyList<FieldObject> objects)
        {
            if (atlasView == null)
            {
                return;
            }

            var live = HashSetPool();
            if (objects != null)
            {
                foreach (var fieldObject in objects)
                {
                    if (fieldObject.IsExpired || IsGroundState(fieldObject))
                    {
                        continue;
                    }

                    live.Add(fieldObject.Position);

                    if (markers.TryGetValue(fieldObject.Position, out var existing))
                    {
                        var identityChanged = existing.Kind != fieldObject.Kind ||
                            !string.Equals(existing.VisualRef ?? string.Empty, fieldObject.VisualRef ?? string.Empty, StringComparison.Ordinal);
                        if (!identityChanged)
                        {
                            existing.View.SetData(fieldObject); // refresh remaining turns / value
                            continue;
                        }

                        DestroyMarker(existing);
                        markers.Remove(fieldObject.Position);
                    }

                    if (!TryResolveWorld(fieldObject.Position, out var world))
                    {
                        continue;
                    }

                    markers[fieldObject.Position] = Spawn(fieldObject, world);
                }
            }

            removalScratch.Clear();
            foreach (var pair in markers)
            {
                if (!live.Contains(pair.Key))
                {
                    removalScratch.Add(pair.Key);
                }
            }

            foreach (var coord in removalScratch)
            {
                DestroyMarker(markers[coord]);
                markers.Remove(coord);
            }
        }

        public void Clear()
        {
            foreach (var marker in markers.Values)
            {
                DestroyMarker(marker);
            }

            markers.Clear();
        }

        private void OnDisable()
        {
            Clear();
        }

        private HashSet<HexCoord> reusableLiveSet;

        private HashSet<HexCoord> HashSetPool()
        {
            if (reusableLiveSet == null)
            {
                reusableLiveSet = new HashSet<HexCoord>();
            }
            else
            {
                reusableLiveSet.Clear();
            }

            return reusableLiveSet;
        }

        /// <summary>
        /// 이 장판이 <b>지형 상태</b>인가 — 그렇다면 여기서 아무것도 세우지 않는다(2026-09-01 #18).
        ///
        /// <para>🔴 두억시니의 파열·둔화 지대는 「누가 뭘 놓은 것」이 아니라 <b>그 칸의 땅이 부서진
        /// 것</b>이다(사용자 정정). 실린더나 프리팹을 칸 위에 세우면 오브젝트로 읽히고, 무엇보다
        /// 「부수면 없어지는 물건」처럼 보인다. 표현은 타일 자신이 맡는다 —
        /// <c>HexOverlayLayer.RupturedGround</c>가 <c>CombatState.GetRupturedGroundCells</c>를 그린다.</para>
        ///
        /// <para>여기서 <b>빼는 것</b>이 맞는 이유: 이 프리젠터가 계속 마커를 세우면 오버레이와 겹쳐
        /// 두 벌로 보인다. 소멸 처리는 아래 정리 루프가 알아서 한다(살아 있는 목록에서 빠졌으므로).</para>
        /// </summary>
        private static bool IsGroundState(FieldObject fieldObject)
        {
            return fieldObject.Kind == FieldObjectKind.StatusZone;
        }

        private bool TryResolveWorld(HexCoord coord, out Vector3 world)
        {
            if (atlasView == null)
            {
                world = default;
                return false;
            }

            world = atlasView.transform.TransformPoint(atlasView.ProjectOverlaySurface(coord));
            return true;
        }

        private Marker Spawn(FieldObject fieldObject, Vector3 world)
        {
            var visualRef = fieldObject.VisualRef ?? string.Empty;
            GameObject root;
            if (TryResolvePrefab(visualRef, out var prefab))
            {
                root = Instantiate(prefab, transform);
                root.transform.position = world;
            }
            else
            {
                root = CreateCylinder(fieldObject.Kind, world);
            }

            root.name = $"FieldObject_{fieldObject.Kind}_{fieldObject.Position}";

            var view = root.GetComponent<FieldObjectMarkerView>();
            if (view == null)
            {
                view = root.AddComponent<FieldObjectMarkerView>();
            }

            view.SetData(fieldObject);

            // Reuse the existing map-object hover/fade controller: it ensures a hover collider (needed for
            // the tooltip raycast) and occlusion fade, matching every other placed map object.
            MapObjectVisualController.Ensure(root);

            return new Marker
            {
                Root = root,
                View = view,
                VisualRef = visualRef,
                Kind = fieldObject.Kind,
            };
        }

        private bool TryResolvePrefab(string visualRef, out GameObject prefab)
        {
            prefab = null;
            if (!string.IsNullOrWhiteSpace(visualRef) &&
                prefabByVisualRef.TryGetValue(visualRef.Trim(), out prefab) &&
                prefab != null)
            {
                return true;
            }

            if (!TryResolveDefaultObjectRef(visualRef, out var objectRef))
            {
                return false;
            }

            return MapObjectCatalogSet.TryResolveDefault(objectRef, out prefab) && prefab != null;
        }

        private GameObject CreateCylinder(FieldObjectKind kind, Vector3 world)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.transform.SetParent(transform, false);

            var tileRadius = atlasView != null ? atlasView.TileRadius : 1f;
            var diameter = Mathf.Max(0.1f, tileRadius * 0.5f);
            var halfHeight = Mathf.Max(0.1f, tileRadius * 0.45f);
            go.transform.localScale = new Vector3(diameter, halfHeight, diameter);
            // Cylinder mesh spans y in [-1, 1] before scale, so lift by halfHeight to seat its base on the tile.
            go.transform.position = world + Vector3.up * halfHeight;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = GetCylinderMaterial(kind);
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return go;
        }

        private Material GetCylinderMaterial(FieldObjectKind kind)
        {
            if (cylinderMaterials.TryGetValue(kind, out var cached) && cached != null)
            {
                return cached;
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Standard") ??
                         Shader.Find("Sprites/Default");
            var color = ResolveKindColor(kind);
            var material = new Material(shader) { name = $"FieldObjectMarker_{kind}", color = color };
            if (material.HasProperty(BaseColorPropertyId))
            {
                material.SetColor(BaseColorPropertyId, color);
            }

            if (material.HasProperty(ColorPropertyId))
            {
                material.SetColor(ColorPropertyId, color);
            }

            cylinderMaterials[kind] = material;
            return material;
        }

        private static Color ResolveKindColor(FieldObjectKind kind)
        {
            switch (kind)
            {
                case FieldObjectKind.FieldDamage:
                    return new Color(0.85f, 0.20f, 0.16f); // 피해 = 적색
                case FieldObjectKind.MassImmobilize:
                    return new Color(0.20f, 0.45f, 0.90f); // 섬광/군중제어 = 청색
                case FieldObjectKind.ConditionalHeal:
                    return new Color(0.25f, 0.80f, 0.40f); // 회복 = 녹색
                case FieldObjectKind.FogReveal:
                    return new Color(0.95f, 0.80f, 0.25f); // 시야공개 = 황색
                case FieldObjectKind.LifestealDamage:
                    return new Color(0.62f, 0.18f, 0.55f); // 흡수 = 자주색(피해 적색과 회복 녹색의 중간 성격)
                default:
                    return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        private void DestroyMarker(Marker marker)
        {
            if (marker?.Root == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(marker.Root);
            }
            else
            {
                DestroyImmediate(marker.Root);
            }
        }
    }
}
