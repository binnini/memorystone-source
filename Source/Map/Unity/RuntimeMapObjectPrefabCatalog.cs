using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// Former legacy single catalog of placeable map object prefabs, unified into the per-type
    /// <see cref="MapObjectTypedCatalog"/> assets aggregated through <see cref="MapObjectCatalogSet"/>
    /// (리팩토링 6단계, 2026-07-02). The <c>RuntimeMapObjectPrefabCatalog.asset</c> and every
    /// resolution/fallback path through this type were removed after
    /// <c>MapObjectCatalogSetTests.CatalogSetFullyCoversLegacyCatalog</c> confirmed full coverage.
    /// The type only remains as the container of the shared <see cref="Entry"/> layout that the typed
    /// catalogs still serialize.
    /// </summary>
    public static class RuntimeMapObjectPrefabCatalog
    {
        [Serializable]
        public sealed class Entry
        {
            [SerializeField] private string objectRef;
            [SerializeField] private GameObject prefab;
            [SerializeField] private HexMapObjectType objectType = HexMapObjectType.Building;
            [SerializeField] private bool blocksMovement = true;
            [SerializeField] private bool blocksVision = true;
            [SerializeField] private bool interactable;
            [SerializeField] private Vector3 visualScaleMultiplier = Vector3.one;
            [SerializeField] private List<HexMapObjectFootprintCellRef> footprintOffsets = new List<HexMapObjectFootprintCellRef> { new HexMapObjectFootprintCellRef(0, 0) };

            public Entry()
            {
            }

            public Entry(string objectRef, GameObject prefab)
                : this(objectRef, prefab, HexMapObjectType.Building)
            {
            }

            public Entry(
                string objectRef,
                GameObject prefab,
                HexMapObjectType objectType,
                bool? blocksMovement = null,
                bool? blocksVision = null,
                bool? interactable = null,
                IEnumerable<HexCoord> footprintOffsets = null,
                Vector3? visualScaleMultiplier = null)
            {
                this.objectRef = objectRef;
                this.prefab = prefab;
                this.objectType = objectType;
                this.blocksMovement = blocksMovement ?? HexMapObjectTypeDefaults.BlocksMovement(objectType);
                this.blocksVision = blocksVision ?? HexMapObjectTypeDefaults.BlocksVision(objectType);
                this.interactable = interactable ?? HexMapObjectTypeDefaults.Interactable(objectType);
                this.footprintOffsets = NormalizeFootprintOffsets(footprintOffsets);
                this.visualScaleMultiplier = SanitizeScale(visualScaleMultiplier ?? Vector3.one);
            }

            public string ObjectRef => objectRef ?? string.Empty;
            public GameObject Prefab => prefab;
            public HexMapObjectType ObjectType => objectType;
            public bool BlocksMovement => blocksMovement;
            public bool BlocksVision => blocksVision;
            public bool Interactable => interactable;
            public Vector3 VisualScaleMultiplier => SanitizeScale(visualScaleMultiplier);
            public IReadOnlyList<HexCoord> FootprintOffsets => NormalizeFootprintOffsets(
                footprintOffsets == null ? null : footprintOffsets.Select(cell => cell.Offset))
                .Select(cell => cell.Offset)
                .ToArray();

            public void ApplyDefaults(HexMapObjectType objectType, bool blocksMovement, bool blocksVision, bool interactable, IEnumerable<HexCoord> footprintOffsets, Vector3 visualScaleMultiplier)
            {
                this.objectType = objectType;
                this.blocksMovement = blocksMovement;
                this.blocksVision = blocksVision;
                this.interactable = interactable;
                this.footprintOffsets = NormalizeFootprintOffsets(footprintOffsets);
                this.visualScaleMultiplier = SanitizeScale(visualScaleMultiplier);
            }

            private static List<HexMapObjectFootprintCellRef> NormalizeFootprintOffsets(IEnumerable<HexCoord> offsets)
            {
                var normalized = offsets == null
                    ? new[] { new HexCoord(0, 0) }
                    : offsets.Distinct().OrderBy(coord => coord).ToArray();
                if (normalized.Length == 0)
                {
                    normalized = new[] { new HexCoord(0, 0) };
                }

                return normalized.Select(coord => new HexMapObjectFootprintCellRef(coord.Q, coord.R)).ToList();
            }

            private static Vector3 SanitizeScale(Vector3 value)
            {
                return new Vector3(
                    value.x > 0f ? value.x : 1f,
                    value.y > 0f ? value.y : 1f,
                    value.z > 0f ? value.z : 1f);
            }
        }
    }
}
