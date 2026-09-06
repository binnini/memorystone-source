Repo head: `6d873e00c087ad089e25dc2a29b219f9a111943` (git log timestamp 2026-09-07 00:36:18 +0900), repo `/Users/yebin/game/memorystone-source`, no working-tree changes at inspection time.

Note up front: this repo contains only `Source/` and `Tests/` (a Unity package/plugin repo, no `Assets/` folder). `cards.csv` itself is **not physically present** in this repo — it is referenced only as a path constant (`Assets/Data/Combat/Cards/Source/cards.csv`), confirming this repo is the code-only source tree and the CSV lives in the consuming Unity project (Seoul-Playup-MVP).

---

## D1. Data path

**cards.csv location (constant, not a file in this repo):**
`Source/Combat/Runtime/CombatCsvPaths.cs:16`
```
public const string CardsCsv = CardDirectory + "/cards.csv";
```
resolves to `Assets/Data/Combat/Cards/Source/cards.csv`. No such file exists in `/Users/yebin/game/memorystone-source` (confirmed via `find` — zero hits); every reference in the codebase is a path string or a runtime/editor consumer.

**Header (26 columns), verified at** `Source/Cards/Unity/CardCatalogAsset.cs:21-28` (`ExpectedHeader`):
```
id, name, description, type, target, cost, range, shape,
damage, shield, heal, stateEffect, buff_debuff, duration,
hitCount, costMode, scalingMode, gameplayType, targeting,
status, includeInDecks, visibleInCatalog, illustrationId, rarity,
choiceTexts, descriptionUpgraded
```
Header check is by **name-set**, not fixed order/index (`ParseTable`, same file, lines 133–165); duplicate/missing/extra columns throw `FormatException`.

**Importer:**
`Source/Editor/Cards/CardCatalogCsvImporter.cs` — class `CardCatalogCsvImporter` (static, Editor-only). `InputPath = CombatCsvPaths.CardsCsv` (line 13), `OutputPath = "Assets/Data/Combat/Cards/Catalogs/CardCatalog.asset"` (line 14). Menu item `Tools/Cards/Import cards.csv` (line 16) → `ImportDefault()` (line 17) → `Import(csvPath, assetPath)` (line 22): reads the CSV text, calls `CardCatalogAsset.ParseCsvText`, creates/loads a `CardCatalogAsset` ScriptableObject, `SetRows`, `ValidateRows`, saves.

**CardCatalogAsset** — `Source/Cards/Unity/CardCatalogAsset.cs:13`, `public sealed class CardCatalogAsset : ScriptableObject`. Holds `List<CardCatalogCsvRow> rows` (line 62), converts to `CardCatalogDefinition` via `ToCardCatalogDefinition(CombatConfig config)` (line 75). Notably `BuildEntry` (line 167) calls `CardBehaviorRegistry.Get(row.Id)` (line 172) to consult the card class for `Choices` (targeting derivation) and `FieldObjectKind`, and `ValidateRow` (line 234) rejects any CSV row whose id has no registered `CardBehavior` (line 243-247) — i.e. the CSV can no longer author cards without a matching class.

**CardCatalogDefinition** — `Source/CardCore/Runtime/CardCatalogDefinition.cs:7`, `public sealed class CardCatalogDefinition`. Fields: `entries` (`List<CardCatalogEntry>`, line 9); public surface: `SourceId`, `DisplayName`, `Entries` (lines 20-22), `CreateDeck(CardCategory deckType)` (line 24), `GetVisibleCatalogEntries()` (line 32), `CreateBindingEvidence()` (line 39), `Validate(out reason)` (line 51).

**CardCatalogEntry** fields (constructor) — `Source/CardCore/Runtime/CardCatalogEntry.cs:8-38`: `id, displayName, deckType, actionType, cost, range, amount, targeting, sourceTrace, areaRadius, phaseAvailability, playMode, fieldObjectKind, durationTurns, status, gameplayType, costMode, targetMode, scalingMode, presentationRef, includeInGameplayDecks, visibleInCatalog, shapeId, hitCount, choiceOptionTexts, description, rarity, stateEffect, buffDebuff, healAmount, descriptionUpgraded`.

