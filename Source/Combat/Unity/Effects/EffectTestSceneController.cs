using System;
using System.Collections;
using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Cards;
using SeoulPlayup.Map.Unity;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Small in-Editor/dev scene harness for previewing combat effect presentation mappings.
    /// Intended for Assets/Scenes/Dev/EffectTest.unity.
    /// </summary>
    public sealed class EffectTestSceneController : MonoBehaviour
    {
        public enum EffectTestTargetAnchor
        {
            Center,
            Player,
            Monster,
            Field
        }

        [SerializeField] private EffectPresentationController presentation;
        [SerializeField] private Transform previewCenter;
        [SerializeField] private Transform playerAnchor;
        [SerializeField] private Transform monsterAnchor;
        [SerializeField] private Transform fieldAnchor;
        [SerializeField] private Transform playerMoveDirectionTarget;
        [SerializeField] private CharacterActorVisual playerVisual;
        [SerializeField] private CharacterActorVisual monsterVisual;
        [SerializeField] private bool snapFacingToHexSides = true;
        [SerializeField] private float hexFacingYawOffset = 90f;
        [SerializeField] private GameObject[] playModeHiddenGuides = Array.Empty<GameObject>();
        [SerializeField] private Camera previewCamera;
        [SerializeField] private Vector3 cameraLookOffset = new Vector3(0f, 0.9f, 0f);
        [SerializeField] private float cameraYaw;
        [SerializeField] private float cameraPitch = 55f;
        [SerializeField] private float cameraDistance = 12f;
        [SerializeField] private float gridSpacing = 1.73f;
        [SerializeField] private int areaRadius = 1;
        [SerializeField] private bool autoPlay;
        [SerializeField] private float autoPlayInterval = 1.2f;
        [SerializeField] private VfxAssignment[] vfxAssignments = Array.Empty<VfxAssignment>();

        private readonly List<Vector3> areaTilePositions = new List<Vector3>();
        private int selectedIndex;
        private float autoPlayTimer;
        private Vector2 scrollPosition;
        private EffectVfxCatalog sceneOverrideCatalog;
        private bool guidesWereHidden;

        private static readonly Sample[] Samples =
        {
            new Sample("Player Hit", EffectKind.Damage, "", 8, 8, 0, false, EffectTestTargetAnchor.Player, "player"),
            new Sample("Monster Hit", EffectKind.Damage, CardIds.Sweep, 8, 8, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Double Hit", EffectKind.Damage, CardIds.DoubleHit, 8, 8, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Block", EffectKind.Block, "", 5, 5, 0, false, EffectTestTargetAnchor.Player, "player"),
            new Sample("Heal", EffectKind.Heal, "", 6, 6, 0, false, EffectTestTargetAnchor.Player, "player"),
            new Sample("Heal Field", EffectKind.Heal, "field.heal", 6, 6, 1, true, EffectTestTargetAnchor.Field, "field"),
            new Sample("Fog Reveal", EffectKind.FogReveal, "field.fog-reveal", 3, 3, 1, true, EffectTestTargetAnchor.Field, "field"),
            new Sample("AoE Tile Highlight", EffectKind.Damage, "area.highlight", 4, 4, 1, true, EffectTestTargetAnchor.Field, "field"),
            new Sample("Reflect Damage", EffectKind.ReflectDamage, "", 4, 4, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Immobilize", StatusEffectKind.Immobilize, "", 1, 1, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Flashbang Field", StatusEffectKind.Stun, "field.immobilize.flashbang", 1, 1, 1, true, EffectTestTargetAnchor.Field, "field"),
            new Sample("Agility", StatusEffectKind.Agility, "", 2, 2, 0, false, EffectTestTargetAnchor.Player, "player"),
            new Sample("Knockback", EffectKind.Knockback, "knockback", 1, 1, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Knockback Impact", EffectKind.Damage, "knockback.impact", 4, 4, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Poison", StatusEffectKind.Poison, "", 2, 2, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Stun", StatusEffectKind.Stun, "", 1, 1, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Slow", StatusEffectKind.Slow, "", 1, 1, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Rupture", StatusEffectKind.Rupture, "", 2, 2, 0, false, EffectTestTargetAnchor.Monster, "monster-01"),
            new Sample("Damage Field", EffectKind.Damage, "field.damage", 4, 4, 1, true, EffectTestTargetAnchor.Field, "field"),
            new Sample("Fog Field", EffectKind.FogReveal, "field.fog-reveal.object", 3, 3, 1, true, EffectTestTargetAnchor.Field, "field"),
            new Sample("Trap Trigger", EffectKind.Damage, "trap.", 2, 2, 1, true, EffectTestTargetAnchor.Field, "field"),
            new Sample("Player Death", EffectKind.Damage, "player.death", 10, 10, 0, false, EffectTestTargetAnchor.Player, "player"),
            new Sample("Treasure Chest", EffectKind.FogReveal, "treasure.chest", 0, 0, 0, false, EffectTestTargetAnchor.Field, "field"),
            new Sample("Player Move", EffectKind.Push, "player.move", 0, 0, 0, false, EffectTestTargetAnchor.Player, "player")
        };

        private void Reset()
        {
            presentation = GetComponentInChildren<EffectPresentationController>();
            previewCenter = transform;
            previewCamera = Camera.main;
        }

        private void Awake()
        {
            ResolveReferences();
            EnsureAssignments();
            ApplyCameraOrbit();
        }

        private void OnDestroy()
        {
            if (sceneOverrideCatalog != null)
            {
                Destroy(sceneOverrideCatalog);
            }
        }

        private void Update()
        {
            HideGuidesDuringPlay();
            HandleKeyboard();
            ApplyCameraOrbit();

            if (!autoPlay)
            {
                return;
            }

            autoPlayTimer += Time.deltaTime;
            if (autoPlayTimer >= Mathf.Max(0.2f, autoPlayInterval))
            {
                autoPlayTimer = 0f;
                PlayCurrent();
                SelectNext();
            }
        }

        private void OnGUI()
        {
            const int width = 360;
            GUILayout.BeginArea(new Rect(16f, 16f, width, Screen.height - 32f), GUI.skin.box);
            GUILayout.Label("Effect Test Scene");
            GUILayout.Label("샘플 선택: ↑/↓, Space 재생, A 자동재생, C 클리어");
            GUILayout.Label("Camera: Q/E rotate, R/F pitch, +/- zoom");

            EnsureAssignments();
            var selected = GetSample(selectedIndex);
            GUILayout.Space(8f);
            GUILayout.Label($"샘플: {selectedIndex + 1}/{SampleCount}");
            GUILayout.Label(selected.Label);
            GUILayout.Label($"Kind: {selected.Kind}");
            GUILayout.Label($"SourceRef: {(string.IsNullOrWhiteSpace(selected.SourceRef) ? "(kind default)" : selected.SourceRef)}");
            GUILayout.Label($"Target: {selected.TargetAnchor} / {selected.TargetUnitId}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("?? Prev"))
            {
                SelectPrevious();
            }

            if (GUILayout.Button("Play"))
            {
                PlayCurrent();
            }

            if (GUILayout.Button("Next"))
            {
                SelectNext();
            }
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Play All Grid"))
            {
                PlayAllGrid();
            }

            if (GUILayout.Button("Clear"))
            {
                Clear();
            }
            GUILayout.EndHorizontal();

            autoPlay = GUILayout.Toggle(autoPlay, $"Auto Play ({autoPlayInterval:0.0}s)");
            GUILayout.Space(8f);

            GUILayout.Label("Camera");
            cameraYaw = Slider("Yaw", cameraYaw, -180f, 180f);
            cameraPitch = Slider("Pitch", cameraPitch, 15f, 80f);
            cameraDistance = Slider("Distance", cameraDistance, 4f, 24f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Front"))
            {
                SetCameraPreset(0f, 55f, 12f);
            }

            if (GUILayout.Button("Quarter"))
            {
                SetCameraPreset(35f, 55f, 12f);
            }

            if (GUILayout.Button("Top"))
            {
                SetCameraPreset(0f, 78f, 13f);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8f);

            scrollPosition = GUILayout.BeginScrollView(scrollPosition);
            for (var i = 0; i < SampleCount; i++)
            {
                if (GUILayout.Button($"{i + 1:00}. {GetSample(i).Label}"))
                {
                    selectedIndex = i;
                    PlayCurrent();
                }
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void HideGuidesDuringPlay()
        {
            if (guidesWereHidden)
            {
                return;
            }

            if (playModeHiddenGuides == null)
            {
                return;
            }

            foreach (var guide in playModeHiddenGuides)
            {
                if (guide != null)
                {
                    guide.SetActive(false);
                }
            }

            guidesWereHidden = true;
        }

        [ContextMenu("Play Current")]
        public void PlayCurrent()
        {
            ResolveReferences();
            if (presentation == null)
            {
                Debug.LogWarning("EffectTestSceneController requires an EffectPresentationController.", this);
                return;
            }

            EnsureAssignments();
            ApplySceneAssignmentsToPresentation();
            var sample = GetSample(selectedIndex);
            var resultEvent = sample.ToEvent(areaRadius);
            var worldPosition = ResolveSamplePosition(sample);
            PrepareActorsForSample(sample, worldPosition, out var facingRotation);
            if (sample.IsArea)
            {
                BuildAreaTiles(worldPosition, Mathf.Max(1, resultEvent.Radius));
                presentation.PlayArea(resultEvent, worldPosition, areaTilePositions, facingRotation);
            }
            else
            {
                presentation.Play(resultEvent, worldPosition, facingRotation);
            }
        }

        [ContextMenu("Play All Grid")]
        public void PlayAllGrid()
        {
            ResolveReferences();
            if (presentation == null)
            {
                return;
            }

            EnsureAssignments();
            ApplySceneAssignmentsToPresentation();
            Clear();
            var origin = ResolveCenter();
            var columns = Mathf.CeilToInt(Mathf.Sqrt(SampleCount));
            for (var i = 0; i < SampleCount; i++)
            {
                var x = i % columns;
                var z = i / columns;
                var offset = new Vector3(
                    (x - (columns - 1) * 0.5f) * gridSpacing,
                    0f,
                    z * gridSpacing);
                var sample = GetSample(i);
                var resultEvent = sample.ToEvent(areaRadius);
                var position = origin + offset;
                PrepareActorsForSample(sample, position, out var facingRotation);
                if (sample.IsArea)
                {
                    BuildAreaTiles(position, Mathf.Max(1, resultEvent.Radius));
                    presentation.PlayArea(resultEvent, position, areaTilePositions, facingRotation);
                }
                else
                {
                    presentation.Play(resultEvent, position, facingRotation);
                }
            }
        }

        [ContextMenu("Clear")]
        public void Clear()
        {
            if (presentation != null)
            {
                presentation.ClearSpawnedEffects();
            }
        }

        private void ResolveReferences()
        {
            if (presentation == null)
            {
                presentation = GetComponentInChildren<EffectPresentationController>();
            }

            if (previewCenter == null)
            {
                previewCenter = transform;
            }

            if (previewCamera == null)
            {
                previewCamera = Camera.main;
            }

            if (playerVisual == null && playerAnchor != null)
            {
                playerVisual = playerAnchor.GetComponentInChildren<CharacterActorVisual>(includeInactive: true);
            }

            if (playerMoveDirectionTarget == null)
            {
                playerMoveDirectionTarget = monsterAnchor != null ? monsterAnchor : fieldAnchor;
            }

            if (monsterVisual == null && monsterAnchor != null)
            {
                monsterVisual = monsterAnchor.GetComponentInChildren<CharacterActorVisual>(includeInactive: true);
            }
        }

        private Vector3 ResolveCenter()
        {
            return previewCenter != null ? previewCenter.position : transform.position;
        }

        private Vector3 ResolveSamplePosition(Sample sample)
        {
            switch (sample.TargetAnchor)
            {
                case EffectTestTargetAnchor.Player:
                    if (playerAnchor != null)
                    {
                        return playerAnchor.position;
                    }

                    break;
                case EffectTestTargetAnchor.Monster:
                    if (monsterAnchor != null)
                    {
                        return monsterAnchor.position;
                    }

                    break;
                case EffectTestTargetAnchor.Field:
                    if (fieldAnchor != null)
                    {
                        return fieldAnchor.position;
                    }

                    break;
            }

            return ResolveCenter();
        }


        private void PrepareActorsForSample(Sample sample, Vector3 targetWorldPosition, out Quaternion facingRotation)
        {
            if (IsPlayerMoveSample(sample))
            {
                PreparePlayerMovePreview(out facingRotation);
                return;
            }

            if (!TryResolveSourceAnchor(sample, out var sourceAnchor) || sourceAnchor == null)
            {
                facingRotation = Quaternion.identity;
                TriggerTargetReaction(sample);
                return;
            }

            facingRotation = ResolveHexFacingRotation(sourceAnchor.position, targetWorldPosition);
            ApplyActorFacing(sourceAnchor, facingRotation);
            TriggerSourceAction(sample, sourceAnchor);

            var targetAnchor = ResolveTargetActorAnchor(sample);
            if (targetAnchor != null && targetAnchor != sourceAnchor)
            {
                var targetFacing = ResolveHexFacingRotation(targetAnchor.position, sourceAnchor.position);
                ApplyActorFacing(targetAnchor, targetFacing);
            }

            TriggerTargetReaction(sample);
        }


        private void PreparePlayerMovePreview(out Quaternion facingRotation)
        {
            var moveDirection = ResolvePlayerMoveDirection();
            var playerFacingRotation = ResolveHexFacingRotation(moveDirection);
            facingRotation = ResolveHexFacingRotation(-moveDirection);

            ApplyActorFacing(playerAnchor, playerFacingRotation);
            PulseMove(playerAnchor);
        }

        private Vector3 ResolvePlayerMoveDirection()
        {
            if (playerAnchor != null && playerMoveDirectionTarget != null)
            {
                var direction = playerMoveDirectionTarget.position - playerAnchor.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    return direction.normalized;
                }
            }

            return Vector3.right;
        }

        private static bool IsPlayerMoveSample(Sample sample)
        {
            return string.Equals(sample.SourceRef, "player.move", StringComparison.Ordinal);
        }

        private bool TryResolveSourceAnchor(Sample sample, out Transform sourceAnchor)
        {
            switch (sample.TargetAnchor)
            {
                case EffectTestTargetAnchor.Monster:
                case EffectTestTargetAnchor.Field:
                    sourceAnchor = playerAnchor;
                    return sourceAnchor != null;
                case EffectTestTargetAnchor.Player:
                    sourceAnchor = IsSelfAuthoredPlayerEffect(sample) ? playerAnchor : monsterAnchor;
                    return sourceAnchor != null;
                default:
                    sourceAnchor = playerAnchor != null ? playerAnchor : monsterAnchor;
                    return sourceAnchor != null;
            }
        }

        private Transform ResolveTargetActorAnchor(Sample sample)
        {
            switch (sample.TargetAnchor)
            {
                case EffectTestTargetAnchor.Player:
                    return playerAnchor;
                case EffectTestTargetAnchor.Monster:
                    return monsterAnchor;
                default:
                    return null;
            }
        }

        private static bool IsSelfAuthoredPlayerEffect(Sample sample)
        {
            if (sample.TargetAnchor != EffectTestTargetAnchor.Player)
            {
                return false;
            }

            return sample.Kind == EffectKind.Block ||
                   sample.Kind == EffectKind.Heal ||
                   sample.StatusKind == StatusEffectKind.Agility ||
                   string.Equals(sample.SourceRef, "player.death", StringComparison.Ordinal) ||
                   string.Equals(sample.SourceRef, "player.move", StringComparison.Ordinal);
        }

        private Quaternion ResolveHexFacingRotation(Vector3 fromWorld, Vector3 toWorld)
        {
            return CombatFacingUtility.ResolveHexSideRotation(fromWorld, toWorld, snapFacingToHexSides, hexFacingYawOffset);
        }

        private Quaternion ResolveHexFacingRotation(Vector3 direction)
        {
            return CombatFacingUtility.ResolveHexSideRotation(direction, snapFacingToHexSides, hexFacingYawOffset);
        }

        private void ApplyActorFacing(Transform actorAnchor, Quaternion facingRotation)
        {
            if (actorAnchor == null)
            {
                return;
            }

            actorAnchor.rotation = facingRotation;
        }

        private void TriggerSourceAction(Sample sample, Transform sourceAnchor)
        {
            if (sample.Kind == EffectKind.Push
                || sample.Kind == EffectKind.Knockback
                || string.Equals(sample.SourceRef, "player.move", StringComparison.Ordinal))
            {
                PulseMove(sourceAnchor);
                return;
            }

            if (sample.Kind == EffectKind.Damage && !string.Equals(sample.SourceRef, "player.death", StringComparison.Ordinal))
            {
                TriggerAttack(sourceAnchor);
            }
        }

        private void TriggerTargetReaction(Sample sample)
        {
            var targetAnchor = ResolveTargetActorAnchor(sample);
            if (string.Equals(sample.SourceRef, "player.death", StringComparison.Ordinal))
            {
                TriggerDead(targetAnchor);
                return;
            }

            if (sample.Kind == EffectKind.Damage || sample.StatusKind == StatusEffectKind.Poison || sample.StatusKind == StatusEffectKind.Rupture)
            {
                TriggerHit(targetAnchor);
            }
            else if (sample.Kind == EffectKind.Push || sample.Kind == EffectKind.Knockback)
            {
                PulseMove(targetAnchor);
            }
        }

        private void TriggerAttack(Transform actorAnchor)
        {
            var visual = ResolveActorVisual(actorAnchor);
            if (visual != null)
            {
                visual.TriggerAttack();
                return;
            }

            TriggerAnimator(actorAnchor, "AttackTrigger");
        }

        private void TriggerHit(Transform actorAnchor)
        {
            var visual = ResolveActorVisual(actorAnchor);
            if (visual != null)
            {
                visual.TriggerHit();
                return;
            }

            TriggerAnimator(actorAnchor, "HitTrigger");
        }

        private void TriggerDead(Transform actorAnchor)
        {
            var visual = ResolveActorVisual(actorAnchor);
            if (visual != null)
            {
                visual.TriggerDead();
                return;
            }

            TriggerAnimator(actorAnchor, "Dead");
        }

        private void PulseMove(Transform actorAnchor)
        {
            var visual = ResolveActorVisual(actorAnchor);
            if (visual != null)
            {
                visual.PulseMove();
                return;
            }

            SetAnimatorFloat(actorAnchor, "MoveSpeed", 1f);
        }


        private static void TriggerAnimator(Transform actorAnchor, string parameterName)
        {
            var animator = actorAnchor != null ? actorAnchor.GetComponentInChildren<Animator>(includeInactive: true) : null;
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            if (!TryFindAnimatorParameter(animator, parameterName, out var parameter))
            {
                return;
            }

            if (parameter.type == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger(parameterName);
            }
            else if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(parameterName, true);
            }
        }

        private void SetAnimatorFloat(Transform actorAnchor, string parameterName, float value)
        {
            var animator = actorAnchor != null ? actorAnchor.GetComponentInChildren<Animator>(includeInactive: true) : null;
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return;
            }

            if (!TryFindAnimatorParameter(animator, parameterName, out var parameter) ||
                parameter.type != AnimatorControllerParameterType.Float)
            {
                return;
            }

            animator.SetFloat(parameterName, value);
            if (Application.isPlaying && isActiveAndEnabled && value > 0f)
            {
                StartCoroutine(ResetAnimatorFloatAfter(animator, parameterName, 0.25f));
            }
        }

        private static IEnumerator ResetAnimatorFloatAfter(Animator animator, string parameterName, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (animator != null &&
                TryFindAnimatorParameter(animator, parameterName, out var parameter) &&
                parameter.type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(parameterName, 0f);
            }
        }

        private static bool TryFindAnimatorParameter(Animator animator, string parameterName, out AnimatorControllerParameter parameter)
        {
            var parameters = animator.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName)
                {
                    parameter = parameters[i];
                    return true;
                }
            }

            parameter = null;
            return false;
        }

        private CharacterActorVisual ResolveActorVisual(Transform actorAnchor)
        {
            if (actorAnchor == null)
            {
                return null;
            }

            if (actorAnchor == playerAnchor)
            {
                if (playerVisual == null)
                {
                    playerVisual = playerAnchor.GetComponentInChildren<CharacterActorVisual>(includeInactive: true);
                }

                return playerVisual;
            }

            if (actorAnchor == monsterAnchor)
            {
                if (monsterVisual == null)
                {
                    monsterVisual = monsterAnchor.GetComponentInChildren<CharacterActorVisual>(includeInactive: true);
                }

                return monsterVisual;
            }

            return actorAnchor.GetComponentInChildren<CharacterActorVisual>(includeInactive: true);
        }

        private static int SampleCount => Samples.Length;

        private void EnsureAssignments()
        {
            if (vfxAssignments != null && vfxAssignments.Length == Samples.Length)
            {
                return;
            }

            vfxAssignments = new VfxAssignment[Samples.Length];
            for (var i = 0; i < Samples.Length; i++)
            {
                vfxAssignments[i] = VfxAssignment.FromSample(Samples[i]);
            }
        }

        private Sample GetSample(int index)
        {
            if (vfxAssignments != null &&
                index >= 0 &&
                index < vfxAssignments.Length &&
                vfxAssignments[index] != null)
            {
                return vfxAssignments[index].ToSample();
            }

            return Samples[Mathf.Clamp(index, 0, Samples.Length - 1)];
        }

        [ContextMenu("Reset VFX Assignments")]
        public void ResetVfxAssignments()
        {
            vfxAssignments = new VfxAssignment[Samples.Length];
            for (var i = 0; i < Samples.Length; i++)
            {
                vfxAssignments[i] = VfxAssignment.FromSample(Samples[i]);
            }
        }

        private void ApplySceneAssignmentsToPresentation()
        {
            if (presentation == null)
            {
                return;
            }

            var entries = new List<EffectVfxCatalog.Entry>();
            if (vfxAssignments != null)
            {
                foreach (var assignment in vfxAssignments)
                {
                    if (assignment != null && assignment.TryCreateCatalogEntry(out var entry))
                    {
                        entries.Add(entry);
                    }
                }
            }

            var defaultCatalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            if (defaultCatalog != null)
            {
                entries.AddRange(defaultCatalog.Entries);
            }

            if (sceneOverrideCatalog == null)
            {
                sceneOverrideCatalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
                sceneOverrideCatalog.hideFlags = HideFlags.HideAndDontSave;
            }

            sceneOverrideCatalog.SetEntries(entries.ToArray());
            presentation.SetVfxCatalog(sceneOverrideCatalog);
        }

        private void ApplyCameraOrbit()
        {
            if (previewCamera == null)
            {
                return;
            }

            var target = ResolveCenter() + cameraLookOffset;
            var clampedPitch = Mathf.Clamp(cameraPitch, 15f, 80f);
            var clampedDistance = Mathf.Clamp(cameraDistance, 4f, 24f);
            var rotation = Quaternion.Euler(clampedPitch, cameraYaw, 0f);
            previewCamera.transform.position = target + rotation * new Vector3(0f, 0f, -clampedDistance);
            previewCamera.transform.rotation = Quaternion.LookRotation(target - previewCamera.transform.position, Vector3.up);
            cameraPitch = clampedPitch;
            cameraDistance = clampedDistance;
        }

        private void SetCameraPreset(float yaw, float pitch, float distance)
        {
            cameraYaw = yaw;
            cameraPitch = pitch;
            cameraDistance = distance;
            ApplyCameraOrbit();
        }

        private static float Slider(string label, float value, float min, float max)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {value:0.0}", GUILayout.Width(110f));
            value = GUILayout.HorizontalSlider(value, min, max);
            GUILayout.EndHorizontal();
            return value;
        }

        private void BuildAreaTiles(Vector3 center, int radius)
        {
            areaTilePositions.Clear();
            areaTilePositions.Add(center);
            for (var ring = 1; ring <= radius; ring++)
            {
                areaTilePositions.Add(center + new Vector3(gridSpacing * 0.5f * ring, 0f, gridSpacing * 0.86f * ring));
                areaTilePositions.Add(center + new Vector3(gridSpacing * ring, 0f, 0f));
                areaTilePositions.Add(center + new Vector3(gridSpacing * 0.5f * ring, 0f, -gridSpacing * 0.86f * ring));
                areaTilePositions.Add(center + new Vector3(-gridSpacing * 0.5f * ring, 0f, -gridSpacing * 0.86f * ring));
                areaTilePositions.Add(center + new Vector3(-gridSpacing * ring, 0f, 0f));
                areaTilePositions.Add(center + new Vector3(-gridSpacing * 0.5f * ring, 0f, gridSpacing * 0.86f * ring));
            }
        }

        private void SelectPrevious()
        {
            selectedIndex = (selectedIndex - 1 + SampleCount) % SampleCount;
        }

        private void SelectNext()
        {
            selectedIndex = (selectedIndex + 1) % SampleCount;
        }

        private void HandleKeyboard()
        {
            if (WasPressed(KeyCode.LeftArrow))
            {
                SelectPrevious();
            }

            if (WasPressed(KeyCode.RightArrow))
            {
                SelectNext();
            }

            if (WasPressed(KeyCode.Space))
            {
                PlayCurrent();
            }

            if (WasPressed(KeyCode.A))
            {
                PlayAllGrid();
            }

            if (WasPressed(KeyCode.C))
            {
                Clear();
            }

            cameraYaw += AxisHeld(KeyCode.Q, KeyCode.E) * 75f * Time.deltaTime;
            cameraPitch += AxisHeld(KeyCode.F, KeyCode.R) * 55f * Time.deltaTime;
            cameraDistance += AxisHeld(KeyCode.Equals, KeyCode.Minus) * 8f * Time.deltaTime;
        }

        private static bool WasPressed(KeyCode key)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            switch (key)
            {
                case KeyCode.LeftArrow:
                    return keyboard.leftArrowKey.wasPressedThisFrame;
                case KeyCode.RightArrow:
                    return keyboard.rightArrowKey.wasPressedThisFrame;
                case KeyCode.Space:
                    return keyboard.spaceKey.wasPressedThisFrame;
                case KeyCode.A:
                    return keyboard.aKey.wasPressedThisFrame;
                case KeyCode.C:
                    return keyboard.cKey.wasPressedThisFrame;
                default:
                    return false;
            }
