using System;
using SeoulPlayup.Map.Runtime;

namespace SeoulPlayup.Combat.Runtime
{
    [Serializable]
    public sealed class PlayerState
    {
        public PlayerState(
            PlayerVitals vitals = null,
            PlayerResources resources = null,
            PlayerPositionState position = null,
            PlayerDecksRuntimeState decks = null,
            PlayerKnowledgeState knowledge = null,
            PlayerObjectiveProgressState objective = null,
            PlayerInventoryState inventory = null,
            PlayerFeedbackState feedback = null)
        {
            Vitals = vitals ?? new PlayerVitals();
            Resources = resources ?? new PlayerResources();
            Position = position ?? new PlayerPositionState();
            Decks = decks ?? new PlayerDecksRuntimeState();
            Knowledge = knowledge ?? new PlayerKnowledgeState();
            Objective = objective ?? new PlayerObjectiveProgressState();
            Inventory = inventory ?? new PlayerInventoryState();
            Feedback = feedback ?? new PlayerFeedbackState();
        }

        public PlayerVitals Vitals { get; }
        public PlayerResources Resources { get; }
        public PlayerPositionState Position { get; }
        public PlayerDecksRuntimeState Decks { get; }
        public PlayerKnowledgeState Knowledge { get; }
        public PlayerObjectiveProgressState Objective { get; }
        public PlayerInventoryState Inventory { get; }
        public PlayerFeedbackState Feedback { get; }

        public static PlayerState FromSnapshot(PlayerStateSnapshot snapshot)
        {
            return new PlayerState(
                new PlayerVitals(snapshot.Hp, snapshot.MaxHp, snapshot.Block, snapshot.IsDead),
                new PlayerResources(snapshot.CurrentKi, snapshot.MaxKi),
                new PlayerPositionState(snapshot.Position, snapshot.Phase),
                new PlayerDecksRuntimeState(snapshot.MoveDeck, snapshot.ActionDeck),
                new PlayerKnowledgeState(snapshot.Visibility),
                new PlayerObjectiveProgressState(snapshot.ObjectiveCompleted, snapshot.ObjectiveStatusText),
                new PlayerInventoryState(),
                new PlayerFeedbackState(snapshot.LastFailureReason, snapshot.LastDiscardedCard, snapshot.LastInvestigateResult));
        }
    }

    [Serializable]
    public sealed class PlayerVitals
    {
        public PlayerVitals(int hp = 1, int maxHp = 1, int block = 0, bool isDead = false)
        {
            MaxHp = Math.Max(1, maxHp);
            Hp = isDead ? 0 : Math.Max(0, Math.Min(hp, MaxHp));
            Block = Math.Max(0, block);
        }

        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public int Block { get; private set; }
        public bool IsDead => Hp <= 0;

        public void SetHp(int hp)
        {
            Hp = Math.Max(0, Math.Min(hp, MaxHp));
        }

        public void SetMaxHp(int maxHp, bool preserveHp = true)
        {
            var previousHp = Hp;
            MaxHp = Math.Max(1, maxHp);
            Hp = preserveHp ? Math.Min(previousHp, MaxHp) : MaxHp;
        }

        public void SetBlock(int block)
        {
            Block = Math.Max(0, block);
        }
    }

    [Serializable]
    public sealed class PlayerResources
    {
        public PlayerResources(int currentKi = 0, int maxKi = 0)
        {
            MaxKi = Math.Max(0, maxKi);
            CurrentKi = Math.Max(0, Math.Min(currentKi, MaxKi));
        }

        public int CurrentKi { get; private set; }
        public int MaxKi { get; private set; }

        public void SetKi(int currentKi, int maxKi)
        {
            MaxKi = Math.Max(0, maxKi);
            CurrentKi = Math.Max(0, Math.Min(currentKi, MaxKi));
        }

