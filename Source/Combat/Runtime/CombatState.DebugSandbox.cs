using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.CardCore;
using SeoulPlayup.Map.Runtime;
using CardDefinition = SeoulPlayup.CardCore.CardDefinition;

namespace SeoulPlayup.Combat.Runtime
{
    /// <summary>
    /// Card sandbox rules surface (docs/card-sandbox-scene-plan.md). These exist so the editor-only
    /// <c>CombatDebugControlPanel</c> can set up the *situations* a card needs — a dummy to hit, a debuff on the
    /// target, a non-empty 소멸 더미 — inside a live MainGameplay combat, instead of hand-editing starting decks.
    ///
    /// House rules for everything in this file, matching the existing <c>Debug*</c> tuning helpers:
    /// - additive/restorative only, never called by gameplay, so shipped play is unaffected;
    /// - they go through the same paths a card would (notably <see cref="ApplyStatusToMonster"/>), never poking
    ///   <c>activeEffects</c> or a monster's committed <c>TurnPlan</c> directly;
    /// - they return what happened so the panel can report it instead of failing silently.
    /// </summary>
    public sealed partial class CombatState
    {
        private const string DebugSandboxSourceRef = "debug.sandbox";
        private const string DebugSandboxMonsterIdPrefix = "monster-sandbox-";

        // Ids this sandbox spawned, so "더미 제거" can clean up after itself without touching monsters the
        // authored map spawned.
        private readonly HashSet<string> debugSandboxMonsterIds = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Monster ids this sandbox spawned and has not removed yet.</summary>
        public IReadOnlyCollection<string> DebugSandboxMonsterIds => debugSandboxMonsterIds;

        /// <summary>
        /// Spawn a dummy monster from a real catalog entry, so it gets the catalog's attack patterns, move rate
        /// and visual prefab exactly like an authored spawn. <paramref name="maxHp"/> of 0 takes the catalog Hp.
        /// The monster is classified and given a turn plan through the normal path, so it is immediately a live
        /// participant in the turn loop rather than a dormant prop.
        /// </summary>
        public bool DebugSandboxSpawnMonsterAt(string definitionId, HexCoord coord, int maxHp, out string spawnedId)
        {
            // 스폰 자체는 프로덕션 경로(TrySpawnMonsterAt)를 그대로 쓴다 — 검증(맵 안·걸을 수 있음·비어 있음)이
            // 한 곳에만 있어야 샌드박스와 실제 스폰이 갈라지지 않는다. 샌드박스가 더하는 것은 "내가 만든 것"
            // 추적(더미 제거용)과 실패 메시지의 접두어뿐이다.
            // 역할은 "normal-enemy": 저작 맵이 평범한 조우에 쓰는 역할이라, 활동 분류·인텐트 예고가
            // 실제 몬스터와 동일하게 동작해야 카드 테스트가 의미를 갖는다.
            var id = CreateDebugSandboxMonsterId();
            if (!TrySpawnMonsterAt(definitionId, coord, MonsterSpawnRoles.NormalEnemy, maxHp, id, out spawnedId))
            {
                LastFailureReason = $"Sandbox: {LastFailureReason}";
                return false;
            }

            debugSandboxMonsterIds.Add(spawnedId);
            return true;
        }

        /// <summary>
        /// Remove every monster this sandbox spawned. Removal is silent (no death effect) because these are props,
        /// not kills — use a real attack when you want to see the death presentation. Returns how many went away.
        /// </summary>
        public int DebugRemoveSandboxMonsters()
        {
            if (debugSandboxMonsterIds.Count == 0)
            {
                return 0;
            }

            var removed = monsters.RemoveAll(monster => debugSandboxMonsterIds.Contains(monster.Id));
            debugSandboxMonsterIds.Clear();
            if (removed > 0)
            {
                UpdateOccupancy();
                RefreshMonsterActivityStatesForAction();
            }

            return removed;
        }

        /// <summary>
        /// A walkable, unoccupied tile at <paramref name="preferredDistance"/> from the player, falling back to the
        /// nearest free tile when that ring is full. This is where "더미 소환" puts a dummy so it lands in range of
        /// the card being tested without the designer typing coordinates.
        /// </summary>
        public bool DebugTryFindFreeCoordNearPlayer(int preferredDistance, out HexCoord coord)
        {
            return DebugTryFindFreeCoordNearPlayer(preferredDistance, string.Empty, out coord);
        }

