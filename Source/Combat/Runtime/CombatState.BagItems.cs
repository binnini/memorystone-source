using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    // T4-1 소모품 가방(RC-8 실효화). 규칙: 슬롯 3(기본) · 사용 무비용(기 소모 없음) · 자기 턴
    // (이동/액션 페이즈) 중 언제든 · 사용 즉시 소비. 효과 수치는 전부 consumable_items.csv가 정본이고
    // 여기는 effectRef → 기존 이음매(회복·방어막·정화·수호·등불·기절 부여…) 디스패치만 한다.
    public sealed partial class CombatState
    {
        /// <summary>맹호 호리병(use.attack_bonus)의 이번 턴 한정 공격 보너스 — 무선 이어폰와 같은
        /// "kind 없는 스칼라" 문법으로 <see cref="GetFlatAttackDamageBonus"/>에 합산된다(미리보기=집행).</summary>
        private int bagAttackBonusThisTurn;

        public int BagAttackBonusThisTurn => bagAttackBonusThisTurn;

        /// <summary>가방 슬롯 상한 — 기본 3 + 배달 가방(T4-3 BagSlotBonus).</summary>
        public int GetEffectiveBagSlotLimit()
        {
            return Math.Max(1, PlayerBagState.BaseSlotCount
                + GetPermanentItemEffectTotal(PlayerPermanentItemEffectKind.BagSlotBonus));
        }

        /// <summary>
        /// 소모품 획득의 단일 관문(유물의 <c>TryGrantPermanentItem</c>과 같은 결). 미등록 id·슬롯 만원이면
        /// 거부한다 — 뽑기(T4-3)의 "만원 시 돈 폴백" 판정이 이 반환값을 쓴다.
        /// </summary>
        public bool TryAddBagItem(string itemId)
        {
            if (!ConsumableItemCatalog.TryGet(itemId, out _))
            {
                return false;
            }

            // 은퇴한 소모품은 어떤 경로로도 가방에 들어오지 않는다(2026-09-05) — 전리품·상점·뽑기·
            // 이벤트 오브젝트·디버그 지급이 전부 이 문을 지나므로 여기 한 곳이면 샐 곳이 없다.
            if (ConsumableItemAvailability.IsRetired(itemId))
            {
                return false;
            }

            if (PlayerInventory?.Bag == null || !PlayerInventory.Bag.TryAddItem(itemId, GetEffectiveBagSlotLimit()))
            {
                return false;
            }

            // 소모품은 같은 페이즈 안에서 받고 쓰는 것이 흔하다 — 훑기를 기다리면 가방을 이미 떠난 뒤다.
            MarkCodexSighting(CodexDomainIds.Consumable, itemId);
            return true;
        }

        /// <summary>
        /// 가방 아이템 사용. 자기 턴(이동/액션 페이즈)에만, 기 소모 없이 쓸 수 있다.
        /// 대상 지정형(Enemy/Tile)은 <paramref name="target"/>이 필요하다.
        /// 성공 시 아이템은 즉시 소비된다 — 효과량이 0으로 떨어져도(만피 회복 등) 소비는 성립한다.
        /// </summary>
        public bool TryUseBagItem(string itemId, HexCoord? target = null)
        {
            if (IsTerminal)
            {
                return Fail("Combat ended. Restart to play again.");
            }

            if (Phase != CombatPhase.PlayerMovement && Phase != CombatPhase.PlayerAction)
            {
                return Fail("아이템은 내 턴에만 쓸 수 있습니다.");
            }

            if (!ConsumableItemCatalog.TryGet(itemId, out var item))
            {
                return Fail("알 수 없는 아이템입니다.");
            }

            if (PlayerInventory?.Bag == null || PlayerInventory.Bag.Stacks.All(stack => stack.ItemId != itemId))
            {
                return Fail("가방에 없는 아이템입니다.");
            }

            if (!TryApplyBagItemEffect(item, target, out var reason))
            {
                return Fail(reason);
            }

            PlayerInventory.Bag.TryConsumeItem(itemId);
            LastFailureReason = string.Empty;
            BagItemUsed?.Invoke(itemId);
            return true;
        }

        /// <summary>
        /// 소모품이 실제로 소비됐을 때(아이템 id). 효과 하나하나는 EffectResolved로 따로 나가지만
        /// 「썼다」는 한 번뿐이라 — 호리병/구슬 사용음(item.flask.use / item.bead.use)은 여기에 물린다.
        /// </summary>
        public event Action<string> BagItemUsed;

        private bool TryApplyBagItemEffect(ConsumableItemDefinition item, HexCoord? target, out string reason)
        {
            reason = string.Empty;
            var sourceRef = $"item.{item.Id}";
            switch (item.EffectRef)
            {
                case ConsumableItemEffectRefs.Heal:
                {
                    var healed = Player.Heal(item.Amount);
                    if (healed > 0)
                    {
                        RaiseEffect(EffectKind.Heal, PlayerCoord, 0, healed, PlayerUnitId, sourceRef);
                    }

                    return true;
                }

                case ConsumableItemEffectRefs.Block:
                {
                    var block = AddBlockWithRupture(Player, PlayerUnitId, item.Amount);
                    if (block > 0)
                    {
                        RaiseEffect(EffectKind.Block, PlayerCoord, 0, block, PlayerUnitId, sourceRef);
                    }

                    return true;
                }

                case ConsumableItemEffectRefs.Cleanse:
                    CleanseStatusEffects(PlayerUnitId, sourceRef);
                    return true;

                case ConsumableItemEffectRefs.Guard:
                    // 벽사 호리병: 수호 부적과 같은 관문(GrantGuardCharge) — 보유 중이면 추가 부여가
                    // 조용히 무시되므로(최대 1) 낭비를 막기 위해 사전 거부한다.
                    if (HasActiveGuardCharge())
                    {
                        reason = "이미 수호가 걸려 있습니다.";
                        return false;
                    }

                    GrantGuardCharge(item.Amount, sourceRef);
                    return true;

                case ConsumableItemEffectRefs.Ki:
                    ActionCostRemaining += Math.Max(0, item.Amount);
                    return true;

                case ConsumableItemEffectRefs.AttackBonus:
                    bagAttackBonusThisTurn += Math.Max(0, item.Amount);
                    return true;

                case ConsumableItemEffectRefs.StunNearby:
                {
                    // 벽력 구슬: S03 기절초광과 같은 제어 상태 경로 — activeEffects를 직접 찌르면
                    // 커밋된 몬스터 계획이 실행 불가한 공격을 계속 예고한다.
                    var turns = Math.Max(1, item.DurationTurns);
                    var radius = Math.Max(1, item.Radius);
                    var targets = monsters
                        .Where(monster => !monster.Combatant.IsDead && PlayerCoord.DistanceTo(monster.Coord) <= radius)
                        .ToList();
                    var hitIndex = 0;
                    foreach (var monster in targets)
                    {
                        var intent = CaptureMonsterIntentForCancel(monster);
                        AddDurationStatusEffect(StatusEffectKind.Stun, monster.Id, turns, 0, sourceRef);
                        ApplyControlStatusConstraintToPlan(monster);
                        RaiseEffect(
                            EffectKind.StatusEffectApplied,
                            monster.Coord,
                            0,
                            turns,
                            monster.Id,
                            sourceRef,
                            sourceUnitId: PlayerUnitId,
                            sourceActorKind: "player",
                            targetActorKind: "monster",
                            hitIndex: hitIndex,
                            hitCount: targets.Count,
                            statusKind: StatusEffectKind.Stun,
                            delaySeconds: 0.12f + (0.06f * hitIndex));
                        EmitMonsterIntentCancelText(monster, intent.WasMoving, intent.WasAttacking, StatusEffectKind.Stun);
                        hitIndex++;
                    }

                    return true;
                }

                case ConsumableItemEffectRefs.Torch:
                    GrantTorchLight(item.Amount, sourceRef);
                    return true;

                case ConsumableItemEffectRefs.Stealth:
                    GrantStealth(Math.Max(1, item.DurationTurns), sourceRef);
                    return true;

                case ConsumableItemEffectRefs.BlastDamage:
                {
                    // 화염 구슬(T4-2): 지정 칸 반경 1 피해. 사거리 제한 없음(StS 포션 문법 — 잠정),
                    // 대신 안개 속 투척은 금지한다(시야 대원칙 — 보이지 않는 곳을 때릴 수 없다).
                    // 피해는 카드 보정(힘·강화·쇠약)을 받지 않는 고정치다 — 아이템 수치는 CSV가 전부.
                    if (!TryResolveVisibleCell(target, out var blastCenter, out reason))
                    {
                        return false;
                    }

                    var blastCells = HexArea.CellsWithin(blastCenter, Math.Max(0, item.Radius)).ToList();
                    var hitTargets = monsters
                        .Where(monster => !monster.Combatant.IsDead
                                          && blastCells.Any(cell => IsMonsterOccupying(monster, cell)))
                        .ToList();
                    var anyKilled = false;
                    var hitIndex = 0;
                    foreach (var monster in hitTargets)
                    {
                        var applied = DamageMonster(monster, item.Amount);
                        if (monster.Combatant.IsDead)
                        {
                            ResetDeadMonsterToPatrolIntent(monster);
                            anyKilled = true;
                        }

                        RaiseEffect(
                            EffectKind.Damage,
                            monster.Coord,
                            0,
                            applied,
                            monster.Id,
                            sourceRef,
                            sourceUnitId: PlayerUnitId,
                            sourceActorKind: "player",
                            targetActorKind: "monster",
                            hitIndex: hitIndex,
                            hitCount: hitTargets.Count,
                            delaySeconds: 0.06f * hitIndex);
                        hitIndex++;
                    }

                    if (anyKilled)
                    {
                        UpdateOccupancy();
                    }

                    return true;
                }

                case ConsumableItemEffectRefs.BlindWeaken:
                {
                    // 혼미 구슬(T4-2): 대상 몬스터에게 실명+쇠약. 쇠약은 몬스터 공격 산식이 이미 소비하고,
                    // 몬스터 측 실명은 BlindedSenseMaskingMonsterAi가 "그 몬스터만 나를 놓친다"로 소비한다.
                    if (!target.HasValue)
                    {
                        reason = "대상 몬스터를 지정해야 합니다.";
                        return false;
                    }

                    var sprayTarget = FindLivingMonsterAt(target.Value);
                    if (sprayTarget == null
                        || visibilityRuntime.GetVisibility(target.Value) != HexCellVisibility.Revealed)
                    {
                        reason = "보이는 몬스터를 지정해야 합니다.";
                        return false;
                    }

                    var sprayTurns = Math.Max(1, item.DurationTurns);
                    AddDurationStatusEffect(
                        StatusEffectKind.Blind, sprayTarget.Id, sprayTurns,
                        StatusEffectInfo.DefaultAmount(StatusEffectKind.Blind), sourceRef);
                    AddDurationStatusEffect(
                        StatusEffectKind.Weaken, sprayTarget.Id, sprayTurns,
                        StatusEffectInfo.DefaultAmount(StatusEffectKind.Weaken), sourceRef);
                    RaiseStatusEffect(StatusEffectKind.Blind, sprayTarget.Coord, 0, sprayTurns, sprayTarget.Id, sourceRef);
                    RaiseStatusEffect(StatusEffectKind.Weaken, sprayTarget.Coord, 0, sprayTurns, sprayTarget.Id, sourceRef);
                    return true;
                }

                case ConsumableItemEffectRefs.SpawnObstacle:
                {
                    // 결계 구슬(T4-2): 빈 칸에 임시 장애물. 철조각과 같은 몬스터 호스팅(점유·피격·세이브
                    // 공짜)이며, 수명은 AgeTurns(세이브 왕복됨) + 아이템 durationTurns로 계산한다.
                    if (!TryResolveVisibleCell(target, out var coneCoord, out reason))
                    {
                        return false;
                    }

                    if (!IsSpawnableCoord(coneCoord))
                    {
                        reason = "빈 칸에만 설치할 수 있습니다.";
                        return false;
                    }

                    if (!TrySpawnMonsterAt(TrafficConePropDefinitionId, coneCoord, MonsterSpawnRoles.PlayerProp, maxHp: 0, monsterId: null, out var coneId))
                    {
                        reason = string.IsNullOrEmpty(LastFailureReason) ? "설치에 실패했습니다." : LastFailureReason;
                        return false;
                    }

                    var cone = monsters.FirstOrDefault(monster => string.Equals(monster.Id, coneId, StringComparison.Ordinal));
                    if (cone != null)
                    {
                        cone.OwnerUnitId = PlayerUnitId;
                        cone.AgeTurns = 0;
                    }

                    return true;
                }

                default:
                    reason = "지원하지 않는 아이템 효과입니다.";
                    return false;
            }
        }

        /// <summary>결계 구슬이 쓰는 기물 몬스터 정의(monster_catalog.csv). 철조각(M901)과 같은 호스팅.
        /// ⚠️M902~M904는 자동 공격 터렛(LV1~3)이 선점한 id다 — 기물이라고 M90x 초반을 넘보지 말 것.</summary>
        internal const string TrafficConePropDefinitionId = "M905";

        private bool TryResolveVisibleCell(HexCoord? target, out HexCoord resolved, out string reason)
        {
            resolved = default;
            reason = string.Empty;
            if (!target.HasValue)
            {
                reason = "대상 칸을 지정해야 합니다.";
                return false;
            }

            if (!Map.TryGetCell(target.Value, out _)
                || visibilityRuntime.GetVisibility(target.Value) != HexCellVisibility.Revealed)
            {
                reason = "보이는 칸만 지정할 수 있습니다.";
                return false;
            }

            resolved = target.Value;
            return true;
        }

        /// <summary>
        /// 실명(T4-2)의 감지 마스킹 술어 — 계획 레이어(<c>MonsterAiPlanner.CreateMonsterFsmContext</c>)가
        /// 몬스터 자기 좌표로 물어 온다(점유가 유일하므로 좌표=신원).
        /// </summary>
        private bool IsMonsterSenseBlindedAt(HexCoord monsterCoord)
        {
            var monster = monsters.FirstOrDefault(candidate =>
                !candidate.Combatant.IsDead && candidate.Coord == monsterCoord);
            if (monster == null)
            {
                return false;
            }

            return activeEffects.Has(monster.Id, StatusEffectKind.Blind);
        }

        /// <summary>
        /// 플레이어 기물(결계 구슬)의 수명 진행 — 매 전체 턴 시작에 나이를 올리고, 아이템 저작 수명이
        /// 다한 기물은 <b>제거</b>한다(사망이 아니다 — 흡수와 같은 계약: 연출·보상·처치 기록 없음).
        /// </summary>
        private void AdvanceAndExpirePlayerProps()
        {
            var expired = new List<MonsterRuntime>();
            foreach (var monster in monsters)
            {
                if (monster.Combatant.IsDead || !MonsterSpawnRoles.IsPlayerProp(monster.SpawnRole))
                {
                    continue;
                }

                monster.AgeTurns++;
                if (monster.AgeTurns >= ResolvePlayerPropLifetimeTurns(monster))
                {
                    expired.Add(monster);
                }
            }

            if (expired.Count == 0)
            {
                return;
            }

            foreach (var monster in expired)
            {
                monsters.Remove(monster);
            }

            UpdateOccupancy();
        }

        private static int ResolvePlayerPropLifetimeTurns(MonsterRuntime prop)
        {
            // 수명 정본은 소모품 CSV다. 기물 정의(M902)와 아이템(item-barrier-bead)의 연결은 effectRef가
            // SpawnObstacle인 아이템을 찾는 것으로 충분하다 — 현행 1종. 늘어나면 그때 컬럼으로 승격한다.
            foreach (var item in ConsumableItemCatalog.Definitions)
            {
                if (string.Equals(item.EffectRef, ConsumableItemEffectRefs.SpawnObstacle, StringComparison.Ordinal))
                {
                    return Math.Max(1, item.DurationTurns);
                }
            }

            return 3;
        }
    }
}
