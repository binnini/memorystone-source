using System;
using System.Collections.Generic;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.CardCore;

namespace SeoulPlayup.Combat.Runtime
{
    // Save DTO contract: JsonUtility persistence serializes public fields only, so every member in
    // this family is a public field (never an auto-property) and Nullable<T> is not allowed.
    [Serializable]
    public sealed class PlayerRunSaveData
    {
        public PlayerVitalsSaveData Vitals = new PlayerVitalsSaveData();
        public PlayerResourcesSaveData Resources = new PlayerResourcesSaveData();
        public PlayerPositionSaveData Position = new PlayerPositionSaveData();
        public PlayerDeckSaveData Decks = new PlayerDeckSaveData();
        public PlayerKnowledgeSaveData Knowledge = new PlayerKnowledgeSaveData();
        public PlayerInventorySaveData Inventory = new PlayerInventorySaveData();
        public PlayerObjectiveSaveData Objective = new PlayerObjectiveSaveData();
        public PlayerFeedbackSaveData Feedback = new PlayerFeedbackSaveData();

        public static PlayerRunSaveData FromPlayerState(PlayerState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            return new PlayerRunSaveData
            {
                Vitals = PlayerVitalsSaveData.FromState(state.Vitals),
                Resources = PlayerResourcesSaveData.FromState(state.Resources),
                Position = PlayerPositionSaveData.FromState(state.Position),
                Decks = PlayerDeckSaveData.FromState(state.Decks),
                Knowledge = PlayerKnowledgeSaveData.FromState(state.Knowledge),
                Inventory = PlayerInventorySaveData.FromState(state.Inventory),
                Objective = PlayerObjectiveSaveData.FromState(state.Objective),
                Feedback = PlayerFeedbackSaveData.FromState(state.Feedback)
            };
        }

        public static PlayerRunSaveData FromCombatState(CombatState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            var data = FromPlayerState(state.CreatePlayerStateProjection());
            data.Decks = PlayerDeckSaveData.FromCombatState(state);
            return data;
        }

        public PlayerState ToPlayerState()
        {
            return new PlayerState(
                (Vitals ?? new PlayerVitalsSaveData()).ToState(),
                (Resources ?? new PlayerResourcesSaveData()).ToState(),
                (Position ?? new PlayerPositionSaveData()).ToState(),
                (Decks ?? new PlayerDeckSaveData()).ToState(),
                (Knowledge ?? new PlayerKnowledgeSaveData()).ToState(),
                (Objective ?? new PlayerObjectiveSaveData()).ToState(),
                (Inventory ?? new PlayerInventorySaveData()).ToState(),
                (Feedback ?? new PlayerFeedbackSaveData()).ToState());
        }
    }

    [Serializable]
    public sealed class PlayerVitalsSaveData
    {
        public int Hp;
        public int MaxHp;
        public int Block;

        public static PlayerVitalsSaveData FromState(PlayerVitals state)
        {
            return new PlayerVitalsSaveData
            {
                Hp = state?.Hp ?? 0,
                MaxHp = state?.MaxHp ?? 0,
                Block = state?.Block ?? 0
            };
        }

        public PlayerVitals ToState() => new PlayerVitals(Hp, MaxHp, Block);
    }

    [Serializable]
    public sealed class PlayerResourcesSaveData
    {
        public int CurrentKi;
        public int MaxKi;

        public static PlayerResourcesSaveData FromState(PlayerResources state)
        {
            return new PlayerResourcesSaveData
            {
                CurrentKi = state?.CurrentKi ?? 0,
                MaxKi = state?.MaxKi ?? 0
            };
        }

        public PlayerResources ToState() => new PlayerResources(CurrentKi, MaxKi);
    }

    [Serializable]
    public sealed class PlayerPositionSaveData
    {
        public int Q;
        public int R;
        public CombatPhase Phase;

        public static PlayerPositionSaveData FromState(PlayerPositionState state)
        {
            var coord = state?.Coord ?? default;
            return new PlayerPositionSaveData
            {
                Q = coord.Q,
                R = coord.R,
                Phase = state?.Phase ?? CombatPhase.PlayerMovement
            };
        }

        public PlayerPositionState ToState() => new PlayerPositionState(new HexCoord(Q, R), Phase);
    }