        /// <summary>
        /// <paramref name="definitionId"/>가 형상 footprint(삼각형 정예)면 앵커만이 아니라 <b>몸통 칸 전부</b>가 빈 후보만 고른다 —
        /// 앵커만 보고 고르면 스폰 검증(TrySpawnMonsterAt)이 몸통 칸에서 튕겨 「요괴 6종」 프리셋이 반쪽만 선다.
        /// </summary>
        public bool DebugTryFindFreeCoordNearPlayer(int preferredDistance, string definitionId, out HexCoord coord)
        {
            coord = PlayerCoord;
            var offsets = MonsterFootprints.SingleOffsets;
            if (!string.IsNullOrEmpty(definitionId) && monsterCatalog != null && monsterCatalog.TryGetEntry(definitionId, out var entry))
            {
                offsets = MonsterFootprints.OffsetsOf(entry.FootprintShape);
            }

            bool IsFree(HexCoord candidate) =>
                candidate != PlayerCoord
                && !runtimeStates.ContainsKey(candidate)
                && Map.TryGetCell(candidate, out var candidateCell)
                && candidateCell.BaseWalkable
                && !Map.HasMovementBlockingObject(candidate);

            var candidates = Map.AllCells
                .Where(cell => cell.BaseWalkable)
                .Select(cell => cell.Coord)
                .Where(candidate => offsets.All(offset => IsFree(candidate + offset)))
                .ToList();
            if (candidates.Count == 0)
            {
                LastFailureReason = "Sandbox: no free walkable tile to spawn on.";
                return false;
            }

            var target = Math.Max(1, preferredDistance);
            // Prefer the requested ring, then the closest ring to it; ties break on coordinate order so repeated
            // spawns fan out deterministically instead of stacking on one tile.
            coord = candidates
                .OrderBy(candidate => Math.Abs(PlayerCoord.DistanceTo(candidate) - target))
                .ThenBy(candidate => candidate)
                .First();
            LastFailureReason = string.Empty;
            return true;
        }

        /// <summary>
        /// Apply a status to the player through the same duration-effect path a card or trap uses, including the
        /// presentation event, so the HUD icon and floating text show up exactly as in play.
        /// </summary>
        public bool DebugApplyStatusToPlayer(StatusEffectKind kind, int turns, int amount)
        {
            if (Player.IsDead)
            {
                LastFailureReason = "Sandbox: player is dead.";
                return false;
            }

            var clampedTurns = Math.Max(1, turns);
            AddDurationStatusEffect(kind, PlayerUnitId, clampedTurns, Math.Max(0, amount), DebugSandboxSourceRef);
            RaiseStatusEffect(kind, PlayerCoord, 0, Math.Max(0, amount), PlayerUnitId, DebugSandboxSourceRef);
            LastFailureReason = string.Empty;
            return true;
        }

        /// <summary>
        /// Apply a status to every living monster. Routed through <see cref="ApplyStatusToMonster"/> so hard control
        /// (기절/속박) also refreshes the committed plan and emits the intent-cancel cue — poking activeEffects here
        /// would leave a stunned monster telegraphing an attack it can no longer make. Returns how many were hit.
        /// </summary>
        public int DebugApplyStatusToAllMonsters(StatusEffectKind kind, int turns, int amount)
        {
            var targets = monsters.Where(monster => !monster.Combatant.IsDead).ToList();
            foreach (var monster in targets)
            {
                ApplyStatusToMonster(monster, kind, Math.Max(1, turns), Math.Max(0, amount), DebugSandboxSourceRef);
            }

            LastFailureReason = string.Empty;
            return targets.Count;
        }

        /// <summary>
        /// Put <paramref name="count"/> cards into the 소멸 더미 without disturbing the current hand: each card is
        /// injected and then permanently removed. A13 잔혼 공격 scales off this pile and U02 recovers from it, so a
        /// non-empty pile is the precondition for testing either. Returns how many were added.
        /// </summary>
        public int DebugFillExilePile(int count)
        {
            if (count <= 0 || CardCatalog == null)
            {
                return 0;
            }

            // Action-deck entries only: the pile is shared, and drawing filler from one deck keeps the injected
            // cards recognisable when the exile overlay lists them.
            var entries = CardCatalog.Entries
                .Where(entry => entry.DeckType == CardCategory.Action)
                .ToList();
            if (entries.Count == 0)
            {
                return 0;
            }

            var added = 0;
            for (var i = 0; i < count; i++)
            {
                var entry = entries[i % entries.Count];
                var card = entry.ToCardDefinition(CardCatalog.SourceId, CreateRuntimeInstanceId(entry.Id, "sandbox-exile"));
                ActionDeck.InjectIntoHand(card);
                if (ActionDeck.PermanentRemoveFromHand(card))
                {
                    added++;
                }
            }

            return added;
        }

        /// <summary>
        /// Deal unblockable-looking damage to the player through the real damage path (Block still absorbs, which is
        /// the point when checking D02/D05 피해 무효). Returns the damage that actually landed.
        /// </summary>
        public int DebugDamagePlayer(int amount)
        {
            if (amount <= 0 || Player.IsDead)
            {
                return 0;
            }

            var blockBefore = Player.Block;
            var applied = Player.ApplyDamage(amount);
            if (applied > 0)
            {
                RaiseEffect(
                    EffectKind.Damage,
                    PlayerCoord,
                    0,
                    applied,
                    PlayerUnitId,
                    DebugSandboxSourceRef,
                    targetActorKind: "player");
            }
            else if (Player.Block < blockBefore)
            {
                // Fully absorbed: surface "방어!" rather than a "-0" hit, matching the trap path.
                RaiseEffect(
                    EffectKind.DamageBlocked,
                    PlayerCoord,
                    0,
                    0,
                    PlayerUnitId,
                    DebugSandboxSourceRef,
                    targetActorKind: "player");
            }

            return applied;
        }

