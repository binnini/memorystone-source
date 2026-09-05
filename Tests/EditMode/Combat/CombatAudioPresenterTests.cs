using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatAudioPresenterTests
    {
        [Test]
        public void EffectResultMappingRequestsExpectedCueIds()
        {
            // 🔴 공격·정찰 큐는 이제 저작(cards.csv type 컬럼)에서 갈린다 — 카탈로그를 넘겨야
            // 실제 출하 경로와 같은 판정을 받는다(T3, 2026-08-31).
            var attack = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "player",
                    center: new HexCoord(1, 0),
                    sourceRef: "A01"),
                null,
                ShippingCardCatalogSource.Load());
            Assert.That(attack, Does.Contain(AudioCueIds.CardAttackResolve));
            Assert.That(attack, Does.Contain(AudioCueIds.PlayerVoiceAttack));
            Assert.That(attack, Does.Contain(AudioCueIds.CombatMonsterHit));

            var playerHit = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                center: new HexCoord(0, 0),
                sourceRef: "manual-test-monster"));
            Assert.That(playerHit, Does.Contain(AudioCueIds.CombatPlayerHit));
            Assert.That(playerHit, Does.Contain(AudioCueIds.PlayerVoiceHit));
            Assert.That(playerHit.ToList().IndexOf(AudioCueIds.CombatPlayerHit), Is.LessThan(playerHit.ToList().IndexOf(AudioCueIds.PlayerVoiceHit)));

            var fieldDamage = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "field",
                center: new HexCoord(0, 0),
                radius: 2,
                amount: 6,
                appliedAmount: 6,
                sourceRef: CardEffectRefs.FieldDamage));
            Assert.That(fieldDamage.Single(), Is.EqualTo(AudioCueIds.FieldDamageEffect));

            // The tick's area announce, not a placement: it draws the footprint while the per-unit effects
            // that follow carry the audio, so it must stay silent.
            var fieldTickAnnounce = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "field",
                center: new HexCoord(0, 0),
                radius: 2,
                sourceRef: CardEffectRefs.FieldDamage));
            Assert.That(fieldTickAnnounce, Is.Empty);

            var defend = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(EffectKind.Block, center: new HexCoord(0, 0), sourceRef: ApprovedCardCatalogFactory.DefendOldSuitId));
            Assert.That(defend.Single(), Is.EqualTo(AudioCueIds.CardDefendResolve));

            var scout = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.FogReveal, center: new HexCoord(0, 0), sourceRef: "S01"),
                null,
                ShippingCardCatalogSource.Load());
            Assert.That(scout, Does.Contain(AudioCueIds.CardScoutResolve));
            Assert.That(scout, Does.Contain(AudioCueIds.FogTileRevealed));
        }

        [Test]
        public void EffectResultMappingCoversPreviouslySilentEffectKinds()
        {
            var heal = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(EffectKind.Heal, targetUnitId: "player"));
            Assert.That(heal.Single(), Is.EqualTo(AudioCueIds.EffectHeal));

            var push = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(EffectKind.Push, targetUnitId: "m1"));
            Assert.That(push.Single(), Is.EqualTo(AudioCueIds.EffectPush));

            var buff = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", statusKind: StatusEffectKind.Agility));
            Assert.That(buff.Single(), Is.EqualTo(AudioCueIds.CardBuffResolve));

            var fieldFlashbang = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "monster",
                radius: 2,
                sourceRef: CardEffectRefs.FieldImmobilizeFlashbang,
                statusKind: StatusEffectKind.Stun));
            Assert.That(fieldFlashbang.Single(), Is.EqualTo(AudioCueIds.FieldFlashbangEffect));

            var fieldFlashbangMonsterHit = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "m1",
                sourceRef: CardEffectRefs.FieldImmobilizeFlashbang,
                statusKind: StatusEffectKind.Immobilize));
            Assert.That(fieldFlashbangMonsterHit.Single(), Is.EqualTo(AudioCueIds.EffectImmobilize));
        }

        /// <summary>
        /// Installing a field object borrows the tick's EffectKind so the footprint renders. Before this was
        /// guarded, that made F01/F04/F05 play a monster hit, F02 the heal cue and F03 the immobilize cue at
        /// the moment of placement — the tick's own sound, played a turn before the tick.
        /// </summary>
        [Test]
        public void FieldPlacementAnnounceRequestsNoCueForAnyFieldKind()
        {
            var damageField = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "field",
                center: new HexCoord(0, 0),
                radius: 2,
                sourceRef: CardEffectRefs.FieldPlacement));
            Assert.That(damageField, Is.Empty);

            var healField = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Heal,
                targetUnitId: "field",
                center: new HexCoord(0, 0),
                radius: 2,
                sourceRef: CardEffectRefs.FieldPlacement));
            Assert.That(healField, Is.Empty);

            var immobilizeField = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "field",
                center: new HexCoord(0, 0),
                radius: 2,
                sourceRef: CardEffectRefs.FieldPlacement,
                statusKind: StatusEffectKind.Immobilize));
            Assert.That(immobilizeField, Is.Empty);
        }

        /// <summary>
        /// The tick's area announce is identified by its shape — raised on the field, carrying no amount —
        /// not by a list of field refs. The list version silently missed <c>field.lifesteal</c> when F04 was
        /// added, so F04's tick opened with a monster hit sound before it had hit anyone.
        /// </summary>
        [Test]
        public void FieldTickAreaAnnounceIsSilentForEveryFieldRef()
        {
            foreach (var sourceRef in new[]
                     {
                         CardEffectRefs.FieldDamage,
                         CardEffectRefs.FieldHeal,
                         CardEffectRefs.FieldImmobilizeFlashbang,
                         CardEffectRefs.FieldLifesteal
                     })
            {
                var announce = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "field",
                    center: new HexCoord(0, 0),
                    radius: 2,
                    sourceRef: sourceRef));
                Assert.That(announce, Is.Empty, $"{sourceRef} area announce should request no cue.");
            }

            // The per-unit damage that follows the announce is a real impact and must keep its hit cue.
            var monsterHit = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "m1",
                center: new HexCoord(0, 0),
                amount: 2,
                appliedAmount: 2,
                sourceRef: CardEffectRefs.FieldLifesteal));
            Assert.That(monsterHit, Does.Contain(AudioCueIds.CombatMonsterHit));
        }

        [Test]
        public void TrapSourcedDebuffLeadsWithTrapTriggerCue()
        {
            var trapBurn = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "player",
                sourceRef: "trap.firebomb",
                statusKind: StatusEffectKind.Poison));

            Assert.That(trapBurn[0], Is.EqualTo(AudioCueIds.EffectTrapTrigger));
            Assert.That(trapBurn, Does.Contain(AudioCueIds.EffectPoison));

            var cardBurn = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.StatusEffectApplied,
                targetUnitId: "m1",
                sourceRef: "card.firebomb",
                statusKind: StatusEffectKind.Poison));
            Assert.That(cardBurn.Single(), Is.EqualTo(AudioCueIds.EffectPoison));
        }

        [Test]
        public void KnockbackImpactDamageAddsKnockbackCue()
        {
            var knockback = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "m1",
                sourceRef: "knockback.impact"));

            Assert.That(knockback, Does.Contain(AudioCueIds.EffectKnockbackImpact));
        }

        [Test]
        public void MonsterPatternDamageUsesMonsterSpecificAttackCueWhenKnown()
        {
            var playerHit = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                sourceRef: "monster.pattern.A003",
                sourcePatternId: "A003"));

            Assert.That(playerHit, Does.Contain(AudioCueIds.MonsterIntentWarning));
            Assert.That(playerHit, Does.Contain(AudioCueIds.MonsterAttack("M002")));
            Assert.That(playerHit, Does.Contain(AudioCueIds.CombatPlayerHit));
            Assert.That(playerHit, Does.Contain(AudioCueIds.PlayerVoiceHit));
            Assert.That(playerHit.ToList().IndexOf(AudioCueIds.MonsterIntentWarning), Is.LessThan(playerHit.ToList().IndexOf(AudioCueIds.MonsterAttack("M002"))));
        }

        // 2026-09 발주 A 패턴 타격음 — 카탈로그가 연출 저작을 들고 있을 때만 붙는다(손 픽스처=종전 두 겹).
        private static MonsterCatalogDefinition CreatePatternSoundCatalog()
        {
            var strike = new MonsterAttackPattern("A003", "오른발톱 후려치기", 1, 0, 6);
            var roar = new MonsterAttackPattern("A029", "강화 포효", 1, 0, 0, targeting: "self", statusEffects: new[] { StatusEffectKind.Strength });
            var cross = new MonsterAttackPattern("A005", "위압의 십자", 1, 0, 5, statusEffects: new[] { StatusEffectKind.Slow });
            var entry = new MonsterCatalogEntry("M002", "불가살", "boss", "B001", detectionRange: 5, movePerTurn: 1, hp: 90, attackPatterns: new[] { strike, roar, cross });
            return new MonsterCatalogDefinition(
                "pattern-sound-test",
                "Pattern Sound Test",
                new[] { entry },
                new[]
                {
                    new MonsterPatternPresentationDefinition("A003", "V003", "S001", "S002", "S006", "Attack1"),
                    new MonsterPatternPresentationDefinition("A029", "V029", "S001", "S002", "S011", "Attack2"),
                    new MonsterPatternPresentationDefinition("A005", "V005", "S001", "S002", "S011", "Attack1"),
                });
        }

        [Test]
        public void MonsterPatternDamageAddsTheAuthoredImpactCueBetweenWarningAndVoice()
        {
            var catalog = CreatePatternSoundCatalog();
            var hit = new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", sourceRef: "monster.pattern.A003", sourcePatternId: "A003");

            var withCatalog = CombatAudioPresenter.MapEffectToCueIds(hit, null, null, catalog).ToList();
            Assert.That(withCatalog, Does.Contain("S006"));
            Assert.That(withCatalog.IndexOf(AudioCueIds.MonsterIntentWarning), Is.LessThan(withCatalog.IndexOf("S006")));
            Assert.That(withCatalog.IndexOf("S006"), Is.LessThan(withCatalog.IndexOf(AudioCueIds.MonsterAttack("M002"))), "물리 층은 목소리 층 앞이다.");

            var withoutCatalog = CombatAudioPresenter.MapEffectToCueIds(hit, null, null, null);
            Assert.That(withoutCatalog, Does.Not.Contain("S006"), "카탈로그가 없으면 종전 두 겹 그대로다.");

            var blocked = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.DamageBlocked, targetUnitId: "player", sourceRef: "monster.pattern.A003", sourcePatternId: "A003"), null, null, catalog);
            Assert.That(blocked, Does.Contain("S006"), "방어막에 막혀도 타격은 닿았다.");

            var missed = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.AttackMissed, targetUnitId: "player", sourceRef: "monster.pattern.A003", sourcePatternId: "A003"), null, null, catalog);
            Assert.That(missed, Does.Not.Contain("S006"), "빗나감에는 타격음이 없다.");
        }

        [Test]
        public void NonDamagingPatternStatusReplacesTheGenericBuffCueWithThePatternCue()
        {
            var catalog = CreatePatternSoundCatalog();

            // 자기부여(강화 포효): SourcePatternId 없이 sourceRef만 실린다(ApplyMonsterSelfStatusEffects).
            var selfBuff = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "m1", sourceRef: "monster.pattern.A029", sourceUnitId: "m1", statusKind: StatusEffectKind.Strength),
                null, null, catalog);
            Assert.That(selfBuff, Is.EqualTo(new[] { "S011" }), "피해 0 패턴은 부여가 곧 타격이다 — 범용 버프음 대신 패턴음.");

            // 피해 있는 패턴의 상태 부여: Damage 이벤트가 이미 S011을 냈으니 여기서는 종전 상태음만.
            var slowFromCross = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "player", sourceRef: "monster.pattern.A005", sourcePatternId: "A005", sourceActorKind: "monster", statusKind: StatusEffectKind.Slow),
                null, null, catalog);
            Assert.That(slowFromCross, Is.EqualTo(new[] { AudioCueIds.EffectSlow }));

            // 지대 설치(field)는 타격이 아니다.
            var zone = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.StatusEffectApplied, targetUnitId: "field", sourceRef: "monster.pattern.A029", sourceActorKind: "monster", targetActorKind: "field", statusKind: StatusEffectKind.Slow),
                null, null, catalog);
            Assert.That(zone, Does.Not.Contain("S011"));
        }

        [Test]
        public void LethalMonsterPatternDamageDoesNotAlsoPlayNormalPlayerHitVoice()
        {
            var lethalHit = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                appliedAmount: 80,
                sourceRef: "monster.pattern.A003",
                sourcePatternId: "A003",
                lethal: true));

            Assert.That(lethalHit, Does.Contain(AudioCueIds.MonsterIntentWarning));
            Assert.That(lethalHit, Does.Contain(AudioCueIds.MonsterAttack("M002")));
            Assert.That(lethalHit, Does.Not.Contain(AudioCueIds.CombatPlayerHit));
            Assert.That(lethalHit, Does.Not.Contain(AudioCueIds.PlayerVoiceHit));
        }

        [Test]
        public void DamageOverTimePlayerHitUsesDotVoiceAfterHitCue()
        {
            var poisonTick = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "player",
                appliedAmount: 3,
                statusKind: StatusEffectKind.Poison));

            Assert.That(poisonTick, Does.Contain(AudioCueIds.CombatPlayerHit));
            Assert.That(poisonTick, Does.Contain(AudioCueIds.PlayerVoiceDotHit));
            Assert.That(poisonTick, Does.Not.Contain(AudioCueIds.PlayerVoiceHit));
            Assert.That(poisonTick.ToList().IndexOf(AudioCueIds.CombatPlayerHit), Is.LessThan(poisonTick.ToList().IndexOf(AudioCueIds.PlayerVoiceDotHit)));
        }

        [Test]
        public void ResolvedMonsterHitUsesCommonHitThenMonsterVoiceCue()
        {
            var monsterHit = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "m1",
                    sourceRef: "A01"),
                unitId => unitId == "m1" ? "M004" : string.Empty,
                ShippingCardCatalogSource.Load());

            Assert.That(monsterHit, Does.Contain(AudioCueIds.CombatMonsterHit));
            Assert.That(monsterHit, Does.Contain(AudioCueIds.MonsterHit("M004")));
            Assert.That(monsterHit.ToList().IndexOf(AudioCueIds.CombatMonsterHit), Is.LessThan(monsterHit.ToList().IndexOf(AudioCueIds.MonsterHit("M004"))));
        }

        [Test]
        public void LethalMonsterHitAddsCommonAndMonsterSpecificDeathCues()
        {
            var lethalHit = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(
                    EffectKind.Damage,
                    targetUnitId: "m1",
                    appliedAmount: 99,
                    lethal: true),
                unitId => unitId == "m1" ? "M003" : string.Empty,
                // 카드 출처가 없는 치명타라 카탈로그가 판정에 끼어들 자리가 없다.
                null);

            Assert.That(lethalHit, Does.Contain(AudioCueIds.CombatMonsterHit));
            Assert.That(lethalHit, Does.Contain(AudioCueIds.MonsterHit("M003")));
            Assert.That(lethalHit, Does.Contain(AudioCueIds.CombatEnemyDeath));
            Assert.That(lethalHit, Does.Contain(AudioCueIds.MonsterDeath("M003")));
            Assert.That(lethalHit.ToList().IndexOf(AudioCueIds.MonsterHit("M003")),
                Is.LessThan(lethalHit.ToList().IndexOf(AudioCueIds.CombatEnemyDeath)));
        }

        [Test]
        public void FieldDamageTickUsesFieldEffectCueAndCommonMonsterHitFallback()
        {
            var monsterTick = CombatAudioPresenter.MapEffectToCueIds(new EffectResultEvent(
                EffectKind.Damage,
                targetUnitId: "m1",
                appliedAmount: 3,
                sourceRef: CardEffectRefs.FieldDamage));

            Assert.That(monsterTick, Does.Contain(AudioCueIds.FieldDamageEffect));
            Assert.That(monsterTick, Does.Contain(AudioCueIds.CombatMonsterHit));
        }

        [Test]
        public void PhaseTransitionCueIsRequestedOnlyWhenPhaseChanges()
        {
            var host = new GameObject("Combat audio presenter phase fixture");
            try
            {
                var presenter = host.AddComponent<CombatAudioPresenter>();
                presenter.HandlePhaseChangedForTests(CombatPhase.PlayerAction, CombatPhase.PlayerAction);
                Assert.That(presenter.RequestedCueHistory, Is.Empty);

                presenter.HandlePhaseChangedForTests(CombatPhase.PlayerAction, CombatPhase.PlayerMovement);
                Assert.That(presenter.RequestedCueHistory.Single(), Is.EqualTo(AudioCueIds.UiPhaseChange));

                presenter.HandlePhaseChangedForTests(CombatPhase.PlayerMovement, CombatPhase.Defeat);
                Assert.That(presenter.RequestedCueHistory.Last(), Is.EqualTo(AudioCueIds.GameDefeat));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void BossPhaseBgmLoopsAndTransitionRequestsBgmThenSfxStinger()
        {
            // 보스 페이즈 BGM은 루프 배경음으로 취급되어 페이즈 전환 시 하드컷이 아니라 크로스페이드된다.
            Assert.That(CombatAudioPresenter.IsLoopingBackgroundCueForTests("music.boss.bulgasal.p1"), Is.True);
            Assert.That(CombatAudioPresenter.IsLoopingBackgroundCueForTests("music.boss.bulgasal.p3"), Is.True);
            // 전환 스팅어는 루프가 아니다(원샷). 카탈로그에서 Sfx 버스로 저작되어 있어 BGM 루프를 죽이지 않는다.
            Assert.That(
                CombatAudioPresenter.IsLoopingBackgroundCueForTests(AudioCueIds.BossPhaseTransitionStinger),
                Is.False);

            var host = new GameObject("Combat audio presenter boss bgm fixture");
            try
            {
                var presenter = host.AddComponent<CombatAudioPresenter>();
                presenter.RequestBossPhaseTransitionAudioForTests("music.boss.bulgasal.p2", "test");

                // 전환은 새 페이즈 BGM(크로스페이드 대상)과 전환 스팅어를 순서대로 요청한다.
                Assert.That(presenter.RequestedCueHistory, Is.EqualTo(new[]
                {
                    "music.boss.bulgasal.p2",
                    AudioCueIds.BossPhaseTransitionStinger,
                }));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void NullOrMissingCatalogDoesNotBlockCueRequests()
        {
            var host = new GameObject("Combat audio presenter null catalog fixture");
            try
            {
                var presenter = host.AddComponent<CombatAudioPresenter>();

                Assert.DoesNotThrow(() => presenter.RequestCue(AudioCueIds.UiCardSelect, "test"));
                Assert.That(presenter.LastRequestedCueId, Is.EqualTo(AudioCueIds.UiCardSelect));
                Assert.That(presenter.LastStatus, Is.EqualTo(SoundPlaybackStatus.MissingClip));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RestartGameplayAudioReplaysGameplayLoopEvenAfterCooldown()
        {
            var host = new GameObject("Combat audio presenter restart fixture");
            var musicClip = AudioClip.Create("test gameplay loop", 16, 1, 8000, false);
            var ambienceClip = AudioClip.Create("test ambience loop", 16, 1, 8000, false);
            try
            {
                host.SetActive(false);
                var presenter = host.AddComponent<CombatAudioPresenter>();
                var catalog = SoundCatalog.CreateForTests(
                    new SoundCatalog.Entry("music.gameplay", musicClip, SoundBus.Music, cooldownSeconds: 10f),
                    new SoundCatalog.Entry(AudioCueIds.AmbienceSeoulBase, ambienceClip, SoundBus.Ambience, cooldownSeconds: 10f));

                presenter.Bind(null, catalog);

                presenter.RestartGameplayAudio();
                presenter.RestartGameplayAudio();

                Assert.That(presenter.RequestedCueHistory.Count(cue => cue == "music.gameplay"), Is.EqualTo(2));
                Assert.That(presenter.RequestedCueHistory.Count(cue => cue == AudioCueIds.AmbienceSeoulBase), Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(musicClip);
                Object.DestroyImmediate(ambienceClip);
                Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 2026-09 발주 B 새 발화 지점 — 이 세 kind는 종전에 switch에 케이스가 아예 없어 무음이었다.
        /// 해제·무효는 클립이 Player 폴더인 대로 플레이어 것만 울리고(몬스터 만료는 매 턴 여러 개라 소음),
        /// 수호 만료는 소비 시 무효음이 같은 순간 따로 나므로 뺀다.
        /// </summary>
        [Test]
        public void NewRaiseSiteEffectKindsMapToTheirDedicatedCues()
        {
            var expired = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.StatusEffectExpired, targetUnitId: "player", statusKind: StatusEffectKind.Poison));
            Assert.That(expired.Single(), Is.EqualTo(AudioCueIds.EffectExpire));

            var monsterExpired = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.StatusEffectExpired, targetUnitId: "m1", statusKind: StatusEffectKind.Poison));
            Assert.That(monsterExpired, Is.Empty, "몬스터 상태이상 만료는 무음이다.");

            var guardConsumed = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.StatusEffectExpired, targetUnitId: "player", statusKind: StatusEffectKind.Guard));
            Assert.That(guardConsumed, Is.Empty, "수호 소비는 StatusNegated가 울린다 — 해제음까지 겹치면 안 된다.");

            var negated = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.StatusNegated, targetUnitId: "player", statusKind: StatusEffectKind.Poison));
            Assert.That(negated.Single(), Is.EqualTo(AudioCueIds.EffectNegated));

            var injected = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.StatusCardInjected, targetUnitId: "player", sourceCardId: "STATUS-CURSE"));
            Assert.That(injected.Single(), Is.EqualTo(AudioCueIds.CurseInjected));
        }

        [Test]
        public void WeakSpotHitDamageAddsTheWeakSpotCueOnlyWhenFlagged()
        {
            var catalog = ShippingCardCatalogSource.Load();
            var plain = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m1", amount: 3, sourceRef: "A01"), null, catalog);
            Assert.That(plain, Does.Not.Contain(AudioCueIds.BossWeakSpotHit));

            var weakSpot = CombatAudioPresenter.MapEffectToCueIds(
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "m1", amount: 6, sourceRef: "A01", weakSpotHit: true), null, catalog);
            Assert.That(weakSpot, Does.Contain(AudioCueIds.BossWeakSpotHit));
            var order = weakSpot.ToList();
            Assert.That(order.IndexOf(AudioCueIds.CardAttackResolve), Is.LessThan(order.IndexOf(AudioCueIds.BossWeakSpotHit)),
                "취약타는 카드 타격음 위에 얹힌다.");
        }

        [Test]
        public void BagItemUseCueSplitsFlasksFromBeadsByIdSuffix()
        {
            Assert.That(CombatAudioPresenter.ResolveBagItemUseCueId("item-vitality-flask"), Is.EqualTo(AudioCueIds.ItemFlaskUse));
            Assert.That(CombatAudioPresenter.ResolveBagItemUseCueId("item-flame-bead"), Is.EqualTo(AudioCueIds.ItemBeadUse));
            Assert.That(CombatAudioPresenter.ResolveBagItemUseCueId("item-unknown-charm"), Is.Empty, "묶음 밖 아이템은 무음이다(클립 없음).");
            Assert.That(CombatAudioPresenter.ResolveBagItemUseCueId(null), Is.Empty);
        }

        [Test]
        [Category("ShippingData")]
        public void EveryShippingConsumableHasAUseCue()
        {
            var catalog = ConsumableItemCatalogCsv.ConvertFile(CombatCsvPaths.ConsumableItemsCsv);
            foreach (var item in catalog.Entries)
            {
                Assert.That(CombatAudioPresenter.ResolveBagItemUseCueId(item.Id), Is.Not.Empty,
                    $"{item.Id}: 호리병/구슬 어느 묶음에도 안 든다 — 사용음이 무음이 된다.");
            }
        }

        [Test]
        public void ObjectiveRevealCueOnlyBecomesPresentableAfterRevealedStatusText()
        {
            var objectiveCoord = new HexCoord(3, 0);
            var state = new CombatState(
                CombatObjectiveMapBuilder.CreateObjectiveMap(objectiveCoord),
                new HexCoord(0, 0),
                new HexCoord(4, 0),
                new CombatConfig(20, 10, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 2));

            Assert.That(CombatAudioPresenter.IsObjectiveRevealPresentableForTests(state), Is.False);

            state.RevealForTests(objectiveCoord);

            Assert.That(CombatAudioPresenter.IsObjectiveRevealPresentableForTests(state), Is.True);
            Assert.That(state.ObjectiveCompleted, Is.False);
            Assert.That(state.ObjectiveStatusText, Does.Contain("revealed"));
        }
    }
}




