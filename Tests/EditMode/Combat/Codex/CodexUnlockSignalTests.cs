using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode.Codex
{
    /// <summary>
    /// 해금 신호와 도감 항목이 <b>같은 문자열</b>을 쓰는지(<c>docs/codex-plan.md</c> P3 완료 조건 5,
    /// 인계문 §5-5 지뢰).
    /// <para>
    /// 🔴<b>이것이 이 트랙에서 가장 조용한 실패 방식이다.</b> 신호가 <c>InstanceId</c>나 런타임
    /// <c>TrapId</c> 같은 <b>런마다 달라지는 id</b>를 실어 보내면 코드는 아무 데서도 안 터지고,
    /// 저장 파일도 정상으로 보이고, 화면만 영원히 잠겨 있다. 그래서 도메인이 항목에 쓰는 id와
    /// 전투가 보내는 id를 <b>실제 전투를 돌려</b> 맞대어 본다.
    /// </para>
    /// </summary>
    public sealed class CodexUnlockSignalTests
    {
        private static readonly Color Accent = Color.white;

        /// <summary>어느 도메인에 어떤 id가 들어왔는지 그대로 받아 적는 감시자.</summary>
        private sealed class RecordingSink : ICodexSightingSink
        {
            private readonly Dictionary<string, HashSet<string>> seen =
                new Dictionary<string, HashSet<string>>();

            public void MarkSeen(string domainId, string entryId)
            {
                if (string.IsNullOrWhiteSpace(domainId) || string.IsNullOrWhiteSpace(entryId))
                {
                    return;
                }

                if (!seen.TryGetValue(domainId, out var set))
                {
                    set = new HashSet<string>();
                    seen[domainId] = set;
                }

                set.Add(entryId);
            }

            public IReadOnlyCollection<string> For(string domainId)
            {
                return seen.TryGetValue(domainId, out var set) ? set : (IReadOnlyCollection<string>)new string[0];
            }
        }

        // ── 도메인 id는 한 곳에서만 정의된다 ────────────────────────────────

        [Test]
        public void EveryDomainUsesTheSharedIdConstantSoSignalsCannotDrift()
        {
            // 도메인 클래스가 값을 따로 적어 두면 전투가 보내는 문자열과 조용히 갈라진다 —
            // 값을 비교하는 것이 아니라 "같은 상수를 가리키는가"를 못 박는다.
            Assert.That(CodexCardDomain.DomainId, Is.EqualTo(CodexDomainIds.Card));
            Assert.That(CodexMonsterDomain.DomainId, Is.EqualTo(CodexDomainIds.Monster));
            Assert.That(CodexRelicDomain.DomainId, Is.EqualTo(CodexDomainIds.Relic));
            Assert.That(CodexTrapDomain.DomainId, Is.EqualTo(CodexDomainIds.Trap));
            Assert.That(CodexConsumableItemDomain.DomainId, Is.EqualTo(CodexDomainIds.Consumable));
            Assert.That(CodexStatusEffectDomain.DomainId, Is.EqualTo(CodexDomainIds.StatusEffect));
            Assert.That(CodexAttackShapeDomain.DomainId, Is.EqualTo(CodexDomainIds.AttackShape));
            Assert.That(CodexObjectDomain.DomainId, Is.EqualTo(CodexDomainIds.Object));
        }

        [Test]
        public void DomainIdsAreDistinct()
        {
            var ids = new[]
            {
                CodexDomainIds.Card, CodexDomainIds.Monster, CodexDomainIds.Relic, CodexDomainIds.Trap,
                CodexDomainIds.Consumable, CodexDomainIds.StatusEffect, CodexDomainIds.AttackShape,
                CodexDomainIds.Object
            };

            Assert.That(ids.Distinct().Count(), Is.EqualTo(ids.Length), "도메인 키가 겹치면 두 도감이 한 집합을 쓴다.");
        }

        // ── 카드 ────────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void OpeningHandSignalsCardIdsThatTheCardDomainActuallyHas()
        {
            var catalog = LoadCardCatalog();
            var sink = new RecordingSink();
            var state = NewState(catalog);
            state.CodexSightings = sink;

            var signalled = sink.For(CodexDomainIds.Card);
            Assert.That(signalled, Is.Not.Empty, "개시 손패가 있는데 아무 신호도 안 왔다 — 붙이는 순간의 훑기가 죽었다.");

            var domainIds = new HashSet<string>(new CodexCardDomain(catalog, Accent).Entries.Select(entry => entry.Id));
            foreach (var id in signalled)
            {
                Assert.That(
                    domainIds, Does.Contain(id),
                    $"카드 신호 '{id}'가 도감 항목에 없다. 인스턴스 id(런마다 다름)를 보낸 것이 아닌지 볼 것.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void CardSignalCarriesTheCatalogIdNotTheRuntimeInstanceId()
        {
            var catalog = LoadCardCatalog();
            var sink = new RecordingSink();
            var state = NewState(catalog);
            state.CodexSightings = sink;

            // 인스턴스 id는 카탈로그 id에 소스 태그가 붙은 모양이라 반드시 더 길다.
            foreach (var id in sink.For(CodexDomainIds.Card))
            {
                Assert.That(id, Does.Not.Contain("#"), $"'{id}'는 인스턴스 id로 보인다.");
                Assert.That(id.Trim(), Is.EqualTo(id));
            }
        }

        // ── 유물 ────────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void GrantingARelicSignalsTheIdTheRelicDomainUses()
        {
            var relicCatalog = LoadRelicCatalog();
            var target = relicCatalog.Entries.First();

            var sink = new RecordingSink();
            var state = NewState(LoadCardCatalog());
            state.CodexSightings = sink;

            Assert.That(state.TryGrantPermanentItem(target.Id, out var reason), Is.True, reason);

            var domainIds = new HashSet<string>(new CodexRelicDomain(relicCatalog, Accent).Entries.Select(entry => entry.Id));
            Assert.That(sink.For(CodexDomainIds.Relic), Does.Contain(target.Id));
            Assert.That(domainIds, Does.Contain(target.Id));
        }

        [Test]
        public void RefusedRelicGrantSignalsNothing()
        {
            var sink = new RecordingSink();
            var state = NewState(LoadCardCatalog());
            state.CodexSightings = sink;
            var before = sink.For(CodexDomainIds.Relic).Count;

            Assert.That(state.TryGrantPermanentItem("no-such-relic", out _), Is.False);
            Assert.That(
                sink.For(CodexDomainIds.Relic).Count, Is.EqualTo(before),
                "받지 못한 것이 도감에 열리면 안 된다 — 관문을 통과한 뒤에 표시해야 하는 이유다.");
        }

        // ── 소모품 ──────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void AddingABagItemSignalsTheIdTheConsumableDomainUses()
        {
            var consumableCatalog = LoadConsumableCatalog();
            var target = consumableCatalog.Entries.First();

            var sink = new RecordingSink();
            var state = NewState(LoadCardCatalog());
            state.CodexSightings = sink;

            Assert.That(state.TryAddBagItem(target.Id), Is.True);

            var domainIds = new HashSet<string>(
                new CodexConsumableItemDomain(consumableCatalog, Accent).Entries.Select(entry => entry.Id));
            Assert.That(sink.For(CodexDomainIds.Consumable), Does.Contain(target.Id));
            Assert.That(domainIds, Does.Contain(target.Id));
        }

        [Test]
        public void RefusedBagAddSignalsNothing()
        {
            var sink = new RecordingSink();
            var state = NewState(LoadCardCatalog());
            state.CodexSightings = sink;

            Assert.That(state.TryAddBagItem("no-such-item"), Is.False);
            Assert.That(sink.For(CodexDomainIds.Consumable), Is.Empty);
        }

        // ── 함정 ────────────────────────────────────────────────────────────

        [Test]
        public void SteppingOnATrapSignalsItsPresetIdNotItsInstanceId()
        {
            const string PresetId = "spike_pit";
            var trap = new HexTrapData(
                // 🔴이 둘이 다르다는 것이 이 테스트의 전부다 — 판 위의 한 개(TrapId)와 종류(PresetId).
                trapId: "map-trap-0007",
                coord: new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 1) },
                presetId: PresetId);

            var sink = new RecordingSink();
            var state = NewState(LoadCardCatalog(), trapRefs: new[] { trap });
            state.CodexSightings = sink;

            Assert.That(sink.For(CodexDomainIds.Trap), Is.Empty, "밟기 전에는 열리지 않는다.");

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True, state.LastFailureReason);

            Assert.That(sink.For(CodexDomainIds.Trap), Does.Contain(PresetId));
            Assert.That(
                sink.For(CodexDomainIds.Trap), Does.Not.Contain("map-trap-0007"),
                "인스턴스 id를 보내면 도감은 영원히 잠긴다 — 저작 어디에도 그 문자열은 없다.");
        }

        [Test]
        public void RuntimePlacedTrapsWithoutAPresetSignalNothing()
        {
            // 보스가 뿌리는 런타임 함정(§21.5)은 저작 프리셋이 아니라 도감에 자리가 없다.
            var trap = new HexTrapData(
                trapId: "boss-trap-1",
                coord: new HexCoord(1, 0),
                radius: 0,
                effects: new[] { new HexTrapEffectData(HexTrapEffectKind.Damage, 1) });

            var sink = new RecordingSink();
            var state = NewState(LoadCardCatalog(), trapRefs: new[] { trap });
            state.CodexSightings = sink;

            Assert.That(state.TryPlayerMove(new HexCoord(1, 0)), Is.True, state.LastFailureReason);

            Assert.That(sink.For(CodexDomainIds.Trap), Is.Empty);
        }

        [Test]
        [Category("ShippingData")]
        public void EveryAuthoredTrapPresetIdIsAnIdTheTrapDomainCanShow()
        {
            // 배관이 뚫렸어도 저작이 도감에 없는 프리셋을 실어 보내면 신호가 갈 곳이 없다.
            var catalog = LoadTrapCatalog();
            var domainIds = new HashSet<string>(
                new CodexTrapDomain(catalog, null, Accent).Entries.Select(entry => entry.Id));

            foreach (var preset in catalog.Presets)
            {
                Assert.That(domainIds, Does.Contain(preset.PresetId));
            }
        }

        // ── 몬스터 ──────────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void AVisibleMonsterSignalsTheIdTheMonsterDomainUses()
        {
            var monsterCatalog = LoadMonsterCatalog();
            var target = monsterCatalog.Entries.First();

            var sink = new RecordingSink();
            var state = NewState(
                LoadCardCatalog(),
                monsterCatalog: monsterCatalog,
                monsters: new[]
                {
                    new MonsterConfig("m-1", new HexCoord(2, 0), maxHp: 10, definitionId: target.Id)
                });
            state.CodexSightings = sink;

            var domainIds = new HashSet<string>(
                new CodexMonsterDomain(monsterCatalog, Accent).Entries.Select(entry => entry.Id));

            Assert.That(
                sink.For(CodexDomainIds.Monster), Does.Contain(target.Id),
                "시야 안 몬스터가 신호를 못 냈다 — 훑기가 가시 판정을 읽는 자리를 확인할 것.");
            Assert.That(domainIds, Does.Contain(target.Id));
        }

        [Test]
        [Category("ShippingData")]
        public void MonstersHiddenByFogStayLocked()
        {
            var monsterCatalog = LoadMonsterCatalog();
            var target = monsterCatalog.Entries.First();

            var sink = new RecordingSink();
            // 시야 1짜리 플레이어에게서 멀리 떨어뜨린다 — 안개 속의 것은 만난 것이 아니다.
            var state = NewState(
                LoadCardCatalog(),
                monsterCatalog: monsterCatalog,
                monsters: new[]
                {
                    new MonsterConfig("m-far", new HexCoord(8, 0), maxHp: 10, definitionId: target.Id)
                },
                playerVisionRange: 1);
            state.CodexSightings = sink;

            Assert.That(sink.For(CodexDomainIds.Monster), Is.Empty);
        }

        // ── 픽스처 ──────────────────────────────────────────────────────────

        private static CombatState NewState(
            CardCatalogDefinition cardCatalog,
            IEnumerable<HexTrapData> trapRefs = null,
            MonsterCatalogDefinition monsterCatalog = null,
            IEnumerable<MonsterConfig> monsters = null,
            int playerVisionRange = 7)
        {
            var map = new HexMapData(TestMaps.LineCells(12), trapRefs: trapRefs);
            return new CombatState(
                map,
                new HexCoord(0, 0),
                monsters ?? System.Array.Empty<MonsterConfig>(),
                TestCombatConfigs.Standard(playerVisionRange: playerVisionRange),
                cardCatalog: cardCatalog,
                monsterCatalog: monsterCatalog);
        }

        private static CardCatalogDefinition LoadCardCatalog()
        {
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv)));
                return asset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }

        private static RelicCatalogDefinition LoadRelicCatalog() =>
            RelicCatalogCsv.ConvertText(File.ReadAllText(CombatCsvPaths.RelicsCsv, Encoding.UTF8));

        private static ConsumableItemCatalogDefinition LoadConsumableCatalog() =>
            ConsumableItemCatalogCsv.ConvertText(
                File.ReadAllText(CombatCsvPaths.ConsumableItemsCsv, Encoding.UTF8));

        private static Map.Unity.TrapPresetCatalog LoadTrapCatalog()
        {
            var catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<Map.Unity.TrapPresetCatalog>(
                CodexShippingDomains.TrapCatalogPath);
            Assert.That(catalog, Is.Not.Null, "출하 함정 프리셋 카탈로그를 찾지 못했다.");
            return catalog;
        }

        private static MonsterCatalogDefinition LoadMonsterCatalog() =>
            MonsterCatalogCsvConverter.ConvertDirectories(
                    CombatCsvPaths.MonsterDirectory,
                    CombatCsvPaths.PresentationDirectory,
                    "codex-p3-test",
                    "Codex P3 Test")
                .MonsterCatalog;
    }
}