**PlayerDeckData** — `Source/CardCore/Runtime/PlayerDeckData.cs:33`, `public sealed class PlayerDeckData`. Holds two lists: `movementCards` and `actionCards` (both `List<PlayerCardInstanceData>`, lines 35-36), exposed as `MovementCards`/`ActionCards` (lines 50-51). This is the **owned/purchasable deck roster** (persistent, per-run), not the live shuffled deck state. Key methods: `FromCatalog` (line 53), `CreateDeck(catalog, category)` (line 65), `AddCard` (line 88), `RemoveCard` (line 129), `UpgradeCard` (line 155).

**CardDeckState** — `Source/CardCore/Runtime/DeckState.cs:316-332`:
```csharp
public sealed class CardDeckState : DeckState<CardDefinition>
```
`CardDeckState` itself is a **single** deck instance (draw/hand/discard/removed piles) parameterized on `CardDefinition`. It does **not** internally hold two decks. The "two decks" (movement/action) structure lives one level up, in `CombatState`: `MovementDeck` and `ActionDeck` are two separate `CardDeckState` instances — `Source/Combat/Runtime/CombatState.cs:433-434`:
```csharp
public CardDeckState MovementDeck { get; private set; }
public CardDeckState ActionDeck { get; private set; }
```
constructed at lines 172-173 via `CreateDeck(CardCategory.Movement, shuffleDecks)` / `CreateDeck(CardCategory.Action, shuffleDecks)`.

**DeckState<TCard> piles** — `Source/CardCore/Runtime/DeckState.cs:10-13`: private `List<TCard>` fields `drawPile`, `hand`, `discardPile`, `removedPile`; exposed read-only as `DrawPile`, `Hand`, `DiscardPile`, `RemovedPile` (lines 38-41), with counts `DrawCount`, `HandCount`, `DiscardCount`, `RemovedCount` (lines 42-45). Confirms 4 piles: draw / hand / discard / removed.

**DeckState<T> public API** (`Source/CardCore/Runtime/DeckState.cs`):
- `Draw(int count)` — line 47
- `DiscardFromHand(TCard card)` → bool — line 67
- `DiscardHand()` — line 78
- `DiscardHandExcept(Func<TCard,bool> keep)` → int — line 89 (turn-end retain gate, comment references "T5-2")
- `ReturnHandToDrawExcept(TCard excluded)` → int — line 116
- `InjectIntoDrawPile(TCard card)` — line 136
- `InjectIntoHand(TCard card)` — line 147
- `TryRecoverFromRemovedPile(TCard card)` → bool — line 162
- `PermanentRemoveFromHand(TCard card)` → bool — line 173
- `PermanentRemoveAnywhere(TCard card)` → bool — line 191
- `TryReplaceAnywhere(TCard existing, TCard replacement)` → bool — line 213
- `ShuffleDrawPile()` — line 243
- `ReshuffleDiscardIntoDraw()` — line 248
- `static Action<IList<TCard>> CreateSeededShuffle(int seed)` — line 266
- `static Action<IList<TCard>> CreateSeededShuffle(Random rng)` — line 275
- private `CreateShuffle(Random rng)` — Fisher–Yates, line 287-297

**Shuffle RNG / seed streams** — verified in `Source/Map/Runtime/RunSeedStreams.cs`:
```
MonsterPlacement = 0, Traps = 1, Chests = 2, Services = 3, SpawnHpVariance = 3,
MonsterAttackPattern = 4, CombatJudgement = 5, BossProps = 6, Rewards = 7,
MovementDeckShuffle = 8, ActionDeckShuffle = 9
```
i.e. **10 numbered stream slots (0–9)**, of which movement/action deck shuffles use stream numbers **8 and 9** respectively (comment: "이동덱 셔플. 행동덱과 갈라 둔다" / "행동덱 셔플"), confirmed consumed in `Source/Combat/Runtime/CombatState.cs:7318-7333` (`DeckShuffleFor`): `movementShuffleRng ??= CreateStreamRandom(RunSeedStreams.MovementDeckShuffle)` / `actionShuffleRng ??= CreateStreamRandom(RunSeedStreams.ActionDeckShuffle)`, feeding `CardDeckState.CreateSeededShuffle(rng)`. `Derive(int runSeed, int stream)` (last method) calls `PlacementRandomizer.DeriveSeed`.

