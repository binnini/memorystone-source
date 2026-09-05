using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEditor;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class SoundCatalogAssetTests
    {
        private static readonly string[] P0CueIds =
        {
            AudioCueIds.UiCardHover,
            AudioCueIds.UiCardSelect,
            AudioCueIds.UiCardInvalid,
            AudioCueIds.UiKiInsufficient,
            AudioCueIds.UiPhaseChange,
            AudioCueIds.RewardCardHover,
            AudioCueIds.CardMoveResolve,
            AudioCueIds.CardAttackResolve,
            AudioCueIds.CardDefendResolve,
            AudioCueIds.CardScoutResolve,
            AudioCueIds.FogTileRevealed,
            AudioCueIds.CombatPlayerHit,
            AudioCueIds.CombatMonsterHit,
            AudioCueIds.ObjectiveMemoryGyeolRevealed,
            AudioCueIds.ObjectiveInvestigateSuccess,
            AudioCueIds.GameDefeat,
            AudioCueIds.GameVictory
        };

        private static readonly string[] P1CatalogOnlyCueIds =
        {
            AudioCueIds.UiCardDraw,
            AudioCueIds.UiCardDiscard,
            AudioCueIds.MovementTerrainStreet,
            AudioCueIds.MovementTerrainPark,
            AudioCueIds.MonsterIntentWarning,
            AudioCueIds.MonsterMoveResolve,
            AudioCueIds.EnemyTurnBegin,
            AudioCueIds.AmbienceSeoulBase,
            AudioCueIds.AmbienceYokaiPressure,
            AudioCueIds.FieldDamageEffect,
            AudioCueIds.FieldFlashbangEffect,
            AudioCueIds.CardMoveCast,
            AudioCueIds.CardAttackCast,
            AudioCueIds.CardDefendCast,
            AudioCueIds.CardBuffCast,
            AudioCueIds.CardUtilityCast,
            AudioCueIds.CardFieldCast
        };

        private static readonly string[] MonsterCueIds =
        {
            AudioCueIds.MonsterAttack("M001"),
            AudioCueIds.MonsterHit("M001"),
            AudioCueIds.MonsterDeath("M001"),
            AudioCueIds.MonsterAttack("M002"),
            AudioCueIds.MonsterHit("M002"),
            AudioCueIds.MonsterDeath("M002"),
            AudioCueIds.MonsterAttack("M003"),
            AudioCueIds.MonsterHit("M003"),
            AudioCueIds.MonsterDeath("M003"),
            AudioCueIds.MonsterAttack("M004"),
            AudioCueIds.MonsterHit("M004"),
            AudioCueIds.MonsterDeath("M004"),
            AudioCueIds.MonsterAttack("M005"),
            AudioCueIds.MonsterHit("M005"),
            AudioCueIds.MonsterDeath("M005"),
            AudioCueIds.MonsterAttack("M006"),
            AudioCueIds.MonsterHit("M006"),
            AudioCueIds.MonsterDeath("M006")
        };

        private static readonly string[] PlayerVoiceCueIds =
        {
            AudioCueIds.PlayerVoiceAttack,
            AudioCueIds.PlayerVoiceHit,
            AudioCueIds.PlayerVoiceDotHit,
            AudioCueIds.PlayerVoiceDeath
        };

        // 2026-09 발주 A·B 반입(cs:1374) — 상태이상 10 + 새 발화 지점 18. 전부 실제 클립이어야 한다(placeholder 아님).
        private static readonly string[] SoundOrder2026_09CueIds =
        {
            AudioCueIds.EffectWeaken,
            AudioCueIds.EffectVulnerable,
            AudioCueIds.EffectSeal,
            AudioCueIds.EffectTorchLight,
            AudioCueIds.EffectBossAura,
            AudioCueIds.EffectMight,
            AudioCueIds.EffectUnknown,
            AudioCueIds.EffectGuard,
            AudioCueIds.EffectStealth,
            AudioCueIds.EffectInvincible,
            AudioCueIds.ItemFlaskUse,
            AudioCueIds.ItemBeadUse,
            AudioCueIds.CombatAmbush,
            AudioCueIds.EffectExpire,
            AudioCueIds.EffectNegated,
            AudioCueIds.TrapSpawn,
            AudioCueIds.CurseInjected,
            AudioCueIds.BossWeakSpotHit,
            AudioCueIds.ShopBuy,
            AudioCueIds.ShopDenied,
            AudioCueIds.ShopCardRemove,
            AudioCueIds.ServiceRest,
            AudioCueIds.ServiceRefine,
            AudioCueIds.MoneyGain,
            AudioCueIds.RewardRelicAcquire,
            AudioCueIds.RewardReroll,
            AudioCueIds.RewardLootAppear,
            AudioCueIds.RewardSkip,
            AudioCueIds.MonsterStealthHide
        };

        [Test]
        public void SoundCatalogAssetWiresEvery2026_09OrderCueToADeliveredClip()
        {
            var catalog = LoadCatalog();
            var monsterCues = new[] { "M008", "M009", "M010", "M012", "M013", "M014" }
                .SelectMany(id => new[] { AudioCueIds.MonsterAttack(id), AudioCueIds.MonsterHit(id), AudioCueIds.MonsterDeath(id) });
            var patternCues = Enumerable.Range(5, 21).Where(index => index != 24).Select(index => $"S{index:000}");

            foreach (var cueId in SoundOrder2026_09CueIds.Concat(monsterCues).Concat(patternCues))
            {
                Assert.That(catalog.TryGetEntry(cueId, out var entry), Is.True, $"{cueId}: SoundCatalog.asset에 엔트리가 없다 — 매니페스트 임포트 누락.");
                Assert.That(entry.Clip, Is.Not.Null, $"{cueId}: 클립이 비었다.");
                var clipPath = AssetDatabase.GetAssetPath(entry.Clip);
                Assert.That(clipPath, Does.StartWith("Assets/Sounds/SFX/"), $"{cueId}: 납품 클립이 아니라 {clipPath}를 물고 있다.");
                Assert.That(clipPath, Does.Not.Contain("Placeholder"), $"{cueId}: placeholder를 물고 있다.");
            }
        }

        [Test]
        public void SoundCatalogAssetContainsP0CueIdsWithPlaceholderClips()
        {
            var catalog = LoadCatalog();

            foreach (var cueId in P0CueIds)
            {
                Assert.That(catalog.TryGetEntry(cueId, out var entry), Is.True, cueId);
                Assert.That(entry.Clip, Is.Not.Null, cueId);
                Assert.DoesNotThrow(() => catalog.TryBeginPlayback(cueId, 100f, out _), cueId);
            }
        }

        [Test]
        public void SoundCatalogAssetContainsP1PlaceholderEntriesWithoutRoutingAssumptions()
        {
            var catalog = LoadCatalog();

            foreach (var cueId in P1CatalogOnlyCueIds)
            {
                Assert.That(catalog.TryGetEntry(cueId, out var entry), Is.True, cueId);
                Assert.That(entry.Clip, Is.Not.Null, cueId);
            }
        }

        [Test]
        public void SoundCatalogAssetContainsMonsterSpecificCueIds()
        {
            var catalog = LoadCatalog();

            foreach (var cueId in MonsterCueIds)
            {
                Assert.That(catalog.TryGetEntry(cueId, out var entry), Is.True, cueId);
                Assert.That(entry.Clip, Is.Not.Null, cueId);
            }
        }

        [Test]
        public void SoundCatalogAssetContainsPlayerVoiceCueIds()
        {
            var catalog = LoadCatalog();

            foreach (var cueId in PlayerVoiceCueIds)
            {
                Assert.That(catalog.TryGetEntry(cueId, out var entry), Is.True, cueId);
                Assert.That(entry.Clip, Is.Not.Null, cueId);
            }
        }

        [Test]
        public void FogRevealCueUsesAssignedStatusEffectClip()
        {
            var catalog = LoadCatalog();

            Assert.That(catalog.TryGetEntry(AudioCueIds.FogTileRevealed, out var entry), Is.True);
            Assert.That(entry.Clip, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(entry.Clip), Is.EqualTo("Assets/Sounds/SFX/StatusEffect/효과_안개공개.wav"));
        }

        private static SoundCatalog LoadCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<SoundCatalog>(TestAssetPaths.SoundCatalogAsset);
            Assert.That(catalog, Is.Not.Null);
            return catalog;
        }
    }
}

