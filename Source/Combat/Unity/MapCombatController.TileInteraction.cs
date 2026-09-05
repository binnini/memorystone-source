using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Unity
{
    /// <summary>
    /// 플레이어가 선 칸의 상호작용 오브젝트 해소 체인(4B-A · 2026-09-04). 옛 <c>MapCombatController.Cinematics.cs</c>에
    /// 있었으나 호출 그래프상 시네마틱이 아니라 게임플레이 획득 규칙이다(카드 선택 3·이동 타임라인·디버그 이동이 부른다).
    /// 기억석 트리거는 규칙 해소(<c>State.TryInteractMemoryStoneAtPlayer</c>) 뒤에야 승리 연출을 시작하므로 경계는
    /// <c>BeginMemoryStoneVictoryPresentation</c>이다. 본문 무변경. 리플렉션 문자열
    /// <c>"ResolveTileInteractionAtPlayerCoord"</c>(CamperWorkshopObjectTests)가 이 인스턴스 멤버를 호스트에서 잡는다.
    /// </summary>
    public sealed partial class MapCombatController
    {
        /// <summary>
        /// 플레이어가 지금 서 있는 칸의 <b>획득·상호작용 오브젝트를 해소하는 단일 지점</b>(R-5).
        ///
        /// 이 체인은 원래 다섯 군데에 그대로 복사돼 있었다(카드 선택 3 + 이동 타임라인 + 디버그 이동).
        /// 계획은 "3곳"으로 적고 있었는데 실측은 5곳이었고, 그 자체가 왜 한 곳으로 모아야 하는지의
        /// 근거다 — 획득 종류를 하나 추가할 때마다 다섯 번 손대야 하고, 한 번만 빠뜨리면
        /// <b>특정 이동 경로에서만 안 줍히는</b> 버그가 된다(재현 조건이 이동 방식에 걸려 있어 찾기 어렵다).
        ///
        /// 우선순위는 기존 순서를 그대로 보존한다: 기억석 → 보물 상자 → 저주받은 인형뽑기 → 상점.
        /// 반환값은 "무언가 발동했는가"이며, 디버그 이동 경로만 이 값을 쓴다.
        /// </summary>
        private bool ResolveTileInteractionAtPlayerCoord()
        {
            if (TryTriggerMemoryStoneAtPlayerCoord() || TryTriggerTreasureChestAtPlayerCoord() || TryTriggerCursedGachaAtPlayerCoord())
            {
                return true;
            }

            // 상점 뒤에 캠핑카·공작소(camper-workshop P2·P3). 같은 칸에 둘이 겹칠 일은 저작상 없지만
            // 우선순위는 체인 순서가 정의한다.
            return TryTriggerShopAtPlayerCoord()
                || TryTriggerCamperVanAtPlayerCoord()
                || TryTriggerWorkshopAtPlayerCoord();
        }

        private bool TryTriggerMemoryStoneAtPlayerCoord()
        {
            if (State == null || LoadedMap == null || State.IsTerminal)
            {
                return false;
            }

            var memoryStone = LoadedMap.GetObjectsAt(State.PlayerCoord)
                .FirstOrDefault(candidate =>
                    candidate.Interactable &&
                    string.Equals(candidate.ObjectType, "MemoryStone", System.StringComparison.Ordinal));
            if (!memoryStone.IsConfigured)
            {
                return false;
            }

            Cinematics.DeferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes =
                playMemoryStoneVictorySequence && State.ObjectiveTargetCoord.HasValue;
            if (!State.TryInteractMemoryStoneAtPlayer(out var reason))
            {
                Cinematics.DeferVictoryOverlayAndAudioUntilMemoryStonePresentationCompletes = false;
                LastInputMessage = reason;
                RefreshHudOnly();
                return false;
            }

            LastInputMessage = reason;
            Cinematics.BeginMemoryStoneVictoryPresentation();
            RefreshView();
            return true;
        }
    }
}