        public bool TrySpendKi(int amount)
        {
            var cost = Math.Max(0, amount);
            if (cost > CurrentKi)
            {
                return false;
            }

            CurrentKi -= cost;
            return true;
        }

        public void RefillKi()
        {
            CurrentKi = MaxKi;
        }
    }

    [Serializable]
    public sealed class PlayerPositionState
    {
        public PlayerPositionState(HexCoord coord = default, CombatPhase phase = CombatPhase.PlayerMovement)
        {
            Coord = coord;
            Phase = phase;
        }

        public HexCoord Coord { get; private set; }
        public CombatPhase Phase { get; private set; }

        public void Set(HexCoord coord, CombatPhase phase)
        {
            Coord = coord;
            Phase = phase;
        }
    }

    [Serializable]
    public sealed class PlayerDecksRuntimeState
    {
        public PlayerDecksRuntimeState(PlayerDeckRuntimeSummary moveDeck = default, PlayerDeckRuntimeSummary actionDeck = default)
        {
            MoveDeck = moveDeck;
            ActionDeck = actionDeck;
        }

        public PlayerDeckRuntimeSummary MoveDeck { get; private set; }
        public PlayerDeckRuntimeSummary ActionDeck { get; private set; }

        public void Set(PlayerDeckRuntimeSummary moveDeck, PlayerDeckRuntimeSummary actionDeck)
        {
            MoveDeck = moveDeck;
            ActionDeck = actionDeck;
        }
    }

    [Serializable]
    public sealed class PlayerKnowledgeState
    {
        public PlayerKnowledgeState(PlayerVisibilitySummary visibility = default)
        {
            Visibility = visibility;
        }

        public PlayerVisibilitySummary Visibility { get; private set; }

        public void SetVisibility(PlayerVisibilitySummary visibility)
        {
            Visibility = visibility;
        }
    }

    [Serializable]
    public sealed class PlayerObjectiveProgressState
    {
        public PlayerObjectiveProgressState(bool completed = false, string statusText = "")
        {
            Completed = completed;
            StatusText = statusText ?? string.Empty;
        }

        public bool Completed { get; private set; }
        public string StatusText { get; private set; }