    [Serializable]
    public sealed class PlayerDeckSaveData
    {
        public PlayerDeckPileCountsSaveData MoveDeck = new PlayerDeckPileCountsSaveData();
        public PlayerDeckPileCountsSaveData ActionDeck = new PlayerDeckPileCountsSaveData();
        public List<PlayerCardInstanceSaveData> MoveCards = new List<PlayerCardInstanceSaveData>();
        public List<PlayerCardInstanceSaveData> ActionCards = new List<PlayerCardInstanceSaveData>();
        public string CardCatalogSourceId = string.Empty;
        public PlayerDeckZoneSaveData MovementZones = new PlayerDeckZoneSaveData();
        public PlayerDeckZoneSaveData ActionZones = new PlayerDeckZoneSaveData();

        public static PlayerDeckSaveData FromState(PlayerDecksRuntimeState state)
        {
            return new PlayerDeckSaveData
            {
                MoveDeck = PlayerDeckPileCountsSaveData.FromSummary(state?.MoveDeck ?? default),
                ActionDeck = PlayerDeckPileCountsSaveData.FromSummary(state?.ActionDeck ?? default)
            };
        }

        public PlayerDecksRuntimeState ToState()
        {
            return new PlayerDecksRuntimeState(
                (MoveDeck ?? new PlayerDeckPileCountsSaveData()).ToSummary(),
                (ActionDeck ?? new PlayerDeckPileCountsSaveData()).ToSummary());
        }

        public static PlayerDeckSaveData FromPlayerDeckData(PlayerDeckData deck)
        {
            var data = new PlayerDeckSaveData();
            if (deck == null)
            {
                return data;
            }

            foreach (var card in deck.MovementCards)
            {
                data.MoveCards.Add(PlayerCardInstanceSaveData.FromState(card));
            }

            foreach (var card in deck.ActionCards)
            {
                data.ActionCards.Add(PlayerCardInstanceSaveData.FromState(card));
            }

            return data;
        }

        public static PlayerDeckSaveData FromCombatState(CombatState state)
        {
            var data = FromPlayerDeckData(state?.PlayerDeck);
            if (state == null)
            {
                return data;
            }

            data.CardCatalogSourceId = state.CardCatalog?.SourceId ?? string.Empty;
            data.MoveDeck = PlayerDeckPileCountsSaveData.FromCardDeckState(state.MovementDeck);
            data.ActionDeck = PlayerDeckPileCountsSaveData.FromCardDeckState(state.ActionDeck);
            data.MovementZones = PlayerDeckZoneSaveData.FromDeckState(state.MovementDeck);
            data.ActionZones = PlayerDeckZoneSaveData.FromDeckState(state.ActionDeck);
            return data;
        }

        public PlayerDeckData ToPlayerDeckData()
        {
            var movement = new List<PlayerCardInstanceData>();
            foreach (var card in MoveCards ?? new List<PlayerCardInstanceSaveData>())
            {
                movement.Add(card.ToState());
            }

            var action = new List<PlayerCardInstanceData>();
            foreach (var card in ActionCards ?? new List<PlayerCardInstanceSaveData>())
            {
                action.Add(card.ToState());
            }

            return new PlayerDeckData(movement, action);
        }

        /// <param name="shuffle">복원된 덱이 이후 재셔플에 쓸 셔플. null = 무시드(구세이브 폴백).
        /// 저장된 더미 순서는 셔플하지 않고 그대로 되살린다 — 세이브 포맷·복원 순서는 이 트랙이
        /// 건드리지 않는다(seed-determinism-handoff §6).</param>
        public CardDeckState CreateMovementDeck(CardCatalogDefinition catalog, Action<IList<CardDefinition>> shuffle = null)
        {
            return CreateDeck(catalog, CardCategory.Movement, MovementZones, MoveCards, shuffle);
        }

        public CardDeckState CreateActionDeck(CardCatalogDefinition catalog, Action<IList<CardDefinition>> shuffle = null)
        {
            return CreateDeck(catalog, CardCategory.Action, ActionZones, ActionCards, shuffle);
        }