        /// <summary>
        /// True when the card class registered for <paramref name="cardId"/> overrides at least one rule hook
        /// (<see cref="Cards.CardBehavior.HasCustomRules"/>).
        ///
        /// This is the sandbox's answer to the trap that actually bit during P6: a rule can be written and its
        /// registration forgotten (or an override signature mistyped), which compiles and imports cleanly while the
        /// card silently runs the generic path. A false here is NOT an error by itself — plenty of cards
        /// intentionally have no custom rule (U01 redraw, S00 scout.reveal, plain attack/defend) — it is a fact
        /// for a human to judge against what the card is supposed to do.
        /// </summary>
        public bool DebugIsCardHandled(string cardId)
        {
            return Cards.CardBehaviorRegistry.TryGet(cardId, out var behavior) && behavior.HasCustomRules;
        }

        /// <summary>
        /// How a card actually reaches its behaviour, as a short label for the sandbox readout. There are two
        /// dispatch surfaces, and asking only about the card class gets 설치(field) cards wrong: those key off
        /// <see cref="CardFieldObjectKind"/> instead, so F04/F05 would read as unhandled while working fine.
        ///
        /// "기본" means the card runs the generic path for its type, which is correct for most of the catalog —
        /// it is only a red flag when you just wrote a rule for that card.
        /// </summary>
        public string DebugDescribeCardHandling(CardCatalogEntry entry)
        {
            if (entry == null)
            {
                return "(없음)";
            }

            if (entry.FieldObjectKind != CardFieldObjectKind.None)
            {
                var runtimeKind = ToRuntimeFieldObjectKind(entry.FieldObjectKind);
                return IsFieldObjectKindRegistered(runtimeKind)
                    ? $"설치 핸들러 ({runtimeKind})"
                    : $"설치 핸들러 없음 ({runtimeKind}) ⚠";
            }

            return DebugIsCardHandled(entry.Id) ? "핸들러" : "기본";
        }

        /// <summary>Every card id whose class overrides a rule hook, sorted for stable display.</summary>
        public IReadOnlyList<string> DebugHandledCardIds =>
            Cards.CardBehaviorRegistry.RegisteredIds
                .Where(DebugIsCardHandled)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Puts <paramref name="cardId"/> into the player's hand and restores a fresh, fully-funded player
        /// action phase, so the card can then be replayed through its own real <c>TryPlayer*</c> path rather
        /// than a presentation-only stand-in.
        ///
        /// Tuning only. The injected instance is deliberately <b>not</b> added to <c>PlayerDeck</c> — unlike
        /// <see cref="TryAddRewardCardToCurrentHand"/>, which grants a card for the rest of the run — so
        /// replaying a card in the lab never changes the run's deck contents. A card already in hand is
        /// reused instead of duplicated.
        /// </summary>
        public bool DebugPrepareCardForTuning(string cardId, out CardDefinition card, out string reason)
        {
            card = null;
            reason = string.Empty;

            if (string.IsNullOrWhiteSpace(cardId))
            {
                reason = "카드 id가 비어 있습니다.";
                return false;
            }

            var entry = CardCatalog.Entries.FirstOrDefault(
                candidate => string.Equals(candidate.Id, cardId, StringComparison.Ordinal));
            if (entry == null)
            {
                reason = $"카탈로그에 없는 카드입니다: {cardId}";
                return false;
            }

            // Phase / Ki / HP first: CanUseActionCard gates on all three, so a card injected into a spent or
            // non-action phase would still be unplayable.
            DebugRestoreCombatantsForTuning();

            // The restore above lands on PlayerAction, which is right for every card except movement ones —
            // those are gated to PlayerMovement, so without this they would be injected and then refused.
            if (entry.PhaseAvailability == CardUsePhase.Movement)
            {
                SetPhase(CombatPhase.PlayerMovement);
            }

            var deck = entry.DeckType == CardCategory.Movement ? MovementDeck : ActionDeck;
            card = deck.Hand.FirstOrDefault(candidate => MatchesCardKey(candidate, cardId));
            if (card == null)
            {
                card = entry.ToCardDefinition(CardCatalog.SourceId, CreateRuntimeInstanceId(entry.Id, "tuning"));
                deck.InjectIntoHand(card);
            }

            return true;
        }

        /// <summary>
        /// Catalog cards of one effect type (id + display name), for the tuning lab's replay list. Reads the
        /// catalog rather than the player's deck so a card can be auditioned before it is ever drafted —
        /// which is the case for every newly authored card.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> DebugGetCatalogCardChoices(CardEffectType effectType)
        {
            return CardCatalog.Entries
                .Where(entry => entry.ActionType == effectType && !string.IsNullOrWhiteSpace(entry.Id))
                .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                .Select(entry => new KeyValuePair<string, string>(
                    entry.Id, string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.Id : entry.DisplayName))
                .ToList();
        }

        /// <summary>
        /// First map cell that passes <paramref name="validate"/>, searched outward from the nearest living
        /// monster and then around the player. The caller passes the card's own validator, so target rules
        /// (range, walkability, occupancy) come from the rules layer instead of being restated here.
        /// </summary>
        public bool DebugTryPickTargetForTuning(
            int maxRadius, Func<HexCoord, bool> validate, out HexCoord target)
        {
            target = default;
            if (validate == null)
            {
                return false;
            }

            foreach (var candidate in DebugEnumerateTargetCandidates(Math.Max(0, maxRadius)))
            {
                if (validate(candidate))
                {
                    target = candidate;
                    return true;
                }
            }

            return false;
        }