        public void Set(bool completed, string statusText)
        {
            Completed = completed;
            StatusText = statusText ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class PlayerInventoryState
    {
        public PlayerInventoryState(
            PlayerRelicCurseInventory relicsAndCurses = null,
            PlayerBagState bag = null,
            PlayerWalletState wallet = null,
            int mightStacks = 0)
        {
            RelicsAndCurses = relicsAndCurses ?? new PlayerRelicCurseInventory();
            Bag = bag ?? new PlayerBagState();
            Wallet = wallet ?? new PlayerWalletState();
            MightStacks = Math.Max(0, mightStacks);
        }

        public PlayerRelicCurseInventory RelicsAndCurses { get; }
        public PlayerBagState Bag { get; }
        public PlayerWalletState Wallet { get; }
        public string RelicCurseStatusText => RelicsAndCurses.StatusText;
        public string BagStatusText => Bag.StatusText;

        /// <summary>
        /// 힘(<see cref="StatusEffectKind.Might"/>)의 <b>정본</b> — 가하는 피해에 더해지는 고정치이며
        /// 런 전체 유지된다(D-3). 유물과 같은 칸에 사는 이유가 그것이다: 지갑처럼 전투마다 리셋되지
        /// 않고, 상태이상처럼 턴마다 깎이지도 않는다.
        /// <para>HUD에는 상태이상 독으로 <b>투영</b>되지만(D-9) <c>ActiveEffect</c>로 복제하지 않는다 —
        /// 복제하면 턴 틱이 깎고 정화가 집고 피해 합산이 두 번 센다.</para>
        /// </summary>
        public int MightStacks { get; private set; }

        /// <summary>힘을 더한다(음수면 줄인다). 0 아래로는 내려가지 않는다.</summary>
        public int AddMight(int amount)
        {
            MightStacks = Math.Max(0, MightStacks + amount);
            return MightStacks;
        }

        public PlayerInventoryState Clone()
        {
            return new PlayerInventoryState(RelicsAndCurses.Clone(), Bag.Clone(), Wallet.Clone(), MightStacks);
        }
    }

    /// <summary>
    /// 스테이지 한정 재화. 전투가 시작될 때 0에서 출발하고 스테이지가 끝나면 사라진다 — 인벤토리가
    /// 스테이지마다 새로 만들어지므로(DEC-2026-07-28-05) 별도의 초기화 코드 없이 그 성질이 나온다.
    /// 획득처는 인형뽑기, 사용처는 맵에 놓인 상점이다(설계: docs/design/relic-curse-economy.md).
    /// </summary>
    [Serializable]
    public sealed class PlayerWalletState
    {
        public PlayerWalletState(int balance = 0)
        {
            Balance = Math.Max(0, balance);
        }

        public int Balance { get; private set; }
        public string StatusText => $"재화 {Balance}";

        /// <summary>지급. 음수·0은 무시한다. 실제로 늘어난 금액을 돌려준다(연출이 표시할 값).</summary>
        public int Add(int amount)
        {
            var gain = Math.Max(0, amount);
            Balance += gain;
            return gain;
        }

        /// <summary>
        /// 지불. 잔액이 모자라면 **아무것도 차감하지 않고** false를 돌려준다 — 부분 지불은 상점에서
        /// 의미가 없고, 조용히 0으로 깎이면 잔액이 사라진 이유를 추적할 수 없다.
        /// </summary>
        public bool TrySpend(int amount, out string reason)
        {
            if (amount <= 0)
            {
                reason = "Spend amount must be positive.";
                return false;
            }

            if (Balance < amount)
            {
                reason = $"Not enough currency: have {Balance}, need {amount}.";
                return false;
            }

            Balance -= amount;
            reason = string.Empty;
            return true;
        }

        public PlayerWalletState Clone() => new PlayerWalletState(Balance);
    }

    public enum PlayerPermanentItemKind
    {
        Relic,
        Curse
    }

    /// <summary>
    /// 영구 아이템이 더하는 패시브 효과의 축. 전부 "종류별 총량을 대응 계산식에 가산"하는 스칼라
    /// 가산형이다(게임플레이 계약 RC-1). 새 축은 반드시 **끝에 추가**한다 — CSV는 이름으로 저작하지만
    /// 세이브·직렬화가 정수값에 의존할 수 있어 중간 삽입은 기존 데이터의 의미를 바꾼다.
    /// </summary>
    public enum PlayerPermanentItemEffectKind
    {
        None,
        AttackDamageBonus,
        MovementRangeBonus,
        IncomingDamageDelta,
        VisionRangeBonus,
        MaxHpBonus,
        BlockGainBonus,
        MaxKiBonus,
        MovementHandSizeBonus,
        ActionHandSizeBonus,

        // ---- T2 페이즈 B(2026-08-06) 신규 축 — 반드시 끝에 추가(직렬화 정수값 안정) ----
        /// <summary>획득 즉시 돈 +N(1회성 — 관문에서 지급). 복주머니.</summary>
        MoneyGrantOnce,

        /// <summary>획득 즉시 힘 +N(1회성 — MightStacks 가산). 삼족오 타투.</summary>
        MightGrantOnce,

        /// <summary>상점 전 품목 N% 할인 — 재고 추첨 시점에 가격표를 깎는다. 단골 도장.</summary>
        ShopDiscountPercent,

        /// <summary>돈을 얻을 때마다 +N — 지급 단일 이음매(GrantMoneyWithRelicBonus)에서 가산. 적립 카드.</summary>
        MoneyGainBonus,

        /// <summary>0이 아니면 시야에 들어온 함정이 자동 발각된다. 골목 CCTV.</summary>
        TrapAutoReveal,

        /// <summary>얻는 방어막 −N — 파열과 같은 지점에서 감산. 무거운 배낭의 대가.</summary>
        BlockGainPenalty,

        /// <summary>획득 즉시 최대 체력 −N(1회성 — 하한 1). 마이너스 통장의 대가.</summary>
        MaxHpPenalty,

        // ---- T4-3(2026-08-07) — 반드시 끝에 추가(직렬화 정수값 안정) ----
        /// <summary>가방 슬롯 +N — 유물↔가방 교차 축의 첫 사례. 배달 가방.</summary>
        BagSlotBonus
    }

    /// <summary>양날 유물(T2 페이즈 B)을 위한 (축, 크기) 한 쌍 — 정의·상태의 extraEffects 원소.</summary>
    [Serializable]
    public readonly struct PlayerPermanentItemEffect
    {
        public PlayerPermanentItemEffect(PlayerPermanentItemEffectKind kind, int amount)
        {
            Kind = kind;
            Amount = amount;
        }

        public PlayerPermanentItemEffectKind Kind { get; }
        public int Amount { get; }
    }

    [Serializable]
    public sealed class PlayerPermanentItemDefinition
    {
        public PlayerPermanentItemDefinition(
            string id,
            PlayerPermanentItemKind kind,
            string displayName,
            string description,
            PlayerPermanentItemEffectKind effectKind = PlayerPermanentItemEffectKind.None,
            int effectAmount = 0,
            System.Collections.Generic.IReadOnlyList<PlayerPermanentItemEffect> extraEffects = null,
            int durationTurns = 0,
            RelicTriggerKind triggerKind = RelicTriggerKind.None,
            int triggerParam = 0,
            SeoulPlayup.CardCore.CardRarity rarity = SeoulPlayup.CardCore.CardRarity.Rare,
            int priceDelta = 0,
            string iconId = null)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Permanent item definition id is required.", nameof(id)) : id;
            Kind = kind;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Description = description ?? string.Empty;
            EffectKind = effectKind;
            EffectAmount = effectAmount;
            ExtraEffects = extraEffects ?? System.Array.Empty<PlayerPermanentItemEffect>();
            DurationTurns = Math.Max(0, durationTurns);
            TriggerKind = triggerKind;
            TriggerParam = Math.Max(0, triggerParam);
            Rarity = rarity;
            PriceDelta = priceDelta;
            IconId = iconId ?? string.Empty;
        }

        public string Id { get; }
        public PlayerPermanentItemKind Kind { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public PlayerPermanentItemEffectKind EffectKind { get; }
        public int EffectAmount { get; }

        /// <summary>양날 유물(T2 페이즈 B)의 부가 효과 목록 — CSV extraEffects(kind:amount;…) 컬럼.</summary>
        public System.Collections.Generic.IReadOnlyList<PlayerPermanentItemEffect> ExtraEffects { get; }

        /// <summary>턴 제한 유물(청사초롱)의 활성 턴 수. 0=무기한.</summary>
        public int DurationTurns { get; }

        /// <summary>트리거 유물(T2 페이즈 C)의 트리거 종류. None=스칼라 유물.</summary>
        public RelicTriggerKind TriggerKind { get; }

        /// <summary>트리거의 주기/문턱(수호 20턴·코인 세탁기 10장·무선 이어폰 3칸). 미사용 트리거는 0.</summary>
        public int TriggerParam { get; }

        /// <summary>
        /// 등급(DEC-2026-08-31-01 Q1). 🔴<b>플레이어에게 노출되지 않는다</b> — 저작·밸런스용 내부 축이고
        /// 하는 일은 상점 기준가를 고르는 것뿐이다. 도감·툴팁·칩·뽑기 어디에도 이 값이 새면 안 된다.
        /// </summary>
        public SeoulPlayup.CardCore.CardRarity Rarity { get; }

        /// <summary>등급 기준가에 더하는 행별 변주(−10 … +15). 가격이 등급을 그대로 폭로하지 않게 흐린다.</summary>
        public int PriceDelta { get; }

        /// <summary>아이콘 스프라이트 키(<c>relic_icon_*</c>). 비면 아직 아트가 없다는 뜻이다.</summary>
        public string IconId { get; }

        public PlayerPermanentItemState ToState()
        {
            return new PlayerPermanentItemState(
                Id, Kind, DisplayName, Description, EffectKind, EffectAmount, ExtraEffects, DurationTurns, TriggerKind, TriggerParam, IconId);
        }
    }

    /// <summary>
    /// 유물·저주 정의의 조회 façade. 정의는 CSV(<see cref="CombatCsvPaths.RelicsCsv"/>)가 정본이며,
    /// 에디터/테스트에서는 <see cref="EnsureLoaded"/>가 파일을 지연 로딩하고, 플레이어 빌드에서는
    /// Unity glue가 부팅 시 <see cref="Register"/>로 TextAsset 파싱 카탈로그를 주입한다.
    /// (하드코딩 배열에서 데이터 주도로 전환 — 대응 계획 docs/source/20-plans/systems/relic-curse-gameplay.md)
    /// </summary>
    public static class PlayerPermanentItemCatalog
    {
        public const string TigerBadgeRelicId = "relic-tiger-badge";
        public const string HanriverShoesRelicId = "relic-hanriver-shoes";
        public const string CrackedMemoryCurseId = "curse-cracked-memory";

        private static RelicCatalogDefinition catalog;

        /// <summary>
        /// 파싱된 유물·저주 카탈로그를 주입한다(플레이어 빌드 부팅 경로). 이후 조회는 이 카탈로그를 쓴다.
        /// </summary>
        public static void Register(RelicCatalogDefinition relicCatalog)
        {
            catalog = relicCatalog ?? throw new ArgumentNullException(nameof(relicCatalog));
        }

        public static System.Collections.Generic.IReadOnlyList<PlayerPermanentItemDefinition> Definitions => EnsureLoaded().Entries;

        public static bool TryGet(string id, out PlayerPermanentItemDefinition definition)
        {
            return EnsureLoaded().TryGet(id, out definition);
        }

        private static RelicCatalogDefinition EnsureLoaded()
        {
            return catalog ??= LoadFromSource();
        }

        private static RelicCatalogDefinition LoadFromSource()
        {
            try
            {
                if (System.IO.File.Exists(CombatCsvPaths.RelicsCsv))
                {
                    return RelicCatalogCsv.ConvertFile(CombatCsvPaths.RelicsCsv);
                }
            }
            catch
            {
                // 소스 CSV가 없거나 손상된 컨텍스트(예: Register 이전의 플레이어 빌드)에서는 빈 카탈로그로
                // 폴백한다. 플레이어 빌드는 부팅 시 Register로 카탈로그를 주입해 이 경로에 의존하지 않는다.
            }

            return new RelicCatalogDefinition(System.Array.Empty<PlayerPermanentItemDefinition>());
        }
    }

    [Serializable]
    public sealed class PlayerPermanentItemState
    {
        public PlayerPermanentItemState(
            string id,
            PlayerPermanentItemKind kind,
            string displayName = "",
            string description = "",
            PlayerPermanentItemEffectKind effectKind = PlayerPermanentItemEffectKind.None,
            int effectAmount = 0,
            System.Collections.Generic.IReadOnlyList<PlayerPermanentItemEffect> extraEffects = null,
            int durationTurns = 0,
            RelicTriggerKind triggerKind = RelicTriggerKind.None,
            int triggerParam = 0,
            string iconId = null)
        {
            Id = string.IsNullOrWhiteSpace(id) ? throw new ArgumentException("Permanent item id is required.", nameof(id)) : id;
            Kind = kind;
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? Id : displayName;
            Description = description ?? string.Empty;
            EffectKind = effectKind;
            EffectAmount = effectAmount;
            ExtraEffects = extraEffects ?? System.Array.Empty<PlayerPermanentItemEffect>();
            DurationTurns = Math.Max(0, durationTurns);
            RemainingActiveTurns = DurationTurns > 0 ? DurationTurns : -1;
            TriggerKind = triggerKind;
            TriggerParam = Math.Max(0, triggerParam);
            IconId = iconId ?? string.Empty;
        }

        public string Id { get; }
        public PlayerPermanentItemKind Kind { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public PlayerPermanentItemEffectKind EffectKind { get; }
        public int EffectAmount { get; }
        public System.Collections.Generic.IReadOnlyList<PlayerPermanentItemEffect> ExtraEffects { get; }

        /// <summary>
        /// 아이콘 스프라이트 키(<c>relic_icon_*</c>) — 저작에서 그대로 실려 온다. 비면 아직 아트가 없다는
        /// 뜻이고 사이드바 칩은 표시명 2글자 폴백으로 떨어진다.
        /// </summary>
        public string IconId { get; }

        /// <summary>턴 제한 유물(청사초롱). 0=무기한.</summary>
        public int DurationTurns { get; }

        /// <summary>남은 활성 턴. −1=무기한, 0=만료. 만료돼도 아이템은 삭제하지 않는다(사용자 확정 — 칩 비활성).</summary>
        public int RemainingActiveTurns { get; private set; }

        /// <summary>트리거 유물(T2 페이즈 C)의 트리거 종류. None=스칼라 유물.</summary>
        public RelicTriggerKind TriggerKind { get; }

        /// <summary>트리거의 주기/문턱. 미사용 트리거는 0.</summary>
        public int TriggerParam { get; }

        public bool IsActive => RemainingActiveTurns != 0;

        /// <summary>플레이어 턴 시작마다 1 감소(무기한은 불변).</summary>
        public void TickActiveTurn()
        {
            if (RemainingActiveTurns > 0)
            {
                RemainingActiveTurns--;
            }
        }

        /// <summary>세이브 복원 전용. 음수(구 세이브의 필드 부재)는 새로 산 것처럼 전량 복원한다.</summary>
        public void RestoreRemainingActiveTurns(int remaining)
        {
            if (DurationTurns > 0)
            {
                RemainingActiveTurns = remaining < 0 ? DurationTurns : Math.Max(0, remaining);
            }
        }

        /// <summary>주 효과 + 부가 효과의 단일 순회 — 관문 1회 적용과 SumEffect가 같은 목록을 본다.</summary>
        public System.Collections.Generic.IEnumerable<PlayerPermanentItemEffect> AllEffects
        {
            get
            {
                if (EffectKind != PlayerPermanentItemEffectKind.None && EffectAmount != 0)
                {
                    yield return new PlayerPermanentItemEffect(EffectKind, EffectAmount);
                }

                for (var i = 0; i < ExtraEffects.Count; i++)
                {
                    yield return ExtraEffects[i];
                }
            }
        }

        public bool CanRemove => false;
        public bool HasGameplayEffect =>
            (EffectKind != PlayerPermanentItemEffectKind.None && EffectAmount != 0)
            || ExtraEffects.Count > 0
            || TriggerKind != RelicTriggerKind.None;

        public string EffectSummary
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                foreach (var effect in AllEffects)
                {
                    parts.Add(PlayerPermanentItemText.Effect(effect.Kind, effect.Amount));
                }

                if (TriggerKind != RelicTriggerKind.None)
                {
                    parts.Add(PlayerPermanentItemText.Trigger(TriggerKind, TriggerParam, EffectAmount));
                }

                if (parts.Count == 0)
                {
                    return "효과 없음";
                }

                var summary = string.Join(" · ", parts);
                if (DurationTurns > 0)
                {
                    summary += RemainingActiveTurns > 0 ? $" ({RemainingActiveTurns}턴 남음)" : " (만료)";
                }

                return summary;
            }
        }

    }

