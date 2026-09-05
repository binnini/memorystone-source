using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 미지(Unknown)의 계약을 고정한다 — 예고만 사라지고 <b>규칙은 그대로</b>(D-4),
    /// 새는 구멍 없이 전부 가려지며(D-5), 대신 몬스터 자기 좌표에 '?' 표식이 뜬다.
    /// </summary>
    public sealed class UnknownIntentHidingTests
    {
        [Test]
        public void HiddenMonster_EmitsNoMoveOrAttackForecast()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();

            var before = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(p => p.MonsterId == monster.Id);

            Inject(state, StatusEffectKind.Unknown, monster.Id);

            var after = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(p => p.MonsterId == monster.Id);

            Assert.That(after.IsIntentHidden, Is.True);
            Assert.That(after.AttackRangeCoords, Is.Empty, "공격 범위 예고가 남으면 은폐가 아니다.");
            Assert.That(after.WillMove, Is.False, "이동 예고도 함께 사라져야 한다 — 둘 다 숨기는 것이 결정이다(D-4).");
            Assert.That(after.PredictedMoveCoord, Is.EqualTo(monster.Coord));
            Assert.That(before.MonsterId, Is.EqualTo(after.MonsterId), "몬스터 자체는 예고 목록에서 사라지지 않는다.");
        }

        [Test]
        public void HiddenMonster_LeaksNoPatternInformation()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();
            Inject(state, StatusEffectKind.Unknown, monster.Id);

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(p => p.MonsterId == monster.Id);

            Assert.That(preview.AttackPatternId, Is.Empty);
            Assert.That(preview.AttackPatternDisplayName, Is.Empty);
            Assert.That(preview.AttackPatternDamage, Is.Zero, "예고 피해가 새면 툴팁과 디버그 표면이 숨긴 값을 말한다.");
            Assert.That(preview.AttackPatternStatusEffects, Is.Empty);
            Assert.That(preview.AttackPatternKnockbackDistance, Is.Zero);
        }

        // --- 🔴 규칙은 건드리지 않는다 -------------------------------------------------------------

        [Test]
        public void HiddenMonster_CanStillAttack()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();
            Inject(state, StatusEffectKind.Unknown, monster.Id);

            Assert.That(IsAttackBlocked(state, monster.Id), Is.False,
                "미지는 예고만 감춘다. 기절과 같은 술어로 묶으면 미지 몬스터가 공격을 못 하게 되고, "
                + "그 증상은 '왜 가끔 안 때리지?'로 나타나 원인을 찾기 어렵다.");
        }

        [Test]
        public void StunnedMonster_IsNotMarkedAsHidden()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();
            Inject(state, StatusEffectKind.Stun, monster.Id);

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(p => p.MonsterId == monster.Id);

            Assert.That(preview.AttackRangeCoords, Is.Empty, "기절도 공격 예고를 비운다.");
            Assert.That(preview.IsIntentHidden, Is.False,
                "하지만 기절은 '가려진 것'이 아니라 '실제로 취소된 것'이다 — '?' 표식이 뜨면 거짓말이 된다.");
        }

        // --- 오버레이 캐시 -------------------------------------------------------------------------

        [Test]
        public void OverlaySignature_ChangesWhenHidingToggles()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();

            var before = state.ComputeMonsterIntentOverlaySignature(includeUnrevealed: true);
            Inject(state, StatusEffectKind.Unknown, monster.Id);
            var after = state.ComputeMonsterIntentOverlaySignature(includeUnrevealed: true);

            Assert.That(after, Is.Not.EqualTo(before),
                "서명이 안 바뀌면 오버레이 캐시가 갱신되지 않아 미지를 걸어도 예고가 화면에 그대로 남는다. "
                + "이 트랙에서 가장 놓치기 쉬운 한 줄이다.");
        }

        // --- '?' 표식 -----------------------------------------------------------------------------

        [Test]
        public void HiddenMonster_GetsAQuestionMarkOnItsOwnTile()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();
            Inject(state, StatusEffectKind.Unknown, monster.Id);

            var previews = state.GetMonsterIntentPreviews(includeUnrevealed: true);
            var annotations = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(previews);

            var marker = annotations.FirstOrDefault(a => a.Coord == monster.Coord);
            Assert.That(marker, Is.Not.Null, "가려졌다는 사실 자체는 알려야 한다 — 몬스터 칸에 표식이 뜬다.");
            Assert.That(
                marker.Effects.Contains(StatusEffectKind.Unknown),
                Is.True,
                "맵 오버레이가 공격 예고 셀이 아닌 곳에 아이콘을 그리는 유일한 경우다.");
        }

        [Test]
        public void VisibleMonster_GetsNoQuestionMark()
        {
            var state = CombatState.CreateDefaultDemo();

            var annotations = CombatOverlayQuery.BuildMonsterAttackStatusIconCells(
                state.GetMonsterIntentPreviews(includeUnrevealed: true));

            Assert.That(
                annotations.Any(a => a.Effects.Contains(StatusEffectKind.Unknown)),
                Is.False,
                "가려지지 않은 몬스터에 '?'가 뜨면 표식의 뜻이 무너진다.");
        }

        // --- 자기부여 저작 경로 (D-4) ---------------------------------------------------------------

        [Test]
        public void SelfTargetedPattern_AppliesTheStatusToTheMonsterItself()
        {
            var state = CombatState.CreateDefaultDemo();
            var monster = state.Monsters.First();

            var pattern = new MonsterAttackPattern(
                "TEST_SELF_VEIL",
                "장막",
                range: 0,
                areaRadius: 0,
                damage: 0,
                effectRef: string.Empty,
                targeting: "self",
                weight: 1,
                shapeId: string.Empty,
                statusEffects: new[] { StatusEffectKind.Unknown },
                statusEffectDurationTurns: 2);

            ApplySelfPattern(state, monster.Id, pattern);

            Assert.That(state.IsMonsterIntentHidden(monster.Id), Is.True,
                "targeting=self면 상태가 몬스터 자신에게 걸려야 한다 — 이 경로가 없으면 미지를 걸 방법이 없다.");
            Assert.That(
                state.ActiveEffects.Any(e => e.TargetUnitId == state.Player.Id && e.Kind == StatusEffectKind.Unknown),
                Is.False,
                "자기부여가 플레이어에게 새면 안 된다.");
        }

        // --- 분류 ---------------------------------------------------------------------------------

        [Test]
        public void Unknown_IsABuffAndNotCleansable()
        {
            Assert.That(StatusEffectInfo.IsBuff(StatusEffectKind.Unknown), Is.True,
                "몬스터에게 이로운 효과다.");
            Assert.That(StatusEffectInfo.IsCleansable(StatusEffectKind.Unknown), Is.False,
                "플레이어 정화의 사정권이 아니다 — 축을 명시적으로 닫아 두지 않으면 "
                + "몬스터 정화가 생기는 날 조용히 열린다.");
        }

        // --- 헬퍼 ---------------------------------------------------------------------------------

        private static void Inject(CombatState state, StatusEffectKind kind, string unitId)
        {
            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, kind, unitId, remainingTurns: 3, amount: 0, "test"));
        }

        // 자기부여는 private 경로(ApplyMonsterSelfStatusEffects)라 저작 패턴을 직접 태워 확인한다.
        private static void ApplySelfPattern(CombatState state, string monsterId, MonsterAttackPattern pattern)
        {
            var monsters = (System.Collections.IEnumerable)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);

            foreach (var monster in monsters)
            {
                var id = (string)monster.GetType().GetProperty("Id").GetValue(monster);
                if (!string.Equals(id, monsterId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                var method = typeof(CombatState).GetMethod(
                    "ApplyMonsterAttackStatusEffects", BindingFlags.Instance | BindingFlags.NonPublic);
                method.Invoke(state, new[] { monster, pattern, null, string.Empty });
                return;
            }

            Assert.Fail("몬스터를 찾지 못했다.");
        }

        private static bool IsAttackBlocked(CombatState state, string monsterId)
        {
            var monsters = (System.Collections.IEnumerable)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);

            foreach (var monster in monsters)
            {
                var id = (string)monster.GetType().GetProperty("Id").GetValue(monster);
                if (!string.Equals(id, monsterId, System.StringComparison.Ordinal))
                {
                    continue;
                }

                var method = typeof(CombatState).GetMethod(
                    "IsMonsterAttackBlocked", BindingFlags.Instance | BindingFlags.NonPublic);
                return (bool)method.Invoke(state, new[] { monster });
            }

            Assert.Fail("몬스터를 찾지 못했다.");
            return false;
        }
    }
}
