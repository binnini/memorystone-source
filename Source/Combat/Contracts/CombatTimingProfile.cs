using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// Reusable art-direction profile for combat presentation timing (the "beat sheet" of a turn).
    /// Centralizes the per-sequence wait values that used to live only as scattered serialized
    /// fields on <see cref="MapCombatController"/>, so designers can tune feel from one asset.
    ///
    /// When no profile is assigned the controller keeps using its own inspector values, so this is a
    /// purely additive, non-breaking override layer.
    /// </summary>
    [CreateAssetMenu(menuName = "Seoul Playup/Combat/Timing Profile", fileName = "CombatTimingProfile")]
    public sealed class CombatTimingProfile : ScriptableObject
    {
        [Header("Player Movement")]
        [Tooltip("Base duration of a player move animation. When 'Scale Move By Distance' is on this is the time for a single tile step.")]
        [SerializeField] private float playerMoveSeconds = 0.35f;
        [Tooltip("If enabled, multi-tile moves take longer: total = playerMoveSeconds + (extraTiles * secondsPerExtraTile), clamped to maxPlayerMoveSeconds.")]
        [SerializeField] private bool scaleMoveByDistance;
        [Tooltip("Extra seconds added per tile beyond the first when 'Scale Move By Distance' is on.")]
        [SerializeField] private float secondsPerExtraTile = 0.12f;
        [Tooltip("Upper bound for a single scaled player move, so long paths still feel snappy.")]
        [SerializeField] private float maxPlayerMoveSeconds = 1.2f;

        [Header("Enemy Movement")]
        [Tooltip("Delay before a monster begins following the player's move during the move sequence.")]
        [SerializeField] private float enemyMoveStartDelay = 0.08f;
        [Tooltip("Duration of a single monster move animation.")]
        [SerializeField] private float enemyMoveSeconds = 0.3f;

        [Header("Impact Sync")]
        [Tooltip("When on, player-attack damage SFX/VFX/floating number/camera-shake are deferred to the animation impact frame instead of firing the instant the rules resolve. Default off preserves legacy timing.")]
        [SerializeField] private bool alignImpactToAnimation;
        [Tooltip("When on, the attack impact beat waits for the attacker's animation clip to reach its strike frame (clip-driven) instead of a fixed delay. Default off preserves fixed-delay timing.")]
        [SerializeField] private bool alignImpactToAnimationClip;

        [Header("Attack Beats")]
        [Tooltip("Time from attack trigger to the impact moment (wind-up). VFX/SFX/shake should land on impact, not here.")]
        [SerializeField] private float attackWindupDelay = 0.12f;
        [Tooltip("Time held on the impact frame before the hit reaction resolves.")]
        [SerializeField] private float attackImpactDelay = 0.2f;
        [Tooltip("Time held after a lethal hit so the death animation can read before the sequence completes.")]
        [SerializeField] private float deathDelay = 0.25f;

        [Header("Impact Channel Offsets")]
        [Tooltip("Per-channel timing offset (seconds) relative to the attack impact beat, so each channel can " +
                 "lead (negative) or trail (positive) the others. All default 0 = every channel fires together " +
                 "on impact (legacy behavior). The earliest (most negative) channel defines the impact instant; " +
                 "the rest trail it. Only applied during impact-synced attack flushes.\n\n" +
                 "This field: the impact VFX + floating damage number.\n\n" +
                 "There is deliberately no SFX channel here: impact SFX timing is authored on the VFX cue " +
                 "(EffectVfxCatalog playbackDelaySeconds) and the sound follows it, so a hit's picture and " +
                 "its sound cannot drift apart by being tuned in two places.")]
        [SerializeField] private float visualImpactOffset;
        [Tooltip("Offset for the impact camera shake.")]
        [SerializeField] private float shakeImpactOffset;
        [Tooltip("Offset for the impact hit-stop pause.")]
        [SerializeField] private float hitStopImpactOffset;

        [Header("Hit Stop")]
        [Tooltip("When on, attack impact beats briefly pause the presentation sequence using unscaled time. Default off preserves legacy timing.")]
        [SerializeField] private bool enableHitStop;
        [Tooltip("Realtime pause length after a non-lethal player attack impact.")]
        [SerializeField] private float playerAttackHitStopSeconds = 0.06f;
        [Tooltip("Realtime pause length after a non-lethal monster attack impact.")]
        [SerializeField] private float monsterAttackHitStopSeconds = 0.05f;
        [Tooltip("Realtime pause length for lethal impacts. If zero, the matching player/monster hit-stop length is used.")]
        [SerializeField] private float lethalHitStopSeconds = 0.1f;
        [Tooltip("Animator speed applied to affected actors during hit stop. 0 fully freezes their animation without changing global Time.timeScale.")]
        [SerializeField] private float hitStopAnimationSpeed;
        [Tooltip("Global time scale applied during hit stop so particles/camera/game feel also pause. 1 keeps global time unchanged; 0 fully freezes scaled-time systems.")]
        [SerializeField] private float hitStopTimeScale = 1f;

        [Header("Card Draw/Discard")]
        [Tooltip("Flight duration of a single card from the draw pile to its hand slot.")]
        [SerializeField] private float drawFlightSeconds = 0.45f;
        [Tooltip("Ghost scale at draw take-off; grows to full size by landing.")]
        [SerializeField] private float drawStartScale = 0.3f;
        [Tooltip("Upward bow of the draw flight path, in overlay units.")]
        [SerializeField] private float drawArcHeight = 90f;
        [Tooltip("Per-card delay between cards in a staggered draw batch.")]
        [SerializeField] private float drawStaggerSeconds = 0.12f;
        [Tooltip("Scale overshoot played at the draw landing point for a little pop.")]
        [SerializeField] private float drawLandingPopScale = 1.08f;
        [Tooltip("Duration of the draw landing pop.")]
        [SerializeField] private float drawLandingPopSeconds = 0.08f;
        [Tooltip("Flight duration of a single card from its hand slot into the discard pile.")]
        [SerializeField] private float discardFlightSeconds = 0.16f;
        [Tooltip("Per-card delay between cards in a staggered discard batch.")]
        [SerializeField] private float discardStaggerSeconds = 0.045f;
        [Tooltip("Scale the front ghost shrinks to as it is sucked into the discard pile.")]
        [SerializeField] private float discardEndScale = 0.35f;
        [Tooltip("Upward bow of the discard flight path, in overlay units.")]
        [SerializeField] private float discardArcHeight = 60f;
        [Tooltip("Duration of the reshuffle cut (discard pile sweeps back into the draw pile). Phase 5.")]
        [SerializeField] private float reshuffleSeconds = 0.25f;

        [Header("Attack Strike Sync")]
        [Tooltip("Fraction (0..1) of the attack animation at which the strike/impact lands, used to schedule the impact beat relative to the clip length.")]
        [SerializeField] private float attackStrikeFraction = 0.85f;
        [Tooltip("Upper bound (seconds) on how long to wait for an attack's strike frame, so a long/looping clip can't stall the sequence.")]
        [SerializeField] private float attackStrikeMaxWaitSeconds = 1.5f;

        [Header("Global Speed")]
        [Tooltip("Master playback-speed multiplier for the whole combat presentation. 1 = authored speed, 2 = twice as fast (half the waits), 0.5 = half speed. The presentation scheduler divides every scheduled wait by this so designers/players can tune overall pacing from one knob.")]
        [SerializeField] private float globalSpeedMultiplier = 1f;

        [Header("Stagger / Pacing (tunable)")]
        [Tooltip("Delay inserted between consecutive impact effects so each hit's SFX/VFX/damage number reads one at a time instead of stacking on a single frame (which made only one sound/VFX register).")]
        [SerializeField] private float effectStaggerSeconds = 0.2f;
        [Tooltip("Delay between consecutive strikes of a single multi-hit attack (e.g. Double Hit). Larger than the generic effect stagger so each strike's number/SFX reads as a distinct hit instead of a blur.")]
        [SerializeField] private float multiHitStrikeIntervalSeconds = 0.25f;
        [Tooltip("Delay between consecutive targets of an area/blast attack or burst (휘둘러치기/지뢰찾기/폭탄 투하), so each target's flinch + damage number + hit SFX reads one at a time instead of all on a single frame. Must stay above the per-cue SFX cooldown (~0.05s) so every target's hit sound rings.")]
        [SerializeField] private float aoeTargetIntervalSeconds = 0.14f;
        [Tooltip("Delay between consecutive status-effect applies (속박/기절/중독 등) when several land at once — e.g. 섬광 장판(F03)이 풋프린트 안 여러 몬스터를 동시에 속박할 때 — so each apply's VFX/SFX/icon/number reads one at a time instead of all on a single frame. Independent of the AoE damage-target interval.")]
        [SerializeField] private float statusEffectStaggerSeconds = 0.5f;
        [Tooltip("Short delay between floating-text-bearing effects in one impact, e.g. monster status text then damage number.")]
        [SerializeField] private float floatingTextQueueStaggerSeconds = 0.5f;
        [Tooltip("Breathing pause between consecutive monsters' actions during the end-of-turn monster phase, so three monsters acting in a row read as distinct beats instead of blurring together.")]
        [SerializeField] private float monsterActionGapSeconds = 0.5f;
        [Header("Action Camera Focus (docs/monster-action-camera-focus-plan.md)")]
        [Tooltip("How long an off-screen action is framed BEFORE its beats play — the arrival lead, sized to " +
            "the focus damping's settle. Deliberately constant: the length of the event belongs on the other " +
            "side of it (focusDwell*), because a longer lead only buys dead air before anything happens.")]
        [SerializeField] private float focusPanSeconds = 0.5f;

        [Tooltip("Actions within this hex distance of the coordinate the camera is ACTUALLY framing share one " +
            "camera move. Applied at replay time, not during assembly: a spotlight the geometry gate declines " +
            "never moved the camera, so letting it set an assembly-time anchor silently swallowed the " +
            "off-screen events behind it (measured 2026-07-31, plan §10.6). Justified geometrically (the " +
            "screen spans ~5.45 hexes to each side), not by the P0 measurement — see plan §6.1.")]
        [SerializeField] private int coalesceRadiusHexes = 5;

        [Tooltip("Ceiling for the post-event dwell, so a pathological cluster cannot stall the enemy turn. " +
            "⚠ It is load-bearing, not just a safety valve: the covered length is a SUM of the covered cues, " +
            "while a burst plays them overlapping, so a field hitting three monsters reports 6.2s for an event " +
            "that resolves on screen in about 3 (measured 2026-07-31). Until the annotation models overlap, " +
            "this ceiling is what keeps the hold near the real length — do not raise it expecting 'more of the " +
            "same event', because past ~3s it is buying time after the event has already finished.")]
        [SerializeField] private float focusDwellMaxSeconds = 3f;

        [Tooltip("How long the camera keeps holding a framing after the last floating text of the framed event " +
            "appears. The text appearing is where reading STARTS, so cutting on that frame means the number is " +
            "never read. Only the dwell that is still outstanding is waited for, so an event whose beats " +
            "already outlasted it costs nothing.")]
        [SerializeField] private float focusDwellTextReadSeconds = 0.5f;

        [Tooltip("How long the framing leans toward a melee attacker. Sized to the wind-up + impact so the " +
            "lean is present while the blow lands and gone before the next monster acts.")]
        [SerializeField] private float attackEmphasisSeconds = 0.45f;

        [Tooltip("Camera moves allowed per monster phase. Overflow is dropped in timeline order, and field " +
            "ticks are appended last — so a budget that binds silently drops exactly them. At 2 the lab " +
            "measured 3 spotlights dropped in one phase and only 2 of 4 fields framed; 4 covers that " +
            "scenario outright at a measured cost of +0.42s (plan §10.6, §10.10).")]
        [SerializeField] private int maxFocusPerPhase = 4;

        [Tooltip("Ceiling on the wait for the camera to settle back on the player after a phase's framing is " +
            "released, before the turn-start beats play (plan §10.13). The return runs on the global damping " +
            "(~0.35 → ~1.05s to settle), so this only has to outlast that. A timeout, not a duration: the wait " +
            "ends the moment the rig has arrived, and a rig that can never report its position skips it " +
            "entirely — nothing here may be able to stall a turn.")]
        [SerializeField] private float focusReturnMaxSeconds = 1.2f;

        [Tooltip("Longer pause after the last visible monster attack before the next overall turn begins.")]
        [SerializeField] private float postMonsterAttackPauseSeconds = 2f;

        public float PlayerMoveSeconds => Mathf.Max(0f, playerMoveSeconds);
        public bool ScaleMoveByDistance => scaleMoveByDistance;
        public float SecondsPerExtraTile => Mathf.Max(0f, secondsPerExtraTile);
        public float MaxPlayerMoveSeconds => Mathf.Max(PlayerMoveSeconds, maxPlayerMoveSeconds);
        public float EnemyMoveStartDelay => Mathf.Max(0f, enemyMoveStartDelay);
        public float EnemyMoveSeconds => Mathf.Max(0f, enemyMoveSeconds);
        public bool AlignImpactToAnimation => alignImpactToAnimation;
        public bool AlignImpactToAnimationClip => alignImpactToAnimationClip;
        public float AttackWindupDelay => Mathf.Max(0f, attackWindupDelay);
        public float AttackImpactDelay => Mathf.Max(0f, attackImpactDelay);
        public float DeathDelay => Mathf.Max(0f, deathDelay);
        public bool EnableHitStop => enableHitStop;
        public float PlayerAttackHitStopSeconds => enableHitStop ? Mathf.Max(0f, playerAttackHitStopSeconds) : 0f;
        public float MonsterAttackHitStopSeconds => enableHitStop ? Mathf.Max(0f, monsterAttackHitStopSeconds) : 0f;
        public float LethalHitStopSeconds => enableHitStop ? Mathf.Max(0f, lethalHitStopSeconds) : 0f;
        public float HitStopAnimationSpeed => Mathf.Max(0f, hitStopAnimationSpeed);
        public float HitStopTimeScale => Mathf.Clamp01(hitStopTimeScale);

        // Signed per-channel impact offsets (seconds). The most negative one anchors the impact instant;
        // the rest trail it. ResolveImpactChannelDelay converts an offset into a >=0 wait measured from the
        // (shifted) impact flush, so every channel lands at impact + its own offset.
        public float VisualImpactOffset => visualImpactOffset;
        public float ShakeImpactOffset => shakeImpactOffset;
        public float HitStopImpactOffset => hitStopImpactOffset;
        public float MinImpactChannelOffset =>
            Mathf.Min(visualImpactOffset, Mathf.Min(shakeImpactOffset, hitStopImpactOffset));
        public bool HasImpactChannelOffsets => !Mathf.Approximately(MinImpactChannelOffset, 0f)
            || !Mathf.Approximately(Mathf.Max(visualImpactOffset, Mathf.Max(shakeImpactOffset, hitStopImpactOffset)), 0f);

        /// <summary>Delay (>=0) a channel waits after the impact flush so it lands at impact + its own offset.</summary>
        public float ResolveImpactChannelDelay(float channelOffset) => Mathf.Max(0f, channelOffset - MinImpactChannelOffset);
        public float VisualImpactDelay => ResolveImpactChannelDelay(visualImpactOffset);
        public float ShakeImpactDelay => ResolveImpactChannelDelay(shakeImpactOffset);
        public float HitStopImpactDelay => ResolveImpactChannelDelay(hitStopImpactOffset);

        public float DrawFlightSeconds => Mathf.Max(0.01f, drawFlightSeconds);
        public float DrawStartScale => Mathf.Max(0f, drawStartScale);
        public float DrawArcHeight => drawArcHeight;
        public float DrawStaggerSeconds => Mathf.Max(0f, drawStaggerSeconds);
        public float DrawLandingPopScale => Mathf.Max(1f, drawLandingPopScale);
        public float DrawLandingPopSeconds => Mathf.Max(0f, drawLandingPopSeconds);
        public float DiscardFlightSeconds => Mathf.Max(0.01f, discardFlightSeconds);
        public float DiscardStaggerSeconds => Mathf.Max(0f, discardStaggerSeconds);
        public float DiscardEndScale => Mathf.Clamp01(discardEndScale);
        public float DiscardArcHeight => discardArcHeight;
        public float ReshuffleSeconds => Mathf.Max(0f, reshuffleSeconds);

        public float AttackStrikeFraction => Mathf.Clamp01(attackStrikeFraction);
        public float AttackStrikeMaxWaitSeconds => ScaleDuration(Mathf.Max(0f, attackStrikeMaxWaitSeconds));

        // Master speed knob: the scheduler divides every wait by this. Clamped to a sane range so a stray 0
        // can't freeze the presentation and a huge value can't make it instant.
        public float GlobalSpeedMultiplier => Mathf.Clamp(globalSpeedMultiplier, 0.05f, 8f);

        // Stagger/pacing getters return the speed-scaled value; Raw* expose the authored value (used by the
        // dev tuning sliders so the slider shows the stored number, not the scaled one).
        public float EffectStaggerSeconds => ScaleDuration(Mathf.Max(0f, effectStaggerSeconds));
        public float RawEffectStaggerSeconds => Mathf.Max(0f, effectStaggerSeconds);
        public float MultiHitStrikeIntervalSeconds => ScaleDuration(Mathf.Max(0f, multiHitStrikeIntervalSeconds));
        public float RawMultiHitStrikeIntervalSeconds => Mathf.Max(0f, multiHitStrikeIntervalSeconds);
        public float AoeTargetIntervalSeconds => ScaleDuration(Mathf.Max(0f, aoeTargetIntervalSeconds));
        public float RawAoeTargetIntervalSeconds => Mathf.Max(0f, aoeTargetIntervalSeconds);
        public float StatusEffectStaggerSeconds => ScaleDuration(Mathf.Max(0f, statusEffectStaggerSeconds));
        public float RawStatusEffectStaggerSeconds => Mathf.Max(0f, statusEffectStaggerSeconds);
        public float FloatingTextQueueStaggerSeconds => ScaleDuration(Mathf.Max(0f, floatingTextQueueStaggerSeconds));
        public float RawFloatingTextQueueStaggerSeconds => Mathf.Max(0f, floatingTextQueueStaggerSeconds);
        public float MonsterActionGapSeconds => ScaleDuration(Mathf.Max(0f, monsterActionGapSeconds));
        public float RawMonsterActionGapSeconds => Mathf.Max(0f, monsterActionGapSeconds);
        public float FocusPanSeconds => ScaleDuration(Mathf.Max(0f, focusPanSeconds));
        public int CoalesceRadiusHexes => Mathf.Max(0, coalesceRadiusHexes);
        public int MaxFocusPerPhase => Mathf.Max(0, maxFocusPerPhase);
        public float AttackEmphasisSeconds => ScaleDuration(Mathf.Max(0f, attackEmphasisSeconds));

        public float FocusDwellTextReadSeconds => ScaleDuration(Mathf.Max(0f, focusDwellTextReadSeconds));
        public float FocusDwellMaxSeconds => ScaleDuration(Mathf.Max(0f, focusDwellMaxSeconds));
        public float FocusReturnMaxSeconds => ScaleDuration(Mathf.Max(0f, focusReturnMaxSeconds));

        /// <summary>
        /// How long a framing must be held from the moment the camera arrives, before the next spotlight may
        /// take the camera away.
        ///
        /// <paramref name="coveredSeconds"/> is the measured length of the cues the spotlight covers
        /// (docs/presentation-duration-data-plan.md P4); it is 0 for a spotlight covering only beats with no
        /// duration data (reaction-only clusters, or before the bake has run).
        ///
        /// <paramref name="coveredLeadInSeconds"/> is the longest authored playback delay among those cues —
        /// the dead time before anything appears at all.
        ///
        /// This is a floor, not an added wait: the sink holds only the part of it that the covered beats did
        /// not already consume. That is the whole reason the length lives here and no longer inflates
        /// <see cref="FocusPanSeconds"/> — spending it *before* the beats bought dead air on an empty frame
        /// and still let the camera leave mid-explosion (plan §10.6).
        ///
        /// ⚠ The lead-in is applied OUTSIDE the <see cref="FocusDwellMaxSeconds"/> clamp, and deliberately so.
        /// The clamp exists to stop a long event parking the camera; it must never cut a hold shorter than the
        /// point where the event becomes visible, or the camera leaves before there is anything to see. The
        /// field-damage cue's 1.1s lead-in did exactly that: measured against dispatch, every framing expired
        /// while its explosion was still pending (plan §10.11).
        /// </summary>
        public float ResolveFocusDwellSeconds(float coveredSeconds, float coveredLeadInSeconds = 0f)
        {
            var raw = coveredSeconds > 0f && !float.IsNaN(coveredSeconds) && !float.IsInfinity(coveredSeconds)
                ? coveredSeconds
                : 0f;
            var content = Mathf.Min(raw, Mathf.Max(0f, focusDwellMaxSeconds));

            var leadIn = coveredLeadInSeconds > 0f
                && !float.IsNaN(coveredLeadInSeconds)
                && !float.IsInfinity(coveredLeadInSeconds)
                    ? coveredLeadInSeconds
                    : 0f;

            // The number appearing is where reading starts, so the visible-at moment still owes the read time.
            var visible = leadIn > 0f ? leadIn + Mathf.Max(0f, focusDwellTextReadSeconds) : 0f;

            return ScaleDuration(Mathf.Max(content, visible));
        }
        public float PostMonsterAttackPauseSeconds => ScaleDuration(Mathf.Max(0f, postMonsterAttackPauseSeconds));
        public float RawPostMonsterAttackPauseSeconds => Mathf.Max(0f, postMonsterAttackPauseSeconds);

        /// <summary>Divide an authored wait by the global speed multiplier so one knob scales all pacing.</summary>
        public float ScaleDuration(float seconds) => Mathf.Max(0f, seconds) / GlobalSpeedMultiplier;

        // --- Dev/tuning write surface ---
        // Used only by the editor/development-build CombatDebugControlPanel "Timing" tab so designers can
        // drag values live during play. These compile in release but are inert (no caller). The raw* members
        // expose the ungated backing fields so a slider shows the stored value even when EnableHitStop is off.
        public bool TunableAlignImpactToAnimation { get => alignImpactToAnimation; set => alignImpactToAnimation = value; }
        public bool TunableAlignImpactToAnimationClip { get => alignImpactToAnimationClip; set => alignImpactToAnimationClip = value; }
        public bool TunableEnableHitStop { get => enableHitStop; set => enableHitStop = value; }
        public float TunablePlayerMoveSeconds { get => playerMoveSeconds; set => playerMoveSeconds = Mathf.Max(0f, value); }
        public float TunableEnemyMoveStartDelay { get => enemyMoveStartDelay; set => enemyMoveStartDelay = Mathf.Max(0f, value); }
        public float TunableEnemyMoveSeconds { get => enemyMoveSeconds; set => enemyMoveSeconds = Mathf.Max(0f, value); }
        public float TunableAttackWindupDelay { get => attackWindupDelay; set => attackWindupDelay = Mathf.Max(0f, value); }
        public float TunableAttackImpactDelay { get => attackImpactDelay; set => attackImpactDelay = Mathf.Max(0f, value); }
        public float TunableDeathDelay { get => deathDelay; set => deathDelay = Mathf.Max(0f, value); }
        public float TunablePlayerAttackHitStopSeconds { get => playerAttackHitStopSeconds; set => playerAttackHitStopSeconds = Mathf.Max(0f, value); }
        public float TunableMonsterAttackHitStopSeconds { get => monsterAttackHitStopSeconds; set => monsterAttackHitStopSeconds = Mathf.Max(0f, value); }
        public float TunableLethalHitStopSeconds { get => lethalHitStopSeconds; set => lethalHitStopSeconds = Mathf.Max(0f, value); }
        public float TunableHitStopAnimationSpeed { get => hitStopAnimationSpeed; set => hitStopAnimationSpeed = Mathf.Max(0f, value); }
        public float TunableHitStopTimeScale { get => hitStopTimeScale; set => hitStopTimeScale = Mathf.Clamp01(value); }
        // Signed: a channel may lead (negative) or trail (positive) the impact beat.
        public float TunableVisualImpactOffset { get => visualImpactOffset; set => visualImpactOffset = value; }
        public float TunableShakeImpactOffset { get => shakeImpactOffset; set => shakeImpactOffset = value; }
        public float TunableHitStopImpactOffset { get => hitStopImpactOffset; set => hitStopImpactOffset = value; }
        public float TunableGlobalSpeedMultiplier { get => globalSpeedMultiplier; set => globalSpeedMultiplier = Mathf.Clamp(value, 0.05f, 8f); }
        public float TunableEffectStaggerSeconds { get => effectStaggerSeconds; set => effectStaggerSeconds = Mathf.Max(0f, value); }
        public float TunableMultiHitStrikeIntervalSeconds { get => multiHitStrikeIntervalSeconds; set => multiHitStrikeIntervalSeconds = Mathf.Max(0f, value); }
        public float TunableAoeTargetIntervalSeconds { get => aoeTargetIntervalSeconds; set => aoeTargetIntervalSeconds = Mathf.Max(0f, value); }
        public float TunableStatusEffectStaggerSeconds { get => statusEffectStaggerSeconds; set => statusEffectStaggerSeconds = Mathf.Max(0f, value); }
        public float TunableFloatingTextQueueStaggerSeconds { get => floatingTextQueueStaggerSeconds; set => floatingTextQueueStaggerSeconds = Mathf.Max(0f, value); }
        public float TunableMonsterActionGapSeconds { get => monsterActionGapSeconds; set => monsterActionGapSeconds = Mathf.Max(0f, value); }
        public float TunableFocusPanSeconds { get => focusPanSeconds; set => focusPanSeconds = Mathf.Max(0f, value); }
        public int TunableCoalesceRadiusHexes { get => coalesceRadiusHexes; set => coalesceRadiusHexes = Mathf.Max(0, value); }
        public int TunableMaxFocusPerPhase { get => maxFocusPerPhase; set => maxFocusPerPhase = Mathf.Max(0, value); }
        public float TunableAttackEmphasisSeconds { get => attackEmphasisSeconds; set => attackEmphasisSeconds = Mathf.Max(0f, value); }
        public float TunableFocusDwellMaxSeconds { get => focusDwellMaxSeconds; set => focusDwellMaxSeconds = Mathf.Max(0f, value); }
        public float TunableFocusDwellTextReadSeconds { get => focusDwellTextReadSeconds; set => focusDwellTextReadSeconds = Mathf.Max(0f, value); }
        public float TunableFocusReturnMaxSeconds { get => focusReturnMaxSeconds; set => focusReturnMaxSeconds = Mathf.Max(0f, value); }
        public float TunablePostMonsterAttackPauseSeconds { get => postMonsterAttackPauseSeconds; set => postMonsterAttackPauseSeconds = Mathf.Max(0f, value); }
        public float TunableAttackStrikeFraction { get => attackStrikeFraction; set => attackStrikeFraction = Mathf.Clamp01(value); }
        public float TunableAttackStrikeMaxWaitSeconds { get => attackStrikeMaxWaitSeconds; set => attackStrikeMaxWaitSeconds = Mathf.Max(0f, value); }

        public float ResolvePlayerAttackHitStopSeconds(bool lethal)
        {
            return ResolveHitStopSeconds(PlayerAttackHitStopSeconds, lethal);
        }

        public float ResolveMonsterAttackHitStopSeconds(bool lethal)
        {
            return ResolveHitStopSeconds(MonsterAttackHitStopSeconds, lethal);
        }

        /// <summary>
        /// Resolve the player move duration for a path that spans <paramref name="tileDistance"/> tiles.
        /// Returns the flat base duration unless distance scaling is enabled.
        /// </summary>
        public float ResolvePlayerMoveSeconds(int tileDistance)
        {
            if (!scaleMoveByDistance)
            {
                return PlayerMoveSeconds;
            }

            var extraTiles = Mathf.Max(0, tileDistance - 1);
            var scaled = PlayerMoveSeconds + extraTiles * SecondsPerExtraTile;
            return Mathf.Min(scaled, MaxPlayerMoveSeconds);
        }

        /// <summary>
        /// Resolve a monster move duration using the same distance-scaling policy as player movement.
        /// The monster's one-tile baseline remains <see cref="EnemyMoveSeconds"/>.
        /// </summary>
        public float ResolveEnemyMoveSeconds(int tileDistance)
        {
            if (!scaleMoveByDistance)
            {
                return EnemyMoveSeconds;
            }

            var extraTiles = Mathf.Max(0, tileDistance - 1);
            var scaled = EnemyMoveSeconds + extraTiles * SecondsPerExtraTile;
            return Mathf.Min(scaled, Mathf.Max(EnemyMoveSeconds, maxPlayerMoveSeconds));
        }

        private void OnValidate()
        {
            playerMoveSeconds = Mathf.Max(0f, playerMoveSeconds);
            secondsPerExtraTile = Mathf.Max(0f, secondsPerExtraTile);
            maxPlayerMoveSeconds = Mathf.Max(playerMoveSeconds, maxPlayerMoveSeconds);
            enemyMoveStartDelay = Mathf.Max(0f, enemyMoveStartDelay);
            enemyMoveSeconds = Mathf.Max(0f, enemyMoveSeconds);
            attackWindupDelay = Mathf.Max(0f, attackWindupDelay);
            attackImpactDelay = Mathf.Max(0f, attackImpactDelay);
            deathDelay = Mathf.Max(0f, deathDelay);
            playerAttackHitStopSeconds = Mathf.Max(0f, playerAttackHitStopSeconds);
            monsterAttackHitStopSeconds = Mathf.Max(0f, monsterAttackHitStopSeconds);
            lethalHitStopSeconds = Mathf.Max(0f, lethalHitStopSeconds);
            hitStopAnimationSpeed = Mathf.Max(0f, hitStopAnimationSpeed);
            hitStopTimeScale = Mathf.Clamp01(hitStopTimeScale);
            drawFlightSeconds = Mathf.Max(0.01f, drawFlightSeconds);
            drawStartScale = Mathf.Max(0f, drawStartScale);
            drawStaggerSeconds = Mathf.Max(0f, drawStaggerSeconds);
            drawLandingPopScale = Mathf.Max(1f, drawLandingPopScale);
            drawLandingPopSeconds = Mathf.Max(0f, drawLandingPopSeconds);
            discardFlightSeconds = Mathf.Max(0.01f, discardFlightSeconds);
            discardStaggerSeconds = Mathf.Max(0f, discardStaggerSeconds);
            discardEndScale = Mathf.Clamp01(discardEndScale);
            reshuffleSeconds = Mathf.Max(0f, reshuffleSeconds);
            globalSpeedMultiplier = Mathf.Clamp(globalSpeedMultiplier, 0.05f, 8f);
            effectStaggerSeconds = Mathf.Max(0f, effectStaggerSeconds);
            multiHitStrikeIntervalSeconds = Mathf.Max(0f, multiHitStrikeIntervalSeconds);
            aoeTargetIntervalSeconds = Mathf.Max(0f, aoeTargetIntervalSeconds);
            statusEffectStaggerSeconds = Mathf.Max(0f, statusEffectStaggerSeconds);
            floatingTextQueueStaggerSeconds = Mathf.Max(0f, floatingTextQueueStaggerSeconds);
            monsterActionGapSeconds = Mathf.Max(0f, monsterActionGapSeconds);
            postMonsterAttackPauseSeconds = Mathf.Max(0f, postMonsterAttackPauseSeconds);
            attackStrikeFraction = Mathf.Clamp01(attackStrikeFraction);
            attackStrikeMaxWaitSeconds = Mathf.Max(0f, attackStrikeMaxWaitSeconds);
        }

        private float ResolveHitStopSeconds(float baseSeconds, bool lethal)
        {
            if (!enableHitStop)
            {
                return 0f;
            }

            return lethal && LethalHitStopSeconds > 0f ? LethalHitStopSeconds : baseSeconds;
        }
    }
}
