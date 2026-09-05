using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 몬스터 은신(요괴 트랙 §4-1 · 어둑시니)과 성장 「어둠 먹기」(§4-2), 매복 프로파일(§4-6).
    ///
    /// <para>🔴 이 스위트의 중심은 <b>은신 노출이 안개 규칙을 덮지 않는다</b>는 것이다(§7 ⑧).
    /// 시야 밖 몬스터는 은신과 무관하게 이미 안 보이고, 은신이 더하는 것은 「시야 안인데도 안 보임」
    /// 하나뿐이다 — 두 술어를 하나로 합치면 드러난 몬스터가 안개 속에서도 보이게 된다.</para>
    /// </summary>
    public sealed class MonsterStealthTests
    {
        private const string MonsterId = "shade";
        private const string DefinitionId = "M009T";

        // ------------------------------------------------------------------ 안개 ∧ 은신

        [Test]
        public void HiddenMonsterIsMaskedEvenWhenItsCellIsRevealed()
        {
            var state = CreateState();
            var monster = FirstMonster(state);

            Assert.That(state.GetVisibility(monster.Coord), Is.EqualTo(HexCellVisibility.Revealed),
                "전제: 이 칸은 안개가 걷혀 있다 — 그래야 '시야 안인데도 안 보임'을 물을 수 있다.");
            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.True);
        }

        [Test]
        public void RevealingStealthDoesNotTouchTheFogRule()
        {
            // 🔴 §7 ⑧ 절반: 노출은 은신 술어만 끈다. 안개는 그대로여야 한다 — 노출이 안개를 걷어내면
            // 두 규칙이 하나로 합쳐진 것이고, 그때부터 "드러난 몬스터는 안개 속에서도 보인다"가 된다.
            var state = CreateState();
            var monster = FirstMonster(state);
            var before = state.Map.AllCells.ToDictionary(cell => cell.Coord, cell => state.GetVisibility(cell.Coord));

            Reveal(state, monster);

            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.False, "노출됐다.");
            foreach (var pair in before)
            {
                Assert.That(state.GetVisibility(pair.Key), Is.EqualTo(pair.Value),
                    $"노출이 {pair.Key}의 안개를 바꿨다 — 두 규칙이 섞였다.");
            }
        }

        [Test]
        public void StealthMaskingIsIndependentOfWhereTheMonsterStands()
        {
            // 🔴 §7 ⑧ 나머지 절반: 은신 술어는 <b>좌표를 보지 않는다</b>. 안개 판정을 이 술어 안으로
            // 끌어들이면(= 두 조건을 합치면) 서 있는 칸에 따라 답이 달라지기 시작한다.
            var state = CreateState();
            var monster = FirstMonster(state);

            foreach (var cell in state.Map.AllCells.Take(20))
            {
                monster.Coord = cell.Coord;
                Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.True,
                    $"{cell.Coord}: 은신은 자리와 무관하다.");
            }
        }

        [Test]
        public void AMonsterWithoutTheTraitIsNeverStealthHidden()
        {
            var state = CreateState(hiddenTraitRef: string.Empty, hiddenTraitParam: string.Empty);
            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.False);
        }

        [Test]
        public void AHiddenMonsterDrawsNoMovePathUntilItIsRevealed()
        {
            // 2026-09-05 실플레이: 어둑시니가 숨어 있는데 이동 예정 잔상(고스트+화살표)이 그려졌다.
            // 예고 산출부(GetMonsterIntentPreviews)는 은폐 술어를 봐서 이동 예고를 지웠지만, 잔상이
            // 읽는 경로 술어(GetMonsterMovePath)는 예측 좌표를 직접 읽어 다른 답을 냈다.
            var state = CreateState();
            var monster = FirstMonster(state);
            var destination = new HexCoord(1, 0);
            // 픽스처는 생성 직후 턴 계획이 이미 서 있다(planActive) — 경로 술어는 활성 계획을 먼저
            // 읽으므로 계획 쪽 목적지를 세운다. 예측 좌표도 같이 맞춰 두 갈래가 같은 답을 내게 한다.
            var moveIntent = new EnemyIntent(EnemyIntentType.Chase, 0, monster.Coord, destination);
            monster.TurnPlan = new MonsterTurnPlan(moveIntent, destination, moveIntent, isActive: true, canAttack: false);
            monster.IntentPredictedMoveCoord = destination;

            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.True, "전제: 지금 숨어 있다.");
            var hiddenPath = state.GetMonsterMovePath(MonsterId);
            Assert.That(hiddenPath.Count, Is.EqualTo(1), "숨은 몬스터의 경로는 제자리 하나여야 잔상이 안 그려진다.");
            Assert.That(hiddenPath[0], Is.EqualTo(monster.Coord));

            Reveal(state, monster);

            var revealedPath = state.GetMonsterMovePath(MonsterId);
            Assert.That(revealedPath.Count, Is.GreaterThanOrEqualTo(2), "드러나면 같은 계획 좌표로 경로가 다시 열린다.");
            Assert.That(revealedPath[revealedPath.Count - 1], Is.EqualTo(destination));
        }

        // ------------------------------------------------------------------ 노출 계기

        [Test]
        public void ScoutRevealsHiddenMonstersInItsArea()
        {
            // Q1 확정: 정찰에 「숨은 것을 드러내는」 쓸모가 붙는다.
            var state = CreateState();
            var monster = FirstMonster(state);
            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.True);

            Invoke(state, "RevealStealthMonstersInScoutArea", monster.Coord, 2);

            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.False);
            Assert.That(monster.StealthRevealTurnsRemaining, Is.EqualTo(2), "노출은 2턴(Q1 확정).");
        }

        [Test]
        public void ScoutOutsideTheRadiusLeavesTheMonsterHidden()
        {
            var state = CreateState();
            var monster = FirstMonster(state);

            Invoke(state, "RevealStealthMonstersInScoutArea", state.PlayerCoord, 0);

            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.True,
                $"정찰 반경 밖(거리 {state.PlayerCoord.DistanceTo(monster.Coord)})은 드러나지 않는다.");
        }

        [Test]
        public void RevealCountsDownAndTheMonsterHidesAgain()
        {
            var state = CreateState();
            var monster = FirstMonster(state);
            Reveal(state, monster);

            Invoke(state, "TickMonsterStealthPerTurn");
            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.False, "1턴 남았다.");

            Invoke(state, "TickMonsterStealthPerTurn");
            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.True, "2턴이 지나면 다시 숨는다.");
        }

        /// <summary>
        /// 🔴 2026-09-05 사용자 확정: <b>노출 카운터는 갱신되지 않는다.</b> 정찰을 겹치거나 계속
        /// 때리면 영영 안 숨던 종전 동작(Math.Max 재충전)은 배지가 든 「남은 턴」을 거짓말로 만든다 —
        /// 한 번 켜진 시계는 제 속도로 끝까지 간다.
        /// </summary>
        [Test]
        public void RevealTurnsNeverRefreshNoMatterHowOftenItIsFoundOrHit()
        {
            var state = CreateState();
            var monster = FirstMonster(state);
            Reveal(state, monster);
            var afterFirstReveal = monster.StealthRevealTurnsRemaining;

            Invoke(state, "TickMonsterStealthPerTurn");
            Assert.That(monster.StealthRevealTurnsRemaining, Is.EqualTo(afterFirstReveal - 1));

            // 다시 정찰에 걸려도, 맞아도 시계는 안 돌아간다.
            Reveal(state, monster);
            state.DamageMonster(monster, 1);
            Assert.That(
                monster.StealthRevealTurnsRemaining, Is.EqualTo(afterFirstReveal - 1),
                "노출 중 재발견·피격이 카운터를 되돌리면 「남은 턴」 배지가 거짓말이 된다.");
        }

        /// <summary>
        /// 맞으면 곧바로 드러난다(2026-09-05 사용자 확정). 부적·장판·함정·반사 어느 경로든
        /// <c>DamageMonster</c> 관문 하나를 지나므로, 관문 자체를 재면 경로 전체가 잠긴다.
        /// </summary>
        [Test]
        public void BeingHitRevealsTheHiddenMonsterImmediately()
        {
            var state = CreateState();
            var monster = FirstMonster(state);
            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.True, "처음엔 숨어 있다.");

            state.DamageMonster(monster, 1);

            Assert.That(
                state.IsMonsterHiddenByStealth(MonsterId), Is.False,
                "맞았는데 계속 숨어 있으면 「때려도 안 보이는」 몬스터가 된다.");
        }

        /// <summary>방어막에 전부 막혀도 드러난다 — 판정은 「HP가 줄었나」가 아니라 「맞았나」다.</summary>
        [Test]
        public void ADamageFullyAbsorbedByBlockStillReveals()
        {
            var state = CreateState();
            var monster = FirstMonster(state);
            monster.Combatant.AddBlock(10);

            var applied = state.DamageMonster(monster, 3);

            Assert.That(applied, Is.Zero, "방어막이 전부 흡수했다.");
            Assert.That(
                state.IsMonsterHiddenByStealth(MonsterId), Is.False,
                "막혔다고 안 드러나면 방어막 있는 은신 몬스터를 영영 못 본다.");
        }

        // ------------------------------------------------------------------ 특성 알림(2026-09-01)

        [Test]
        public void HidingAgainAnnouncesItselfOnTheTurnItHappens()
        {
            var state = CreateState();
            var monster = FirstMonster(state);
            Reveal(state, monster);
            var announced = CaptureTraitAnnouncements(state);

            Invoke(state, "TickMonsterStealthPerTurn");
            Assert.That(announced, Is.Empty, "1턴 남았다 — 아직 숨지 않았다.");

            Invoke(state, "TickMonsterStealthPerTurn");
            Assert.That(announced.Count(a => a.SourceRef == MonsterTraitAnnouncement.StealthHiddenRef), Is.EqualTo(1),
                "다시 숨는 순간을 알린다 — 그 글자가 없으면 몬스터가 그냥 사라진 것으로 보인다.");

            Invoke(state, "TickMonsterStealthPerTurn");
            Invoke(state, "TickMonsterStealthPerTurn");
            Assert.That(announced.Count(a => a.SourceRef == MonsterTraitAnnouncement.StealthHiddenRef), Is.EqualTo(1),
                "숨어 있는 내내 반복되면 안 된다 — 알리는 것은 상태가 아니라 전이다.");
        }

        [Test]
        public void RevealAnnouncesItselfThroughTheExistingFogRevealEvent()
        {
            // 🔑 노출은 새 kind를 만들지 않았다 — 이미 FogReveal로 나가고 전용 VFX까지 붙어 있어서,
            //    문안만 「밝혀짐!」에서 「들킴!」으로 갈아 끼웠다(전염·뒤끝 접두와 같은 문법).
            var state = CreateState();
            var monster = FirstMonster(state);
            var seen = new List<EffectResultEvent>();
            state.EffectResolved += seen.Add;

            Reveal(state, monster);

            var reveal = seen.SingleOrDefault(e => e.Kind == EffectKind.FogReveal);
            Assert.That(reveal.SourceRef, Is.EqualTo(CombatState.StealthScoutRevealRef));
            Assert.That(MonsterTraitAnnouncement.TryGetText(reveal.SourceRef, reveal.AppliedAmount, out var text), Is.True,
                "표현층이 이 ref로 문안을 찾지 못하면 화면에는 범용 「밝혀짐!」이 남는다.");
            Assert.That(text, Is.EqualTo("들킴!"));
        }

        [Test]
        public void RevealInFogChangesTheRuleButAnnouncesNothing()
        {
            // 🔴🔴 노출 신호가 특성 알림과 <b>다른 문</b>으로 나가고 있었다(2026-09-01 #12). 특성 알림은
            //      안개를 보는데 FogReveal은 보지 않아서, 안개 속 어둑시니가 정찰에 걸리면 아무것도
            //      안 보이는 칸에 「들킴!」만 떴다 — 위치를 그대로 흘리는 누수다.
            //      규칙(노출 카운터)은 그대로 서야 한다. 조용해지는 것은 <b>신호</b>뿐이다.
            var state = CreateState(playerVisionRange: 2);
            var monster = FirstMonster(state);
            var foggy = state.Map.AllCells
                .Select(cell => cell.Coord)
                .FirstOrDefault(coord => state.GetVisibility(coord) != HexCellVisibility.Revealed);
            Assert.That(state.GetVisibility(foggy), Is.Not.EqualTo(HexCellVisibility.Revealed),
                "전제: 픽스처 맵에 안개 칸이 있어야 이 규칙을 물을 수 있다.");
            monster.Coord = foggy;

            var seen = new List<EffectResultEvent>();
            state.EffectResolved += seen.Add;

            Reveal(state, monster);

            Assert.That(seen.Any(e => e.Kind == EffectKind.FogReveal), Is.False,
                "안개 속 노출은 알리지 않는다 — 보이지 않는 칸의 「들킴!」은 위치 누수다.");
            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.False,
                "규칙은 그대로 선다 — 조용해지는 것은 신호뿐이고 노출 자체는 일어나야 한다.");
        }

        [Test]
        public void RevealInSightStillAnnounces()
        {
            // 위 게이트가 「안개」가 아니라 「전부 조용」으로 잘못 잠기면 이 테스트가 문다.
            var state = CreateState(playerVisionRange: 2);
            var monster = FirstMonster(state);
            Assert.That(state.GetVisibility(monster.Coord), Is.EqualTo(HexCellVisibility.Revealed),
                "전제: 몬스터가 시야 안에 서 있다.");

            var seen = new List<EffectResultEvent>();
            state.EffectResolved += seen.Add;

            Reveal(state, monster);

            Assert.That(seen.Count(e => e.Kind == EffectKind.FogReveal), Is.EqualTo(1),
                "보이는 곳에서 드러나는 순간은 여전히 알린다.");
        }

        [Test]
        public void ScoutLightsTheCellFirstSoTheRevealStillAnnounces()
        {
            // 🔴 순서가 계약이다. TryPlayerScout는 visibilityRuntime.ScoutReveal을 <b>먼저</b> 부르고
            //    그 다음에 RevealStealthMonstersInScoutArea를 부른다(CombatState.cs:2328 → 2339).
            //    뒤집히면 위의 가시성 게이트가 <b>정당한</b> 「들킴!」까지 삼킨다 — 정찰로 드러냈는데
            //    아무 말이 없는 화면. 이 테스트가 그 순서를 잡아 둔다.
            var state = CreateState(playerVisionRange: 2);
            var monster = FirstMonster(state);
            var foggy = state.Map.AllCells
                .Select(cell => cell.Coord)
                .FirstOrDefault(coord => state.GetVisibility(coord) != HexCellVisibility.Revealed);
            monster.Coord = foggy;

            var visibility = (HexVisibilityRuntime)typeof(CombatState)
                .GetField("visibilityRuntime", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            visibility.ScoutReveal(foggy, 0);
            Assert.That(state.GetVisibility(foggy), Is.EqualTo(HexCellVisibility.Revealed),
                "전제: 정찰은 칸을 Revealed로 켠다 — Hinted였다면 게이트가 노출 신호를 통째로 삼킨다.");

            var seen = new List<EffectResultEvent>();
            state.EffectResolved += seen.Add;

            Reveal(state, monster);

            Assert.That(seen.Count(e => e.Kind == EffectKind.FogReveal), Is.EqualTo(1),
                "정찰이 켠 칸에서는 「들킴!」이 그대로 떠야 한다.");
        }

        [Test]
        public void AHiddenMonsterNeverAnnouncesItsOtherTraits()
        {
            // 🔴 특성 알림은 좌표에 뜨는 글자다. 숨은 놈이 약오름을 쌓을 때마다 위치가 새면
            //    은신의 값이 통째로 사라진다.
            var state = CreateState();
            var monster = FirstMonster(state);
            Assert.That(state.IsMonsterHiddenByStealth(MonsterId), Is.True, "전제: 지금 숨어 있다.");

            var announced = CaptureTraitAnnouncements(state);
            InvokeAnnouncement(state, monster, MonsterTraitAnnouncement.AgitationGainedRef, 2, false);

            Assert.That(announced, Is.Empty, "숨은 몬스터의 특성은 조용해야 한다.");

            // 같은 호출이 노출 뒤에는 통과한다 — 게이트가 「좌표가 안 보인다」가 아니라 「은신」임을 못 박는다.
            Reveal(state, monster);
            InvokeAnnouncement(state, monster, MonsterTraitAnnouncement.AgitationGainedRef, 2, false);
            Assert.That(announced.Count, Is.EqualTo(1));
        }

        [Test]
        public void AMonsterInFogNeverAnnouncesAnything()
        {
            // 🔴 은신 예외(allowWhileHidden)가 안개까지 뚫으면 안 된다 — 은신 진입 알림이 시야 밖
            //    몬스터의 위치를 그리는 순간, 「숨었다」가 「저기 있다」가 된다.
            var state = CreateState(playerVisionRange: 2);
            var monster = FirstMonster(state);
            var foggy = state.Map.AllCells
                .Select(cell => cell.Coord)
                .FirstOrDefault(coord => state.GetVisibility(coord) != HexCellVisibility.Revealed);
            Assert.That(state.GetVisibility(foggy), Is.Not.EqualTo(HexCellVisibility.Revealed),
                "전제: 픽스처 맵에 안개 칸이 있어야 이 규칙을 물을 수 있다.");
            monster.Coord = foggy;

            var announced = CaptureTraitAnnouncements(state);
            InvokeAnnouncement(state, monster, MonsterTraitAnnouncement.StealthHiddenRef, 0, true);

            Assert.That(announced, Is.Empty, "안개 속에서는 은신 진입조차 알리지 않는다.");
        }

        private static List<EffectResultEvent> CaptureTraitAnnouncements(CombatState state)
        {
            var announced = new List<EffectResultEvent>();
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.MonsterTraitTriggered)
                {
                    announced.Add(resultEvent);
                }
            };

            return announced;
        }

        private static void InvokeAnnouncement(
            CombatState state, MonsterRuntime monster, string traitRef, int amount, bool allowWhileHidden)
        {
            Invoke(state, "RaiseMonsterTraitAnnouncement", monster, traitRef, amount, allowWhileHidden);
        }

        // ------------------------------------------------------------------ 예고 은폐(2026-09-01)

        [Test]
        public void HiddenMonsterEmitsNoMoveOrAttackPreview()
        {
            // 2026-09-01 사용자 확정으로 종전 규칙(「예고는 감추지 않는다」)이 뒤집혔다. 마커 없는
            // 위험 칸은 누가 겨누는지 읽을 수 없어 정보가 아니었다 — 그 자리는 "기습!"이 메운다.
            var state = CreateState();
            var monster = FirstMonster(state);

            var hidden = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(preview => preview.MonsterId == MonsterId);

            Assert.That(hidden.IsIntentHidden, Is.True, "은신은 미지와 같은 은폐 축에 합류했다.");
            Assert.That(hidden.AttackRangeCoords, Is.Empty, "위험 칸이 새면 숨은 것이 어디 있는지 읽힌다.");
            Assert.That(hidden.PredictedMoveCoord, Is.EqualTo(monster.Coord), "이동 예고도 지운다.");
            Assert.That(hidden.AttackPatternId, Is.Empty, "패턴 id가 새면 툴팁·디버그 표면이 숨긴 것을 말한다.");
        }

        [Test]
        public void RevealedMonsterGetsItsPreviewBack()
        {
            // 은폐가 「한 번 숨으면 영영」이 되면 노출이 아무 값도 못 한다 — 드러난 4턴이 곧
            // 「지금은 읽을 수 있다」여야 노출에 의미가 생긴다.
            var state = CreateState();
            var monster = FirstMonster(state);
            Reveal(state, monster);

            var shown = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .Single(preview => preview.MonsterId == MonsterId);

            Assert.That(shown.IsIntentHidden, Is.False);
        }

        // ------------------------------------------------------------------ 어둠 먹기

        [Test]
        public void DarkFeedGrowsWhileHiddenAndCapsAtTheAuthoredMax()
        {
            var state = CreateState();
            var monster = FirstMonster(state);

            for (var turn = 0; turn < 10; turn++)
            {
                Invoke(state, "TickMonsterStealthPerTurn");
            }

            Assert.That(monster.AgitationStacks, Is.EqualTo(6),
                "드러나지 않은 턴마다 +2, 상한 +6 — 저장·보너스·세이브는 약오름 배관을 빌려 쓴다.");
        }

        [Test]
        public void DarkFeedResetsWhenTheMonsterIsRevealed()
        {
            var state = CreateState();
            var monster = FirstMonster(state);
            Invoke(state, "TickMonsterStealthPerTurn");
            Invoke(state, "TickMonsterStealthPerTurn");
            Assert.That(monster.AgitationStacks, Is.EqualTo(4));

            Reveal(state, monster);

            Assert.That(monster.AgitationStacks, Is.Zero, "드러난 순간이 곧 리셋이다 — 조건이 「드러나지 않은 턴」이므로.");
        }

        [Test]
        public void DarkFeedStacksSurviveASuspendRoundTripWithTheRevealCounter()
        {
            var state = CreateState();
            var monster = FirstMonster(state);
            Invoke(state, "TickMonsterStealthPerTurn");
            Reveal(state, monster);

            var restored = CreateState();
            restored.RestoreFromSuspend(state.CreateSuspendSnapshot());

            var restoredMonster = FirstMonster(restored);
            Assert.That(restoredMonster.StealthRevealTurnsRemaining, Is.EqualTo(2),
                "노출 잔여 턴을 잃으면 재개 순간 몬스터가 도로 사라진다.");
            Assert.That(restored.IsMonsterHiddenByStealth(MonsterId), Is.False);
        }

        // ------------------------------------------------------------------ 저작 검증

        [Test]
        public void HiddenTraitParserGuardsTheAuthoring()
        {
            Assert.That(MonsterHiddenTrait.TryParse(string.Empty, string.Empty, out var none, out _), Is.True);
            Assert.That(none.RevealTurns, Is.Zero, "저작 없음.");

            Assert.That(MonsterHiddenTrait.TryParse(string.Empty, "revealTurns=2", out _, out var orphan), Is.False);
            Assert.That(orphan, Does.Contain("without a hiddenTraitRef"));

            Assert.That(MonsterHiddenTrait.TryParse("hidden.unknown", string.Empty, out _, out var unknown), Is.False);
            Assert.That(unknown, Does.Contain("not registered"));

            Assert.That(MonsterHiddenTrait.TryParse(MonsterHiddenTrait.StealthRef, "revealTurns=0", out _, out _),
                Is.False, "0턴 노출은 잡을 수 없는 몬스터다.");
            Assert.That(MonsterHiddenTrait.TryParse(MonsterHiddenTrait.StealthRef, "growthPerTurn=2", out _, out var half),
                Is.False, "상한 없는 성장은 금지다.");
            Assert.That(half, Does.Contain("함께 저작"));

            Assert.That(MonsterHiddenTrait.TryParse(
                MonsterHiddenTrait.StealthRef, "revealTurns=2;growthPerTurn=2;growthMax=6",
                out var spec, out _), Is.True);
            Assert.That(spec.RevealTurns, Is.EqualTo(2));
            Assert.That(spec.HasGrowth, Is.True);

            // 구 A039 흩어지기(2026-09-04 완전 삭제)의 scatterPattern= 잔존 저작은 미지의 key=value로
            // 조용히 무시된다 — 파서가 죽으면 세이브·구버전 CSV가 함께 죽는다.
            Assert.That(MonsterHiddenTrait.TryParse(
                MonsterHiddenTrait.StealthRef, "revealTurns=2;scatterPattern=A039", out _, out _), Is.True);
        }

        [Test]
        public void AgitationAndDarkFeedCannotBeAuthoredTogether()
        {
            // 🔴 둘은 같은 스택 카운터를 쓴다(요괴 §4-2) — 함께 저작하면 기록자가 둘이 되어 수치가 엉킨다.
            var ex = Assert.Throws<System.ArgumentException>(() => ConvertCatalog(
                "M001,Tester,test-melee,B001,30,6,2,1,prototype,,3,0,,,FALSE,0,FALSE,"
                + "hidden.stealth,revealTurns=2;growthPerTurn=2;growthMax=6\n"));
            Assert.That(ex.Message, Does.Contain("같은 스택 카운터"));
        }

        // ------------------------------------------------------------------ AI 프로파일 · 매복

        [Test]
        public void BehaviorProfileRegistryResolvesTheAuthoredProfiles()
        {
            // 🔴 behaviorProfileRef는 오랫동안 죽은 컬럼이었다(§4-6 ①). 이제 값이 의도 선택의 매복 분기를 고른다
            // (DEC-2026-09-05-03: 트리 팩토리 → 계획기 if/else 분기). 미등록 저작은 기본으로 내려앉는다.
            Assert.That(MonsterBehaviorProfileRegistry.IsRegistered(MonsterBehaviorProfileRegistry.DefaultProfileRef), Is.True);
            Assert.That(MonsterBehaviorProfileRegistry.IsRegistered(MonsterBehaviorProfileRegistry.AmbushProfileRef), Is.True);
            Assert.That(MonsterBehaviorProfileRegistry.IsRegistered("B999"), Is.False);

            Assert.That(MonsterBehaviorProfileRegistry.IsAmbush(MonsterBehaviorProfileRegistry.AmbushProfileRef), Is.True);
            Assert.That(MonsterBehaviorProfileRegistry.IsAmbush("B999"), Is.False, "미등록 저작은 기본 분기다.");
            Assert.That(MonsterBehaviorProfileRegistry.IsAmbush(null), Is.False);
        }

        [Test]
        public void AmbushHoldsItsGroundUntilThePlayerComesClose()
        {
            var memory = new MonsterFsmMemory();

            var far = MonsterAiPlanner.SelectMovementIntent(
                Context(new HexCoord(0, 0), new HexCoord(6, 0)), memory, MonsterBehaviorProfileRegistry.AmbushProfileRef);
            Assert.That(far.Type, Is.EqualTo(EnemyIntentType.Return), "거리 6 > 4 — 자리를 지킨다.");
            Assert.That(memory.State, Is.EqualTo(MonsterFsmState.Patrol), "사거리 밖에서는 아래 규칙을 아예 돌리지 않는다(기억 무변경).");

            var near = MonsterAiPlanner.SelectMovementIntent(
                Context(new HexCoord(0, 0), new HexCoord(3, 0)), memory, MonsterBehaviorProfileRegistry.AmbushProfileRef);
            Assert.That(near.Type, Is.EqualTo(EnemyIntentType.Chase), "사거리 안이면 평소 규칙이 그대로 돈다.");
            Assert.That(memory.State, Is.EqualTo(MonsterFsmState.Chase));
        }

        // ------------------------------------------------------------------ 출하 데이터

        [Test]
        [Category("ShippingData")]
        public void EodukshiniIsAuthoredAsTheHiddenAmbusher()
        {
            var catalog = ShippingMonsterCatalog();
            var entry = catalog.Entries.Single(candidate => candidate.Id == "M009");

            Assert.That(entry.HasHiddenTrait, Is.True);
            Assert.That(MonsterHiddenTrait.TryParse(entry.HiddenTraitRef, entry.HiddenTraitParam, out var spec, out var error),
                Is.True, error);
            // 2026-09-01 실플레이 판정: 노출 2턴 → 4턴, 「어둠 먹기」 폐기.
            // 2026-09-05 사용자 확정: 다시 4턴 → 2턴으로 되돌린다. 노출 카운터가 더 이상 갱신되지
            // 않게 되면서(정찰·피격으로 리셋 안 됨) 4턴은 「한 번 들키면 반영구」에 가까워졌다.
            Assert.That(spec.RevealTurns, Is.EqualTo(2));
            Assert.That(spec.HasGrowth, Is.False,
                "어둠 먹기는 폐기됐다 — 숨은 채 자라는 피해가 「어디서 맞았는지 모르는」 체감의 절반이었고, "
                + "약오름 배관을 빌려 쓰던 탓에 특성 표시까지 약오름으로 샜다.");
            Assert.That(entry.AttackPatterns.Any(pattern => pattern.Id == "A039"), Is.False,
                "그림자로 흩어지기(A039)는 2026-09-04 리워크로 완전 삭제됐다 — 노출 4턴 약속을 "
                + "1턴으로 만들어 노출 약속을 0으로 되돌리던 결함. 재은신은 자동 만료(2턴)뿐이다.");

            Assert.That(entry.BehaviorProfileRef, Is.EqualTo(MonsterBehaviorProfileRegistry.AmbushProfileRef));
            Assert.That(entry.AgitationMaxStacks, Is.Zero, "어둠 먹기와 약오름은 배타다.");
        }

        // ------------------------------------------------------------------ 픽스처

        // ------------------------------------------------------------------ 「들킴!」이 뜨는 시점(2026-09-05)

        [Test]
        public void RevealAfterAnAttackCarriesThatAttacksPresentationGroup()
        {
            // 🔴 그룹이 비면 어셈블러가 이 이벤트를 턴 경계 뭉치로 흘려보내, 「들킴!」이 몬스터 페이즈가
            //    <b>전부 끝난 뒤</b> 뜬다(사용자: "공격한 직후에 이루어져야함"). 규칙은 이미 맞는 순간에
            //    서 있었고 늦은 것은 화면뿐이었다 — 그래서 재는 것은 노출 여부가 아니라 <b>그룹</b>이다.
            var state = CreateState();
            var monster = FirstMonster(state);
            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Invoke(state, "RevealStealthMonsterAfterAttack", monster, "attack-group-1");

            var reveal = events.Single(candidate => candidate.Kind == EffectKind.FogReveal);
            Assert.That(reveal.SourceRef, Is.EqualTo(CombatState.StealthAttackRevealRef));
            Assert.That(reveal.PresentationGroupId, Is.EqualTo("attack-group-1"),
                "노출 신호는 그 공격의 연출 그룹에 실려야 공격 비트에 붙는다.");
        }

        [Test]
        public void ScoutRevealStaysOutsideAnyAttackGroup()
        {
            // 정찰 노출은 어떤 공격에도 딸려 있지 않다 — 남의 그룹에 실리면 그 공격의 비트로 끌려간다.
            var state = CreateState();
            var monster = FirstMonster(state);
            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            Invoke(state, "RevealStealthMonster", monster, CombatState.StealthScoutRevealRef);

            var reveal = events.Single(candidate => candidate.Kind == EffectKind.FogReveal);
            Assert.That(reveal.PresentationGroupId, Is.Empty);
        }

        [Test]
        public void AStealthAttackAnnouncesTheRevealInsideTheAttacksOwnGroup()
        {
            // 끝에서 끝까지: 숨은 채 때린 몬스터의 「들킴!」이 <b>그 공격의 그룹</b>에 실려 나오는가.
            // 위 두 테스트는 배관을 재고, 이 테스트는 <b>집행부가 그 배관을 쓰는지</b>를 잰다 —
            // 인자를 넘기는 줄이 빠지면 여기만 빨개진다.
            var state = CreateState();
            Assume.That(state.TryDebugMovePlayer(new HexCoord(1, 0)), Is.True, "전제: 몬스터(2,0) 옆에 선다.");
            var events = new List<EffectResultEvent>();
            state.EffectResolved += events.Add;

            AdvanceOneOverallTurn(state);

            var damage = events.FirstOrDefault(e => e.Kind == EffectKind.Damage && e.TargetUnitId == "player");
            Assume.That(damage.Kind, Is.EqualTo(EffectKind.Damage), "전제: 이 턴에 실제로 맞았다.");
            var reveals = events
                .Where(e => e.Kind == EffectKind.FogReveal && e.SourceRef == CombatState.StealthAttackRevealRef)
                .ToList();
            Assert.That(reveals, Is.Not.Empty, "때렸으면 드러난다.");
            Assert.That(reveals[0].PresentationGroupId, Is.EqualTo(damage.PresentationGroupId),
                "노출은 그 공격과 같은 연출 그룹이어야 공격 비트에 붙는다 — 그룹이 비면 턴 경계로 밀린다.");
            Assert.That(reveals[0].PresentationGroupId, Is.Not.Empty);
        }

        private static void AdvanceOneOverallTurn(CombatState state)
        {
            Assert.That(state.TryPlayerMove(state.PlayerCoord), Is.True);
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterMovement();
            Assert.That(state.EndAction(), Is.True);
            state.ResolveMonsterAction();
        }

        private static MonsterFsmContext Context(HexCoord monsterCoord, HexCoord playerCoord)
        {
            return new MonsterFsmContext(
                monsterCoord,
                playerCoord,
                monsterCoord,
                attackRange: 1,
                chaseRange: 8,
                disengageRange: 12,
                map: CombatState.CreateDemoMap(8),
                runtimeStates: new Dictionary<HexCoord, HexCellRuntimeState>(),
                playerIsDead: false,
                patrolArea: System.Array.Empty<HexCoord>());
        }

        private static void Reveal(CombatState state, MonsterRuntime monster)
        {
            Invoke(state, "RevealStealthMonster", monster, CombatState.StealthScoutRevealRef);
        }

        private static CombatState CreateState(
            string hiddenTraitRef = MonsterHiddenTrait.StealthRef,
            string hiddenTraitParam = "revealTurns=2;growthPerTurn=2;growthMax=6",
            // 기본 7 = 반경 6짜리 픽스처 맵 전체가 밝다(안개 변수를 빼기 위한 기존 설정).
            // 안개 규칙을 물어야 하는 테스트만 이 값을 줄인다.
            int playerVisionRange = 7)
        {
            return new CombatState(
                CombatState.CreateDemoMap(6),
                new HexCoord(0, 0),
                new[] { new MonsterConfig(MonsterId, new HexCoord(2, 0), 20, definitionId: DefinitionId) },
                TestCombatConfigs.Standard(playerMaxHp: 200, playerVisionRange: playerVisionRange),
                monsterCatalog: new MonsterCatalogDefinition(
                    "stealth-test-catalog",
                    "Stealth Test Catalog",
                    new List<MonsterCatalogEntry>
                    {
                        new MonsterCatalogEntry(
                            DefinitionId,
                            "그림자",
                            "test-melee",
                            MonsterBehaviorProfileRegistry.AmbushProfileRef,
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: 20,
                            attackSpeed: 1,
                            attackPatterns: new[]
                            {
                                new MonsterAttackPattern("AT89", "삼키기", range: 1, areaRadius: 0, damage: 3)
                            },
                            hiddenTraitRef: hiddenTraitRef,
                            hiddenTraitParam: hiddenTraitParam)
                    }));
        }

        private static MonsterCatalogDefinition ShippingMonsterCatalog()
        {
            AttackShapeLibrary.Initialize(AttackShapeCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.AttackShapesCsv, Encoding.UTF8)));
            return MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory).MonsterCatalog;
        }

        private static MonsterCatalogCsvBundle ConvertCatalog(string monsterRow)
        {
            const string header =
                "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,"
                + "visualPrefabPath,agitationMaxStacks,toughnessReloadTurns,onDeathEffectRef,onDeathEffectParam,"
                + "sturdyBlock,hpVariancePct,advanceAfterAttack,hiddenTraitRef,hiddenTraitParam\n";
            return MonsterCatalogCsvConverter.Convert(new MonsterCatalogCsvSource(
                header + monsterRow,
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,"
                + "statusEffectDurationTurns,cooldownTurns\n"
                + "A001,Basic,1,3,attack.damage,player_in_range,V001,single,,2,0\n",
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\nM001,A001,1,true,,\n",
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_vfx_cues.csv")),
                File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, "combat_sound_cues.csv"))));
        }

        private static MonsterRuntime FirstMonster(CombatState state)
        {
            var monsters = (List<MonsterRuntime>)typeof(CombatState)
                .GetField("monsters", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(state);
            return monsters[0];
        }

        /// <summary>
        /// 사설 메서드 호출. 🔴 <b>파라미터 개수로 정확히 맞추지 않는다</b> — 선택 인자가 하나 늘 때마다
        /// (2026-09-05 연출 그룹 id) 이 스위트 전체가 "Sequence contains no matching element"로 무너졌다.
        /// 넘긴 인자는 앞에서부터 채우고 나머지 선택 인자는 <see cref="Type.Missing"/>로 기본값을 쓴다.
        /// </summary>
        private static object Invoke(CombatState state, string methodName, params object[] args)
        {
            var method = typeof(CombatState)
                .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(candidate =>
                {
                    if (candidate.Name != methodName)
                    {
                        return false;
                    }

                    var parameters = candidate.GetParameters();
                    if (parameters.Length < args.Length)
                    {
                        return false;
                    }

                    // 넘기지 않은 나머지는 전부 선택 인자여야 한다 — 그래야 기본값으로 채울 수 있다.
                    for (var i = args.Length; i < parameters.Length; i++)
                    {
                        if (!parameters[i].IsOptional)
                        {
                            return false;
                        }
                    }

                    return true;
                });

            var target = method.GetParameters();
            var call = new object[target.Length];
            for (var i = 0; i < target.Length; i++)
            {
                call[i] = i < args.Length ? args[i] : Type.Missing;
            }

            return method.Invoke(state, BindingFlags.OptionalParamBinding, null, call, null);
        }
    }
}
