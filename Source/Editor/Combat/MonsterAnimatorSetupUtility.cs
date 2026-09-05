#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SeoulPlayup.Combat.Editor
{
    /// <summary>
    /// Builds generated monster AnimatorControllers and keeps monster prefabs wired to
    /// the CharacterActorVisual animator contract from designer-facing CSV files.
    ///
    /// Common locomotion/reaction clips live in monster_animation_sets.csv.
    /// Attack trigger-to-clip bindings live in monster_animation_attack_clips.csv and
    /// should match monster_attack_patterns.csv animationTrigger values.
    ///
    /// Rebuild triggers: importing either CSV, or the menu item below. A controller that
    /// already matches the CSV spec is left untouched so the generated .controller files
    /// stay byte-stable in version control (rebuilds assign fresh sub-asset fileIDs).
    /// Hand edits to generated controllers are overwritten on the next rebuild.
    /// </summary>
    public static class MonsterAnimatorSetupUtility
    {
        private const string CsvDirectory = CombatCsvPaths.MonsterDirectory;
        private const string AnimationSetsPath = CsvDirectory + "/monster_animation_sets.csv";
        private const string AttackClipsPath = CsvDirectory + "/monster_animation_attack_clips.csv";
        private const float DefaultMoveSpeedThreshold = 0.1f;
        private const float DefaultTransitionDuration = 0.08f;
        private const float DefaultAttackExitTime = 0.9f;
        private const float HitExitTime = 0.85f;

        [MenuItem("Seoul Playup/Combat/Setup Monster Animators")]
        public static void EnsureAssetsFromMenu()
        {
            EnsureAssets(logResult: true);
        }

        internal static bool IsSourceCsv(string assetPath)
        {
            return string.Equals(assetPath, AnimationSetsPath, StringComparison.OrdinalIgnoreCase)
                || string.Equals(assetPath, AttackClipsPath, StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureAssets(bool logResult)
        {
            if (!File.Exists(AnimationSetsPath))
            {
                if (logResult)
                {
                    Debug.LogWarning($"Monster animator setup skipped: missing {AnimationSetsPath}.");
                }

                return;
            }

            var animationSets = ReadCsv(AnimationSetsPath)
                .Select(MonsterAnimationSet.FromRow)
                .Where(set => set.Enabled)
                .ToArray();
            var attackClips = File.Exists(AttackClipsPath)
                ? ReadCsv(AttackClipsPath).Select(MonsterAttackAnimationClip.FromRow).ToArray()
                : Array.Empty<MonsterAttackAnimationClip>();

            var issues = new List<string>();
            var processed = 0;
            var rebuiltControllers = 0;
            var changedPrefabs = 0;

            foreach (var set in animationSets)
            {
                if (!ValidateSet(set, issues))
                {
                    continue;
                }

                var clips = AssetDatabase.LoadAllAssetsAtPath(set.ModelAssetPath)
                    .OfType<AnimationClip>()
                    .Where(clip => clip != null && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                    .ToArray();
                if (clips.Length == 0)
                {
                    issues.Add($"Warning: {set.MonsterId} has no animation clips in {set.ModelAssetPath}.");
                    continue;
                }

                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(set.ControllerPath);
                var created = controller == null;
                if (created)
                {
                    EnsureFolder(Path.GetDirectoryName(set.ControllerPath)?.Replace('\\', '/'));
                    controller = AnimatorController.CreateAnimatorControllerAtPath(set.ControllerPath);
                }

                var attacks = attackClips
                    .Where(attack => string.Equals(attack.MonsterId, set.MonsterId, StringComparison.Ordinal))
                    .Where(attack => !string.IsNullOrWhiteSpace(attack.AnimationTrigger))
                    .ToArray();

                if (created || !ControllerMatchesSpec(controller, set, attacks, clips))
                {
                    ConfigureController(controller, set, attacks, clips, issues);
                    EditorUtility.SetDirty(controller);
                    rebuiltControllers++;
                }

                if (ConfigurePrefab(set, controller, issues))
                {
                    changedPrefabs++;
                }

                processed++;
            }

            AssetDatabase.SaveAssets();

            if (logResult)
            {
                foreach (var issue in issues)
                {
                    if (issue.StartsWith("Error:", StringComparison.Ordinal))
                    {
                        Debug.LogError(issue);
                    }
                    else
                    {
                        Debug.LogWarning(issue);
                    }
                }

                Debug.Log($"Monster animator setup complete. Processed={processed}, ControllersRebuilt={rebuiltControllers}, PrefabsChanged={changedPrefabs}, Issues={issues.Count}");
            }
        }

        private static bool ValidateSet(MonsterAnimationSet set, List<string> issues)
        {
            var valid = true;
            if (string.IsNullOrWhiteSpace(set.MonsterId))
            {
                issues.Add("Error: monster_animation_sets.csv contains a row without monsterId.");
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(set.ModelAssetPath) || !File.Exists(set.ModelAssetPath))
            {
                issues.Add($"Error: {set.MonsterId} modelAssetPath not found: {set.ModelAssetPath}");
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(set.VisualPrefabPath) || !File.Exists(set.VisualPrefabPath))
            {
                issues.Add($"Error: {set.MonsterId} visualPrefabPath not found: {set.VisualPrefabPath}");
                valid = false;
            }

            if (string.IsNullOrWhiteSpace(set.ControllerPath))
            {
                issues.Add($"Error: {set.MonsterId} controllerPath is empty.");
                valid = false;
            }

            return valid;
        }

        private static void ConfigureController(
            AnimatorController controller,
            MonsterAnimationSet set,
            IReadOnlyList<MonsterAttackAnimationClip> attacks,
            AnimationClip[] clips,
            List<string> issues)
        {
            EnsureParameter(controller, set.MoveSpeedParameter, AnimatorControllerParameterType.Float);
            EnsureParameter(controller, set.HitTriggerParameter, AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, set.KnockbackTriggerParameter, AnimatorControllerParameterType.Trigger);
            EnsureParameter(controller, set.DeathParameter, AnimatorControllerParameterType.Bool);
            foreach (var attack in attacks)
            {
                EnsureParameter(controller, attack.AnimationTrigger, AnimatorControllerParameterType.Trigger);
            }

            var stateMachine = controller.layers[0].stateMachine;
            ClearStateMachine(stateMachine);

            var idle = AddState(stateMachine, "Idle", FindClip(clips, set.IdleClip, set.MonsterId, issues), new Vector3(240f, 120f, 0f));
            var move = AddState(stateMachine, "Move", FindClip(clips, set.MoveClip, set.MonsterId, issues), new Vector3(520f, 120f, 0f));
            var hitMotion = FindClip(clips, set.HitClip, set.MonsterId, issues);
            var hit = AddState(stateMachine, "Hit", hitMotion, new Vector3(520f, 300f, 0f));
            var knockbackMotion = FindOptionalClip(clips, set.KnockbackClip);
            if (knockbackMotion == null && !string.IsNullOrWhiteSpace(set.KnockbackClip))
            {
                issues.Add($"Warning: {set.MonsterId} knockback clip not found: {set.KnockbackClip}; using hit clip.");
            }

            var knockback = AddState(stateMachine, "Knockback", knockbackMotion != null ? knockbackMotion : hitMotion, new Vector3(800f, 300f, 0f));
            var death = AddState(stateMachine, "Death", FindClip(clips, set.DeathClip, set.MonsterId, issues), new Vector3(1080f, 300f, 0f));
            stateMachine.defaultState = idle;

            AddFloatTransition(idle, move, set.MoveSpeedParameter, AnimatorConditionMode.Greater, set.MoveSpeedThreshold, false, set.TransitionDuration);
            AddFloatTransition(move, idle, set.MoveSpeedParameter, AnimatorConditionMode.Less, set.MoveSpeedThreshold, false, set.TransitionDuration);
            // 죽은 뒤에는 피격·넉백·공격이 사망 자세를 빼앗지 못한다(2026-09-05 — 아래 함수 주석 참조).
            AddTriggerAnyTransition(stateMachine, hit, set.HitTriggerParameter, set.TransitionDuration, canTransitionToSelf: true, set.DeathParameter);
            AddTriggerAnyTransition(stateMachine, knockback, set.KnockbackTriggerParameter, set.TransitionDuration, canTransitionToSelf: true, set.DeathParameter);
            AddBoolAnyTransition(stateMachine, death, set.DeathParameter, expected: true, set.TransitionDuration);
            AddExitTransition(hit, idle, HitExitTime, set.TransitionDuration);
            AddExitTransition(knockback, idle, HitExitTime, set.TransitionDuration);

            var x = 240f;
            var y = 480f;
            foreach (var attack in attacks)
            {
                var attackState = AddState(
                    stateMachine,
                    attack.AnimationTrigger,
                    FindClip(clips, attack.ClipName, set.MonsterId, issues),
                    new Vector3(x, y, 0f));
                AddTriggerAnyTransition(stateMachine, attackState, attack.AnimationTrigger, attack.TransitionDuration, canTransitionToSelf: true, set.DeathParameter);
                AddExitTransition(attackState, idle, attack.ExitTime, attack.TransitionDuration);
                y += 180f;
                if (y > 840f)
                {
                    y = 480f;
                    x += 280f;
                }
            }
        }

        /// <summary>
        /// True when the controller already carries exactly the graph ConfigureController would
        /// build from the CSV spec, so the rebuild (and its fileID churn) can be skipped.
        /// Mirrors ConfigureController: any drift there must be reflected here.
        /// </summary>
        private static bool ControllerMatchesSpec(
            AnimatorController controller,
            MonsterAnimationSet set,
            IReadOnlyList<MonsterAttackAnimationClip> attacks,
            AnimationClip[] clips)
        {
            if (!HasParameter(controller, set.MoveSpeedParameter) ||
                !HasParameter(controller, set.HitTriggerParameter) ||
                !HasParameter(controller, set.KnockbackTriggerParameter) ||
                !HasParameter(controller, set.DeathParameter))
            {
                return false;
            }

            foreach (var attack in attacks)
            {
                if (!HasParameter(controller, attack.AnimationTrigger))
                {
                    return false;
                }
            }

            if (controller.layers.Length == 0)
            {
                return false;
            }

            var stateMachine = controller.layers[0].stateMachine;
            if (stateMachine == null || stateMachine.stateMachines.Length != 0 || stateMachine.entryTransitions.Length != 0)
            {
                return false;
            }

            var states = new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            foreach (var child in stateMachine.states)
            {
                if (child.state == null || states.ContainsKey(child.state.name))
                {
                    return false;
                }

                states.Add(child.state.name, child.state);
            }

            var hitMotion = FindOptionalClip(clips, set.HitClip);
            var knockbackMotion = FindOptionalClip(clips, set.KnockbackClip);
            var expectedStates = new List<KeyValuePair<string, Motion>>
            {
                new KeyValuePair<string, Motion>("Idle", FindOptionalClip(clips, set.IdleClip)),
                new KeyValuePair<string, Motion>("Move", FindOptionalClip(clips, set.MoveClip)),
                new KeyValuePair<string, Motion>("Hit", hitMotion),
                new KeyValuePair<string, Motion>("Knockback", knockbackMotion != null ? knockbackMotion : hitMotion),
                new KeyValuePair<string, Motion>("Death", FindOptionalClip(clips, set.DeathClip)),
            };
            foreach (var attack in attacks)
            {
                expectedStates.Add(new KeyValuePair<string, Motion>(attack.AnimationTrigger, FindOptionalClip(clips, attack.ClipName)));
            }

            if (states.Count != expectedStates.Count)
            {
                return false;
            }

            foreach (var expected in expectedStates)
            {
                if (!states.TryGetValue(expected.Key, out var state) || state.motion != expected.Value || !state.writeDefaultValues)
                {
                    return false;
                }
            }

            var idle = states["Idle"];
            if (stateMachine.defaultState != idle)
            {
                return false;
            }

            var hasMoveParameter = !string.IsNullOrWhiteSpace(set.MoveSpeedParameter);
            if (!MatchesSingleConditionTransitions(idle, hasMoveParameter, states["Move"], set.MoveSpeedParameter, AnimatorConditionMode.Greater, set.MoveSpeedThreshold, set.TransitionDuration) ||
                !MatchesSingleConditionTransitions(states["Move"], hasMoveParameter, idle, set.MoveSpeedParameter, AnimatorConditionMode.Less, set.MoveSpeedThreshold, set.TransitionDuration) ||
                !MatchesSingleExitTransition(states["Hit"], idle, HitExitTime, set.TransitionDuration) ||
                !MatchesSingleExitTransition(states["Knockback"], idle, HitExitTime, set.TransitionDuration) ||
                states["Death"].transitions.Length != 0)
            {
                return false;
            }

            foreach (var attack in attacks)
            {
                if (!MatchesSingleExitTransition(states[attack.AnimationTrigger], idle, attack.ExitTime, attack.TransitionDuration))
                {
                    return false;
                }
            }

            var expectedAny = new List<Func<AnimatorStateTransition, bool>>();
            if (!string.IsNullOrWhiteSpace(set.HitTriggerParameter))
            {
                expectedAny.Add(transition => MatchesAnyStateTransition(transition, states["Hit"], set.HitTriggerParameter, AnimatorConditionMode.If, set.TransitionDuration, canTransitionToSelf: true, set.DeathParameter));
            }

            if (!string.IsNullOrWhiteSpace(set.KnockbackTriggerParameter))
            {
                expectedAny.Add(transition => MatchesAnyStateTransition(transition, states["Knockback"], set.KnockbackTriggerParameter, AnimatorConditionMode.If, set.TransitionDuration, canTransitionToSelf: true, set.DeathParameter));
            }

            if (!string.IsNullOrWhiteSpace(set.DeathParameter))
            {
                expectedAny.Add(transition => MatchesAnyStateTransition(transition, states["Death"], set.DeathParameter, AnimatorConditionMode.If, set.TransitionDuration, canTransitionToSelf: false));
            }

            foreach (var attack in attacks)
            {
                var attackState = states[attack.AnimationTrigger];
                var trigger = attack.AnimationTrigger;
                var duration = attack.TransitionDuration;
                var deathParameter = set.DeathParameter;
                expectedAny.Add(transition => MatchesAnyStateTransition(transition, attackState, trigger, AnimatorConditionMode.If, duration, canTransitionToSelf: true, deathParameter));
            }

            var anyStateTransitions = stateMachine.anyStateTransitions;
            if (anyStateTransitions.Length != expectedAny.Count)
            {
                return false;
            }

            for (var i = 0; i < expectedAny.Count; i++)
            {
                if (!expectedAny[i](anyStateTransitions[i]))
                {
                    return false;
                }
            }

            return true;
        }

        // Name-only on purpose: EnsureParameter never fixes a wrong-typed parameter either,
        // so requiring the type here would make the spec check fail (and rebuild) forever.
        private static bool HasParameter(AnimatorController controller, string name)
        {
            return string.IsNullOrWhiteSpace(name)
                || controller.parameters.Any(parameter => parameter.name == name);
        }

        private static bool MatchesSingleConditionTransitions(AnimatorState from, bool expectTransition, AnimatorState to, string parameter, AnimatorConditionMode mode, float threshold, float duration)
        {
            var transitions = from.transitions;
            if (!expectTransition)
            {
                return transitions.Length == 0;
            }

            if (transitions.Length != 1)
            {
                return false;
            }

            var transition = transitions[0];
            return transition != null
                && transition.destinationState == to
                && !transition.isExit
                && !transition.hasExitTime
                && Mathf.Approximately(transition.duration, duration)
                && transition.conditions.Length == 1
                && transition.conditions[0].mode == mode
                && transition.conditions[0].parameter == parameter
                && Mathf.Approximately(transition.conditions[0].threshold, threshold);
        }

        private static bool MatchesSingleExitTransition(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            if (from.transitions.Length != 1)
            {
                return false;
            }

            var transition = from.transitions[0];
            return transition != null
                && transition.destinationState == to
                && !transition.isExit
                && transition.hasExitTime
                && Mathf.Approximately(transition.exitTime, exitTime)
                && Mathf.Approximately(transition.duration, duration)
                && transition.conditions.Length == 0;
        }

        /// <summary>
        /// AnyState 전이가 생성기 사양과 같은가. <paramref name="notWhileDeadParameter"/>가 주어지면
        /// 「죽었으면 걸리지 않는다」 조건까지 요구한다 — 이 검사가 없으면 사양을 바꿔도
        /// <c>ControllerMatchesSpec</c>이 「이미 맞다」고 보고 재생성을 건너뛴다(2026-09-05에 실제로 겪었다).
        /// </summary>
        private static bool MatchesAnyStateTransition(
            AnimatorStateTransition transition,
            AnimatorState to,
            string parameter,
            AnimatorConditionMode mode,
            float duration,
            bool canTransitionToSelf,
            string notWhileDeadParameter = null)
        {
            var expectedConditions = string.IsNullOrWhiteSpace(notWhileDeadParameter) ? 1 : 2;
            if (transition == null
                || transition.destinationState != to
                || transition.hasExitTime
                || !Mathf.Approximately(transition.duration, duration)
                || transition.canTransitionToSelf != canTransitionToSelf
                || transition.conditions.Length != expectedConditions
                || transition.conditions[0].mode != mode
                || transition.conditions[0].parameter != parameter)
            {
                return false;
            }

            return expectedConditions == 1
                || (transition.conditions[1].mode == AnimatorConditionMode.IfNot
                    && transition.conditions[1].parameter == notWhileDeadParameter);
        }

        private static bool ConfigurePrefab(MonsterAnimationSet set, AnimatorController controller, List<string> issues)
        {
            var root = PrefabUtility.LoadPrefabContents(set.VisualPrefabPath);
            try
            {
                var changed = false;
                var target = ResolveAnimatorTarget(root.transform, set.AnimatorTarget);
                if (target == null)
                {
                    issues.Add($"Error: {set.MonsterId} animatorTarget not found: {set.AnimatorTarget}");
                    return false;
                }

                foreach (var animator in root.GetComponentsInChildren<Animator>(includeInactive: true))
                {
                    if (animator.transform == target)
                    {
                        continue;
                    }

                    UnityEngine.Object.DestroyImmediate(animator, allowDestroyingAssets: true);
                    changed = true;
                }

                var targetAnimator = target.GetComponent<Animator>();
                if (targetAnimator == null)
                {
                    targetAnimator = target.gameObject.AddComponent<Animator>();
                    changed = true;
                }

                var avatar = AssetDatabase.LoadAllAssetsAtPath(set.ModelAssetPath).OfType<Avatar>().FirstOrDefault();
                if (targetAnimator.runtimeAnimatorController != controller || targetAnimator.avatar != avatar || targetAnimator.applyRootMotion)
                {
                    targetAnimator.runtimeAnimatorController = controller;
                    targetAnimator.avatar = avatar;
                    targetAnimator.applyRootMotion = false;
                    changed = true;
                }

                var visual = root.GetComponent<CharacterActorVisual>();
                if (visual == null)
                {
                    visual = root.AddComponent<CharacterActorVisual>();
                    changed = true;
                }

                if (BindCharacterActorVisual(visual, root.transform, targetAnimator))
                {
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, set.VisualPrefabPath);
                }

                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static Transform ResolveAnimatorTarget(Transform root, string targetSpec)
        {
            if (string.IsNullOrWhiteSpace(targetSpec) || string.Equals(targetSpec, "Root", StringComparison.OrdinalIgnoreCase))
            {
                return root;
            }

            if (string.Equals(targetSpec, "FirstChild", StringComparison.OrdinalIgnoreCase))
            {
                return FindLikelyModelRoot(root) ?? (root.childCount > 0 ? root.GetChild(0) : root);
            }

            const string childPrefix = "Child:";
            if (targetSpec.StartsWith(childPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return FindChildRecursive(root, targetSpec.Substring(childPrefix.Length));
            }

            return root.Find(targetSpec) ?? FindChildRecursive(root, targetSpec);
        }

        private static bool BindCharacterActorVisual(CharacterActorVisual visual, Transform root, Animator animator)
        {
            if (visual == null)
            {
                return false;
            }

            var serialized = new SerializedObject(visual);
            var changed = SetObjectReference(serialized, "animator", animator);
            changed |= SetObjectReference(serialized, "hitCenterAnchor", FindChildRecursive(root, "HitCenter"));
            changed |= SetObjectReference(serialized, "groundAnchor", FindChildRecursive(root, "Ground"));
            changed |= SetObjectReference(serialized, "headAnchor", FindChildRecursive(root, "Head"));
            changed |= SetObjectReference(serialized, "attackSourceAnchor", FindChildRecursive(root, "AttackSource"));
            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return changed;
        }

        private static bool SetObjectReference(SerializedObject serialized, string propertyName, UnityEngine.Object value)
        {
            var property = serialized.FindProperty(propertyName);
            if (property == null || property.objectReferenceValue == value)
            {
                return false;
            }

            property.objectReferenceValue = value;
            return true;
        }

        private static Transform FindLikelyModelRoot(Transform root)
        {
            if (root == null)
            {
                return null;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (string.Equals(child.name, "VFXRoot", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (child.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) != null ||
                    child.GetComponentInChildren<MeshRenderer>(includeInactive: true) != null)
                {
                    return child;
                }
            }

            return null;
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            foreach (Transform child in parent)
            {
                if (string.Equals(child.name, name, StringComparison.Ordinal))
                {
                    return child;
                }

                var nested = FindChildRecursive(child, name);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private static AnimationClip FindClip(AnimationClip[] clips, string clipName, string monsterId, List<string> issues)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return null;
            }

            var clip = clips.FirstOrDefault(candidate => string.Equals(candidate.name, clipName, StringComparison.Ordinal));
            if (clip == null)
            {
                issues.Add($"Warning: {monsterId} animation clip not found: {clipName}");
            }

            return clip;
        }

        private static AnimationClip FindOptionalClip(AnimationClip[] clips, string clipName)
        {
            if (clips == null || string.IsNullOrWhiteSpace(clipName))
            {
                return null;
            }

            return clips.FirstOrDefault(candidate => string.Equals(candidate.name, clipName, StringComparison.Ordinal));
        }

        private static AnimatorState AddState(AnimatorStateMachine stateMachine, string stateName, Motion motion, Vector3 position)
        {
            var state = stateMachine.AddState(stateName, position);
            state.motion = motion;
            state.writeDefaultValues = true;
            return state;
        }

        private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
        {
            if (string.IsNullOrWhiteSpace(name) || controller.parameters.Any(parameter => parameter.name == name))
            {
                return;
            }

            controller.AddParameter(name, type);
        }

        private static void ClearStateMachine(AnimatorStateMachine stateMachine)
        {
            foreach (var transition in stateMachine.anyStateTransitions.ToArray())
            {
                stateMachine.RemoveAnyStateTransition(transition);
            }

            foreach (var child in stateMachine.states.ToArray())
            {
                stateMachine.RemoveState(child.state);
            }
        }

        private static void AddFloatTransition(AnimatorState from, AnimatorState to, string parameter, AnimatorConditionMode mode, float threshold, bool hasExitTime, float duration)
        {
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            var transition = from.AddTransition(to);
            transition.hasExitTime = hasExitTime;
            transition.duration = duration;
            transition.AddCondition(mode, threshold, parameter);
        }

        /// <summary>
        /// AnyState 트리거 전이. <paramref name="notWhileDeadParameter"/>를 주면 <b>죽은 뒤에는 걸리지 않는다</b>
        /// (2026-09-05).
        ///
        /// <para>🔴 이 가드가 없어서 사망 애니메이션이 재생되지 않았다: 피격·넉백·공격 전이가 전부
        /// AnyState에서 조건 하나(자기 트리거)만 보고 있었고, Death는 <b>AnyState의 또 다른 목적지</b>일 뿐이라
        /// 우선권이 없다. 유니티 트리거는 소비될 때까지 남으므로, 치명타로 죽는 순간까지 쌓여 있던
        /// 피격 트리거가 Death에 들어간 직후 다시 발화해 시체가 「맞는 자세」로 돌아갔다 —
        /// 죽는 데 여러 대가 필요한 정예(두억시니 44)에서 특히 잘 재현된다.</para>
        /// </summary>
        private static void AddTriggerAnyTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState to,
            string parameter,
            float duration,
            bool canTransitionToSelf,
            string notWhileDeadParameter = null)
        {
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            var transition = stateMachine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.canTransitionToSelf = canTransitionToSelf;
            transition.AddCondition(AnimatorConditionMode.If, 0f, parameter);
            if (!string.IsNullOrWhiteSpace(notWhileDeadParameter))
            {
                transition.AddCondition(AnimatorConditionMode.IfNot, 0f, notWhileDeadParameter);
            }
        }

        private static void AddBoolAnyTransition(AnimatorStateMachine stateMachine, AnimatorState to, string parameter, bool expected, float duration)
        {
            if (string.IsNullOrWhiteSpace(parameter))
            {
                return;
            }

            var transition = stateMachine.AddAnyStateTransition(to);
            transition.hasExitTime = false;
            transition.duration = duration;
            transition.canTransitionToSelf = false;
            transition.AddCondition(expected ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameter);
        }

        private static void AddExitTransition(AnimatorState from, AnimatorState to, float exitTime, float duration)
        {
            var transition = from.AddTransition(to);
            transition.hasExitTime = true;
            transition.exitTime = exitTime;
            transition.duration = duration;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            EnsureFolder(parent);
            if (!string.IsNullOrWhiteSpace(parent) && !string.IsNullOrWhiteSpace(name) && !AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parent, name);
            }
        }

        private static IReadOnlyList<IReadOnlyDictionary<string, string>> ReadCsv(string path)
        {
            var lines = File.ReadAllLines(path)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            if (lines.Length == 0)
            {
                return Array.Empty<IReadOnlyDictionary<string, string>>();
            }

            var headers = ParseCsvLine(lines[0]);
            var rows = new List<IReadOnlyDictionary<string, string>>();
            for (var lineIndex = 1; lineIndex < lines.Length; lineIndex++)
            {
                var values = ParseCsvLine(lines[lineIndex]);
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count; i++)
                {
                    row[headers[i]] = i < values.Count ? values[i] : string.Empty;
                }

                rows.Add(row);
            }

            return rows;
        }

        private static IReadOnlyList<string> ParseCsvLine(string line)
        {
            var values = new List<string>();
            var value = new System.Text.StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var character = line[i];
                if (character == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (character == ',' && !inQuotes)
                {
                    values.Add(value.ToString().Trim());
                    value.Length = 0;
                }
                else
                {
                    value.Append(character);
                }
            }

            values.Add(value.ToString().Trim());
            return values;
        }

        private static string Read(IReadOnlyDictionary<string, string> row, string key)
        {
            return row.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static bool ReadBool(IReadOnlyDictionary<string, string> row, string key, bool defaultValue)
        {
            var value = Read(row, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
        }

        private static float ReadFloat(IReadOnlyDictionary<string, string> row, string key, float defaultValue)
        {
            var value = Read(row, key);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : defaultValue;
        }

        private sealed class MonsterAnimationSet
        {
            public string MonsterId;
            public bool Enabled;
            public string ModelAssetPath;
            public string VisualPrefabPath;
            public string ControllerPath;
            public string AnimatorTarget;
            public string MoveSpeedParameter;
            public string HitTriggerParameter;
            public string KnockbackTriggerParameter;
            public string DeathParameter;
            public string IdleClip;
            public string MoveClip;
            public string HitClip;
            public string KnockbackClip;
            public string DeathClip;
            public float MoveSpeedThreshold;
            public float TransitionDuration;

            public static MonsterAnimationSet FromRow(IReadOnlyDictionary<string, string> row)
            {
                return new MonsterAnimationSet
                {
                    MonsterId = Read(row, "monsterId"),
                    Enabled = ReadBool(row, "enabled", true),
                    ModelAssetPath = Read(row, "modelAssetPath"),
                    VisualPrefabPath = Read(row, "visualPrefabPath"),
                    ControllerPath = Read(row, "controllerPath"),
                    AnimatorTarget = Read(row, "animatorTarget"),
                    MoveSpeedParameter = Read(row, "moveSpeedParameter"),
                    HitTriggerParameter = Read(row, "hitTriggerParameter"),
                    KnockbackTriggerParameter = string.IsNullOrWhiteSpace(Read(row, "knockbackTriggerParameter")) ? "KnockbackTrigger" : Read(row, "knockbackTriggerParameter"),
                    DeathParameter = Read(row, "deathParameter"),
                    IdleClip = Read(row, "idleClip"),
                    MoveClip = Read(row, "moveClip"),
                    HitClip = Read(row, "hitClip"),
                    KnockbackClip = Read(row, "knockbackClip"),
                    DeathClip = Read(row, "deathClip"),
                    MoveSpeedThreshold = ReadFloat(row, "moveSpeedThreshold", DefaultMoveSpeedThreshold),
                    TransitionDuration = ReadFloat(row, "transitionDuration", DefaultTransitionDuration)
                };
            }
        }

        private sealed class MonsterAttackAnimationClip
        {
            public string MonsterId;
            public string AnimationTrigger;
            public string ClipName;
            public float ExitTime;
            public float TransitionDuration;

            public static MonsterAttackAnimationClip FromRow(IReadOnlyDictionary<string, string> row)
            {
                return new MonsterAttackAnimationClip
                {
                    MonsterId = Read(row, "monsterId"),
                    AnimationTrigger = Read(row, "animationTrigger"),
                    ClipName = Read(row, "clipName"),
                    ExitTime = ReadFloat(row, "exitTime", DefaultAttackExitTime),
                    TransitionDuration = ReadFloat(row, "transitionDuration", DefaultTransitionDuration)
                };
            }
        }
    }

    /// <summary>
    /// Reruns the monster animator setup whenever one of its source CSVs is (re)imported.
    /// Deferred to the next editor update tick so the rebuild never runs inside the import
    /// pipeline (update, unlike delayCall, also ticks while the editor is unfocused).
    /// </summary>
    internal sealed class MonsterAnimationCsvPostprocessor : AssetPostprocessor
    {
        private static bool setupQueued;

        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            if (setupQueued || !importedAssets.Any(MonsterAnimatorSetupUtility.IsSourceCsv))
            {
                return;
            }

            setupQueued = true;
            EditorApplication.update += RunQueuedSetup;
        }

        private static void RunQueuedSetup()
        {
            EditorApplication.update -= RunQueuedSetup;
            setupQueued = false;
            MonsterAnimatorSetupUtility.EnsureAssetsFromMenu();
        }
    }
}
#endif