#else
            return Input.GetKeyDown(key);
#endif
        }

        private static float AxisHeld(KeyCode negativeKey, KeyCode positiveKey)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return 0f;
            }

            var value = 0f;
            if (IsPressed(keyboard, negativeKey))
            {
                value -= 1f;
            }

            if (IsPressed(keyboard, positiveKey))
            {
                value += 1f;
            }

            return value;
#else
            var value = 0f;
            if (Input.GetKey(negativeKey))
            {
                value -= 1f;
            }

            if (Input.GetKey(positiveKey))
            {
                value += 1f;
            }

            return value;
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static bool IsPressed(Keyboard keyboard, KeyCode key)
        {
            switch (key)
            {
                case KeyCode.Q:
                    return keyboard.qKey.isPressed;
                case KeyCode.E:
                    return keyboard.eKey.isPressed;
                case KeyCode.R:
                    return keyboard.rKey.isPressed;
                case KeyCode.F:
                    return keyboard.fKey.isPressed;
                case KeyCode.Equals:
                    return keyboard.equalsKey.isPressed || keyboard.numpadPlusKey.isPressed;
                case KeyCode.Minus:
                    return keyboard.minusKey.isPressed || keyboard.numpadMinusKey.isPressed;
                default:
                    return false;
            }
        }
