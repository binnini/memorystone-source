using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 자기부여 패턴(targeting=self · §21.8 제안 8)의 계약. 술어 하나(<see cref="MonsterAttackPattern.IsSelfTargeted"/>)를
    /// 네 이음새가 나눠 읽는다: 선택(항상 후보) · 커버 판정(제외 — 이동/도약이 오판하면 추격이 멈춘다) ·
    /// 예고(위험 칸 없음) · 집행(상태를 자신에게 · 피해 없음). 여기의 핀은 그 이음새들이 갈라지지 않게 한다.
    /// </summary>
    public sealed class MonsterSelfBuffPatternTests
    {
        private const string MonsterId = "buffer-01";
        private const string DefinitionId = "M801";
        private const int MeleeDamage = 4;

        [Test]
        public void SelfBuffPatternResolvesOnTheMonsterWithoutTouchingThePlayer()
        {
            // 미지 장막 하나만 가진 몬스터: 매 턴 자기부여만 한다. 플레이어가 붙어 있어도 피해가 없어야 하고,
            // 버프는 실제로 걸려야 한다(Unknown → 예고 은폐 술어가 공개 표면이다).
            var state = CreateState(SelfPattern("Unknown", cooldownTurns: 0));

            RunFullTurn(state);

            Assert.That(state.Player.Hp, Is.EqualTo(200), "자기부여 패턴은 피해 0이다 — 인접해 있어도 플레이어를 건드리지 않는다.");
            Assert.That(state.IsMonsterIntentHidden(MonsterId), Is.True,
                "버프가 실제로 자신에게 걸렸다 — Unknown이면 예고 은폐 술어가 참이 된다.");
        }

        [Test]
        public void SelfBuffPatternShowsNoDangerCellsInTheCommittedIntentPreview()
        {
            // Strength로 확인한다(Unknown은 예고를 통째로 비워 이 이음새를 가린다). 커밋 시점 예고에
            // 위험 칸이 나오면 "서 있으면 맞는 칸" 어휘가 0피해 버프에 거짓말을 한다.
            var state = CreateState(SelfPattern("Strength", cooldownTurns: 0));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(candidate => candidate.MonsterId == MonsterId);
            Assert.That(preview.AttackRangeCoords, Is.Empty,
                "자기부여 패턴의 예고에는 위험 칸이 없어야 한다.");
        }

        [Test]
        public void StrengthRoarDoublesTheFollowingAttack()
        {
            // 포효(Strength 2턴) → 다음 턴 근접 공격이 +100%로 들어간다. 선택이 가중치 추첨이므로
            // 랩과 같은 강제 핀(ForcedAttackPatternIndex)으로 순서를 고정한다(§25 — 핀은 선택만 우회하고
            // 집행·상태 부여·쿨다운은 실제 경로를 그대로 탄다).
            var state = CreateState(
                new MonsterAttackPattern("AT01", "물기", 1, 0, MeleeDamage, cooldownTurns: 0),
                SelfPattern("Strength", cooldownTurns: 5));

            // ⚠️ 계획은 이전 턴 말에 이미 서 있다 — 핀을 바꾼 뒤에는 재계획까지 해야 다음 결의에 반영된다.
            // DebugForceAttackPattern(§25 랩 API)이 그 두 가지를 함께 한다(직접 필드 대입은 낡은 계획을 남긴다).
            Assert.That(state.DebugForceAttackPattern(MonsterId, 1, out _), Is.True); // 포효
            RunFullTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(200), "포효 턴에는 피해가 없다.");

            Assert.That(state.DebugForceAttackPattern(MonsterId, 0, out _), Is.True); // 근접 공격
            RunFullTurn(state);
            Assert.That(state.Player.Hp, Is.EqualTo(200 - MeleeDamage * 2),
                "강화(+100%)가 걸린 다음 공격은 두 배로 들어간다 — 몬스터 Strength 소비 배선의 핀이다.");
        }

        /// <summary>
        /// 2026-08-20 #16의 이음새: 자기부여는 커버 판정을 건너뛰어 이동 FSM 의도(IntentType)가
        /// Attack으로 승격되지 않는다 — IntentType만 보는 소비자(의도 배지)는 버프 턴을 통째로
        /// 놓친다. 예고가 IsSelfBuffIntent 축을 따로 실어야 배지·오버레이가 이 턴을 읽을 수 있다.
        /// </summary>
        [Test]
        public void SelfBuffTurnPublishesSelfBuffIntentInThePreview()
        {
            var state = CreateState(SelfPattern("Strength", cooldownTurns: 0));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(candidate => candidate.MonsterId == MonsterId);
            Assert.That(preview.IsSelfBuffIntent, Is.True,
                "자기부여 턴의 예고는 IsSelfBuffIntent를 실어야 한다 — IntentType은 Attack이 아니다.");
            Assert.That(preview.AttackPatternStatusEffects, Does.Contain(StatusEffectKind.Strength),
                "걸릴 상태 종류가 예고에 실려야 배지가 그릴 그림을 안다.");
        }

        /// <summary>A031 몸 사리기의 축: 상태 없이 방어막만 두르는 자기부여도 예고에 수치가 실린다.</summary>
        [Test]
        public void SelfBuffShieldGainIsPublishedInThePreview()
        {
            var state = CreateState(new MonsterAttackPattern(
                "AT91", "몸 사리기", range: 1, areaRadius: 0, damage: 0,
                targeting: MonsterAttackPattern.SelfTargetingValue, shieldGain: 4, cooldownTurns: 0));

            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(candidate => candidate.MonsterId == MonsterId);
            Assert.That(preview.IsSelfBuffIntent, Is.True);
            Assert.That(preview.AttackPatternShieldGain, Is.EqualTo(4),
                "두를 방어막 수치가 예고에 실려야 한다.");
        }

        /// <summary>
        /// 2026-08-20 #2의 이음새: 집행 기록이 자기부여를 표시해야 어셈블러가 암시야 "기습!" 판정에서
        /// 제외할 수 있다(AttackedPlayer는 가시 공격 애니용으로 참을 유지한다).
        /// </summary>
        [Test]
        public void SelfBuffResolutionRecordIsMarkedSelfTargetedButStillPresentsAsAction()
        {
            var state = CreateState(SelfPattern("Strength", cooldownTurns: 0));

            RunFullTurn(state);

            var record = state.LastMonsterActionRecords.Single(candidate => candidate.MonsterId == MonsterId);
            Assert.That(record.SelfTargetedAttack, Is.True, "자기부여 집행 기록에 표식이 없다 — 기습 오발의 뿌리.");
            Assert.That(record.AttackedPlayer, Is.True, "가시 연출(버프 모션)은 AttackedPlayer 경로가 지탱한다.");
        }

        /// <summary>
        /// 2026-08-20 #10: 자기부여 턴에는 공격 예고(붉은 위험 해치)가 통째로 비므로 유저 눈에는
        /// "이 몬스터는 이번 턴 아무것도 안 한다"로 읽혔다. 전용 오버레이가 그 공백을 몬스터
        /// <b>제자리 footprint</b>에 메운다. 위험 해치와 <b>배타적</b>이라는 것까지 함께 잠근다.
        /// </summary>
        [Test]
        public void SelfBuffTurnHighlightsTheMonsterFootprintAndNoDangerCells()
        {
            var state = CreateState(SelfPattern("Strength", cooldownTurns: 0));
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var query = new SeoulPlayup.Combat.Unity.CombatOverlayQuery();
            var selfBuffCells = query
                .GetMonsterSelfBuffHighlightCells(state, state.Map, includeUnrevealedMonsters: true)
                .ToList();
            var dangerCells = query
                .GetMonsterIntentAttackHighlightCells(state, state.Map, includeUnrevealedMonsters: true)
                .ToList();

            Assert.That(selfBuffCells, Is.EquivalentTo(new[] { new HexCoord(2, 0) }),
                "자기부여 예고는 몬스터가 서 있는 칸에 뜬다.");
            Assert.That(dangerCells, Is.Empty,
                "같은 턴에 위험 해치는 뜨지 않는다 — 두 어휘가 섞이면 0피해 버프가 '밟으면 아픈 칸'으로 읽힌다.");
        }

        /// <summary>공격 턴에는 자기부여 오버레이가 뜨지 않는다(반대 방향 회귀 잠금).</summary>
        [Test]
        public void PlainAttackTurnPublishesNoSelfBuffHighlight()
        {
            var state = CreateState(new MonsterAttackPattern("AT01", "물기", 1, 0, MeleeDamage, cooldownTurns: 0));
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();

            var query = new SeoulPlayup.Combat.Unity.CombatOverlayQuery();
            Assert.That(
                query.GetMonsterSelfBuffHighlightCells(state, state.Map, includeUnrevealedMonsters: true).ToList(),
                Is.Empty);
        }

        // --- helpers ------------------------------------------------------------------------------

        private static MonsterAttackPattern SelfPattern(string statusKind, int cooldownTurns)
        {
            return new MonsterAttackPattern(
                "AT90",
                "자기부여",
                range: 1,
                areaRadius: 0,
                damage: 0,
                targeting: MonsterAttackPattern.SelfTargetingValue,
                statusEffects: new[] { (StatusEffectKind)System.Enum.Parse(typeof(StatusEffectKind), statusKind) },
                statusEffectDurationTurns: 2,
                cooldownTurns: cooldownTurns);
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
            var monsterCoord = new HexCoord(2, 0); // 인접 — 피해 0 계약을 가장 엄격하게 묻는 배치.
            return new CombatState(
                CombatState.CreateDemoMap(6),
                playerCoord,
                new[] { new MonsterConfig(MonsterId, monsterCoord, 30, definitionId: DefinitionId) },
                new CombatConfig(200, 30, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "self-buff-test-catalog",
                    "Self Buff Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            DefinitionId,
                            "버프꾼",
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