        private IEnumerable<HexCoord> DebugEnumerateTargetCandidates(int maxRadius)
        {
            var nearestMonster = monsters
                .Where(monster => !monster.Combatant.IsDead)
                .OrderBy(monster => PlayerCoord.DistanceTo(monster.Coord))
                .ThenBy(monster => monster.Id, StringComparer.Ordinal)
                .Select(monster => (HexCoord?)monster.Coord)
                .FirstOrDefault();

            // A monster's tile first: it is what a designer wants to look at for scout/field cards, and it is
            // the case most likely to be rejected (occupied), which the search then walks away from.
            if (nearestMonster.HasValue)
            {
                yield return nearestMonster.Value;

                foreach (var neighbour in nearestMonster.Value.NeighborsInDirectionOrder())
                {
                    yield return neighbour;
                }
            }

            yield return PlayerCoord;

            // Rings outward from the player, nearest first, so the picked tile stays close to the action.
            for (var radius = 1; radius <= maxRadius; radius++)
            {
                for (var dq = -radius; dq <= radius; dq++)
                {
                    var minDr = Math.Max(-radius, -dq - radius);
                    var maxDr = Math.Min(radius, -dq + radius);
                    for (var dr = minDr; dr <= maxDr; dr++)
                    {
                        var coord = new HexCoord(PlayerCoord.Q + dq, PlayerCoord.R + dr);
                        if (PlayerCoord.DistanceTo(coord) == radius)
                        {
                            yield return coord;
                        }
                    }
                }
            }
        }

        private string CreateDebugSandboxMonsterId()
        {
            for (var i = 1; ; i++)
            {
                var candidate = DebugSandboxMonsterIdPrefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (monsters.All(monster => !string.Equals(monster.Id, candidate, StringComparison.Ordinal)))
                {
                    return candidate;
                }
            }
        }

        // ===== 아래는 CombatState.cs에서 이관된 Dev/튜닝 표면(cs:767). 위 샌드박스와 같은 하우스 룰을 따른다. =====

        public bool DebugInjectCardIntoHand(string cardId)
        {
            if (string.IsNullOrEmpty(cardId))
            {
                return false;
            }

            var entry = CardCatalog.Entries.FirstOrDefault(e => string.Equals(e.Id, cardId, StringComparison.Ordinal));
            if (entry == null)
            {
                return false;
            }

            var card = entry.ToCardDefinition(CardCatalog.SourceId, CreateRuntimeInstanceId(entry.Id, "debug"));
            if (entry.DeckType == CardCategory.Movement)
            {
                MovementDeck.InjectIntoHand(card);
            }
            else
            {
                ActionDeck.InjectIntoHand(card);
            }

            return true;
        }

        // --- Dev/tuning sandbox (editor/dev-build CombatDebugControlPanel "Timing" tab only) ---
        // These set up a deterministic, repeatable sandbox so presentation timing (attack windup/impact,
        // hit reaction, damage VFX, camera shake, hit stop) can be tuned without hunting for a monster,
        // drawing cards, or managing ki. They are restorative/additive and never run unless the dev panel
        // calls them, so gameplay and release builds are unaffected.

        /// <summary>
        /// 함정 발견 기록만 정찰과 같은 경로로 남긴다(<c>HexVisibilityRuntime.RevealTrapsInArea</c>) — 안개는
        /// 건드리지 않는다. 랩에서 함정 마커와 예고형 오버레이를 보려면 발견 상태가 필요한데, 정찰 카드를
        /// 손에 쥐여 주고 기력을 맞추는 것은 "함정을 확인한다"는 목적과 무관한 준비 작업이기 때문이다.
        ///
        /// ⚠️발견은 <b>안개와 독립</b>이다 — 안개를 전부 걷어도(<c>revealAllMapCellsInDebugMode</c>) 함정
        /// 마커는 안 뜬다. 실제로 이 둘을 같은 것으로 착각하면 "랩에서 함정이 안 보인다"고 오진하게 된다.
        /// </summary>
        /// <returns>이 호출로 새로 발견된 함정 수(이미 발견된 것은 세지 않는다).</returns>
        public int DebugRevealTrapsForTuning(HexCoord center, int radius)
        {
            var before = visibilityRuntime.RevealedTrapCoords.Count;
            visibilityRuntime.RevealTrapsInArea(center, Math.Max(0, radius));
            // 정찰 본경로(TryPlayerScout)와 같은 짝 — 런타임(보스 배치) 함정도 함께 발견해야
            // 랩에서 보스 함정 마커를 확인할 수 있다(2026-09-03 함정 숨김 전환).
            RevealRuntimeTrapsInArea(center, Math.Max(0, radius));
            return visibilityRuntime.RevealedTrapCoords.Count - before;
        }

        /// <summary>
        /// Restore every combatant to full health in place (reviving any that died) and reset to a fresh
        /// player action phase with full ki, so the next replayed attack starts from identical conditions.
        /// Positions are left untouched so the dummy stays where the designer set up the fight.
        /// </summary>
        public void DebugRestoreCombatantsForTuning()
        {
            Player.Heal(Player.MaxHp);
            foreach (var monster in monsters)
            {
                monster.Combatant.Heal(monster.Combatant.MaxHp);
            }

            SetPhase(CombatPhase.PlayerAction);
            ActionCostRemaining = MaxKi;
            UpdateOccupancy();
            LastFailureReason = string.Empty;
        }

