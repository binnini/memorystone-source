using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// 도감 해금 신호(계획 §8-3, P3). 전투가 "무엇을 만났는가"를 <see cref="ICodexSightingSink"/>에
    /// 흘려 보내는 유일한 자리다.
    /// <para>
    /// 🔑<b>왜 훑기(sweep)인가.</b> 인계문은 도메인마다 유입 지점 하나씩을 후보로 뒀지만, 실제로는
    /// 유입 경로가 도메인마다 여럿이다 — 카드는 개시 드로우·턴 드로우·보상 지급 셋이고, 함정 발견은
    /// 정찰·밟기·해제·서스펜드 복원 넷이다. 지점마다 훅을 박으면 <b>새 경로가 생길 때마다 조용히
    /// 구멍이 난다</b>. 대신 상태 전이의 단일 관문(<c>SetPhase</c>)에서 <b>지금 보이는 것 전부</b>를
    /// 훑는다. 집합이라 중복 표시는 무해하므로(계획 §8-3) 이쪽이 옳은 비대칭이다.
    /// </para>
    /// <para>
    /// 🔴<b>몬스터 가시 판정에 부작용을 넣지 않는다.</b> <c>IsVisibleLivingMonster</c>는 스물 몇 곳에서
    /// 불리는 읽기 전용 술어라 거기서 표시하면 미리보기·집행 어디서든 터진다. 훑기가 같은 술어를
    /// <b>읽기만</b> 하고 지나간다.
    /// </para>
    /// </summary>
    public sealed partial class CombatState
    {
        private ICodexSightingSink codexSightings;

        /// <summary>
        /// 해금 신호를 받아 적을 곳. <c>null</c>이면 신호는 조용히 버려진다 — 테스트·랩·디버그 씬이
        /// 도감을 몰라도 돌아야 한다.
        /// <para>
        /// 붙이는 순간 <b>한 번 훑는다</b>. 전투가 이미 시작된 뒤에 붙어도(개시 손패는 생성자에서
        /// 이미 뽑혔다) 그 시점의 손패·시야·인벤토리가 그대로 잡힌다.
        /// </para>
        /// </summary>
        public ICodexSightingSink CodexSightings
        {
            get => codexSightings;
            set
            {
                codexSightings = value;
                SweepCodexSightings();
            }
        }

        /// <summary>
        /// 지금 플레이어가 만나고 있는 것 전부를 표시한다. 손패·시야 안 몬스터·발견된 함정·유물·가방.
        /// 비용은 (손패 + 몬스터 + 함정 + 인벤토리)의 선형 훑기라 페이즈 전이마다 돌려도 무해하다.
        /// </summary>
        internal void SweepCodexSightings()
        {
            var sink = codexSightings;
            if (sink == null)
            {
                return;
            }

            SweepCodexHand(sink, MovementDeck);
            SweepCodexHand(sink, ActionDeck);

            foreach (var monster in monsters)
            {
                // 죽은 몬스터는 세지 않는다 — 시체가 시야에 남아 있다고 "만났다"가 되지는 않는다.
                // (죽기 전에 반드시 보였으므로 이미 표시돼 있다.)
                if (IsVisibleLivingMonster(monster))
                {
                    sink.MarkSeen(CodexDomainIds.Monster, monster.DefinitionId);
                }
            }

            foreach (var trap in AllTrapRefs)
            {
                // 🔴발견한 함정만이다. 안개 속의 함정을 표시하면 도감이 정찰을 대신해 준다.
                // presetId가 빈 함정은 저작 프리셋이 아니라 런타임 배치(보스 §21.5)라 도감에 자리가 없다.
                if (!string.IsNullOrEmpty(trap.PresetId) && visibilityRuntime.IsTrapRevealed(trap.Coord))
                {
                    sink.MarkSeen(CodexDomainIds.Trap, trap.PresetId);
                }
            }

            SweepCodexMapObjects(sink);

            var inventory = PlayerInventory;
            if (inventory == null)
            {
                return;
            }

            if (inventory.RelicsAndCurses != null)
            {
                // 유물과 저주가 같은 목록에 산다 — 도감 유물 도메인도 둘을 같이 싣는다(P2).
                foreach (var item in inventory.RelicsAndCurses.Items)
                {
                    sink.MarkSeen(CodexDomainIds.Relic, item.Id);
                }
            }

            if (inventory.Bag != null)
            {
                foreach (var stack in inventory.Bag.Stacks)
                {
                    sink.MarkSeen(CodexDomainIds.Consumable, stack.ItemId);
                }
            }
        }

        /// <summary>
        /// 맵에 배치된 오브젝트 가운데 <b>지금 보이는 것</b>(P6). 함정 가지와 같은 모양이다.
        /// <para>
        /// 🔴 <b>안개 속 오브젝트를 표시하지 않는다.</b> 표시해 버리면 도감이 정찰을 대신해 준다 —
        /// 아직 가 보지 않은 구역에 상점이 있다는 것을 도감으로 먼저 알게 된다. 함정이
        /// <c>IsTrapRevealed</c>로 막은 것과 같은 문제다.
        /// </para>
        /// <para>
        /// 🔑 <b>anchor 칸이 아니라 점유 칸 전부를 본다.</b> 롯데타워처럼 여러 칸을 밟는 건물은
        /// anchor가 아직 안개인 채로 몸통만 시야에 드는 일이 흔하다 — anchor만 보면 눈앞에 서 있는
        /// 탑이 도감에 안 열린다.
        /// </para>
        /// <para>
        /// 🔴 <b>키를 <c>objectRef</c> 그대로 쓰지 않는다.</b> 진행도에 적는 것은 CSV의 안정
        /// <c>id</c>다(<c>treasureChest_tmp</c> 같은 임시 모델 이름이 저장 키로 굳으면 정식 모델이
        /// 왔을 때 진행도가 통째로 초기화된다). 그리고 <b>같은 <c>objectRef</c> 필드가 몬스터
        /// 스폰에는 <c>M001</c>을 담는데</b>, 그 값은 이 표에 없으므로 조회가 저절로 걸러 낸다 —
        /// 타입 문자열로 한 번 더 거르면 그 목록이 언젠가 낡는다.
        /// </para>
        /// </summary>
        private void SweepCodexMapObjects(ICodexSightingSink sink)
        {
            var objectCatalog = CodexObjectCatalogSource.Current;
            if (objectCatalog == null || objectCatalog.Entries.Count == 0)
            {
                return;
            }

            foreach (var mapObject in Map.ObjectRefs)
            {
                if (!mapObject.IsConfigured
                    || !objectCatalog.TryGetByCatalogRef(mapObject.ObjectRef, out var entry)
                    || !entry.VisibleInCatalog)
                {
                    continue;
                }

                foreach (var coord in mapObject.OccupiedCoords)
                {
                    if (visibilityRuntime.GetVisibility(coord) != HexCellVisibility.Revealed)
                    {
                        continue;
                    }

                    sink.MarkSeen(CodexDomainIds.Object, entry.Id);
                    break;
                }
            }
        }

        /// <summary>
        /// 손패 한 벌. 🔴<b><see cref="CardDefinition.Id"/>이지 <c>InstanceId</c>가 아니다</b> —
        /// 인스턴스 id는 런마다 달라서 도감 항목과 영영 안 맞는다(<c>CodexCardDomain</c>은 카탈로그
        /// 엔트리 id로 항목을 만든다).
        /// </summary>
        private static void SweepCodexHand(ICodexSightingSink sink, CardDeckState deck)
        {
            if (deck == null)
            {
                return;
            }

            foreach (var card in deck.Hand)
            {
                if (card != null)
                {
                    sink.MarkSeen(CodexDomainIds.Card, card.Id);
                }
            }
        }

        /// <summary>
        /// 훑기를 기다리지 않고 즉시 표시한다. 획득 관문처럼 "이 한 줄을 지났으면 무조건 만난 것"이
        /// 확실한 자리에서만 쓴다.
        /// </summary>
        private void MarkCodexSighting(string domainId, string entryId)
        {
            codexSightings?.MarkSeen(domainId, entryId);
        }
    }
}