    [Serializable]
    public sealed class PlayerRelicCurseInventory
    {
        private readonly System.Collections.Generic.List<PlayerPermanentItemState> items = new System.Collections.Generic.List<PlayerPermanentItemState>();

        public PlayerRelicCurseInventory(int slotLimit = 0)
        {
            SlotLimit = Math.Max(0, slotLimit);
        }

        public int SlotLimit { get; }
        public System.Collections.Generic.IReadOnlyList<PlayerPermanentItemState> Items => items;
        public int RelicCount => items.FindAll(item => item.Kind == PlayerPermanentItemKind.Relic).Count;
        public int CurseCount => items.FindAll(item => item.Kind == PlayerPermanentItemKind.Curse).Count;
        public string StatusText => $"Relics {RelicCount}, Curses {CurseCount}, SlotLimit {(SlotLimit == 0 ? "TBD" : SlotLimit.ToString())}";

        /// <summary>보유 여부. 뽑기가 중복 유물을 추첨 풀에서 빼는 데 쓴다(D-9).</summary>
        public bool Contains(string itemId)
        {
            return !string.IsNullOrWhiteSpace(itemId)
                && items.Exists(existing => string.Equals(existing.Id, itemId, StringComparison.Ordinal));
        }

        public int SumEffect(PlayerPermanentItemEffectKind effectKind)
        {
            var sum = 0;
            for (var i = 0; i < items.Count; i++)
            {
                if (!items[i].IsActive)
                {
                    // 턴 제한 만료(청사초롱) — 칩은 남지만 효과는 꺼진다(T2 페이즈 B).
                    continue;
                }

                foreach (var effect in items[i].AllEffects)
                {
                    if (effect.Kind == effectKind)
                    {
                        sum += effect.Amount;
                    }
                }
            }

            return sum;
        }

