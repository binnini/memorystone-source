#if UNITY_EDITOR
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev.Editor
{
    public static class MonsterAnimatorPrefabFixer
    {
        private static readonly string[] MonsterPrefabPaths =
        {
            "Assets/Art/Characters/monster/ThreeEyeDog/Prefabs/ThreeEyeDog.prefab",
            "Assets/Art/Characters/monster/Bulgasal/Prefabs/Bulgasal.prefab",
            "Assets/Art/Characters/monster/Bull/Prefabs/Bull.prefab",
            "Assets/Art/Characters/monster/LionMask/Prefabs/LionMask.prefab",
            "Assets/Art/Characters/monster/Pig/Prefabs/Pig.prefab",
            "Assets/Art/Characters/monster/tiger/Prefabs/Tiger.prefab",
            "Assets/Art/Characters/TinyRex/Prefabs/EnemyTinyRex.prefab"
        };

        [MenuItem("Seoul Playup/Dev/Fix Monster Prefab Animators")]
        public static void FixMonsterPrefabAnimators()
        {
            var fixedCount = 0;
            var importerFixedCount = 0;
            for (var i = 0; i < MonsterPrefabPaths.Length; i++)
            {
                if (FixPrefab(MonsterPrefabPaths[i], out var modelAssetPath))
                {
                    fixedCount++;
                }

                if (FixModelImporterLoops(modelAssetPath))
                {
                    importerFixedCount++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Monster animator prefab fix complete. Updated {fixedCount} prefab(s), {importerFixedCount} model importer(s).");
        }

        private static bool FixPrefab(string prefabPath, out string modelAssetPath)
        {
            modelAssetPath = null;
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            if (root == null)
            {
                return false;
            }

            var changed = false;
            try
            {
                var vfxRoot = FindChild(root.transform, "VFXRoot");
                var model = FindChild(root.transform, "Model") ?? FindLikelyModelRoot(root.transform, vfxRoot);
                if (model == null)
                {
                    Debug.LogWarning($"[{prefabPath}] Model root not found; skipped animator fix.");
                    return false;
                }

                modelAssetPath = ResolveModelAssetPath(model.gameObject);
                var misplacedAnimator = vfxRoot != null ? vfxRoot.GetComponent<Animator>() : null;
                var modelAnimator = model.GetComponent<Animator>();
                if (modelAnimator == null)
                {
                    modelAnimator = model.gameObject.AddComponent<Animator>();
                    changed = true;
                }

                if (modelAnimator.runtimeAnimatorController == null && misplacedAnimator != null && misplacedAnimator.runtimeAnimatorController != null)
                {
                    modelAnimator.runtimeAnimatorController = misplacedAnimator.runtimeAnimatorController;
                    changed = true;
                }

                if (modelAnimator.avatar == null)
                {
                    var avatar = ResolveAvatar(model.gameObject);
                    if (avatar != null)
                    {
                        modelAnimator.avatar = avatar;
                        changed = true;
                    }
                    else if (RequiresExplicitAvatar(modelAssetPath))
                    {
                        Debug.LogWarning($"[{prefabPath}] No valid Avatar found for {model.name}; Animator may still not animate.");
                    }
                }

                modelAnimator.enabled = true;
                modelAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                if (misplacedAnimator != null && misplacedAnimator != modelAnimator)
                {
                    Object.DestroyImmediate(misplacedAnimator, true);
                    changed = true;
                }

                var visual = root.GetComponent<CharacterActorVisual>();
                if (visual == null)
                {
                    visual = root.AddComponent<CharacterActorVisual>();
                    changed = true;
                }

                BindCharacterActorVisual(visual, root.transform, modelAnimator);
                changed = true;

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                    Debug.Log($"[{prefabPath}] Animator moved to {GetPath(model)} with controller {modelAnimator.runtimeAnimatorController?.name ?? "none"} and avatar {modelAnimator.avatar?.name ?? "none"}.");
                }

                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool FixModelImporterLoops(string modelAssetPath)
        {
            if (string.IsNullOrWhiteSpace(modelAssetPath))
            {
                return false;
            }

            var modelImporter = AssetImporter.GetAtPath(modelAssetPath) as ModelImporter;
            if (modelImporter == null)
            {
                return false;
            }

            var clips = modelImporter.clipAnimations;
            if (clips == null || clips.Length == 0)
            {
                clips = modelImporter.defaultClipAnimations;
            }

            if (clips == null || clips.Length == 0)
            {
                return false;
            }

            var changed = false;
            for (var i = 0; i < clips.Length; i++)
            {
                if (!ShouldLoopClip(clips[i].name))
                {
                    continue;
                }

                if (!clips[i].loopTime)
                {
                    clips[i].loopTime = true;
                    changed = true;
                }

                // Loop pose keeps short locomotion/idle clips from visually snapping at the wrap point.
                if (!clips[i].loopPose)
                {
                    clips[i].loopPose = true;
                    changed = true;
                }
            }

            if (!changed)
            {
                return false;
            }

            modelImporter.clipAnimations = clips;
            modelImporter.SaveAndReimport();
            Debug.Log($"[{modelAssetPath}] Enabled looping for idle/locomotion animation clips.");
            return true;
        }

        private static bool ShouldLoopClip(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return false;
            }

            var lowerName = clipName.ToLowerInvariant();
            return lowerName.Contains("idle") ||
                   lowerName.Contains("move") ||
                   lowerName.Contains("walk") ||
                   lowerName.Contains("run");
        }

        private static void BindCharacterActorVisual(CharacterActorVisual visual, Transform root, Animator animator)
        {
            var serialized = new SerializedObject(visual);
            serialized.FindProperty("animator").objectReferenceValue = animator;
            serialized.FindProperty("hitCenterAnchor").objectReferenceValue = FindChild(root, "HitCenter");
            serialized.FindProperty("groundAnchor").objectReferenceValue = FindChild(root, "Ground");
            serialized.FindProperty("headAnchor").objectReferenceValue = FindChild(root, "Head");
            serialized.FindProperty("attackSourceAnchor").objectReferenceValue = FindChild(root, "AttackSource");
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Avatar ResolveAvatar(GameObject modelInstance)
        {
            var assetPath = ResolveModelAssetPath(modelInstance);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (var i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Avatar avatar && avatar != null && avatar.isValid)
                {
                    return avatar;
                }
            }

            return null;
        }

        private static string ResolveModelAssetPath(GameObject modelInstance)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(modelInstance);
            var assetPath = source != null ? AssetDatabase.GetAssetPath(source) : AssetDatabase.GetAssetPath(modelInstance);
            return string.IsNullOrWhiteSpace(assetPath) ? null : assetPath;
        }

        private static bool RequiresExplicitAvatar(string modelAssetPath)
        {
            var modelImporter = AssetImporter.GetAtPath(modelAssetPath) as ModelImporter;
            return modelImporter == null || modelImporter.animationType == ModelImporterAnimationType.Human;
        }

        private static Transform FindLikelyModelRoot(Transform root, Transform excluded)
        {
            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child == excluded)
                {
                    continue;
                }

                if (child.GetComponentInChildren<SkinnedMeshRenderer>(true) != null ||
                    child.GetComponentInChildren<MeshRenderer>(true) != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform FindChild(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }

                var nested = FindChild(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }
    }
}
#endif