---

## D2. Hand rules

Hand-size fields: `Source/Combat/Runtime/CombatConfig.cs:53-54` — `public int MovementHandSize { get; }`, `public int ActionHandSize { get; }`; constructor defaults `Source/Combat/Runtime/CombatConfig.cs:16-17`:
```
int movementHandSize = 1,
int actionHandSize = 5,
```
clamped in body (lines 33-34): `MovementHandSize = movementHandSize <= 0 ? 1 : movementHandSize; ActionHandSize = actionHandSize <= 0 ? 5 : actionHandSize;`

**`CombatConfig.Default`** (`Source/Combat/Runtime/CombatConfig.cs:89`) explicitly passes `movementHandSize=1, actionHandSize=5`:
```
public static CombatConfig Default => new CombatConfig(80, 30, 2, 1, 4, 4, 6, 1, 5, 4, 1, 5, 7, 4);
```
So the code-level default is **1 (movement) / 5 (action)**, not literally "2/5" as your prompt assumed — verified, not asserted. However the actual shipped Unity scene inspector default differs: `Source/Combat/Unity/MapCombatController.cs:119-120` — `[SerializeField] private int movementHandSize = 3;` / `actionHandSize = 5;` (clamped `Max(1,...)` at lines 958-959). So the effective in-game movement hand size is scene-config-driven (3 in the inspector default), while the pure-code fallback (`CombatConfig.Default`) is 1. Action hand size is 5 in both places.

**Retain-on-turn-end**: exists as `CardBehavior.RetainOnTurnEnd` (virtual bool, default `false`) — `Source/Combat/Runtime/Cards/CardBehavior.cs:67`. Consumed by `CombatState.IsRetainedOnTurnEnd` (line 6818) and `CountRetainedInHand` (line 6812-6815), which feed `DrawNewTurnHands`.

**`DrawNewTurnHands` (`CombatState.cs:6824-6836`)**, full body with line numbers:
```
6821
6822        // 손패는 두 벌(이동/행동)이고 유물도 축을 둘로 나눠 가진다 — "카드를 더 뽑는다"가 어느 덱을
6823        // 가리키는지 저작으로 정할 수 있어야 정찰형 유물과 기동형 유물이 갈린다.
6824        private void DrawNewTurnHands()
6825        {
6826            // 유지 카드는 정원 밖이다(2026-09-02 #6 규칙 변경) — 목표를 「정원 + 유지분」으로 올려
6827            // 잡으므로, 유지가 없으면 예전과 정확히 같은 수를 뽑는다.
6828            MovementDeck.Draw(Math.Max(0,
6829                GetEffectiveMovementHandSize() + CountRetainedInHand(MovementDeck) - MovementDeck.HandCount));
6830            DrawActionCards(Math.Max(0,
6831                GetEffectiveActionHandSize() + CountRetainedInHand(ActionDeck) - ActionDeck.HandCount));
6832            // 🔴페이즈 전이 훑기만으로는 새 손패를 놓친다 — 턴 시작 드로우는 PlayerMovement로 넘어간
6833            // <b>뒤에</b> 일어나므로, 이동 페이즈에서 뽑자마자 쓴 카드는 다음 훑기 때 이미 손에 없다.
6834            SweepCodexSightings();
6835            PlayerHandsDrawn?.Invoke();
6836        }
```
`GetEffectiveMovementHandSize()` (line 6863) = `Config.MovementHandSize + itemBonus`; `GetEffectiveActionHandSize()` (line 6868) = `Config.ActionHandSize + itemBonus`. So retained (kept) cards are outside the target hand size ("정원 밖") — the draw target is `handSize + retainedCount - currentHandCount`, meaning retained cards do not reduce next turn's draw. `DrawNewTurnHands` is called at combat start construction (`CombatState.cs:184`, inside `drawOpeningHands` branch) and again at line 3120 (turn-start).

---

