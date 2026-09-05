using System.Collections;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Dev-only OnGUI harness that auditions the per-card-type presentation scenarios for the card/effect
    /// SFX split (decision B). For each scenario it plays the immediate <b>cast</b> SFX layer right away
    /// (<c>card.{kind}.cast</c> via <see cref="CombatAudioPresenter.RequestCue"/>), then fires the
    /// <b>effect</b> layer (VFX + impact SFX + floating text) after the cue's authored
    /// <see cref="EffectVfxCatalog.Entry.PlaybackDelaySeconds"/> ??exactly as the runtime does, so the two
    /// layers can be heard/seen in isolation without driving a full combat. Same-assembly, so it reuses the
    /// real <see cref="CombatAudioPresenter.MapEffectToCueIds"/> mapping rather than reinventing it.
    /// </summary>
    public sealed class CardPresentationScenarioPlayer : MonoBehaviour
    {
        [SerializeField] private EffectPresentationController presentation;
        [SerializeField] private CombatAudioPresenter audioPresenter;
        [SerializeField] private MapCombatController controller;
        [Tooltip("World anchor for spawned demo effects. Defaults to the player position, then this transform.")]
        [SerializeField] private Transform anchor;
        [SerializeField] private bool showGui = true;
        [SerializeField] private int demoAmount = 7;

        private void Awake()
        {
            ResolveReferences();
        }

        private void ResolveReferences()
        {
            if (presentation == null)
            {
                presentation = FindFirstObjectByType<EffectPresentationController>();
            }

            if (audioPresenter == null)
            {
                audioPresenter = FindFirstObjectByType<CombatAudioPresenter>();
            }

            if (controller == null)
            {
                controller = FindFirstObjectByType<MapCombatController>();
            }
        }

        private void OnGUI()
        {
            if (!showGui)
            {
                return;
            }

            ResolveReferences();
            GUILayout.BeginArea(new Rect(12f, 12f, 280f, 420f), GUI.skin.box);
            GUILayout.Label("Card Presentation Scenarios (B)");
            GUILayout.Label("cast SFX = 즉시 · effect = VFX 동기");

            if (GUILayout.Button("공격 (Attack)"))
            {
                PlayCastThenEffect(
                    CombatCardKind.Attack,
                    new EffectResultEvent(EffectKind.Damage, targetUnitId: "monster", appliedAmount: demoAmount, amount: demoAmount, sourceRef: "A01"));
            }

            if (GUILayout.Button("방어 (Defend)"))
            {
                PlayCastThenEffect(
                    CombatCardKind.Defend,
                    new EffectResultEvent(EffectKind.Block, targetUnitId: "player", appliedAmount: 5, amount: 5, sourceRef: "D01"));
            }

            if (GUILayout.Button("정찰 (Scout)"))
            {
                PlayCastThenEffect(
                    CombatCardKind.Scout,
                    new EffectResultEvent(EffectKind.FogReveal, targetUnitId: "field", appliedAmount: 1, amount: 1, radius: 1, sourceRef: "S01"));
            }

            if (GUILayout.Button("버프 - 민첩 (Buff/Agility)"))
            {
                PlayCastThenEffect(
                    CombatCardKind.Buff,
                    new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", appliedAmount: 2, amount: 2, statusKind: StatusEffectKind.Agility));
            }

            if (GUILayout.Button("이동 (Move)"))
            {
                PlayCastThenEffect(
                    AudioCueIds.CardMoveCast,
                    new EffectResultEvent(EffectKind.Push, targetUnitId: "player", sourceRef: "player.move"));
            }

            if (GUILayout.Button("필드 - 모닥불 (campfire/FogReveal)"))
            {
                PlayCastThenEffect(
                    AudioCueIds.CardFieldCast,
                    new EffectResultEvent(EffectKind.FogReveal, targetUnitId: "field", appliedAmount: 1, amount: 1, radius: 1, sourceRef: "field.fog_reveal.campfire"));
            }

            if (GUILayout.Button("필드 - 화염병 (firebomb/Damage)"))
            {
                PlayCastThenEffect(
                    AudioCueIds.CardFieldCast,
                    new EffectResultEvent(EffectKind.Damage, targetUnitId: "field", appliedAmount: 4, amount: 4, sourceRef: "field.damage.firebomb"));
            }

            if (GUILayout.Button("유틸 (Utility)"))
            {
                PlayCastCue(AudioCueIds.CardUtilityCast);
            }

            GUILayout.Space(8f);
            GUILayout.Label("상태 레이어(이전 작업 확인용)");

            if (GUILayout.Button("중독 부여(apply burst)"))
            {
                PlayEffectOnly(new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", appliedAmount: 2, amount: 2, statusKind: StatusEffectKind.Poison));
            }

            if (GUILayout.Button("중독 피해(Damage+StatusKind, 데이터 통제)"))
            {
                PlayEffectOnly(new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", appliedAmount: 2, amount: 2, sourceRef: "test.poison", statusKind: StatusEffectKind.Poison));
            }

            if (GUILayout.Button("상태 해제 (StatusEffectExpired)"))
            {
                PlayEffectOnly(new EffectResultEvent(EffectKind.StatusEffectExpired, targetUnitId: "player", appliedAmount: 0, amount: 0, statusKind: StatusEffectKind.Poison));
            }

            GUILayout.Space(8f);
            if (GUILayout.Button("Clear VFX"))
            {
                presentation?.ClearSpawnedEffects();
            }

            GUILayout.EndArea();
        }

        private void PlayCastThenEffect(CombatCardKind kind, EffectResultEvent effect)
        {
            PlayCastCue(kind);
            PlayEffectOnly(effect);
        }

        private void PlayCastThenEffect(string castCueId, EffectResultEvent effect)
        {
            PlayCastCue(castCueId);
            PlayEffectOnly(effect);
        }

        private void PlayCastCue(string cueId)
        {
            if (audioPresenter != null && !string.IsNullOrEmpty(cueId))
            {
                audioPresenter.RequestCue(cueId, "scenario-cast");
            }
        }

        // L1: the immediate cast SFX, played the instant the card is "used".
        private void PlayCastCue(CombatCardKind kind)
        {
            if (audioPresenter == null)
            {
                return;
            }

            var cueId = ResolveCastCue(kind);
            if (!string.IsNullOrEmpty(cueId))
            {
                audioPresenter.RequestCue(cueId, $"scenario-cast:{kind}");
            }
        }

        private static string ResolveCastCue(CombatCardKind kind)
        {
            switch (kind)
            {
                case CombatCardKind.Attack: return AudioCueIds.CardAttackCast;
                case CombatCardKind.Defend: return AudioCueIds.CardDefendCast;
                case CombatCardKind.Buff: return AudioCueIds.CardBuffCast;
                default: return string.Empty;
            }
        }

        // L2: the effect layer (VFX + impact SFX + floating text), fired after the cue's authored delay so it
        // lands in sync with the VFX ??mirroring the runtime's one-beat synchronization.
        private void PlayEffectOnly(EffectResultEvent effect)
        {
            var delay = ResolveEffectDelaySeconds(effect);
            if (delay > 0f && isActiveAndEnabled)
            {
                StartCoroutine(FireEffectAfter(effect, delay));
            }
            else
            {
                FireEffectNow(effect);
            }
        }

        private IEnumerator FireEffectAfter(EffectResultEvent effect, float delay)
        {
            yield return new WaitForSeconds(delay);
            FireEffectNow(effect);
        }

        private void FireEffectNow(EffectResultEvent effect)
        {
            if (presentation != null)
            {
                presentation.Play(effect, ResolveWorldPosition(), Quaternion.identity);
            }

            if (audioPresenter != null)
            {
                // 공격·정찰 큐는 이제 저작(cards.csv type)에서 갈리므로 카탈로그를 넘겨야 한다 —
                // 안 넘기면 이 랩에서만 그 두 큐가 조용히 빠져 실게임과 다르게 들린다.
                foreach (var cueId in CombatAudioPresenter.MapEffectToCueIds(effect, null, controller != null ? controller.State?.CardCatalog : null))
                {
                    audioPresenter.RequestCue(cueId, $"scenario-effect:{effect.Kind}");
                }
            }
        }

        private float ResolveEffectDelaySeconds(EffectResultEvent effect)
        {
            var catalog = presentation != null ? presentation.VfxCatalog : null;
            return catalog != null && catalog.TryResolve(effect, out var entry) && entry != null
                ? entry.PlaybackDelaySeconds
                : 0f;
        }

        private Vector3 ResolveWorldPosition()
        {
            if (controller != null && controller.TryGetPlayerWorldPosition(out var playerWorld))
            {
                return playerWorld + Vector3.up * 0.5f;
            }

            return anchor != null ? anchor.position : transform.position;
        }
    }
}