        /// <summary>
        /// Replay a player attack against the nearest living monster through the real damage + EffectResolved
        /// path (so the impact-sync buffer, the four presentation subscribers, and the hit/death reaction all
        /// fire exactly as in a live attack). Callers should run <see cref="DebugRestoreCombatantsForTuning"/>
        /// first so the target is full health and every tap is identical; pass <paramref name="lethal"/> to
        /// force a kill and exercise the death timing. Returns false when there is no monster to hit.
        /// </summary>
        public bool DebugReplayPlayerAttack(bool lethal, out HexCoord targetCoord, out string targetId)
            => DebugReplayPlayerAttack("A01", lethal, out targetCoord, out targetId);

        /// <summary>
        /// Replay a specific player attack card's hit on the nearest monster through the real RaiseEffect path,
        /// so the VFX cue, hit SFX and floating number match that card and the per-attack timing resolves by its
        /// id. The damage amount is synthetic (a fraction of the target's HP, or lethal) ??this is a presentation
        /// replay for tuning feel, not a full re-evaluation of the card's effect (area/multi-hit/knockback are not
        /// reproduced). <paramref name="attackCardId"/> falls back to "A01" (basic attack) when blank.
        /// </summary>
        public bool DebugReplayPlayerAttack(string attackCardId, bool lethal, out HexCoord targetCoord, out string targetId)
        {
            targetCoord = default;
            targetId = string.Empty;
            var cardId = string.IsNullOrWhiteSpace(attackCardId) ? "A01" : attackCardId;

            var monster = monsters
                .Where(m => !m.Combatant.IsDead)
                .OrderBy(m => PlayerCoord.DistanceTo(m.Coord))
                .ThenBy(m => m.Coord.Q)
                .ThenBy(m => m.Coord.R)
                .ThenBy(m => m.Id)
                .FirstOrDefault();
            if (monster == null)
            {
                LastFailureReason = "No living monster available to replay an attack against.";
                return false;
            }

            var hp = monster.Combatant.Hp;
            var requested = lethal
                ? hp + 9999
                : Math.Min(Math.Max(1, hp / 3), Math.Max(1, hp - 1));
            var applied = DamageMonster(monster, requested);
            if (monster.Combatant.IsDead)
            {
                ResetDeadMonsterToPatrolIntent(monster);
                UpdateOccupancy();
            }

            targetCoord = monster.Coord;
            targetId = monster.Id;

            // Attack card ids (A01..A11) classify as player attack sources (CombatEffectSourceClassifier), so the
            // shake/SFX branch and floating damage number match a real attack; the cue + per-attack timing key off
            // this card id.
            RaiseEffect(
                EffectKind.Damage,
                monster.Coord,
                0,
                applied,
                monster.Id,
                cardId,
                sourceUnitId: PlayerUnitId,
                sourceActorKind: "player",
                targetActorKind: "monster",
                sourceCardId: cardId,
                presentationGroupId: CreatePresentationGroupId(PlayerUnitId, cardId));
            LastFailureReason = string.Empty;
            return true;
        }

        /// <summary>
        /// Replay the nearest living monster's attack against the player through the real damage + EffectResolved
        /// path, mirroring <see cref="DebugReplayPlayerAttack"/> for the opposite direction. Uses the monster's
        /// real <c>CurrentAttackPattern</c> so the source ref, VFX cue and hit SFX match a live monster attack.
        /// Outputs what the presentation layer needs (attacker, animation trigger, whether the hit was lethal).
        /// Returns false when no monster with an attack pattern is available.
        /// </summary>
        public bool DebugReplayMonsterAttack(
            bool lethal,
            out HexCoord attackerCoord,
            out string attackerId,
            out string animationTrigger,
            out bool playerDied,
            out string patternId)
        {
            attackerCoord = default;
            attackerId = string.Empty;
            animationTrigger = string.Empty;
            playerDied = false;
            patternId = string.Empty;

            var monster = monsters
                .Where(m => !m.Combatant.IsDead && m.AttackPatterns != null && m.AttackPatterns.Length > 0)
                .OrderBy(m => PlayerCoord.DistanceTo(m.Coord))
                .ThenBy(m => m.Coord.Q)
                .ThenBy(m => m.Coord.R)
                .ThenBy(m => m.Id)
                .FirstOrDefault();
            if (monster == null)
            {
                LastFailureReason = "No living monster with an attack pattern to replay.";
                return false;
            }

            var pattern = monster.CurrentAttackPattern;
            var hp = Player.Hp;
            var requested = lethal
                ? hp + 9999
                : Math.Min(Math.Max(1, hp / 5), Math.Max(1, hp - 1));
            var applied = Player.ApplyDamage(requested);
            playerDied = Player.IsDead;

            attackerCoord = monster.Coord;
            attackerId = monster.Id;
            animationTrigger = pattern.AnimationTrigger;
            patternId = pattern.Id;

            var presentationGroupId = CreatePresentationGroupId(monster.Id, pattern.Id);
            RaiseEffect(
                EffectKind.Damage,
                PlayerCoord,
                pattern.AreaRadius,
                applied,
                PlayerUnitId,
                ToMonsterPatternSourceRef(pattern),
                sourceUnitId: monster.Id,
                sourceActorKind: "monster",
                targetActorKind: "player",
                sourcePatternId: pattern.Id,
                hitIndex: 0,
                hitCount: 1,
                presentationGroupId: presentationGroupId);
            LastFailureReason = string.Empty;
            return true;
        }

