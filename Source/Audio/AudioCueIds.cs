namespace SeoulPlayup.Combat.Unity
{
    public static class AudioCueIds
    {
        public const string UiCardHover = "ui.card.hover";
        public const string UiCardSelect = "ui.card.select";
        public const string UiCardInvalid = "ui.card.invalid";
        public const string UiKiInsufficient = "ui.ki.insufficient";
        public const string UiPhaseChange = "ui.phase.change";
        public const string UiButtonClick = "ui.button.click";
        public const string RewardCardHover = "ui.reward.hover";

        public const string CardMoveResolve = "card.move.resolve";
        public const string CardAttackResolve = "card.attack.resolve";
        public const string CardDefendResolve = "card.defend.resolve";
        public const string CardScoutResolve = "card.scout.resolve";

        public const string FogTileRevealed = "fog.tile.revealed";
        public const string CombatPlayerHit = "combat.player.hit";
        public const string CombatPlayerDeath = "combat.player.death";
        public const string CombatMonsterHit = "combat.monster.hit";
        public const string PlayerVoiceAttack = "player.voice.attack";
        public const string PlayerVoiceHit = "player.voice.hit";
        public const string PlayerVoiceDotHit = "player.voice.dot_hit";
        public const string PlayerVoiceDeath = "player.voice.death";

        public const string ObjectiveMemoryGyeolRevealed = "objective.memory_gyeol.revealed";
        public const string ObjectiveInvestigateSuccess = "objective.investigate.success";

        public const string GameDefeat = "game.defeat";
        public const string GameVictory = "game.victory";

        // Clear/victory BGM started the instant the memory-stone victory sequence begins (distinct from the
        // GameVictory stinger above, which fires when the victory result overlay pops up).
        public const string MusicVictory = "music.victory";

        // Looping background music cues. Gameplay music is auto-started by CombatAudioPresenter; the lobby
        // loop is played by LobbyController, which resolves the clip from the same SoundCatalog.
        public const string MusicLobby = "music.lobby";
        public const string MusicGameplay = "music.gameplay";

        // Boss BGM. The per-phase cue ids are data-driven from the boss profile's bgmCueBase
        // (e.g. music.boss.bulgasal.p1/.p2/.p3 — see BossProfileDefinition.GetBgmCueId), so they are not
        // enumerated here; CombatAudioPresenter treats any cue under this prefix as a looping background
        // cue so a phase change crossfades between them like the other loops. The transition stinger is a
        // SEPARATE one-shot that MUST sit on the Sfx bus — a one-shot on the Music bus cuts the loop it
        // shares the source with (see CombatAudioPresenter.Play). Clips are authored in SoundCatalog.asset.
        public const string MusicBossPrefix = "music.boss.";
        public const string BossPhaseTransitionStinger = "boss.phase.transition";

        // Boss iron-scrap moments (2026-09-05 결정 3). Dedicated cue ids so the sound order can land without
        // touching code; until clips arrive the SoundCatalog entries point at the previously borrowed clips
        // (intent warning / trap trigger / rupture / push) so nothing goes silent in the meantime.
        public const string BossPropVolleyCast = "boss.prop.volley.cast";
        public const string BossPropPlaced = "boss.prop.placed";
        public const string BossPropAbsorbBlast = "boss.prop.absorb.blast";
        public const string BossPropAbsorbPull = "boss.prop.absorb.pull";
        public const string BossTrapPlaced = "boss.trap.placed";
        public const string BossScrapChainHit = "boss.scrap-chain.hit";

        public const string UiCardDraw = "ui.card.draw";
        public const string UiCardDiscard = "ui.card.discard";
        public const string MovementTerrainStreet = "movement.terrain.street";
        public const string MovementTerrainPark = "movement.terrain.park";
        public const string MonsterIntentWarning = "monster.intent.warning";
        public const string MonsterMoveResolve = "monster.move.resolve";
        public const string EnemyTurnBegin = "phase.monster.begin";
        public const string AmbienceSeoulBase = "ambience.seoul.base";
        public const string AmbienceYokaiPressure = "ambience.yokai.pressure";

        // P1.5 ??previously-silent presentation moments now wired to cues.
        // Effect-driven cues are emitted automatically via CombatAudioPresenter.MapEffectToCueIds
        // (off the CombatState.EffectResolved stream); the rest are raised at explicit gameplay sites.
        public const string CombatEnemyDeath = "combat.enemy.death";
        public const string EffectHeal = "effect.heal";
        public const string EffectPoison = "effect.poison";
        public const string EffectStun = "effect.stun";
        public const string EffectSlow = "effect.slow";
        public const string EffectRupture = "effect.rupture";
        // 실명(Blind). 옛 effect.vision_down의 자리를 대신한다: VisionDown은 StatusEffectKind가 없어
        // 아무 코드도 발화할 수 없는 유령 큐였고 2026-07-26에 제거됐다. 이제 함정의 VisionDown이
        // StatusEffectKind.Blind로 해소되므로(C-6) 실제로 도달 가능한 큐가 됐다. 여전히 없는 것은
        // effect.burn — Burn은 D-1로 부결됐고 대응 상태이상이 영영 생기지 않는다.
        // ⏳ SoundCatalog 엔트리는 아직 없다(전용 클립 미저작) — 다른 상태이상 클립을 조용히 빌려 쓰면
        // 감사에 안 잡히는 오배선이 되므로 일부러 비워 둔다. `sound-audit`의
        // raisedCuesMissingFromCatalog가 이 항목을 이름으로 계속 보고하는 것이 추적 수단이다.
        public const string EffectBlind = "effect.blind";

        // 무장 해제(Disarm, C-5). ⏳ 실명과 같은 이유로 SoundCatalog 엔트리는 비워 뒀다 —
        // 전용 클립 미저작이며, 기절 클립을 빌려 쓰면 감사에 안 잡히는 오배선이 된다.
        // `sound-audit`의 raisedCuesMissingFromCatalog가 계속 보고한다.
        // 은신 재진입(어둑시니 — trait.stealth.hidden 전이·2026-09-04 피드백). 클립은 2026-09 발주 A의
        // M013_버프형(그늘 속으로·자기 은폐) — 대상 패턴 A046이 삭제돼 패턴 큐 대신 이 전이 큐에 물렸다
        // (2026-09-05 사용자 결정). 취소된 특성 알림 8종과는 다른 자리다(그쪽은 텍스트만).
        public const string MonsterStealthHide = "monster.stealth.hide";

        public const string EffectDisarm = "effect.disarm";

        // 상태이상 부여 SFX 10종(2026-09 발주 B 반입, cs:1374). 정본은 status_effects.csv의 applyAudioCueId이고
        // 여기 상수는 그 리터럴이 실재하는 큐인지 테스트가 대조하는 표다(StatusEffectInfo.FallbackApplyAudioCueId와
        // 같은 값이어야 한다 — CsvPresentationColumnsMatchTheHardcodedFallbacks).
        public const string EffectWeaken = "effect.weaken";
        public const string EffectVulnerable = "effect.vulnerable";
        public const string EffectSeal = "effect.seal";
        public const string EffectTorchLight = "effect.torchlight";
        public const string EffectBossAura = "effect.bossaura";
        public const string EffectMight = "effect.might";
        public const string EffectUnknown = "effect.unknown";
        public const string EffectGuard = "effect.guard";
        public const string EffectStealth = "effect.stealth";
        public const string EffectInvincible = "effect.invincible";
        public const string EffectImmobilize = "effect.immobilize";
        public const string EffectPush = "effect.push";
        public const string EffectTrapTrigger = "effect.trap.trigger";
        public const string EffectKnockbackImpact = "effect.knockback.impact";
        public const string FieldDamageEffect = "field.damage.effect";
        public const string FieldFlashbangEffect = "field.flashbang.effect";
        public const string CardBuffResolve = "card.buff.resolve";

        // Card "cast" cues: played the instant a card is committed (by card kind), distinct from the
        // effect-synced "resolve" cues above which fire with the VFX/impact. See CombatAudioPresenter.
        public const string CardMoveCast = "card.move.cast";
        public const string CardAttackCast = "card.attack.cast";
        public const string CardDefendCast = "card.defend.cast";
        public const string CardBuffCast = "card.buff.cast";
        public const string CardUtilityCast = "card.utility.cast";
        public const string CardFieldCast = "card.field.cast";

        public const string RewardPopupAppear = "reward.popup.appear";
        public const string RewardCardAcquire = "reward.card.acquire";
        public const string RewardChestOpen = "reward.chest.open";

        // 2026-09 발주 A·B 신규 발화 지점(클립 cs:1374 반입). 큐 id는 발주 체크리스트
        // docs/resource-tracker/sound_order_batch_ab_2026-09.csv의 「제안 큐 id」와 같다.
        // 취소 10건(특성 알림 8·엽전 손실·결계 봉인)은 사용자 결정으로 큐를 만들지 않았다.
        public const string ItemFlaskUse = "item.flask.use";
        public const string ItemBeadUse = "item.bead.use";
        public const string CombatAmbush = "combat.ambush";
        public const string EffectExpire = "effect.expire";
        public const string EffectNegated = "effect.negated";
        public const string TrapSpawn = "trap.spawn";
        public const string CurseInjected = "curse.injected";
        public const string BossWeakSpotHit = "boss.weakspot.hit";
        public const string ShopBuy = "shop.buy";
        public const string ShopDenied = "shop.denied";
        public const string ShopCardRemove = "shop.card_remove";
        public const string ServiceRest = "service.rest";
        public const string ServiceRefine = "service.refine";
        public const string MoneyGain = "money.gain";
        public const string RewardRelicAcquire = "reward.relic.acquire";
        public const string RewardReroll = "reward.reroll";
        public const string RewardLootAppear = "reward.loot.appear";
        public const string RewardSkip = "reward.skip";

        public static string MonsterAttack(string monsterDefinitionId) => MonsterCue(monsterDefinitionId, "attack", MonsterIntentWarning);
        public static string MonsterHit(string monsterDefinitionId) => MonsterCue(monsterDefinitionId, "hit", CombatMonsterHit);
        public static string MonsterDeath(string monsterDefinitionId) => MonsterCue(monsterDefinitionId, "death", CombatEnemyDeath);

        private static string MonsterCue(string monsterDefinitionId, string action, string fallback)
        {
            return string.IsNullOrWhiteSpace(monsterDefinitionId)
                ? fallback
                : $"monster.{monsterDefinitionId}.{action}";
        }
    }
}
