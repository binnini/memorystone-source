using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    public sealed class EffectRuntime
    {
        private readonly List<EffectResultEvent> resultEvents = new List<EffectResultEvent>();
        private readonly List<ActiveEffect> activeEffects = new List<ActiveEffect>();
        private readonly FieldObjectRegistry fieldObjects;

        public EffectRuntime(FieldObjectRegistry fieldObjects = null)
        {
            this.fieldObjects = fieldObjects ?? new FieldObjectRegistry();
        }

        public event Action<EffectResultEvent> EffectResolved;

        public IReadOnlyList<EffectResultEvent> ResultEvents => resultEvents;
        public IReadOnlyList<ActiveEffect> ActiveEffects => activeEffects;
        public FieldObjectRegistry FieldObjects => fieldObjects;

        public EffectResultEvent Apply(
            EffectDefinition definition,
            CombatantState target = null,
            CombatantState source = null,
            HexVisibilityRuntime visibility = null,
            HexCoord center = default,
            int contextAmount = 0)
        {
            switch (definition.Type)
            {
                case EffectType.Instant:
                    return ApplyInstant(definition, target, source, visibility, center, contextAmount);
                case EffectType.Duration:
                    return ApplyDuration(definition, RequireTarget(target, definition.Kind));
                case EffectType.FieldObject:
                    return ApplyFieldObject(definition, visibility, center);
                default:
                    throw new NotSupportedException($"Effect type '{definition.Type}' is not supported by the MVP effect runtime.");
            }
        }

        /// <summary>
        /// <paramref name="center"/> is the tile the damage happened on, for callers that know it. Damage
        /// dealt by a card already carries its coordinate through the effect definition, but damage a *field*
        /// deals on its own at the turn boundary has no card behind it — so unless the caller supplies it, the
        /// event says only "m1 took 2" with no place attached, and anything downstream that needs to know
        /// where to look cannot. That is what left field ticks unframed by the action camera while its sibling
        /// field.immobilize, which does pass a centre, worked (plan §10.8).
        /// </summary>
        public EffectResultEvent ApplyDamage(
            CombatantState target, int amount, string sourceRef = "", HexCoord? center = null)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var clampedAmount = Math.Max(0, amount);
            var previousHp = target.Hp;
            var appliedDamage = target.ApplyDamage(clampedAmount);
            return Record(new EffectResultEvent(
                EffectKind.Damage,
                target.Id,
                clampedAmount,
                appliedDamage,
                previousHp,
                target.Hp,
                center: center,
                sourceRef: sourceRef));
        }

        public EffectResultEvent ApplyBlock(CombatantState target, int amount, string sourceRef = "")
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var clampedAmount = Math.Max(0, amount);
            var rupture = SumActiveEffectAmountByValueMode(target, StatusEffectValueMode.BlockGainPenalty);
            var effectiveAmount = Math.Max(0, clampedAmount - rupture);
            var previousBlock = target.Block;
            target.AddBlock(effectiveAmount);
            return Record(new EffectResultEvent(
                EffectKind.Block,
                target.Id,
                clampedAmount,
                target.Block - previousBlock,
                previousBlock,
                target.Block,
                sourceRef: sourceRef));
        }

        public EffectResultEvent ApplyHeal(CombatantState target, int amount, string sourceRef = "")
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var clampedAmount = Math.Max(0, amount);
            var previousHp = target.Hp;
            var healed = target.Heal(clampedAmount);
            return Record(new EffectResultEvent(
                EffectKind.Heal,
                target.Id,
                clampedAmount,
                healed,
                previousHp,
                target.Hp,
                sourceRef: sourceRef));
        }

        public EffectResultEvent ApplyReflect(CombatantState source, int receivedDamage, int percent, string sourceRef = "")
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var clampedReceivedDamage = Math.Max(0, receivedDamage);
            var clampedPercent = Math.Max(0, percent);
            var reflectedDamage = clampedReceivedDamage * clampedPercent / 100;
            var previousHp = source.Hp;
            var appliedDamage = source.ApplyDamage(reflectedDamage);
            return Record(new EffectResultEvent(
                EffectKind.ReflectDamage,
                source.Id,
                clampedPercent,
                appliedDamage,
                previousHp,
                source.Hp,
                sourceRef: sourceRef));
        }

        public EffectResultEvent ApplyFogReveal(HexVisibilityRuntime visibility, HexCoord center, int radius, string sourceRef = "")
        {
            if (visibility == null)
            {
                throw new ArgumentNullException(nameof(visibility));
            }

            var clampedRadius = Math.Max(0, radius);
            var revealed = 0;
            foreach (var cell in visibility.Map.AllCells.Where(cell => center.DistanceTo(cell.Coord) <= clampedRadius))
            {
                var before = visibility.GetVisibility(cell.Coord);
                visibility.Reveal(cell.Coord);
                if (before != HexCellVisibility.Revealed && visibility.GetVisibility(cell.Coord) == HexCellVisibility.Revealed)
                {
                    revealed++;
                }
            }

            return Record(new EffectResultEvent(
                EffectKind.FogReveal,
                amount: clampedRadius,
                appliedAmount: revealed,
                center: center,
                radius: clampedRadius,
                sourceRef: sourceRef));
        }

        public EffectResultEvent ApplyDuration(EffectDefinition definition, CombatantState target)
        {
            if (definition.Type != EffectType.Duration)
            {
                throw new ArgumentException("Only Duration definitions can be registered as active effects.", nameof(definition));
            }

            if (!definition.StatusKind.HasValue || !IsSupportedDurationKind(definition.StatusKind.Value))
            {
                throw new NotSupportedException($"Duration status effect kind '{definition.StatusKind}' is not supported by the MVP effect runtime.");
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var activeEffect = AddDurationStatusEffect(
                definition.StatusKind.Value,
                target.Id,
                definition.DurationTurns,
                definition.Amount,
                definition.SourceRef);
            return Record(new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                target.Id,
                definition.Amount,
                activeEffect.RemainingTurns,
                0,
                activeEffect.RemainingTurns,
                sourceRef: definition.SourceRef,
                statusKind: definition.StatusKind));
        }


        public EffectResultEvent ApplyFieldObject(EffectDefinition definition, HexVisibilityRuntime visibility, HexCoord center)
        {
            if (definition.Type != EffectType.FieldObject)
            {
                throw new ArgumentException("Only FieldObject definitions can be registered as field objects.", nameof(definition));
            }

            var kind = ToFieldObjectKind(definition);
            if (kind == FieldObjectKind.FogReveal && visibility == null)
            {
                throw new ArgumentNullException(nameof(visibility));
            }

            var fieldObject = new FieldObject(center, definition.Radius, definition.DurationTurns, kind, definition.Amount);
            fieldObjects.Add(fieldObject);

            if (kind == FieldObjectKind.FogReveal)
            {
                var result = ApplyFogReveal(visibility, center, fieldObject.Radius, definition.SourceRef);
                return result;
            }

            return Record(new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                amount: fieldObject.Value,
                appliedAmount: fieldObject.RemainingTurns,
                center: center,
                radius: fieldObject.Radius,
                sourceRef: definition.SourceRef,
                statusKind: definition.StatusKind));
        }

        public int TickFieldObjects(HexVisibilityRuntime visibility = null, IEnumerable<FieldObjectTarget> targets = null)
        {
            var targetList = targets == null ? new List<FieldObjectTarget>() : targets.ToList();
            return fieldObjects.Tick(fieldObject =>
            {
                TickFieldObject(fieldObject, visibility, targetList);
                return fieldObject.Tick();
            });
        }

        public void ClearFieldObjects()
        {
            fieldObjects.Clear();
        }

        public bool HasActiveEffect(CombatantState target, StatusEffectKind kind)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return activeEffects.Any(effect => effect.TargetUnitId == target.Id && effect.Kind == kind && !effect.IsExpired);
        }

        public int SumActiveEffectAmount(CombatantState target, StatusEffectKind kind)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            var total = 0;
            foreach (var effect in activeEffects)
            {
                if (effect.TargetUnitId == target.Id && effect.Kind == kind && !effect.IsExpired)
                    total += effect.Amount;
            }
            return total;
        }

        /// <summary>
        /// <c>CombatState.SumActiveEffectAmountByValueMode</c>의 순수 계층 짝. 소비 지점이 종류 이름이
        /// 아니라 값 축으로 묻게 해서, 같은 축의 상태이상을 추가할 때 소비 코드가 안 바뀌게 한다(D-6).
        /// </summary>
        public int SumActiveEffectAmountByValueMode(CombatantState target, StatusEffectValueMode valueMode)
        {
            if (target == null)
            {
                return 0;
            }

            var total = 0;
            foreach (var effect in activeEffects)
            {
                if (effect.TargetUnitId == target.Id
                    && !effect.IsExpired
                    && StatusEffectInfo.ValueMode(effect.Kind) == valueMode)
                {
                    total += Math.Max(0, effect.Amount);
                }
            }

            return total;
        }

        private ActiveEffect AddDurationStatusEffect(StatusEffectKind kind, string targetUnitId, int turns, int amount, string sourceRef)
        {
            var clampedTurns = Math.Max(1, turns);
            var clampedAmount = Math.Max(0, amount);
            if (!StatusEffectInfo.UsesDurationOnlyStacking(kind) || string.IsNullOrEmpty(targetUnitId))
            {
                var added = new ActiveEffect(EffectType.Duration, kind, targetUnitId, clampedTurns, clampedAmount, sourceRef, skipNextTick: true);
                activeEffects.Add(added);
                return added;
            }

            var existingIndex = activeEffects.FindIndex(effect =>
                effect.TargetUnitId == targetUnitId &&
                effect.Kind == kind &&
                !effect.IsExpired);
            if (existingIndex < 0)
            {
                var added = new ActiveEffect(EffectType.Duration, kind, targetUnitId, clampedTurns, clampedAmount, sourceRef, skipNextTick: true);
                activeEffects.Add(added);
                return added;
            }

            var existing = activeEffects[existingIndex];
            var mergedTurns = Math.Max(existing.RemainingTurns, clampedTurns);
            var mergedAmount = StatusEffectInfo.UsesAdditiveAmountStacking(kind)
                ? existing.Amount + clampedAmount
                : Math.Max(existing.Amount, clampedAmount);
            var merged = new ActiveEffect(
                EffectType.Duration,
                kind,
                targetUnitId,
                mergedTurns,
                mergedAmount,
                string.IsNullOrWhiteSpace(existing.SourceRef) ? sourceRef : existing.SourceRef,
                skipNextTick: true);
            activeEffects[existingIndex] = merged;
            return merged;
        }


        public int TickDurationEffects()
        {
            var expiredCount = 0;
            for (var i = activeEffects.Count - 1; i >= 0; i--)
            {
                    var ticked = activeEffects[i].Tick();
                if (ticked.IsExpired)
                {
                    activeEffects.RemoveAt(i);
                    expiredCount++;
                }
                else
                {
                    activeEffects[i] = ticked;
                }
            }

            return expiredCount;
        }

        public void ClearResults()
        {
            resultEvents.Clear();
        }

        public void ClearActiveEffects()
        {
            activeEffects.Clear();
        }


        private void TickFieldObject(FieldObject fieldObject, HexVisibilityRuntime visibility, IReadOnlyList<FieldObjectTarget> targets)
        {
            switch (fieldObject.Kind)
            {
                case FieldObjectKind.FogReveal:
                    if (visibility == null)
                    {
                        throw new ArgumentNullException(nameof(visibility));
                    }

                    ApplyFogReveal(visibility, fieldObject.Position, fieldObject.Radius, "field.fog-reveal");
                    return;
                case FieldObjectKind.FieldDamage:
                    foreach (var target in targets.Where(target => fieldObject.Contains(target.Position)))
                    {
                        ApplyDamage(target.Combatant, fieldObject.Value, "field.damage", target.Position);
                    }

                    return;
                case FieldObjectKind.ConditionalHeal:
                    foreach (var target in targets.Where(target => target.Kind == FieldObjectTargetKind.Player && fieldObject.Contains(target.Position)))
                    {
                        ApplyHeal(target.Combatant, fieldObject.Value, "field.heal");
                    }

                    return;
                case FieldObjectKind.MassImmobilize:
                    foreach (var target in targets.Where(target => target.Kind == FieldObjectTargetKind.Monster && fieldObject.Contains(target.Position)))
                    {
                        var activeEffect = new ActiveEffect(EffectType.Duration, StatusEffectKind.Immobilize, target.Combatant.Id, 1, fieldObject.Value, "field.immobilize", skipNextTick: true);
                        activeEffects.Add(activeEffect);
                        Record(new EffectResultEvent(
                            EffectKind.StatusEffectApplied,
                            target.Combatant.Id,
                            fieldObject.Value,
                            activeEffect.RemainingTurns,
                            0,
                            activeEffect.RemainingTurns,
                            center: target.Position,
                            radius: fieldObject.Radius,
                            sourceRef: "field.immobilize",
                            statusKind: StatusEffectKind.Immobilize));
                    }

                    return;
                default:
                    throw new NotSupportedException($"Field object kind '{fieldObject.Kind}' is not supported by the MVP effect runtime.");
            }
        }

        private static FieldObjectKind ToFieldObjectKind(EffectDefinition definition)
        {
            switch (definition.Kind)
            {
                case EffectKind.FogReveal:
                    return FieldObjectKind.FogReveal;
                case EffectKind.Damage:
                    return FieldObjectKind.FieldDamage;
                case EffectKind.Heal:
                    return FieldObjectKind.ConditionalHeal;
                case EffectKind.StatusEffectApplied:
                    if (definition.StatusKind == StatusEffectKind.Immobilize || definition.StatusKind == StatusEffectKind.Stun)
                    {
                        return FieldObjectKind.MassImmobilize;
                    }
                    break;
            }

            throw new NotSupportedException($"FieldObject effect kind {definition.Kind}/{definition.StatusKind} is not supported by the MVP effect runtime.");
        }

        private EffectResultEvent ApplyInstant(
            EffectDefinition definition,
            CombatantState target,
            CombatantState source,
            HexVisibilityRuntime visibility,
            HexCoord center,
            int contextAmount)
        {
            switch (definition.Kind)
            {
                case EffectKind.Damage:
                    return ApplyDamage(RequireTarget(target, definition.Kind), definition.Amount, definition.SourceRef);
                case EffectKind.Block:
                    return ApplyBlock(RequireTarget(target, definition.Kind), definition.Amount, definition.SourceRef);
                case EffectKind.Heal:
                    return ApplyHeal(RequireTarget(target, definition.Kind), definition.Amount, definition.SourceRef);
                case EffectKind.ReflectDamage:
                    return ApplyReflect(RequireSource(source, definition.Kind), contextAmount, definition.Amount, definition.SourceRef);
                case EffectKind.FogReveal:
                    if (visibility == null)
                    {
                        throw new ArgumentNullException(nameof(visibility));
                    }

                    return ApplyFogReveal(visibility, center, definition.Radius, definition.SourceRef);
                default:
                    throw new NotSupportedException($"Instant effect kind '{definition.Kind}' is not supported by the MVP effect runtime.");
            }
        }

        private static bool IsSupportedDurationKind(StatusEffectKind kind)
        {
            return kind == StatusEffectKind.Immobilize
                || kind == StatusEffectKind.Agility
                || kind == StatusEffectKind.Poison
                || kind == StatusEffectKind.Stun
                || kind == StatusEffectKind.Slow
                || kind == StatusEffectKind.Rupture
                || kind == StatusEffectKind.Reflect;
        }

        private static CombatantState RequireTarget(CombatantState target, EffectKind kind)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target), $"Effect kind '{kind}' requires a target combatant.");
            }

            return target;
        }

        private static CombatantState RequireSource(CombatantState source, EffectKind kind)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source), $"Effect kind '{kind}' requires a source combatant.");
            }

            return source;
        }

        private EffectResultEvent Record(EffectResultEvent resultEvent)
        {
            resultEvents.Add(resultEvent);
            EffectResolved?.Invoke(resultEvent);
            return resultEvent;
        }
    }
}