        /// <summary>플레이어 턴 시작 훅(T2 페이즈 B) — 턴 제한 유물의 남은 턴을 깎는다.</summary>
        public void TickLimitedRelicTurns()
        {
            for (var i = 0; i < items.Count; i++)
            {
                items[i].TickActiveTurn();
            }
        }

        public bool TryAddDefinition(string definitionId, out string reason)
        {
            if (!PlayerPermanentItemCatalog.TryGet(definitionId, out var definition))
            {
                reason = "Unknown permanent item definition.";
                return false;
            }

            return TryAdd(definition.ToState(), out reason);
        }

        public bool TryAdd(PlayerPermanentItemState item, out string reason)
        {
            reason = string.Empty;
            if (item == null)
            {
                reason = "Permanent item is required.";
                return false;
            }

            if (SlotLimit > 0 && items.Count >= SlotLimit)
            {
                reason = "Permanent item slot limit reached.";
                return false;
            }

            if (items.Exists(existing => string.Equals(existing.Id, item.Id, StringComparison.Ordinal)))
            {
                reason = "Permanent item already acquired.";
                return false;
            }

            items.Add(item);
            return true;
        }

        public bool TryRemove(string id, out string reason)
        {
            reason = "Relics and curses cannot be removed in the MVP placeholder.";
            return false;
        }

