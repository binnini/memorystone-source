using System.Collections;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SeoulPlayup.Map.Unity
{
    public enum CharacterVfxAnchorKind
    {
        Root,
        HitCenter,
        Ground,
        Head,
        AttackSource,

        /// <summary>
        /// 모델 렌더러 바운즈의 꼭대기(중심 XZ) — 손저작 본이 아니라 실측이다(2026-08-20 #9).
        /// Head 본은 두개골 <b>안</b>이라 스케일 0.6×~5×를 오가는 몬스터들에서 상태 VFX가 머리와
        /// 겹치거나 발치에 파묻혔다. 네임플레이트 높이 자동 산출과 같은 원리. append-only.
        /// </summary>
        HeadTop
    }

    /// <summary>
    /// Small visual-only bridge for character prefabs that are parented under runtime map anchors.
    /// The gameplay systems keep ownership of projected anchor positions; this component owns only
    /// child visual setup, animator parameters, and raycast safety.
    /// </summary>
    public sealed class CharacterActorVisual : MonoBehaviour
    {
        private const string IgnoreRaycastLayerName = "Ignore Raycast";
        private const string MoveSpeedParameter = "MoveSpeed";
        private const string AttackTriggerParameter = "AttackTrigger";
        private const string Attack1TriggerParameter = "Attack1";
        private const string Attack2TriggerParameter = "Attack2";
        private const string Attack3TriggerParameter = "Attack3";
        private const string Attack4TriggerParameter = "Attack4";
        private const string Attack5TriggerParameter = "Attack5";
        private const string ShieldTriggerParameter = "ShieldTrigger";
        private const string ShieldParameter = "Shield";
        private const string BuffTriggerParameter = "BuffTrigger";
        private const string BuffParameter = "Buff";
        private const string FieldTriggerParameter = "FieldTrigger";
        private const string FieldParameter = "Field";
        private const string HitTriggerParameter = "HitTrigger";
        private const string HitParameter = "Hit";
        private const string KnockbackTriggerParameter = "KnockbackTrigger";
        private const string KnockBackTriggerParameter = "KnockBackTrigger";
        private const string KnockbackParameter = "Knockback";
        private const string KnockBackParameter = "KnockBack";
        private const string DeadParameter = "Dead";
        private const string DanceParameter = "Dance";

        [SerializeField] private Animator animator;
        [Header("VFX Anchors")]
        [SerializeField] private Transform hitCenterAnchor;
        [SerializeField] private Transform groundAnchor;
        [SerializeField] private Transform headAnchor;
        [SerializeField] private Transform attackSourceAnchor;

        // HeadTop(#9)의 합성 앵커 — 첫 요청 때 렌더러 바운즈로 한 번 계산해 자식 트랜스폼으로 박는다
        // (모델을 따라 움직이고, 매 프레임 바운즈 재계산 비용이 없다). 프리팹에 저작하지 않는다.
        private Transform headTopAnchor;

        private Coroutine movePulseRoutine;

        // 은신 해제 페이드가 <b>바꿔 끼운</b> 머티리얼의 원복 정보. 코루틴 지역변수가 아니라 컴포넌트가
        // 들고 있는 것이 요점이다 — 코루틴은 비활성화·StopAllCoroutines·마커 재사용으로 조용히 끊기고,
        // 그때 finally는 <b>돌지 않는다</b>. 상태를 밖에 두면 어느 경로로 끊겨도 OnDisable에서 되돌린다.
        private Coroutine revealFadeRoutine;
        private Renderer[] revealFadeRenderers;
        private Material[][] revealFadeOriginals;
        private Material[][] revealFadeCopies;

        public float LastMoveSpeed { get; private set; }
        public string LastTriggerName { get; private set; } = string.Empty;
        public string LastAnimationCommand { get; private set; } = string.Empty;
        public string LastAnimationWarning { get; private set; } = string.Empty;
        public Transform HitCenterAnchor => ResolveVfxAnchor(CharacterVfxAnchorKind.HitCenter);
        public Transform GroundAnchor => ResolveVfxAnchor(CharacterVfxAnchorKind.Ground);
        public Transform HeadAnchor => ResolveVfxAnchor(CharacterVfxAnchorKind.Head);
        public Transform AttackSourceAnchor => ResolveVfxAnchor(CharacterVfxAnchorKind.AttackSource);
        public bool CanPlayMove => HasAnyFloatParameter(MoveSpeedParameter, "Move Speed");
        public bool CanPlayAttack => HasAnyTriggerOrBoolParameter(AttackTriggerParameter, "Attack", Attack1TriggerParameter, Attack2TriggerParameter, Attack3TriggerParameter, Attack4TriggerParameter, Attack5TriggerParameter);
        public bool CanPlayShield => HasAnyTriggerOrBoolParameter(ShieldTriggerParameter, ShieldParameter);
        public bool CanPlayBuff => HasAnyTriggerOrBoolParameter(BuffTriggerParameter, BuffParameter);
        public bool CanPlayField => HasAnyTriggerOrBoolParameter(FieldTriggerParameter, FieldParameter);
        public bool CanPlayHit => HasAnyTriggerOrBoolParameter(HitTriggerParameter, HitParameter);
        public bool CanPlayKnockback => HasAnyTriggerOrBoolParameter(KnockbackTriggerParameter, KnockBackTriggerParameter, KnockbackParameter, KnockBackParameter);
        public bool CanPlayDead => HasAnyTriggerOrBoolParameter(DeadParameter);
        public bool CanPlayDance => HasAnyTriggerOrBoolParameter(DanceParameter);
        public Animator Animator
        {
            get
            {
                EnsureAnimator();
                return animator;
            }
        }

        public float AnimationSpeed
        {
            get
            {
                EnsureAnimator();
                return animator != null ? animator.speed : 1f;
            }
        }

        public static CharacterActorVisual PrepareInstantiatedVisual(
            GameObject visualRoot,
            Vector3 localOffset,
            Vector3 localEulerAngles,
            Vector3 localScale)
        {
            if (visualRoot == null)
            {
                return null;
            }

            visualRoot.transform.localPosition = localOffset;
            visualRoot.transform.localRotation = Quaternion.Euler(localEulerAngles);
            visualRoot.transform.localScale = SafeScale(localScale);

            var visual = visualRoot.GetComponent<CharacterActorVisual>();
            if (visual == null)
            {
                visual = visualRoot.AddComponent<CharacterActorVisual>();
            }

            visual.EnsureAnimator();
            visual.EnsureAnimationPlaybackReady();
            visual.ApplyRaycastSafeVisualPolicy();
            return visual;
        }

        /// <summary>
        /// 모델의 발(<c>Ground</c> 앵커)이 정확히 <paramref name="worldY"/>에 닿도록 <paramref name="visualRoot"/>를 수직으로
        /// 옮긴다(2026-09-03 「요괴가 떠 보인다」). 마커는 타일 윗면에 서지만 모델마다 발이 루트 피벗에서 다른 높이에 있어
        /// (신규 6종은 −0.13×스케일, 기존 4종은 0) 일괄 오프셋으로는 절대 다 맞지 않는다 — 프리팹의 Ground 앵커가 저작면이다.
        /// 앵커가 없으면 아무것도 하지 않는다(저작 오프셋 존중). 멱등이라 배율이 바뀔 때마다 다시 불러도 된다.
        /// </summary>
        public static void SnapGroundAnchorToWorldY(Transform visualRoot, float worldY)
        {
            if (visualRoot == null)
            {
                return;
            }

            var visual = visualRoot.GetComponent<CharacterActorVisual>();
            Transform ground = null;
            if (visual != null && visual.groundAnchor != null)
            {
                ground = visual.groundAnchor;
            }

            ground ??= FindChildRecursive(visualRoot, "Ground");
            if (ground == null || ground == visualRoot)
            {
                // Ground 앵커가 없는 모델(더미·구형 마커)은 저작된 localOffset을 그대로 존중한다.
                return;
            }

            var delta = worldY - ground.position.y;
            if (Mathf.Abs(delta) > 1e-5f)
            {
                visualRoot.position += Vector3.up * delta;
            }
        }

        public bool TryGetVfxAnchor(CharacterVfxAnchorKind kind, out Transform anchor)
        {
            anchor = ResolveVfxAnchor(kind);
            return anchor != null;
        }

        public Vector3 GetVfxAnchorWorldPosition(CharacterVfxAnchorKind kind)
        {
            return ResolveVfxAnchor(kind).position;
        }

        public void SetMoveSpeed(float speed)
        {
            LastAnimationCommand = "Move";
            LastMoveSpeed = Mathf.Max(0f, speed);
            if (!SetFloatIfPresent(LastMoveSpeed, MoveSpeedParameter, "Move Speed"))
            {
                WarnMissingParameter("Move", AnimatorControllerParameterType.Float, MoveSpeedParameter, "Move Speed");
            }
        }

        public void SetAnimationSpeed(float speed)
        {
            EnsureAnimator();
            if (animator != null)
            {
                animator.speed = Mathf.Max(0f, speed);
            }
        }

        public void EnsureAnimationPlaybackReady()
        {
            EnsureAnimator();
            if (animator == null)
            {
                return;
            }

            animator.enabled = true;
            animator.speed = animator.speed <= 0f ? 1f : animator.speed;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (animator.runtimeAnimatorController == null)
            {
                return;
            }

            animator.Rebind();
            animator.Update(0f);
        }

        /// <summary>
        /// 은신 해제 페이드를 <b>시작</b>한다(2026-09-05 수리). 종전에는 코루틴 자체를 밖에서 돌렸고
        /// 중단은 <c>StopCoroutine(nameof(...))</c>로 시도했는데, 문자열 중단은
        /// <c>StartCoroutine(string)</c>으로 <b>시작한</b> 코루틴에만 걸린다 — 즉 아무것도 멈추지 않았다.
        /// 그 결과 겹쳐 돈 두 번째 페이드가 <b>첫 번째의 투명 사본</b>을 "원본"으로 잡아 두었다가
        /// 되돌려 놓아, 들킨 몬스터가 반투명인 채로 굳었다. 진입점을 하나로 만들고 원복을 컴포넌트가
        /// 소유하게 해서 두 사고를 함께 막는다.
        /// </summary>
        public void BeginRevealFade(float seconds, float startAlpha = 0.15f)
        {
            // 이전 페이드는 <b>원상 복구까지 끝내고</b> 물러난다 — 끊기만 하면 사본이 화면에 남는다.
            StopRevealFade();
            if (!isActiveAndEnabled)
            {
                return;
            }

            revealFadeRoutine = StartCoroutine(PlayRevealFade(seconds, startAlpha));
        }

        /// <summary>
        /// 페이드를 멈추고 원래 공유 머티리얼로 되돌린다. 몇 번 불러도 안전하다(진행 중이 아니면 무해).
        /// </summary>
        public void StopRevealFade()
        {
            if (revealFadeRoutine != null)
            {
                StopCoroutine(revealFadeRoutine);
                revealFadeRoutine = null;
            }

            RestoreRevealFadeMaterials();
        }

        /// <summary>
        /// 은신이 풀려 모습이 <b>돌아오는</b> 연출(2026-09-05 사용자 요구): 반투명에서 시작해
        /// 정상 불투명으로 차오른다. 사라질 때는 연막(V026)이 가리지만 나타날 때는 가릴 것이 없어서,
        /// 이 페이드가 없으면 모델이 한 프레임에 <b>튀어나온다</b>.
        ///
        /// <para>🔴 알파만 낮춰서는 안 보인다 — URP Lit의 불투명 머티리얼은 알파를 아예 읽지 않는다.
        /// 그래서 렌더러마다 <b>사본</b>을 만들어 투명 표면으로 바꾸고, 끝나면 원래 공유 머티리얼로
        /// 되돌린 뒤 사본을 버린다. 공유 머티리얼을 직접 만지면 같은 프리팹을 쓰는 다른 개체까지
        /// 투명해지고, 그 상태가 <b>에셋에 남는다</b>.</para>
        ///
        /// <para>🔴 원복은 <b>finally에 맡기지 않는다</b>. 코루틴이 비활성화·중단으로 끊기면 finally가
        /// 돌지 않아 반투명 사본이 그대로 굳는다("보이긴 하는데 투명하게 보임"의 정체) — 그래서
        /// 복구 정보를 컴포넌트 필드에 두고 <see cref="StopRevealFade"/>·<c>OnDisable</c>에서도 되돌린다.</para>
        ///
        /// <para>⚠️ 외부에서 직접 돌리지 말 것 — 진입점은 <see cref="BeginRevealFade"/> 하나다
        /// (겹쳐 돌면 투명 사본을 원본으로 잡는다).</para>
        /// </summary>
        private IEnumerator PlayRevealFade(float seconds, float startAlpha = 0.15f)
        {
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers == null || renderers.Length == 0 || seconds <= 0f)
            {
                revealFadeRoutine = null;
                yield break;
            }

            var originals = new Material[renderers.Length][];
            var fading = new Material[renderers.Length][];
            for (var i = 0; i < renderers.Length; i++)
            {
                originals[i] = renderers[i].sharedMaterials;
                var copies = new Material[originals[i].Length];
                for (var m = 0; m < copies.Length; m++)
                {
                    copies[m] = originals[i][m] != null ? new Material(originals[i][m]) : null;
                    MakeTransparent(copies[m]);
                }

                fading[i] = copies;
                renderers[i].sharedMaterials = copies;
            }

            // 스왑이 <b>끝난 뒤</b>에 등록한다 — 이 세 줄이 곧 "무엇을 되돌려야 하는가"의 정본이다.
            revealFadeRenderers = renderers;
            revealFadeOriginals = originals;
            revealFadeCopies = fading;

            var elapsed = 0f;
            while (elapsed < seconds)
            {
                elapsed += Time.deltaTime;
                var alpha = Mathf.Lerp(Mathf.Clamp01(startAlpha), 1f, Mathf.Clamp01(elapsed / seconds));
                for (var i = 0; i < fading.Length; i++)
                {
                    for (var m = 0; m < fading[i].Length; m++)
                    {
                        SetMaterialAlpha(fading[i][m], alpha);
                    }
                }

                yield return null;
            }

            revealFadeRoutine = null;
            RestoreRevealFadeMaterials();
        }

        /// <summary>바꿔 끼운 사본을 걷고 원래 공유 머티리얼을 되돌린다. 멱등이다.</summary>
        private void RestoreRevealFadeMaterials()
        {
            var renderers = revealFadeRenderers;
            var originals = revealFadeOriginals;
            var copies = revealFadeCopies;
            revealFadeRenderers = null;
            revealFadeOriginals = null;
            revealFadeCopies = null;
            if (renderers == null || originals == null)
            {
                return;
            }

            for (var i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null && originals[i] != null)
                {
                    renderers[i].sharedMaterials = originals[i];
                }

                if (copies == null || copies[i] == null)
                {
                    continue;
                }

                foreach (var copy in copies[i])
                {
                    if (copy != null)
                    {
                        Destroy(copy);
                    }
                }
            }
        }

        /// <summary>URP Lit/Simple Lit 사본을 알파 블렌드 표면으로 바꾼다. 사본에만 쓴다.</summary>
        private static void MakeTransparent(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_Surface"))
            {
                material.SetFloat("_Surface", 1f);
            }

            if (material.HasProperty("_SrcBlend"))
            {
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            }

            if (material.HasProperty("_DstBlend"))
            {
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            }

            if (material.HasProperty("_ZWrite"))
            {
                material.SetFloat("_ZWrite", 0f);
            }

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static void SetMaterialAlpha(Material material, float alpha)
        {
            if (material == null)
            {
                return;
            }

            if (material.HasProperty("_BaseColor"))
            {
                var color = material.GetColor("_BaseColor");
                color.a = alpha;
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                var color = material.GetColor("_Color");
                color.a = alpha;
                material.SetColor("_Color", color);
            }
        }

        public void ResetVisualState()
        {
            LastAnimationCommand = "Reset";
            LastTriggerName = string.Empty;
            LastAnimationWarning = string.Empty;
            if (movePulseRoutine != null)
            {
                StopCoroutine(movePulseRoutine);
                movePulseRoutine = null;
            }

            EnsureAnimationPlaybackReady();
            SetMoveSpeed(0f);
        }

        public void PulseMove(float speed = 1f, float duration = 0.25f)
        {
            SetMoveSpeed(speed);
            if (!Application.isPlaying || !isActiveAndEnabled || duration <= 0f)
            {
                SetMoveSpeed(0f);
                return;
            }

            if (movePulseRoutine != null)
            {
                StopCoroutine(movePulseRoutine);
            }

            movePulseRoutine = StartCoroutine(ResetMoveSpeedAfter(duration));
        }

        public void TriggerAttack()
        {
            LastAnimationCommand = "Attack";
            if (TryTriggerIfPresent(AttackTriggerParameter, recordAttempt: true)) return;
            if (TryTriggerIfPresent("Attack", recordAttempt: false)) return;
            if (TryTriggerIfPresent(Attack1TriggerParameter, recordAttempt: false)) return;
            if (TryTriggerIfPresent(Attack2TriggerParameter, recordAttempt: false)) return;
            if (TryTriggerIfPresent(Attack3TriggerParameter, recordAttempt: false)) return;
            if (TryTriggerIfPresent(Attack4TriggerParameter, recordAttempt: false)) return;
            if (TryTriggerIfPresent(Attack5TriggerParameter, recordAttempt: false)) return;
            WarnMissingParameter("Attack", AnimatorControllerParameterType.Trigger, AttackTriggerParameter, "Attack", Attack1TriggerParameter, Attack2TriggerParameter, Attack3TriggerParameter, Attack4TriggerParameter, Attack5TriggerParameter);
        }

        public void TriggerAttack(string parameterName)
        {
            LastAnimationCommand = "Attack";
            TriggerIfPresent(string.IsNullOrWhiteSpace(parameterName) ? AttackTriggerParameter : parameterName);
        }

        // Waits until the current attack animation reaches its strike point (strikeFraction of clip length)
        // so callers can align the impact (damage VFX/SFX) with the visual hit. Falls back to a fixed delay
        // when not playing or no animator clip is available; never waits longer than maxWaitSeconds.
        public IEnumerator WaitForAttackStrike(float strikeFraction, float fallbackSeconds, float maxWaitSeconds)
        {
            EnsureAnimator();
            strikeFraction = Mathf.Clamp01(strikeFraction);
            maxWaitSeconds = Mathf.Max(0f, maxWaitSeconds);

            if (!Application.isPlaying || animator == null || animator.runtimeAnimatorController == null)
            {
                if (fallbackSeconds > 0f)
                {
                    yield return new WaitForSeconds(fallbackSeconds);
                }
                yield break;
            }

            var waited = 0f;
            // Let a just-fired Attack trigger enter its transition before sampling the current state.
            // Without this frame, slow-to-evaluate controllers report the previous Idle/Move state and the
            // strike point would be computed from the wrong clip length.
            yield return null;
            waited += Time.deltaTime;

            // Wait only for the transition into the attack state to finish. This must NOT also wait on a
            // "am I in the right state?" predicate: an earlier revision additionally waited for the actor to
            // be in its *Dead* state here (pasted from WaitForDead), and since an attacking actor never is,
            // that loop always ran to maxWaitSeconds — which then swallowed the strike target below, making
            // every impact a fixed maxWait delay regardless of the clip. There is no reliable "is in attack
            // state" predicate to substitute: attack states are authored per monster with per-clip names
            // (Attack1..Attack5), so the transition end is the only portable signal.
            while (animator.IsInTransition(0) && waited < maxWaitSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            var length = animator.GetCurrentAnimatorStateInfo(0).length;
            if (!TryResolveStrikeWaitTarget(length, strikeFraction, maxWaitSeconds, out var target))
            {
                var remaining = Mathf.Max(0f, fallbackSeconds - waited);
                if (remaining > 0f)
                {
                    yield return new WaitForSeconds(remaining);
                }
            }
            else
            {
                while (waited < target)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }
        }

        /// <summary>
        /// Pure strike-point calculation split out of <see cref="WaitForAttackStrike"/> so it is testable
        /// without an <see cref="Animator"/> (the coroutine itself is runtime-only). Returns false when the
        /// sampled clip length is unusable (no clip, or a looping/invalid state reporting 0 / Infinity /
        /// NaN), which tells the caller to fall back to its fixed delay instead of guessing a strike point.
        /// </summary>
        public static bool TryResolveStrikeWaitTarget(
            float clipLength,
            float strikeFraction,
            float maxWaitSeconds,
            out float target)
        {
            if (clipLength <= 0f || float.IsInfinity(clipLength) || float.IsNaN(clipLength))
            {
                target = 0f;
                return false;
            }

            target = Mathf.Min(clipLength * Mathf.Clamp01(strikeFraction), Mathf.Max(0f, maxWaitSeconds));
            return true;
        }

        // Waits for a death animation to play out so a kill reward/cleanup can hold until the body settles.
        // The Dead trigger is fired separately (TriggerDead); this just waits the current state's length,
        // clamped to maxWaitSeconds. Falls back to a fixed delay when not playing or no clip is available.
        // (Reconstructed: the 16:36 source predates the only available Map decompile, 01:45.)
        public IEnumerator WaitForDead(float fallbackSeconds, float maxWaitSeconds)
        {
            EnsureAnimator();
            maxWaitSeconds = Mathf.Max(0f, maxWaitSeconds);

            if (!Application.isPlaying || animator == null || animator.runtimeAnimatorController == null)
            {
                if (fallbackSeconds > 0f)
                {
                    yield return new WaitForSeconds(fallbackSeconds);
                }
                yield break;
            }

            var waited = 0f;
            while (animator.IsInTransition(0) && waited < maxWaitSeconds)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            var length = animator.GetCurrentAnimatorStateInfo(0).length;
            if (length <= 0f || float.IsInfinity(length) || float.IsNaN(length))
            {
                var remaining = Mathf.Max(0f, fallbackSeconds - waited);
                if (remaining > 0f)
                {
                    yield return new WaitForSeconds(remaining);
                }
            }
            else
            {
                var target = Mathf.Min(length, maxWaitSeconds);
                while (waited < target)
                {
                    waited += Time.deltaTime;
                    yield return null;
                }
            }
        }

        public void TriggerShield()
        {
            LastAnimationCommand = "Shield";
            if (!TryTriggerIfPresent(ShieldTriggerParameter, recordAttempt: true) &&
                !TryTriggerIfPresent(ShieldParameter, recordAttempt: false))
            {
                WarnMissingParameter("Shield", AnimatorControllerParameterType.Trigger, ShieldTriggerParameter, ShieldParameter);
            }
        }

        public void TriggerBuff()
        {
            LastAnimationCommand = "Buff";
            if (!TryTriggerIfPresent(BuffTriggerParameter, recordAttempt: true) &&
                !TryTriggerIfPresent(BuffParameter, recordAttempt: false))
            {
                WarnMissingParameter("Buff", AnimatorControllerParameterType.Trigger, BuffTriggerParameter, BuffParameter);
            }
        }

        public void TriggerField()
        {
            LastAnimationCommand = "Field";
            if (!TryTriggerIfPresent(FieldTriggerParameter, recordAttempt: true) &&
                !TryTriggerIfPresent(FieldParameter, recordAttempt: false))
            {
                WarnMissingParameter("Field", AnimatorControllerParameterType.Trigger, FieldTriggerParameter, FieldParameter);
            }
        }

        public void TriggerHit()
        {
            LastAnimationCommand = "Hit";
            if (!TryTriggerIfPresent(HitTriggerParameter, recordAttempt: true) &&
                !TryTriggerIfPresent(HitParameter, recordAttempt: false))
            {
                WarnMissingParameter("Hit", AnimatorControllerParameterType.Trigger, HitTriggerParameter, HitParameter);
            }
        }

        public void TriggerKnockback()
        {
            LastAnimationCommand = "Knockback";
            if (TryTriggerIfPresent(KnockbackTriggerParameter, recordAttempt: true)) return;
            if (TryTriggerIfPresent(KnockBackTriggerParameter, recordAttempt: false)) return;
            if (TryTriggerIfPresent(KnockbackParameter, recordAttempt: false)) return;
            if (TryTriggerIfPresent(KnockBackParameter, recordAttempt: false)) return;
            WarnMissingParameter("Knockback", AnimatorControllerParameterType.Trigger, KnockbackTriggerParameter, KnockBackTriggerParameter, KnockbackParameter, KnockBackParameter);
        }

        public void TriggerDead()
        {
            LastAnimationCommand = "Dead";
            if (!TryTriggerIfPresent(DeadParameter, recordAttempt: true))
            {
                WarnMissingParameter("Dead", AnimatorControllerParameterType.Bool, DeadParameter);
            }
        }

        public void TriggerDance()
        {
            LastAnimationCommand = "Dance";
            if (!TryTriggerIfPresent(DanceParameter, recordAttempt: true))
            {
                WarnMissingParameter("Dance", AnimatorControllerParameterType.Trigger, DanceParameter);
            }
        }

        public void ApplyRaycastSafeVisualPolicy()
        {
            var ignoreRaycastLayer = LayerMask.NameToLayer(IgnoreRaycastLayerName);
            ApplyRaycastSafeVisualPolicy(transform, ignoreRaycastLayer);
        }

        private static void ApplyRaycastSafeVisualPolicy(Transform root, int ignoreRaycastLayer)
        {
            if (root == null)
            {
                return;
            }

            if (ignoreRaycastLayer >= 0)
            {
                root.gameObject.layer = ignoreRaycastLayer;
            }

            var colliders = root.GetComponentsInChildren<Collider>(includeInactive: true);
            for (var i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                ApplyRaycastSafeVisualPolicy(root.GetChild(i), ignoreRaycastLayer);
            }
        }

        private void Awake()
        {
            EnsureAnimator();
            EnsureAnimationPlaybackReady();
            AutoAssignNamedVfxAnchors();
            ApplyRaycastSafeVisualPolicy();
        }

        /// <summary>
        /// 마커 재사용·씬 전환으로 꺼지면 코루틴은 <b>말없이 죽는다</b> — 그때 반투명 사본이 남지
        /// 않도록 여기서 되돌린다(2026-09-05 수리의 안전망).
        /// </summary>
        private void OnDisable()
        {
            StopRevealFade();
        }

        private void OnValidate()
        {
            EnsureAnimator(allowCreateEditorAnimator: false);
            AutoAssignNamedVfxAnchors();
        }

        private IEnumerator ResetMoveSpeedAfter(float duration)
        {
            yield return new WaitForSeconds(duration);
            SetMoveSpeed(0f);
            movePulseRoutine = null;
        }

        private void EnsureAnimator(bool allowCreateEditorAnimator = true)
        {
            if (IsAnimatorUsable(animator))
            {
                return;
            }

            var candidates = GetComponentsInChildren<Animator>(includeInactive: true);
            if (candidates == null || candidates.Length == 0)
            {
                animator = null;
                return;
            }

            var controllerSource = FindControllerSource(candidates);
            var best = FindBestAnimator(candidates);
#if UNITY_EDITOR
            if (allowCreateEditorAnimator && !IsAnimatorUsable(best))
            {
                best = TryCreateEditorModelAnimator(controllerSource);
                candidates = GetComponentsInChildren<Animator>(includeInactive: true);
            }
#endif
            best = best ?? controllerSource ?? candidates[0];
            if (best != null &&
                best.runtimeAnimatorController == null &&
                controllerSource != null &&
                controllerSource.runtimeAnimatorController != null)
            {
                best.runtimeAnimatorController = controllerSource.runtimeAnimatorController;
            }

            if (controllerSource != null &&
                controllerSource != best &&
                controllerSource.avatar == null &&
                controllerSource.runtimeAnimatorController != null)
            {
                controllerSource.enabled = false;
            }

            animator = best;
        }

        private static Animator FindControllerSource(Animator[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                if (candidates[i] != null && candidates[i].runtimeAnimatorController != null)
                {
                    return candidates[i];
                }
            }

            return null;
        }

        private static Animator FindBestAnimator(Animator[] candidates)
        {
            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (IsAnimatorUsable(candidate) && HasRenderableChildren(candidate.transform))
                {
                    return candidate;
                }
            }

            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (IsAnimatorUsable(candidate))
                {
                    return candidate;
                }
            }

            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate != null &&
                    string.Equals(candidate.gameObject.name, "Model", System.StringComparison.OrdinalIgnoreCase) &&
                    HasRenderableChildren(candidate.transform))
                {
                    return candidate;
                }
            }

            for (var i = 0; i < candidates.Length; i++)
            {
                var candidate = candidates[i];
                if (candidate != null && HasRenderableChildren(candidate.transform))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool IsAnimatorUsable(Animator candidate)
        {
            return candidate != null &&
                   ((candidate.avatar != null && candidate.avatar.isValid) ||
                    (candidate.runtimeAnimatorController != null && HasRenderableChildren(candidate.transform)));
        }

        private static bool HasRenderableChildren(Transform root)
        {
            if (root == null)
            {
                return false;
            }

            return root.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) != null ||
                   root.GetComponentInChildren<MeshRenderer>(includeInactive: true) != null;
        }