        /// <summary>
        /// 지금 보스의 도약 상태 한 줄(§18 · 랩 표시용). 도약은 조건이 여섯 갈래라 화면만 보고는
        /// "왜 안 뛰는가"를 알 수 없다 — 이 줄이 그 물음에 값으로 답한다.
        /// </summary>
        public string DebugDescribeBossLeapState()
        {
            var boss = monsters.FirstOrDefault(monster => !monster.Combatant.IsDead && IsBossMonster(monster));
            if (boss == null)
            {
                return "보스 없음";
            }

            return boss.PlannedLeap
                ? $"이번 턴 도약 예정 → {boss.TurnPlan.PlannedMoveCoord}"
                : planner.DescribeLeapEligibility(boss);
        }

        /// <summary>
        /// 도약이 실제로 발동하도록 플레이어를 옮겨 준다(§18 · 랩 전용).
        ///
        /// 도약의 트리거는 "버튼"이 아니라 <b>위치 관계</b>다: 보스가 지금 자리에서 플레이어를 못 덮고,
        /// 도약 사거리 안에 원판이 통째로 들어가면서 거기서는 덮는 칸이 있을 때 뛴다. 그래서 이 헬퍼는
        /// 기믹을 새로 만들지 않고 <b>그 조건을 만족하는 칸을 찾아 세워 줄</b> 뿐이다 — 규칙은 그대로다.
        ///
        /// 후보를 실제로 대입해 계획을 돌려 보고 판정한다(술어를 복제하면 랩과 실게임이 갈라진다).
        /// 함정은 일부러 발동시키지 않는다 — 배치용 이동이 둔화를 걸어 버리면 시험 자체가 오염된다.
        /// </summary>
        public bool DebugTryArrangeBossLeap(out string report)
        {
            var boss = monsters.FirstOrDefault(monster => !monster.Combatant.IsDead && IsBossMonster(monster));
            if (boss == null)
            {
                report = "보스가 없다.";
                return false;
            }

            var originalCoord = PlayerCoord;
            // 몸통 가장자리에서 가까운 순으로 훑는다: 가장 가까운 곳에서 도약이 나야 "다가오는 압박"으로 읽힌다.
            var candidates = HexArea.CellsWithin(boss.Coord, GetMonsterFootprintRadius(boss) + 8)
                .Where(coord => coord != originalCoord)
                .Where(IsDebugLeapProbeCell)
                .OrderBy(coord => coord.DistanceTo(boss.Coord))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToList();

            foreach (var candidate in candidates)
            {
                PlayerCoord = candidate;
                UpdateOccupancy();
                RefreshMonsterIntentStep();
                if (!boss.PlannedLeap)
                {
                    continue;
                }

                RefreshPlayerVision();
                report = $"플레이어를 {candidate}로 옮겼다 — 보스가 {boss.TurnPlan.PlannedMoveCoord}로 도약할 계획을 세웠다. "
                    + "'행동 종료'를 눌러 실행할 것.";
                return true;
            }

            PlayerCoord = originalCoord;
            UpdateOccupancy();
            RefreshPlayerVision();
            RefreshMonsterIntentStep();
            report = "도약이 나오는 자리를 찾지 못했다 — " + planner.DescribeLeapEligibility(boss);
            return false;
        }

        /// <summary>도약 시험 배치의 후보 칸: 걸어서 설 수 있고 아무도 점유하지 않은 칸.</summary>
        /// <summary>
        /// 패턴 랩(§25)의 대상이 될 수 있는 몬스터들. 기물(철조각)은 공격 패턴이 자리표시라 제외한다.
        /// </summary>
        public IReadOnlyList<MonsterRuntimeState> DebugListPatternLabMonsters()
        {
            return monsters
                .Where(monster => !monster.Combatant.IsDead
                                  && !MonsterSpawnRoles.IsBossProp(monster.SpawnRole)
                                  && monster.AttackPatterns != null
                                  && monster.AttackPatterns.Length > 0)
                .OrderByDescending(IsBossMonster)
                .ThenBy(monster => PlayerCoord.DistanceTo(monster.Coord))
                .ThenBy(monster => monster.Id, StringComparer.Ordinal)
                .Select(CreateMonsterSnapshot)
                .ToList();
        }

        /// <summary>이 몬스터의 패턴마다 "지금 왜 후보인가/아닌가" 한 줄씩(§25 · 랩 표시용).</summary>
        public IReadOnlyList<string> DebugDescribeAttackPatternGates(string monsterId)
        {
            var monster = FindDebugPatternLabMonster(monsterId);
            return monster == null
                ? new[] { "몬스터를 찾지 못했다." }
                : planner.DescribeAttackPatternGates(monster);
        }

