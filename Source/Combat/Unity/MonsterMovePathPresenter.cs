using System.Collections.Generic;
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// REQ9/10/11: visualises where monsters are about to move.
    /// • A translucent ghost copy of each monster's 3D model is shown at its predicted destination at all times
    ///   (no hover required).
    /// • Hovering a monster additionally draws a <see cref="LineRenderer"/> along its move route plus a flat
    ///   arrow between the last-but-one tile and the destination (oriented along the final step).
    /// • The route/destination are supplied already trimmed to the reachable cell when a field object blocks the
    ///   way (see <c>CombatState.GetMonsterMovePath</c>), so this presenter just renders what it is given.
    /// Driven once per view refresh by <c>MapCombatController</c> via <see cref="Refresh"/>; it reconciles
    /// per-monster GameObjects (ghost / line / arrow) keyed by monster id and destroys stale ones.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MonsterMovePathPresenter : MonoBehaviour
    {
        public readonly struct Entry
        {
            public Entry(string monsterId, IReadOnlyList<Vector3> worldPath, GameObject ghostPrefab, Vector3 ghostLocalScale, Vector3 ghostEulerAngles)
            {
                MonsterId = monsterId;
                WorldPath = worldPath;
                GhostPrefab = ghostPrefab;
                GhostLocalScale = ghostLocalScale;
                GhostEulerAngles = ghostEulerAngles;
            }

            public string MonsterId { get; }
            public IReadOnlyList<Vector3> WorldPath { get; }
            public GameObject GhostPrefab { get; }
            public Vector3 GhostLocalScale { get; }
            public Vector3 GhostEulerAngles { get; }

            public bool WillMove => WorldPath != null && WorldPath.Count >= 2;
        }

        private static readonly Color PathColor = new Color(0.45f, 0.85f, 1f, 0.85f);
        private static readonly Color GhostColor = new Color(0.6f, 0.85f, 1f, 0.42f);
        private const float PathLift = 0.06f;
        private const float PathWidth = 0.14f;
        private const float ArrowSize = 0.3f;

        [SerializeField] private Sprite arrowSprite;

        private readonly Dictionary<string, GameObject> ghosts = new Dictionary<string, GameObject>();
        private readonly Dictionary<string, LineRenderer> lines = new Dictionary<string, LineRenderer>();
        private readonly Dictionary<string, GameObject> arrows = new Dictionary<string, GameObject>();
        private readonly HashSet<string> seen = new HashSet<string>();
        private Material lineMaterial;
        private Material ghostMaterial;
        private Mesh arrowMesh;
        private Material arrowMeshMaterial;

        public void SetArrowSprite(Sprite sprite)
        {
            arrowSprite = sprite;
        }

        public void Refresh(IReadOnlyList<Entry> entries, string hoveredMonsterId)
        {
            seen.Clear();

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.MonsterId) || !entry.WillMove)
                    {
                        continue;
                    }

                    seen.Add(entry.MonsterId);
                    var destination = entry.WorldPath[entry.WorldPath.Count - 1];
                    var beforeDestination = entry.WorldPath[entry.WorldPath.Count - 2];

                    UpdateGhost(entry, destination, beforeDestination);

                    var hovered = entry.MonsterId == hoveredMonsterId;
                    UpdateLine(entry, hovered);
                    UpdateArrow(entry, destination, beforeDestination, hovered);
                }
            }

            // Tear down visuals for monsters no longer reported as moving (moved, died, despawned).
            Prune(ghosts);
            Prune(lines);
            Prune(arrows);
        }

        public void Clear()
        {
            DestroyAll(ghosts);
            DestroyAll(lines);
            DestroyAll(arrows);
        }

        private void UpdateGhost(Entry entry, Vector3 destination, Vector3 beforeDestination)
        {
            if (entry.GhostPrefab == null)
            {
                return;
            }

            if (!ghosts.TryGetValue(entry.MonsterId, out var ghost) || ghost == null)
            {
                ghost = Instantiate(entry.GhostPrefab, transform);
                ghost.name = $"MoveGhost_{entry.MonsterId}";
                StripGhost(ghost);
                ApplyGhostMaterial(ghost);
                // Preserve the prefab's authored scale, then apply the same visual-scale multiplier the live
                // marker uses, so the ghost matches the on-map model size.
                ghost.transform.localScale = Vector3.Scale(ghost.transform.localScale, SanitizeScale(entry.GhostLocalScale));
                ghosts[entry.MonsterId] = ghost;
            }

            ghost.transform.position = destination;
            var forward = destination - beforeDestination;
            forward.y = 0f;
            var facing = forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
            ghost.transform.rotation = facing * Quaternion.Euler(entry.GhostEulerAngles);
            // 실물 마커와 같은 규약: 발(Ground 앵커)을 목적지 타일 윗면에 붙인다.
            CharacterActorVisual.SnapGroundAnchorToWorldY(ghost.transform, destination.y);
            if (!ghost.activeSelf)
            {
                ghost.SetActive(true);
            }
        }

        private void UpdateLine(Entry entry, bool visible)
        {
            if (!visible)
            {
                if (lines.TryGetValue(entry.MonsterId, out var hiddenLine) && hiddenLine != null)
                {
                    hiddenLine.enabled = false;
                }

                return;
            }

            if (!lines.TryGetValue(entry.MonsterId, out var line) || line == null)
            {
                var go = new GameObject($"MovePath_{entry.MonsterId}");
                go.transform.SetParent(transform, false);
                line = go.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.material = EnsureLineMaterial();
                line.startColor = PathColor;
                line.endColor = PathColor;
                line.startWidth = PathWidth;
                line.endWidth = PathWidth;
                line.numCornerVertices = 4;
                line.numCapVertices = 4;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.alignment = LineAlignment.View;
                lines[entry.MonsterId] = line;
            }

            line.enabled = true;
            var count = entry.WorldPath.Count;
            line.positionCount = count;
            for (var i = 0; i < count; i++)
            {
                line.SetPosition(i, entry.WorldPath[i] + Vector3.up * PathLift);
            }
        }

        private void UpdateArrow(Entry entry, Vector3 destination, Vector3 beforeDestination, bool visible)
        {
            if (!visible)
            {
                if (arrows.TryGetValue(entry.MonsterId, out var hiddenArrow) && hiddenArrow != null)
                {
                    hiddenArrow.SetActive(false);
                }

                return;
            }

            if (!arrows.TryGetValue(entry.MonsterId, out var arrow) || arrow == null)
            {
                arrow = CreateArrow(entry.MonsterId);
                arrows[entry.MonsterId] = arrow;
            }

            arrow.SetActive(true);
            var direction = destination - beforeDestination;
            direction.y = 0f;
            // #3: sit the arrowhead at the destination tile (the end of the route), pulled back by half its
            // length so the tip lands on the destination centre rather than overshooting past it.
            var dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            arrow.transform.position = destination + Vector3.up * PathLift - dir * (ArrowSize * 0.5f);
            // Lay the arrow flat on the ground pointing along the final step toward the destination.
            arrow.transform.rotation = Quaternion.LookRotation(Vector3.up, dir);
        }

        private GameObject CreateArrow(string monsterId)
        {
            var go = new GameObject($"MovePathArrow_{monsterId}");
            go.transform.SetParent(transform, false);

            // A flat, double-sided arrowhead mesh — guaranteed to render regardless of import settings / camera
            // angle (the sprite path proved unreliable). The mesh lies in the local XY plane with +Y as the tip.
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = EnsureArrowMesh();
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = EnsureArrowMeshMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private Material EnsureLineMaterial()
        {
            if (lineMaterial == null)
            {
                lineMaterial = CombatOverlayMaterialFactory.Create("MonsterMovePathLine", PathColor);
            }

            return lineMaterial;
        }

        private Material EnsureArrowMeshMaterial()
        {
            if (arrowMeshMaterial == null)
            {
                arrowMeshMaterial = CombatOverlayMaterialFactory.Create("MonsterMovePathArrow", PathColor);
            }

            return arrowMeshMaterial;
        }

        private Mesh EnsureArrowMesh()
        {
            if (arrowMesh != null)
            {
                return arrowMesh;
            }

            // Flat arrowhead in the local XY plane (the GameObject is rotated so +Y maps to ground forward).
            // Double-sided (both winding orders) so it renders no matter which way the normal faces the camera.
            var s = ArrowSize;
            arrowMesh = new Mesh { name = "MonsterMovePathArrowMesh" };
            arrowMesh.SetVertices(new List<Vector3>
            {
                new Vector3(0f, s, 0f),         // tip
                new Vector3(-s * 0.85f, -s, 0f), // left
                new Vector3(s * 0.85f, -s, 0f),  // right
            });
            arrowMesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 1 }, 0);
            arrowMesh.RecalculateBounds();
            arrowMesh.RecalculateNormals();
            return arrowMesh;
        }

        private void ApplyGhostMaterial(GameObject ghost)
        {
            var material = EnsureGhostMaterial();
            foreach (var renderer in ghost.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is ParticleSystemRenderer)
                {
                    continue;
                }

                var shared = renderer.sharedMaterials;
                var replacement = new Material[shared.Length];
                for (var i = 0; i < replacement.Length; i++)
                {
                    replacement[i] = material;
                }

                renderer.sharedMaterials = replacement;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        private Material EnsureGhostMaterial()
        {
            if (ghostMaterial != null)
            {
                return ghostMaterial;
            }

            // Best-effort URP transparent material; falls back through pipelines so the ghost still draws.
            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Sprites/Default");
            ghostMaterial = new Material(shader) { name = "MonsterMoveGhost" };
            if (ghostMaterial.HasProperty("_Surface"))
            {
                ghostMaterial.SetFloat("_Surface", 1f); // 0 opaque, 1 transparent (URP)
            }

            if (ghostMaterial.HasProperty("_Blend"))
            {
                ghostMaterial.SetFloat("_Blend", 0f); // alpha
            }

            ghostMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            ghostMaterial.SetOverrideTag("RenderType", "Transparent");
            ghostMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            ghostMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            ghostMaterial.SetInt("_ZWrite", 0);
            ghostMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            SetGhostColor(ghostMaterial, GhostColor);
            return ghostMaterial;
        }

        private static void SetGhostColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.color = color;
        }

        private static Vector3 SanitizeScale(Vector3 scale)
        {
            return scale.x > 0f && scale.y > 0f && scale.z > 0f ? scale : Vector3.one;
        }

        private static void StripGhost(GameObject ghost)
        {
            // A ghost is a static silhouette: drop behaviour, physics and animation so instantiating the model
            // prefab has no side effects (no AI ticking, no colliders, frozen pose).
            foreach (var behaviour in ghost.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour != null)
                {
                    behaviour.enabled = false;
                }
            }

            foreach (var animator in ghost.GetComponentsInChildren<Animator>(true))
            {
                if (animator != null)
                {
                    animator.enabled = false;
                }
            }

            foreach (var collider in ghost.GetComponentsInChildren<Collider>(true))
            {
                if (collider != null)
                {
                    collider.enabled = false;
                }
            }
        }

        private void Prune(Dictionary<string, GameObject> map)
        {
            DestroyStale(map);
        }

        private void Prune(Dictionary<string, LineRenderer> map)
        {
            var stale = new List<string>();
            foreach (var pair in map)
            {
                if (!seen.Contains(pair.Key))
                {
                    if (pair.Value != null)
                    {
                        Destroy(pair.Value.gameObject);
                    }

                    stale.Add(pair.Key);
                }
            }

            foreach (var key in stale)
            {
                map.Remove(key);
            }
        }

        private void DestroyStale(Dictionary<string, GameObject> map)
        {
            var stale = new List<string>();
            foreach (var pair in map)
            {
                if (!seen.Contains(pair.Key))
                {
                    if (pair.Value != null)
                    {
                        Destroy(pair.Value);
                    }

                    stale.Add(pair.Key);
                }
            }

            foreach (var key in stale)
            {
                map.Remove(key);
            }
        }

        private static void DestroyAll(Dictionary<string, GameObject> map)
        {
            foreach (var value in map.Values)
            {
                if (value != null)
                {
                    Destroy(value);
                }
            }

            map.Clear();
        }

        private static void DestroyAll(Dictionary<string, LineRenderer> map)
        {
            foreach (var value in map.Values)
            {
                if (value != null)
                {
                    Destroy(value.gameObject);
                }
            }

            map.Clear();
        }

        private void OnDisable()
        {
            Clear();
        }
    }
}
