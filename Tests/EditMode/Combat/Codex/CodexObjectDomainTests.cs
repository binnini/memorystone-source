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
using SeoulPlayup.Map.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode.Codex
{
    /// <summary>
    /// 오브젝트 도메인(P6)의 계약. 세 가지를 잰다 — <b>저작이 실물과 맞는가</b>(CSV의
    /// <c>catalogRef</c>가 카탈로그에 있는가), <b>경계를 지키는가</b>(§8-1 = Q44: 장판은 여기 없다),
    /// <b>신호가 도감 항목과 같은 문자열인가</b>(P3 §5-5 지뢰).
    /// </summary>
    public sealed class CodexObjectDomainTests
    {
        private static readonly Color Accent = Color.white;

        private sealed class RecordingSink : ICodexSightingSink
        {
            private readonly Dictionary<string, HashSet<string>> seen = new Dictionary<string, HashSet<string>>();

            public void MarkSeen(string domainId, string entryId)
            {
                if (!seen.TryGetValue(domainId, out var set))
                {
                    set = new HashSet<string>();
                    seen[domainId] = set;
                }

                set.Add(entryId);
            }

            public IReadOnlyCollection<string> For(string domainId) =>
                seen.TryGetValue(domainId, out var set) ? set : (IReadOnlyCollection<string>)new string[0];
        }

        [TearDown]
        public void ClearRegisteredCatalog() => CodexObjectCatalogSource.ResetForTests();

        // ── 저작 ↔ 실물 ─────────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void EveryAuthoredCatalogRefResolvesToARealPrefab()
        {
            // 🔴 이것이 이 도메인의 가장 조용한 실패다 — catalogRef가 오타여도 도감은 이름만 띄우고
            //    멀쩡해 보인다. 그림과 수치 줄만 조용히 빠진다.
            var catalogSet = CodexShippingDomains.LoadMapObjectCatalogSet();

            foreach (var entry in CodexShippingDomains.LoadMapObjectCodexCatalog().Entries)
            {
                Assert.That(
                    catalogSet.TryGetEntry(entry.CatalogRef, out var catalogEntry), Is.True,
                    $"'{entry.Id}'의 catalogRef '{entry.CatalogRef}'가 오브젝트 카탈로그에 없다.");
                Assert.That(
                    catalogEntry.Prefab, Is.Not.Null,
                    $"'{entry.Id}'의 프리팹 참조가 비었다 — 썸네일을 구울 수 없다.");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void NoEntryShipsWithoutAnAuthoredKoreanNameAndSentence()
        {
            // 계획 §10.4의 취지 그대로 — 지어낸 임시 이름이 화면에 뜨는 구간을 만들지 않는다.
            foreach (var entry in new CodexObjectDomain(
                         CodexShippingDomains.LoadMapObjectCodexCatalog(),
                         Accent,
                         CodexShippingDomains.LoadMapObjectCatalogSet()).Entries)
            {
                Assert.That(entry.DisplayName, Is.Not.Empty);
                Assert.That(
                    entry.DisplayName, Is.Not.EqualTo(entry.Id),
                    $"'{entry.Id}'가 이름 대신 id를 띄우고 있다.");
                Assert.That(entry.Description, Is.Not.Empty, $"'{entry.Id}'에 설명 문장이 없다.");
            }
        }

        // ── 경계(§8-1 = Q44) ────────────────────────────────────────────────

        [Test]
        [Category("ShippingData")]
        public void FieldEffectPlatesStayInTheCardDomainAndNeverAppearHere()
        {
            // 🔴🔴 P6의 핵심 판정. 피해·회복 장판은 맵 저작물이 아니라 cards.csv의 behaviorId에서
            //     나온다 — 오브젝트 도메인에 들어오면 카드 도감과 같은 것이 두 번 뜬다.
            var objectIds = new HashSet<string>(
                new CodexObjectDomain(
                    CodexShippingDomains.LoadMapObjectCodexCatalog(),
                    Accent,
                    CodexShippingDomains.LoadMapObjectCatalogSet()).Entries.Select(entry => entry.Id));

            var fieldCardIds = CodexShippingDomains.LoadCardCatalog().Entries
                .Where(entry => entry.FieldObjectKind != CardFieldObjectKind.None)
                .Select(entry => entry.Id)
                .ToArray();

            Assert.That(fieldCardIds, Is.Not.Empty, "장판 카드가 하나도 없다 — 이 시험의 전제가 무너졌다.");
            foreach (var cardId in fieldCardIds)
            {
                Assert.That(
                    objectIds, Does.Not.Contain(cardId),
                    $"장판 '{cardId}'가 오브젝트 도메인에 있다 — 카드 도감과 중복이다(§8-1 Q44).");
            }
        }

        [Test]
        [Category("ShippingData")]
        public void DetailRowsNeverTeachTheUnimplementedVisionBlockingRule()
        {
            // blocksVision은 649개에 저작돼 있지만 소비자가 0이고 영구 미구현이다
            // (DEC-2026-07-28-03). 도감 상세에 쓰면 없는 규칙을 가르치게 된다.
            foreach (var entry in new CodexObjectDomain(
                         CodexShippingDomains.LoadMapObjectCodexCatalog(),
                         Accent,
                         CodexShippingDomains.LoadMapObjectCatalogSet()).Entries)
            {
                foreach (var row in entry.DetailRows)
                {
                    Assert.That(
                        row.Label + row.Value, Does.Not.Contain("시야"),
                        $"'{entry.Id}' 상세가 시야 차단을 말하고 있다 — 그 규칙은 구현돼 있지 않다.");
                }
            }
        }

        // ── 해금 신호 ───────────────────────────────────────────────────────

        [Test]
        public void AVisibleObjectSignalsTheStableIdNotTheCatalogRef()
        {
            const string CatalogRef = "treasureChest_tmp";
            const string StableId = "reward_gacha";
            CodexObjectCatalogSource.Register(SingleEntryCatalog(StableId, CatalogRef));

            var sink = new RecordingSink();
            NewState(NearbyObject(CatalogRef, new HexCoord(1, 0))).CodexSightings = sink;

            Assert.That(
                sink.For(CodexDomainIds.Object), Does.Contain(StableId),
                "보이는 오브젝트가 신호를 못 냈다.");
            Assert.That(
                sink.For(CodexDomainIds.Object), Does.Not.Contain(CatalogRef),
                "임시 모델 이름이 저장 키로 새면 정식 모델이 왔을 때 진행도가 초기화된다.");
        }

        [Test]
        public void ObjectsHiddenByFogStayLocked()
        {
            // 🔴 표시해 버리면 도감이 정찰을 대신해 준다 — 가 보지 않은 구역의 상점을 미리 알게 된다.
            CodexObjectCatalogSource.Register(SingleEntryCatalog("reward_gacha", "treasureChest_tmp"));

            var sink = new RecordingSink();
            NewState(NearbyObject("treasureChest_tmp", new HexCoord(9, 0)), playerVisionRange: 1)
                .CodexSightings = sink;

            Assert.That(sink.For(CodexDomainIds.Object), Is.Empty);
        }

        [Test]
        public void MonsterSpawnRefsAreNotMistakenForObjects()
        {
            // ⚠️ 맵 소스는 몬스터 스폰도 같은 objectRef 필드에 담는다 — 거기 들어 있는 것은 M001이다.
            CodexObjectCatalogSource.Register(SingleEntryCatalog("reward_gacha", "treasureChest_tmp"));

            var sink = new RecordingSink();
            NewState(new HexMapObjectData(
                objectId: "spawn-1",
                objectType: "MonsterSpawn",
                objectRef: "M001",
                coord: new HexCoord(1, 0))).CodexSightings = sink;

            Assert.That(sink.For(CodexDomainIds.Object), Is.Empty);
        }

        [Test]
        public void AnObjectIsSeenWhenAnyFootprintCellIsRevealedNotOnlyItsAnchor()
        {
            // 롯데타워처럼 여러 칸을 밟는 건물은 anchor가 안개인 채로 몸통만 보이는 일이 흔하다.
            CodexObjectCatalogSource.Register(SingleEntryCatalog("lotte_tower", "lottetower_50000"));

            var sink = new RecordingSink();
            NewState(new HexMapObjectData(
                    objectId: "tower-1",
                    objectType: "Building",
                    objectRef: "lottetower_50000",
                    // anchor는 시야 밖(3칸), 발자국 한 칸이 시야 안(1칸)으로 들어온다.
                    coord: new HexCoord(3, 0),
                    footprintOffsets: new[] { new HexCoord(0, 0), new HexCoord(-2, 0) }),
                    playerVisionRange: 1)
                .CodexSightings = sink;

            Assert.That(sink.For(CodexDomainIds.Object), Does.Contain("lotte_tower"));
        }

        [Test]
        public void EntriesHiddenFromTheCatalogNeverSignalAndNeverGetACell()
        {
            var hidden = new CodexObjectCatalog(new[]
            {
                new CodexObjectEntry("fence", "fence01", CodexObjectKind.Landmark, "울타리", "장식.", "", false),
            });
            CodexObjectCatalogSource.Register(hidden);

            var sink = new RecordingSink();
            NewState(NearbyObject("fence01", new HexCoord(1, 0))).CodexSightings = sink;

            Assert.That(sink.For(CodexDomainIds.Object), Is.Empty);
            Assert.That(new CodexObjectDomain(hidden, Accent).Entries, Is.Empty);
        }

        [Test]
        [Category("ShippingData")]
        public void EveryAuthoredIdIsAnIdTheDomainCanShow()
        {
            var authored = CodexShippingDomains.LoadMapObjectCodexCatalog();
            var domainIds = new HashSet<string>(
                new CodexObjectDomain(authored, Accent, CodexShippingDomains.LoadMapObjectCatalogSet())
                    .Entries.Select(entry => entry.Id));

            foreach (var entry in authored.Entries.Where(entry => entry.VisibleInCatalog))
            {
                Assert.That(domainIds, Does.Contain(entry.Id));
            }
        }

        // ── CSV 계약 ────────────────────────────────────────────────────────

        [Test]
        public void DuplicateIdsAreRejectedSoTwoCellsCannotShareProgress()
        {
            Assert.That(
                () => CodexObjectCatalogCsv.ConvertText(
                    "id,catalogRef,kind,displayNameKo,descriptionKo,thumbnailRef,visibleInCatalog\n"
                    + "a,refA,interactive,가,설명.,,TRUE\n"
                    + "a,refB,interactive,나,설명.,,TRUE\n"),
                Throws.ArgumentException);
        }

        [Test]
        public void DuplicateCatalogRefsAreRejectedSoTwoCellsCannotShareOnePrefab()
        {
            Assert.That(
                () => CodexObjectCatalogCsv.ConvertText(
                    "id,catalogRef,kind,displayNameKo,descriptionKo,thumbnailRef,visibleInCatalog\n"
                    + "a,ref,interactive,가,설명.,,TRUE\n"
                    + "b,ref,interactive,나,설명.,,TRUE\n"),
                Throws.ArgumentException);
        }

        [Test]
        public void ARowWithoutAKoreanNameIsRejected()
        {
            Assert.That(
                () => CodexObjectCatalogCsv.ConvertText(
                    "id,catalogRef,kind,displayNameKo,descriptionKo,thumbnailRef,visibleInCatalog\n"
                    + "a,ref,interactive,,설명.,,TRUE\n"),
                Throws.ArgumentException);
        }

        [Test]
        public void AnUnknownKindIsRejectedRatherThanSilentlyDefaulted()
        {
            Assert.That(
                () => CodexObjectCatalogCsv.ConvertText(
                    "id,catalogRef,kind,displayNameKo,descriptionKo,thumbnailRef,visibleInCatalog\n"
                    + "a,ref,fieldEffect,가,설명.,,TRUE\n"),
                Throws.ArgumentException,
                "장판(fieldEffect)은 이 도메인의 갈래가 아니다 — 조용히 통과하면 경계가 무너진다.");
        }

        [Test]
        public void AnEmptyThumbnailRefFallsBackToTheStableId()
        {
            var entry = new CodexObjectEntry("reward_gacha", "treasureChest_tmp", CodexObjectKind.Interactive,
                "보상뽑기", "설명.", thumbnailRef: "", visibleInCatalog: true);

            Assert.That(entry.ResolveThumbnailKey(), Is.EqualTo("reward_gacha"));
        }

        [Test]
        [Category("ShippingData")]
        public void TheShippingCsvParsesAndCarriesBothKinds()
        {
            var entries = CodexShippingDomains.LoadMapObjectCodexCatalog().Entries;

            Assert.That(entries, Is.Not.Empty);
            Assert.That(
                entries.Any(entry => entry.Kind == CodexObjectKind.Interactive), Is.True,
                "상호작용 오브젝트가 하나도 없다.");
            Assert.That(
                entries.Any(entry => entry.Kind == CodexObjectKind.Landmark), Is.True,
                "랜드마크가 하나도 없다(Q48-B 확정).");
        }

        // ── 픽스처 ──────────────────────────────────────────────────────────

        private static CodexObjectCatalog SingleEntryCatalog(string id, string catalogRef) =>
            new CodexObjectCatalog(new[]
            {
                new CodexObjectEntry(id, catalogRef, CodexObjectKind.Interactive, "시험용", "시험용 설명.", "", true),
            });

        private static HexMapObjectData NearbyObject(string objectRef, HexCoord coord) =>
            new HexMapObjectData(
                objectId: $"obj-{objectRef}",
                objectType: "TreasureChest",
                objectRef: objectRef,
                coord: coord);

        private static CombatState NewState(HexMapObjectData mapObject, int playerVisionRange = 7)
        {
            var map = new HexMapData(TestMaps.LineCells(12), objectRefs: new[] { mapObject });
            return new CombatState(
                map,
                new HexCoord(0, 0),
                System.Array.Empty<MonsterConfig>(),
                TestCombatConfigs.Standard(playerVisionRange: playerVisionRange),
                cardCatalog: LoadCardCatalog());
        }

        private static CardCatalogDefinition LoadCardCatalog()
        {
            var asset = ScriptableObject.CreateInstance<CardCatalogAsset>();
            try
            {
                asset.SetRows(CardCatalogAsset.ParseCsvText(File.ReadAllText(CombatCsvPaths.CardsCsv, Encoding.UTF8)));
                return asset.ToCardCatalogDefinition(CombatConfig.Default);
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
