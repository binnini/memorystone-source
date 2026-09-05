using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 보스 저작 CSV 파서. 초점은 "저작만으로 규칙을 깰 수 있는가"다 — 페이즈 단조성, 최대 체력 감소 금지,
    /// 미구현 기믹 참조는 전부 파싱 시점에 거부되어야 한다.
    /// </summary>
    public sealed class BossCatalogCsvConverterTests
    {
        private const string ProfilesHeader =
            BossCsv.ProfilesHeader + "\n";

        private const string PhasesHeader =
            BossCsv.PhasesHeader + "\n";

        [Category("ShippingData")]
        [Test]
        public void ConvertsShippedBossCsvToRuntimeCatalog()
        {
            var catalog = BossCatalogCsvConverter.ConvertDirectory(CombatCsvPaths.MonsterDirectory, "designer-boss-csv-test", "Boss CSV Test");

            Assert.That(catalog.SourceId, Is.EqualTo("designer-boss-csv-test"));
            Assert.That(catalog.TryGetProfile("M002", out var bulgasal), Is.True, "불가살(M002) 프로필이 저작되어 있어야 한다.");
            Assert.That(bulgasal.DisplayName, Is.EqualTo("불가살"));
            Assert.That(bulgasal.PhaseMetric, Is.EqualTo(BossPhaseMetricKind.AbsorbedStacks));
            Assert.That(bulgasal.PhaseCount, Is.EqualTo(3));
            // §17에서 100/200 → 70/150으로 내렸다: 1페이즈가 구조적으로 가장 비었는데(패턴 2종·전멸기
            // 없음·함정 0개) 13~16턴으로 가장 길었다. 첫 흡수(볼리 5개×15=75)만으로 2페이즈에 들어간다.
            Assert.That(bulgasal.Phases.Select(phase => phase.ProgressThreshold), Is.EqualTo(new[] { 0, 70, 250 }));
            Assert.That(bulgasal.Phases.Select(phase => phase.PatternPhaseMin), Is.EqualTo(new[] { 0, 1, 2 }));
            Assert.That(bulgasal.GetPhase(3).StrengthBonusPercent, Is.EqualTo(120));
            Assert.That(bulgasal.GetPhase(3).MaxHpBonus, Is.EqualTo(36));
            Assert.That(bulgasal.GetBgmCueId(2), Is.EqualTo("music.boss.bulgasal.p2"));
            // §13.5: mechanicId는 | 구분 다중 저작 — 철조각·전멸기·취약 부위·함정 배치가 한 보스에 공존한다.
            // ⚠️ 순서는 가독성일 뿐 정합성이 걸려 있지 않다(§20-B-9): 전멸기↔철조각 상호 배제는
            // 단일 술어 + 래치로 판정하므로 이 배열을 뒤집어도 동작이 같아야 한다.
            // 2026-09-05 개편(사용자 확정 8건): 전멸기·별도 함정 배치·페이즈 진입 수호는 이 보스에서 은퇴했다.
            // 기믹 코드는 남아 있으므로(다른 보스가 저작 가능) 여기서는 「M002가 무엇을 저작하는가」만 고정한다.
            Assert.That(bulgasal.MechanicIds, Is.EqualTo(new[] { "iron-scrap", "scrap-chain", "weak-spot" }));
            Assert.That(bulgasal.MechanicIds, Has.No.Member("annihilation").And.No.Member("trap-volley").And.No.Member("guard"));
            // 스택 가치·성숙 턴수·소환 수량은 전부 저작에서 온다(코드 상수 금지).
            Assert.That(bulgasal.GetMechanicString(IronScrapMechanicParams.PropId), Is.EqualTo("M901"));
            Assert.That(bulgasal.GetMechanicInt(IronScrapMechanicParams.MaturityTurns), Is.GreaterThan(0));
            // 페이즈별 주기 + 살포 동반 함정 풀이 저작돼 있고, 파서가 페이즈 수와 맞춰 받아들였다는 것만 본다(값은 저작).
            Assert.That(bulgasal.GetMechanicIntList(IronScrapMechanicParams.VolleyIntervalByPhase).Count, Is.EqualTo(bulgasal.PhaseCount));
            Assert.That(bulgasal.GetMechanicIntList(IronScrapMechanicParams.VolleyByPhase).Count, Is.EqualTo(bulgasal.PhaseCount));
            var trapPools = BossVolleyTrapPool.ParseByPhase(
                bulgasal.GetMechanicString(IronScrapMechanicParams.VolleyTrapPoolByPhase), IronScrapMechanicParams.VolleyTrapPoolByPhase);
            Assert.That(trapPools.Count, Is.EqualTo(bulgasal.PhaseCount));
            Assert.That(trapPools.All(pool => pool.Count > 0), Is.True, "모든 페이즈가 살포 턴에 함정을 심는다(결정 2).");
            Assert.That(bulgasal.GetMechanicInt(ScrapChainMechanicParams.PhaseMin), Is.EqualTo(2), "사슬은 2페이즈부터(Q16).");
            Assert.That(bulgasal.GetMechanicInt(IronScrapMechanicParams.RingRadius), Is.EqualTo(3));
            Assert.That(bulgasal.GetMechanicInt(IronScrapMechanicParams.MinSpacing), Is.EqualTo(2));
            // maxAlive는 노브가 아니라 가드다: 정상 저작(주기 10 > 성숙 3)에서는 살포 시점 생존 기물이 0이라 걸릴 일이 없다.
            Assert.That(bulgasal.GetMechanicInt(IronScrapMechanicParams.MaxAlive), Is.EqualTo(14));
            // 살포 동반 함정(2026-09-05 결정 2)·방어막·사슬은 저작 여부만 본다 — 수치는 실플레이 노브다.
            Assert.That(bulgasal.GetMechanicInt(IronScrapMechanicParams.VolleyGuardBlock), Is.GreaterThan(0), "살포 턴 방어막(§21.8 제안 4 후계).");
            Assert.That(bulgasal.GetMechanicString(ScrapChainMechanicParams.PropId), Is.EqualTo("M901"));
            Assert.That(bulgasal.GetMechanicInt(ScrapChainMechanicParams.RootTurns), Is.GreaterThan(0));
        }

        [Category("ShippingData")]
        [Test]
        public void ShippedBossVisualScaleGrowsAcrossPhases()
        {
            // 보스의 기본 크기는 프리팹이 아니라 1페이즈 visualScale이다(크기 결정이 한 표에 모인다).
            var catalog = BossCatalogCsvConverter.ConvertDirectory(CombatCsvPaths.MonsterDirectory, "designer-boss-csv-test", "Boss CSV Test");
            Assert.That(catalog.TryGetProfile("M002", out var bulgasal), Is.True);

            var scales = bulgasal.Phases.Select(phase => phase.VisualScale).ToList();
            Assert.That(scales, Is.Ordered.Ascending, "페이즈가 오를수록 커져야 한다(성장형 보스).");
            Assert.That(scales[0], Is.LessThan(1f), "1페이즈는 모델 원본보다 작게 시작한다.");
        }

        /// <summary>
        /// 불가살 페이즈-몸 정렬(2026-09-04 §12): P1 = 1칸 · P2 = <b>3칸 tri</b> · P3 = 7칸(반경 1) —
        /// 페이즈 전환 연출과 몸 성장이 겹쳐 「커지는 보스」가 규칙으로 읽힌다. 반경 2(19칸)는 실제로
        /// 그려 보고 부결됐다(§11.2 — 아레나의 31%를 차지하고 철조각 살포 링까지 깔고 앉는다).
        /// 이 결정이 저작에서 조용히 되돌아가지 않도록 고정한다.
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void ShippedBossGrowsFromOneCellToSevenAndNeverBeyond()
        {
            var catalog = BossCatalogCsvConverter.ConvertDirectory(CombatCsvPaths.MonsterDirectory, "designer-boss-csv-test", "Boss CSV Test");
            Assert.That(catalog.TryGetProfile("M002", out var bulgasal), Is.True);

            Assert.That(
                bulgasal.Phases.Select(phase => phase.FootprintRadius),
                Is.EqualTo(new[] { 0, 0, 1 }),
                "1·2페이즈는 반경 0(P2는 tri 형상이 몸을 3칸으로 만든다), 3페이즈부터 7칸 원판이다.");
            Assert.That(
                bulgasal.Phases.Select(phase => phase.FootprintShape),
                Is.EqualTo(new[]
                {
                    MonsterFootprintShape.Single, MonsterFootprintShape.Triangle, MonsterFootprintShape.Single,
                }),
                "P2만 tri — 몸 성장 사다리 1→3→7의 가운데 단이다(2026-09-04 §12).");

            // 반경 2(19칸)는 반경 4 아레나의 31%를 한 마리가 차지하고 철조각 살포 링까지 깔고 앉아
            // 부결됐다(§11.2). 상한은 여전히 1이다.
            Assert.That(
                bulgasal.Phases.Select(phase => phase.FootprintRadius),
                Is.All.LessThanOrEqualTo(1),
                "반경 2 이상은 §11.2에서 부결됐다.");
        }

        [Test]
        public void RejectsTriangleFootprintShapeCombinedWithADiscRadius()
        {
            // 두 몸 축이 함께 적히면 어느 쪽이 정본인지 못 읽는다(2026-09-04 §12 파서 가드).
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                "bossId,phaseIndex,threshold,strengthBonusPercent,maxHpBonus,patternPhaseMin,visualScale,footprintRadius,footprintShape,auraStatusKind,designerNote\n"
                + "M900,1,0,0,0,0,1,1,tri,,\n"));
            Assert.That(ex.Message, Does.Contain("footprintRadius=0"));
        }

        [Test]
        public void RejectsVolleyListWhoseLengthDoesNotMatchThePhaseCount()
        {
            // 페이즈별 리스트 저작은 페이즈 수를 알아야 검증되므로, 페이즈 행을 다 읽은 뒤에 걸린다.
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + $"M900,보스,AbsorbedStacks,iron-scrap,{IronScrapParams("volleyByPhase", "5|6")},,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\nM900,2,50,0,0,0,1,0,,\nM900,3,90,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("one entry per phase"));
        }

        [Test]
        public void RejectsVolleyIntervalThatIsNotLongerThanMaturity()
        {
            // 주기 <= 성숙이면 이전 볼리가 남은 채로 다음 볼리가 겹쳐 maxAlive 가드에 걸리고 조용히 잘린다.
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + $"M900,보스,AbsorbedStacks,iron-scrap,{IronScrapParams("volleyIntervalTurns", "2")},,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("must be greater than"));
        }

        /// <summary>기본 철조각 저작에서 키 하나만 바꿔 넣는다(나머지 필수 키가 빠져 다른 에러로 새는 것을 막는다).</summary>
        private static string IronScrapParams(string overrideKey, string overrideValue)
        {
            var values = new System.Collections.Generic.Dictionary<string, string>
            {
                { IronScrapMechanicParams.PropId, "M901" },
                { IronScrapMechanicParams.StackPerProp, "15" },
                { IronScrapMechanicParams.MaturityTurns, "3" },
                { IronScrapMechanicParams.VolleyIntervalTurns, "10" },
                { IronScrapMechanicParams.VolleyByPhase, "5" },
                { IronScrapMechanicParams.RingRadius, "3" },
                { IronScrapMechanicParams.MinSpacing, "2" },
                { IronScrapMechanicParams.MaxAlive, "14" }
            };
            values[overrideKey] = overrideValue;
            return string.Join(";", values.Select(pair => $"{pair.Key}={pair.Value}"));
        }

        [Category("ShippingData")]
        [Test]
        public void ShippedBossPropIsAuthoredAsAnOrdinaryCatalogMonster()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);

            Assert.That(bundle.MonsterCatalog.TryGetEntry("M901", out var scrap), Is.True);
            Assert.That(scrap.DisplayName, Is.EqualTo("철조각"));
            // "일정 이상 데미지로 제거"는 MaxHp로 자연 표현된다 — 별도 규칙 코드가 없다.
            Assert.That(scrap.Hp, Is.EqualTo(5)); // 2026-09-05 사용자 확정: 살포 5·6·7개로 늘리며 HP 10→5
        }

        [Test]
        public void RejectsMechanicMissingARequiredParamKey()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,AbsorbedStacks,iron-scrap,propId=M901,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("requires mechanicParams key"));
        }

        [Test]
        public void RejectsMechanicParamsEntryWithoutAnEqualsSign()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,propId,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("key=value"));
        }

        [Test]
        public void RejectsDuplicateMechanicParamsKey()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,ringRadius=1;ringRadius=2,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("duplicates key"));
        }

        [Test]
        public void MechanicParamsAreOptionalForAPhaseOnlyBoss()
        {
            var catalog = Convert(
                ProfilesHeader + "M900,고정형 보스,TurnCount,,,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n");

            Assert.That(catalog.TryGetProfile("M900", out var profile), Is.True);
            Assert.That(profile.HasMechanic, Is.False);
            Assert.That(profile.MechanicParams, Is.Empty);
        }

        [Category("ShippingData")]
        [Test]
        public void ShippedPatternBindingsGateBulgasalPhaseThreePattern()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);
            var patterns = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M002").AttackPatterns;

            Assert.That(patterns.Single(pattern => pattern.Id == "A003").PhaseMin, Is.EqualTo(0));
            Assert.That(patterns.Single(pattern => pattern.Id == "A004").PhaseMin, Is.EqualTo(0));
            Assert.That(patterns.Single(pattern => pattern.Id == "A005").PhaseMin, Is.EqualTo(1));
            // 2026-08-18: A006은 1~2페이즈 전용으로 내려왔다(아래 PhaseMax 단언과 짝).
            Assert.That(patterns.Single(pattern => pattern.Id == "A006").PhaseMin, Is.EqualTo(0));
            Assert.That(patterns.Single(pattern => pattern.Id == "A007").PhaseMin, Is.EqualTo(2));
        }

        [Category("ShippingData")]
        [Test]
        public void ShippedPatternBindingsRetireEarlyBulgasalPatternsAndUnlockTheWideOnes()
        {
            // §13.2 세대 교체 저작: 광역 패턴의 해금 게이트와, 3페이즈(게이트 2)에서 은퇴하는
            // 초기 패턴(A006→A018 대체)을 고정한다. 기본 발톱(A003)은 은퇴하지 않는다 —
            // "은퇴 없는 쿨다운-0 기본 패턴" 파서 가드의 전제이기도 하다.
            // 2026-08-18: A020·A021 삭제, A018=3페이즈 전용·A006=1~2페이즈 전용으로 재편.
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);
            var patterns = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M002").AttackPatterns;

            // A018은 3페이즈 전용(2026-08-18) — A006[1~2페이즈]과 공존 구간 없는 완전 세대 교체.
            // ⚠️유일한 도약형이라 2페이즈에는 도약 패턴이 없다(§24 ②의 우려 지점, 사용자 판단으로 수용).
            Assert.That(patterns.Single(pattern => pattern.Id == "A018").PhaseMin, Is.EqualTo(2));
            // A019는 §17에서 1페이즈로 내려왔다 — 1페이즈 풀이 A003·A004 둘뿐이라 얇았다.
            Assert.That(patterns.Single(pattern => pattern.Id == "A019").PhaseMin, Is.EqualTo(0));
            Assert.That(patterns.Single(pattern => pattern.Id == "A019").DistMax, Is.EqualTo(4), "2페이즈 거리 4가 A003 100%인 구간을 메운다(§17).");
            Assert.That(patterns.Single(pattern => pattern.Id == "A004").PhaseMax, Is.EqualTo(1));
            Assert.That(patterns.Single(pattern => pattern.Id == "A006").PhaseMax, Is.EqualTo(1));
            Assert.That(patterns.Single(pattern => pattern.Id == "A003").PhaseMax, Is.EqualTo(int.MaxValue));

            // 도약은 2026-09-05 실플레이 3차 #4에서 불가살에게서 삭제됐다(A018 leapRange 4→0). 파서의 바인딩
            // 재생성 경로가 leapRange를 떨어뜨리지 않는가는 픽스처 스위트(BossLeapAttackTests)가 지킨다.
        }

        [Category("ShippingData")]
        [Test]
        public void RejectsPhaseMaxBelowPhaseMin()
        {
            // 창이 비는 [min, max] 저작은 그 패턴이 어느 게이트에서도 후보가 못 된다 — 조용히 죽는 대신 거부.
            var bindings = File.ReadAllText(Path.Combine(CombatCsvPaths.MonsterDirectory, "monster_pattern_bindings.csv"))
                .Replace("M002,A005,3,true,,1,,", "M002,A005,3,true,,1,0,");
            var source = new MonsterCatalogCsvSource(
                ReadMonster("monster_catalog.csv"),
                ReadMonster("monster_attack_patterns.csv"),
                bindings,
                ReadPresentation("combat_vfx_cues.csv"),
                ReadPresentation("combat_sound_cues.csv"),
                ReadPresentation("monster_pattern_vfx_bindings.csv"));

            var ex = Assert.Throws<ArgumentException>(() => MonsterCatalogCsvConverter.Convert(source));
            Assert.That(ex.Message, Does.Contain("phaseMax"));
        }

        [Category("ShippingData")]
        [Test]
        public void RejectsMonsterWhoseEveryCooldownZeroBasicPatternRetires()
        {
            // 쿨다운-0 보장은 모든 게이트에서 성립해야 한다. 게이트 수는 보스 카탈로그 소관이라 몬스터
            // 파서는 게이트별 전수 검증을 할 수 없고, 대신 "은퇴하지 않는 쿨다운-0 기본 패턴 1개"를
            // 저작 규칙으로 강제한다. 불가살의 유일한 해당 패턴(A003)에 phaseMax를 달면 거부되어야 한다.
            var bindings = File.ReadAllText(Path.Combine(CombatCsvPaths.MonsterDirectory, "monster_pattern_bindings.csv"))
                .Replace("M002,A003,1,true,,0,,", "M002,A003,1,true,,0,1,");
            var source = new MonsterCatalogCsvSource(
                ReadMonster("monster_catalog.csv"),
                ReadMonster("monster_attack_patterns.csv"),
                bindings,
                ReadPresentation("combat_vfx_cues.csv"),
                ReadPresentation("combat_sound_cues.csv"),
                ReadPresentation("monster_pattern_vfx_bindings.csv"));

            var ex = Assert.Throws<ArgumentException>(() => MonsterCatalogCsvConverter.Convert(source));
            Assert.That(ex.Message, Does.Contain("never-retiring"));
        }

        [Category("ShippingData")]
        [Test]
        public void NonBossMonstersKeepPhaseMinZeroSoExistingAuthoringIsUnchanged()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(
                CombatCsvPaths.MonsterDirectory,
                CombatCsvPaths.PresentationDirectory);

            var nonBossPatterns = bundle.MonsterCatalog.Entries
                .Where(entry => entry.Id != "M002")
                .SelectMany(entry => entry.AttackPatterns);
            Assert.That(nonBossPatterns.All(pattern => pattern.PhaseMin == 0), Is.True);
        }

        [Test]
        public void HpRatioBelowThresholdsAreNormalizedToAscendingProgress()
        {
            var catalog = Convert(
                ProfilesHeader + "M900,고정형 보스,HpRatioBelow,,,,,music.boss.static,\n",
                PhasesHeader +
                "M900,1,100,0,0,0,1,0,,\n" +
                "M900,2,50,20,0,1,1,0,,\n" +
                "M900,3,25,40,0,2,1,0,,\n");

            Assert.That(catalog.TryGetProfile("M900", out var profile), Is.True);
            // 저작은 내려가는 체력 비율(100/50/25), 내부 progress는 잃은 비율(0/50/75)로 오름차순.
            Assert.That(profile.Phases.Select(phase => phase.AuthoredThreshold), Is.EqualTo(new[] { 100, 50, 25 }));
            Assert.That(profile.Phases.Select(phase => phase.ProgressThreshold), Is.EqualTo(new[] { 0, 50, 75 }));
            Assert.That(profile.ResolvePhaseForProgress(49), Is.EqualTo(1));
            Assert.That(profile.ResolvePhaseForProgress(50), Is.EqualTo(2));
            Assert.That(profile.ResolvePhaseForProgress(100), Is.EqualTo(3));
        }

        [Test]
        public void ResolvePhaseForProgressSkipsStraightToTheReachedPhase()
        {
            var profile = SingleProfile(BossPhaseMetricKind.AbsorbedStacks, new[] { 0, 100, 200 });

            Assert.That(profile.ResolvePhaseForProgress(0), Is.EqualTo(1));
            Assert.That(profile.ResolvePhaseForProgress(99), Is.EqualTo(1));
            Assert.That(profile.ResolvePhaseForProgress(100), Is.EqualTo(2));
            // 지표가 한 번에 크게 뛰어도 중간 페이즈에 머무르지 않는다.
            Assert.That(profile.ResolvePhaseForProgress(1000), Is.EqualTo(3));
        }

        [Category("ShippingData")]
        [Test]
        public void ShippedBossKeepsTheLandingDiskDisjointFromThePropVolleyBand()
        {
            // 🔴 §20-B-3 분리 불변식: 전멸기 착지 원판(중심 거리 ≤ footprintRadius)과 철조각 살포 밴드
            // (중심 거리 ringRadius-1 ~ ringRadius+1)가 <b>서로소</b>여야 착지가 기물을 뭉개는 일이
            // 구조적으로 없다. 둘 다 아레나 <b>중심</b> 기준이라 이 비교만으로 충분하다.
            //
            // 💬 이 불변식은 ValidateParams에서 검증할 수 없다: footprintRadius는 페이즈 행 저작이고
            // 그 훅에는 페이즈 <b>개수</b>만 들어온다. 그래서 여기서 핀으로 잡는다 —
            // 저작으로 ringRadius를 2 이하로 내리면 이 시험이 먼저 깨진다.
            var catalog = BossCatalogCsvConverter.ConvertDirectory(CombatCsvPaths.MonsterDirectory, "boss-invariant-test", "Boss Invariant Test");
            Assert.That(catalog.TryGetProfile("M002", out var bulgasal), Is.True);

            if (!bulgasal.MechanicIds.Contains("annihilation"))
            {
                // 2026-09-05: 불가살은 전멸기를 은퇴했다 — 착지 원판이 없으므로 이 불변식은 걸릴 대상이 없다.
                // 다른 보스가 전멸기+철조각을 함께 저작하면 그때 다시 문다.
                Assert.Pass("M002는 전멸기를 저작하지 않는다(2026-09-05 결정 6).");
            }

            var ringRadius = bulgasal.GetMechanicInt(IronScrapMechanicParams.RingRadius);
            var maxFootprint = bulgasal.Phases.Max(phase => phase.FootprintRadius);
            Assert.That(ringRadius - 1, Is.GreaterThan(maxFootprint),
                $"살포 밴드의 안쪽 고리({ringRadius - 1})가 최대 착지 원판({maxFootprint}) 안으로 들어오면 " +
                "전멸기가 자기 기물을 뭉개고 시작한다.");
        }

        [Test]
        public void RejectsAnnihilationWithoutAnySafeCell()
        {
            // 진짜 안전지대 0 = 회피 불가능한 전멸기. 저작만으로 이 계약을 깰 수 없어야 한다(사용자 요건 §20-B).
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + $"M900,보스,TurnCount,annihilation,{AnnihilationParams("annihilationRealSafeCells", "0")},,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("annihilationRealSafeCells"));
        }

        [Test]
        public void RejectsAnnihilationWithMoreThanOneFakeCandidate()
        {
            // 🔴 회피 100% 보장의 계약 가드(§20-B-2). 가짜가 정확히 하나일 때만 "가짜로 판명되면
            // 남은 둘은 전부 진짜"가 성립한다 — 가짜가 둘이면 정찰 1장으로 회피가 확정되지 않는다.
            // 이 가드가 없으면 사용자 확정 계약이 CSV 한 줄로 조용히 깨진다.
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + $"M900,보스,TurnCount,annihilation,{AnnihilationParams("annihilationRealSafeCells", "1")},,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("must be at most 1"));
        }

        [Test]
        public void RejectsAnnihilationTelegraphShorterThanTwoTurns()
        {
            // 2 = 점프 턴 + 반응 턴. 1이면 점프한 그 턴 안에 폭발해 정찰도 이동도 낄 자리가 없다(§20-B-8).
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + $"M900,보스,TurnCount,annihilation,{AnnihilationParams("annihilationTelegraphTurns", "1")},,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("annihilationTelegraphTurns"));
        }

        [Test]
        public void RejectsAnnihilationIntervalNotLongerThanTelegraph()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + $"M900,보스,TurnCount,annihilation,{AnnihilationParams("annihilationIntervalTurns", "1")},,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("must be greater than"));
        }

        [Test]
        public void RejectsMechanicListContainingAnUnregisteredId()
        {
            // | 목록은 전부 등록되어 있어야 한다 — 하나만 틀려도 저작 전체가 거부된다.
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,annihilation|no-such-mechanic," + AnnihilationParams() + ",,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("no-such-mechanic"));
        }

        /// <summary>기본 전멸기 저작에서 키 하나만 바꿔 넣는다(IronScrapParams와 같은 취지).</summary>
        private static string AnnihilationParams(string overrideKey = null, string overrideValue = null)
        {
            var values = new System.Collections.Generic.Dictionary<string, string>
            {
                { AnnihilationMechanicParams.PhaseMin, "1" },
                { AnnihilationMechanicParams.IntervalTurns, "5" },
                { AnnihilationMechanicParams.TelegraphTurns, "2" },
                { AnnihilationMechanicParams.Damage, "5" },
                { AnnihilationMechanicParams.CandidateCells, "3" },
                { AnnihilationMechanicParams.RealSafeCells, "2" },
                { AnnihilationMechanicParams.CandidateSpacing, "3" },
                { AnnihilationMechanicParams.SafeReach, "2" },
                { AnnihilationMechanicParams.FallbackRadius, "3" },
                { AnnihilationMechanicParams.LandingBlastRadius, "1" },
                { AnnihilationMechanicParams.LandingBlastDamage, "5" }
            };
            if (overrideKey != null)
            {
                values[overrideKey] = overrideValue;
            }

            return string.Join(";", values.Select(pair => $"{pair.Key}={pair.Value}"));
        }

        [Test]
        public void RejectsUnregisteredMechanicId()
        {
            Assume.That(BossMechanicRegistry.IsValidMechanicId("no-such-mechanic"), Is.False);

            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,no-such-mechanic,,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("no-such-mechanic"));
        }

        [Test]
        public void RejectsPhaseIndexThatDoesNotStartAtOne()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                PhasesHeader + "M900,2,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("phaseIndex"));
        }

        [Test]
        public void RejectsNonContiguousPhaseIndex()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                PhasesHeader +
                "M900,1,0,0,0,0,1,0,,\n" +
                "M900,3,5,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("phaseIndex"));
        }

        [Test]
        public void RejectsFirstPhaseThatIsNotEnteredFromTheStart()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                PhasesHeader + "M900,1,3,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("phase 1"));
        }

        [Test]
        public void RejectsThresholdThatDoesNotAdvance()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                PhasesHeader +
                "M900,1,0,0,0,0,1,0,,\n" +
                "M900,2,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("threshold must advance"));
        }

        [Test]
        public void RejectsMaxHpBonusThatDecreasesBetweenPhases()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                PhasesHeader +
                "M900,1,0,0,20,0,1,0,,\n" +
                "M900,2,3,0,10,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("maxHpBonus"));
        }

        [Test]
        public void RejectsPhaseRowWithoutProfile()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                PhasesHeader +
                "M900,1,0,0,0,0,1,0,,\n" +
                "M901,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("M901"));
        }

        [Test]
        public void RejectsProfileWithoutAnyPhaseRow()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader +
                "M900,보스,TurnCount,,,,,,\n" +
                "M901,보스2,TurnCount,,,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("M901"));
        }

        [Test]
        public void RejectsBossIdThatDoesNotMatchMonsterIdFormat()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "bulgasal,보스,TurnCount,,,,,,\n",
                PhasesHeader + "bulgasal,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("bossId"));
        }

        [Test]
        public void RejectsUnknownPhaseMetric()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,MoonPhase,,,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,0,,\n"));
            Assert.That(ex.Message, Does.Contain("phaseMetric"));
        }

        [Test]
        public void RejectsFootprintRadiusOutsideSupportedRange()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,1,3,,\n"));
            Assert.That(ex.Message, Does.Contain("footprintRadius"));
        }

        [Test]
        public void RejectsNonPositiveVisualScale()
        {
            var ex = Assert.Throws<ArgumentException>(() => Convert(
                ProfilesHeader + "M900,보스,TurnCount,,,,,,\n",
                PhasesHeader + "M900,1,0,0,0,0,0,0,,\n"));
            Assert.That(ex.Message, Does.Contain("visualScale"));
        }

        [Category("ShippingData")]
        [Test]
        public void RejectsMonsterWithNoPhaseMinZeroPattern()
        {
            // phaseMin>0 패턴만 바인딩되면 1페이즈에서 쓸 패턴이 없다 — 저작으로 몬스터를 마비시킬 수 없어야 한다.
            var bindings = File.ReadAllText(Path.Combine(CombatCsvPaths.MonsterDirectory, "monster_pattern_bindings.csv"))
                .Replace("M001,A001,1,true,,0,", "M001,A001,1,true,,1,")
                .Replace("M001,A002,2,true,,0,", "M001,A002,2,true,,1,");
            var source = new MonsterCatalogCsvSource(
                ReadMonster("monster_catalog.csv"),
                ReadMonster("monster_attack_patterns.csv"),
                bindings,
                ReadPresentation("combat_vfx_cues.csv"),
                ReadPresentation("combat_sound_cues.csv"),
                ReadPresentation("monster_pattern_vfx_bindings.csv"));

            var ex = Assert.Throws<ArgumentException>(() => MonsterCatalogCsvConverter.Convert(source));
            Assert.That(ex.Message, Does.Contain("phaseMin=0"));
        }

        [Category("ShippingData")]
        [Test]
        public void RejectsMonsterWhoseOnlyPhaseZeroPatternHasCooldown()
        {
            // 1페이즈에서 쓸 수 있는 패턴이 전부 쿨다운형이면 전 패턴이 동시에 잠길 수 있다.
            // 불가살의 쿨다운 0 패턴(A003/A004)만 phaseMin=1로 올리면 그 상태가 된다.
            var bindings = File.ReadAllText(Path.Combine(CombatCsvPaths.MonsterDirectory, "monster_pattern_bindings.csv"))
                .Replace("M002,A003,1,true,,0,", "M002,A003,1,true,,1,")
                .Replace("M002,A004,2,true,,0,", "M002,A004,2,true,,1,")
                .Replace("M002,A005,3,true,,1,", "M002,A005,3,true,,0,");
            var source = new MonsterCatalogCsvSource(
                ReadMonster("monster_catalog.csv"),
                ReadMonster("monster_attack_patterns.csv"),
                bindings,
                ReadPresentation("combat_vfx_cues.csv"),
                ReadPresentation("combat_sound_cues.csv"),
                ReadPresentation("monster_pattern_vfx_bindings.csv"));

            var ex = Assert.Throws<ArgumentException>(() => MonsterCatalogCsvConverter.Convert(source));
            Assert.That(ex.Message, Does.Contain("cooldown-0"));
        }

        [TestCase(BossPhaseMetricKind.AbsorbedStacks, 137, 10, 10, 5, 137)]
        [TestCase(BossPhaseMetricKind.HpRatioBelow, 0, 10, 10, 5, 0)]
        [TestCase(BossPhaseMetricKind.HpRatioBelow, 0, 5, 10, 5, 50)]
        [TestCase(BossPhaseMetricKind.HpRatioBelow, 0, 0, 10, 5, 100)]
        [TestCase(BossPhaseMetricKind.TurnCount, 0, 10, 10, 1, 0)]
        [TestCase(BossPhaseMetricKind.TurnCount, 0, 10, 10, 7, 6)]
        public void EvaluatesEveryMetricOntoOneAscendingProgressAxis(
            BossPhaseMetricKind metric,
            int absorbedStacks,
            int hp,
            int maxHp,
            int overallTurn,
            int expected)
        {
            Assert.That(BossPhaseProgress.Evaluate(metric, absorbedStacks, hp, maxHp, overallTurn), Is.EqualTo(expected));
        }

        private static BossCatalogDefinition Convert(string profilesCsv, string phasesCsv)
        {
            return BossCatalogCsvConverter.Convert(new BossCatalogCsvSource(profilesCsv, phasesCsv, "boss-csv-test", "Boss CSV Test"));
        }

        private static BossProfileDefinition SingleProfile(BossPhaseMetricKind metric, int[] thresholds)
        {
            var profiles = ProfilesHeader + $"M900,보스,{metric},,,,,,\n";
            var phases = PhasesHeader;
            for (var i = 0; i < thresholds.Length; i++)
            {
                phases += $"M900,{i + 1},{thresholds[i]},0,0,{i},1,0,,\n";
            }

            var catalog = Convert(profiles, phases);
            Assert.That(catalog.TryGetProfile("M900", out var profile), Is.True);
            return profile;
        }

        private static string ReadMonster(string fileName) =>
            File.ReadAllText(Path.Combine(CombatCsvPaths.MonsterDirectory, fileName));

        private static string ReadPresentation(string fileName) =>
            File.ReadAllText(Path.Combine(CombatCsvPaths.PresentationDirectory, fileName));
    }
}
