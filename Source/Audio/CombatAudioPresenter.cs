using System;
using System.Collections.Generic;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Runtime.Presentation;
using UnityEngine;
using UnityEngine.Audio;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Thin Unity presentation component that observes resolved combat/UI state and requests sound cues.
    /// It never mutates gameplay state; missing catalog entries or null clips only update debug fields.
    /// </summary>
    public sealed class CombatAudioPresenter : MonoBehaviour
    {
        private const string MixerResourcePath = "Combat/SeoulPlayupMixer";
        private const float VoiceCueDelaySeconds = 0.2f;

        // Stored as the base MonoBehaviour (not the concrete MapCombatController) so the audio
        // layer depends only on the ICombatAudioHost seam, not the Unity combat host. Widening the
        // serialized type preserves existing scene/prefab references (same field name + base type).
        [SerializeField] private MonoBehaviour controller;
        [SerializeField] private SoundCatalog catalog;
        [SerializeField] private bool createRuntimeP0CatalogWhenMissing = true;
        [SerializeField] private bool autoFindController = true;
        [SerializeField] private bool autoCreateAudioSources = true;
        [SerializeField] private AudioMixer audioMixer;
        [SerializeField] private AudioSource uiSource;
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioSource ambienceSource;
        [SerializeField] private AudioSource musicSource;
        [SerializeField] private AudioSource debugSource;

        private readonly List<string> requestedCueHistory = new List<string>();

        // Monsters whose death has already been sounded off the lethal-effect stream this combat.
        // The host's post-timeline death fallback checks this so a death sounds exactly once: the
        // impact-synced cue wins, and the fallback only covers deaths the effect stream never carried.
        private readonly HashSet<string> deathAudioAnnouncedUnitIds = new HashSet<string>(StringComparer.Ordinal);

        // Set once the player-death sequence has sounded combat.player.death. The Defeat phase cue
        // (game.defeat) shares that clip, so it is suppressed for the rest of the combat rather than
        // replaying the same sample a second time.
        private bool playerDeathCueSounded;

        // Fade length for looping background transitions (music / ambience). 0 restores the old hard cut.
        [SerializeField] [Min(0f)] private float backgroundFadeSeconds = 0.6f;

        // The fade coroutine in flight per background source. A transition arriving mid-fade must cancel
        // the previous one: two coroutines writing the same AudioSource.volume leave the final level to
        // whichever happens to run last, which reads as a random volume jump.
        private readonly Dictionary<AudioSource, Coroutine> backgroundFades =
            new Dictionary<AudioSource, Coroutine>();

        private CombatState subscribedState;
        private ICombatAudioHost subscribedController;

        /// <summary>
        /// The bound combat host viewed through the audio seam interface. Honors Unity's
        /// destroyed-object null so member access matches the previous concrete-field behavior.
        /// </summary>
        private ICombatAudioHost Host => controller != null ? controller as ICombatAudioHost : null;

        private static MonoBehaviour FindFirstAudioHost()
        {
            foreach (var behaviour in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour is ICombatAudioHost)
                {
                    return behaviour;
                }
            }

            return null;
        }
        private CombatPhase? lastObservedPhase;
        private string lastObjectiveStatusText = string.Empty;
        private bool lastObjectiveRevealPresentable;
        private SoundCatalog runtimeFallbackCatalog;
        [SerializeField] private bool playGameplayMusicOnEnable = true;
        private bool gameplayMusicStarted;

        public string LastRequestedCueId { get; private set; } = string.Empty;
        public string LastPlayedCueId { get; private set; } = string.Empty;
        public string LastSkippedCueId { get; private set; } = string.Empty;
        public string LastMissingCueId { get; private set; } = string.Empty;
        public SoundPlaybackStatus LastStatus { get; private set; } = SoundPlaybackStatus.InvalidCueId;
        public IReadOnlyList<string> RequestedCueHistory => requestedCueHistory;
        public SoundCatalog Catalog => catalog != null ? catalog : runtimeFallbackCatalog;

        public void Bind(ICombatAudioHost host, SoundCatalog soundCatalog = null)
        {
            if (soundCatalog != null)
            {
                catalog = soundCatalog;
            }

            if (!ReferenceEquals(Host, host))
            {
                UnsubscribeController();
                controller = host as MonoBehaviour;
            }

            SubscribeController();
            ResubscribeState();
            CaptureCurrentPhaseBaseline();
            CaptureObjectiveBaseline();
        }

        public SoundPlaybackStatus RequestCue(string cueId, string context = "")
        {
            LastRequestedCueId = cueId ?? string.Empty;
            requestedCueHistory.Add(LastRequestedCueId);

            var activeCatalog = EnsureCatalog();
            if (activeCatalog == null)
            {
                LastSkippedCueId = LastRequestedCueId;
                LastStatus = SoundPlaybackStatus.MissingCue;
                return LastStatus;
            }

            LastStatus = activeCatalog.TryBeginPlayback(LastRequestedCueId, Time.realtimeSinceStartup, out var entry);

            // Recorded with the resolved status, so a cue swallowed by its cooldown is visible as such
            // instead of looking like a sound that simply never fired.
            CombatPresentationTrace.Record(
                CombatTraceChannel.Sfx,
                LastRequestedCueId,
                string.IsNullOrEmpty(context) ? LastStatus.ToString() : $"{LastStatus} ({context})");

            switch (LastStatus)
            {
                case SoundPlaybackStatus.Playable:
                    Play(entry);
                    LastPlayedCueId = LastRequestedCueId;
                    RequestPairedCueIfNeeded(entry, context);
                    break;
                case SoundPlaybackStatus.MissingCue:
                case SoundPlaybackStatus.MissingClip:
                    LastMissingCueId = LastRequestedCueId;
                    LastSkippedCueId = LastRequestedCueId;
                    MaybeLogMissing(entry, LastStatus, context);
                    break;
                default:
                    LastSkippedCueId = LastRequestedCueId;
                    break;
            }

            return LastStatus;
        }

        public void RestartGameplayAudio()
        {
            StopAllCoroutines();
            backgroundFades.Clear();
            ResetCueCooldowns();
            StopTransientAudio();
            // A restart is a hard reset, not a musical transition: cut the background instead of fading it,
            // so the freshly requested loop is not racing a fade-out of the loop it is replacing.
            StopLoopingBackground(immediate: true);
            gameplayMusicStarted = false;
            ResetPerCombatAudioState();
            CaptureCurrentPhaseBaseline();
            CaptureObjectiveBaseline();
            StartGameplayMusicIfNeeded();
        }

        internal static IReadOnlyList<string> MapEffectToCueIds(EffectResultEvent effect)
        {
            return MapEffectToCueIds(effect, null, null);
        }

        /// <param name="cardCatalog">
        /// 공격·정찰 큐를 가르는 카드 분류의 정본(저작 <c>cards.csv</c>의 <c>type</c> 컬럼).
        /// 없으면 그 두 큐만 빠진다 — 예전 ID switch의 default와 같은 답이다.
        /// 출하 경로는 <c>subscribedState.CardCatalog</c>를 넘긴다.
        /// </param>
        internal static IReadOnlyList<string> MapEffectToCueIds(
            EffectResultEvent effect,
            Func<string, string> resolveMonsterDefinitionId,
            CardCatalogDefinition cardCatalog,
            MonsterCatalogDefinition monsterCatalog = null)
        {
            var cues = new List<string>();

            // Installing a field object is not an impact. Its announce effect borrows the tick's EffectKind
            // (Damage / Heal / StatusEffectApplied) purely so the footprint renders, so without this guard it
            // falls through to the kind's normal branch and pre-plays the tick's sound — a monster hit for
            // F01/F04/F05, effect.heal for F02, effect.immobilize for F03 — before anything has been hit.
            // The placement already sounds via the card.field.cast cast cue.
            if (CombatEffectSourceClassifier.IsFieldPlacementAnnounce(effect.SourceRef))
            {
                return cues;
            }

            if (IsTrapSource(effect.SourceRef) && IsTrapActivation(effect))
            {
                cues.Add(AudioCueIds.EffectTrapTrigger);
            }

            switch (effect.Kind)
            {
                case EffectKind.Block:
                case EffectKind.DamageBlocked:
                    AddMonsterAttackCues(effect, cues, resolveMonsterDefinitionId, monsterCatalog);
                    cues.Add(AudioCueIds.CardDefendResolve);
                    break;
                case EffectKind.FogReveal:
                    if (CombatEffectSourceClassifier.IsScoutCardSource(effect.SourceRef, cardCatalog))
                    {
                        cues.Add(AudioCueIds.CardScoutResolve);
                    }
                    cues.Add(AudioCueIds.FogTileRevealed);
                    break;
                case EffectKind.Damage:
                    if (IsSilentFieldTickAnnounceVfx(effect))
                    {
                        break;
                    }
                    if (IsFieldDamageEffect(effect))
                    {
                        cues.Add(AudioCueIds.FieldDamageEffect);
                        if (string.Equals(effect.TargetUnitId, "field", StringComparison.Ordinal))
                        {
                            break;
                        }
                    }
                    if (IsKnockbackImpact(effect.SourceRef))
                    {
                        cues.Add(AudioCueIds.EffectKnockbackImpact);
                    }
                    if (CombatEffectSourceClassifier.IsPlayerAttackCardSource(effect.SourceRef, cardCatalog))
                    {
                        cues.Add(AudioCueIds.CardAttackResolve);
                        cues.Add(AudioCueIds.PlayerVoiceAttack);
                        if (effect.WeakSpotHit)
                        {
                            // 취약타(§20-A)는 패턴이 아니라 명중 판정에 붙는 소리다 — 규칙이 Damage 연출 이벤트에 찍어 준다.
                            cues.Add(AudioCueIds.BossWeakSpotHit);
                        }
                        AddMonsterHitCues(effect, cues, resolveMonsterDefinitionId);
                    }
                    else if (IsPlayerTarget(effect.TargetUnitId))
                    {
                        AddMonsterAttackCues(effect, cues, resolveMonsterDefinitionId, monsterCatalog);
                        if (!effect.Lethal)
                        {
                            AddPlayerHitCues(effect, cues);
                        }
                    }
                    else
                    {
                        AddMonsterHitCues(effect, cues, resolveMonsterDefinitionId);
                    }
                    break;
                case EffectKind.ReflectDamage:
                    AddMonsterHitCues(effect, cues, resolveMonsterDefinitionId);
                    break;
                case EffectKind.Heal:
                    cues.Add(AudioCueIds.EffectHeal);
                    break;
                case EffectKind.Push:
                case EffectKind.Knockback:
                    cues.Add(AudioCueIds.EffectPush);
                    break;
                case EffectKind.StatusEffectApplied:
                    if (IsSilentFieldTickAnnounceVfx(effect))
                    {
                        break;
                    }
                    if (IsFieldFlashbangEffect(effect))
                    {
                        cues.Add(AudioCueIds.FieldFlashbangEffect);
                        break;
                    }
                    if (IsFieldFlashbangMonsterHit(effect))
                    {
                        cues.Add(AudioCueIds.EffectImmobilize);
                        break;
                    }
                    if (TryResolveNonDamagingPatternImpactCue(effect, monsterCatalog, out var patternCue))
                    {
                        // 피해 0 패턴(자기부여·비타격형)은 상태 부여가 곧 「맞는 순간」이다 — 그 패턴의 타격음이
                        // 범용 부여음(버프/디버프)을 대신한다. 피해가 있는 패턴은 Damage 이벤트가 이미 울렸으므로
                        // 여기서는 종전대로 상태 부여음만 낸다(같은 패턴음이 두 번 나지 않게).
                        cues.Add(patternCue);
                        break;
                    }
                    AddStatusEffectCue(effect.StatusKind, cues);
                    break;
                case EffectKind.AttackMissed:
                    // 빗나감에는 타격음이 없다(발주 구조가 「맞는 순간」 한 클립이라) — 예고음·울음만.
                    AddMonsterAttackCues(effect, cues, resolveMonsterDefinitionId, monsterCatalog: null);
                    break;
                case EffectKind.StatusEffectExpired:
                    // 「지속 효과가 끝나는 순간」(발주 B, 이로움/해로움 공용) — 클립이 Player 폴더인 대로 플레이어 것만.
                    // 수호는 자연 만료가 없고(expirePolicy OnConsume) 소비 시 StatusNegated가 같은 순간 따로 울리므로 뺀다.
                    if (IsPlayerTarget(effect.TargetUnitId) && effect.StatusKind != StatusEffectKind.Guard)
                    {
                        cues.Add(AudioCueIds.EffectExpire);
                    }
                    break;
                case EffectKind.StatusNegated:
                    if (IsPlayerTarget(effect.TargetUnitId))
                    {
                        cues.Add(AudioCueIds.EffectNegated);
                    }
                    break;
                case EffectKind.StatusCardInjected:
                    cues.Add(AudioCueIds.CurseInjected);
                    break;
                case EffectKind.MonsterTraitTriggered:
                    // 특성 알림은 원래 텍스트 전용 kind다 — 소리를 얹는 것은 은신 재진입 하나뿐이다
                    // (2026-09-04 피드백: 연막 VFX·Attack4 동작과 한 묶음). 나머지 ref는 조용히 지나간다.
                    if (string.Equals(effect.SourceRef, MonsterTraitAnnouncement.StealthHiddenRef, StringComparison.Ordinal))
                    {
                        cues.Add(AudioCueIds.MonsterStealthHide);
                    }
                    break;
            }

            return cues;
        }

        /// <summary>
        /// 부여 SFX의 정본은 <c>status_effects.csv</c>의 <c>applyAudioCueId</c>(1단계 구조 리팩토링) — 여기 있던
        /// switch는 <see cref="StatusEffectInfo"/> 폴백으로 옮겼다. 빈 값 = 무음(default 없던 옛 switch와 같다).
        /// </summary>
        private static void AddStatusEffectCue(StatusEffectKind? kind, List<string> cues)
        {
            if (!kind.HasValue)
            {
                return;
            }

            var cueId = StatusEffectInfo.ApplyAudioCueId(kind.Value);
            if (!string.IsNullOrWhiteSpace(cueId))
            {
                cues.Add(cueId);
            }
        }

        internal void HandlePhaseChangedForTests(CombatPhase previous, CombatPhase current)
        {
            HandlePhaseChanged(previous, current);
        }

        internal static bool IsObjectiveRevealPresentableForTests(CombatState state)
        {
            return IsObjectiveRevealPresentable(state);
        }

        // Boss BGM. The looping phase track (music.boss.<bossId>.p1/.p2/.p3) starts when the arena seals —
        // the encounter — and crossfades on each phase change; the transition also fires a one-shot stinger
        // on the Sfx bus. Cue ids are data-driven from the boss profile via BossPhaseState.BgmCueId, so no
        // boss is named here.
        private void OnBossArenaSealed(string arenaId)
        {
            var bgmCueId = ResolveActiveBossBgmCueId();
            if (!string.IsNullOrEmpty(bgmCueId))
            {
                RequestCue(bgmCueId, $"boss-arena-sealed:{arenaId}");
            }
        }

        private void OnBossPhaseChanged(string bossUnitId, int from, int to)
        {
            var bgmCueId = subscribedState != null
                           && subscribedState.TryGetBossPhaseState(bossUnitId, out var phase)
                ? phase.BgmCueId
                : string.Empty;
            RequestBossPhaseTransitionAudio(bgmCueId, $"boss-phase:{bossUnitId}:{from}->{to}");
        }

        // A combat has at most one boss BGM playing at a time on the shared music source: take the first
        // boss with an authored cue base.
        private string ResolveActiveBossBgmCueId()
        {
            if (subscribedState == null)
            {
                return string.Empty;
            }

            foreach (var boss in subscribedState.BossPhases)
            {
                if (!string.IsNullOrEmpty(boss.BgmCueId))
                {
                    return boss.BgmCueId;
                }
            }

            return string.Empty;
        }

        // Crossfades to the phase BGM (a looping background cue) and fires the transition stinger. The
        // stinger is deliberately a distinct Sfx-bus one-shot: routing it through the Music bus would cut
        // the very loop it is announcing (Play() stops a looping Music source before a Music one-shot).
        private void RequestBossPhaseTransitionAudio(string bgmCueId, string context)
        {
            if (!string.IsNullOrEmpty(bgmCueId))
            {
                RequestCue(bgmCueId, context);
            }

            RequestCue(AudioCueIds.BossPhaseTransitionStinger, context + ":stinger");
        }

        internal void HandleBossPhaseChangedForTests(string bossUnitId, int from, int to)
        {
            OnBossPhaseChanged(bossUnitId, from, to);
        }

        internal void HandleBossArenaSealedForTests(string arenaId)
        {
            OnBossArenaSealed(arenaId);
        }

        internal void RequestBossPhaseTransitionAudioForTests(string bgmCueId, string context)
        {
            RequestBossPhaseTransitionAudio(bgmCueId, context);
        }

        internal static bool IsLoopingBackgroundCueForTests(string cueId)
        {
            return IsLoopingBackgroundCue(cueId);
        }

        private void Awake()
        {
            EnsureAudioSources();
            if (controller == null && autoFindController)
            {
                controller = FindFirstAudioHost();
            }
        }

        private void OnEnable()
        {
            SoundSettingsService.EnsureLoaded();
            SoundSettingsService.SettingsChanged += ApplySoundSettingsToSources;
            SubscribeController();
            ResubscribeState();
            CaptureCurrentPhaseBaseline();
            CaptureObjectiveBaseline();
            ApplySoundSettingsToSources();
            StartGameplayMusicIfNeeded();
        }

        private void OnDisable()
        {
            SoundSettingsService.SettingsChanged -= ApplySoundSettingsToSources;
            // Disabling stops every coroutine, so the recorded handles are already dead; keeping them would
            // make ApplySoundSettingsToSources skip sources that are no longer fading.
            backgroundFades.Clear();
            UnsubscribeState();
            UnsubscribeController();
        }

        private void Update()
        {
            if (controller == null && autoFindController)
            {
                controller = FindFirstAudioHost();
            }

            SubscribeController();
            ResubscribeState();
            ObservePhaseTransition();
            ObserveObjectiveReveal();
        }

        private void OnEffectResolved(EffectResultEvent effect)
        {
            // Keep the hit SFX in sync with its VFX/floating number: those spawn after the cue's authored
            // PlaybackDelaySeconds (timed to the animation), so the sound waits the same amount instead of
            // firing the instant the effect resolves. The per-target stagger delay is honored too (whichever
            // is larger), so field/bomb impact SFX lands with the same hit VFX/floating number instead of
            // after the whole stagger chain.
            //
            // The VFX cue is the only place impact SFX timing is authored — there is deliberately no SFX
            // channel offset on the timing profile or the per-attack table, so a hit's picture and its
            // sound cannot be tuned apart. See docs/source/10-specs/schema/sounds.md.
            MarkMonsterDeathAnnouncedIfLethal(effect);

            var delay = 0f;
            var host = Host;
            if (host != null)
            {
                delay = host.ResolveEffectVfxDelaySeconds(effect);
            }
            delay = Mathf.Max(delay, effect.DelaySeconds);

            if (delay > 0f && isActiveAndEnabled)
            {
                StartCoroutine(EmitEffectCuesAfterDelay(effect, delay));
            }
            else
            {
                EmitEffectCues(effect);
            }

            ObserveObjectiveReveal();
        }

        private void EmitEffectCues(EffectResultEvent effect)
        {
            foreach (var cueId in MapEffectToCueIds(effect, ResolveMonsterDefinitionId, subscribedState?.CardCatalog, subscribedState?.MonsterCatalog))
            {
                RequestCueWithVoiceDelay(cueId, $"effect:{effect.Kind}:{effect.SourceRef}");
            }
        }

        private System.Collections.IEnumerator EmitEffectCuesAfterDelay(EffectResultEvent effect, float delay)
        {
            yield return new WaitForSeconds(delay);
            EmitEffectCues(effect);
        }

        private void RequestCueWithVoiceDelay(string cueId, string context)
        {
            if (IsVoiceCue(cueId))
            {
                StartCoroutine(RequestCueAfterDelay(cueId, context, VoiceCueDelaySeconds));
                return;
            }

            RequestCue(cueId, context);
        }

        private System.Collections.IEnumerator RequestCueAfterDelay(string cueId, string context, float delay)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            RequestCue(cueId, context);
        }

        private void OnPlayerHandsDrawn()
        {
            RequestCue(AudioCueIds.UiCardDraw, "hand:drawn");
        }

        private void OnHandCardsDiscarded(int cardCount)
        {
            RequestCue(AudioCueIds.UiCardDiscard, $"hand:discarded:{cardCount}");
        }

        // 소모품 사용음(2026-09 발주 A). 12종이 호리병 6·구슬 6 두 묶음이라 클립도 둘뿐이다 — 묶음은
        // 아이템 id 접미(-flask / -bead)가 정본이고(consumable_items.csv의 category는 생존·버프·즉발… 다른 축),
        // 어느 쪽도 아니면 무음이다(새 묶음이 생기면 클립도 새로 받아야 한다).
        private void OnBagItemUsed(string itemId)
        {
            var cueId = ResolveBagItemUseCueId(itemId);
            if (!string.IsNullOrEmpty(cueId))
            {
                RequestCue(cueId, $"item:use:{itemId}");
            }
        }

        internal static string ResolveBagItemUseCueId(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return string.Empty;
            }

            if (itemId.EndsWith("-flask", StringComparison.Ordinal))
            {
                return AudioCueIds.ItemFlaskUse;
            }

            return itemId.EndsWith("-bead", StringComparison.Ordinal) ? AudioCueIds.ItemBeadUse : string.Empty;
        }

        private void OnTrapMonstersSpawned(string trapId, int count)
        {
            RequestCue(AudioCueIds.TrapSpawn, $"trap:spawn:{trapId}:{count}");
        }

        private void OnControllerAudioCueRequested(string cueId, string context)
        {
            RequestCueWithVoiceDelay(cueId, context);
        }

        /// <summary>
        /// Post-timeline death fallback from the host. Silent for any monster whose death the lethal
        /// effect already sounded at impact — otherwise the same death played twice, far enough apart
        /// (timeline length vs. impact) that the cue cooldown never swallowed it.
        /// </summary>
        private void OnControllerMonsterDeathAudioRequested(string unitId, string monsterDefinitionId, string context)
        {
            if (!string.IsNullOrEmpty(unitId) && !deathAudioAnnouncedUnitIds.Add(unitId))
            {
                CombatPresentationTrace.Record(
                    CombatTraceChannel.Sfx,
                    AudioCueIds.CombatEnemyDeath,
                    $"SuppressedDuplicate ({context}:{unitId})");
                return;
            }

            RequestCueWithVoiceDelay(AudioCueIds.CombatEnemyDeath, $"{context}:{unitId}:common");

            // Guarded so an unresolved definition id (which falls back to combat.enemy.death) does not
            // re-request the common cue we just played.
            var voiceCueId = AudioCueIds.MonsterDeath(monsterDefinitionId);
            if (IsMonsterVoiceCue(voiceCueId))
            {
                RequestCueWithVoiceDelay(voiceCueId, $"{context}:{unitId}:voice");
            }
        }

        /// <summary>
        /// Claims a monster's death for the impact path the moment the lethal effect resolves — before the
        /// cue itself is emitted, which can be delayed by the VFX playback delay until after the timeline
        /// (and therefore after the host's fallback) has already run.
        /// </summary>
        private void MarkMonsterDeathAnnouncedIfLethal(EffectResultEvent effect)
        {
            if (!effect.Lethal || string.IsNullOrEmpty(effect.TargetUnitId) || IsPlayerTarget(effect.TargetUnitId))
            {
                return;
            }

            foreach (var cueId in MapEffectToCueIds(effect, ResolveMonsterDefinitionId, subscribedState?.CardCatalog, subscribedState?.MonsterCatalog))
            {
                if (string.Equals(cueId, AudioCueIds.CombatEnemyDeath, StringComparison.Ordinal))
                {
                    deathAudioAnnouncedUnitIds.Add(effect.TargetUnitId);
                    return;
                }
            }
        }

        private void ObservePhaseTransition()
        {
            var host = Host;
            if (host == null || host.State == null)
            {
                lastObservedPhase = null;
                return;
            }

            var current = host.State.Phase;
            if (!lastObservedPhase.HasValue)
            {
                lastObservedPhase = current;
                return;
            }

            if (lastObservedPhase.Value == current)
            {
                return;
            }

            var previous = lastObservedPhase.Value;
            lastObservedPhase = current;
            HandlePhaseChanged(previous, current);
        }

        private void HandlePhaseChanged(CombatPhase previous, CombatPhase current)
        {
            if (previous == current)
            {
                return;
            }

            if (current == CombatPhase.Defeat)
            {
                // game.defeat currently shares its clip with combat.player.death, which the death sequence
                // already played (and which is timed to the death beat). They are separate cues, so the
                // cooldown never caught the repeat — suppress here instead. Drop this guard once a dedicated
                // defeat stinger is authored for game.defeat.
                if (playerDeathCueSounded)
                {
                    CombatPresentationTrace.Record(
                        CombatTraceChannel.Sfx,
                        AudioCueIds.GameDefeat,
                        $"SuppressedDuplicate (phase:{previous}->{current})");
                    return;
                }

                RequestCue(AudioCueIds.GameDefeat, $"phase:{previous}->{current}");
                return;
            }

            if (current == CombatPhase.Victory)
            {
                var host = Host;
                if (host != null && host.ShouldSuppressVictoryPhaseAudio)
                {
                    return;
                }

                RequestCue(AudioCueIds.GameVictory, $"phase:{previous}->{current}");
                return;
            }

            // Entering an enemy phase already sounds phase.monster.begin at the gameplay site; layering the
            // generic transition cue on top of it stacked two sounds on the same instant and made the
            // per-turn phase chatter (4 transitions every turn) heavier than it needed to be.
            if (IsMonsterPhase(current))
            {
                return;
            }

            RequestCue(AudioCueIds.UiPhaseChange, $"phase:{previous}->{current}");
        }

        private static bool IsMonsterPhase(CombatPhase phase)
        {
            return phase == CombatPhase.MonsterMovement || phase == CombatPhase.MonsterAction;
        }

        private void CaptureCurrentPhaseBaseline()
        {
            var host = Host;
            lastObservedPhase = host != null && host.State != null ? host.State.Phase : (CombatPhase?)null;
        }

        private void CaptureObjectiveBaseline()
        {
            var state = Host?.State;
            lastObjectiveStatusText = state != null ? state.ObjectiveStatusText : string.Empty;
            lastObjectiveRevealPresentable = IsObjectiveRevealPresentable(state);
        }

        private void ObserveObjectiveReveal()
        {
            var state = Host?.State;
            if (state == null)
            {
                lastObjectiveStatusText = string.Empty;
                lastObjectiveRevealPresentable = false;
                return;
            }

            var currentPresentable = IsObjectiveRevealPresentable(state);
            var statusChanged = !string.Equals(lastObjectiveStatusText, state.ObjectiveStatusText, System.StringComparison.Ordinal);
            if (!lastObjectiveRevealPresentable && currentPresentable && statusChanged)
            {
                RequestCue(AudioCueIds.ObjectiveMemoryGyeolRevealed, "objective:memory-gyeol-revealed");
            }

            lastObjectiveStatusText = state.ObjectiveStatusText;
            lastObjectiveRevealPresentable = currentPresentable;
        }

        private void SubscribeController()
        {
            var host = Host;
            if (host == null || ReferenceEquals(subscribedController, host))
            {
                return;
            }

            UnsubscribeController();
            subscribedController = host;
            subscribedController.AudioCueRequested += OnControllerAudioCueRequested;
            subscribedController.MonsterDeathAudioRequested += OnControllerMonsterDeathAudioRequested;
        }

        private void UnsubscribeController()
        {
            if (subscribedController != null)
            {
                subscribedController.AudioCueRequested -= OnControllerAudioCueRequested;
                subscribedController.MonsterDeathAudioRequested -= OnControllerMonsterDeathAudioRequested;
                subscribedController = null;
            }
        }

        private void ResubscribeState()
        {
            var currentState = Host?.State;
            if (subscribedState == currentState)
            {
                return;
            }

            UnsubscribeState();
            subscribedState = currentState;
            if (subscribedState != null)
            {
                subscribedState.EffectResolved += OnEffectResolved;
                subscribedState.PlayerHandsDrawn += OnPlayerHandsDrawn;
                subscribedState.HandCardsDiscarded += OnHandCardsDiscarded;
                subscribedState.BossPhaseChanged += OnBossPhaseChanged;
                subscribedState.BossArenaSealed += OnBossArenaSealed;
                subscribedState.BagItemUsed += OnBagItemUsed;
                subscribedState.TrapMonstersSpawned += OnTrapMonstersSpawned;
            }

            // A different CombatState means a different combat: unit ids and the death sequence restart.
            ResetPerCombatAudioState();
            CaptureObjectiveBaseline();
        }

        private void ResetPerCombatAudioState()
        {
            deathAudioAnnouncedUnitIds.Clear();
            playerDeathCueSounded = false;
        }

        private void UnsubscribeState()
        {
            if (subscribedState != null)
            {
                subscribedState.EffectResolved -= OnEffectResolved;
                subscribedState.PlayerHandsDrawn -= OnPlayerHandsDrawn;
                subscribedState.HandCardsDiscarded -= OnHandCardsDiscarded;
                subscribedState.BossPhaseChanged -= OnBossPhaseChanged;
                subscribedState.BossArenaSealed -= OnBossArenaSealed;
                subscribedState.BagItemUsed -= OnBagItemUsed;
                subscribedState.TrapMonstersSpawned -= OnTrapMonstersSpawned;
                subscribedState = null;
            }
        }

        private SoundCatalog EnsureCatalog()
        {
            if (catalog != null)
            {
                return catalog;
            }

            if (!createRuntimeP0CatalogWhenMissing)
            {
                return null;
            }

            if (runtimeFallbackCatalog == null)
            {
                runtimeFallbackCatalog = SoundCatalog.CreateP0PlaceholderCatalogForRuntime();
            }

            return runtimeFallbackCatalog;
        }

        private void EnsureAudioSources()
        {
            if (!autoCreateAudioSources)
            {
                return;
            }

            var mixer = ResolveMixer();
            uiSource = EnsureAudioSource(uiSource, "UI Audio Source", mixer, "UI");
            sfxSource = EnsureAudioSource(sfxSource, "SFX Audio Source", mixer, "SFX");
            ambienceSource = EnsureAudioSource(ambienceSource, "Ambience Audio Source", mixer, "Ambience");
            musicSource = EnsureAudioSource(musicSource, "Music Audio Source", mixer, "Music");
            debugSource = EnsureAudioSource(debugSource, "Debug Audio Source", mixer, "Debug");
        }

        private AudioSource EnsureAudioSource(AudioSource source, string childName, AudioMixer mixer, string busName)
        {
            if (source == null)
            {
                var child = new GameObject(childName);
                child.transform.SetParent(transform, false);
                source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.spatialBlend = 0f;
            }

            RouteToMixerGroup(source, mixer, busName);
            return source;
        }

        private AudioMixer ResolveMixer()
        {
            if (audioMixer == null)
            {
                audioMixer = Resources.Load<AudioMixer>(MixerResourcePath);
            }

            return audioMixer;
        }

        // Routes a source to its bus group, matching on the group's leaf name so a substring like
        // "Master" cannot capture the wrong group. Scene-assigned routing is left untouched.
        private static void RouteToMixerGroup(AudioSource source, AudioMixer mixer, string busName)
        {
            if (source == null || mixer == null || source.outputAudioMixerGroup != null)
            {
                return;
            }

            var groups = mixer.FindMatchingGroups(busName);
            if (groups == null || groups.Length == 0)
            {
                return;
            }

            foreach (var group in groups)
            {
                if (group != null && group.name == busName)
                {
                    source.outputAudioMixerGroup = group;
                    return;
                }
            }

            source.outputAudioMixerGroup = groups[0];
        }

        private void Play(SoundCatalog.Entry entry)
        {
            if (entry == null || entry.Clip == null)
            {
                return;
            }

            var source = GetSource(entry.Bus);
            if (source == null)
            {
                LastSkippedCueId = entry.CueId;
                return;
            }

            var channelVolume = ResolveChannelVolume(entry);
            source.pitch = UnityEngine.Random.Range(entry.PitchMin, entry.PitchMax);
            if (IsLoopingBackgroundCue(entry.CueId))
            {
                PlayLooping(source, entry, channelVolume);
                return;
            }

            // Any non-looping music cue replaces the looping background so the two never overlap. It also
            // SHARES the music AudioSource with that background, which makes fading the loop out fatal to the
            // cue we are about to start: PlayOneShot is scaled by source.volume, so the fade drags the
            // one-shot down with it, and the Stop() at the end of the fade cuts it off outright. (It also
            // leaves source.volume parked at 0 — ApplySoundSettingsToSources only restores looping sources —
            // so every later music one-shot is silent too.) Cut the loop immediately instead, then hand the
            // source back at unit gain so the one-shot's own volume argument is the single place the BGM
            // setting is applied. music.victory was the only non-looping music cue, so this is exactly the
            // "victory BGM does not play" report.
            if (entry.Bus == SoundBus.Music)
            {
                // Ambience sits on its own source, so it can still fade out gracefully underneath the cue.
                FadeOutAndStop(ambienceSource, immediate: false);
                // The music source is the one the cue itself plays through: cancel any fade on it, cut the
                // loop synchronously, and hand it back at unit gain.
                CancelFade(source);
                StopIfLooping(source);
                source.volume = 1f;
            }

            source.PlayOneShot(entry.Clip, entry.Volume * channelVolume);
        }

        private static float ResolveChannelVolume(SoundCatalog.Entry entry)
        {
            if (entry == null)
            {
                return 1f;
            }

            if (IsVoiceCue(entry.CueId))
            {
                return SoundSettingsService.VoiceoverVolume;
            }

            switch (entry.Bus)
            {
                case SoundBus.Music:
                case SoundBus.Ambience:
                    return SoundSettingsService.BgmVolume;
                default:
                    return SoundSettingsService.SfxVolume;
            }
        }

        // Auto-start the gameplay BGM once when the presenter comes alive (guarded so it never double-starts).
        private void StartGameplayMusicIfNeeded()
        {
            if (playGameplayMusicOnEnable && !gameplayMusicStarted && EnsureCatalog() != null)
            {
                gameplayMusicStarted = RequestCue("music.gameplay", "audio-presenter:auto-gameplay-music") == SoundPlaybackStatus.Playable;
            }
        }

        private void ResetCueCooldowns()
        {
            catalog?.ResetCooldowns();
            runtimeFallbackCatalog?.ResetCooldowns();
        }

        private void StopTransientAudio()
        {
            StopOneShotSource(sfxSource);
            StopOneShotSource(debugSource);
        }

        private static void StopOneShotSource(AudioSource source)
        {
            if (source != null && !source.loop)
            {
                source.Stop();
                source.clip = null;
            }
        }

        // The gameplay music cue pairs with the Seoul base ambience loop; start it alongside.
        private void RequestPairedCueIfNeeded(SoundCatalog.Entry entry, string context)
        {
            if (entry != null && string.Equals(entry.CueId, "music.gameplay", StringComparison.Ordinal))
            {
                RequestCue("ambience.seoul.base", string.IsNullOrEmpty(context) ? "paired:music.gameplay" : context + ":paired-ambience");
                return;
            }

            if (entry != null && string.Equals(entry.CueId, AudioCueIds.CombatPlayerDeath, StringComparison.Ordinal))
            {
                // Latched only on an actual playback (this runs for Playable cues), so a death SFX that was
                // skipped for a missing clip still leaves game.defeat free to sound the defeat.
                playerDeathCueSounded = true;
                RequestCueWithVoiceDelay(AudioCueIds.PlayerVoiceDeath, string.IsNullOrEmpty(context) ? "paired:player-death" : context + ":paired-voice");
            }
        }

        private static bool IsLoopingBackgroundCue(string cueId)
        {
            return string.Equals(cueId, "music.lobby", StringComparison.Ordinal)
                || string.Equals(cueId, "music.gameplay", StringComparison.Ordinal)
                || string.Equals(cueId, "ambience.seoul.base", StringComparison.Ordinal)
                // Every boss-phase BGM (music.boss.<bossId>.p1/.p2/.p3) is a looping background, so a phase
                // change crossfades between them on the shared music source instead of hard-cutting.
                || (cueId != null && cueId.StartsWith(AudioCueIds.MusicBossPrefix, StringComparison.Ordinal));
        }

        private void PlayLooping(AudioSource source, SoundCatalog.Entry entry, float channelVolume)
        {
            var targetVolume = entry.Volume * channelVolume;
            source.loop = true;

            // Already playing this exact loop: keep it running and just settle the level. Cancelling first
            // matters — a fade-out could be in flight for the very clip being re-requested, and letting it
            // finish would stop the loop moments after this call said to keep it.
            if (source.clip == entry.Clip && source.isPlaying)
            {
                CancelFade(source);
                source.volume = targetVolume;
                return;
            }

            CancelFade(source);
            if (!CanFadeBackground)
            {
                source.clip = entry.Clip;
                source.volume = targetVolume;
                source.Play();
                return;
            }

            backgroundFades[source] = StartCoroutine(CrossfadeLoopRoutine(source, entry.Clip, targetVolume));
        }

        private void ApplySoundSettingsToSources()
        {
            if (uiSource != null)
                uiSource.volume = 1f;
            if (sfxSource != null)
                sfxSource.volume = 1f;
            if (debugSource != null)
                debugSource.volume = 1f;
            // A source mid-fade is deliberately below its settled level; writing the settings volume here
            // would snap it back and cancel the transition visually mid-way. The fade lands on the settled
            // level itself, so nothing is lost by skipping it.
            if (musicSource != null && musicSource.loop && !backgroundFades.ContainsKey(musicSource))
                musicSource.volume = SoundSettingsService.BgmVolume;
            if (ambienceSource != null && ambienceSource.loop && !backgroundFades.ContainsKey(ambienceSource))
                ambienceSource.volume = SoundSettingsService.BgmVolume;
        }

        private void StopLoopingBackground(bool immediate = false)
        {
            FadeOutAndStop(musicSource, immediate);
            FadeOutAndStop(ambienceSource, immediate);
        }

        private void FadeOutAndStop(AudioSource source, bool immediate)
        {
            if (source == null || !source.loop)
            {
                return;
            }

            CancelFade(source);
            if (immediate || !CanFadeBackground)
            {
                StopIfLooping(source);
                return;
            }

            backgroundFades[source] = StartCoroutine(FadeOutAndStopRoutine(source));
        }

        private System.Collections.IEnumerator FadeOutAndStopRoutine(AudioSource source)
        {
            yield return FadeVolume(source, source.volume, 0f);
            StopIfLooping(source);
            backgroundFades.Remove(source);
        }

        /// <summary>
        /// Fades the source out, swaps in the new loop, and fades it back up. With one AudioSource per bus
        /// the two halves are sequential rather than overlapping — but music and ambience are separate
        /// sources, so a music change genuinely crosses over the ambience bed rather than cutting it.
        /// </summary>
        private System.Collections.IEnumerator CrossfadeLoopRoutine(
            AudioSource source, AudioClip clip, float targetVolume)
        {
            if (source.isPlaying && source.clip != null)
            {
                yield return FadeVolume(source, source.volume, 0f);
            }

            source.clip = clip;
            source.volume = 0f;
            source.Play();
            yield return FadeVolume(source, 0f, targetVolume);
            backgroundFades.Remove(source);
        }

        // Unscaled so a victory/defeat sequence that slows or pauses time does not stretch the fade with it.
        private System.Collections.IEnumerator FadeVolume(AudioSource source, float from, float to)
        {
            var duration = Mathf.Max(0.0001f, backgroundFadeSeconds);
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                if (source == null)
                {
                    yield break;
                }

                source.volume = Mathf.Lerp(from, to, elapsed / duration);
                yield return null;
            }

            if (source != null)
            {
                source.volume = to;
            }
        }

        private void CancelFade(AudioSource source)
        {
            if (source == null || !backgroundFades.TryGetValue(source, out var routine))
            {
                return;
            }

            if (routine != null)
            {
                StopCoroutine(routine);
            }

            backgroundFades.Remove(source);
        }

        // Coroutines need a live, enabled behaviour; outside play mode (EditMode tests, edit-time probes)
        // there is no frame loop to drive one, so those paths keep the original immediate behavior.
        private bool CanFadeBackground => backgroundFadeSeconds > 0f && isActiveAndEnabled && Application.isPlaying;

        private static void StopIfLooping(AudioSource source)
        {
            if (source != null && source.loop)
            {
                source.Stop();
                source.loop = false;
                source.clip = null;
            }
        }

        private static bool IsTrapActivation(EffectResultEvent effect)
        {
            switch (effect.Kind)
            {
                case EffectKind.StatusEffectApplied:
                case EffectKind.DamageBlocked:
                    return true;
                case EffectKind.Damage:
                    return !effect.StatusKind.HasValue;
                default:
                    return false;
            }
        }

        private static bool IsPlayerTarget(string targetUnitId)
        {
            return string.Equals(targetUnitId, "player", System.StringComparison.Ordinal);
        }

        private AudioSource GetSource(SoundBus bus)
        {
            switch (bus)
            {
                case SoundBus.Ui:
                    return uiSource;
                case SoundBus.Ambience:
                    return ambienceSource;
                case SoundBus.Music:
                    return musicSource;
                case SoundBus.Debug:
                    return debugSource;
                default:
                    return sfxSource;
            }
        }

        private void MaybeLogMissing(SoundCatalog.Entry entry, SoundPlaybackStatus status, string context)
        {
            if (status == SoundPlaybackStatus.MissingCue)
            {
                return;
            }

            if (entry == null || entry.MissingClipBehavior == MissingClipBehavior.Silent)
            {
                return;
            }

            if (entry.MissingClipBehavior == MissingClipBehavior.EditorOnlyWarning && !Application.isEditor)
            {
                return;
            }

            Debug.LogWarning($"Missing audio clip for cue '{entry.CueId}' ({context}).", this);
        }

        // Mirrors EffectPresentationController's trap/knockback source-ref conventions so audio
        // and VFX stay aligned without coupling the two presenters.
        private static bool IsTrapSource(string sourceRef)
        {
            return !string.IsNullOrWhiteSpace(sourceRef)
                && sourceRef.StartsWith("trap.", System.StringComparison.Ordinal);
        }

        private static bool IsKnockbackImpact(string sourceRef)
        {
            return string.Equals(sourceRef, "knockback.impact", System.StringComparison.Ordinal);
        }

        private static bool IsFieldDamageEffect(EffectResultEvent effect)
        {
            return string.Equals(effect.SourceRef, CardEffectRefs.FieldDamage, System.StringComparison.Ordinal)
                && effect.AppliedAmount > 0;
        }

        private static bool IsFieldFlashbangEffect(EffectResultEvent effect)
        {
            return string.Equals(effect.SourceRef, CardEffectRefs.FieldImmobilizeFlashbang, System.StringComparison.Ordinal)
                && string.Equals(effect.TargetUnitId, "monster", System.StringComparison.Ordinal)
                && effect.Radius > 0;
        }

        private static bool IsFieldFlashbangMonsterHit(EffectResultEvent effect)
        {
            return string.Equals(effect.SourceRef, CardEffectRefs.FieldImmobilizeFlashbang, System.StringComparison.Ordinal)
                && effect.Radius == 0
                && !string.Equals(effect.TargetUnitId, "field", System.StringComparison.Ordinal)
                && !string.Equals(effect.TargetUnitId, "monster", System.StringComparison.Ordinal);
        }

        /// <summary>
        /// The field-area centre announce raised by a *tick* (ref `field.damage` / `field.heal` /
        /// `field.immobilize.flashbang`, target "field", amount 0): it draws the area so the tick is legible,
        /// while the per-unit effects that follow carry the real amounts and the real sounds. Silent so the
        /// area draw does not double the per-unit audio.
        ///
        /// Despite what this predicate used to be called, it has nothing to do with placing a field object —
        /// that announce uses `field.placement` and is filtered at the top of
        /// <see cref="MapEffectToCueIds"/>.
        /// </summary>
        private static bool IsSilentFieldTickAnnounceVfx(EffectResultEvent effect)
        {
            return CombatEffectSourceClassifier.IsFieldAreaAnnounce(effect);
        }

        private string ResolveMonsterDefinitionId(string unitId)
        {
            if (subscribedState == null || string.IsNullOrWhiteSpace(unitId))
            {
                return string.Empty;
            }

            foreach (var monster in subscribedState.Monsters)
            {
                if (string.Equals(monster.Id, unitId, StringComparison.Ordinal))
                {
                    return monster.DefinitionId;
                }
            }

            return string.Empty;
        }

        private static void AddMonsterHitCues(EffectResultEvent effect, List<string> cues, Func<string, string> resolveMonsterDefinitionId)
        {
            var definitionId = resolveMonsterDefinitionId != null ? resolveMonsterDefinitionId(effect.TargetUnitId) : string.Empty;
            cues.Add(AudioCueIds.CombatMonsterHit);
            AddMonsterVoiceCue(AudioCueIds.MonsterHit(definitionId), cues);
            if (effect.Lethal)
            {
                cues.Add(AudioCueIds.CombatEnemyDeath);
                AddMonsterVoiceCue(AudioCueIds.MonsterDeath(definitionId), cues);
            }
        }

        private static void AddPlayerHitCues(EffectResultEvent effect, List<string> cues)
        {
            cues.Add(AudioCueIds.CombatPlayerHit);
            cues.Add(IsDamageOverTimeHit(effect) ? AudioCueIds.PlayerVoiceDotHit : AudioCueIds.PlayerVoiceHit);
        }

        private static void AddMonsterAttackCues(
            EffectResultEvent effect,
            List<string> cues,
            Func<string, string> resolveMonsterDefinitionId,
            MonsterCatalogDefinition monsterCatalog)
        {
            if (!IsMonsterAttackSource(effect))
            {
                return;
            }

            cues.Add(AudioCueIds.MonsterIntentWarning);
            // 패턴 타격음(물리 층·2026-09 발주 A): monster_attack_patterns.csv soundImpactCueId → combat_sound_cues.csv.
            // 예고음 다음·울음(+0.2초 목소리 층) 앞에 선다. 카탈로그가 없거나(손 픽스처) 저작이 비면 종전 두 겹 그대로.
            if (TryResolvePatternImpactCue(effect, monsterCatalog, out var impactCueId))
            {
                cues.Add(impactCueId);
            }

            var definitionId = resolveMonsterDefinitionId != null ? resolveMonsterDefinitionId(effect.SourceUnitId) : string.Empty;
            if (string.IsNullOrWhiteSpace(definitionId))
            {
                definitionId = ResolveMonsterDefinitionIdFromPattern(effect.SourcePatternId, effect.SourceRef);
            }

            AddMonsterVoiceCue(AudioCueIds.MonsterAttack(definitionId), cues);
        }

        private static void AddMonsterVoiceCue(string voiceCueId, List<string> cues)
        {
            if (IsMonsterVoiceCue(voiceCueId))
            {
                cues.Add(voiceCueId);
            }
        }

        private static bool IsMonsterVoiceCue(string cueId)
        {
            return !string.IsNullOrWhiteSpace(cueId)
                && cueId.StartsWith("monster.M", StringComparison.Ordinal)
                && (cueId.EndsWith(".attack", StringComparison.Ordinal)
                    || cueId.EndsWith(".hit", StringComparison.Ordinal)
                    || cueId.EndsWith(".death", StringComparison.Ordinal));
        }

        private static bool IsPlayerVoiceCue(string cueId)
        {
            return !string.IsNullOrWhiteSpace(cueId)
                && cueId.StartsWith("player.voice.", StringComparison.Ordinal);
        }

        private static bool IsVoiceCue(string cueId)
        {
            return IsMonsterVoiceCue(cueId) || IsPlayerVoiceCue(cueId);
        }

        private static bool IsDamageOverTimeHit(EffectResultEvent effect)
        {
            return effect.StatusKind.HasValue
                || string.Equals(effect.SourceRef, CardEffectRefs.FieldDamage, StringComparison.Ordinal);
        }

        private static bool IsMonsterAttackSource(EffectResultEvent effect)
        {
            return string.Equals(effect.SourceActorKind, "monster", StringComparison.Ordinal)
                || (!string.IsNullOrWhiteSpace(effect.SourceRef)
                    && effect.SourceRef.StartsWith("monster.pattern.", StringComparison.Ordinal));
        }

        private static string ResolveMonsterDefinitionIdFromPattern(string sourcePatternId, string sourceRef)
        {
            var patternId = !string.IsNullOrWhiteSpace(sourcePatternId)
                ? sourcePatternId
                : ParseMonsterPatternId(sourceRef);

            switch (patternId)
            {
                case "A001":
                case "A002":
                    return "M001";
                case "A003":
                case "A004":
                case "A005":
                case "A006":
                case "A007":
                    return "M002";
                case "A015":
                case "A016":
                    return "M003";
                case "A011":
                case "A012":
                    return "M004";
                case "A013":
                case "A014":
                    return "M005";
                case "A008":
                case "A009":
                case "A010":
                    return "M006";
                default:
                    return string.Empty;
            }
        }

        private static string ParseMonsterPatternId(string sourceRef)
        {
            const string Prefix = "monster.pattern.";
            return !string.IsNullOrWhiteSpace(sourceRef) && sourceRef.StartsWith(Prefix, StringComparison.Ordinal)
                ? sourceRef.Substring(Prefix.Length)
                : string.Empty;
        }

        private static string ResolvePatternId(EffectResultEvent effect)
        {
            // 자기부여 경로(ApplyMonsterSelfStatusEffects)는 SourcePatternId를 안 싣고 sourceRef만 monster.pattern.{id}다.
            return !string.IsNullOrWhiteSpace(effect.SourcePatternId)
                ? effect.SourcePatternId
                : ParseMonsterPatternId(effect.SourceRef);
        }

        private static bool TryResolvePatternImpactCue(EffectResultEvent effect, MonsterCatalogDefinition monsterCatalog, out string cueId)
        {
            cueId = string.Empty;
            if (monsterCatalog == null
                || !monsterCatalog.TryGetPatternPresentation(ResolvePatternId(effect), out var presentation)
                || string.IsNullOrWhiteSpace(presentation.SoundImpactCueId))
            {
                return false;
            }

            cueId = presentation.SoundImpactCueId;
            return true;
        }

        /// <summary>
        /// 상태 부여 이벤트가 <b>피해 0 패턴</b>(자기부여·비타격형)에서 왔으면 그 패턴의 타격음. 지대 설치(field)와
        /// 피해가 있는 패턴은 제외 — 전자는 타격이 아니고 후자는 Damage 이벤트가 이미 울렸다.
        /// </summary>
        private static bool TryResolveNonDamagingPatternImpactCue(EffectResultEvent effect, MonsterCatalogDefinition monsterCatalog, out string cueId)
        {
            cueId = string.Empty;
            if (monsterCatalog == null
                || !IsMonsterAttackSource(effect)
                || string.Equals(effect.TargetActorKind, "field", StringComparison.Ordinal))
            {
                return false;
            }

            var patternId = ResolvePatternId(effect);
            if (!monsterCatalog.TryGetAttackPattern(patternId, out var pattern) || pattern.Damage > 0)
            {
                return false;
            }

            return TryResolvePatternImpactCue(effect, monsterCatalog, out cueId);
        }

        private static bool IsObjectiveRevealPresentable(CombatState state)
        {
            return state != null
                && state.HasObjective
                && !state.ObjectiveCompleted
                && state.ObjectiveStatusText.IndexOf("investigate the revealed", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