#endif

        [Serializable]
        public sealed class VfxAssignment
        {
            [SerializeField] private string cueId = string.Empty;
            [SerializeField] private string label;
            [SerializeField] private string category = string.Empty;
            [SerializeField] private string[] tags = Array.Empty<string>();
            [SerializeField] private string designerNote = string.Empty;
            [SerializeField] private bool deprecated;
            [SerializeField] private EffectKind kind;
            [SerializeField] private EffectVfxTargetFilter targetFilter;
            [SerializeField] private string sourceRef = string.Empty;
            [SerializeField] private bool matchSourceRefPrefix;
            [SerializeField] private int amount;
            [SerializeField] private int appliedAmount;
            [SerializeField] private int radius;
            [SerializeField] private bool isArea;
            [SerializeField] private EffectTestTargetAnchor targetAnchor;
            [SerializeField] private string targetUnitId = string.Empty;
            [SerializeField] private GameObject[] prefabs = Array.Empty<GameObject>();
            [SerializeField] private float scaleMultiplier = 1f;
            [SerializeField] private bool scaleWithRadius = true;
            [SerializeField] private Vector3 positionOffset;
            [SerializeField] private Vector3 rotationEulerOffset;
            [SerializeField] private float lifetimeOverride;

            public static VfxAssignment FromSample(Sample sample)
            {
                return new VfxAssignment
                {
                    cueId = string.Empty,
                    label = sample.Label,
                    category = "Effect Test",
                    tags = Array.Empty<string>(),
                    designerNote = string.Empty,
                    deprecated = false,
                    kind = sample.Kind,
                    targetFilter = ToTargetFilter(sample.TargetAnchor),
                    sourceRef = sample.SourceRef,
                    matchSourceRefPrefix = false,
                    amount = sample.Amount,
                    appliedAmount = sample.AppliedAmount,
                    radius = sample.Radius,
                    isArea = sample.IsArea,
                    targetAnchor = sample.TargetAnchor,
                    targetUnitId = sample.TargetUnitId,
                    scaleMultiplier = 1f,
                    scaleWithRadius = true
                };
            }

            private static EffectVfxTargetFilter ToTargetFilter(EffectTestTargetAnchor anchor)
            {
                switch (anchor)
                {
                    case EffectTestTargetAnchor.Player:
                        return EffectVfxTargetFilter.Player;
                    case EffectTestTargetAnchor.Monster:
                        return EffectVfxTargetFilter.Monster;
                    case EffectTestTargetAnchor.Field:
                        return EffectVfxTargetFilter.Field;
                    default:
                        return EffectVfxTargetFilter.Any;
                }
            }

            public Sample ToSample()
            {
                return new Sample(
                    string.IsNullOrWhiteSpace(label) ? kind.ToString() : label,
                    kind,
                    sourceRef ?? string.Empty,
                    amount,
                    appliedAmount,
                    radius,
                    isArea,
                    targetAnchor,
                    targetUnitId);
            }

            public bool TryCreateCatalogEntry(out EffectVfxCatalog.Entry entry)
            {
                if (!HasPrefab())
                {
                    entry = null;
                    return false;
                }

                entry = new EffectVfxCatalog.Entry(
                    kind,
                    prefabs,
                    targetFilter: targetFilter,
                    sourceRef: sourceRef ?? string.Empty,
                    matchSourceRefPrefix: matchSourceRefPrefix,
                    scaleMultiplier: scaleMultiplier <= 0f ? 1f : scaleMultiplier,
                    scaleWithRadius: scaleWithRadius,
                    positionOffset: positionOffset,
                    rotationEulerOffset: rotationEulerOffset,
                    lifetimeOverride: Mathf.Max(0f, lifetimeOverride),
                    cueId: cueId,
                    displayName: string.IsNullOrWhiteSpace(label) ? kind.ToString() : label,
                    category: category,
                    tags: tags,
                    designerNote: designerNote,
                    deprecated: deprecated);
                return true;
            }

            public void SetPrefabs(GameObject[] newPrefabs)
            {
                prefabs = newPrefabs ?? Array.Empty<GameObject>();
            }

            public bool Matches(EffectKind candidateKind, string candidateSourceRef)
            {
                if (!string.IsNullOrWhiteSpace(sourceRef))
                {
                    var safeSourceRef = candidateSourceRef ?? string.Empty;
                    return matchSourceRefPrefix
                        ? safeSourceRef.StartsWith(sourceRef, StringComparison.Ordinal)
                        : string.Equals(sourceRef, safeSourceRef, StringComparison.Ordinal);
                }

                return kind == candidateKind && string.IsNullOrWhiteSpace(candidateSourceRef);
            }

            public void ApplyVisibilityPreset(float minimumScale, Vector3 offset)
            {
                if (scaleMultiplier < minimumScale)
                {
                    scaleMultiplier = minimumScale;
                }

                positionOffset = offset;
            }

            private bool HasPrefab()
            {
                if (prefabs == null)
                {
                    return false;
                }

                foreach (var prefab in prefabs)
                {
                    if (prefab != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        [Serializable]
        public readonly struct Sample
        {
            public Sample(
                string label,
                EffectKind kind,
                string sourceRef,
                int amount,
                int appliedAmount,
                int radius,
                bool isArea = false,
                EffectTestTargetAnchor targetAnchor = EffectTestTargetAnchor.Center,
                string targetUnitId = "")
            {
                Label = label;
                Kind = kind;
                StatusKind = null;
                SourceRef = sourceRef;
                Amount = amount;
                AppliedAmount = appliedAmount;
                Radius = radius;
                IsArea = isArea;
                TargetAnchor = targetAnchor;
                TargetUnitId = targetUnitId ?? string.Empty;
            }

            public Sample(
                string label,
                StatusEffectKind statusKind,
                string sourceRef,
                int amount,
                int appliedAmount,
                int radius,
                bool isArea = false,
                EffectTestTargetAnchor targetAnchor = EffectTestTargetAnchor.Center,
                string targetUnitId = "")
            {
                Label = label;
                Kind = EffectKind.StatusEffectApplied;
                StatusKind = statusKind;
                SourceRef = sourceRef;
                Amount = amount;
                AppliedAmount = appliedAmount;
                Radius = radius;
                IsArea = isArea;
                TargetAnchor = targetAnchor;
                TargetUnitId = targetUnitId ?? string.Empty;
            }

            public string Label { get; }
            public EffectKind Kind { get; }
            public StatusEffectKind? StatusKind { get; }
            public string SourceRef { get; }
            public int Amount { get; }
            public int AppliedAmount { get; }
            public int Radius { get; }
            public bool IsArea { get; }
            public EffectTestTargetAnchor TargetAnchor { get; }
            public string TargetUnitId { get; }

            public EffectResultEvent ToEvent(int fallbackAreaRadius)
            {
                return new EffectResultEvent(
                    Kind,
                    targetUnitId: TargetUnitId,
                    amount: Amount,
                    appliedAmount: AppliedAmount,
                    radius: Radius > 0 ? Radius : (IsArea ? fallbackAreaRadius : 0),
                    sourceRef: SourceRef,
                    statusKind: StatusKind);
            }
        }    }
}




