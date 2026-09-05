using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatStatusTextFormatterTests
    {
        [Test]
        public void KnockbackTextReadsThePullSign()
        {
            // 🔴 2026-09-01 #5: 끌어당김과 밀치기가 화면에서 <b>같은 말</b>이었다. 부호가 방향이라는
            //    §16.1 규약을 문안까지 끌고 오는 것이 수정의 전부다 — 아트가 아니라 부호가 범인이었다
            //    (2026-08-11 배지 트랙과 같은 사고).
            Assert.That(FormatEffectText(EffectKind.Knockback, -2), Is.EqualTo("끌려감"));
            Assert.That(FormatEffectText(EffectKind.Knockback, 2), Is.EqualTo("밀려남"));
            Assert.That(FormatEffectText(EffectKind.Push, -1), Is.EqualTo("끌려감"));
            Assert.That(FormatEffectText(EffectKind.Push, 1), Is.EqualTo("밀려남"));
        }

        private static string FormatEffectText(EffectKind kind, int amount)
        {
            var resultEvent = new EffectResultEvent(
                kind, targetUnitId: "player", amount: amount, appliedAmount: amount, sourceRef: "knockback");
            var method = typeof(EffectPresentationController).GetMethod(
                "FormatText",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.That(method, Is.Not.Null, "FormatText가 사라졌거나 이름이 바뀌었다 — 문안 정본을 다시 찾을 것.");
            return (string)method.Invoke(null, new object[] { null, resultEvent });
        }

        [Test]
        public void FormatModelStatusUsesPhaseWhenNotResolving()
        {
            var text = CombatStatusTextFormatter.FormatModelStatus(
                CombatPhase.PlayerAction,
                isSequencePlaying: false,
                resolvingStatusText: "Moving",
                selectedBoardEvidenceText: string.Empty);

            Assert.That(text, Is.EqualTo("Phase: PlayerAction"));
        }

        [Test]
        public void FormatModelStatusShowsResolvingOnlyWhenSequenceHasText()
        {
            var text = CombatStatusTextFormatter.FormatModelStatus(
                CombatPhase.MonsterAction,
                isSequencePlaying: true,
                resolvingStatusText: "Enemy turn",
                selectedBoardEvidenceText: string.Empty);

            Assert.That(text, Is.EqualTo("Phase: MonsterAction | Resolving..."));
        }

        [Test]
        public void FormatModelStatusAppendsBoardEvidenceForNormalAndResolvingStates()
        {
            Assert.That(
                CombatStatusTextFormatter.FormatModelStatus(CombatPhase.PlayerMovement, false, string.Empty, "Board: Smoke"),
                Is.EqualTo("Phase: PlayerMovement | Board: Smoke"));
            Assert.That(
                CombatStatusTextFormatter.FormatModelStatus(CombatPhase.PlayerMovement, true, "Moving", "Board: Smoke"),
                Is.EqualTo("Phase: PlayerMovement | Resolving... | Board: Smoke"));
        }
    }
}