        /// <summary>
        /// 이 패턴을 이번 턴 공격으로 <b>강제 커밋</b>한다(§25 · 랩 전용). 계획을 다시 세워 예고 오버레이가
        /// 곧바로 그 패턴을 그린다 — 표시 코드를 새로 만들지 않는다.
        ///
        /// <para>⚠️ 선택기를 <b>우회</b>하므로 "실게임에서 이 패턴이 뽑히는 상황이 나오는가"에는 답하지
        /// 못한다. 그 물음은 <see cref="DebugTryArrangeAttackPatternCondition"/>이 답한다.
        /// 커밋 이후의 겨냥 칸·피해·상태이상은 전부 실제 경로를 탄다.</para>
        /// </summary>
        public bool DebugForceAttackPattern(string monsterId, int patternIndex, out string report)
        {
            var monster = FindDebugPatternLabMonster(monsterId);
            if (monster == null)
            {
                report = "몬스터를 찾지 못했다.";
                return false;
            }

            if (patternIndex < 0 || patternIndex >= monster.AttackPatterns.Length)
            {
                report = "패턴 인덱스가 범위 밖이다.";
                return false;
            }

            monster.ForcedAttackPatternIndex = patternIndex;
            RefreshMonsterIntentStep();

            var pattern = monster.AttackPatterns[patternIndex];
            report = $"{monster.Id}: {pattern.Id} {pattern.DisplayName} 강제 커밋 — 예고를 확인한 뒤 '행동 종료'로 집행할 것."
                     + " (선택기를 우회한 상태다 — 해제할 때까지 유지된다.)";
            if (pattern.LeapRange > 0)
            {
                // §28 후속 T3: 강제 커밋은 <b>공격 선택만</b> 우회한다 — 도약은 이동 계획의 별도 축
                // (PlannedLeap)이라 위치 관계(걸어서 못 덮음 + 착지에서 덮음 + 쿨다운)로만 발동한다.
                // 이 안내가 없으면 "커밋했는데 왜 안 뛰나"를 저작 결함으로 오인한다.
                report += " ⚠️ 도약은 위치 관계로만 발동한다 — 커밋으로는 뛰지 않는다. 「도약 조건 만들기」를 쓸 것.";
            }

            return true;
        }

        /// <summary>강제 커밋 해제. 다음 계획부터 평소 선택기가 다시 고른다.</summary>
        public bool DebugClearForcedAttackPattern(string monsterId, out string report)
        {
            var monster = FindDebugPatternLabMonster(monsterId);
            if (monster == null)
            {
                report = "몬스터를 찾지 못했다.";
                return false;
            }

            monster.ForcedAttackPatternIndex = -1;
            RefreshMonsterIntentStep();
            report = $"{monster.Id}: 강제 커밋 해제 — 선택기가 다시 고른다.";
            return true;
        }

        /// <summary>
        /// 이 패턴이 <b>추첨 후보가 되는</b> 자리로 플레이어를 옮긴다(§25 · 랩 전용).
        /// 「도약 조건 만들기」(§18.4)와 같은 방식이다 — 술어를 복제하지 않고 후보 칸을 <b>실제로 대입해</b>
        /// 계획을 돌려 본 뒤 판정한다.
        ///
        /// <para>🔑 "뽑혔는가"가 아니라 <b>"후보인가"</b>로 묻는다 — 선택은 가중치 추첨이라 조건이 맞아도
        /// 그 턴에 다른 패턴이 뽑힐 수 있고, 그걸 실패로 읽으면 랩이 거짓말을 한다.</para>
        ///
        /// <para>⚠️ 함정은 일부러 발동시키지 않는다(배치용 이동이 둔화를 걸면 시험이 오염된다 — §18.4).</para>
        /// </summary>
        public bool DebugTryArrangeAttackPatternCondition(string monsterId, int patternIndex, out string report)
        {
            var monster = FindDebugPatternLabMonster(monsterId);
            if (monster == null)
            {
                report = "몬스터를 찾지 못했다.";
                return false;
            }

            if (patternIndex < 0 || patternIndex >= monster.AttackPatterns.Length)
            {
                report = "패턴 인덱스가 범위 밖이다.";
                return false;
            }

            var originalCoord = PlayerCoord;
            var pattern = monster.AttackPatterns[patternIndex];
            var searchRadius = GetMonsterFootprintRadius(monster) + 8;
            var candidates = HexArea.CellsWithin(monster.Coord, searchRadius)
                .Where(coord => coord != originalCoord)
                .Where(IsDebugLeapProbeCell)
                .OrderBy(coord => coord.DistanceTo(monster.Coord))
                .ThenBy(coord => coord.Q)
                .ThenBy(coord => coord.R)
                .ToList();

            foreach (var candidate in candidates)
            {
                PlayerCoord = candidate;
                UpdateOccupancy();
                RefreshMonsterIntentStep();
                if (!planner.IsAttackPatternCandidateNow(monster, patternIndex, out _))
                {
                    continue;
                }

                RefreshPlayerVision();
                report = $"플레이어를 {candidate}로 옮겼다 — {pattern.Id} {pattern.DisplayName}이(가) 추첨 후보가 됐다."
                         + " 뽑히지 않으면 턴을 넘기거나 강제 커밋을 쓸 것.";
                return true;
            }

            PlayerCoord = originalCoord;
            UpdateOccupancy();
            RefreshPlayerVision();
            RefreshMonsterIntentStep();
            planner.IsAttackPatternCandidateNow(monster, patternIndex, out var reason);
            report = $"{pattern.Id}이(가) 후보가 되는 자리를 찾지 못했다 — {reason}";
            return false;
        }