        private static CardDeckState CreateDeck(
            CardCatalogDefinition catalog,
            CardCategory category,
            PlayerDeckZoneSaveData zones,
            List<PlayerCardInstanceSaveData> fallbackCards,
            Action<IList<CardDefinition>> shuffle)
        {
            if (zones != null && zones.HasAnyCards)
            {
                return new CardDeckState(
                    ResolveCards(catalog, category, zones.DrawPile),
                    ResolveCards(catalog, category, zones.Hand),
                    ResolveCards(catalog, category, zones.DiscardPile),
                    ResolveCards(catalog, category, zones.RemovedPile),
                    shuffle);
            }

            var owned = fallbackCards ?? new List<PlayerCardInstanceSaveData>();
            return new CardDeckState(ResolveCards(catalog, category, owned), shuffle);
        }

        private static IEnumerable<CardDefinition> ResolveCards(
            CardCatalogDefinition catalog,
            CardCategory category,
            IEnumerable<PlayerCardInstanceSaveData> savedCards)
        {
            foreach (var saved in savedCards ?? new List<PlayerCardInstanceSaveData>())
            {
                // 연마(P4): 복원된 인스턴스의 UpgradeLevel은 카드 클래스 Upgrade로 정의에 반영된다.
                var resolved = SeoulPlayup.Combat.Runtime.Cards.CardUpgrades.Resolve(
                    PlayerDeckData.ResolveCard(catalog, category, saved?.ToState(), requireGameplayDeckEntry: false));
                if (resolved != null)
                {
                    yield return resolved;
                }
            }
        }
    }

    [Serializable]
    public sealed class PlayerCardInstanceSaveData
    {
        public string InstanceId = string.Empty;
        public string CardId = string.Empty;
        public int UpgradeLevel;
        public bool IsTemporary;

        public static PlayerCardInstanceSaveData FromState(PlayerCardInstanceData state)
        {
            return new PlayerCardInstanceSaveData
            {
                InstanceId = state?.InstanceId ?? string.Empty,
                CardId = state?.CardId ?? string.Empty,
                UpgradeLevel = state?.UpgradeLevel ?? 0,
                IsTemporary = state?.IsTemporary ?? false
            };
        }

        public PlayerCardInstanceData ToState()
        {
            return new PlayerCardInstanceData(InstanceId, CardId, UpgradeLevel, IsTemporary);
        }
    }

    [Serializable]
    public sealed class PlayerDeckPileCountsSaveData
    {
        public int DrawCount;
        public int HandCount;
        public int DiscardCount;
        public int RemovedCount;

        public static PlayerDeckPileCountsSaveData FromSummary(PlayerDeckRuntimeSummary summary)
        {
            return new PlayerDeckPileCountsSaveData
            {
                DrawCount = summary.DrawCount,
                HandCount = summary.HandCount,
                DiscardCount = summary.DiscardCount
            };
        }

        public PlayerDeckRuntimeSummary ToSummary() => new PlayerDeckRuntimeSummary(DrawCount, HandCount, DiscardCount);

        public static PlayerDeckPileCountsSaveData FromCardDeckState(CardDeckState state)
        {
            return new PlayerDeckPileCountsSaveData
            {
                DrawCount = state?.DrawCount ?? 0,
                HandCount = state?.HandCount ?? 0,
                DiscardCount = state?.DiscardCount ?? 0,
                RemovedCount = state?.RemovedCount ?? 0
            };
        }
    }

    [Serializable]
    public sealed class PlayerDeckZoneSaveData
    {
        public List<PlayerCardInstanceSaveData> DrawPile = new List<PlayerCardInstanceSaveData>();
        public List<PlayerCardInstanceSaveData> Hand = new List<PlayerCardInstanceSaveData>();
        public List<PlayerCardInstanceSaveData> DiscardPile = new List<PlayerCardInstanceSaveData>();
        public List<PlayerCardInstanceSaveData> RemovedPile = new List<PlayerCardInstanceSaveData>();

        public bool HasAnyCards =>
            (DrawPile?.Count ?? 0) > 0
            || (Hand?.Count ?? 0) > 0
            || (DiscardPile?.Count ?? 0) > 0
            || (RemovedPile?.Count ?? 0) > 0;