## D3. `Source/Combat/Runtime/Cards/CardBehavior.cs` — abstract class

`public abstract class CardBehavior` — line 19. Declarations and hooks (all verified with line numbers from that file):

**Declarations (non-hook properties)**:
- `abstract string Id { get; }` — line 32
- `virtual IReadOnlyList<CardBehaviorMetadata.PostAction> PostActions` — line 36
- `virtual string AdditionalCost` — line 42
- `virtual IReadOnlyList<CardBehaviorMetadata.ChoiceOption> Choices` — line 45
- `virtual bool AmountFollowsDuration` — line 51
- `virtual int ChoiceDrawCount(CardDefinition card)` — line 54
- `virtual CardDisposal Disposal` — line 61 (default `CardDisposal.Discard`)
- `virtual bool RetainOnTurnEnd` — line 67 (default `false`)
- `virtual bool UsableWhileStunned` — line 73 (default `false`)
- `virtual IReadOnlyList<string> Keywords` — line 80
- `bool HasSelfTargetedChoice` (non-virtual, derived) — line 83

**Rule hooks** (also enumerated in `RuleHookNames`, lines 21-27):
- `virtual bool TryResolveMoveDestination(CombatState state, CardDefinition card, int effectiveRange, HexCoord requested, out HexCoord destination, out string failureReason)` — line 86
- `virtual void ApplyAfterMoveResolved(CombatState state, CardDefinition card)` — line 94
- `virtual bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)` — line 103
- `virtual bool HasUtilityEffect(CombatState state, CardDefinition card)` — line 110
- `virtual bool TryApplyUtility(CombatState state, CardDefinition card)` — line 116
- `virtual void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)` — line 122
- `virtual int GetAttackDamage(CombatState state, CardDefinition card, int attackBonus, HexCoord target)` — line 127
- `virtual CardDisposal DisposeAfterPlay(CombatState state, CardDefinition card)` — line 136
- `virtual CombatState.CardRestriction GetRestriction(CombatState state, CardDefinition card)` — line 146

**Upgrade / meta (not counted in HasCustomRules per comment line 26)**:
- `virtual CardDefinition Upgrade(CardDefinition card, int level)` — line 158 (default returns `null`)
- `bool CanUpgrade(CardDefinition card)` (non-virtual, derived from `Upgrade`) — line 164
- `bool HasCustomRules` (reflection-based readout, non-virtual) — line 173-186

**CardDisposal enum** (lines 190-202): `Discard` (default, reshuffled), `Exile` (removed pile, never returns), `HandledByRule` (rule already discarded the whole hand, e.g. U01 — single dispatch point does not touch hand).

**Base classes** (all thin, no members beyond declared abstract, lines 208-254):
- `internal sealed class UnregisteredCardBehavior : CardBehavior` — line 208 (fixture fallback; `Id => string.Empty`)
- `public abstract class BasicAttackCard : CardBehavior` — line 214 (empty body)
- `public abstract class BasicMoveCard : CardBehavior` — line 219
- `public abstract class BasicBlockCard : CardBehavior` — line 224
- `public abstract class FieldObjectCard : CardBehavior` — line 233, adds `abstract CardFieldObjectKind FieldKind { get; }` (line 235)
- `public abstract class ScoutCard : CardBehavior` — line 239
- `public abstract class UtilityCard : CardBehavior` — line 244
- `public abstract class StatusCard : CardBehavior` — line 252

**Stateless single instance per card kind**: confirmed. Class doc comment (line 11): "인스턴스가 아니라 종류당 하나이며 상태를 갖지 않는다" (one instance per *kind*, not per card instance, stateless). `CardBehaviorRegistry.ById` (`CardBehaviorRegistry.cs:14`) is a `Dictionary<string, CardBehavior>` built once via `Build()`/`CreateAll()` — one behavior object per card id, shared across all instances/copies of that card in a deck.

---

## D4. `CardBehaviorRegistry.cs` and `CardBehaviorRegistry.Cards.cs`