#if UNITY_EDITOR
        private Animator TryCreateEditorModelAnimator(Animator controllerSource)
        {
            var model = FindChildRecursive(transform, "Model") ?? FindLikelyModelRoot(transform);
            if (model == null || model == transform)
            {
                return null;
            }

            var modelAnimator = model.GetComponent<Animator>();
            if (modelAnimator == null)
            {
                modelAnimator = model.gameObject.AddComponent<Animator>();
            }

            if (modelAnimator.runtimeAnimatorController == null &&
                controllerSource != null &&
                controllerSource.runtimeAnimatorController != null)
            {
                modelAnimator.runtimeAnimatorController = controllerSource.runtimeAnimatorController;
            }

            if (modelAnimator.avatar == null)
            {
                modelAnimator.avatar = ResolveAvatarFromModelSource(model.gameObject);
            }

            return IsAnimatorUsable(modelAnimator) ? modelAnimator : null;
        }

        private static Avatar ResolveAvatarFromModelSource(GameObject modelInstance)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(modelInstance);
            var assetPath = source != null ? AssetDatabase.GetAssetPath(source) : AssetDatabase.GetAssetPath(modelInstance);
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
#endif

        private Transform ResolveVfxAnchor(CharacterVfxAnchorKind kind)
        {
            AutoAssignNamedVfxAnchors();
            switch (kind)
            {
                case CharacterVfxAnchorKind.HitCenter:
                    return hitCenterAnchor != null ? hitCenterAnchor : transform;
                case CharacterVfxAnchorKind.Ground:
                    return groundAnchor != null ? groundAnchor : transform;
                case CharacterVfxAnchorKind.Head:
                    return headAnchor != null ? headAnchor : transform;
                case CharacterVfxAnchorKind.AttackSource:
                    return attackSourceAnchor != null ? attackSourceAnchor : transform;
                case CharacterVfxAnchorKind.HeadTop:
                    return EnsureHeadTopAnchor();
                default:
                    return transform;
            }
        }

        /// <summary>
        /// 이 모델의 <b>발자국 지름</b>(월드 단위, 2026-08-20 WS-2). 상태이상 바닥 링이 몸집에 비례해
        /// 커지게 하는 계수의 원천이다 — 재는 자는 호버 판정 상자와 공유한다
        /// (<see cref="CharacterFootprint"/>). 보스 페이즈 배율처럼 트랜스폼에 얹힌 확대도 함께 들어온다.
        /// </summary>
        public bool TryGetFootprintDiameter(out float diameter)
        {
            return CharacterFootprint.TryResolveDiameter(transform, out diameter);
        }

        /// <summary>
        /// 모델 꼭대기 합성 앵커(#9). 렌더러 바운즈(파티클 제외)의 max.y + 중심 XZ에 자식 트랜스폼을
        /// 세운다 — 네임플레이트 높이 자동 산출(CombatActorMarkerPresenter.ResolveNameplateLocalPosition)과
        /// 같은 원리라 몬스터 체구가 어떻든 항상 머리 위에 선다. 렌더러가 하나도 없으면 Head 본 →
        /// 루트 순서로 물러난다(앵커 없는 프롭 몬스터도 최소한 발치보다 나쁘지 않게).
        /// </summary>
        private Transform EnsureHeadTopAnchor()
        {
            if (headTopAnchor != null)
            {
                return headTopAnchor;
            }

            var hasBounds = false;
            var bounds = default(Bounds);
            foreach (var childRenderer in GetComponentsInChildren<Renderer>(includeInactive: false))
            {
                if (childRenderer == null || !childRenderer.enabled || childRenderer is ParticleSystemRenderer)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = childRenderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(childRenderer.bounds);
                }
            }

            if (!hasBounds)
            {
                AutoAssignNamedVfxAnchors();
                return headAnchor != null ? headAnchor : transform;
            }

            var anchorObject = new GameObject("HeadTopAuto");
            anchorObject.hideFlags = HideFlags.DontSave;
            headTopAnchor = anchorObject.transform;
            headTopAnchor.SetParent(transform, worldPositionStays: false);
            headTopAnchor.position = new Vector3(bounds.center.x, bounds.max.y, bounds.center.z);
            return headTopAnchor;
        }

        private void AutoAssignNamedVfxAnchors()
        {
            if (hitCenterAnchor == null)
            {
                hitCenterAnchor = FindChildRecursive(transform, "HitCenter");
            }

            if (groundAnchor == null)
            {
                groundAnchor = FindChildRecursive(transform, "Ground");
            }

            if (headAnchor == null)
            {
                headAnchor = FindChildRecursive(transform, "Head");
            }

            if (attackSourceAnchor == null)
            {
                attackSourceAnchor = FindChildRecursive(transform, "AttackSource");
            }
        }

        private static Transform FindChildRecursive(Transform root, string childName)
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

                var nested = FindChildRecursive(child, childName);
                if (nested != null)
                {
                    return nested;
                }
            }

            return null;
        }

        private void SetFloatIfPresent(string parameterName, float value)
        {
            SetFloatIfPresent(value, parameterName);
        }

        private bool SetFloatIfPresent(float value, params string[] parameterNames)
        {
            EnsureAnimator();
            if (animator == null || parameterNames == null)
            {
                return false;
            }

            for (var i = 0; i < parameterNames.Length; i++)
            {
                var parameterName = parameterNames[i];
                if (!HasParameter(parameterName, AnimatorControllerParameterType.Float))
                {
                    continue;
                }

                LastAnimationWarning = string.Empty;
                animator.SetFloat(parameterName, value);
                return true;
            }

            return false;
        }

        private void TriggerIfPresent(string parameterName)
        {
            TryTriggerIfPresent(parameterName, recordAttempt: true);
        }

        private bool TryTriggerIfPresent(string parameterName, bool recordAttempt)
        {
            if (recordAttempt)
            {
                LastTriggerName = parameterName;
            }

            EnsureAnimator();
            if (animator == null || !HasTriggerOrBoolParameter(parameterName))
            {
                return false;
            }

            LastTriggerName = parameterName;
            if (HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
            {
                animator.SetTrigger(parameterName);
            }
            else
            {
                animator.SetBool(parameterName, true);
            }

            return true;
        }

        private bool HasTriggerOrBoolParameter(string parameterName)
        {
            return HasParameter(parameterName, AnimatorControllerParameterType.Trigger) ||
                   HasParameter(parameterName, AnimatorControllerParameterType.Bool);
        }

        private bool HasAnyFloatParameter(params string[] parameterNames)
        {
            EnsureAnimator();
            if (parameterNames == null)
            {
                return false;
            }

            for (var i = 0; i < parameterNames.Length; i++)
            {
                if (HasParameter(parameterNames[i], AnimatorControllerParameterType.Float))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasAnyTriggerOrBoolParameter(params string[] parameterNames)
        {
            EnsureAnimator();
            if (parameterNames == null)
            {
                return false;
            }

            for (var i = 0; i < parameterNames.Length; i++)
            {
                if (HasTriggerOrBoolParameter(parameterNames[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasParameter(string parameterName, AnimatorControllerParameterType type)
        {
            if (animator == null || string.IsNullOrEmpty(parameterName))
            {
                return false;
            }

            var parameters = animator.parameters;
            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.type == type && parameter.name == parameterName)
                {
                    return true;
                }
            }

            return false;
        }

        private void WarnMissingParameter(string command, AnimatorControllerParameterType expectedType, params string[] parameterNames)
        {
            EnsureAnimator();
            var controllerName = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.name
                : "no controller";
            LastAnimationWarning = $"{command}: missing {expectedType} parameter ({string.Join(", ", parameterNames)}) on {controllerName}.";
            Debug.LogWarning($"[{nameof(CharacterActorVisual)}] {LastAnimationWarning}", this);
        }

        private static Vector3 SafeScale(Vector3 value)
        {
            return value == Vector3.zero ? Vector3.one : value;
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
                if (string.Equals(child.name, "VFXRoot", System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (HasRenderableChildren(child))
                {
                    return child;
                }
            }

            return null;
        }
    }
}
