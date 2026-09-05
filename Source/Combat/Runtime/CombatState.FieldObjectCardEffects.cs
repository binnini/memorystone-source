using System.Collections.Generic;
using System;
using System.Linq;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed partial class CombatState
    {
        /// <summary>
        /// 장판(설치물)의 턴 효과. 카드가 아니라 <see cref="FieldObjectKind"/>로 디스패치되므로 카드 클래스
        /// (<see cref="Cards.CardBehavior"/>) 밖에 남는 유일한 핸들러 맵이다 — 장판은 카드가 떠난 뒤에도 살아 있다.
        /// </summary>
        private interface IFieldObjectEffectHandler
        {
            FieldObjectKind Kind { get; }
            void ApplyTurnEffect(CombatState state, FieldObject fieldObject);
        }

        private static readonly IReadOnlyDictionary<FieldObjectKind, IFieldObjectEffectHandler> FieldObjectHandlers = new IFieldObjectEffectHandler[]
        {
            new FogRevealFieldObjectEffectHandler(),
            new FieldDamageFieldObjectEffectHandler(),
            new ConditionalHealFieldObjectEffectHandler(),
            new MassImmobilizeFieldObjectEffectHandler(),
            new LifestealDamageFieldObjectEffectHandler(),
            new StatusZoneFieldObjectEffectHandler()
        }.ToDictionary(handler => handler.Kind);

        private void ApplyFieldObjectTurnEffect(FieldObject fieldObject)
        {
            if (!FieldObjectHandlers.TryGetValue(fieldObject.Kind, out var handler))
            {
                throw new NotSupportedException($"Field object kind '{fieldObject.Kind}' is not supported by CombatState.");
            }

            handler.ApplyTurnEffect(this, fieldObject);
        }

        /// <summary>샌드박스 readout용: 설치 카드가 선언한 kind에 핸들러가 있는가(없으면 해결 시점에 던진다).</summary>
        internal static bool IsFieldObjectKindRegistered(FieldObjectKind kind)
        {
            return FieldObjectHandlers.ContainsKey(kind);
        }

        private sealed class FogRevealFieldObjectEffectHandler : IFieldObjectEffectHandler
        {
            public FieldObjectKind Kind => FieldObjectKind.FogReveal;

            public void ApplyTurnEffect(CombatState state, FieldObject fieldObject)
            {
                // Reveal is handled centrally by CombatState.RefreshPlayerVision (union of active
                // field objects), so the footprint stays Revealed only while this object lives.
                state.RaiseEffect(EffectKind.FogReveal, fieldObject.Position, fieldObject.Radius, fieldObject.Radius, "field", CardEffectRefs.FieldFogReveal, sourceCardId: fieldObject.VisualRef);
            }
        }

        private sealed class FieldDamageFieldObjectEffectHandler : IFieldObjectEffectHandler
        {
            public FieldObjectKind Kind => FieldObjectKind.FieldDamage;

            public void ApplyTurnEffect(CombatState state, FieldObject fieldObject)
            {
                // Field activation VFX is authored on the field center. Per-target hit/flinch/number
                // presentation is raised separately below for each affected unit.
                state.RaiseEffect(EffectKind.Damage, fieldObject.Position, fieldObject.Radius, 0, "field", CardEffectRefs.FieldDamage, sourceCardId: fieldObject.VisualRef);

                // One damage effect per monster standing in the field (its own id + tile) so the timeline presents
                // 폭탄 투하(F01)/장판 damage as a staggered per-target burst — each monster's flinch/death + number +
                // hit SFX one at a time — at placement and on each tick, instead of a single number at the field
                // center. The rules apply the damage as each effect is raised.
                var presentationGroupId = state.CreatePresentationGroupId(
                    PlayerUnitId,
                    string.IsNullOrEmpty(fieldObject.VisualRef) ? CardEffectRefs.FieldDamage : fieldObject.VisualRef);

                // 콩콩탄탄(F05) authors hitCount=2: one tick lands its damage twice. Every other field leaves the
                // column empty (HitsPerTick=1) and behaves exactly as before. The split is deliberately a
                // presentation/타격감 change, not a balance one — block is a pool, so 2×2 and 1×4 punch through a
                // shield identically (DEC-2026-07-24-01, superseding DEC-2026-07-23-06).
                for (var hit = 0; hit < fieldObject.HitsPerTick; hit++)
                {
                    if (!state.Player.IsDead
                        && !string.Equals(fieldObject.SourceUnitId, PlayerUnitId, System.StringComparison.Ordinal)
                        && fieldObject.Contains(state.PlayerCoord))
                    {
                        state.ApplyFieldDamageToPlayer(fieldObject);
                    }

                    foreach (var monster in state.monsters.Where(monster => !monster.Combatant.IsDead && state.DoesFieldObjectOverlapMonster(fieldObject, monster)))
                    {
                        state.DamageMonster(monster, fieldObject.Value);
                        state.RaiseEffect(
                            EffectKind.Damage,
                            monster.Coord,
                            0,
                            fieldObject.Value,
                            monster.Id,
                            CardEffectRefs.FieldDamage,
                            sourceCardId: fieldObject.VisualRef,
                            sourceUnitId: PlayerUnitId,
                            sourceActorKind: "field",
                            targetActorKind: "monster",
                            hitIndex: hit,
                            hitCount: fieldObject.HitsPerTick,
                            presentationGroupId: presentationGroupId,
                            // Stagger follow-up hits like S01/S03's per-target burst so two hits read as two,
                            // not as one doubled number. The lead hit keeps its immediate timing.
                            delaySeconds: hit == 0 ? 0f : 0.12f + (0.06f * hit));
                    }
                }
            }
        }

        private sealed class LifestealDamageFieldObjectEffectHandler : IFieldObjectEffectHandler
        {
            public FieldObjectKind Kind => FieldObjectKind.LifestealDamage;

            public void ApplyTurnEffect(CombatState state, FieldObject fieldObject)
            {
                // 흡수진(F04): a damage field that returns what it actually dealt to whoever placed it.
                // "피해 준 만큼" is the damage that landed, not the field's authored value — a monster on
                // 1 HP heals 1, and a corpse in the footprint heals nothing.
                // Ticks once per turn on purpose: unlike FieldDamage this kind ignores FieldObject.HitsPerTick,
                // because a multi-hit drain would need its own per-hit heal pacing. F04 authors no hitCount.
                state.RaiseEffect(EffectKind.Damage, fieldObject.Position, fieldObject.Radius, 0, "field", CardEffectRefs.FieldLifesteal, sourceCardId: fieldObject.VisualRef);

                if (!state.Player.IsDead
                    && !string.Equals(fieldObject.SourceUnitId, PlayerUnitId, System.StringComparison.Ordinal)
                    && fieldObject.Contains(state.PlayerCoord))
                {
                    state.ApplyFieldDamageToPlayer(fieldObject);
                }

                var presentationGroupId = state.CreatePresentationGroupId(
                    PlayerUnitId,
                    string.IsNullOrEmpty(fieldObject.VisualRef) ? CardEffectRefs.FieldLifesteal : fieldObject.VisualRef);
                var drained = 0;
                foreach (var monster in state.monsters.Where(monster => !monster.Combatant.IsDead && state.DoesFieldObjectOverlapMonster(fieldObject, monster)))
                {
                    drained += state.DamageMonster(monster, fieldObject.Value);
                    state.RaiseEffect(
                        EffectKind.Damage,
                        monster.Coord,
                        0,
                        fieldObject.Value,
                        monster.Id,
                        CardEffectRefs.FieldLifesteal,
                        sourceCardId: fieldObject.VisualRef,
                        sourceUnitId: PlayerUnitId,
                        sourceActorKind: "field",
                        targetActorKind: "monster",
                        hitIndex: 0,
                        hitCount: 1,
                        presentationGroupId: presentationGroupId);
                }

                // Only the caster drains. Fields are player-placed today, but the guard keeps a future
                // monster-placed lifesteal field from healing the player it just hit.
                if (drained <= 0 || !string.Equals(fieldObject.SourceUnitId, PlayerUnitId, System.StringComparison.Ordinal))
                {
                    return;
                }

                var healed = state.Player.Heal(drained);
                if (healed > 0)
                {
                    state.RaiseEffect(EffectKind.Heal, state.PlayerCoord, 0, healed, "player", CardEffectRefs.FieldLifesteal, sourceCardId: fieldObject.VisualRef);
                }
            }
        }

        private sealed class ConditionalHealFieldObjectEffectHandler : IFieldObjectEffectHandler
        {
            public FieldObjectKind Kind => FieldObjectKind.ConditionalHeal;

            public void ApplyTurnEffect(CombatState state, FieldObject fieldObject)
            {
                // Field activation VFX is authored on the field center. The real heal number, if any,
                // is raised on the player so floating text stays anchored to the healed actor.
                state.RaiseEffect(EffectKind.Heal, fieldObject.Position, fieldObject.Radius, 0, "field", CardEffectRefs.FieldHeal, sourceCardId: fieldObject.VisualRef);

                // 신성한 램프(F02): heal the player only when they actually stand in the field, and raise the heal
                // on the player (not the field center) with the real healed amount so the "+N" floating text lands
                // on the player. A no-op heal (player outside the field, or already at full HP) shows nothing.
                if (!state.Player.IsDead && fieldObject.Contains(state.PlayerCoord))
                {
                    var healed = state.Player.Heal(fieldObject.Value);
                    if (healed > 0)
                    {
                        state.RaiseEffect(EffectKind.Heal, state.PlayerCoord, 0, healed, "player", CardEffectRefs.FieldHeal, sourceCardId: fieldObject.VisualRef);
                    }
                }
            }
        }

        /// <summary>
        /// 상태이상 지대(요괴 §4-3 · 두억시니). 몬스터가 공격으로 남기는 장판이고, <b>밟고 선
        /// 플레이어에게만</b> 저작된 상태이상을 건다.
        ///
        /// <para>🔴 다른 장판 종류와 달리 이 핸들러는 <see cref="CombatState.monsters"/>를 아예 보지
        /// 않는다. 몬스터가 자기 지대에 상하면 "잡는 위치를 고른다"가 아니라 "적이 자기 함정에 빠진다"가
        /// 되어 위협이 스스로 풀리고, 무엇보다 AI가 지대를 피하려 드는 순간 통행 불가 갈래를 폐기한
        /// 이유(길찾기·배치·도달성 오염)가 그대로 돌아온다. 지대는 판정 자체를 지나가지 않는 것이 계약이다.</para>
        ///
        /// <para>플레이어가 깐 지대는 없다(저작 표면이 몬스터 패턴뿐이다). 그래도 소유자 검사를 남긴 것은
        /// 장판 배관의 공통 규약이며, 언젠가 플레이어가 같은 kind를 쓰게 되어도 자기 지대에 자기가 걸리지
        /// 않게 한다.</para>
        /// </summary>
        private sealed class StatusZoneFieldObjectEffectHandler : IFieldObjectEffectHandler
        {
            public FieldObjectKind Kind => FieldObjectKind.StatusZone;

            /// <summary>
            /// 🔴 <b>여기서는 아무것도 하지 않는다</b>(2026-09-05 실시간 전환). 지대의 상태이상은
            /// 이제 턴 틱이 아니라 <b>서 있는가</b>가 정하는 파생 상태이고, 그 계산은
            /// <c>CombatState.RefreshStandingStatusZoneEffects</c> 한 곳이 소유한다.
            ///
            /// <para>종전에는 이 자리에서 ①장판마다 배치 VFX를 다시 올리고(7칸이면 일곱 박자)
            /// ②밟고 있으면 2턴짜리 상태이상을 새로 걸었다. ①은 「순차 재생」으로 보였고
            /// ②는 「이동 카드로 들어가도 턴이 끝나야 걸리고, 벗어나도 2턴 남는다」였다.</para>
            ///
            /// <para>핸들러 자체를 지우지 않는 이유: 등록 표(<c>CardEffects</c>)가 kind마다 핸들러
            /// 하나를 요구하고, 지대도 <b>턴 수명은 그대로 센다</b>(반환 없이 Tick만 도는 것이 정답).</para>
            /// </summary>
            public void ApplyTurnEffect(CombatState state, FieldObject fieldObject)
            {
            }
        }

        private sealed class MassImmobilizeFieldObjectEffectHandler : IFieldObjectEffectHandler
        {
            public FieldObjectKind Kind => FieldObjectKind.MassImmobilize;

            public void ApplyTurnEffect(CombatState state, FieldObject fieldObject)
            {
                // 섬광 장판(F03): 풋프린트 안 살아있는 모든 몬스터에 실제 속박(Immobilize) ActiveEffect를 부여/갱신한다.
                // 위치 기반 차단이나 무효한 TurnPlan 리셋 대신 A11 "얼어버려라"와 동일한 상태 기반 처리를 써야
                // ① 광역·일관 적용 ② 몬스터별 속박 아이콘 ③ 정상 지속시간이 보장된다. 속박은 이동만 막고 공격은
                // 허용한다(IsMonsterMovementBlocked만 Immobilize를 검사). 배치 즉시(같은 턴)와 매 필드 틱마다 갱신되어
                // 장판이 살아있는 동안 풋프린트 안 몬스터에 유지된다.
                state.RaiseEffect(
                    EffectKind.StatusEffectApplied,
                    fieldObject.Position,
                    fieldObject.Radius,
                    0,
                    "field",
                    CardEffectRefs.FieldImmobilizeFlashbang,
                    sourceCardId: fieldObject.VisualRef,
                    statusKind: StatusEffectKind.Immobilize);

                var hitIndex = 0;
                var targets = state.monsters.Where(monster => !monster.Combatant.IsDead && state.DoesFieldObjectOverlapMonster(fieldObject, monster)).ToList();
                foreach (var monster in targets)
                {
                    state.ApplyOrRefreshFieldImmobilize(monster, CardEffectRefs.FieldImmobilizeFlashbang);
                    state.RaiseEffect(
                        EffectKind.StatusEffectApplied,
                        monster.Coord,
                        0,
                        FieldImmobilizeRefreshTurns,
                        monster.Id,
                        CardEffectRefs.FieldImmobilizeFlashbang,
                        sourceCardId: fieldObject.VisualRef,
                        hitIndex: hitIndex,
                        hitCount: targets.Count,
                        statusKind: StatusEffectKind.Immobilize,
                        delaySeconds: 0.12f + (0.06f * hitIndex));
                    hitIndex++;
                }
            }
        }
        // ── 필드 오브젝트 피해·속박 갱신 규칙(4-B · 2026-09-04): 옛 CombatState.MonsterAi.cs에서 본문 무변경 이동. 호출자는 이 파일뿐이었다.

        private void ApplyFieldDamageToPlayer(FieldObject fieldObject)
        {
            var incoming = Math.Max(0, fieldObject.Value);
            // Reflect (반사, 양날의 방패): 저작된 퍼센트만큼 경감하고, 경감분을 그대로 피해원에게 되돌린다.
            // 예) 50%·피해 4 → 플레이어 2 피격 + 피해원 2 반사. ⚠️예전엔 퍼센트를 켜짐/꺼짐 게이트로만
            // 쓰고 ÷2가 하드코딩돼 연마(D03+ 75%)가 무효였다(WS-I I-05, DEC-2026-08-19-08).
            var reflected = ComputeReflectedDamage(incoming);
            var incomingToPlayer = incoming - reflected;

            var blockBefore = Player.Block;
            var applied = Player.ApplyDamage(incomingToPlayer);
            if (applied > 0)
            {
                RaiseEffect(EffectKind.Damage, PlayerCoord, 0, applied, "player", CardEffectRefs.FieldDamage);
            }
            else if (incoming > 0 && Player.Block < blockBefore)
            {
                // Field damage fully absorbed by Block: surface "방어!".
                RaiseEffect(EffectKind.DamageBlocked, PlayerCoord, 0, 0, "player", CardEffectRefs.FieldDamage);
            }

            if (reflected <= 0)
            {
                return;
            }

            if (string.Equals(fieldObject.SourceUnitId, PlayerUnitId, StringComparison.Ordinal))
            {
                var selfApplied = Player.ApplyDamage(reflected);
                RaiseEffect(EffectKind.ReflectDamage, PlayerCoord, 0, selfApplied, PlayerUnitId, CardEffectRefs.DefendHalfReflect);
                return;
            }

            var sourceMonster = monsters.FirstOrDefault(monster => string.Equals(monster.Id, fieldObject.SourceUnitId, StringComparison.Ordinal));
            if (sourceMonster != null && !sourceMonster.Combatant.IsDead)
            {
                DamageMonster(sourceMonster, reflected);
                RaiseEffect(EffectKind.ReflectDamage, sourceMonster.Coord, 0, reflected, sourceMonster.Id, CardEffectRefs.DefendHalfReflect);
                if (sourceMonster.Combatant.IsDead)
                {
                    ResetDeadMonsterToPatrolIntent(sourceMonster);
                    UpdateOccupancy();
                }
            }
        }

            // Field-based effects replace existing matching effects in place to preserve stable ordering.
            // Missing entries are appended so newly applied field states affect future planning immediately.
        // Duration (turns) a field MassImmobilize refreshes a monster's 속박 to while it stands in the field.
        private const int FieldImmobilizeRefreshTurns = 2;

        private void ApplyOrRefreshFieldImmobilize(MonsterRuntime monster, string sourceRef)
        {
            if (monster == null)
            {
                return;
            }

            var intent = CaptureMonsterIntentForCancel(monster);
            var refreshed = new ActiveEffect(EffectType.Duration, StatusEffectKind.Immobilize, monster.Id, FieldImmobilizeRefreshTurns, 0, sourceRef);
            if (!activeEffects.TryReplaceInPlaceFromSameSource(refreshed))
            {
                activeEffects.Add(refreshed);
            }

            ApplyControlStatusConstraintToPlan(monster);
            EmitMonsterIntentCancelText(monster, intent.WasMoving, intent.WasAttacking, StatusEffectKind.Immobilize);
        }
    }
}