**`Resolve` signature** — `Source/Combat/Runtime/Cards/CardBehaviorRegistry.cs:43`:
```csharp
public static CardBehavior Resolve(CardDefinition card)
{
    return card != null && TryGet(card.Id, out var behavior) ? behavior : Unregistered;
}
```
(Never throws — falls back to `Unregistered` sentinel for non-catalog test fixtures.) Also present: `TryGet(string cardId, out CardBehavior behavior)` (line 18), `Get(string cardId)` (line 29, throws `KeyNotFoundException` if missing), `RegisteredIds` (line 16), `Build()` (line 48).

**Registered card classes**: `Source/Combat/Runtime/Cards/CardBehaviorRegistry.Cards.cs` is 71 lines total; contains **59** `yield return new ...()` registration lines (`grep -c "yield return new"` = 59), one per card, matching the class-file count.

**3 example registration lines** (from file, lines 12-14):
```
yield return new M01_Move1Hex();
yield return new M02_Move2Hex();
yield return new M03_Move3Hex();
```

**Card class file count**: `find Source/Combat/Runtime/Cards -iname "*.cs"` excluding `CardBehavior.cs`, `CardBehaviorRegistry*.cs`, `CardIds.cs`, `CardUpgrades.cs` = **59 files**, matching the 59 registrations exactly.

**Naming scheme**: `<TypePrefix><NN>_<PascalCaseName>.cs`, one class per file, organized into type subfolders:
- `Attack/A00_BasicStrike.cs` … `A14_Intimidate.cs` (15 files, A00–A14)
- `Move/M01_Move1Hex.cs` … `M08_FullSprint.cs` (8 files, M01–M08)
- `Defend/D00_BasicBlock.cs` … `D07_FullyPrepared.cs` (8 files, D00–D07)
- `Field/F01_Firebomb.cs` … `F05_BounceBomb.cs` (5 files, F01–F05)
- `Scout/S00_BasicScout.cs` … `S06_WeakSpot.cs` (7 files, S00–S06)
- `Status/X01_FineDust.cs` … `X12_VengefulGhost.cs` (12 files, X01–X12)
- `Utility/U01_Redraw.cs` … `U04_Torch.cs` (4 files, U01–U04)

15+8+8+5+7+12+4 = 59, confirmed. This matches the "59장 전수표" (59-card master table) referenced in the Plastic report `04-card-map.html`.

---

## D5. `CardBehaviorRegistry.Resolve` call sites

`grep -n "CardBehaviorRegistry.Resolve" -R Source` — **17 call sites**, all in `Source/Combat/Runtime/CombatState.cs` (15) except two:

| File:Line | Enclosing method (nearest containing method) |
|---|---|
| `CombatState.cs:1236` | move-resolution path (calls `.TryResolveMoveDestination`) |
| `CombatState.cs:1306` | move-resolution path (calls `.ApplyAfterMoveResolved`) |
| `CombatState.cs:1392` | move-resolution path (calls `.ApplyAfterMoveResolved`) |
| `CombatState.cs:2379` | defend path (calls `.TryApplyDefend`) |
| `CombatState.cs:2435` | scout path (calls `.ApplyAfterScoutReveal`) |
| `CombatState.cs:2634` | utility path (calls `.HasUtilityEffect`) |
| `CombatState.cs:2641` | utility path (calls `.TryApplyUtility`) |
| `CombatState.cs:4839` | post-action accessor (returns `.PostActions`) |
| `CombatState.cs:5105` | attack-damage resolution (calls `.GetAttackDamage`) |
| `CombatState.cs:5732` | restriction accessor (calls `.GetRestriction`) |
| `CombatState.cs:6181` | `AdditionalCost` accessor |
| `CombatState.cs:6186` | `Choices` accessor |
| `CombatState.cs:6191` | `ChoiceDrawCount` accessor |
| `CombatState.cs:6206` | `ConsumePlayedCard` (calls `.DisposeAfterPlay`) |
| `CombatState.cs:6819` | `IsRetainedOnTurnEnd` (calls `.RetainOnTurnEnd`) |
| `CombatState.Refine.cs:54` | (calls `.CanUpgrade`) |
| `Source/Combat/Runtime/Cards/CardUpgrades.cs:23` | `CardUpgrades.Resolve` (calls `.Upgrade`) |

