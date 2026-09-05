using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime.Timeline
{
    /// <summary>
    /// Turns the rules layer's structured resolution outputs (move paths, monster action records, buffered
    /// effects) into an ordered <see cref="CombatTimeline"/> the presentation scheduler can replay beat by
    /// beat. This is pure, deterministic, UnityEngine-free logic ??it encodes *what plays in what order*
    /// (per-tile movement, impact grouping, hit/death/knockback reactions) and is fully unit-testable,
    /// independent of the Unity scheduler that turns each beat into seconds of animation.
    ///
    /// Beat order around an impact mirrors the legacy sequences: the reaction flash (hit/knockback) is
    /// triggered *before* the damage effects so the hit reads on the same frame as it did before, then the
    /// effects fire one at a time (staggered by the scheduler), then knockback movement and hit-stop. A kill
    /// presents its death animation + post-kill hold after the effects.
    /// </summary>
    public static class CombatTimelineAssembler
    {
        /// <summary>
        /// Emits the <see cref="CombatTimelineEventKind.ActorSpotlight"/> beats — one candidate framing per
        /// on-map action (docs/monster-action-camera-focus-plan.md §4).
        ///
        /// ⚠ It suppresses only an *exact repeat* of the previous framing coordinate — it no longer coalesces
        /// by <c>coalesceRadiusHexes</c>. The radius version assumed the camera was already pointed at the
        /// previously emitted spotlight, which only holds when that spotlight actually moved the camera; the
        /// sink declines any spotlight whose action is already on screen, so an on-screen first actor planted
        /// an anchor the camera never visited and every off-screen action behind it was silently swallowed.
        /// Measured 2026-07-31: the entire monster movement phase produced zero cuts for exactly this reason
        /// (plan §10.6). Radius coalescing now lives in the sink, against the coordinate the camera is really
        /// framing, next to the frustum test it approximates (§3.1).
        ///
        /// The exact-coordinate case stays here because it is not a camera decision at all: one field ticking
        /// three monsters raises three effects on the same tile, and splitting them across three beats would
        /// divide the event's covered length between them — leaving the dwell sized to a third of the event it
        /// is supposed to outlast. Same tile means one event, at any radius.
        ///
        /// Disabled (<see cref="enabled"/> false) is the default everywhere, which makes P2 a pure addition:
        /// callers that pass nothing produce the same timeline as before.
        /// </summary>
        private struct ActionSpotlightEmitter
        {
            private readonly bool enabled;
            private bool hasFraming;
            private HexCoord framingCoord;

            public ActionSpotlightEmitter(bool enabled)
            {
                this.enabled = enabled;
                hasFraming = false;
                framingCoord = default;
            }

            /// <summary>
            /// Emits a spotlight for an action at <paramref name="coord"/>. Returns true when a beat was
            /// appended, so a caller that is standing in for a
            /// <see cref="CombatTimelineEventKind.MonsterActionGap"/> knows whether to still append the gap.
            /// </summary>
            public bool TryEmit(CombatTimeline timeline, HexCoord coord, string actorId, bool fallbackGap)
            {
                if (!enabled)
                {
                    return false;
                }

                if (hasFraming && framingCoord == coord)
                {
                    return false;
                }

                timeline.Append(
                    CombatTimelineEventKind.ActorSpotlight,
                    actorId,
                    to: coord,
                    fallbackGap: fallbackGap);
                hasFraming = true;
                framingCoord = coord;
                return true;
            }
        }

        /// <summary>
        /// The tile an effect should be framed at: its authored area centre, else the tile it came from.
        /// Effects with neither (deck/economy effects, player-stat changes) are not on-map events and get no
        /// spotlight. Deliberately does not fall back to the player's tile — the camera is already there.
        ///
        /// ⚠ An effect that reaches here without a coordinate is a rules-layer gap, not a case to paper over.
        /// Field-tick damage used to arrive coordless because <c>EffectRuntime.ApplyDamage</c> never took a
        /// centre, so the feature's headline case — a field ticking monsters the player walked away from —
        /// emitted no spotlight at all and the camera never went. It looked like it worked because the dev
        /// metrics resolve the same effect through the target unit and duly reported off-screen field damage,
        /// while the assembler that actually emits the beat could not see it. Fixed at the source, by giving
        /// the tick its tile like its sibling field.immobilize always had (plan §10.8).
        /// </summary>
        private static bool TryResolveEffectSpotlightCoord(EffectResultEvent effect, out HexCoord coord)
        {
            if (effect.Center.HasValue)
            {
                coord = effect.Center.Value;
                return true;
            }

            if (effect.SourceCoord.HasValue)
            {
                coord = effect.SourceCoord.Value;
                return true;
            }

            coord = default;
            return false;
        }

        /// <summary>
        /// Player movement turn: the player walks its captured route one tile at a time, then each monster
        /// that moved (and is presentation-relevant) steps to its new tile.
        /// </summary>
        public static CombatTimeline BuildPlayerMove(
            IReadOnlyList<HexCoord> playerPath,
            IReadOnlyList<MonsterActionResolutionRecord> monsterRecords)
        {
            var timeline = new CombatTimeline();
            timeline.AppendMovePath(CombatTimelineEventKind.PlayerMoveStep, "player", playerPath);
            // No action framing on the player's own turn: the camera is following the player, who is the one
            // acting. Only the monster phase is in this feature's scope (§1).
            var noSpotlights = new ActionSpotlightEmitter(enabled: false);
            AppendMonsterMoveSteps(timeline, monsterRecords, ref noSpotlights);
            return timeline;
        }

        /// <summary>
        /// Player attack: wind-up start, impact, the target's reaction flash, the buffered effects (so
        /// SFX/VFX/damage numbers fire one after another instead of all on a single frame), then knockback
        /// movement, hit-stop, and a post-kill hold on a lethal blow.
        /// </summary>
        public static CombatTimeline BuildPlayerAttack(
            string attackTrigger,
            string timingKey,
            HexCoord playerCoord,
            string targetMonsterId,
            HexCoord targetBefore,
            HexCoord targetAfter,
            bool targetHit,
            bool targetKnockedBack,
            bool targetDied,
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            ISet<string> diedUnitIds = null)
        {
            var timeline = new CombatTimeline();
            // From/To carry the facing vector (player ??target) so the sink can aim the attack animation.
            timeline.Append(CombatTimelineEventKind.PlayerAttackStart, "player", from: playerCoord, to: targetBefore, trigger: attackTrigger, timingKey: timingKey);
            timeline.Append(CombatTimelineEventKind.AttackImpact, "player", groupId: FirstGroupId(bufferedEffects), timingKey: timingKey);

            var hasTarget = !string.IsNullOrEmpty(targetMonsterId);

            // Area/blast attack landing on several enemies: present each target one at a time so its
            // own flinch/death reaction bundled with its damage number/SFX, spaced by the AoE target interval so
            // the blow reads as a sequence of individual hits instead of all numbers/sounds on a single frame.
            if (IsMultiTargetBlast(bufferedEffects))
            {
                AppendStaggeredTargetBurst(timeline, bufferedEffects, diedUnitIds);

                // One trailing hit-stop for impact punch, keyed to the aim target (lethal if it was a kill).
                if (hasTarget && (targetHit || targetKnockedBack || targetDied))
                {
                    timeline.Append(CombatTimelineEventKind.HitStop, targetMonsterId, lethal: targetDied, timingKey: timingKey);
                }

                return timeline;
            }

            // Single-target path (incl. multi-hit strikes such as A06): the reaction flash leads the numbers, and
            // knockback/death resolve after the effects, exactly as the legacy sequence did.
            // The reaction's timing follows the same effect the VFX/SFX/shake do, so the flinch lands together
            // with them (EffectIndex points the sink at that effect's per-cue delay).
            var reactionEffectIndex = FirstDamageEffectIndex(bufferedEffects, string.Empty);
            // Non-lethal reaction flash before the numbers (death anim is presented after the effects).
            if (hasTarget && !targetDied)
            {
                if (targetKnockedBack)
                {
                    timeline.Append(CombatTimelineEventKind.ActorKnockback, targetMonsterId, effectIndex: reactionEffectIndex);
                }
                else if (targetHit)
                {
                    timeline.Append(CombatTimelineEventKind.ActorHit, targetMonsterId, effectIndex: reactionEffectIndex);
                }
            }

            AppendAllEffects(timeline, bufferedEffects);

            if (hasTarget && targetKnockedBack && !targetDied)
            {
                timeline.Append(CombatTimelineEventKind.EnemyKnockbackStep, targetMonsterId, targetBefore, targetAfter);
            }

            if (hasTarget)
            {
                if (targetDied)
                {
                    // ActorDeath triggers the death anim; the scheduler then holds for the death delay.
                    timeline.Append(CombatTimelineEventKind.ActorDeath, targetMonsterId, effectIndex: reactionEffectIndex, lethal: true, timingKey: timingKey);
                }
                else if (targetHit || targetKnockedBack)
                {
                    timeline.Append(CombatTimelineEventKind.HitStop, targetMonsterId, timingKey: timingKey);
                }
            }

            return timeline;
        }

        /// <summary>
        /// A standalone multi-target damage burst (S01/F01-style placement): no attack wind-up/impact
        /// beat -- just each hit target's flinch/death reaction bundled with its damage number, staggered by the AoE
        /// target interval. The caster's cast pose is triggered by the controller before this plays.
        /// </summary>
        public static CombatTimeline BuildEffectBurst(
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            ISet<string> diedUnitIds = null)
        {
            var timeline = new CombatTimeline();
            AppendStaggeredTargetBurst(timeline, bufferedEffects, diedUnitIds);
            return timeline;
        }

        /// <summary>
        /// End-of-turn monster phase: each presentable monster moves (per tile), then any attacker winds up,
        /// impacts (player reaction flash before its grouped effects), and the player reacts. A player
        /// knockback is presented once, and a fatal blow ends with the player's death beat + hold.
        /// </summary>
        public static CombatTimeline BuildEndAction(
            IReadOnlyList<MonsterActionResolutionRecord> records,
            bool playerKnockedBack,
            HexCoord playerBefore,
            HexCoord playerAfter,
            bool playerDied,
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            ISet<string> diedUnitIds = null,
            bool includeMonsterMovement = true,
            bool emitActionSpotlights = false,
            IReadOnlyList<BossPhaseTransition> bossPhaseTransitions = null,
            IReadOnlyList<BossPropAbsorption> bossPropAbsorptions = null,
            IReadOnlyList<BossPropVolleyCast> bossPropVolleyCasts = null,
            IReadOnlyList<BossScrapChainHit> bossScrapChainHits = null)
        {
            var timeline = new CombatTimeline();

            // 보스 기믹(흡수 → 살포)은 규칙상 몬스터 공격 결의 <b>앞</b>에서 돌므로, 연출도 몬스터 페이즈
            // 맨 앞에 온다. 순서는 기믹 안의 순서와 같다: 성숙한 것이 먼저 터져 흡수되고, 그 다음에
            // 새 무더기가 깔린다. 살포한 턴에는 보스가 공격하지 않으므로 이 비트들이 보스 행동 그 자체다.
            AppendBossPropBeats(timeline, bossPropAbsorptions, bossPropVolleyCasts);
            // 사슬 명중(후속 #7)은 저작 순서상 철조각 기믹 뒤에 해소된다 — 선이 그어진 끝에 피해 숫자가 붙는다.
            // 여기서 고정한 효과 인덱스는 끝의 일괄 방출(AppendUnscheduledTurnStartEffects)에서 빠져야 두 번 뜨지 않는다.
            var pinnedEffectIndices = AppendBossScrapChainBeats(timeline, bossScrapChainHits, bufferedEffects);
            // A death zoom owns the camera on the player-death path, so no action framing competes with it.
            var spotlights = new ActionSpotlightEmitter(emitActionSpotlights && !playerDied);
            var playerKnockbackPresented = false;
            var presentedAnyMonster = false;
            var presentedAnyMonsterAttack = false;
            // 한 몬스터 페이즈에서 '기습!' 경고는 한 번만 띄운다(첫 암시야 공격에서).
            var ambushPresented = false;

            if (records != null)
            {
                foreach (var record in records)
                {
                    if (!record.ShouldPresent)
                    {
                        continue;
                    }

                    // A monster hidden in fog (Unknown/Hinted) must not animate its own move/attack/effects.
                    // 단, 공격 연출 가시성(attackShows)은 공격 시점 가시성(VisibleAtAttackTime)까지 포함한다: 이동해서
                    // 시야에 들어와 공격한 뒤 플레이어 넉백으로 시야 밖으로 밀려난 몬스터도 '기존 위치에서 보이던' 공격이므로
                    // 끝까지 가시 연출(빗맞음 포함)로 보여주고 기습으로 오분류하지 않는다.
                    var monsterShows = record.IsMonsterVisible;
                    var attackShows = record.IsAttackVisible;
                    var visibleAttack = attackShows && (record.AttackedPlayer || record.MissedAttack);
                    var producesAction = (includeMonsterMovement && record.Moved && monsterShows) || visibleAttack;
                    // A breathing pause between each acting monster so three monsters in a row read clearly.
                    // When action framing is on, the spotlight takes that slot: a camera move *is* the pause
                    // (§3.2), so the gap is only appended when no spotlight was emitted here. A spotlight that
                    // turns out to be on-screen restores the pause at replay time via FallbackGap, which is
                    // why the flag is set from the same condition that used to append the gap.
                    if (producesAction)
                    {
                        var emitted = spotlights.TryEmit(
                            timeline, record.AfterCoord, record.MonsterId, fallbackGap: presentedAnyMonster);
                        if (!emitted && presentedAnyMonster)
                        {
                            timeline.Append(CombatTimelineEventKind.MonsterActionGap);
                        }

                        presentedAnyMonster = true;
                    }

                    if (includeMonsterMovement && record.Moved && monsterShows)
                    {
                        AppendEnemyMove(timeline, record);
                    }

                    if (record.AttackedPlayer && attackShows)
                    {
                        presentedAnyMonsterAttack = true;
                        var patternKey = record.AttackPatternId;
                        // Only a visible attacker plays wind-up/impact/effects. From/To carry the facing vector
                        // for the attack animation. 🔑 To는 플레이어의 <b>실좌표가 아니라 예고(커밋) 시점에
                        // 겨눈 칸</b>이다(2026-08-20 #7): 규칙층이 형상 footprint를 이미 그 방향으로 굳혀
                        // 두었으므로(GetCommittedMonsterAttackFootprint), 모델만 실좌표를 보면 몸과 판정이
                        // 서로 다른 방향을 가리킨다. 커밋 정보가 없는 경로만 실좌표로 물러난다.
                        timeline.Append(CombatTimelineEventKind.EnemyAttackStart, record.MonsterId, from: record.AfterCoord, to: record.AimCoord ?? playerBefore, trigger: record.AttackAnimationTrigger, timingKey: patternKey);
                        timeline.Append(CombatTimelineEventKind.AttackImpact, record.MonsterId, groupId: record.PresentationGroupId, timingKey: patternKey);
                        var dealtDamage = record.DamageToPlayer > 0 && !playerDied;
                        if (dealtDamage)
                        {
                            var hitEffectIndex = FirstDamageEffectIndex(bufferedEffects, record.PresentationGroupId);
                            timeline.Append(CombatTimelineEventKind.PlayerHit, "player", effectIndex: hitEffectIndex);
                        }

                        // The hit's own effects (damage number, status apply, the "밀려남" knockback cue) land at
            // impact; then the player slides; then the collision impact is replayed so its
                        // number/SFX fire when the knocked-back player actually hits something.
                        AppendEffectsForGroup(timeline, bufferedEffects, record.PresentationGroupId, collisionImpact: false);

                        if (dealtDamage)
                        {
                            timeline.Append(CombatTimelineEventKind.HitStop, "player", timingKey: patternKey);
                        }

                        if (playerKnockedBack && !playerKnockbackPresented && record.KnockedBackPlayer)
                        {
                            AppendPlayerKnockback(timeline, record, playerBefore, playerAfter);
                            AppendMovementKnockbackEffectsForRecord(timeline, bufferedEffects, record);
                            playerKnockbackPresented = true;
                        }

                        AppendEffectsForGroup(timeline, bufferedEffects, record.PresentationGroupId, collisionImpact: true);

                        // 「밀어붙이기」(요괴 §4-5): 때린 <b>뒤</b> 한 칸 다가선다. 공격 앞의 걷기 비트와
                        // 따로 내야 "치고 들어온다"로 읽힌다 — 앞의 걷기에 합치면 두 칸 걸어와 때린 것이 된다.
                        if (record.AdvancedFrom.HasValue && record.AdvancedTo.HasValue)
                        {
                            timeline.Append(
                                CombatTimelineEventKind.EnemyMoveStep,
                                record.MonsterId,
                                record.AdvancedFrom.Value,
                                record.AdvancedTo.Value);
                        }
                    }
                    else if (record.AttackedPlayer && !attackShows)
                    {
                        // 진짜 기습: 공격자가 이동 전·공격 시점·종료 시점 어디서도 보이지 않았다(셋 다 false).
                        // 넉백으로 시야가 바뀌어 나간 몬스터는 WasVisibleBefore 또는 VisibleAtAttackTime이 true라
                        // 이 분기에 들어오지 않으므로 기습에서 제외되고 위의 가시 공격 분기로 끝까지 연출된다.
                        // "기습!" 경고를 가장 먼저 플레이어 위치에 띄운다(페이즈당 1회).
                        // ⚠️ 빗맞은 은닉 공격은 기습이 아니다(§28 W6): 휘두름이 whiff로 판정돼도
                        // AttackedPlayer는 참이라(연출 큐 유지용), 이 가드가 없으면 아무 일도 없었는데
                        // 경고만 뜬다 — "기습!"이 거짓말이 된다.
                        // ⚠️ 자기부여 패턴(2026-08-20 #2)도 같은 이유로 기습이 아니다: 숨은 몬스터가 자기
                        // 버프만 걸어도 AttackedPlayer가 참이라, 이 가드가 없으면 화면엔 아무 일도 없는데
                        // "기습!"만 뜬다(A029/A030 불가살·A031 돼지에서 실측된 원인불명 출력의 정체).
                        if (!ambushPresented && !record.MissedAttack && !record.SelfTargetedAttack)
                        {
                            timeline.Append(CombatTimelineEventKind.AmbushAlert, "player");
                            ambushPresented = true;
                        }

                        var dealtDamage = record.DamageToPlayer > 0 && !playerDied;
                        if (dealtDamage)
                        {
                            var hitEffectIndex = FirstDamageEffectIndex(bufferedEffects, record.PresentationGroupId);
                            timeline.Append(CombatTimelineEventKind.PlayerHit, "player", effectIndex: hitEffectIndex);
                        }

                        // Hidden attackers stay invisible: no monster movement/model attack beats. Still replay
                        // the player-targeted impact group so the player gets damage numbers/effects/SFX/VFX
                        // (공격 SFX·플레이어 피격 SFX/보이스·데미지 텍스트·공격 VFX는 플레이어 대상 버퍼 이펙트
                        // 디스패치에서 재생된다 — MapEffectToCueIds의 IsPlayerTarget 경로).
                        // ⚠️단, 숨은 빗맞음은 통째로 침묵한다(2026-08-20 #14): 예전에는 안개 속에서
                        // 「빗맞음」 텍스트·타일 플래시·공격 SFX가 새어 나와 숨은 몬스터의 위치를
                        // 누설했고, #14로 빗맞음에 공격 VFX까지 실리면 누설이 더 커진다. §28 W6의
                        // 정신("아무 일도 없었다") 그대로 — 아무것도 재생하지 않는다.
                        if (!record.MissedAttack)
                        {
                            AppendEffectsForGroup(timeline, bufferedEffects, record.PresentationGroupId, collisionImpact: false);
                        }
                    }
                    else if (playerKnockedBack && !playerKnockbackPresented && record.KnockedBackPlayer)
                    {
                        AppendPlayerKnockback(timeline, record, playerBefore, playerAfter);
                        AppendMovementKnockbackEffectsForRecord(timeline, bufferedEffects, record);
                        playerKnockbackPresented = true;
                    }
                }
            }

            if (playerKnockedBack && !playerKnockbackPresented)
            {
                timeline.Append(CombatTimelineEventKind.PlayerKnockbackStep, "player", playerBefore, playerAfter);
                AppendMovementKnockbackEffectsForRecord(timeline, bufferedEffects, null);
            }

            // 보스 페이즈 전환 연출: 몬스터 액션 페이즈가 끝난 뒤, 턴 경계 알림(MonsterPhaseCompleteGap/
            // OverallTurnStart) 앞에 방출한다. 규칙상 전환은 이미 해결됐고 이 비트는 sink가 소유한 표현 전용이다
            // (카메라 프레이밍·버스트·셰이크·아우라 전환, P4). 플레이어 사망 시엔 죽음 줌이 카메라를 소유하므로
            // 방출하지 않는다. 프레이밍 좌표는 전환한 보스의 이번 턴 위치이고, 레코드에 없으면(전환한 보스가
            // 행동하지 않은 경우) sink가 unitId로 라이브 위치를 찾는다.
            if (!playerDied && bossPhaseTransitions != null)
            {
                foreach (var transition in bossPhaseTransitions)
                {
                    var framing = TryFindMonsterAfterCoord(records, transition.BossUnitId, out var bossCoord)
                        ? bossCoord
                        : (HexCoord?)null;
                    timeline.Append(CombatTimelineEventKind.BossPhaseTransition, transition.BossUnitId, to: framing);
                }
            }

            if (!playerDied)
            {
                if (presentedAnyMonsterAttack)
                {
                    timeline.Append(CombatTimelineEventKind.MonsterPhaseCompleteGap);
                }

                timeline.Append(CombatTimelineEventKind.OverallTurnStart);
            }

            // Field-object tick damage (F03-style lingering field) hits each monster standing in it: present those
            // as a staggered per-target burst after the monster phase so each one shows its own flinch/death +
            // number. During the monster phase the only Damage dealt to monsters is this field tick (monster
            // attacks hit the player; reflect uses ReflectDamage), so a monster-target filter isolates it.
            AppendMonsterDamageBurst(timeline, bufferedEffects, diedUnitIds, records, ref spotlights);

            // Effects produced by the next-turn start boundary (pending field install/activation, poison ticks,
            // status expiry/refresh cues, player field damage/heal) are not tied to a monster attack group. If
            // left for the controller's defensive flush they all appear on the same frame, bypassing the
            // status/effect stagger policy. Append the remaining unscheduled effects here so the scheduler owns
            // their pacing.
            AppendUnscheduledTurnStartEffects(timeline, bufferedEffects, records, ref spotlights, pinnedEffectIndices);

            if (!playerDied)
            {
                // The camera comes home BEFORE the turn starts (plan §10.13). Emitted as its own beat rather
                // than left to the controller's post-Play cleanup so the order is a property of the timeline —
                // otherwise the card deal-in of the next turn plays while the camera is still out at the last
                // field object, which is exactly what a player sees as "the turn started too early".
                //
                // Unconditional: the sink no-ops when nothing owns the camera, and making the assembler guess
                // whether a spotlight will actually move the camera is the mistake §10.6 already paid for.
                timeline.Append(CombatTimelineEventKind.ActionFocusRelease);
                timeline.Append(CombatTimelineEventKind.PlayerTurnStart);
            }

            if (playerDied)
            {
                timeline.Append(CombatTimelineEventKind.PlayerDeath, "player", lethal: true);
            }

            // Only meaningful when spotlights were emitted; harmless (a no-op walk) otherwise.
            timeline.AnnotateActorSpotlightCoverage();
            return timeline;
        }

        /// <summary>
        /// Monster movement phase only: replay planned monster movement without attack impacts or turn-boundary
        /// effects. The attack phase reuses the same resolution records with movement suppressed.
        /// </summary>
        public static CombatTimeline BuildMonsterMovementPhase(
            IReadOnlyList<MonsterActionResolutionRecord> records,
            bool playerKnockedBack = false,
            HexCoord playerBefore = default,
            HexCoord playerAfter = default,
            bool emitActionSpotlights = false)
        {
            var timeline = new CombatTimeline();
            var spotlights = new ActionSpotlightEmitter(emitActionSpotlights);
            AppendMonsterMoveSteps(timeline, records, ref spotlights);

            // 몬스터가 플레이어 칸으로 진입해 충돌 넉백이 일어난 경우: 순간이동 대신 슬라이드(PlayerKnockbackStep)로
            // 표현한다. 이전에는 이동 페이즈 타임라인이 넉백 비트를 전혀 방출하지 않아 플레이어가 텔레포트했다.
            if (playerKnockedBack)
            {
                var record = FindPlayerKnockbackRecord(records);
                if (record.HasValue)
                {
                    AppendPlayerKnockback(timeline, record.Value, playerBefore, playerAfter);
                }
                else
                {
                    timeline.Append(CombatTimelineEventKind.PlayerKnockbackStep, "player", playerBefore, playerAfter);
                }
            }

            timeline.AnnotateActorSpotlightCoverage();
            return timeline;
        }

        private static MonsterActionResolutionRecord? FindPlayerKnockbackRecord(IReadOnlyList<MonsterActionResolutionRecord> records)
        {
            if (records == null)
            {
                return null;
            }

            for (var i = 0; i < records.Count; i++)
            {
                if (records[i].KnockedBackPlayer)
                {
                    return records[i];
                }
            }

            return null;
        }

        // 전환한 보스의 이번 턴 위치(있으면 프레이밍에 쓴다). 레코드에 없으면 false — sink가 unitId로 라이브
        // 위치를 찾는다(전환한 보스가 이번 턴에 행동하지 않았어도 일어날 수 있다).
        private static bool TryFindMonsterAfterCoord(IReadOnlyList<MonsterActionResolutionRecord> records, string monsterId, out HexCoord coord)
        {
            coord = default;
            if (records == null || string.IsNullOrEmpty(monsterId))
            {
                return false;
            }

            for (var i = 0; i < records.Count; i++)
            {
                if (string.Equals(records[i].MonsterId, monsterId, System.StringComparison.Ordinal))
                {
                    coord = records[i].AfterCoord;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 보스 기물(철조각) 연출 비트: 흡수(폭발 → 보스로 빨려 들어감)가 먼저, 그 다음 살포(캐스트 →
        /// 기물이 하나씩 꽂힘). 규칙 계층의 기믹 결의 순서를 그대로 따른다.
        ///
        /// 놓인 칸을 <b>비트마다</b> 실어 보내므로 sink는 결의 직후의 <c>CombatState</c>를 다시 들여다볼
        /// 필요가 없다 — 재생 시점에 라이브 상태를 읽으면 그 사이 죽은 기물이 연출에서 사라진다.
        /// </summary>
        private static void AppendBossPropBeats(
            CombatTimeline timeline,
            IReadOnlyList<BossPropAbsorption> absorptions,
            IReadOnlyList<BossPropVolleyCast> volleyCasts)
        {
            if (absorptions != null)
            {
                foreach (var absorption in absorptions)
                {
                    timeline.Append(
                        CombatTimelineEventKind.BossPropAbsorb,
                        absorption.BossUnitId,
                        from: absorption.PropCoord,
                        to: absorption.BossCoord);
                }
            }

            if (volleyCasts == null)
            {
                return;
            }

            foreach (var cast in volleyCasts)
            {
                timeline.Append(CombatTimelineEventKind.BossPropVolleyCast, cast.BossUnitId, to: cast.BossCoord);
                foreach (var coord in cast.PlacedCoords)
                {
                    timeline.Append(CombatTimelineEventKind.BossPropPlaced, cast.BossUnitId, to: coord);
                }
            }
        }

        /// <summary>
        /// 사슬 명중 비트(2026-09-05 후속 #7): 가닥마다 선이 그어지는 비트 하나 + 맞았으면 그 사슬의 효과 비트
        /// (피해/방어/속박 — <c>sourceRef boss.scrap-chain</c>)를 <b>바로 뒤에</b> 붙인다. 붙이지 않으면 그 효과들은
        /// 시퀀스 끝 일괄 방출로 밀려 선이 사라진 뒤에 숫자가 뜬다.
        /// </summary>
        private static HashSet<int> AppendBossScrapChainBeats(
            CombatTimeline timeline,
            IReadOnlyList<BossScrapChainHit> hits,
            IReadOnlyList<EffectResultEvent> bufferedEffects)
        {
            var pinned = new HashSet<int>();
            if (hits == null)
            {
                return pinned;
            }

            foreach (var hit in hits)
            {
                timeline.Append(
                    CombatTimelineEventKind.BossScrapChainHit,
                    hit.BossUnitId,
                    from: hit.BossCoord,
                    to: hit.HitPlayer ? hit.PlayerCoord : (HexCoord?)null);
                if (!hit.HitPlayer || bufferedEffects == null)
                {
                    continue;
                }

                for (var i = 0; i < bufferedEffects.Count; i++)
                {
                    var effect = bufferedEffects[i];
                    if (!string.Equals(effect.SourceRef, CombatState.BossScrapChainSourceRefs.Hit, System.StringComparison.Ordinal)
                        || !string.Equals(effect.TargetUnitId, "player", System.StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(effect.SourceUnitId, hit.BossUnitId, System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    pinned.Add(i);
                    timeline.Append(
                        CombatTimelineEventKind.Effect,
                        effectIndex: i,
                        groupId: effect.PresentationGroupId,
                        statusApply: IsStatusApply(effect),
                        textQueue: true);
                }
            }

            return pinned;
        }

        private static void AppendPlayerKnockback(
            CombatTimeline timeline,
            MonsterActionResolutionRecord record,
            HexCoord fallbackBefore,
            HexCoord fallbackAfter)
        {
            var from = record.PlayerKnockbackFrom ?? fallbackBefore;
            var to = record.PlayerKnockbackTo ?? fallbackAfter;
            timeline.Append(CombatTimelineEventKind.PlayerKnockbackStep, "player", from, to);
        }

        private static string FirstGroupId(IReadOnlyList<EffectResultEvent> bufferedEffects)
        {
            return bufferedEffects != null && bufferedEffects.Count > 0 ? bufferedEffects[0].PresentationGroupId : string.Empty;
        }

        /// <summary>
        /// Index of the damage effect a reaction should sync to (so the flinch lands with that effect's
        /// VFX/SFX/shake), restricted to <paramref name="groupId"/> when given. Falls back to the first
        /// effect in the group, or -1 when there is none.
        /// </summary>
        private static int FirstDamageEffectIndex(IReadOnlyList<EffectResultEvent> bufferedEffects, string groupId)
        {
            if (bufferedEffects == null)
            {
                return -1;
            }

            bool InGroup(EffectResultEvent e) =>
                string.IsNullOrEmpty(groupId) || string.Equals(e.PresentationGroupId, groupId, System.StringComparison.Ordinal);

            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                if (bufferedEffects[i].Kind == EffectKind.Damage && InGroup(bufferedEffects[i]))
                {
                    return i;
                }
            }

            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                if (InGroup(bufferedEffects[i]))
                {
                    return i;
                }
            }

            return -1;
        }

        private static void AppendAllEffects(CombatTimeline timeline, IReadOnlyList<EffectResultEvent> bufferedEffects)
        {
            if (bufferedEffects == null)
            {
                return;
            }

            foreach (var i in OrderedEffectIndicesForPresentation(bufferedEffects))
            {
                timeline.Append(
                    CombatTimelineEventKind.Effect,
                    effectIndex: i,
                    groupId: bufferedEffects[i].PresentationGroupId,
                    multiHitStrike: IsMultiHitStrike(bufferedEffects[i]),
                    statusApply: IsStatusApply(bufferedEffects[i]),
                    textQueue: true);
            }
        }

        private static IEnumerable<int> OrderedEffectIndicesForPresentation(IReadOnlyList<EffectResultEvent> bufferedEffects)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < bufferedEffects.Count; i++)
                {
                    var isStatus = IsStatusApply(bufferedEffects[i]);
                    if ((pass == 0 && isStatus) || (pass == 1 && !isStatus))
                    {
                        yield return i;
                    }
                }
            }
        }

        // True when a buffered effect's target is a concrete monster marker we can play a hit/death reaction on
        // (not the player, not an aggregate/area placeholder like "monster"/"field"/empty).
        private static bool IsReactableMonsterTarget(EffectResultEvent effect)
        {
            var id = effect.TargetUnitId;
            return !string.IsNullOrEmpty(id)
                && !string.Equals(id, "monster", System.StringComparison.Ordinal)
                && !string.Equals(id, "player", System.StringComparison.Ordinal)
                && !string.Equals(id, "field", System.StringComparison.Ordinal);
        }

        // A blast/area attack raises one damage effect per concrete target. Two or more distinct reactable
        // monster targets means we present it as a staggered per-target burst instead of the single-target path.
        private static bool IsMultiTargetBlast(IReadOnlyList<EffectResultEvent> bufferedEffects)
        {
            if (bufferedEffects == null)
            {
                return false;
            }

            string first = null;
            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                var effect = bufferedEffects[i];
                if (effect.Kind != EffectKind.Damage || !IsReactableMonsterTarget(effect))
                {
                    continue;
                }

                if (first == null)
                {
                    first = effect.TargetUnitId;
                }
                else if (!string.Equals(first, effect.TargetUnitId, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // Replay every buffered effect as a staggered per-target beat: each concrete monster target's flinch (or
        // death, when in <paramref name="diedUnitIds"/>) reaction first, then its damage number/SFX/VFX, all spaced
        // from the next target by the AoE target interval (Effect.aoeTarget). Non-reactable effects (status applies,
        // player numbers) are still replayed in place with the same spacing but without a reaction. Per-target
        // deaths use lethal:false so the scheduler plays the death anim without holding once per kill.
        private static void AppendStaggeredTargetBurst(CombatTimeline timeline, IReadOnlyList<EffectResultEvent> bufferedEffects, ISet<string> diedUnitIds)
        {
            if (bufferedEffects == null)
            {
                return;
            }

            var pendingAreaCueIndices = new List<int>();
            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                var effect = bufferedEffects[i];
                if (IsFieldDamageAreaCue(effect))
                {
                    pendingAreaCueIndices.Add(i);
                    continue;
                }

                if (effect.Kind == EffectKind.Damage && IsReactableMonsterTarget(effect))
                {
                    var died = diedUnitIds != null && diedUnitIds.Contains(effect.TargetUnitId);
                    timeline.Append(
                        died ? CombatTimelineEventKind.ActorDeath : CombatTimelineEventKind.ActorHit,
                        effect.TargetUnitId,
                        effectIndex: i);

                    AppendPendingCompanionEffects(timeline, bufferedEffects, pendingAreaCueIndices);
                }

                timeline.Append(CombatTimelineEventKind.Effect, effectIndex: i, groupId: effect.PresentationGroupId, aoeTarget: true, statusApply: IsStatusApply(effect));
            }

            AppendPendingCompanionEffects(timeline, bufferedEffects, pendingAreaCueIndices, noWaitAfterEffect: false);
        }

        // Like the burst above but restricted to Damage effects that target a concrete monster ??used at the end of
        // the monster phase to present field-object tick damage without disturbing the player-targeted monster
        // attack effects (those are replayed per attacking monster by their presentation group).
        private static void AppendMonsterDamageBurst(
            CombatTimeline timeline,
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            ISet<string> diedUnitIds,
            IReadOnlyList<MonsterActionResolutionRecord> records,
            ref ActionSpotlightEmitter spotlights)
        {
            if (bufferedEffects == null)
            {
                return;
            }

            var pendingAreaCueIndices = new List<int>();
            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                var effect = bufferedEffects[i];
                if (IsFieldDamageAreaCue(effect))
                {
                    pendingAreaCueIndices.Add(i);
                    continue;
                }

                if (effect.Kind != EffectKind.Damage || !IsReactableMonsterTarget(effect))
                {
                    continue;
                }

                if (IsHiddenMonsterTargetEffect(effect, records))
                {
                    continue;
                }

                // Field-tick damage on a monster the player walked away from is the case this whole feature
                // exists for (§2.3). No gap fallback: this stretch of the timeline never had one.
                if (TryResolveEffectSpotlightCoord(effect, out var burstCoord))
                {
                    spotlights.TryEmit(timeline, burstCoord, effect.TargetUnitId, fallbackGap: false);
                }

                var died = diedUnitIds != null && diedUnitIds.Contains(effect.TargetUnitId);
                timeline.Append(
                    died ? CombatTimelineEventKind.ActorDeath : CombatTimelineEventKind.ActorHit,
                    effect.TargetUnitId,
                    effectIndex: i);
                AppendPendingCompanionEffects(timeline, bufferedEffects, pendingAreaCueIndices);
                timeline.Append(CombatTimelineEventKind.Effect, effectIndex: i, groupId: effect.PresentationGroupId, aoeTarget: true);
            }
        }

        private static void AppendPendingCompanionEffects(
            CombatTimeline timeline,
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            List<int> pendingIndices,
            bool noWaitAfterEffect = true)
        {
            if (pendingIndices == null || pendingIndices.Count == 0)
            {
                return;
            }

            for (var j = 0; j < pendingIndices.Count; j++)
            {
                var index = pendingIndices[j];
                var effect = bufferedEffects[index];
                timeline.Append(
                    CombatTimelineEventKind.Effect,
                    effectIndex: index,
                    groupId: effect.PresentationGroupId,
                    statusApply: IsStatusApply(effect),
                    noWaitAfterEffect: noWaitAfterEffect);
            }

            pendingIndices.Clear();
        }

        private static void AppendUnscheduledTurnStartEffects(
            CombatTimeline timeline,
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            IReadOnlyList<MonsterActionResolutionRecord> records,
            ref ActionSpotlightEmitter spotlights,
            ISet<int> alreadyPinnedEffectIndices = null)
        {
            if (bufferedEffects == null)
            {
                return;
            }

            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                var effect = bufferedEffects[i];
                if (alreadyPinnedEffectIndices != null && alreadyPinnedEffectIndices.Contains(i))
                {
                    continue;
                }

                if (IsScheduledByMonsterActionGroup(effect, records)
                    || IsScheduledByMonsterDamageBurst(effect, bufferedEffects, records))
                {
                    continue;
                }

                if (IsHiddenMonsterTargetEffect(effect, records))
                {
                    continue;
                }

                if (IsMovementKnockbackEffectForAnyRecord(effect, records))
                {
                    continue;
                }

                // Turn-boundary effects with a tile: a distant field activating, a poison tick on a scouted
                // monster (§2.3). Ones with no coord at all (player stat changes, deck effects) are skipped by
                // TryResolveEffectSpotlightCoord rather than dragging the camera to the player.
                if (TryResolveEffectSpotlightCoord(effect, out var effectCoord))
                {
                    spotlights.TryEmit(timeline, effectCoord, effect.TargetUnitId, fallbackGap: false);
                }

                timeline.Append(
                    CombatTimelineEventKind.Effect,
                    effectIndex: i,
                    groupId: effect.PresentationGroupId,
                    multiHitStrike: IsMultiHitStrike(effect),
                    statusApply: IsStatusApply(effect));
            }
        }

        private static void AppendMovementKnockbackEffectsForRecord(
            CombatTimeline timeline,
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            MonsterActionResolutionRecord? record)
        {
            if (bufferedEffects == null)
            {
                return;
            }

            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                if (!IsMovementKnockbackEffectForRecord(bufferedEffects[i], record))
                {
                    continue;
                }

                timeline.Append(
                    CombatTimelineEventKind.Effect,
                    effectIndex: i,
                    groupId: bufferedEffects[i].PresentationGroupId,
                    multiHitStrike: IsMultiHitStrike(bufferedEffects[i]),
                    statusApply: IsStatusApply(bufferedEffects[i]),
                    textQueue: true);
            }
        }

        private static bool IsMovementKnockbackEffectForAnyRecord(EffectResultEvent effect, IReadOnlyList<MonsterActionResolutionRecord> records)
        {
            if (records == null || records.Count == 0)
            {
                return IsMovementKnockbackEffectForRecord(effect, null);
            }

            for (var i = 0; i < records.Count; i++)
            {
                if (IsMovementKnockbackEffectForRecord(effect, records[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMovementKnockbackEffectForRecord(EffectResultEvent effect, MonsterActionResolutionRecord? record)
        {
            if (effect.Kind != EffectKind.Knockback ||
                !string.IsNullOrEmpty(effect.PresentationGroupId) ||
                !string.Equals(effect.SourceRef, "knockback", System.StringComparison.Ordinal) ||
                !string.Equals(effect.TargetUnitId, "player", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!record.HasValue)
            {
                return true;
            }

            var value = record.Value;
            return value.KnockedBackPlayer &&
                   string.Equals(effect.SourceUnitId, value.MonsterId, System.StringComparison.Ordinal);
        }

        private static bool IsScheduledByMonsterActionGroup(EffectResultEvent effect, IReadOnlyList<MonsterActionResolutionRecord> records)
        {
            if (records == null || string.IsNullOrEmpty(effect.PresentationGroupId))
            {
                return false;
            }

            for (var i = 0; i < records.Count; i++)
            {
                if (string.Equals(effect.PresentationGroupId, records[i].PresentationGroupId, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsScheduledByMonsterDamageBurst(
            EffectResultEvent effect,
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            IReadOnlyList<MonsterActionResolutionRecord> records)
        {
            if (effect.Kind == EffectKind.Damage && IsReactableMonsterTarget(effect))
            {
                return true;
            }

            return IsFieldDamageAreaCue(effect) && HasVisibleMonsterDamageBurst(bufferedEffects, records);
        }

        private static bool HasVisibleMonsterDamageBurst(
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            IReadOnlyList<MonsterActionResolutionRecord> records)
        {
            if (bufferedEffects == null)
            {
                return false;
            }

            for (var i = 0; i < bufferedEffects.Count; i++)
            {
                var effect = bufferedEffects[i];
                if (effect.Kind == EffectKind.Damage
                    && IsReactableMonsterTarget(effect)
                    && !IsHiddenMonsterTargetEffect(effect, records))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsFieldDamageAreaCue(EffectResultEvent effect)
        {
            return effect.Kind == EffectKind.Damage
                && string.Equals(effect.TargetUnitId, "field", System.StringComparison.Ordinal)
                && effect.Radius > 0
                && (string.Equals(effect.SourceRef, CardEffectRefs.FieldDamage, System.StringComparison.Ordinal)
                    || string.Equals(effect.SourceRef, CardEffectRefs.FieldDamageFirebomb, System.StringComparison.Ordinal));
        }

        private static bool IsHiddenMonsterTargetEffect(EffectResultEvent effect, IReadOnlyList<MonsterActionResolutionRecord> records)
        {
            if (records == null || records.Count == 0 || !MonsterActionPresentationFilters.IsMonsterTargetEffect(effect))
            {
                return false;
            }

            // 🔴 특성 알림은 이 필터를 지나지 않는다. 띄울지 말지는 규칙층이 안개·은신을 보고 이미 정했고,
            //    여기서 한 번 더 거르면 <b>노출 알림이 노출되지 않는다</b> — 은신 공격으로 드러나는 순간의
            //    「들킴!」은 기록상 「숨은 채 한 행동」에 딸려 있어서 정확히 여기서 죽는다.
            if (MonsterTraitAnnouncement.IsAnnouncement(effect))
            {
                return false;
            }

            for (var i = 0; i < records.Count; i++)
            {
                if (string.Equals(records[i].MonsterId, effect.TargetUnitId, System.StringComparison.Ordinal))
                {
                    return !records[i].IsMonsterVisible;
                }
            }

            return false;
        }

        /// <summary>
        /// Appends a presentation group's buffered effects as Effect beats, restricted to one side of a
        /// knockback slide: <paramref name="collisionImpact"/> false plays the hit's normal effects (damage,
        /// status, the "밀려남" knockback cue) at impact, while true replays only the collision impact
        /// ("knockback.impact") ??emitted after the <see cref="CombatTimelineEventKind.PlayerKnockbackStep"/>
        /// so it lands when the knocked-back target actually hits something. Attacks without a collision simply
        /// have nothing to append on the true pass.
        /// </summary>
        private static void AppendEffectsForGroup(CombatTimeline timeline, IReadOnlyList<EffectResultEvent> bufferedEffects, string groupId, bool collisionImpact)
        {
            if (bufferedEffects == null || string.IsNullOrEmpty(groupId))
            {
                return;
            }

            foreach (var i in OrderedEffectIndicesForPresentation(bufferedEffects, groupId, collisionImpact))
            {
                var effect = bufferedEffects[i];
                timeline.Append(CombatTimelineEventKind.Effect, effectIndex: i, groupId: groupId, multiHitStrike: IsMultiHitStrike(effect), statusApply: IsStatusApply(effect), textQueue: true);
            }
        }

        private static IEnumerable<int> OrderedEffectIndicesForPresentation(
            IReadOnlyList<EffectResultEvent> bufferedEffects,
            string groupId,
            bool collisionImpact)
        {
            for (var pass = 0; pass < 2; pass++)
            {
                for (var i = 0; i < bufferedEffects.Count; i++)
                {
                    var effect = bufferedEffects[i];
                    if (!string.Equals(effect.PresentationGroupId, groupId, System.StringComparison.Ordinal)
                        || IsKnockbackCollisionImpact(effect) != collisionImpact)
                    {
                        continue;
                    }

                    var isStatus = IsStatusApply(effect);
                    if ((pass == 0 && isStatus) || (pass == 1 && !isStatus))
                    {
                        yield return i;
                    }
                }
            }
        }

        // A buffered effect that is one strike of a multi-hit attack: every strike shares hitCount > 1 (see
        // CombatState.TryPlayerAttack), so the scheduler can space them by the multi-hit strike interval.
        private static bool IsMultiHitStrike(EffectResultEvent effect)
        {
            return effect.HitCount > 1;
        }

        // A buffered effect that presents status state (속박/기절/중독 적용/만료 등). Tagging it lets the
        // scheduler space several simultaneous status beats by the dedicated status-effect stagger so each
        // status reads one at a time instead of all on a single frame.
        private static bool IsStatusApply(EffectResultEvent effect)
        {
            return effect.Kind == EffectKind.StatusEffectApplied
                || effect.Kind == EffectKind.StatusEffectExpired;
        }

        // The collision damage/block a directional knockback deals when the target slams into an obstacle is
        // tagged with the "knockback.impact" source ref (see CombatState.ApplyDirectionalKnockback). Splitting
        // it out lets it replay after the knockback movement instead of at the original impact frame.
        private static bool IsKnockbackCollisionImpact(EffectResultEvent effect)
        {
            return string.Equals(effect.SourceRef, "knockback.impact", System.StringComparison.Ordinal);
        }

        private static void AppendMonsterMoveSteps(
            CombatTimeline timeline,
            IReadOnlyList<MonsterActionResolutionRecord> monsterRecords,
            ref ActionSpotlightEmitter spotlights)
        {
            if (monsterRecords == null)
            {
                return;
            }

            foreach (var record in monsterRecords)
            {
                // Monster movement is a pure monster visual ??never show it for a monster in fog.
                if (record.Moved && record.IsMonsterVisible)
                {
                    // No gap fallback: the movement phase has no MonsterActionGap to stand in for (§2.5), so
                    // an on-screen mover must leave the timeline exactly as it is today.
                    spotlights.TryEmit(timeline, ResolveMoveSpotlightCoord(record), record.MonsterId, fallbackGap: false);
                    AppendEnemyMove(timeline, record);
                }
            }
        }

        /// <summary>
        /// Where to frame a monster's move: the midpoint of its route, not its destination.
        ///
        /// ⚠ The destination is the wrong anchor and it is wrong systematically, not occasionally. Monsters
        /// move *toward* the player, so a move that starts off screen usually ends nearer the camera than it
        /// began — framing the destination asks "will the end of this walk be visible", which is the one part
        /// of it that most often already is. Measured 2026-07-31: a monster that walked r6→r4 behind the
        /// camera put its destination at viewport x=0.14, just inside the 0.10 margin, so the gate declined
        /// and the whole approach played off screen (plan §10.6).
        ///
        /// The midpoint is the cheapest anchor that represents the move as a whole: it is off screen whenever
        /// a meaningful part of the walk is, and it keeps both ends closer to frame than either endpoint would.
        /// </summary>
        private static HexCoord ResolveMoveSpotlightCoord(MonsterActionResolutionRecord record)
        {
            var path = record.MovePath;
            if (path != null && path.Count >= 2)
            {
                return path[path.Count / 2];
            }

            return MidpointCoord(record.BeforeCoord, record.AfterCoord);
        }

        /// <summary>
        /// Hex midpoint of two coordinates. Averages in cube space and rounds back, because averaging axial
        /// q/r independently and truncating can land off the line between the two tiles.
        /// </summary>
        private static HexCoord MidpointCoord(HexCoord a, HexCoord b)
        {
            var q = (a.Q + b.Q) / 2f;
            var r = (a.R + b.R) / 2f;
            var s = -q - r;

            var rq = System.Math.Round(q, System.MidpointRounding.AwayFromZero);
            var rr = System.Math.Round(r, System.MidpointRounding.AwayFromZero);
            var rs = System.Math.Round(s, System.MidpointRounding.AwayFromZero);

            // Cube rounding: drop whichever component moved furthest and rebuild it from the other two, so the
            // result always satisfies q + r + s == 0.
            var dq = System.Math.Abs(rq - q);
            var dr = System.Math.Abs(rr - r);
            var ds = System.Math.Abs(rs - s);

            if (dq > dr && dq > ds)
            {
                rq = -rr - rs;
            }
            else if (dr > ds)
            {
                rr = -rq - rs;
            }

            return new HexCoord((int)rq, (int)rr);
        }

        /// <summary>
        /// Emit a monster's movement: one step per tile when the rules captured its route (so it walks the
        /// path hex by hex), falling back to a single before→after hop when no route is available.
        /// </summary>
        private static void AppendEnemyMove(CombatTimeline timeline, MonsterActionResolutionRecord record)
        {
            // 도약(§28 W7)은 걷기 스텝이 아니라 단일 도약 비트로 낸다 — sink가 점프 애니메이션을
            // 얹고 한 번에 착지 지점으로 옮긴다(칸별 슬라이드로 쪼개면 걷기로 읽힌다).
            if (record.LeapedMove)
            {
                timeline.Append(CombatTimelineEventKind.EnemyLeapStep, record.MonsterId, record.BeforeCoord, record.AfterCoord);
                return;
            }

            var path = record.MovePath;
            if (path != null && path.Count >= 2)
            {
                timeline.AppendMovePath(CombatTimelineEventKind.EnemyMoveStep, record.MonsterId, path);
            }
            else
            {
                timeline.Append(CombatTimelineEventKind.EnemyMoveStep, record.MonsterId, record.BeforeCoord, record.AfterCoord);
            }
        }
    }
}


