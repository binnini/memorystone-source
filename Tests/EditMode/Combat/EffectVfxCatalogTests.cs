#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Unity;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class EffectVfxCatalogTests
    {
        [Test]
        public void TryResolvePrefersExactSourceRefOverEarlierPrefixRule()
        {
            var prefixPrefab = new GameObject("Prefix VFX");
            var exactPrefab = new GameObject("Exact VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                var prefixEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Push,
                    new[] { prefixPrefab },
                    sourceRef: "knockback",
                    matchSourceRefPrefix: true);
                var exactEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { exactPrefab },
                    sourceRef: "knockback.impact");
                catalog.SetEntriesForTests(prefixEntry, exactEntry);

                var resultEvent = new EffectResultEvent(
                    EffectKind.Damage,
                    sourceRef: "knockback.impact");

                Assert.That(catalog.TryResolve(resultEvent, out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(exactEntry));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(prefixPrefab);
                Object.DestroyImmediate(exactPrefab);
            }
        }

        [Test]
        public void AttackCueStillPlaysWhenTheHitWasFullyBlocked()
        {
            // 🔴 2026-09-05 실플레이 피드백: "플레이어가 방어했을 때에도 몬스터 공격 VFX가 재생되도록".
            // 방어로 전부 흡수되면 규칙층은 Damage 대신 DamageBlocked를 올리는데, 몬스터 패턴 큐는
            // 전부 Damage로 저작돼 있어 어떤 큐도 안 걸리고 화면이 조용해졌다.
            //
            // 🔑 계약: <b>공격 연출은 「때렸다」의 그림</b>이지 「피해가 들어갔다」의 그림이 아니다.
            // 그래서 sourceRef 티어에서만 두 종류를 같은 것으로 본다 — 범용 종류 티어는 그대로 엄격하다
            // (아래 두 번째 단언). 그러지 않으면 아무 Damage 큐나 방어에 튀어나온다.
            var patternPrefab = new GameObject("Pattern VFX");
            var genericPrefab = new GameObject("Generic Damage VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                var patternEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { patternPrefab },
                    sourceRef: "monster.pattern.A040");
                var genericEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { genericPrefab });
                catalog.SetEntriesForTests(patternEntry, genericEntry);

                var blocked = new EffectResultEvent(
                    EffectKind.DamageBlocked,
                    targetUnitId: "player",
                    sourceRef: "monster.pattern.A040");

                Assert.That(catalog.TryResolve(blocked, out var resolved), Is.True,
                    "방어로 막혔어도 그 공격의 저작 큐는 재생돼야 한다.");
                Assert.That(resolved, Is.SameAs(patternEntry));

                // 저작 큐가 없는 방어는 여전히 조용하다 — 범용 Damage 큐를 빌려 오지 않는다.
                var unrelatedBlocked = new EffectResultEvent(
                    EffectKind.DamageBlocked,
                    targetUnitId: "player",
                    sourceRef: "monster.pattern.A999");

                Assert.That(catalog.TryResolve(unrelatedBlocked, out _), Is.False,
                    "종류 티어까지 열면 아무 Damage 큐나 방어에 튀어나온다.");
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(patternPrefab);
                Object.DestroyImmediate(genericPrefab);
            }
        }

        [Test]
        public void TryResolveExactSourceRefRequiresEffectKindMatch()
        {
            var damagePrefab = new GameObject("Damage VFX");
            var healPrefab = new GameObject("Heal VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                var damageEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { damagePrefab },
                    sourceRef: "A03");
                var healEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Heal,
                    new[] { healPrefab },
                    sourceRef: "A03");
                catalog.SetEntriesForTests(damageEntry, healEntry);

                var resultEvent = new EffectResultEvent(
                    EffectKind.Heal,
                    targetUnitId: "player",
                    sourceRef: "A03");

                Assert.That(catalog.TryResolve(resultEvent, out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(healEntry));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(damagePrefab);
                Object.DestroyImmediate(healPrefab);
            }
        }

        [Test]
        public void TryResolveFallsBackToKindWhenSourceRefDoesNotMatch()
        {
            var sourcePrefab = new GameObject("Trap VFX");
            var kindPrefab = new GameObject("Heal VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                var sourceEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { sourcePrefab },
                    sourceRef: "trap.damage",
                    matchSourceRefPrefix: true);
                var kindEntry = new EffectVfxCatalog.Entry(EffectKind.Heal, new[] { kindPrefab });
                catalog.SetEntriesForTests(sourceEntry, kindEntry);

                var resultEvent = new EffectResultEvent(
                    EffectKind.Heal,
                    sourceRef: "card.heal");

                Assert.That(catalog.TryResolve(resultEvent, out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(kindEntry));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(sourcePrefab);
                Object.DestroyImmediate(kindPrefab);
            }
        }

        [Test]
        public void TryResolveUsesTargetFilterForSameKindFallbacks()
        {
            var playerPrefab = new GameObject("Player Hit VFX");
            var monsterPrefab = new GameObject("Monster Hit VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                var playerEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { playerPrefab },
                    targetFilter: EffectVfxTargetFilter.Player);
                var monsterEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { monsterPrefab },
                    targetFilter: EffectVfxTargetFilter.Monster);
                catalog.SetEntriesForTests(playerEntry, monsterEntry);

                var playerHit = new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "player");
                var monsterHit = new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "monster-01");

                Assert.That(catalog.TryResolve(playerHit, out var resolvedPlayer), Is.True);
                Assert.That(resolvedPlayer, Is.SameAs(playerEntry));
                Assert.That(catalog.TryResolve(monsterHit, out var resolvedMonster), Is.True);
                Assert.That(resolvedMonster, Is.SameAs(monsterEntry));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(playerPrefab);
                Object.DestroyImmediate(monsterPrefab);
            }
        }

        [Test]
        public void TryResolveSkipsDeprecatedEntries()
        {
            var deprecatedPrefab = new GameObject("Deprecated VFX");
            var activePrefab = new GameObject("Active VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                var deprecatedEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { deprecatedPrefab },
                    sourceRef: "monster.pattern.A001",
                    cueId: "V001-old",
                    deprecated: true);
                var activeEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { activePrefab },
                    sourceRef: "monster.pattern.A001",
                    cueId: "V001");
                catalog.SetEntriesForTests(deprecatedEntry, activeEntry);

                var resultEvent = new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "player",
                    sourceRef: "monster.pattern.A001");

                Assert.That(catalog.TryResolve(resultEvent, out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(activeEntry));
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(deprecatedPrefab);
                Object.DestroyImmediate(activePrefab);
            }
        }

        [Test]
        public void PresentationSpawnsCatalogPrefabWhenAvailable()
        {
            var root = new GameObject("Effect Presentation Test Root");
            var prefab = new GameObject("Catalog Damage VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                prefab.AddComponent<ParticleSystem>();
                catalog.SetEntriesForTests(new EffectVfxCatalog.Entry(EffectKind.Damage, new[] { prefab }));

                var presentation = root.AddComponent<EffectPresentationController>();
                presentation.SetVfxCatalogForTests(catalog);
                presentation.Play(new EffectResultEvent(EffectKind.Damage, appliedAmount: 3), Vector3.zero);

                Assert.That(
                    presentation.SpawnedEffects.Any(effect => effect != null && effect.name == "VFX Damage (Catalog Damage VFX)"),
                    Is.True,
                    "A resolved catalog prefab should replace the generated placeholder particle.");
                Assert.That(
                    presentation.SpawnedEffects.Any(effect => effect != null && effect.name == "VFX Placeholder Damage"),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void CatalogEntryStoresPlaybackDelayTiming()
        {
            var prefab = new GameObject("Delayed Damage VFX");
            try
            {
                var entry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { prefab },
                    floatingTextMode: EffectFloatingTextMode.Hide,
                    playbackDelaySeconds: 0.05f);

                Assert.That(entry.PlaybackDelaySeconds, Is.EqualTo(0.05f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void PresentationAppliesFacingRotationToCatalogPrefab()
        {
            var root = new GameObject("Effect Presentation Rotation Test Root");
            var prefab = new GameObject("Directional Damage VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                catalog.SetEntriesForTests(new EffectVfxCatalog.Entry(EffectKind.Damage, new[] { prefab }));

                var presentation = root.AddComponent<EffectPresentationController>();
                presentation.SetVfxCatalogForTests(catalog);
                var facingRotation = Quaternion.LookRotation(Vector3.right, Vector3.up);
                presentation.Play(new EffectResultEvent(EffectKind.Damage, appliedAmount: 3), Vector3.zero, facingRotation);

                var spawned = presentation.SpawnedEffects.Single(effect => effect != null && effect.name == "VFX Damage (Directional Damage VFX)");
                Assert.That(Vector3.Dot(spawned.transform.forward, Vector3.right), Is.GreaterThan(0.99f),
                    "Directional VFX prefabs should face from the attacker/source toward the target.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void PresentationUsesPlaceholderWhenCatalogHasNoMatch()
        {
            var root = new GameObject("Effect Presentation Test Root");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                catalog.SetEntriesForTests();

                var presentation = root.AddComponent<EffectPresentationController>();
                presentation.SetVfxCatalogForTests(catalog);
                presentation.Play(new EffectResultEvent(EffectKind.Heal, appliedAmount: 2), Vector3.zero);

                Assert.That(
                    presentation.SpawnedEffects.Any(effect => effect != null && effect.name == "VFX Placeholder Heal"),
                    Is.True,
                    "The generated particle fallback must remain available for unmapped effects.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void DefaultResourceCatalogAppliesCardFloatingTextPolicy()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            Assert.That(catalog, Is.Not.Null);

            AssertFloatingTextPolicy(
                catalog,
                new EffectResultEvent(EffectKind.Push, targetUnitId: "player", appliedAmount: 1, sourceRef: "M01"),
                EffectFloatingTextMode.Hide,
                expectedShown: false);
            AssertFloatingTextPolicy(
                catalog,
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "monster", appliedAmount: 5, sourceRef: "A01"),
                EffectFloatingTextMode.Show,
                expectedShown: true);
            AssertFloatingTextPolicy(
                catalog,
                new EffectResultEvent(EffectKind.Block, targetUnitId: "player", appliedAmount: 3, sourceRef: "D01"),
                EffectFloatingTextMode.Show,
                expectedShown: true);
            AssertFloatingTextPolicy(
                catalog,
                new EffectResultEvent(EffectKind.FogReveal, targetUnitId: "field", sourceRef: "S01"),
                EffectFloatingTextMode.Show,
                expectedShown: true);
            AssertFloatingTextPolicy(
                catalog,
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "field", appliedAmount: 0, sourceRef: "field.damage"),
                EffectFloatingTextMode.Show,
                expectedShown: true,
                expectedOverride: "폭발!");
            AssertFloatingTextPolicy(
                catalog,
                new EffectResultEvent(EffectKind.Heal, targetUnitId: "field", appliedAmount: 0, sourceRef: "field.heal"),
                EffectFloatingTextMode.Show,
                expectedShown: true,
                expectedOverride: "회복!");
            AssertFloatingTextPolicy(
                catalog,
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "field", appliedAmount: 0, sourceRef: "field.immobilize.flashbang", statusKind: StatusEffectKind.Immobilize),
                EffectFloatingTextMode.Show,
                expectedShown: true,
                expectedOverride: "섬광!");
            AssertFloatingTextPolicy(
                catalog,
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "monster", appliedAmount: 2, sourceRef: "field.immobilize.flashbang", statusKind: StatusEffectKind.Immobilize),
                EffectFloatingTextMode.Show,
                expectedShown: true,
                expectedOverride: "속박!");
        }

        private static void AssertFloatingTextPolicy(
            EffectVfxCatalog catalog,
            EffectResultEvent resultEvent,
            EffectFloatingTextMode expectedMode,
            bool expectedShown,
            string expectedOverride = null)
        {
            Assert.That(catalog.TryResolve(resultEvent, out var entry), Is.True,
                $"Expected {resultEvent.Kind}/{resultEvent.SourceRef} to resolve a catalog entry.");
            Assert.That(entry.FloatingTextMode, Is.EqualTo(expectedMode),
                $"Expected {resultEvent.SourceRef} floating text mode {expectedMode}.");
            if (expectedOverride != null)
            {
                Assert.That(entry.FloatingTextOverride, Is.EqualTo(expectedOverride));
            }
            Assert.That(EffectPresentationController.ShouldShowFloatingText(entry, resultEvent), Is.EqualTo(expectedShown));
        }

        [Test]
        public void CatalogEntryPreservesFloatingTextPolicy()
        {
            var prefab = new GameObject("Floating Policy VFX");
            try
            {
                var entry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { prefab },
                    floatingTextMode: EffectFloatingTextMode.Hide,
                    floatingTextOverride: "밝혀짐!");

                Assert.That(entry.FloatingTextMode, Is.EqualTo(EffectFloatingTextMode.Hide));
                Assert.That(entry.FloatingTextOverride, Is.EqualTo("밝혀짐!"));
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void HideEntryDoesNotSpawnFloatingText()
        {
            var root = new GameObject("Floating Text Hide Root");
            var prefab = new GameObject("Hide VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                prefab.AddComponent<ParticleSystem>();
                catalog.SetEntriesForTests(new EffectVfxCatalog.Entry(
                    EffectKind.Push,
                    new[] { prefab },
                    targetFilter: EffectVfxTargetFilter.Player,
                    sourceRef: "M01",
                    floatingTextMode: EffectFloatingTextMode.Hide));

                var presentation = root.AddComponent<EffectPresentationController>();
                presentation.SetVfxCatalogForTests(catalog);
                presentation.Play(
                    new EffectResultEvent(EffectKind.Push, targetUnitId: "player", appliedAmount: 1, sourceRef: "M01"),
                    Vector3.zero);

                Assert.That(
                    presentation.SpawnedEffects.Any(effect => effect != null && effect.name.StartsWith("Floating Effect Text")),
                    Is.False,
                    "Hide entries must not spawn floating text even though their VFX prefab still plays.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void ShowEntrySpawnsFloatingText()
        {
            var root = new GameObject("Floating Text Show Root");
            var prefab = new GameObject("Show VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                prefab.AddComponent<ParticleSystem>();
                catalog.SetEntriesForTests(new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { prefab },
                    targetFilter: EffectVfxTargetFilter.Monster,
                    sourceRef: "A01",
                    floatingTextMode: EffectFloatingTextMode.Show));

                var presentation = root.AddComponent<EffectPresentationController>();
                presentation.SetVfxCatalogForTests(catalog);
                presentation.Play(
                    new EffectResultEvent(EffectKind.Damage, targetUnitId: "monster", appliedAmount: 7, sourceRef: "A01"),
                    Vector3.zero);

                Assert.That(
                    presentation.SpawnedEffects.Any(effect => effect != null && effect.name.StartsWith("Floating Effect Text")),
                    Is.True,
                    "Show entries must spawn floating text.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void PlayResolvedEntryCanSuppressFloatingTextForSyntheticDeathVfx()
        {
            var root = new GameObject("Floating Text Suppressed Root");
            var prefab = new GameObject("Death VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                prefab.AddComponent<ParticleSystem>();
                var entry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { prefab },
                    targetFilter: EffectVfxTargetFilter.Player,
                    sourceRef: "player.death",
                    floatingTextMode: EffectFloatingTextMode.Show);
                catalog.SetEntriesForTests(entry);

                var presentation = root.AddComponent<EffectPresentationController>();
                presentation.SetVfxCatalogForTests(catalog);
                presentation.PlayResolvedEntry(
                    new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", appliedAmount: 80, sourceRef: "player.death"),
                    entry,
                    Vector3.zero,
                    Quaternion.identity,
                    showFloatingText: false);

                Assert.That(
                    presentation.SpawnedEffects.Any(effect => effect != null && effect.name.StartsWith("Floating Effect Text")),
                    Is.False,
                    "Synthetic player-death VFX must not replay the lethal damage number.");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(prefab);
                Object.DestroyImmediate(catalog);
            }
        }

        [Test]
        public void FloatingTextPolicyResolvesPerEffectKindForSharedSourceRef()
        {
            var damagePrefab = new GameObject("A03 Damage VFX");
            var healPrefab = new GameObject("A03 Heal VFX");
            var catalog = ScriptableObject.CreateInstance<EffectVfxCatalog>();
            try
            {
                var damageEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { damagePrefab },
                    targetFilter: EffectVfxTargetFilter.Monster,
                    sourceRef: "A03",
                    floatingTextMode: EffectFloatingTextMode.Show);
                var healEntry = new EffectVfxCatalog.Entry(
                    EffectKind.Heal,
                    new[] { healPrefab },
                    targetFilter: EffectVfxTargetFilter.Player,
                    sourceRef: "A03",
                    floatingTextMode: EffectFloatingTextMode.Hide);
                catalog.SetEntriesForTests(damageEntry, healEntry);

                var healEvent = new EffectResultEvent(EffectKind.Heal, targetUnitId: "player", sourceRef: "A03");
                Assert.That(catalog.TryResolve(healEvent, out var resolved), Is.True);
                Assert.That(resolved, Is.SameAs(healEntry));
                Assert.That(resolved.FloatingTextMode, Is.EqualTo(EffectFloatingTextMode.Hide));
                Assert.That(EffectPresentationController.ShouldShowFloatingText(resolved, healEvent), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(damagePrefab);
                Object.DestroyImmediate(healPrefab);
            }
        }

        [Test]
        public void AutoFloatingTextShowsNumbersButSuppressesPush()
        {
            Assert.That(
                EffectPresentationController.ShouldShowFloatingText(
                    null, new EffectResultEvent(EffectKind.Damage, appliedAmount: 3)),
                Is.True);
            Assert.That(
                EffectPresentationController.ShouldShowFloatingText(
                    null, new EffectResultEvent(EffectKind.Push, sourceRef: "player.move")),
                Is.False);
            Assert.That(
                EffectPresentationController.ShouldShowFloatingText(
                    null, new EffectResultEvent(EffectKind.Knockback, sourceRef: "knockback")),
                Is.True);
        }

        [Test]
        public void TraitAnnouncementsShowTheirTextEvenWithNoNumber()
        {
            // 🔴 특성 알림은 수치가 없는 것이 정상이다(「은신!」·「견고!」). 0을 「할 말 없음」으로 읽는
            //    정책 분기가 하나라도 앞서면 특성 연출이 통째로 조용해진다 — 규칙은 다 돌았는데
            //    화면만 아무 말도 하지 않는, 가장 찾기 어려운 종류의 침묵이다.
            Assert.That(
                EffectPresentationController.ShouldShowFloatingText(
                    null,
                    new EffectResultEvent(
                        EffectKind.MonsterTraitTriggered,
                        targetUnitId: "m1",
                        sourceRef: MonsterTraitAnnouncement.StealthHiddenRef,
                        targetActorKind: "monster")),
                Is.True);

            Assert.That(
                EffectPresentationController.ShouldShowFloatingText(
                    null,
                    new EffectResultEvent(
                        EffectKind.MonsterTraitTriggered,
                        targetUnitId: "m1",
                        appliedAmount: 2,
                        sourceRef: MonsterTraitAnnouncement.AgitationGainedRef,
                        targetActorKind: "monster")),
                Is.True);
        }

        [Test]
        public void CardVfxCsvReadsOptionalFloatingTextColumn()
        {
            var csv =
                "cueId,cardId,effectKind,targetFilter,prefabPath,floatingTextMode,floatingTextOverride\n" +
                "T1,M01,Push,Player,Assets/Move.prefab,Hide,\n" +
                "T2,S01,FogReveal,Field,Assets/Scout.prefab,Show,밝혀짐!\n" +
                "T3,A02,Damage,Monster,Assets/Slash.prefab,,\n";

            var cues = CombatCardVfxCsvConverter.Convert(csv).ToDictionary(cue => cue.CueId, StringComparer.Ordinal);

            Assert.That(cues["T1"].FloatingTextMode, Is.EqualTo(EffectFloatingTextMode.Hide));
            Assert.That(cues["T2"].FloatingTextMode, Is.EqualTo(EffectFloatingTextMode.Show));
            Assert.That(cues["T2"].FloatingTextOverride, Is.EqualTo("밝혀짐!"));
            Assert.That(cues["T3"].FloatingTextMode, Is.EqualTo(EffectFloatingTextMode.Auto));
        }

        [Test]
        public void DefaultResourceCatalogUsesDefinedEffectKinds()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");

            Assert.That(catalog, Is.Not.Null);
            Assert.That(
                catalog.Entries.Where(entry => entry != null)
                    .Select(entry => entry.Kind)
                    .All(kind => Enum.IsDefined(typeof(EffectKind), kind)),
                Is.True,
                "Serialized catalog entries must stay in sync with the current EffectKind enum.");
        }

        [Test]
        public void DefaultResourceCatalogResolvesRepresentativeLabAndCsvKeys()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");

            Assert.That(catalog, Is.Not.Null);
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.Push,
                targetUnitId: "player",
                sourceRef: "player.move"), "PlayerVfx_Move");
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                // 갈래 1 반입으로 A003이 서드파티 Stone slash에서 전용 발톱 참격 규격으로 갈아탔다.
                sourceRef: "monster.pattern.A003"), "MonsterAttack_ClawSlash");
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "monster",
                sourceRef: "lab.monster.buff",
                statusKind: StatusEffectKind.Agility), "Buff");
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                sourceRef: "object.treasure_chest.open"), "ChestOpen");
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                sourceRef: "object.treasure_chest.reward"), "ChestReward");
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                sourceRef: "object.treasure_chest.claim"), "ChestClaim");
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "monster",
                sourceRef: "A01"), "Slash");
            AssertResolvedSpawnAnchor(catalog, new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "monster",
                sourceRef: "A01"), EffectVfxSpawnAnchor.SourceAttack);
            AssertResolvedSpawnAnchor(catalog, new EffectResultEvent(
                EffectKind.Push,
                targetUnitId: "player",
                sourceRef: "M01"), EffectVfxSpawnAnchor.SourceGround);
            AssertResolvedSpawnAnchor(catalog, new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "field",
                sourceRef: "field.damage"), EffectVfxSpawnAnchor.FieldCenter);
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "monster",
                sourceRef: "status.poison.apply",
                statusKind: StatusEffectKind.Poison), "DebuffHit");
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "player",
                sourceRef: "status.stun.apply",
                statusKind: StatusEffectKind.Stun), "DebuffHit");
            AssertResolvesPrefab(catalog, new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                sourceRef: "trap.spike"), "TrapVfx_Trigger");
        }

        [Test]
        public void ExplicitCatalogSpawnAnchorsOverrideDefaultAnchorPolicy()
        {
            var targetGround = new EffectVfxCatalog.Entry(
                EffectKind.Damage,
                new[] { new GameObject("Target Ground VFX") },
                targetFilter: EffectVfxTargetFilter.Monster,
                sourceRef: "test.target_ground",
                spawnAnchor: EffectVfxSpawnAnchor.TargetGround);
            var sourceAttack = new EffectVfxCatalog.Entry(
                EffectKind.Damage,
                new[] { new GameObject("Source Attack VFX") },
                sourceRef: "test.source_attack",
                spawnAnchor: EffectVfxSpawnAnchor.SourceAttack);

            try
            {
                Assert.That(
                    EffectVfxAnchorPolicy.ResolveTargetAnchor(
                        targetGround,
                        new EffectResultEvent(EffectKind.Damage, targetUnitId: "monster", sourceRef: "test.target_ground")),
                    Is.EqualTo(CharacterVfxAnchorKind.Ground),
                    "Attack cards can be authored to spawn on the selected target ground tile.");

                Assert.That(
                    EffectVfxAnchorPolicy.ResolveSourceAnchor(
                        sourceAttack,
                        new EffectResultEvent(EffectKind.Damage, targetUnitId: "monster", sourceRef: "test.source_attack")),
                    Is.EqualTo(CharacterVfxAnchorKind.AttackSource));
            }
            finally
            {
                foreach (var prefab in targetGround.Prefabs.Concat(sourceAttack.Prefabs))
                {
                    Object.DestroyImmediate(prefab);
                }
            }
        }


        [Category("ShippingData")]
        [Test]
        public void DefaultResourceCatalogPreservesCardCsvSpawnAnchors()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            Assert.That(catalog, Is.Not.Null);

            var cues = CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv)
                .Where(cue => cue.CueId == "CVA01" || cue.CueId == "CVS01")
                .ToArray();
            Assert.That(cues.Select(cue => cue.CueId), Is.EquivalentTo(new[] { "CVA01", "CVS01" }));

            foreach (var cue in cues)
            {
                var resultEvent = new EffectResultEvent(
                    cue.EffectKind,
                    targetUnitId: ToTargetUnitId(cue.TargetFilter),
                    sourceRef: cue.SourceRef);
                Assert.That(catalog.TryResolve(resultEvent, out var entry), Is.True, $"{cue.CueId} should resolve from the default catalog.");
                Assert.That(entry.CueId, Is.EqualTo(cue.CueId));
                Assert.That(entry.SpawnAnchor.ToString(), Is.EqualTo(cue.SpawnAnchor));
                Assert.That(entry.PlaybackDelaySeconds, Is.EqualTo(cue.DelaySeconds).Within(0.0001f));
            }
        }

        [Category("ShippingData")]
        [Test]
        public void VfxCsvPrefabPathsResolveToProjectAssets()
        {
            var missing = new List<string>();

            foreach (var cue in MonsterCatalogCsvConverter.ConvertDirectories(
                         CombatCsvPaths.MonsterDirectory,
                         CombatCsvPaths.PresentationDirectory).VfxCues)
            {
                AssertPrefabPathExists(cue.VfxCueId, cue.PrefabPath, missing);
            }

            foreach (var cue in CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv))
            {
                AssertPrefabPathExists(cue.CueId, cue.PrefabPath, missing);
            }

            Assert.That(missing, Is.Empty, "Every CSV VFX prefabPath must resolve to a GameObject asset.");
        }

        [Category("ShippingData")]
        [Test]
        public void RuntimeMonsterPatternSourceRefsResolveCatalogPrefabs()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);
            var cueById = bundle.VfxCues.ToDictionary(cue => cue.VfxCueId, StringComparer.Ordinal);
            var unresolved = new List<string>();

            foreach (var binding in bundle.PatternVfxBindings)
            {
                if (!cueById.TryGetValue(binding.VfxCueId, out var cue))
                {
                    unresolved.Add($"{binding.PatternId}: missing cue {binding.VfxCueId}");
                    continue;
                }

                var resultEvent = new EffectResultEvent(
                    cue.EffectKind,
                    targetUnitId: ToTargetUnitId(cue.TargetFilter),
                    sourceRef: $"monster.pattern.{binding.PatternId}");
                if (!ResolvesPrefab(catalog, resultEvent))
                {
                    unresolved.Add($"{binding.PatternId}/{binding.VfxCueId}: {cue.EffectKind} {resultEvent.SourceRef}");
                }
            }

            Assert.That(unresolved, Is.Empty, "Every enabled monster attack pattern runtime sourceRef should resolve a catalog prefab.");
        }

        /// <summary>
        /// 2026-08-20 #14: 빗맞음(whiff)에도 공격 VFX가 실린다 — AttackMissed 이벤트를 Damage 프로브로
        /// 정규화해 명중과 같은 바인딩을 해소하고, 공격자 몸에서 나가는 Source 앵커 큐만 남긴다.
        /// 모든 바인딩 패턴에서 정규화가 최소 1개의 Source 앵커 큐를 잡는지 잰다 — 타깃 앵커 큐만
        /// 가진 패턴이 생기면(빗맞음에서 침묵) 여기서 먼저 걸린다.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void WhiffNormalizationResolvesSourceAnchoredAttackCuesForEveryPattern()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);
            // 자기부여 패턴(targeting=self)은 커버 판정이 항상 참이라 빗맞음 자체가 불가능하다 —
            // 큐도 몬스터 대상(자기 VFX)이라 이 계약의 대상이 아니다.
            var selfTargetedPatternIds = new HashSet<string>(
                bundle.MonsterCatalog.Entries
                    .SelectMany(entry => entry.AttackPatterns ?? System.Array.Empty<MonsterAttackPattern>())
                    .Where(pattern => pattern.IsSelfTargeted)
                    .Select(pattern => pattern.Id),
                StringComparer.Ordinal);
            var silentPatterns = new List<string>();

            foreach (var patternId in bundle.PatternVfxBindings.Select(binding => binding.PatternId).Distinct())
            {
                if (selfTargetedPatternIds.Contains(patternId))
                {
                    continue;
                }

                var whiffEvent = new EffectResultEvent(
                    EffectKind.AttackMissed,
                    targetUnitId: "m1",
                    sourceRef: $"monster.pattern.{patternId}",
                    sourceUnitId: "m1",
                    sourceActorKind: "monster",
                    targetActorKind: "player");
                Assert.That(PlayerStateEffectPresentationBridge.IsWhiffMonsterPatternEvent(whiffEvent), Is.True);

                var probe = PlayerStateEffectPresentationBridge.BuildWhiffVfxProbe(whiffEvent);
                Assert.That(probe.Kind, Is.EqualTo(EffectKind.Damage));
                Assert.That(probe.TargetUnitId, Is.EqualTo("player"));

                var entries = PlayerStateEffectPresentationBridge.FilterSourceAnchoredEntries(catalog.ResolveAll(probe));
                if (entries.Length == 0)
                {
                    silentPatterns.Add(patternId);
                }
            }

            Assert.That(silentPatterns, Is.Empty,
                "빗맞음 정규화가 Source 앵커 공격 큐를 하나도 못 잡는 패턴: " + string.Join(", ", silentPatterns));
        }

        [Category("ShippingData")]
        [Test]
        public void DefaultResourceCatalogPreservesMonsterCsvTuningValues()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            Assert.That(catalog, Is.Not.Null);

            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);
            var cueById = bundle.VfxCues.ToDictionary(cue => cue.VfxCueId, StringComparer.Ordinal);
            var mismatches = new List<string>();

            foreach (var binding in bundle.PatternVfxBindings)
            {
                var cue = cueById[binding.VfxCueId];
                var resultEvent = new EffectResultEvent(
                    cue.EffectKind,
                    targetUnitId: ToTargetUnitId(cue.TargetFilter),
                    sourceRef: $"monster.pattern.{binding.PatternId}");
                var expectedAnchor = string.IsNullOrWhiteSpace(binding.SpawnAnchor)
                    ? (string.IsNullOrWhiteSpace(cue.SpawnAnchor) ? EffectVfxSpawnAnchor.Auto.ToString() : cue.SpawnAnchor)
                    : binding.SpawnAnchor;
                var entry = catalog.ResolveAll(resultEvent)
                    .FirstOrDefault(candidate => candidate.CueId == cue.VfxCueId && candidate.SpawnAnchor.ToString() == expectedAnchor);
                if (entry == null)
                {
                    mismatches.Add($"{binding.PatternId}/{cue.VfxCueId}/{expectedAnchor}: did not resolve");
                    continue;
                }

                if (entry.CueId != cue.VfxCueId ||
                    !Mathf.Approximately(entry.ScaleMultiplier, cue.ScaleMultiplier) ||
                    entry.ScaleWithRadius != cue.ScaleWithRadius ||
                    !Approximately(entry.PositionOffset, new Vector3(cue.OffsetX, cue.OffsetY, cue.OffsetZ), 0.001f) ||
                    !Approximately(entry.RotationOffset.eulerAngles, Quaternion.Euler(cue.RotationX, cue.RotationY, cue.RotationZ).eulerAngles, 0.5f) ||
                    !Mathf.Approximately(entry.LifetimeOverride, cue.LifetimeOverride) ||
                    !Mathf.Approximately(entry.PlaybackDelaySeconds, binding.DelaySeconds) ||
                    entry.SpawnAnchor.ToString() != expectedAnchor)
                {
                    mismatches.Add($"{binding.PatternId}/{cue.VfxCueId}/{expectedAnchor}: catalog values drift from CSV");
                }
            }

            Assert.That(mismatches, Is.Empty, "Monster CSV tuning values should match DefaultEffectVfxCatalog entries used by PrototypeTest.");
        }

        [Category("ShippingData")]
        [Test]
        public void MonsterPatternBindingsResolveConfiguredMonsterAnchors()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            Assert.That(catalog, Is.Not.Null);

            var entries = catalog.ResolveAll(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                sourceRef: "monster.pattern.A003"));

            Assert.That(entries.Select(entry => entry.CueId), Has.All.EqualTo("V003"));
            Assert.That(entries.Select(entry => entry.SpawnAnchor), Is.EqualTo(new[] { EffectVfxSpawnAnchor.SourceAttack }));

            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);
            var cueById = bundle.VfxCues.ToDictionary(cue => cue.VfxCueId, StringComparer.Ordinal);

            foreach (var binding in bundle.PatternVfxBindings.Where(binding => string.CompareOrdinal(binding.VfxCueId, "V001") >= 0 && string.CompareOrdinal(binding.VfxCueId, "V010") <= 0))
            {
                var cue = cueById[binding.VfxCueId];
                var resultEvent = new EffectResultEvent(
                    cue.EffectKind,
                    targetUnitId: ToTargetUnitId(cue.TargetFilter),
                    sourceRef: $"monster.pattern.{binding.PatternId}");
                var resolved = catalog.ResolveAll(resultEvent)
                    .SingleOrDefault(entry => entry.CueId == binding.VfxCueId && entry.SourceRef == resultEvent.SourceRef);

                Assert.That(resolved, Is.Not.Null, $"{binding.PatternId}/{binding.VfxCueId} should resolve exactly one catalog entry.");
                Assert.That(resolved.SpawnAnchor.ToString(), Is.EqualTo(binding.SpawnAnchor));
                Assert.That(resolved.PlaybackDelaySeconds, Is.EqualTo(binding.DelaySeconds).Within(0.0001f));
                Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(cue.PrefabPath), Is.Not.Null, $"{binding.VfxCueId}: {cue.PrefabPath}");
            }
        }

        [Category("ShippingData")]
        [Test]
        public void RuntimeCardSourceRefsResolveCatalogPrefabs()
        {
            var catalog = Resources.Load<EffectVfxCatalog>("Combat/DefaultEffectVfxCatalog");
            var unresolved = new List<string>();

            foreach (var cue in CombatCardVfxCsvConverter.ConvertFile(CombatCsvPaths.CardVfxCuesCsv))
            {
                var resultEvent = new EffectResultEvent(
                    cue.EffectKind,
                    targetUnitId: ToTargetUnitId(cue.TargetFilter),
                    sourceRef: cue.SourceRef);
                if (!ResolvesPrefab(catalog, resultEvent))
                {
                    unresolved.Add($"{cue.CueId}/{cue.CardId}: {cue.EffectKind} {cue.SourceRef}");
                }
            }

            Assert.That(unresolved, Is.Empty, "Every card VFX cue sourceRef should resolve a catalog prefab.");
        }

        private static void AssertPrefabPathExists(string cueId, string prefabPath, List<string> missing)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                missing.Add($"{cueId}: {prefabPath}");
            }
        }

        private static bool ResolvesPrefab(EffectVfxCatalog catalog, EffectResultEvent resultEvent)
        {
            return catalog != null &&
                   catalog.TryResolve(resultEvent, out var entry) &&
                   entry.Prefabs.Any(prefab => prefab != null);
        }

        private static bool Approximately(Vector3 left, Vector3 right, float tolerance)
        {
            return Mathf.Abs(left.x - right.x) <= tolerance &&
                   Mathf.Abs(left.y - right.y) <= tolerance &&
                   Mathf.Abs(left.z - right.z) <= tolerance;
        }

        private static string ToTargetUnitId(string targetFilter)
        {
            if (string.Equals(targetFilter, "Player", StringComparison.OrdinalIgnoreCase))
            {
                return "player";
            }

            if (string.Equals(targetFilter, "Field", StringComparison.OrdinalIgnoreCase))
            {
                return "field";
            }

            return "monster";
        }

        private static void AssertResolvesPrefab(EffectVfxCatalog catalog, EffectResultEvent resultEvent, string expectedPrefabNameFragment)
        {
            Assert.That(catalog.TryResolve(resultEvent, out var entry), Is.True);
            Assert.That(
                entry.Prefabs.Any(prefab => prefab != null && prefab.name.Contains(expectedPrefabNameFragment)),
                Is.True,
                $"Expected {resultEvent.Kind}/{resultEvent.SourceRef} to resolve a prefab containing '{expectedPrefabNameFragment}'.");
        }

        private static void AssertResolvedSpawnAnchor(
            EffectVfxCatalog catalog,
            EffectResultEvent resultEvent,
            EffectVfxSpawnAnchor expectedSpawnAnchor)
        {
            Assert.That(catalog.TryResolve(resultEvent, out var entry), Is.True);
            Assert.That(
                entry.SpawnAnchor,
                Is.EqualTo(expectedSpawnAnchor),
                $"Expected {resultEvent.Kind}/{resultEvent.SourceRef} to resolve spawn anchor {expectedSpawnAnchor}.");
        }
    }
}
#endif