**`ConsumePlayedCard`** — `Source/Combat/Runtime/CombatState.cs:6199-6218`:
```
6199        private void ConsumePlayedCard(CardDeckState deck, CardDefinition card)
6200        {
6201            if (card == null)
6202            {
6203                return;
6204            }
6205
6206            switch (CardBehaviorRegistry.Resolve(card).DisposeAfterPlay(this, card))
6207            {
6208                case CardDisposal.Exile:
6209                    deck.PermanentRemoveFromHand(card);
6210                    return;
6211                case CardDisposal.HandledByRule:
6212                    // 규칙이 손패 전체를 버렸다(U01). 재드로우로 같은 카드가 다시 손에 왔다면 그것은 새로 뽑은 손패다 — 건드리지 않는다.
6213                    return;
6214                default:
6215                    deck.DiscardFromHand(card);
6216                    return;
6217            }
6218        }
```
Signature: `private void ConsumePlayedCard(CardDeckState deck, CardDefinition card)`, line 6199. It is the **single dispatch point** for post-play disposal (doc comment at line 133-135 in `CardBehavior.cs` and inline comment at `CombatState.cs:2640`): it asks the card's behavior for `DisposeAfterPlay`, and switches on `CardDisposal`: `Exile` → `deck.PermanentRemoveFromHand(card)` (→ removed pile), `HandledByRule` → no-op (rule already handled hand mutation, e.g. U01 redraw), default (`Discard`) → `deck.DiscardFromHand(card)` (→ discard pile). Called from 8 call sites across `CombatState.cs` (lines 1325, 1405, 1866, 2017, 2090, 2115, 2124, 2384, 2619, 2642, 6173 — passing either `MovementDeck` or `ActionDeck`).

---

## D6. Verbatim source

**`Source/Combat/Runtime/Cards/Attack/A01_Sweep.cs`** (full file, 14 lines):
```
 1	using SeoulPlayup.CardCore;
 2	
 3	namespace SeoulPlayup.Combat.Runtime.Cards
 4	{
 5	    /// <summary>A01 휘둘러치기 — 플레이어 주변 {Shape} 내의 적 모두에게 피해 {Damage}를 줍니다. (옛 behaviorId `attack.damage`)</summary>
 6	    public sealed class A01_Sweep : BasicAttackCard
 7	    {
 8	        public override string Id => "A01";
 9	
10	        /// <summary>연마(옛 card_upgrades.csv): 자기 주변 blast라 형상 유지·피해 3→5.</summary>
11	        public override CardDefinition Upgrade(CardDefinition card, int level) => card.With(amount: 5);
12	    }
13	}
14	
```
(No override of `Disposal`, `RetainOnTurnEnd`, `Keywords`, or any rule hook — A01 is entirely default behavior + amount-only upgrade 3→5.)

**`CardBehavior` hook signature lines** (verbatim, `Source/Combat/Runtime/Cards/CardBehavior.cs`):
```
 86	        public virtual bool TryResolveMoveDestination(CombatState state, CardDefinition card, int effectiveRange, HexCoord requested, out HexCoord destination, out string failureReason)
 94	        public virtual void ApplyAfterMoveResolved(CombatState state, CardDefinition card)
103	        public virtual bool TryApplyDefend(CombatState state, CardDefinition card, out int blockGranted)
110	        public virtual bool HasUtilityEffect(CombatState state, CardDefinition card)
116	        public virtual bool TryApplyUtility(CombatState state, CardDefinition card)
122	        public virtual void ApplyAfterScoutReveal(CombatState state, CardDefinition card, HexCoord target, int revealRadius)
127	        public virtual int GetAttackDamage(CombatState state, CardDefinition card, int attackBonus, HexCoord target)
136	        public virtual CardDisposal DisposeAfterPlay(CombatState state, CardDefinition card)
146	        public virtual CombatState.CardRestriction GetRestriction(CombatState state, CardDefinition card)
158	        public virtual CardDefinition Upgrade(CardDefinition card, int level)
```

**`DrawNewTurnHands` full body** — already reproduced verbatim with line numbers in D2 (`CombatState.cs:6824-6836`).