        public static PlayerDeckZoneSaveData FromDeckState(CardDeckState state)
        {
            return new PlayerDeckZoneSaveData
            {
                DrawPile = FromCards(state?.DrawPile),
                Hand = FromCards(state?.Hand),
                DiscardPile = FromCards(state?.DiscardPile),
                RemovedPile = FromCards(state?.RemovedPile)
            };
        }

        private static List<PlayerCardInstanceSaveData> FromCards(IEnumerable<CardDefinition> cards)
        {
            var result = new List<PlayerCardInstanceSaveData>();
            foreach (var card in cards ?? Array.Empty<CardDefinition>())
            {
                if (card == null)
                {
                    continue;
                }

                result.Add(new PlayerCardInstanceSaveData
                {
                    InstanceId = card.InstanceId,
                    CardId = card.Id,
                    UpgradeLevel = card.UpgradeLevel,
                    IsTemporary = card.IsTemporary
                });
            }

            return result;
        }
    }

    [Serializable]
    public sealed class PlayerKnowledgeSaveData
    {
        public int UnknownCount;
        public int HintedCount;
        public int RevealedCount;

        public static PlayerKnowledgeSaveData FromState(PlayerKnowledgeState state)
        {
            var visibility = state?.Visibility ?? default;
            return new PlayerKnowledgeSaveData
            {
                UnknownCount = visibility.UnknownCount,
                HintedCount = visibility.HintedCount,
                RevealedCount = visibility.RevealedCount
            };
        }

        public PlayerKnowledgeState ToState() => new PlayerKnowledgeState(new PlayerVisibilitySummary(UnknownCount, HintedCount, RevealedCount));
    }

    [Serializable]
    public sealed class PlayerObjectiveSaveData
    {
        public bool Completed;
        public string ObjectiveId = string.Empty;
        public string StatusText = string.Empty;

        public static PlayerObjectiveSaveData FromState(PlayerObjectiveProgressState state, string objectiveId = "")
        {
            return new PlayerObjectiveSaveData
            {
                Completed = state?.Completed ?? false,
                ObjectiveId = objectiveId ?? string.Empty,
                StatusText = state?.StatusText ?? string.Empty
            };
        }

        public PlayerObjectiveProgressState ToState() => new PlayerObjectiveProgressState(Completed, StatusText);
    }

    [Serializable]
    public sealed class PlayerInventorySaveData
    {
        public List<PlayerPermanentItemSaveData> PermanentItems = new List<PlayerPermanentItemSaveData>();
        public List<PlayerBagItemStackSaveData> BagStacks = new List<PlayerBagItemStackSaveData>();
        // 스테이지 한정 재화지만 저장은 해야 한다 — 이 세이브는 스테이지 인계용이 아니라
        // 같은 전투의 중단→재개용이고, 재개 시 잔액이 0으로 돌아가면 번 돈이 사라진다.
        public int WalletBalance;

        // 힘은 런 전체 유지되므로 반드시 실려야 한다(D-3). 스키마 버전은 올리지 않는다 —
        // PlayerRunSaveEnvelope.IsValid가 버전 불일치를 마이그레이션 없이 거부하므로 올리는 순간
        // 기존 세이브가 전부 무효가 된다. JsonUtility는 없는 필드를 0으로 채우니 구버전은 힘 0으로 열린다.
        public int MightStacks;

        public static PlayerInventorySaveData FromState(PlayerInventoryState state)
        {
            var data = new PlayerInventorySaveData();
            if (state == null)
            {
                return data;
            }

            foreach (var item in state.RelicsAndCurses.Items)
            {
                data.PermanentItems.Add(PlayerPermanentItemSaveData.FromState(item));
            }

            foreach (var stack in state.Bag.Stacks)
            {
                data.BagStacks.Add(PlayerBagItemStackSaveData.FromState(stack));
            }

            data.WalletBalance = state.Wallet?.Balance ?? 0;
            data.MightStacks = state.MightStacks;
            return data;
        }

