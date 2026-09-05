#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class EffectVfxAreaSpawnModeTests
    {
        private readonly UnityObjectScope scope = new UnityObjectScope();

        [TearDown]
        public void TearDown()
        {
            scope.Dispose();
        }

        [Test]
        public void EntryDefaultsToNoneWithZeroStagger()
        {
            var prefab = scope.NewGameObject("Default Mode VFX");
            var entry = new EffectVfxCatalog.Entry(EffectKind.Damage, new[] { prefab });

            Assert.That(entry.AreaSpawnMode, Is.EqualTo(EffectVfxAreaSpawnMode.None));
            Assert.That(entry.PerTileDelaySeconds, Is.EqualTo(0f));

            var negative = new EffectVfxCatalog.Entry(
                EffectKind.Damage,
                new[] { prefab },
                areaSpawnMode: EffectVfxAreaSpawnMode.PerTile,
                perTileDelaySeconds: -0.5f);
            Assert.That(negative.PerTileDelaySeconds, Is.EqualTo(0f), "Negative stagger must normalize to 0.");
        }

        [Test]
        public void EffectAreaFootprintPreservesCommittedOrderAndFiltersOffMapCells()
        {
            var map = TestMaps.Line(5);
            var resultEvent = new EffectResultEvent(
                EffectKind.Damage,
                areaCoords: new[]
                {
                    new HexCoord(3, 0),
                    new HexCoord(1, 0),
                    new HexCoord(9, 9),
                    new HexCoord(0, 0)
                });

            var results = new List<HexCoord>();
            EffectAreaFootprint.Resolve(resultEvent, map, results);

            Assert.That(results, Is.EqualTo(new[]
            {
                new HexCoord(3, 0),
                new HexCoord(1, 0),
                new HexCoord(0, 0)
            }), "Committed footprint must keep its authored order (stagger direction rides on it) and drop off-map cells.");
        }

        [Test]
        public void EffectAreaFootprintFallsBackToCenterDiskWithoutAreaCoords()
        {
            var map = TestMaps.Line(5);
            var resultEvent = new EffectResultEvent(
                EffectKind.Damage,
                center: new HexCoord(2, 0),
                radius: 1);

            var results = new List<HexCoord>();
            EffectAreaFootprint.Resolve(resultEvent, map, results);

            Assert.That(results, Is.EquivalentTo(new[]
            {
                new HexCoord(1, 0),
                new HexCoord(2, 0),
                new HexCoord(3, 0)
            }), "Without a committed footprint the Center+Radius disk (map-filtered) is the contract, matching the tile flash.");
        }

        [Test]
        public void EffectAreaFootprintIsEmptyWithoutCoordsAndCenter()
        {
            var results = new List<HexCoord> { new HexCoord(9, 9) };
            EffectAreaFootprint.Resolve(new EffectResultEvent(EffectKind.Damage), TestMaps.Line(3), results);

            Assert.That(results, Is.Empty);
        }

        [Test]
        public void ResolvePerTileSpawnDelayGrowsMonotonicallyFromAuthoredDelay()
        {
            var prefab = scope.NewGameObject("Stagger VFX");
            var entry = new EffectVfxCatalog.Entry(
                EffectKind.Damage,
                new[] { prefab },
                playbackDelaySeconds: 0.2f,
                areaSpawnMode: EffectVfxAreaSpawnMode.PerTile,
                perTileDelaySeconds: 0.15f);

            var delays = Enumerable.Range(0, 4)
                .Select(index => EffectPresentationController.ResolvePerTileSpawnDelay(entry, index))
                .ToArray();

            Assert.That(delays[0], Is.EqualTo(0.2f).Within(0.0001f), "Tile 0 spawns at the cue's authored delay.");
            for (var i = 1; i < delays.Length; i++)
            {
                Assert.That(delays[i] - delays[i - 1], Is.EqualTo(0.15f).Within(0.0001f), "Stagger must grow by perTileDelaySeconds per tile.");
            }
        }

        [Test]
        public void HasPerTileAreaEntryDetectsOnlyPerTileEntries()
        {
            var prefab = scope.NewGameObject("Detect VFX");
            var noneEntry = new EffectVfxCatalog.Entry(EffectKind.Damage, new[] { prefab });
            var perTileEntry = new EffectVfxCatalog.Entry(
                EffectKind.Damage,
                new[] { prefab },
                areaSpawnMode: EffectVfxAreaSpawnMode.PerTile);

            Assert.That(EffectPresentationController.HasPerTileAreaEntry(null), Is.False);
            Assert.That(EffectPresentationController.HasPerTileAreaEntry(new[] { noneEntry }), Is.False);
            Assert.That(EffectPresentationController.HasPerTileAreaEntry(new[] { noneEntry, perTileEntry }), Is.True);
        }

        [Test]
        public void PlayAreaSpawnsOneInstancePerTileForPerTileEntry()
        {
            var presentation = CreatePresentation(out var catalog);
            var prefab = scope.NewGameObject("PerTile VFX");
            catalog.SetEntriesForTests(new EffectVfxCatalog.Entry(
                EffectKind.Damage,
                new[] { prefab },
                sourceRef: "monster.pattern.TEST",
                floatingTextMode: EffectFloatingTextMode.Hide,
                areaSpawnMode: EffectVfxAreaSpawnMode.PerTile));

            var tiles = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(4f, 0f, 0f) };
            presentation.PlayArea(
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", appliedAmount: 1, sourceRef: "monster.pattern.TEST"),
                new Vector3(2f, 0f, 0f),
                tiles,
                Quaternion.identity);

            var spawned = SpawnedVfx(presentation);
            Assert.That(spawned, Has.Count.EqualTo(tiles.Length), "PerTile mode must spawn exactly one instance per footprint tile.");
            Assert.That(spawned.Select(effect => effect.transform.position), Is.EquivalentTo(tiles));
        }

        [Test]
        public void PlayAreaWithoutPerTileEntryKeepsSingleCenterBurst()
        {
            var presentation = CreatePresentation(out var catalog);
            var prefab = scope.NewGameObject("Center Burst VFX");
            catalog.SetEntriesForTests(new EffectVfxCatalog.Entry(
                EffectKind.Damage,
                new[] { prefab },
                sourceRef: "monster.pattern.TEST",
                floatingTextMode: EffectFloatingTextMode.Hide));

            var center = new Vector3(2f, 0f, 0f);
            presentation.PlayArea(
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", appliedAmount: 1, sourceRef: "monster.pattern.TEST"),
                center,
                new[] { new Vector3(0f, 0f, 0f), center, new Vector3(4f, 0f, 0f) },
                Quaternion.identity);

            var spawned = SpawnedVfx(presentation);
            // 개수 고정 유지: 1은 오늘의 저작이 아니라 Default 모드의 정의(중앙 한 방)다.
            Assert.That(spawned, Has.Count.EqualTo(1), "Default entries must keep the pre-mode single center burst (regression guard).");
            Assert.That(spawned[0].transform.position, Is.EqualTo(center));
        }

        [Test]
        public void PlayAreaHybridSpawnsCenterBurstPlusPerTileInstances()
        {
            var presentation = CreatePresentation(out var catalog);
            var centerPrefab = scope.NewGameObject("Hybrid Center VFX");
            var tilePrefab = scope.NewGameObject("Hybrid Tile VFX");
            catalog.SetEntriesForTests(
                new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { centerPrefab },
                    sourceRef: "monster.pattern.TEST",
                    floatingTextMode: EffectFloatingTextMode.Hide),
                new EffectVfxCatalog.Entry(
                    EffectKind.Damage,
                    new[] { tilePrefab },
                    sourceRef: "monster.pattern.TEST",
                    floatingTextMode: EffectFloatingTextMode.Hide,
                    areaSpawnMode: EffectVfxAreaSpawnMode.PerTile));

            var tiles = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f) };
            presentation.PlayArea(
                new EffectResultEvent(EffectKind.Damage, targetUnitId: "player", appliedAmount: 1, sourceRef: "monster.pattern.TEST"),
                new Vector3(1f, 0f, 0f),
                tiles,
                Quaternion.identity);

            var spawned = SpawnedVfx(presentation);
            Assert.That(spawned.Count(effect => effect.name.Contains("Hybrid Center VFX")), Is.EqualTo(1),
                "The non-PerTile entry of a hybrid cue keeps its single center burst.");
            Assert.That(spawned.Count(effect => effect.name.Contains("Hybrid Tile VFX")), Is.EqualTo(tiles.Length),
                "The PerTile entry of a hybrid cue spawns per footprint tile.");
        }

        [Test]
        public void MonsterVfxCsvParsesAreaSpawnModeColumns()
        {
            var vfxCsv =
                "vfxCueId,effectKind,targetFilter,prefabPath,scaleMultiplier,scaleWithRadius,offsetX,offsetY,offsetZ,rotationX,rotationY,rotationZ,lifetimeOverride,sourceRef,matchSourceRefPrefix,spawnAnchor,designerNote,areaSpawnMode,perTileDelaySeconds\n" +
                "V001,Damage,Player,Assets/Foo.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A001,false,Auto,per tile row,PerTile,0.15\n" +
                "V002,Damage,Player,Assets/Bar.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A002,false,Auto,fixture row,None,0\n" +
                "V003,StatusEffectApplied,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A003,false,Auto,fixture row,None,0\n" +
                "V004,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A004,false,Auto,fixture row,None,0\n" +
                "V005,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A005,false,Auto,fixture row,None,0\n" +
                "V006,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A006,false,Auto,fixture row,None,0\n" +
                "V007,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A007,false,Auto,fixture row,None,0\n" +
                "V008,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A008,false,Auto,fixture row,None,0\n" +
                "V009,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A009,false,Auto,fixture row,None,0\n" +
                "V010,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A010,false,Auto,fixture row,None,0\n";

            var source = new MonsterCatalogCsvSource(
                ReadShippingCsv("monster_catalog.csv"),
                ReadShippingCsv("monster_attack_patterns.csv"),
                ReadShippingCsv("monster_pattern_bindings.csv"),
                vfxCsv,
                ReadShippingCsv("combat_sound_cues.csv"));
            var cues = MonsterCatalogCsvConverter.Convert(source).VfxCues;

            var perTile = cues.Single(cue => cue.VfxCueId == "V001");
            Assert.That(perTile.AreaSpawnMode, Is.EqualTo("PerTile"));
            Assert.That(perTile.PerTileDelaySeconds, Is.EqualTo(0.15f).Within(0.0001f));

            var none = cues.Single(cue => cue.VfxCueId == "V002");
            Assert.That(none.AreaSpawnMode, Is.EqualTo("None"));
            Assert.That(none.PerTileDelaySeconds, Is.EqualTo(0f));
        }

        /// <summary>
        /// 출하 큐의 모드 C 저작 규약을 지킨다. 이 테스트는 원래 "아직 아무도 PerTile을 쓰지 않는다"를
        /// 지키는 임시 가드였는데, 갈래 1 반입(V013·V014·V015)으로 그 전제가 소멸했다. 대신 남는
        /// 항구적 계약을 지킨다 — <b>PerTile 큐는 scaleWithRadius가 반드시 false</b>여야 한다.
        /// true면 칸마다 반지름 배율이 곱해져 한 칸 모듈이 (2R+1)배로 거대해지고, 링 18칸이 화면을
        /// 통째로 덮는다(발주서·인계문이 지목한 모드 C 최대 지뢰). 반대로 PerTile이 아닌 큐가 스태거를
        /// 들고 있으면 아무 효과 없는 죽은 저작이라 오해를 부른다.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void ShippingPerTileCuesKeepTheModeCAuthoringContract()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);

            foreach (var cue in bundle.VfxCues)
            {
                var isPerTile = cue.AreaSpawnMode == "PerTile";
                if (isPerTile)
                {
                    Assert.That(
                        cue.ScaleWithRadius,
                        Is.False,
                        $"{cue.VfxCueId} is PerTile so scaleWithRadius must be false — otherwise every tile is scaled by the area radius.");
                    Assert.That(
                        cue.PerTileDelaySeconds,
                        Is.GreaterThanOrEqualTo(0f),
                        $"{cue.VfxCueId} stagger cannot be negative.");
                    continue;
                }

                Assert.That(
                    string.IsNullOrEmpty(cue.AreaSpawnMode) || cue.AreaSpawnMode == "None",
                    $"{cue.VfxCueId} has an unknown areaSpawnMode '{cue.AreaSpawnMode}'.");
                Assert.That(
                    cue.PerTileDelaySeconds,
                    Is.EqualTo(0f),
                    $"{cue.VfxCueId} is not PerTile so its stagger is dead authoring — keep it at 0.");
            }
        }

        private EffectPresentationController CreatePresentation(out EffectVfxCatalog catalog)
        {
            var root = scope.NewGameObject("Area Spawn Mode Test Root");
            var presentation = root.AddComponent<EffectPresentationController>();
            catalog = scope.Track(ScriptableObject.CreateInstance<EffectVfxCatalog>());
            presentation.SetVfxCatalogForTests(catalog);
            return presentation;
        }

        private static List<GameObject> SpawnedVfx(EffectPresentationController presentation)
        {
            return presentation.SpawnedEffects
                .Where(effect => effect != null && effect.name.StartsWith("VFX "))
                .ToList();
        }

        private static string ReadShippingCsv(string fileName)
        {
            var directory = fileName == "combat_vfx_cues.csv" || fileName == "combat_sound_cues.csv"
                ? CombatCsvPaths.PresentationDirectory
                : CombatCsvPaths.MonsterDirectory;
            return File.ReadAllText(Path.Combine(directory, fileName));
        }
    }
}
#endif