        /// <summary>패턴 형상 미리보기 스냅숏(§25 · 랩 표시용). 계산은 전부 플래너가 했다 — 랩은 그리기만 한다.</summary>
        public readonly struct DebugAttackShapePreview
        {
            public DebugAttackShapePreview(
                string shapeId,
                HexCoord origin,
                HexDirection attackDirection,
                int footprintRadius,
                IReadOnlyList<HexCoord> affectedCells,
                HexCoord playerCoord)
            {
                ShapeId = shapeId;
                Origin = origin;
                AttackDirection = attackDirection;
                FootprintRadius = footprintRadius;
                AffectedCells = affectedCells;
                PlayerCoord = playerCoord;
            }

            public string ShapeId { get; }
            /// <summary>형상의 기준 좌표 = 이번 턴 계획된 이동 지점(§15.4). 몸통 원판의 중심이기도 하다.</summary>
            public HexCoord Origin { get; }
            public HexDirection AttackDirection { get; }
            public int FootprintRadius { get; }
            /// <summary><see cref="AttackShapeLibrary.GetAffectedCells"/>가 돌려준 절대 좌표들.</summary>
            public IReadOnlyList<HexCoord> AffectedCells { get; }
            public HexCoord PlayerCoord { get; }
        }

        /// <summary>
        /// 패턴 형상 미리보기(§25 · 랩 표시용). 실제 조준 방향(계획 지점→플레이어)으로
        /// <see cref="AttackShapeLibrary.GetAffectedCells"/>를 그대로 편 결과다 — 형상 계산을 랩에
        /// 복제하지 않는다(§18.4). shape 없는 패턴이나 몬스터 부재 시 false.
        /// </summary>
        public bool DebugTryGetAttackPatternShapePreview(string monsterId, int patternIndex, out DebugAttackShapePreview preview)
        {
            var monster = FindDebugPatternLabMonster(monsterId);
            if (monster == null
                || !planner.TryDescribeAttackPatternShape(
                    monster, patternIndex,
                    out var shapeId, out var origin, out var direction, out var footprint, out var cells))
            {
                preview = default;
                return false;
            }

            preview = new DebugAttackShapePreview(shapeId, origin, direction, footprint, cells, PlayerCoord);
            return true;
        }

        private MonsterRuntime FindDebugPatternLabMonster(string monsterId)
        {
            if (string.IsNullOrEmpty(monsterId))
            {
                return monsters.FirstOrDefault(monster => !monster.Combatant.IsDead && IsBossMonster(monster))
                       ?? monsters.FirstOrDefault(monster => !monster.Combatant.IsDead);
            }

            return monsters.FirstOrDefault(monster =>
                !monster.Combatant.IsDead && string.Equals(monster.Id, monsterId, StringComparison.Ordinal));
        }

        private bool IsDebugLeapProbeCell(HexCoord coord)
        {
            return Map.TryGetCell(coord, out var cell)
                && cell.BaseWalkable
                && terrainTraits.IsWalkable(cell.TerrainTypeId)
                && !Map.HasMovementBlockingObject(coord)
                && !monsters.Any(monster => !monster.Combatant.IsDead && IsMonsterOccupying(monster, coord));
        }

        /// <summary>
        /// 서비스 디버그(#20): 소비 원장(ClaimedEventObjectIds)에서 오브젝트 하나를 되돌린다 —
        /// 잡화점/캠핑카/공작소 재방문 테스트가 매 판 새 답사를 요구하지 않게 한다.
        /// </summary>
        public bool DebugUnclaimEventObject(string objectId)
        {
            return !string.IsNullOrWhiteSpace(objectId) && claimedEventObjectIds.Remove(objectId.Trim());
        }

        public bool TryDebugMovePlayer(HexCoord destination)
        {
            LastFailureReason = string.Empty;
            if (IsTerminal)
            {
                return Fail("Combat ended. Restart to test again.");
            }

            if (!Map.TryGetCell(destination, out var cell))
            {
                return Fail("Debug move failed: selected hex is not part of the map.");
            }

            if (!cell.BaseWalkable || !terrainTraits.IsWalkable(cell.TerrainTypeId))
            {
                return Fail("Debug move failed: selected hex is not walkable.");
            }

            if (Map.HasMovementBlockingObject(destination))
            {
                return Fail("Debug move failed: selected hex is blocked by an object.");
            }

            if (monsters.Any(monster => !monster.Combatant.IsDead && IsMonsterOccupying(monster, destination)))
            {
                return Fail("Debug move failed: selected hex is occupied by a monster.");
            }

            var previousCoord = PlayerCoord;
            PlayerCoord = destination;
            lastMovedDistance = previousCoord.DistanceTo(destination);
            ResolveTrapTriggersAt(PlayerCoord);
            if (CheckTerminalOutcomeStep())
            {
                return true;
            }

            RefreshPlayerVision();
            UpdateOccupancy();
            RefreshMonsterIntentStep();
            return true;
        }
    }
}
