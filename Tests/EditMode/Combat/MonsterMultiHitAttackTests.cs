using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 몬스터 다단 히트(§21.8 제안 6 · <see cref="MonsterAttackPattern.HitCount"/>)의 계약:
    /// <c>Damage</c>는 히트당 피해(F05 <c>HitsPerTick</c> 규약), 방어는 히트마다 갈리고,
    /// 상태 부여는 히트 수와 무관하게 공격당 1회다.
    /// </summary>
    public sealed class MonsterMultiHitAttackTests
    {
        private const string MonsterId = "flamer-01";
        private const string DefinitionId = "M802";

        [Test]
        public void MultiHitAttackDealsPerHitDamageTimesHitCount()
        {
            var state = CreateState(Pattern(damage: 2, hitCount: 3));
            RunFullTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(200 - 6),
                "히트당 2 × 3히트 = 총 6 — Damage는 총량이 아니라 히트당 피해다.");
        }

        [Test]
        public void BlockIsConsumedPerHitSoLaterHitsBreakThrough()
        {
            // 방어 3 vs 2피해 3히트: 1타 흡수(잔여 1) → 2타 1 흡수 + 1 관통 → 3타 2 관통 = 피해 3.
            // 단일 히트 6이었다면 3 흡수 + 3 관통으로 산술이 같지만, 이벤트가 "방어!→피해→피해"로
            // 갈리는 것이 다단의 체감이다 — 여기서는 산술의 등가와 최종 상태를 고정한다.
            var state = CreateState(Pattern(damage: 2, hitCount: 3));
            state.Player.AddBlock(3);

            RunFullTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(200 - 3), "방어 3을 뚫고 들어온 피해는 3이다.");
            Assert.That(state.Player.Block, Is.Zero, "방어는 앞 히트부터 차례로 소모된다.");
        }

        [Test]
        public void StatusEffectsApplyOncePerAttackNotPerHit()
        {
            // 🔴 상태를 히트당 돌리면 스택형 상태(중독)가 히트 수만큼 조용히 배가된다 — 공격당 1회 계약의 핀.
            var state = CreateState(Pattern(damage: 2, hitCount: 3, statusEffects: new[] { StatusEffectKind.Poison }));
            RunFullTurn(state);

            var poison = state.ActiveEffects.Where(effect => effect.Kind == StatusEffectKind.Poison).ToList();
            Assert.That(poison, Has.Count.EqualTo(1), "중독은 공격당 한 번만 부여된다.");
            Assert.That(poison.Single().Amount, Is.EqualTo(StatusEffectInfo.DefaultAmount(StatusEffectKind.Poison)),
                "부여량도 1회분이어야 한다 — 히트 수만큼 배가되면 수치 저작이 폭주한다.");
        }

        [Test]
        public void FullyBlockedAttackStillApplesItsStatusEffect()
        {
            // 🔴 「방어에 성공하면 상태이상이 부여되지 않는다」(2026-09-01 #2)의 규칙층 핀.
            //    상태 부여는 피해 루프보다 <b>앞</b>에서 공격당 1회로 돌므로 방어막과 무관해야 한다.
            //    방어 10 vs 2피해 3히트 = 한 대도 안 들어온다. 그래도 중독은 걸린다.
            var state = CreateState(Pattern(damage: 2, hitCount: 3, statusEffects: new[] { StatusEffectKind.Poison }));
            state.Player.AddBlock(10);

            RunFullTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(200), "전제: 피해가 한 점도 안 들어왔다.");
            Assert.That(
                state.ActiveEffects.Count(effect => effect.Kind == StatusEffectKind.Poison), Is.EqualTo(1),
                "방어는 피해를 막지 상태이상을 막지 않는다 — 막는 것은 수호(StatusNegated)뿐이다.");
        }

        // --- helpers ------------------------------------------------------------------------------

        private static MonsterAttackPattern Pattern(int damage, int hitCount, StatusEffectKind[] statusEffects = null)
        {
            return new MonsterAttackPattern(
                "AT10",
                "연타",
                range: 1,
                areaRadius: 0,
                damage: damage,
                statusEffects: statusEffects,
                statusEffectDurationTurns: 2,
                cooldownTurns: 0,
                hitCount: hitCount);
        }

        private static void RunFullTurn(CombatState state)
        {
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static CombatState CreateState(params MonsterAttackPattern[] patterns)
        {
            var playerCoord = new HexCoord(1, 0);
            var monsterCoord = new HexCoord(2, 0);
            return new CombatState(
                CombatState.CreateDemoMap(6),
                playerCoord,
                new[] { new MonsterConfig(MonsterId, monsterCoord, 30, definitionId: DefinitionId) },
                new CombatConfig(200, 30, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "multi-hit-test-catalog",
                    "Multi Hit Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            DefinitionId,
                            "연타꾼",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 30,
                            attackSpeed: 1,
                            attackPatterns: patterns)
                    }));
        }
    }
}
