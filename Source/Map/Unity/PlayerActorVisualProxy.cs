using System.Collections;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Map.Unity
{
    /// <summary>
    /// The authored marker settings the proxy needs to build its instance. Passed as a snapshot instead of
    /// seven interface members because all seven are <c>[SerializeField]</c>s on the view that are authored
    /// per scene with genuinely different values (MainGameplay ships height 0 / scale 2.5, ArtLookdev ships
    /// height 0.32 / scale 1 / no prefab at all), so they stay on the MonoBehaviour.
    /// </summary>
    internal readonly struct PlayerMarkerAuthoring
    {
        public PlayerMarkerAuthoring(
            GameObject prefab,
            float radius,
            Vector3 visualLocalOffset,
            Vector3 visualLocalEulerAngles,
            Vector3 visualLocalScale)
        {
            Prefab = prefab;
            Radius = radius;
            VisualLocalOffset = visualLocalOffset;
            VisualLocalEulerAngles = visualLocalEulerAngles;
            VisualLocalScale = visualLocalScale;
        }

        public GameObject Prefab { get; }
        public float Radius { get; }
        public Vector3 VisualLocalOffset { get; }
        public Vector3 VisualLocalEulerAngles { get; }
        public Vector3 VisualLocalScale { get; }
    }

    /// <summary>
    /// What <see cref="PlayerActorVisualProxy"/> needs from <see cref="AtlasTilePresentationView"/>. Five
    /// members, all either projection or authored-value reads — the proxy cannot reach tiles, overlays, map
    /// objects, or the visibility pass.
    /// </summary>
    internal interface IPlayerActorVisualHost
    {
        /// <summary>Parent for the marker instance, and the local space its position is expressed in.</summary>
        Transform MapTransform { get; }

        /// <summary>Authored lift above the projected tile surface. Scene-authored: 0 in MainGameplay, 0.32 elsewhere.</summary>
        float PlayerHeightOffset { get; }

        Vector3 ProjectOverlaySurface(HexCoord coord);

        PlayerMarkerAuthoring GetPlayerMarkerAuthoring();

        /// <summary>Fallback sphere material, built through the view's shared material factory.</summary>
        Material CreatePlayerMarkerMaterial();
    }

    /// <summary>
    /// Owns the player's on-map marker: the instance itself, the fallback sphere material, and every
    /// animation/trigger call that used to be forwarded to <see cref="CharacterActorVisual"/> from the view.
    ///
    /// Extracted from <see cref="AtlasTilePresentationView"/>, which keeps all 20 public members as
    /// delegation (37 call sites across gameplay, cinematics and the trailer runner). The win is not the
    /// line count — it is that the marker's two fields stopped being reachable from 3,500 unrelated lines,
    /// and that the "ensure marker, find the visual, null-check, call one method" ritual that was copied
    /// twelve times now exists once as <see cref="ResolveVisual"/>.
    ///
    /// The marker is created lazily and never torn down: its name does not match the generated-child
    /// prefixes the view reaps on rebuild, so it survives every Render() by design.
    /// </summary>
    internal sealed class PlayerActorVisualProxy
    {
        private readonly IPlayerActorVisualHost host;
        private GameObject marker;
        private Material markerMaterial;

        public PlayerActorVisualProxy(IPlayerActorVisualHost host)
        {
            this.host = host ?? throw new System.ArgumentNullException(nameof(host));
        }

        public Vector3 LocalPosition => marker != null ? marker.transform.localPosition : Vector3.zero;

        /// <summary>
        /// World-space center of the marker's visible renderer bounds. Unlike <see cref="LocalPosition"/>
        /// this does not force the marker into existence — callers use it for framing, and a missing marker
        /// should read as "nothing to frame" rather than spawn one.
        /// </summary>
        public bool TryGetRendererBoundsWorldCenter(out Vector3 worldCenter)
        {
            worldCenter = default;
            if (marker == null)
            {
                return false;
            }

            var renderers = marker.GetComponentsInChildren<Renderer>(includeInactive: false);
            var hasBounds = false;
            var bounds = default(Bounds);
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!hasBounds)
            {
                return false;
            }

            worldCenter = bounds.center;
            return true;
        }

        /// <summary>
        /// Anchor position for VFX that must attach to a body part (chest, weapon). Falls back to the marker
        /// origin when the visual has no such anchor, so a cue never silently plays at the world origin.
        /// </summary>
        /// <summary>
        /// Transform variant of <see cref="TryGetVfxAnchorWorldPosition"/> for followSourceAnchor cues that
        /// keep tracking the anchor after spawn. No marker-position fallback: without a live actor anchor
        /// there is nothing to follow.
        /// </summary>
        public bool TryGetVfxAnchor(CharacterVfxAnchorKind anchorKind, out Transform anchor)
        {
            EnsureMarker();
            var visual = marker != null
                ? marker.GetComponentInChildren<CharacterActorVisual>(includeInactive: true)
                : null;
            if (visual != null && visual.TryGetVfxAnchor(anchorKind, out anchor) && anchor != null)
            {
                return true;
            }

            anchor = null;
            return false;
        }

        /// <summary>
        /// 플레이어 모델의 발자국 지름(월드 단위). 상태이상 바닥 링 비례 스케일의 <b>기준 체구</b>다 —
        /// 이 값이 계수 1이 되므로 플레이어 화면은 현행 튜닝 그대로 유지된다(WS-2).
        /// </summary>
        public bool TryGetFootprintDiameter(out float diameter)
        {
            EnsureMarker();
            var visual = marker != null
                ? marker.GetComponentInChildren<CharacterActorVisual>(includeInactive: true)
                : null;
            if (visual != null && visual.TryGetFootprintDiameter(out diameter))
            {
                return true;
            }

            diameter = 0f;
            return false;
        }

        public bool TryGetVfxAnchorWorldPosition(CharacterVfxAnchorKind anchorKind, out Vector3 worldPosition)
        {
            EnsureMarker();
            var visual = marker != null
                ? marker.GetComponentInChildren<CharacterActorVisual>(includeInactive: true)
                : null;
            if (visual != null && visual.TryGetVfxAnchor(anchorKind, out var anchor) && anchor != null)
            {
                worldPosition = anchor.position;
                return true;
            }

            if (marker != null)
            {
                worldPosition = marker.transform.position;
                return true;
            }

            worldPosition = default;
            return false;
        }

        public void SetPosition(HexCoord coord)
        {
            EnsureMarker();
            marker.transform.localPosition = ProjectStandPosition(coord);
        }

        public void SetFacingDirectionLocal(Vector3 localForward)
        {
            EnsureMarker();
            localForward.y = 0f;
            if (localForward.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            marker.transform.localRotation = Quaternion.LookRotation(localForward.normalized, Vector3.up);
        }

        /// <summary>
        /// Turns the marker to face a map cell (horizontal only), through the same rotation path the move
        /// animation uses. Framing seam for the trailer runner: a death beat reads wrong when the victim has
        /// their back to the attacker.
        /// </summary>
        public void FaceTowards(HexCoord target)
        {
            EnsureMarker();
            var direction = host.ProjectOverlaySurface(target) - marker.transform.localPosition;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                SetFacingDirectionLocal(direction);
            }
        }

        public void SetMoveSpeed(float speed)
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.SetMoveSpeed(speed);
            }
        }

        public void ResetVisualState()
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.ResetVisualState();
            }
        }

        // Waits until the player's attack animation reaches its strike point so the controller can align the
        // hit impact. Delegates to the player CharacterActorVisual; fixed-delay fallback if none is present.
        public IEnumerator WaitForAttackStrike(float strikeFraction, float fallbackSeconds, float maxWaitSeconds)
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                yield return visual.WaitForAttackStrike(strikeFraction, fallbackSeconds, maxWaitSeconds);
            }
            else if (fallbackSeconds > 0f)
            {
                yield return new WaitForSeconds(fallbackSeconds);
            }
        }

        public bool TryGetAnimationSpeed(out float speed)
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                speed = visual.AnimationSpeed;
                return true;
            }

            speed = 1f;
            return false;
        }

        public void SetAnimationSpeed(float speed)
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.SetAnimationSpeed(speed);
            }
        }

        public void PulseMove(float speed, float duration)
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.PulseMove(speed, duration);
            }
        }

        public void TriggerAttack()
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.TriggerAttack();
            }
        }

        public void TriggerShield()
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.TriggerShield();
            }
        }

        public void TriggerBuff()
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.TriggerBuff();
            }
        }

        public void TriggerField()
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.TriggerField();
            }
        }

        public void TriggerHit()
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.TriggerHit();
            }
        }

        public void TriggerKnockback()
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.TriggerKnockback();
            }
        }

        public void TriggerDead()
        {
            var visual = ResolveVisual();
            if (visual != null)
            {
                visual.TriggerDead();
            }
        }

        public IEnumerator AnimateMove(HexCoord from, HexCoord to, float duration)
        {
            EnsureMarker();
            var fromPos = ProjectStandPosition(from);
            var toPos = ProjectStandPosition(to);
            var moveDirection = toPos - fromPos;
            moveDirection.y = 0f;
            SetFacingDirectionLocal(moveDirection);
            SetMoveSpeed(1f);

            if (!Application.isPlaying || duration <= 0f)
            {
                marker.transform.localPosition = toPos;
                SetMoveSpeed(0f);
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (marker == null)
                {
                    yield break;
                }

                elapsed += Time.deltaTime;
                marker.transform.localPosition = Vector3.Lerp(fromPos, toPos, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (marker != null)
            {
                marker.transform.localPosition = toPos;
                SetMoveSpeed(0f);
            }
        }

        public IEnumerator AnimateKnockback(HexCoord from, HexCoord to, float duration)
        {
            EnsureMarker();
            var fromPos = ProjectStandPosition(from);
            var toPos = ProjectStandPosition(to);
            // Face the source, not the destination: knockback slides the actor backwards away from the hit.
            var sourceDirection = fromPos - toPos;
            sourceDirection.y = 0f;
            SetFacingDirectionLocal(sourceDirection);

            if (!Application.isPlaying || duration <= 0f)
            {
                marker.transform.localPosition = toPos;
                yield break;
            }

            var elapsed = 0f;
            while (elapsed < duration)
            {
                if (marker == null)
                {
                    yield break;
                }

                elapsed += Time.deltaTime;
                marker.transform.localPosition = Vector3.Lerp(fromPos, toPos, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (marker != null)
            {
                marker.transform.localPosition = toPos;
            }
        }

        private Vector3 ProjectStandPosition(HexCoord coord)
            => host.ProjectOverlaySurface(coord) + Vector3.up * host.PlayerHeightOffset;

        /// <summary>
        /// The "ensure the marker exists, then find its animated visual" step every trigger shares. Returns
        /// null when the marker is the fallback primitive sphere (no prefab authored), which is why every
        /// caller null-checks rather than assuming a visual.
        /// </summary>
        private CharacterActorVisual ResolveVisual()
        {
            EnsureMarker();
            return marker != null ? marker.GetComponentInChildren<CharacterActorVisual>(true) : null;
        }

        private void EnsureMarker()
        {
            if (marker != null)
            {
                return;
            }

            var authoring = host.GetPlayerMarkerAuthoring();
            if (authoring.Prefab != null)
            {
                marker = new GameObject("Atlas Player Marker");
                marker.transform.SetParent(host.MapTransform, false);
                var visualRoot = UnityEngine.Object.Instantiate(authoring.Prefab, marker.transform, false);
                CharacterActorVisual.PrepareInstantiatedVisual(
                    visualRoot,
                    authoring.VisualLocalOffset,
                    authoring.VisualLocalEulerAngles,
                    authoring.VisualLocalScale);
                return;
            }

            // No authored prefab (ArtLookdev): a plain sphere stands in so the map still shows where the
            // player is. It has no CharacterActorVisual, so every animation trigger becomes a no-op.
            markerMaterial ??= host.CreatePlayerMarkerMaterial();
            marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "Atlas Player Marker";
            marker.transform.SetParent(host.MapTransform, false);
            marker.transform.localScale = Vector3.one * Mathf.Max(0.01f, authoring.Radius);
            var collider = marker.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
            }

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = markerMaterial;
            }
        }
    }
}