        public PlayerInventoryState ToState()
        {
            var relicsAndCurses = new PlayerRelicCurseInventory();
            foreach (var item in PermanentItems ?? new List<PlayerPermanentItemSaveData>())
            {
                relicsAndCurses.TryAdd(item.ToState(), out _);
            }

            // 복원도 슬롯 상한을 지킨다(DEC-2026-08-31-02 Q4·Q5). 종전에는 이 경로에만 게이트가
            // 없어서(「세이브가 정본이므로») 상한을 넘긴 가방이 되살아날 수 있었다. 그리고 소모품은
            // 스택되지 않으므로(획득 관문이 언제나 새 칸에 1개를 넣는다) 옛 세이브의 Count>1은
            // 한 칸짜리 여러 개로 펴서 싣는다 — 그래야 「중복 포함 최대 3칸」이 복원에서도 참이 된다.
            var bag = new PlayerBagState();
            var bagSlotLimit = PlayerBagState.BaseSlotCount;
            foreach (var item in relicsAndCurses.Items)
            {
                foreach (var effect in item.AllEffects)
                {
                    if (effect.Kind == PlayerPermanentItemEffectKind.BagSlotBonus)
                    {
                        bagSlotLimit += effect.Amount;
                    }
                }
            }

            bagSlotLimit = Math.Max(1, bagSlotLimit);
            foreach (var stack in BagStacks ?? new List<PlayerBagItemStackSaveData>())
            {
                var migratedId = ConsumableItemIdMigration.Migrate(stack.ItemId);
                for (var i = 0; i < Math.Max(0, stack.Count) && bag.UsedSlotCount < bagSlotLimit; i++)
                {
                    bag.AddPlaceholderStack(migratedId, 1);
                }
            }

            return new PlayerInventoryState(relicsAndCurses, bag, new PlayerWalletState(WalletBalance), MightStacks);
        }
    }

    [Serializable]
    public sealed class PlayerPermanentItemSaveData
    {
        public string Id = string.Empty;
        public PlayerPermanentItemKind Kind;

        /// <summary>턴 제한 유물(청사초롱, T2 페이즈 B)의 남은 활성 턴. −1=무기한/필드 부재(구 세이브).</summary>
        public int RemainingActiveTurns = -1;

        public static PlayerPermanentItemSaveData FromState(PlayerPermanentItemState state)
        {
            return new PlayerPermanentItemSaveData
            {
                Id = state?.Id ?? string.Empty,
                Kind = state?.Kind ?? PlayerPermanentItemKind.Relic,
                RemainingActiveTurns = state?.RemainingActiveTurns ?? -1
            };
        }

        public PlayerPermanentItemState ToState()
        {
            if (PlayerPermanentItemCatalog.TryGet(Id, out var definition))
            {
                var state = definition.ToState();
                state.RestoreRemainingActiveTurns(RemainingActiveTurns);
                return state;
            }

            return new PlayerPermanentItemState(Id, Kind);
        }
    }

    [Serializable]
    public sealed class PlayerBagItemStackSaveData
    {
        public string ItemId = string.Empty;
        public int Count;

        public static PlayerBagItemStackSaveData FromState(PlayerBagItemStack state)
        {
            return new PlayerBagItemStackSaveData
            {
                ItemId = state.ItemId,
                Count = state.Count
            };
        }

        public PlayerBagItemStack ToState() => new PlayerBagItemStack(ItemId, Count);
    }

    [Serializable]
    public sealed class PlayerFeedbackSaveData
    {
        public string LastFailureReason = string.Empty;
        // JsonUtility cannot serialize Nullable<T>, so the optional discarded-card value is stored
        // as a has-flag + plain enum pair and reassembled in ToState().
        public bool HasLastDiscardedCard;
        public CombatCardKind LastDiscardedCard;
        public string LastInvestigateResult = string.Empty;

        public static PlayerFeedbackSaveData FromState(PlayerFeedbackState state)
        {
            return new PlayerFeedbackSaveData
            {
                LastFailureReason = state?.LastFailureReason ?? string.Empty,
                HasLastDiscardedCard = state?.LastDiscardedCard != null,
                LastDiscardedCard = state?.LastDiscardedCard ?? default,
                LastInvestigateResult = state?.LastInvestigateResult ?? string.Empty
            };
        }

        public PlayerFeedbackState ToState() => new PlayerFeedbackState(
            LastFailureReason,
            HasLastDiscardedCard ? LastDiscardedCard : (CombatCardKind?)null,
            LastInvestigateResult);
    }
}