        public PlayerRelicCurseInventory Clone()
        {
            var clone = new PlayerRelicCurseInventory(SlotLimit);
            for (var i = 0; i < items.Count; i++)
            {
                clone.items.Add(items[i]);
            }

            return clone;
        }
    }

    /// <summary>
    /// 소모품 가방(T4-1에서 실효화 — RC-8 개정). 슬롯 하나 = 아이템 하나(스택 count는 세이브 호환용
    /// 레거시 표면이며 실아이템은 항상 1). 아이템의 의미·효과는 <see cref="ConsumableItemCatalog"/>가
    /// 정본이고 여기는 보유 목록만 든다 — 유물 인벤토리(RC-7)와 같은 "세이브는 id, 해석은 카탈로그" 계약.
    /// 슬롯 상한은 상태가 아니라 호출부(<c>CombatState.GetEffectiveBagSlotLimit</c>)가 게이트한다 —
    /// 멜빵 유물(T4-3)의 +1이 인벤토리 상태를 재작성하지 않게 하기 위해서다.
    /// </summary>
    [Serializable]
    public sealed class PlayerBagState
    {
        public const int BaseSlotCount = 3;

        private readonly System.Collections.Generic.List<PlayerBagItemStack> stacks = new System.Collections.Generic.List<PlayerBagItemStack>();

