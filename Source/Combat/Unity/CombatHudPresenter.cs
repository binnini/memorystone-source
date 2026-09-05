using System.Linq;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    internal sealed class CombatHudPresenter
    {
        public string FormatStatusText(
            CombatState state,
            bool isSequencePlaying,
            string resolvingStatusText,
            string selectedBoardEvidenceText)
        {
            return state == null
                ? "Uninitialized"
                : CombatStatusTextFormatter.FormatModelStatus(
                    state.Phase,
                    isSequencePlaying,
                    resolvingStatusText,
                    selectedBoardEvidenceText);
        }

        public string FormatEnemyIntentDisplayText(CombatState state)
        {
            return state?.RepresentativeLivingMonster is { } monster
                ? monster.Intent.ToDisplayText(state.Config)
                : string.Empty;
        }

        public string FormatMonsterStatusText(CombatState state)
        {
            return state == null
                ? string.Empty
                : string.Join("  |  ", state.Monsters.Select(monster => $"{monster.Id} {monster.Hp}/{monster.MaxHp} @ {monster.Coord} {(monster.HasAttackIntent ? "Intent:Attack" : monster.Intent.Type.ToString())}"));
        }

        public string FormatDeckStatusText(CombatState state)
        {
            return state == null
                ? string.Empty
                // 재화를 여기 얹은 것은 임시다. 사이드바에 'currency' 패널 자리가 예약돼 있지만 전용 뷰가
                // 없어(크기 0,0) 잔액을 보여줄 표면이 이 줄뿐이다 — 뽑기에서 돈이 나오는데 어디에도
                // 안 보이면 획득 자체가 없는 것과 같다. 패널을 저작하면 여기서 빼도 된다.
                : $"Move D/H/X {state.MovementDeck.DrawCount}/{state.MovementDeck.HandCount}/{state.MovementDeck.DiscardCount}  Action D/H/X {state.ActionDeck.DrawCount}/{state.ActionDeck.HandCount}/{state.ActionDeck.DiscardCount}  Ki {state.CurrentKi}/{state.MaxKi}  재화 {state.PlayerInventory?.Wallet?.Balance ?? 0}";
        }
    }
}
