using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    // ② full-snapshot suspend support. These live on the partial CombatState so they can reach the
    // private runtime (monsters/markedMonster/activeEffects/visibilityRuntime and the misc turn fields)
    // that a sibling assembly cannot. The design is full-snapshot + suspend: no RNG internal state is
    // persisted; monster intent is not stored (it is re-derived deterministically from position/FSM/player
    // by RefreshMonsterIntentStep, exactly as the constructor does on its final line).
    public sealed partial class CombatState
    {
        /// <summary>
        /// 🔴 <b>담기 전에 지연된 몬스터 행동을 확정한다.</b> 연출이 임팩트에 닿기 전까지 규칙 상태는
        /// 공격 <b>전</b>으로 되감겨 있는데(<c>HoldDeferredMonsterActionStateUntilPresentation</c>),
        /// 그 창 안에서 저장하면 <b>이미 해결된 몬스터 공격이 환불된 세이브</b>가 남는다
        /// (2026-08-31 실증: 5피해가 사라졌다).
        /// 세이브는 전투를 치우는 동작이고 재생할 연출이 남아 있지 않으므로, 담기는 값은 언제나
        /// 규칙이 확정한 결과여야 한다. 창이 열려 있지 않으면 무동작이다.
        /// ⚠️ 이 확정이 「죽은 플레이어」를 담을 수 있으므로, 호출부는 그 전에
        /// <see cref="HasDeferredMonsterActionPlayerDeath"/>로 중단 저장 자체를 걸러야 한다.
        /// </summary>
        public CombatSuspendData CreateSuspendSnapshot()
        {
            FinishDeferredMonsterActionState();

            var data = new CombatSuspendData
            {
                OverallTurn = OverallTurnNumber,
                Phase = Phase,
                ActionCostRemaining = ActionCostRemaining,
                LastMovedDistance = lastMovedDistance,
                ActionCardsUsedThisTurn = actionCardsUsedThisTurn,
                TotalActionCardsUsed = totalActionCardsUsed,
                BagAttackBonusThisTurn = bagAttackBonusThisTurn,
                DefensiveCardUsedThisTurn = defensiveCardUsedThisTurn,
                ObjectiveCompleted = ObjectiveCompleted,
                MarkedMonsterId = markedMonster != null ? markedMonster.Id : string.Empty,
                ConsumedTrapIds = consumedTrapIds.ToList(),
                RevealedTrapCoords = visibilityRuntime.RevealedTrapCoords
                    .Select(coord => new HexCoordSaveData(coord))
                    .ToList(),
                PermanentlyRevealedCoords = visibilityRuntime.PermanentlyRevealedCoords
                    .Select(coord => new HexCoordSaveData(coord))
                    .ToList(),
                ClaimedEventObjectIds = claimedEventObjectIds.ToList(),
                SealedBossArenaId = Boss.SealedArenaIdRaw,
                RngCursors = CaptureRngCursors(),
                Player = PlayerRunSaveData.FromCombatState(this),
                TracksRewardClaims = true
            };

            // 🔑 예약 7필드는 PendingEffects가 직접 채운다(T6). 직렬화 필드 이름은 그대로라 기존
            // 세이브와 호환되고, 예약을 늘릴 때 여기 나열을 빠뜨려 세이브만 조용히 깨지는 경로가 없다.
            pending.WriteTo(data);

            foreach (var monster in monsters)
            {
                data.Monsters.Add(CreateMonsterSuspendData(monster));
            }

            foreach (var effect in activeEffects.All)
            {
                data.ActiveEffects.Add(ActiveEffectSaveData.FromEffect(effect));
            }

            foreach (var pair in visibilityRuntime.States)
            {
                data.Visibility.Add(new HexCellVisibilitySaveData(pair.Key, pair.Value));
            }

            foreach (var fieldObject in FieldObjects.Objects)
            {
                data.FieldObjects.Add(FieldObjectSaveData.FromFieldObject(fieldObject));
            }

            foreach (var fieldObject in PendingFieldObjects.Objects)
            {
                data.PendingFieldObjects.Add(FieldObjectSaveData.FromFieldObject(fieldObject));
            }

            // 런타임(보스 배치) 함정(§21.5). 소진 여부는 위의 ConsumedTrapIds가, 발견 표시는
            // RevealedTrapCoords가 이미 함께 실린다 — 여기서는 함정 본체만 왕복시키면 된다.
            data.RuntimeTrapSequence = runtimeTrapSequence;
            foreach (var trap in runtimeTrapRefs)
            {
                data.RuntimeTraps.Add(RuntimeTrapSaveData.FromTrap(trap));
            }

            foreach (var track in Boss.Tracks)
            {
                var trackSave = new BossPhaseTrackSaveData
                {
                    BossUnitId = track.BossUnitId,
                    BossDefinitionId = track.BossDefinitionId,
                    CurrentPhase = track.CurrentPhase,
                    MetricProgress = track.MetricProgress,
                    AbsorbedStacks = track.AbsorbedStacks,
                    AppliedMaxHpBonus = track.AppliedMaxHpBonus,
                    MechanicCooldownTurns = track.MechanicCooldownTurns,
                    TrapVolleyCooldownTurns = track.TrapVolleyCooldownTurns,
                    GuardChargesGrantedPhase = track.GuardChargesGrantedPhase,
                    ScrapChainCooldownTurns = track.ScrapChainCooldownTurns,
                    AnnihilationCooldownTurns = track.AnnihilationCooldownTurns,
                    AnnihilationTelegraphTurnsRemaining = track.AnnihilationTelegraphTurnsRemaining,
                    HasWeakSpot = track.HasWeakSpot,
                    WeakSpotOffsetQ = track.WeakSpotOffsetQ,
                    WeakSpotOffsetR = track.WeakSpotOffsetR,
                    WeakSpotKnownTurnsRemaining = track.WeakSpotKnownTurnsRemaining
                };
                foreach (var cell in track.AnnihilationTelegraphCells)
                {
                    trackSave.AnnihilationTelegraphCells.Add(new HexCellSaveData { Q = cell.Q, R = cell.R });
                }

                foreach (var cell in track.AnnihilationCandidateCells)
                {
                    trackSave.AnnihilationCandidateCells.Add(new HexCellSaveData { Q = cell.Q, R = cell.R });
                }

                foreach (var cell in track.AnnihilationRealSafeCells)
                {
                    trackSave.AnnihilationRealSafeCells.Add(new HexCellSaveData { Q = cell.Q, R = cell.R });
                }

                foreach (var cell in track.AnnihilationRevealedCandidates)
                {
                    trackSave.AnnihilationRevealedCandidates.Add(new HexCellSaveData { Q = cell.Q, R = cell.R });
                }

                foreach (var cell in track.PropVolleyTelegraphCells)
                {
                    trackSave.PropVolleyTelegraphCells.Add(new HexCellSaveData { Q = cell.Q, R = cell.R });
                }

                foreach (var strand in track.ScrapChainStrands)
                {
                    var strandSave = new ScrapChainStrandSaveData { PropUnitId = strand.PropUnitId };
                    foreach (var cell in strand.Cells)
                    {
                        strandSave.Cells.Add(new HexCellSaveData { Q = cell.Q, R = cell.R });
                    }

                    trackSave.ScrapChainStrands.Add(strandSave);
                }

                data.BossPhaseTracks.Add(trackSave);
            }

            return data;
        }

        public void RestoreFromSuspend(CombatSuspendData data)
        {
            if (data == null)
            {
                return;
            }

            // 난수 스트림을 저장된 커서로 되감는다(P5). 덱 복원이 셔플 클로저에 난수원을 잡기 전이어야 한다.
            RestoreRngCursors(data.RngCursors);
            RestorePlayerFromSuspend(data.Player);

            // Context is authoritative for turn/phase/Ki and the derived per-turn counters.
            OverallTurnNumber = System.Math.Max(1, data.OverallTurn);
            Phase = data.Phase;
            ActionCostRemaining = data.ActionCostRemaining;
            pending.ReadFrom(data);
            lastMovedDistance = data.LastMovedDistance;
            actionCardsUsedThisTurn = data.ActionCardsUsedThisTurn;
            totalActionCardsUsed = System.Math.Max(0, data.TotalActionCardsUsed);
            bagAttackBonusThisTurn = System.Math.Max(0, data.BagAttackBonusThisTurn);
            defensiveCardUsedThisTurn = data.DefensiveCardUsedThisTurn;
            ObjectiveCompleted = data.ObjectiveCompleted;

            consumedTrapIds.Clear();
            foreach (var id in data.ConsumedTrapIds ?? new List<string>())
            {
                if (!string.IsNullOrEmpty(id))
                {
                    consumedTrapIds.Add(id);
                }
            }

            claimedEventObjectIds.Clear();
            foreach (var id in data.ClaimedEventObjectIds ?? new List<string>())
            {
                if (!string.IsNullOrEmpty(id))
                {
                    claimedEventObjectIds.Add(id);
                }
            }

            // 결계는 UpdateOccupancy(아래)가 소비하므로 몬스터 복원 뒤·점유 재구축 전에 되살린다.
            Boss.SealedArenaIdRaw = data.SealedBossArenaId ?? string.Empty;

            RestoreMonstersFromSuspend(data.Monsters, data.MarkedMonsterId, data.TracksRewardClaims);
            RestoreBossPhaseTracksFromSuspend(data.BossPhaseTracks);

            activeEffects.RestoreFrom(
                (data.ActiveEffects ?? new List<ActiveEffectSaveData>()).Select(effect => effect.ToEffect()));

            // Clean replace of the fog: reset first (SetVisibility only ever raises a cell), so the
            // constructor's start-spawn reveals never leak in as additive fog on top of the persisted set.
            visibilityRuntime.ResetAll();
            foreach (var cell in data.Visibility ?? new List<HexCellVisibilitySaveData>())
            {
                visibilityRuntime.SetVisibility(cell.ToCoord(), cell.Visibility);
            }

            // 함정 발견 기록(GAP-2)은 안개와 별개 집합이라 위 복원으로 따라오지 않는다. ResetAll이 이미
            // 비운 뒤이므로 여기서 그대로 되살린다.
            visibilityRuntime.RestoreTrapReveals(
                (data.RevealedTrapCoords ?? new List<HexCoordSaveData>()).Select(coord => coord.ToCoord()));
            visibilityRuntime.RestorePermanentReveals(
                (data.PermanentlyRevealedCoords ?? new List<HexCoordSaveData>()).Select(coord => coord.ToCoord()));

            // Restore active/pending field objects before occupancy so their movement-blocking tiles are
            // re-derived by UpdateOccupancy (matching the live card-deploy path).
            FieldObjects.Clear();
            foreach (var fieldObject in data.FieldObjects ?? new List<FieldObjectSaveData>())
            {
                FieldObjects.Add(fieldObject.ToFieldObject());
            }

            PendingFieldObjects.Clear();
            foreach (var fieldObject in data.PendingFieldObjects ?? new List<FieldObjectSaveData>())
            {
                PendingFieldObjects.Add(fieldObject.ToFieldObject());
            }

            // 런타임(보스 배치) 함정(§21.5). 발견 표시는 위 RestoreTrapReveals가 이미 되살렸고
            // 소진 기록은 consumedTrapIds 복원이 담당한다 — 본체 목록과 id 시퀀스만 채우면 된다.
            RestoreRuntimeTraps(
                (data.RuntimeTraps ?? new List<RuntimeTrapSaveData>()).Select(trap => trap.ToTrap()),
                data.RuntimeTrapSequence);

            // Re-derive occupancy and monster intent from the restored positions/FSM, mirroring the
            // constructor's final two calls. Intent geometry is intentionally not persisted — but the
            // committed pattern index and damage roll ARE (「굴린 값이 상태」), so this refresh must not
            // re-roll them: re-rolling overwrote the saved intent and advanced the RNG cursor past the
            // saved one, which is exactly what P5 (RngCursors) exists to prevent.
            UpdateOccupancy();
            planner.RefreshAllIntents(preserveCommittedAttackRolls: true);

            // The resume boot path constructs with drawOpeningHands:false, which arms playerTurnDrawPending.
            // Restore has just refilled the hands from the persisted zones, so clearing the flag stops the
            // start-of-turn sequence (StartPlayerTurn) from appending a second opening draw on top of the
            // restored hand — which would duplicate cards and corrupt the zone order this feature preserves.
            playerTurnDrawPending = false;
        }

        /// <summary>
        /// Monster definition ids from the last <see cref="RestoreFromSuspend"/> that had no catalog entry
        /// and were rebuilt with a generic fallback pattern. Empty when every id resolved. Surfaced so the
        /// Unity layer (which owns logging) can warn without the pure-C# runtime referencing UnityEngine.
        /// </summary>
        public IReadOnlyList<string> SuspendRestoreUnresolvedMonsterDefinitionIds => suspendRestoreUnresolvedMonsterDefinitionIds;

        private readonly List<string> suspendRestoreUnresolvedMonsterDefinitionIds = new List<string>();

        private void RestorePlayerFromSuspend(PlayerRunSaveData player)
        {
            if (player == null)
            {
                return;
            }

            var vitals = player.Vitals ?? new PlayerVitalsSaveData();
            // Upper-clamp to MaxHp for parity with the ① RestorePlayerRunHp path so a corrupt save can
            // never resume above the player's max (RestoreVitals already floors at 0).
            Player.RestoreVitals(System.Math.Min(vitals.Hp, Player.MaxHp), vitals.Block);

            var position = player.Position ?? new PlayerPositionSaveData();
            PlayerCoord = new HexCoord(position.Q, position.R);

            var decks = player.Decks ?? new PlayerDeckSaveData();
            PlayerDeck = decks.ToPlayerDeckData();
            // 저장된 더미 순서는 그대로, 이후 재셔플만 런 시드 스트림을 탄다(P2).
            MovementDeck = decks.CreateMovementDeck(CardCatalog, DeckShuffleFor(SeoulPlayup.CardCore.CardCategory.Movement));
            ActionDeck = decks.CreateActionDeck(CardCatalog, DeckShuffleFor(SeoulPlayup.CardCore.CardCategory.Action));

            PlayerInventory = (player.Inventory ?? new PlayerInventorySaveData()).ToState();
        }

        private MonsterRuntimeSaveData CreateMonsterSuspendData(MonsterRuntime monster)
        {
            var fsm = monster.FsmMemory;
            var save = new MonsterRuntimeSaveData
            {
                Id = monster.Id,
                CatalogSourceId = monster.CatalogSourceId,
                DefinitionId = monster.DefinitionId,
                SpawnRefId = monster.SpawnRefId,
                SpawnRole = monster.SpawnRole,
                Q = monster.Coord.Q,
                R = monster.Coord.R,
                SpawnQ = monster.SpawnCoord.Q,
                SpawnR = monster.SpawnCoord.R,
                Hp = monster.Combatant.Hp,
                MaxHp = monster.Combatant.MaxHp,
                Block = monster.Combatant.Block,
                ActivityState = monster.ActivityState,
                AttackPatternIndex = monster.AttackPatternIndex,
                PendingAttackIntent = monster.PendingAttackIntent,
                AttackDamageRollOffset = monster.AttackDamageRollOffset,
                OwnerUnitId = monster.OwnerUnitId,
                AgeTurns = monster.AgeTurns,
                AgitationStacks = monster.AgitationStacks,
                RewardClaimed = monster.RewardClaimed,
                HasWeakSpot = monsterWeakSpots.TryGetValue(monster.Id, out var weakSpotSlot) && weakSpotSlot.HasWeakSpot,
                WeakSpotOffsetQ = weakSpotSlot?.WeakSpotOffsetQ ?? 0,
                WeakSpotOffsetR = weakSpotSlot?.WeakSpotOffsetR ?? 0,
                WeakSpotKnownTurnsRemaining = weakSpotSlot?.WeakSpotKnownTurnsRemaining ?? 0,
                StolenMoney = monster.StolenMoney,
                ToughnessSpent = monster.ToughnessSpent,
                ToughnessReloadProgress = monster.ToughnessReloadProgress,
                GuardRechargeProgress = monster.GuardRechargeProgress,
                StealthRevealTurnsRemaining = monster.StealthRevealTurnsRemaining,
                FsmState = fsm.State,
                FsmPreAlertState = fsm.PreAlertState,
                HasLastKnownPlayerCoord = fsm.LastKnownPlayerCoord.HasValue,
                LastKnownPlayerQ = fsm.LastKnownPlayerCoord?.Q ?? 0,
                LastKnownPlayerR = fsm.LastKnownPlayerCoord?.R ?? 0,
                SearchTurnsRemaining = fsm.SearchTurnsRemaining,
                AlertTurnsRemaining = fsm.AlertTurnsRemaining,
                AlertRangeBonus = fsm.AlertRangeBonus,
                PatrolCursor = fsm.PatrolCursor
            };

            foreach (var pair in monster.CloneAttackPatternCooldowns())
            {
                save.AttackPatternCooldowns.Add(new MonsterCooldownSaveData { Index = pair.Key, Remaining = pair.Value });
            }

            foreach (var coord in monster.PatrolArea)
            {
                save.PatrolArea.Add(new HexCoordSaveData(coord));
            }

            return save;
        }

        private void RestoreMonstersFromSuspend(List<MonsterRuntimeSaveData> saved, string markedMonsterId, bool tracksRewardClaims)
        {
            monsters.Clear();
            monsterWeakSpots.Clear();
            markedMonster = null;
            suspendRestoreUnresolvedMonsterDefinitionIds.Clear();
            if (saved == null)
            {
                return;
            }

            foreach (var save in saved)
            {
                var attackSpeed = 1;
                var movePerTurn = 1;
                MonsterAttackPattern[] attackPatterns = null;
                if (monsterCatalog.TryGetEntry(save.DefinitionId, out var entry))
                {
                    attackSpeed = entry.AttackSpeed;
                    movePerTurn = entry.MovePerTurn;
                    attackPatterns = entry.AttackPatterns;
                }
                else
                {
                    // No catalog entry: the monster is rebuilt with a generic fallback attack pattern.
                    // Record the id (never throw) so the Unity layer can log the degraded restore.
                    suspendRestoreUnresolvedMonsterDefinitionIds.Add(save.DefinitionId ?? string.Empty);
                }

                var patrolArea = (save.PatrolArea ?? new List<HexCoordSaveData>())
                    .Select(coord => coord.ToCoord())
                    .ToList();
                var combatant = new CombatantState(save.Id, save.MaxHp);
                // Construct at the saved spawn coord so SpawnCoord (used by patrol/return behavior) is
                // exact, then move the runtime to its current coord.
                var monster = new MonsterRuntime(
                    save.Id,
                    new HexCoord(save.SpawnQ, save.SpawnR),
                    combatant,
                    save.CatalogSourceId,
                    save.DefinitionId,
                    save.SpawnRefId,
                    save.SpawnRole,
                    attackSpeed,
                    attackPatterns,
                    patrolArea,
                    movePerTurn);

                monster.Coord = new HexCoord(save.Q, save.R);
                if (monsterCatalog.TryGetEntry(save.DefinitionId, out var footprintEntry))
                {
                    monster.FootprintShape = footprintEntry.FootprintShape;
                }

                // Upper-clamp to the monster's MaxHp (parity with the player restore) so a corrupt save
                // cannot resume a monster above its max; RestoreVitals already floors at 0.
                combatant.RestoreVitals(System.Math.Min(save.Hp, combatant.MaxHp), save.Block);
                monster.ActivityState = save.ActivityState;
                monster.AttackPatternIndex = save.AttackPatternIndex;
                monster.PendingAttackIntent = save.PendingAttackIntent;
                monster.AttackDamageRollOffset = save.AttackDamageRollOffset;
                monster.OwnerUnitId = save.OwnerUnitId ?? string.Empty;
                monster.AgeTurns = System.Math.Max(0, save.AgeTurns);
                monster.AgitationStacks = System.Math.Max(0, save.AgitationStacks);
                // 플래그가 없던 옛 저장은 시체를 전부 지급 완료로 본다 — 이어하기마다 보상이 되풀이되는 것이
                // 열려 있던 전리품 창 하나를 잃는 것보다 나쁘다(2026-09-05 #7).
                monster.RewardClaimed = tracksRewardClaims ? save.RewardClaimed : save.Hp <= 0;
                if (save.HasWeakSpot)
                {
                    var restoredSlot = new MonsterWeakSpotSlot();
                    restoredSlot.SetWeakSpotOffset(save.WeakSpotOffsetQ, save.WeakSpotOffsetR);
                    restoredSlot.WeakSpotKnownTurnsRemaining = System.Math.Max(0, save.WeakSpotKnownTurnsRemaining);
                    monsterWeakSpots[monster.Id] = restoredSlot;
                }
                monster.StolenMoney = System.Math.Max(0, save.StolenMoney);
                monster.ToughnessSpent = save.ToughnessSpent;
                monster.ToughnessReloadProgress = System.Math.Max(0, save.ToughnessReloadProgress);
                monster.GuardRechargeProgress = System.Math.Max(0, save.GuardRechargeProgress);
                monster.StealthRevealTurnsRemaining = System.Math.Max(0, save.StealthRevealTurnsRemaining);

                monster.FsmMemory.State = save.FsmState;
                monster.FsmMemory.PreAlertState = save.FsmPreAlertState;
                monster.FsmMemory.LastKnownPlayerCoord = save.HasLastKnownPlayerCoord
                    ? new HexCoord(save.LastKnownPlayerQ, save.LastKnownPlayerR)
                    : (HexCoord?)null;
                monster.FsmMemory.SearchTurnsRemaining = save.SearchTurnsRemaining;
                monster.FsmMemory.AlertTurnsRemaining = save.AlertTurnsRemaining;
                monster.FsmMemory.AlertRangeBonus = save.AlertRangeBonus;
                monster.FsmMemory.PatrolCursor = save.PatrolCursor;

                var cooldowns = new Dictionary<int, int>();
                foreach (var cooldown in save.AttackPatternCooldowns ?? new List<MonsterCooldownSaveData>())
                {
                    cooldowns[cooldown.Index] = cooldown.Remaining;
                }

                monster.RestoreCooldowns(cooldowns);
                monsters.Add(monster);
            }

            if (!string.IsNullOrEmpty(markedMonsterId))
            {
                markedMonster = monsters.FirstOrDefault(monster => monster.Id == markedMonsterId);
            }

            // 힘 상태이상을 저장된 카운터에서 다시 세운다(2026-09-04). 카운터가 정본이고 상태이상은
            // 그 투영이라, 힘을 모르는 구세이브도 불러오는 즉시 배지·피해가 맞는다. 멱등이라
            // 새 세이브(효과까지 저장된)에서 두 벌이 되지도 않는다.
            RebuildMonsterMightEffects();
        }

        /// <summary>
        /// 보스 페이즈 트랙 복원. 저장된 트랙을 먼저 그대로 되살리고, 그 뒤 <c>EnsureBossPhaseTracks</c>로
        /// 트랙이 없던 보스(예: 세이브가 만들어진 뒤 보스 프로필이 저작된 경우)를 1페이즈로 보강한다.
        /// 복원은 <b>기록만</b> 한다 — 페이즈 진입 효과를 다시 적용하지 않는다. 몬스터의 MaxHp/HP는
        /// 이미 보너스가 반영된 값으로 저장되어 있고, <c>AppliedMaxHpBonus</c>도 함께 복원되므로
        /// 다음 페이즈 전환의 차분 계산이 정확하게 이어진다.
        /// </summary>
        private void RestoreBossPhaseTracksFromSuspend(List<BossPhaseTrackSaveData> saved)
        {
            Boss.Tracks.Clear();
            foreach (var save in saved ?? new List<BossPhaseTrackSaveData>())
            {
                if (string.IsNullOrEmpty(save.BossUnitId))
                {
                    continue;
                }

                var restored = new BossPhaseTrack(save.BossUnitId, save.BossDefinitionId)
                {
                    CurrentPhase = System.Math.Max(1, save.CurrentPhase),
                    MetricProgress = System.Math.Max(0, save.MetricProgress),
                    AbsorbedStacks = System.Math.Max(0, save.AbsorbedStacks),
                    AppliedMaxHpBonus = System.Math.Max(0, save.AppliedMaxHpBonus),
                    MechanicCooldownTurns = System.Math.Max(0, save.MechanicCooldownTurns),
                    TrapVolleyCooldownTurns = System.Math.Max(0, save.TrapVolleyCooldownTurns),
                    GuardChargesGrantedPhase = System.Math.Max(0, save.GuardChargesGrantedPhase),
                    ScrapChainCooldownTurns = System.Math.Max(0, save.ScrapChainCooldownTurns),
                    AnnihilationCooldownTurns = System.Math.Max(0, save.AnnihilationCooldownTurns),
                    AnnihilationTelegraphTurnsRemaining = System.Math.Max(0, save.AnnihilationTelegraphTurnsRemaining),
                    WeakSpotKnownTurnsRemaining = System.Math.Max(0, save.WeakSpotKnownTurnsRemaining)
                };
                if (save.HasWeakSpot)
                {
                    restored.SetWeakSpotOffset(save.WeakSpotOffsetQ, save.WeakSpotOffsetR);
                }
                foreach (var cell in save.AnnihilationTelegraphCells ?? new List<HexCellSaveData>())
                {
                    restored.AnnihilationTelegraphCells.Add(new SeoulPlayup.Map.Runtime.HexCoord(cell.Q, cell.R));
                }

                foreach (var cell in save.AnnihilationCandidateCells ?? new List<HexCellSaveData>())
                {
                    restored.AnnihilationCandidateCells.Add(new SeoulPlayup.Map.Runtime.HexCoord(cell.Q, cell.R));
                }

                foreach (var cell in save.AnnihilationRealSafeCells ?? new List<HexCellSaveData>())
                {
                    restored.AnnihilationRealSafeCells.Add(new SeoulPlayup.Map.Runtime.HexCoord(cell.Q, cell.R));
                }

                foreach (var cell in save.AnnihilationRevealedCandidates ?? new List<HexCellSaveData>())
                {
                    restored.AnnihilationRevealedCandidates.Add(new SeoulPlayup.Map.Runtime.HexCoord(cell.Q, cell.R));
                }

                foreach (var cell in save.PropVolleyTelegraphCells ?? new List<HexCellSaveData>())
                {
                    restored.PropVolleyTelegraphCells.Add(new SeoulPlayup.Map.Runtime.HexCoord(cell.Q, cell.R));
                }

                foreach (var strand in save.ScrapChainStrands ?? new List<ScrapChainStrandSaveData>())
                {
                    restored.ScrapChainStrands.Add(new ScrapChainStrand(
                        strand.PropUnitId,
                        (strand.Cells ?? new List<HexCellSaveData>())
                            .Select(cell => new SeoulPlayup.Map.Runtime.HexCoord(cell.Q, cell.R))));
                }

                Boss.Tracks.Add(restored);
            }

            EnsureBossPhaseTracks();
        }
    }
}
