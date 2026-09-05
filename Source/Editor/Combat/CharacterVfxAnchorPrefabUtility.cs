using System.Collections.Generic;
using SeoulPlayup.EditorTools;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Editor
{
    public static class CharacterVfxAnchorPrefabUtility
    {
        private const string MenuPath = "Tools/Seoul Playup/Combat/Ensure Character VFX Anchors";
        private static readonly string[] CharacterPrefabRoots =
        {
            "Assets/Art/Characters"
        };

        [MenuItem(MenuPath)]
        public static void EnsureCharacterVfxAnchorsMenu()
        {
            // ⚠️ isBatchMode 가드로는 부족했다 — 에이전트가 부르는 에디터는 배치 모드가 아니라
            // <b>사람이 없을 뿐인 일반 에디터</b>라 그대로 모달이 뜨고 멈춘다.
            EditorReportDialog.Report("Character VFX Anchors", EnsureCharacterVfxAnchors());
        }

        public static string EnsureCharacterVfxAnchors()
        {
            var prefabPaths = CollectCharacterPrefabPaths();
            var changed = 0;
            var skipped = 0;
            var withoutVisual = 0;

            foreach (var prefabPath in prefabPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    var visuals = root.GetComponentsInChildren<CharacterActorVisual>(includeInactive: true);
                    if (visuals.Length == 0)
                    {
                        withoutVisual++;
                        var prefabChanged = EnsureAnchors(root.transform, visual: null);
                        if (prefabChanged)
                        {
                            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                            changed++;
                        }
                        else
                        {
                            skipped++;
                        }

                        continue;
                    }

                    var visualPrefabChanged = false;
                    foreach (var visual in visuals)
                    {
                        visualPrefabChanged |= EnsureAnchors(visual.transform, visual);
                    }

                    if (visualPrefabChanged)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                        changed++;
                    }
                    else
                    {
                        skipped++;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return $"Character VFX anchors ensured. changed={changed}, unchanged={skipped}, without CharacterActorVisual={withoutVisual}, scanned={prefabPaths.Count}";
        }

        private static List<string> CollectCharacterPrefabPaths()
        {
            var paths = new List<string>();
            foreach (var root in CharacterPrefabRoots)
            {
                if (!AssetDatabase.IsValidFolder(root))
                {
                    continue;
                }

                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { root }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.IsNullOrWhiteSpace(path) && path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(path);
                    }
                }
            }

            paths.Sort(System.StringComparer.OrdinalIgnoreCase);
            return paths;
        }

        private static bool EnsureAnchors(Transform visualRoot, CharacterActorVisual visual)
        {
            var changed = false;
            var vfxRoot = FindDirectChild(visualRoot, "VFXRoot");
            if (vfxRoot == null)
            {
                vfxRoot = new GameObject("VFXRoot").transform;
                vfxRoot.SetParent(visualRoot, false);
                vfxRoot.localPosition = Vector3.zero;
                vfxRoot.localRotation = Quaternion.identity;
                vfxRoot.localScale = Vector3.one;
                changed = true;
            }

            var bounds = ResolveLocalRendererBounds(visualRoot);
            var center = bounds?.center ?? Vector3.zero;
            var min = bounds?.min ?? Vector3.zero;
            var max = bounds?.max ?? new Vector3(0f, 1f, 0f);

            var hitCenter = EnsureAnchor(vfxRoot, "HitCenter", center, ref changed);
            var ground = EnsureAnchor(vfxRoot, "Ground", new Vector3(center.x, min.y, center.z), ref changed);
            var head = EnsureAnchor(vfxRoot, "Head", new Vector3(center.x, max.y, center.z), ref changed);
            var attackSource = EnsureAnchor(vfxRoot, "AttackSource", new Vector3(center.x, center.y, max.z), ref changed);

            if (visual != null)
            {
                var serialized = new SerializedObject(visual);
                changed |= AssignTransform(serialized, "hitCenterAnchor", hitCenter);
                changed |= AssignTransform(serialized, "groundAnchor", ground);
                changed |= AssignTransform(serialized, "headAnchor", head);
                changed |= AssignTransform(serialized, "attackSourceAnchor", attackSource);
                if (changed)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(visual);
                }
            }

            return changed;
        }

        private static Transform EnsureAnchor(Transform parent, string name, Vector3 localPosition, ref bool changed)
        {
            var anchor = FindDirectChild(parent, name);
            if (anchor != null)
            {
                return anchor;
            }

            anchor = new GameObject(name).transform;
            anchor.SetParent(parent, false);
            anchor.localPosition = localPosition;
            anchor.localRotation = Quaternion.identity;
            anchor.localScale = Vector3.one;
            changed = true;
            return anchor;
        }

        private static bool AssignTransform(SerializedObject serializedObject, string propertyName, Transform value)
        {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == value)
            {
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null)
            {
                return null;
            }

            for (var i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// 앵커 저작면이 되는 로컬 바운즈. <see cref="Renderer.bounds"/>는 스키닝 모델에서 실제 지오메트리보다
        /// 훨씬 헐거워(발밑으로 0.1~0.16 더 내려간다) 그대로 쓰면 Ground 앵커가 발 아래에 박히고,
        /// 접지 보정(<see cref="SeoulPlayup.Map.Unity.CharacterActorVisual.SnapGroundAnchorToWorldY"/>)이
        /// 그만큼 모델을 띄운다(2026-09-04 「신규 6종이 떠 있다」). 그래서 메시 정점을 직접 본다.
        /// </summary>
        private static Bounds? ResolveLocalRendererBounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            var hasBounds = false;
            var bounds = default(Bounds);
            foreach (var renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                if (!TryResolveLocalMeshBounds(root, renderer, out var localBounds))
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = localBounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(localBounds);
                }
            }

            return hasBounds ? bounds : null;
        }

        private static bool TryResolveLocalMeshBounds(Transform root, Renderer renderer, out Bounds localBounds)
        {
            localBounds = default;
            Mesh mesh = null;
            var temporaryMesh = false;
            var meshToRoot = Matrix4x4.identity;

            if (renderer is SkinnedMeshRenderer skinned)
            {
                if (skinned.sharedMesh == null)
                {
                    return false;
                }

                // 본 포즈가 반영된 정점이 필요하다 — sharedMesh는 바인드 포즈라 리깅된 모델에서 어긋난다.
                mesh = new Mesh();
                skinned.BakeMesh(mesh, useScale: true);
                temporaryMesh = true;
                meshToRoot = root.worldToLocalMatrix * skinned.transform.localToWorldMatrix;
            }
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                {
                    return false;
                }

                mesh = filter.sharedMesh;
                meshToRoot = root.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            }

            var vertices = mesh.vertices;
            if (vertices.Length == 0)
            {
                if (temporaryMesh)
                {
                    Object.DestroyImmediate(mesh);
                }

                return false;
            }

            var min = meshToRoot.MultiplyPoint3x4(vertices[0]);
            var max = min;
            for (var i = 1; i < vertices.Length; i++)
            {
                var vertex = meshToRoot.MultiplyPoint3x4(vertices[i]);
                min = Vector3.Min(min, vertex);
                max = Vector3.Max(max, vertex);
            }

            if (temporaryMesh)
            {
                Object.DestroyImmediate(mesh);
            }

            localBounds = new Bounds((min + max) * 0.5f, max - min);
            return true;
        }
    }
}