        public System.Collections.Generic.IReadOnlyList<PlayerBagItemStack> Stacks => stacks;
        public int UsedSlotCount => stacks.Count;
        public string StatusText => stacks.Count == 0 ? "Bag empty" : $"Bag ({stacks.Count} item(s))";

        /// <summary>슬롯에 자리가 있으면 아이템 1개를 넣는다. 같은 아이템도 슬롯을 하나씩 차지한다.</summary>
        public bool TryAddItem(string itemId, int slotLimit)
        {
            if (string.IsNullOrWhiteSpace(itemId) || stacks.Count >= System.Math.Max(0, slotLimit))
            {
                return false;
            }

            stacks.Add(new PlayerBagItemStack(itemId, 1));
            return true;
        }

        /// <summary>사용 소비 — 해당 아이템 1개를 제거한다. 없으면 false.</summary>
        public bool TryConsumeItem(string itemId)
        {
            for (var i = 0; i < stacks.Count; i++)
            {
                if (!string.Equals(stacks[i].ItemId, itemId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (stacks[i].Count > 1)
                {
                    stacks[i] = new PlayerBagItemStack(stacks[i].ItemId, stacks[i].Count - 1);
                }
                else
                {
                    stacks.RemoveAt(i);
                }

                return true;
            }

            return false;
        }

        public void SetPlaceholderStack(string itemId, int count)
        {
            stacks.Clear();
            AddPlaceholderStack(itemId, count);
        }

        /// <summary>세이브 복원·디버그 표면 — 슬롯 게이트 없이 그대로 싣는다(세이브가 정본이므로).</summary>
        public void AddPlaceholderStack(string itemId, int count)
        {
            if (string.IsNullOrWhiteSpace(itemId) || count <= 0)
            {
                return;
            }

            stacks.Add(new PlayerBagItemStack(itemId, count));
        }

        public PlayerBagState Clone()
        {
            var clone = new PlayerBagState();
            for (var i = 0; i < stacks.Count; i++)
            {
                clone.stacks.Add(stacks[i]);
            }

            return clone;
        }
    }

    [Serializable]
    public readonly struct PlayerBagItemStack
    {
        public PlayerBagItemStack(string itemId, int count)
        {
            ItemId = itemId ?? string.Empty;
            Count = Math.Max(0, count);
        }

        public string ItemId { get; }
        public int Count { get; }
    }

    [Serializable]
    public sealed class PlayerFeedbackState
    {
        public PlayerFeedbackState(string lastFailureReason = "", CombatCardKind? lastDiscardedCard = null, string lastInvestigateResult = "")
        {
            LastFailureReason = lastFailureReason ?? string.Empty;
            LastDiscardedCard = lastDiscardedCard;
            LastInvestigateResult = lastInvestigateResult ?? string.Empty;
        }

        public string LastFailureReason { get; private set; }
        public CombatCardKind? LastDiscardedCard { get; private set; }
        public string LastInvestigateResult { get; private set; }

        public void Set(string lastFailureReason, CombatCardKind? lastDiscardedCard, string lastInvestigateResult)
        {
            LastFailureReason = lastFailureReason ?? string.Empty;
            LastDiscardedCard = lastDiscardedCard;
            LastInvestigateResult = lastInvestigateResult ?? string.Empty;
        }
    }
}
