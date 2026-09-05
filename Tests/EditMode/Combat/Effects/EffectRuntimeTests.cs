using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class EffectRuntimeTests
    {
        [Test]
        public void ApplyDamageReducesHpAndRecordsResultEvent()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("monster", 10);

            var result = runtime.ApplyDamage(target, 4, "test.damage");

            Assert.That(target.Hp, Is.EqualTo(6));
            Assert.That(result.Kind, Is.EqualTo(EffectKind.Damage));
            Assert.That(result.TargetUnitId, Is.EqualTo("monster"));
            Assert.That(result.Amount, Is.EqualTo(4));
            Assert.That(result.AppliedAmount, Is.EqualTo(4));
            Assert.That(result.PreviousValue, Is.EqualTo(10));
            Assert.That(result.CurrentValue, Is.EqualTo(6));
            Assert.That(runtime.ResultEvents.Single().Kind, Is.EqualTo(EffectKind.Damage));
        }

        [Test]
        public void ApplyBlockAbsorbsDamageBeforeHpLoss()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("player", 20);

            var block = runtime.ApplyBlock(target, 5, "test.block");
            var damage = runtime.ApplyDamage(target, 8, "test.damage");

            Assert.That(block.Kind, Is.EqualTo(EffectKind.Block));
            Assert.That(block.AppliedAmount, Is.EqualTo(5));
            Assert.That(damage.AppliedAmount, Is.EqualTo(3));
            Assert.That(target.Hp, Is.EqualTo(17));
            Assert.That(target.Block, Is.EqualTo(0));
        }

        [Test]
        public void ApplyDamageRecordsActualHpLossOnLethalHit()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("monster", 5);
            runtime.ApplyDamage(target, 3);

            var result = runtime.ApplyDamage(target, 99, "test.overkill");

            Assert.That(target.Hp, Is.EqualTo(0));
            Assert.That(result.Amount, Is.EqualTo(99));
            Assert.That(result.AppliedAmount, Is.EqualTo(2));
            Assert.That(result.PreviousValue, Is.EqualTo(2));
            Assert.That(result.CurrentValue, Is.EqualTo(0));
        }

        [Test]
        public void ApplyHealClampsToMaxHpAndRecordsActualAmount()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("player", 20);
            runtime.ApplyDamage(target, 7);

            var result = runtime.ApplyHeal(target, 99, "test.heal");

            Assert.That(target.Hp, Is.EqualTo(20));
            Assert.That(result.Kind, Is.EqualTo(EffectKind.Heal));
            Assert.That(result.Amount, Is.EqualTo(99));
            Assert.That(result.AppliedAmount, Is.EqualTo(7));
            Assert.That(result.PreviousValue, Is.EqualTo(13));
            Assert.That(result.CurrentValue, Is.EqualTo(20));
        }

        [Test]
        public void ApplyFogRevealRevealsRadiusAndPublishesCenterResult()
        {
            var runtime = new EffectRuntime();
            var visibility = new HexVisibilityRuntime(CreateRadiusMap(2), new HexCoord(0, 0), 0);
            var observed = new List<EffectResultEvent>();
            runtime.EffectResolved += observed.Add;

            var result = runtime.ApplyFogReveal(visibility, new HexCoord(0, 0), 1, "test.reveal");

            Assert.That(result.Kind, Is.EqualTo(EffectKind.FogReveal));
            Assert.That(result.Center, Is.EqualTo(new HexCoord(0, 0)));
            Assert.That(result.Radius, Is.EqualTo(1));
            Assert.That(result.AppliedAmount, Is.EqualTo(6), "Start cell was already revealed by visibility initialization; six neighbors are newly revealed.");
            Assert.That(visibility.GetVisibility(new HexCoord(1, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(visibility.GetVisibility(new HexCoord(2, 0)), Is.EqualTo(HexCellVisibility.Unknown));
            Assert.That(observed.Single().Kind, Is.EqualTo(EffectKind.FogReveal));
        }



        [Test]
        public void DefaultEffectDefinitionExposesSafeTextDefaults()
        {
            var definition = default(EffectDefinition);

            Assert.That(definition.Id, Is.EqualTo(string.Empty));
            Assert.That(definition.SourceRef, Is.EqualTo(string.Empty));
            Assert.DoesNotThrow(() => definition.GetHashCode());
        }

        [Test]
        public void EffectDefinitionNormalizesTextAndClampsNumericFields()
        {
            var definition = new EffectDefinition(null, EffectType.Instant, EffectKind.Damage, -4, -2, -1, null);

            Assert.That(definition.Id, Is.EqualTo(string.Empty));
            Assert.That(definition.Amount, Is.EqualTo(0));
            Assert.That(definition.Radius, Is.EqualTo(0));
            Assert.That(definition.DurationTurns, Is.EqualTo(0));
            Assert.That(definition.SourceRef, Is.EqualTo(string.Empty));
        }

        [Test]
        public void ApplyDefinitionDamageReducesHpAndPreservesSourceRef()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("monster", 10);
            var definition = new EffectDefinition("damage-card", EffectType.Instant, EffectKind.Damage, amount: 4, sourceRef: "vfx.damage.slash");

            var result = runtime.Apply(definition, target: target);

            Assert.That(target.Hp, Is.EqualTo(6));
            Assert.That(result.Kind, Is.EqualTo(EffectKind.Damage));
            Assert.That(result.Amount, Is.EqualTo(4));
            Assert.That(result.AppliedAmount, Is.EqualTo(4));
            Assert.That(result.SourceRef, Is.EqualTo("vfx.damage.slash"));
        }

        [Test]
        public void ApplyDefinitionBlockAddsBlock()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("player", 20);
            var definition = new EffectDefinition("guard", EffectType.Instant, EffectKind.Block, amount: 5, sourceRef: "vfx.block.shield");

            var result = runtime.Apply(definition, target: target);

            Assert.That(target.Block, Is.EqualTo(5));
            Assert.That(result.Kind, Is.EqualTo(EffectKind.Block));
            Assert.That(result.Amount, Is.EqualTo(5));
            Assert.That(result.AppliedAmount, Is.EqualTo(5));
            Assert.That(result.SourceRef, Is.EqualTo("vfx.block.shield"));
        }

        [Test]
        public void ApplyDefinitionHealClampsToMissingHpAndPreservesSourceRef()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("player", 20);
            runtime.ApplyDamage(target, 3);
            var definition = new EffectDefinition("heal", EffectType.Instant, EffectKind.Heal, amount: 10, sourceRef: "vfx.heal.pulse");

            var result = runtime.Apply(definition, target: target);

            Assert.That(target.Hp, Is.EqualTo(20));
            Assert.That(result.Kind, Is.EqualTo(EffectKind.Heal));
            Assert.That(result.Amount, Is.EqualTo(10));
            Assert.That(result.AppliedAmount, Is.EqualTo(3));
            Assert.That(result.SourceRef, Is.EqualTo("vfx.heal.pulse"));
        }

        [Test]
        public void ApplyDefinitionFogRevealUsesRadiusForEventAmountAndRevealRadius()
        {
            var runtime = new EffectRuntime();
            var visibility = new HexVisibilityRuntime(CreateRadiusMap(2), new HexCoord(0, 0), 0);
            var definition = new EffectDefinition("fog-reveal", EffectType.Instant, EffectKind.FogReveal, amount: 99, radius: 1, sourceRef: "vfx.fog.reveal");

            var result = runtime.Apply(definition, visibility: visibility, center: new HexCoord(0, 0));

            Assert.That(result.Kind, Is.EqualTo(EffectKind.FogReveal));
            Assert.That(result.Amount, Is.EqualTo(1));
            Assert.That(result.Radius, Is.EqualTo(1));
            Assert.That(result.AppliedAmount, Is.EqualTo(6));
            Assert.That(result.SourceRef, Is.EqualTo("vfx.fog.reveal"));
            Assert.That(visibility.GetVisibility(new HexCoord(1, 0)), Is.EqualTo(HexCellVisibility.Revealed));
            Assert.That(visibility.GetVisibility(new HexCoord(2, 0)), Is.EqualTo(HexCellVisibility.Unknown));
        }


        [Test]
        public void ApplyReflectDamagesSourceByPercentOfContextDamage()
        {
            var runtime = new EffectRuntime();
            var attacker = new CombatantState("monster", 20);
            var definition = new EffectDefinition("reflect", EffectType.Instant, EffectKind.ReflectDamage, amount: 50, sourceRef: "vfx.reflect");

            var result = runtime.Apply(definition, source: attacker, contextAmount: 8);

            Assert.That(attacker.Hp, Is.EqualTo(16));
            Assert.That(result.Kind, Is.EqualTo(EffectKind.ReflectDamage));
            Assert.That(result.TargetUnitId, Is.EqualTo("monster"));
            Assert.That(result.Amount, Is.EqualTo(50));
            Assert.That(result.AppliedAmount, Is.EqualTo(4));
            Assert.That(result.SourceRef, Is.EqualTo("vfx.reflect"));
        }

        [Test]
        public void ApplyReflectRequiresSourceCombatant()
        {
            var runtime = new EffectRuntime();
            var definition = new EffectDefinition("reflect", EffectType.Instant, EffectKind.ReflectDamage, amount: 50);

            Assert.Throws<ArgumentNullException>(() => runtime.Apply(definition, contextAmount: 8));
        }

        [TestCase(StatusEffectKind.Immobilize)]
        [TestCase(StatusEffectKind.Agility)]
        [TestCase(StatusEffectKind.Reflect)]
        public void ApplyDurationDefinitionRegistersActiveEffectAndPublishesResult(StatusEffectKind kind)
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("player", 20);
            var definition = new EffectDefinition($"duration-{kind}", EffectType.Duration, kind, amount: 2, durationTurns: 3, sourceRef: $"vfx.{kind}");

            var result = runtime.Apply(definition, target: target);

            Assert.That(result.Kind, Is.EqualTo(EffectKind.StatusEffectApplied));
            Assert.That(result.StatusKind, Is.EqualTo(kind));
            Assert.That(result.TargetUnitId, Is.EqualTo("player"));
            Assert.That(result.Amount, Is.EqualTo(2));
            Assert.That(result.AppliedAmount, Is.EqualTo(3));
            Assert.That(result.SourceRef, Is.EqualTo($"vfx.{kind}"));
            Assert.That(runtime.ActiveEffects.Single().Kind, Is.EqualTo(kind));
            Assert.That(runtime.ActiveEffects.Single().RemainingTurns, Is.EqualTo(3));
            Assert.That(runtime.HasActiveEffect(target, kind), Is.True);
        }

        [Test]
        public void TickDurationEffectsDecrementsAndExpiresActiveEffects()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("player", 20);
            var definition = new EffectDefinition("immobilize", EffectType.Duration, StatusEffectKind.Immobilize, durationTurns: 2);
            runtime.Apply(definition, target: target);

            // 새로 적용된 지속 효과는 적용 턴의 첫 틱을 건너뛴다(SkipNextTick) —
            // "N턴 동안"에 적용 턴은 포함되지 않는다.
            var firstExpired = runtime.TickDurationEffects();

            Assert.That(firstExpired, Is.EqualTo(0));
            Assert.That(runtime.ActiveEffects.Single().RemainingTurns, Is.EqualTo(2));
            Assert.That(runtime.HasActiveEffect(target, StatusEffectKind.Immobilize), Is.True);

            var secondExpired = runtime.TickDurationEffects();

            Assert.That(secondExpired, Is.EqualTo(0));
            Assert.That(runtime.ActiveEffects.Single().RemainingTurns, Is.EqualTo(1));
            Assert.That(runtime.HasActiveEffect(target, StatusEffectKind.Immobilize), Is.True);

            var thirdExpired = runtime.TickDurationEffects();

            Assert.That(thirdExpired, Is.EqualTo(1));
            Assert.That(runtime.ActiveEffects, Is.Empty);
            Assert.That(runtime.HasActiveEffect(target, StatusEffectKind.Immobilize), Is.False);
        }

        [Test]
        public void ApplyDurationDefinitionRejectsUnsupportedDurationKind()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("player", 20);
            var definition = new EffectDefinition("duration-damage", EffectType.Duration, EffectKind.Damage, amount: 2, durationTurns: 3);

            Assert.Throws<NotSupportedException>(() => runtime.Apply(definition, target: target));
        }

        [Test]
        public void ApplyPushRemainsUnsupportedUntilBoardMovementContractExists()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("monster", 10);
            var definition = new EffectDefinition("push", EffectType.Instant, EffectKind.Push, amount: 1);

            Assert.Throws<NotSupportedException>(() => runtime.Apply(definition, target: target));
        }


        [Test]
        public void CombatStateOwnsFieldObjectRegistry()
        {
            var state = CombatState.CreateDefaultDemo();

            Assert.That(state.FieldObjects, Is.Not.Null);
            Assert.That(state.FieldObjects.Objects, Is.Empty);
        }

        [Test]
        public void EffectRuntimeUsesSuppliedFieldObjectRegistry()
        {
            var registry = new FieldObjectRegistry();
            var runtime = new EffectRuntime(registry);
            var definition = new EffectDefinition("field-damage", EffectType.FieldObject, EffectKind.Damage, amount: 2, radius: 1, durationTurns: 2);

            runtime.Apply(definition, center: new HexCoord(0, 0));

            Assert.That(runtime.FieldObjects, Is.SameAs(registry));
            Assert.That(registry.Objects.Single().Kind, Is.EqualTo(FieldObjectKind.FieldDamage));
        }

        [Test]
        public void FieldObjectTargetRequiresCombatantAndPreservesRoleAndPosition()
        {
            var combatant = new CombatantState("player", 20);
            var target = new FieldObjectTarget(combatant, new HexCoord(1, 0), FieldObjectTargetKind.Player);

            Assert.That(target.Combatant, Is.SameAs(combatant));
            Assert.That(target.Position, Is.EqualTo(new HexCoord(1, 0)));
            Assert.That(target.Kind, Is.EqualTo(FieldObjectTargetKind.Player));
            Assert.Throws<ArgumentNullException>(() => new FieldObjectTarget(null, new HexCoord(0, 0), FieldObjectTargetKind.Monster));
        }

        [Test]
        public void ApplyFieldObjectFogRevealStoresMappedFieldsAndRevealsOnPlacement()
        {
            var runtime = new EffectRuntime();
            var visibility = new HexVisibilityRuntime(CreateRadiusMap(2), new HexCoord(0, 0), 0);
            var definition = new EffectDefinition("campfire", EffectType.FieldObject, EffectKind.FogReveal, amount: 99, radius: 1, durationTurns: 3, sourceRef: "field.campfire");

            var result = runtime.Apply(definition, visibility: visibility, center: new HexCoord(0, 0));

            var fieldObject = runtime.FieldObjects.Objects.Single();
            Assert.That(fieldObject.Position, Is.EqualTo(new HexCoord(0, 0)));
            Assert.That(fieldObject.Radius, Is.EqualTo(1));
            Assert.That(fieldObject.RemainingTurns, Is.EqualTo(3));
            Assert.That(fieldObject.Kind, Is.EqualTo(FieldObjectKind.FogReveal));
            Assert.That(fieldObject.Value, Is.EqualTo(99));
            Assert.That(result.Kind, Is.EqualTo(EffectKind.FogReveal));
            Assert.That(result.AppliedAmount, Is.EqualTo(6));
            Assert.That(visibility.GetVisibility(new HexCoord(1, 0)), Is.EqualTo(HexCellVisibility.Revealed));
        }

        [Test]
        public void ApplyFieldObjectFogRevealRequiresVisibilityBeforeRegistering()
        {
            var runtime = new EffectRuntime();
            var definition = new EffectDefinition("campfire", EffectType.FieldObject, EffectKind.FogReveal, radius: 1, durationTurns: 3);

            Assert.Throws<ArgumentNullException>(() => runtime.Apply(definition, center: new HexCoord(0, 0)));
            Assert.That(runtime.FieldObjects.Objects, Is.Empty);
        }

        [Test]
        public void TickFieldObjectFogRevealRunsInInsertionOrderAndExpires()
        {
            var runtime = new EffectRuntime();
            var visibility = new HexVisibilityRuntime(CreateRadiusMap(3), new HexCoord(0, 0), 0);
            runtime.Apply(new EffectDefinition("first", EffectType.FieldObject, EffectKind.FogReveal, radius: 1, durationTurns: 2), visibility: visibility, center: new HexCoord(0, 0));
            runtime.Apply(new EffectDefinition("second", EffectType.FieldObject, EffectKind.FogReveal, radius: 1, durationTurns: 1), visibility: visibility, center: new HexCoord(2, 0));
            runtime.ClearResults();

            var firstExpired = runtime.TickFieldObjects(visibility);

            Assert.That(firstExpired, Is.EqualTo(1));
            Assert.That(runtime.ResultEvents.Select(result => result.Kind).ToArray(), Is.EqualTo(new[] { EffectKind.FogReveal, EffectKind.FogReveal }));
            Assert.That(runtime.ResultEvents[0].Center, Is.EqualTo(new HexCoord(0, 0)));
            Assert.That(runtime.ResultEvents[1].Center, Is.EqualTo(new HexCoord(2, 0)));
            Assert.That(runtime.FieldObjects.Objects.Single().RemainingTurns, Is.EqualTo(1));

            var secondExpired = runtime.TickFieldObjects(visibility);

            Assert.That(secondExpired, Is.EqualTo(1));
            Assert.That(runtime.FieldObjects.Objects, Is.Empty);
        }

        [Test]
        public void FieldDamageDamagesPlayerAndMonsterTargetsInsideRadiusInTargetOrder()
        {
            var runtime = new EffectRuntime();
            var player = new CombatantState("player", 20);
            var monster = new CombatantState("monster", 20);
            var outside = new CombatantState("outside", 20);
            var targets = new[]
            {
                new FieldObjectTarget(player, new HexCoord(0, 0), FieldObjectTargetKind.Player),
                new FieldObjectTarget(monster, new HexCoord(1, 0), FieldObjectTargetKind.Monster),
                new FieldObjectTarget(outside, new HexCoord(3, 0), FieldObjectTargetKind.Monster)
            };
            runtime.Apply(new EffectDefinition("fire", EffectType.FieldObject, EffectKind.Damage, amount: 4, radius: 1, durationTurns: 1), center: new HexCoord(0, 0));
            runtime.ClearResults();

            var expired = runtime.TickFieldObjects(targets: targets);

            Assert.That(expired, Is.EqualTo(1));
            Assert.That(player.Hp, Is.EqualTo(16));
            Assert.That(monster.Hp, Is.EqualTo(16));
            Assert.That(outside.Hp, Is.EqualTo(20));
            Assert.That(runtime.ResultEvents.Select(result => result.TargetUnitId).ToArray(), Is.EqualTo(new[] { "player", "monster" }));
        }

        [Test]
        public void FieldDamageCarriesTheTickedTileSoConsumersKnowWhereItHappened()
        {
            // A field tick has no card behind it, so unless the tick supplies the tile the event says only
            // "monster took 4" with no place attached. That gap is not cosmetic: it left the action camera
            // unable to frame field ticks at all — the case the whole feature exists for — while the sibling
            // field.immobilize, which always passed a centre, worked
            // (docs/monster-action-camera-focus-plan.md §10.8).
            var runtime = new EffectRuntime();
            var monster = new CombatantState("monster", 20);
            var monsterTile = new HexCoord(1, 0);
            var targets = new[]
            {
                new FieldObjectTarget(monster, monsterTile, FieldObjectTargetKind.Monster)
            };
            runtime.Apply(
                new EffectDefinition("fire", EffectType.FieldObject, EffectKind.Damage, amount: 4, radius: 1, durationTurns: 1),
                center: new HexCoord(0, 0));
            runtime.ClearResults();

            runtime.TickFieldObjects(targets: targets);

            var tick = runtime.ResultEvents.Single(result => result.TargetUnitId == "monster");
            Assert.That(tick.SourceRef, Is.EqualTo("field.damage"));
            Assert.That(tick.Center, Is.EqualTo(monsterTile), "틱은 자기가 때린 칸을 실어 보내야 한다");
        }

        [Test]
        public void ConditionalHealHealsOnlyPlayerTargetsInsideRadius()
        {
            var runtime = new EffectRuntime();
            var player = new CombatantState("player", 20);
            var monster = new CombatantState("monster", 20);
            runtime.ApplyDamage(player, 8);
            runtime.ApplyDamage(monster, 8);
            runtime.ClearResults();
            runtime.Apply(new EffectDefinition("holy-fire", EffectType.FieldObject, EffectKind.Heal, amount: 5, radius: 1, durationTurns: 1), center: new HexCoord(0, 0));
            runtime.ClearResults();

            runtime.TickFieldObjects(targets: new[]
            {
                new FieldObjectTarget(monster, new HexCoord(0, 0), FieldObjectTargetKind.Monster),
                new FieldObjectTarget(player, new HexCoord(1, 0), FieldObjectTargetKind.Player)
            });

            Assert.That(player.Hp, Is.EqualTo(17));
            Assert.That(monster.Hp, Is.EqualTo(12));
            Assert.That(runtime.ResultEvents.Single().Kind, Is.EqualTo(EffectKind.Heal));
            Assert.That(runtime.ResultEvents.Single().TargetUnitId, Is.EqualTo("player"));
        }

        [Test]
        public void MassImmobilizeAffectsOnlyMonsterTargetsInsideRadius()
        {
            var runtime = new EffectRuntime();
            var player = new CombatantState("player", 20);
            var monster = new CombatantState("monster", 20);
            var outside = new CombatantState("outside", 20);
            runtime.Apply(new EffectDefinition("flash", EffectType.FieldObject, StatusEffectKind.Immobilize, amount: 1, radius: 1, durationTurns: 1), center: new HexCoord(0, 0));
            runtime.ClearResults();

            runtime.TickFieldObjects(targets: new[]
            {
                new FieldObjectTarget(player, new HexCoord(0, 0), FieldObjectTargetKind.Player),
                new FieldObjectTarget(monster, new HexCoord(1, 0), FieldObjectTargetKind.Monster),
                new FieldObjectTarget(outside, new HexCoord(3, 0), FieldObjectTargetKind.Monster)
            });

            Assert.That(runtime.HasActiveEffect(monster, StatusEffectKind.Immobilize), Is.True);
            Assert.That(runtime.HasActiveEffect(player, StatusEffectKind.Immobilize), Is.False);
            Assert.That(runtime.HasActiveEffect(outside, StatusEffectKind.Immobilize), Is.False);
            Assert.That(runtime.ResultEvents.Single().Kind, Is.EqualTo(EffectKind.StatusEffectApplied));
            Assert.That(runtime.ResultEvents.Single().TargetUnitId, Is.EqualTo("monster"));
        }

        [Test]
        public void FieldObjectRejectsUnsupportedKinds()
        {
            var runtime = new EffectRuntime();
            var definition = new EffectDefinition("reflect-field", EffectType.FieldObject, StatusEffectKind.Reflect, amount: 1, radius: 1, durationTurns: 1);

            Assert.Throws<NotSupportedException>(() => runtime.Apply(definition, center: new HexCoord(0, 0)));
        }

        [TestCase(EffectType.Duration)]
        public void ApplyDefinitionThrowsForUnsupportedEffectTypes(EffectType type)
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("target", 10);
            var definition = new EffectDefinition("unsupported", type, EffectKind.Damage, amount: 1);

            Assert.Throws<NotSupportedException>(() => runtime.Apply(definition, target: target));
        }

        [Test]
        public void ApplyDefinitionThrowsForUnsupportedEffectKind()
        {
            var runtime = new EffectRuntime();
            var target = new CombatantState("target", 10);
            var definition = new EffectDefinition("unknown", EffectType.Instant, (EffectKind)999, amount: 1);

            Assert.Throws<NotSupportedException>(() => runtime.Apply(definition, target: target));
        }

        [TestCase(EffectKind.Damage)]
        [TestCase(EffectKind.Block)]
        [TestCase(EffectKind.Heal)]
        public void ApplyDefinitionThrowsWhenTargetEffectHasNoTarget(EffectKind kind)
        {
            var runtime = new EffectRuntime();
            var definition = new EffectDefinition("needs-target", EffectType.Instant, kind, amount: 1);

            Assert.Throws<ArgumentNullException>(() => runtime.Apply(definition));
        }

        [Test]
        public void ApplyDefinitionThrowsWhenFogRevealHasNoVisibility()
        {
            var runtime = new EffectRuntime();
            var definition = new EffectDefinition("fog", EffectType.Instant, EffectKind.FogReveal, radius: 1);

            Assert.Throws<ArgumentNullException>(() => runtime.Apply(definition, center: new HexCoord(0, 0)));
        }

        private static HexMapData CreateRadiusMap(int radius)
        {
            var cells = new List<HexCellData>();
            for (var q = -radius; q <= radius; q++)
            {
                for (var r = -radius; r <= radius; r++)
                {
                    var coord = new HexCoord(q, r);
                    if (new HexCoord(0, 0).DistanceTo(coord) <= radius)
                    {
                        cells.Add(new HexCellData(coord, "ground", "street", 1, true, false));
                    }
                }
            }

            return new HexMapData(cells);
        }
    }
}






