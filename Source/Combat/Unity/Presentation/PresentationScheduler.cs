using System.Collections;
using SeoulPlayup.Combat.Runtime.Presentation;
using SeoulPlayup.Combat.Runtime.Timeline;

namespace SeoulPlayup.Combat.Unity.Presentation
{
    /// <summary>
    /// Replays an ordered <see cref="CombatTimeline"/> beat by beat against an
    /// <see cref="ICombatPresentationSink"/>. Movement and effect-stagger waits come from the global
    /// <see cref="CombatTimingProfile"/>; attack-shaped waits (wind-up, impact, hit-stop) are delegated to
    /// the sink via each beat's timing key so the per-attack timing table is honored without the scheduler
    /// knowing about it.
    ///
    /// This is the single queue that fixes the old "everything on one frame" problems: movement plays one
    /// tile at a time, and each impact effect is dispatched then spaced by the profile's effect-stagger so
    /// SFX/VFX/damage numbers read individually instead of collapsing into one. The scheduler holds no Unity
    /// references itself, which keeps its ordering logic deterministic and unit-testable.
    /// </summary>
    public sealed class PresentationScheduler
    {
        public IEnumerator Play(CombatTimeline timeline, CombatTimingProfile timing, ICombatPresentationSink sink)
        {
            if (timeline == null || timing == null || sink == null)
            {
                yield break;
            }

            foreach (var beat in timeline.Events)
            {
                // Stamped before the beat runs, so a beat's timestamp is when it started rather than when its
                // wait finished — that is what makes the gap to each channel's landing readable.
                CombatPresentationTrace.Record(CombatTraceChannel.Beat, beat.Kind.ToString(), DescribeBeat(beat));

                switch (beat.Kind)
                {
                    case CombatTimelineEventKind.PlayerMoveStep:
                        yield return sink.MovePlayerStep(beat.From.Value, beat.To.Value, timing.ScaleDuration(timing.PlayerMoveSeconds));
                        break;

                    case CombatTimelineEventKind.EnemyMoveStep:
                        yield return sink.MoveEnemyStep(beat.ActorId, beat.From.Value, beat.To.Value, timing.ScaleDuration(timing.EnemyMoveSeconds));
                        break;

                    case CombatTimelineEventKind.EnemyLeapStep:
                        yield return sink.LeapEnemyStep(beat.ActorId, beat.From.Value, beat.To.Value, timing.ScaleDuration(timing.EnemyMoveSeconds));
                        break;

                    case CombatTimelineEventKind.PlayerKnockbackStep:
                        yield return sink.KnockbackPlayerStep(beat.From.Value, beat.To.Value, timing.ScaleDuration(timing.PlayerMoveSeconds));
                        break;

                    case CombatTimelineEventKind.EnemyKnockbackStep:
                        yield return sink.KnockbackEnemyStep(beat.ActorId, beat.From.Value, beat.To.Value, timing.ScaleDuration(timing.EnemyMoveSeconds));
                        break;

                    case CombatTimelineEventKind.PlayerAttackStart:
                        sink.StartPlayerAttack(beat.From.Value, beat.To.Value);
                        yield return sink.AttackWindup(beat.TimingKey);
                        break;

                    case CombatTimelineEventKind.EnemyAttackStart:
                        sink.StartEnemyAttack(beat.ActorId, beat.Trigger, beat.From.Value, beat.To.Value);
                        yield return sink.AttackWindup(beat.TimingKey);
                        break;

                    case CombatTimelineEventKind.AttackImpact:
                        yield return sink.AttackImpactWait(beat.TimingKey, beat.ActorId);
                        sink.CommitImpact(beat.GroupId);
                        break;

                    case CombatTimelineEventKind.Effect:
                        sink.DispatchEffect(beat.EffectIndex);
                        if (beat.NoWaitAfterEffect)
                        {
                            break;
                        }

                        // Status beats (속박/기절 적용/만료 등) are spaced by the dedicated status-effect stagger so
                        // several simultaneous statuses read one at a time; area/blast targets by the AoE target interval so
                        // each target's flinch + number + hit SFX reads sequentially; strikes of a multi-hit attack
                        // by their own (larger) interval; all other effects keep the generic per-effect stagger.
                        yield return sink.Wait(beat.TextQueue
                            ? timing.FloatingTextQueueStaggerSeconds
                            : beat.StatusApply
                                ? timing.StatusEffectStaggerSeconds
                                : beat.AoeTarget
                                ? timing.AoeTargetIntervalSeconds
                                : beat.MultiHitStrike
                                    ? timing.MultiHitStrikeIntervalSeconds
                                    : timing.EffectStaggerSeconds);
                        break;

                    case CombatTimelineEventKind.ActorHit:
                        sink.ReactActorHit(beat.ActorId, beat.EffectIndex);
                        break;

                    case CombatTimelineEventKind.ActorKnockback:
                        sink.ReactActorKnockback(beat.ActorId, beat.EffectIndex);
                        break;

                    case CombatTimelineEventKind.ActorDeath:
                        sink.ReactActorDeath(beat.ActorId, beat.EffectIndex);
                        // Only the climactic/lethal kill holds for its death animation; per-target blast deaths
                        // (lethal:false) play their death anim but rely on the AoE target interval for spacing so
                        // a multi-kill blast doesn't freeze once per dead enemy.
                        if (beat.Lethal)
                        {
                            yield return sink.DeathHold(beat.TimingKey);
                        }
                        break;

                    case CombatTimelineEventKind.PlayerHit:
                        sink.ReactPlayerHit(beat.EffectIndex);
                        break;

                    case CombatTimelineEventKind.PlayerDeath:
                        sink.ReactPlayerDeath(beat.EffectIndex);
                        yield return sink.DeathHold(beat.TimingKey);
                        break;

                    case CombatTimelineEventKind.HitStop:
                        yield return sink.HitStop(beat.TimingKey, beat.ActorId, beat.Lethal);
                        break;

                    case CombatTimelineEventKind.MonsterActionGap:
                        yield return sink.MonsterActionGap();
                        break;

                    case CombatTimelineEventKind.ActorSpotlight:
                        // The gate is the sink's: it alone knows where the camera is pointing. The scheduler
                        // only forwards the framing point and whether this beat stands in for a gap.
                        yield return sink.FocusOnAction(
                            beat.To ?? default,
                            beat.ActorId,
                            beat.FallbackGap,
                            beat.CoveredBeats,
                            beat.CoveredSeconds,
                            beat.CoveredLeadInSeconds);
                        break;

                    case CombatTimelineEventKind.MonsterPhaseCompleteGap:
                        yield return sink.Wait(timing.PostMonsterAttackPauseSeconds);
                        break;

                    case CombatTimelineEventKind.ActionFocusRelease:
                        // Same division of labour as ActorSpotlight: the scheduler only says WHEN the camera
                        // must be back, the sink decides what that costs (and usually nothing).
                        yield return sink.ActionFocusRelease();
                        break;

                    case CombatTimelineEventKind.OverallTurnStart:
                        yield return sink.OverallTurnStart();
                        break;

                    case CombatTimelineEventKind.PlayerTurnStart:
                        yield return sink.PlayerTurnStart();
                        break;

                    case CombatTimelineEventKind.AmbushAlert:
                        // 기습 경고는 가장 먼저 보여야 하므로, 이후 피격/이펙트 비트보다 한 박자 앞서도록 짧게 텀을 둔다.
                        sink.ShowAmbushAlert();
                        yield return sink.Wait(timing.FloatingTextQueueStaggerSeconds);
                        break;

                    case CombatTimelineEventKind.BossPhaseTransition:
                        // 규칙 전환은 이미 끝났다. sink가 카메라 펀치인·셰이크·히트스톱·아우라 전환을 연주한다.
                        yield return sink.BossPhaseTransition(beat.ActorId, beat.To);
                        break;

                    case CombatTimelineEventKind.BossPropVolleyCast:
                        // 살포 캐스트는 그 턴 보스 행동의 얼굴이다(살포한 턴엔 공격이 없다).
                        yield return sink.BossPropVolleyCast(beat.ActorId, beat.To);
                        break;

                    case CombatTimelineEventKind.BossPropPlaced:
                        yield return sink.BossPropPlaced(beat.ActorId, beat.To);
                        break;

                    case CombatTimelineEventKind.BossPropAbsorb:
                        // From = 기물이 서 있던 칸(폭발 원점), To = 힘이 빨려 들어갈 보스 좌표.
                        yield return sink.BossPropAbsorb(beat.ActorId, beat.From, beat.To);
                        break;

                    case CombatTimelineEventKind.BossScrapChainHit:
                        // From = 보스 좌표(선의 시작), To = 맞은 플레이어 좌표(빗나가면 null). 가닥은 sink가 투영에서 읽는다.
                        yield return sink.BossScrapChainHit(beat.ActorId, beat.From, beat.To);
                        break;
                }
            }
        }

        // Only the fields that identify a beat when reading a trace: who it belongs to, which buffered effect
        // it releases, and which timing key resolved its duration.
        private static string DescribeBeat(CombatTimelineEvent beat)
        {
            var parts = new System.Collections.Generic.List<string>(4);
            if (!string.IsNullOrEmpty(beat.ActorId))
            {
                parts.Add(beat.ActorId);
            }

            if (beat.EffectIndex >= 0)
            {
                parts.Add($"effect#{beat.EffectIndex}");
            }

            if (!string.IsNullOrEmpty(beat.TimingKey))
            {
                parts.Add($"timing={beat.TimingKey}");
            }

            if (beat.Lethal)
            {
                parts.Add("lethal");
            }

            return string.Join(" ", parts);
        }
    }
}
