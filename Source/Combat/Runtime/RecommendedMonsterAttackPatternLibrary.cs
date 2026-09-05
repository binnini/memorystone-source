using System;
using System.Collections.Generic;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Candidate monster attack pattern sets kept out of the active monster catalog until designers opt in.
    /// </summary>
    public static class RecommendedMonsterAttackPatternLibrary
    {
        public static IReadOnlyList<MonsterAttackPattern> CreateTestRangedRecommendations(CombatConfig config)
        {
            return new[]
            {
                new MonsterAttackPattern(
                    "test-ranged-precision-shot",
                    "정밀 사격",
                    3,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.damage.ranged.precision",
                    "player_in_range",
                    4,
                    AttackShapeLibrary.Line3),

                new MonsterAttackPattern(
                    "test-ranged-long-shot",
                    "장거리 저격",
                    4,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.ranged.long",
                    "player_in_range",
                    2,
                    AttackShapeLibrary.Line4),

                new MonsterAttackPattern(
                    "test-ranged-piercing-shot",
                    "관통 사격",
                    4,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.damage.pierce.placeholder",
                    "player_in_range",
                    2,
                    AttackShapeLibrary.Line4),

                new MonsterAttackPattern(
                    "test-ranged-spread-burst",
                    "산탄 분사",
                    3,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.spread.placeholder",
                    "player_area_in_range",
                    2,
                    AttackShapeLibrary.ConeMid),

                new MonsterAttackPattern(
                    "test-ranged-t-barrage",
                    "T자 탄막",
                    3,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.t_barrage.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.TForward),

                new MonsterAttackPattern(
                    "test-ranged-close-panic-shot",
                    "근접 견제탄",
                    1,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.close_ranged",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.ConeNear),

                new MonsterAttackPattern(
                    "test-ranged-cross-bomb",
                    "십자 폭탄",
                    2,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.cross.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.CrossNear),

                new MonsterAttackPattern(
                    "test-ranged-slow-orb-plus",
                    "둔화 구체",
                    4,
                    0,
                    1,
                    "attack.slow.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Line4),

                new MonsterAttackPattern(
                    "test-ranged-marking-shot",
                    "표식 사격",
                    3,
                    0,
                    1,
                    "attack.mark.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Line3),

                new MonsterAttackPattern(
                    "test-ranged-danger-zone",
                    "위험지대 포격",
                    3,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 3),
                    "attack.zone.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.ConeMid),

                new MonsterAttackPattern(
                    "test-ranged-wide-breath",
                    "광역 브레스",
                    3,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.breath.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.ConeWide),

                new MonsterAttackPattern(
                    "test-ranged-split-ray",
                    "분기 광선",
                    3,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.split_ray.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.VSplit),

                new MonsterAttackPattern(
                    "test-ranged-side-pincer",
                    "측면 협공탄",
                    2,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.pincer.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.Pincer),

                new MonsterAttackPattern(
                    "test-ranged-crossfire",
                    "교차 폭탄",
                    2,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 3),
                    "attack.damage.crossfire.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.CrossFar),

                new MonsterAttackPattern(
                    "test-ranged-orbital-burst",
                    "주변 폭발탄",
                    1,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.orbital.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.RingNear),

                new MonsterAttackPattern(
                    "test-ranged-sniper-mark",
                    "저격 표식",
                    4,
                    0,
                    1,
                    "attack.mark.sniper.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Line4),

                new MonsterAttackPattern(
                    "test-ranged-armor-melter",
                    "장갑 용해탄",
                    3,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.defense_down.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Line3),

                new MonsterAttackPattern(
                    "test-ranged-rooting-bolt",
                    "속박 볼트",
                    3,
                    0,
                    1,
                    "attack.root.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Line3),

                new MonsterAttackPattern(
                    "test-ranged-mortar-lob",
                    "곡사 박격포",
                    4,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 3),
                    "attack.damage.mortar.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.CrossNear),

                new MonsterAttackPattern(
                    "test-ranged-retreat-shot",
                    "후퇴 사격",
                    2,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.retreat.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Line2)
            };
        }

        public static IReadOnlyList<MonsterAttackPattern> CreateTestMeleeRecommendations(CombatConfig config)
        {
            return new[]
            {
                new MonsterAttackPattern(
                    "test-melee-quick-slash",
                    "빠른 베기",
                    1,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.damage.quick",
                    "player_in_range",
                    4,
                    AttackShapeLibrary.Single),

                new MonsterAttackPattern(
                    "test-melee-heavy-bite",
                    "강한 물어뜯기",
                    1,
                    0,
                    config.EnemyAttackDamage + 1,
                    "attack.damage.heavy",
                    "player_in_range",
                    2,
                    AttackShapeLibrary.Single),

                new MonsterAttackPattern(
                    "test-melee-wide-cleave",
                    "넓은 휩쓸기",
                    1,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.damage.cleave.placeholder",
                    "player_area_in_range",
                    2,
                    AttackShapeLibrary.ConeNear),

                new MonsterAttackPattern(
                    "test-melee-tail-sweep",
                    "꼬리 휩쓸기",
                    1,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.sweep.placeholder",
                    "player_area_in_range",
                    2,
                    AttackShapeLibrary.CrossNear),

                new MonsterAttackPattern(
                    "test-melee-lunge",
                    "돌진 찌르기",
                    2,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.damage.lunge",
                    "player_in_range",
                    2,
                    AttackShapeLibrary.Line2),

                new MonsterAttackPattern(
                    "test-melee-stun-bash-plus",
                    "기절 강타",
                    1,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.stun.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Single),

                new MonsterAttackPattern(
                    "test-melee-guard-breaker",
                    "방어 깨기",
                    1,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.block_break.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Single),

                new MonsterAttackPattern(
                    "test-melee-ground-shock",
                    "지면 충격",
                    1,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.shock.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.ConeNear),

                new MonsterAttackPattern(
                    "test-melee-reach-swipe",
                    "긴 팔 휘두르기",
                    2,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.reach",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Line2),

                new MonsterAttackPattern(
                    "test-melee-tactical-hook",
                    "갈고리 베기",
                    2,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.pull.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.TForward),

                new MonsterAttackPattern(
                    "test-melee-spinning-cleave",
                    "회전 난무",
                    1,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.spin.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.RingNear),

                new MonsterAttackPattern(
                    "test-melee-whirlwind",
                    "소용돌이 베기",
                    2,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 3),
                    "attack.damage.whirlwind.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.CrossFar),

                new MonsterAttackPattern(
                    "test-melee-horn-charge",
                    "뿔 돌진",
                    3,
                    0,
                    config.EnemyAttackDamage,
                    "attack.damage.charge.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Line3),

                new MonsterAttackPattern(
                    "test-melee-forked-claw",
                    "쌍갈래 발톱",
                    2,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.damage.forked.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.VSplit),

                new MonsterAttackPattern(
                    "test-melee-side-maul",
                    "측면 난타",
                    2,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.damage.side_maul.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.Pincer),

                new MonsterAttackPattern(
                    "test-melee-intimidating-roar",
                    "위협 포효",
                    1,
                    1,
                    0,
                    "attack.weaken.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.ConeNear),

                new MonsterAttackPattern(
                    "test-melee-bleeding-rake",
                    "출혈 할퀴기",
                    1,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.bleed.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Single),

                new MonsterAttackPattern(
                    "test-melee-knockback-palm",
                    "밀쳐내기 장타",
                    1,
                    0,
                    Math.Max(1, config.EnemyAttackDamage - 2),
                    "attack.push.placeholder",
                    "player_in_range",
                    1,
                    AttackShapeLibrary.Single),

                new MonsterAttackPattern(
                    "test-melee-crushing-leap",
                    "압살 도약",
                    2,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.damage.leap.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.CrossNear),

                new MonsterAttackPattern(
                    "test-melee-boss-sweep",
                    "보스급 대형 휩쓸기",
                    3,
                    1,
                    Math.Max(1, config.EnemyAttackDamage - 1),
                    "attack.damage.boss_sweep.placeholder",
                    "player_area_in_range",
                    1,
                    AttackShapeLibrary.ConeWide)
            };
        }
    }
}
