using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class MonsterCatalogCsvConverterTests
    {
        private const string CsvDirectory = CombatCsvPaths.MonsterDirectory;

        [Category("ShippingData")]
        [Test]
        public void ConvertsDesignerMonsterCsvToRuntimeCatalog()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CsvDirectory, CombatCsvPaths.PresentationDirectory, "designer-monster-csv-test", "Designer Monster CSV Test");

            Assert.That(bundle.MonsterCatalog.SourceId, Is.EqualTo("designer-monster-csv-test"));
            // M001~M006 = 조우용 몬스터 6종 + M901 = 불가살 기믹 기물(철조각)
            // + M902~M904 = 자동 공격 터렛 LV1~LV3(C-7, 스폰 함정이 부르는 이동 0 포탑)
            // + M905 = 결계 구슬(T4-2, 플레이어 설치 기물).
            // 2026-08-30 요괴 6종 개편: 여우불(M007)·구미호(M011) 폐기, 거구귀(M012)·그슨새(M013)·
            // 야광귀(M014) 편입. 나무 말뚝(M906)은 소환 저작 철회(2026-08-30)로 빠졌다.
            // 🔑 상수 17이 아니라 저작 행 수와 맞춘다(2026-08-31 T4). 몬스터를 추가할 때마다 이 줄이
            // 거짓 경보로 깨지면 안 된다 — 위 이력이 그 흔적이다. 계약은 "저작 행 하나 = 엔트리 하나".
            Assert.That(bundle.MonsterCatalog.Entries, Is.Not.Empty, "출하 몬스터를 하나도 못 읽었다.");
            Assert.That(
                bundle.MonsterCatalog.Entries,
                Has.Count.EqualTo(AuthoredCsvRows.Count(CombatCsvPaths.MonsterDirectory + "/monster_catalog.csv")),
                "monster_catalog.csv 행 하나가 엔트리 하나다 — 수가 다르면 변환이 행을 흘렸다.");

            var monster = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M001");
            Assert.That(monster.Id, Is.EqualTo("M001"));
            Assert.That(monster.DisplayName, Is.EqualTo("삼목구"));
            Assert.That(monster.Archetype, Is.EqualTo("test-melee"));
            Assert.That(monster.BehaviorProfileRef, Is.EqualTo("B001"));
            Assert.That(monster.Hp, Is.EqualTo(18));
            Assert.That(monster.DetectionRange, Is.EqualTo(6));
            Assert.That(monster.MovePerTurn, Is.EqualTo(2));
            Assert.That(monster.AttackSpeed, Is.EqualTo(3)); // 규약: 값↑ = 빠름 = 선공 (M001=3)
            Assert.That(monster.VisualPrefabPath, Is.EqualTo("Assets/Art/Characters/monster/ThreeEyeDog/Prefabs/ThreeEyeDog.prefab"));
            // T2(2026-08-06): 해코지 A027(저주 주입) 추가.
            Assert.That(monster.AttackPatterns.Select(pattern => pattern.Id), Is.EqualTo(new[] { "A001", "A002", "A027" }));

            var bite = monster.AttackPatterns[0];
            Assert.That(bite.DisplayName, Is.EqualTo("깨물기"));
            // 2026-09-04 형상 문법 전환: 기존 요괴도 신규 6종처럼 안전지대가 있는 open 형상을 쓴다
            // (single→bite-front). 무안전지대(full)는 터렛 3종·호랑 A010·불가살만 남는다.
            Assert.That(bite.ShapeId, Is.EqualTo("bite-front"));
            // 2026-09-02 너프: 4 → 3(사용자 실플레이 "삼목구 데미지를 살짝").
            Assert.That(bite.Damage, Is.EqualTo(3));

            var claw = monster.AttackPatterns[1];
            Assert.That(claw.DisplayName, Is.EqualTo("할퀴기"));
            Assert.That(claw.ShapeId, Is.EqualTo("lunge-3"), "직선 길이 식별: 돼지 2·삼목구 3·황소 4.");
            Assert.That(claw.Damage, Is.EqualTo(2));

            // Status-effect pattern coverage moved to Bulgasal's A005 (우월한 자태, Slow 3턴).
            var slow = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M002")
                .AttackPatterns.Single(pattern => pattern.Id == "A005");
            Assert.That(slow.DisplayName, Is.EqualTo("위압의 십자")); // 2026-09-05 이름 재저작(Q9)
            Assert.That(slow.Damage, Is.EqualTo(5));
            Assert.That(slow.StatusEffects, Is.EqualTo(new[] { StatusEffectKind.Slow }));
            Assert.That(slow.StatusEffectDurationTurns, Is.EqualTo(3));
            Assert.That(slow.ShapeId, Is.EqualTo(AttackShapeLibrary.CrossFar));

            var newMonsterIds = bundle.MonsterCatalog.Entries
                .Where(entry => entry.Id != "M001")
                .Select(entry => entry.Id)
                .ToArray();
            Assert.That(newMonsterIds, Is.EqualTo(new[]
            {
                "M000",                          // 2026-09-05 튜토리얼 전용 「어린 삼목구」(M001 바로 뒤에 저작)
                "M002", "M003", "M004", "M005", "M006",
                "M901", "M902", "M903", "M904", "M905",
                // 카탈로그 순서는 저작 순서 그대로다(기물 뒤에 붙는다).
                "M008", "M010", "M009",          // 요괴 S2~S4 잔존분(M008은 도깨비 → 두두리 개명)
                "M012", "M013", "M014",  // 2026-08-30 6종 개편 편입분(M906 나무 말뚝은 소환 철회로 제거)
            }));
            // 튜토리얼 전용 「어린 삼목구」(2026-09-05): 3분 축약 각본이 이 세 값에 기댄다 —
            // 공격의 기초 3방(9)에 죽고, 깨물기(3) 하나만 있어 방어막 3이 반격을 전부 흡수하며, HP 변동이 없다.
            var youngDog = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M000");
            Assert.That(youngDog.Hp, Is.EqualTo(9), "어린 삼목구 HP = 공격의 기초 3 × 3.");
            Assert.That(youngDog.HpVariancePct, Is.EqualTo(0), "튜토리얼 요괴는 HP 변동이 없어야 각본이 결정적이다.");
            Assert.That(youngDog.AttackPatterns.Select(pattern => pattern.Id), Is.EqualTo(new[] { "A000" }),
                "어린 삼목구는 깨물기(어린)만 — 해코지(실명)가 섞이면 턴 3의 기억결이 시야 밖으로 나갈 수 있다.");
            var youngBite = youngDog.AttackPatterns.Single();
            Assert.That(youngBite.Damage, Is.EqualTo(3), "깨물기(어린) 피해 3 = 방어의 기초 방어막 3.");
            Assert.That(youngBite.HasDamageJitter, Is.False, "피해 변주가 있으면 방어막 3이 4를 못 막는 턴이 생긴다(실플레이 실측 hp 79).");
            Assert.That(youngDog.VisualPrefabPath, Is.EqualTo(monster.VisualPrefabPath), "삼목구 프리팹을 그대로 쓴다.");

            // C-7 터렛(D-5): 이동 0이 저작 그대로 살아 있어야 한다. 예전에는 런타임이 0을 1로 끌어올려
            // "고정 포탑"을 저작할 방법 자체가 없었다.
            foreach (var turretId in new[] { "M902", "M903", "M904" })
            {
                Assert.That(
                    bundle.MonsterCatalog.Entries.Single(entry => entry.Id == turretId).MovePerTurn,
                    Is.EqualTo(0),
                    $"{turretId}는 고정 포탑이다 — 이동 0이 저작 그대로 살아 있어야 한다.");
            }

            // 레벨은 HP/피해/사거리로만 갈린다(사용자 확정: 3/2/1 · 6/4/1 · 9/6/2).
            var turretLevels = new[]
            {
                ("M902", "A017", 3, 2, 1),
                ("M903", "A023", 6, 4, 1),
                ("M904", "A024", 9, 6, 2),
            };
            foreach (var (monsterId, patternId, hp, damage, range) in turretLevels)
            {
                var turret = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == monsterId);
                Assert.That(turret.Hp, Is.EqualTo(hp), $"{monsterId} HP");
                var shot = turret.AttackPatterns.Single(pattern => pattern.Id == patternId);
                Assert.That(shot.Damage, Is.EqualTo(damage), $"{monsterId} 피해");
                Assert.That(shot.Range, Is.EqualTo(range), $"{monsterId} 사거리");
            }
            // §21.8 제안 5·8(2026-08-06): 휩쓸기 A028(저주 주입+밀치기) · 자기부여 A029(강화)·A030(미지) 추가.
            // 2026-08-18: 스타일 중복 정리로 A020(초장 브레스)·A021(지맥 분출) 삭제.
            // 2026-09-03: A030(미지의 장막) 폐기 — 은폐는 플레이어가 시각 피드백을 얻기 어렵다(사용자 확정).
            var bulgasalPatterns = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M002").AttackPatterns;
            Assert.That(bulgasalPatterns.Select(pattern => pattern.Id), Is.EqualTo(new[] { "A003", "A004", "A005", "A006", "A007", "A018", "A019", "A028", "A029", "A061", "A062", "A063" }),
                "2026-09-05: A022 은퇴(enabled=false) · A061 쇳가루 폭풍 · A062 삼각 할퀴기 · A063 거구 진동 신설.");
            // 저작은 전부 단일 히트(컬럼 백필 1)여야 한다 — 0/공백이 섞이면 파서가 1로 눌러 주지만
            // 저작 표면이 조용히 어긋난 것이므로 여기서 잡는다. (다단이던 A020은 삭제됨.)
            Assert.That(bulgasalPatterns.Select(pattern => pattern.HitCount), Is.All.EqualTo(1));
            var lionMaskPatterns = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M004").AttackPatterns;
            // T1(2026-08-06): A026(봉인) 추가 — 2026-09-04 「요술 걸기」로 재명명.
            Assert.That(lionMaskPatterns.Select(pattern => pattern.Id), Is.EqualTo(new[] { "A011", "A012", "A026" }));
            Assert.That(lionMaskPatterns.Single(pattern => pattern.Id == "A011").Weight, Is.EqualTo(4));
            Assert.That(lionMaskPatterns.Single(pattern => pattern.Id == "A012").Weight, Is.EqualTo(1));
            Assert.That(bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M006").AttackPatterns.Select(pattern => pattern.AnimationTrigger), Is.EqualTo(new[] { "Attack1", "Attack2", "Attack3", "Attack2" }));  // 네 번째 = 꾸중 A025(T1)
        }

        [Category("ShippingData")]
        [Test]
        public void YokaiReworkAuthoringPinsHold()
        {
            // 2026-09-04 요괴 리워크(정본: docs/prompts/yokai-six-trait-pattern-rework-handoff.md)의
            // 저작 핀. 값이 어긋나면 리워크 확정이 조용히 되돌려진 것이다.
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CsvDirectory, CombatCsvPaths.PresentationDirectory);
            MonsterAttackPattern Pattern(string monsterId, string patternId) =>
                bundle.MonsterCatalog.Entries.Single(entry => entry.Id == monsterId)
                    .AttackPatterns.Single(pattern => pattern.Id == patternId);

            // 두억시니 — swipe-wide 8 / slam 6 / fissure-twin 5 / 몸부림 body-ring·넉백 2·cd 3.
            Assert.That(Pattern("M010", "A040").ShapeId, Is.EqualTo("swipe-wide"));
            Assert.That(Pattern("M010", "A040").Damage, Is.EqualTo(8));
            Assert.That(Pattern("M010", "A041").Damage, Is.EqualTo(6));
            Assert.That(Pattern("M010", "A042").ShapeId, Is.EqualTo("fissure-twin"));
            Assert.That(Pattern("M010", "A042").Damage, Is.EqualTo(5));
            var thrash = Pattern("M010", "A057");
            Assert.That(thrash.DisplayName, Is.EqualTo("몸부림"));
            Assert.That(thrash.ShapeId, Is.EqualTo("body-ring"));
            Assert.That(thrash.KnockbackDistance, Is.EqualTo(2));
            Assert.That(thrash.CooldownTurns, Is.EqualTo(3));
            Assert.That(thrash.DistMax, Is.EqualTo(1), "몸 둘레 공격이라 바인딩 거리 0~1.");

            // 거구귀 — maw-wide 5 / 뱉어내기 body-ring·밀침 +2(충돌 2 — 당김 -1에서 반전·§16.1).
            Assert.That(Pattern("M012", "A048").ShapeId, Is.EqualTo("maw-wide"));
            Assert.That(Pattern("M012", "A048").Damage, Is.EqualTo(5));
            var spit = Pattern("M012", "A049");
            Assert.That(spit.DisplayName, Is.EqualTo("뱉어내기"));
            Assert.That(spit.ShapeId, Is.EqualTo("body-ring"));
            Assert.That(spit.KnockbackDistance, Is.EqualTo(2), "당김(-1)→밀침(+2) 반전이 이 리워크의 핵심 수다.");
            Assert.That(spit.KnockbackImpactDamage, Is.EqualTo(2));
            Assert.That(spit.Damage, Is.EqualTo(7));

            // 불가살 — A028 donut-2→body-ring(몸 성장 자동 대응·수치 무너프).
            Assert.That(Pattern("M002", "A028").ShapeId, Is.EqualTo("body-ring"));
            Assert.That(Pattern("M002", "A028").Damage, Is.EqualTo(4), "수치 무너프.");

            // 기존 요괴 Part 2 — 호랑 A010 히트 3→2 · A014 둔화 제거 · A025 cd 3 · 사자탈 뒤끝=봉인.
            Assert.That(Pattern("M006", "A010").HitCount, Is.EqualTo(2), "총 9→6 너프. 형상(ring-full-2)은 중간보스 전유로 유지.");
            Assert.That(Pattern("M005", "A014").StatusEffects, Is.EqualTo(new[] { StatusEffectKind.Rupture }));
            Assert.That(Pattern("M006", "A025").CooldownTurns, Is.EqualTo(3));
            Assert.That(Pattern("M004", "A026").DisplayName, Is.EqualTo("요술 걸기"));
            // 🔴 2026-09-05 사용자 확정: 사자탈 뒤끝을 봉인(kind=Seal)에서 <b>저주 카드 부여</b>로 교체.
            // 대가가 그 판 두 턴이 아니라 <b>덱에 남는다</b>. 반경은 종전 봉인과 같은 값을 유지해
            // 「붙어서 잡으면 대가, 떨어져서 잡으면 면한다」 규약이 그대로 산다 —
            // 여기서 재는 것은 풀의 내용물이 아니라 <b>그 규약</b>이다(풀은 밸런스 손잡이).
            var lionMask = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M004");
            Assert.That(
                MonsterDeathAftermath.TryParse(lionMask.OnDeathEffectRef, lionMask.OnDeathEffectParam, out var lionSpec, out var lionError),
                Is.True, lionError);
            Assert.That(lionSpec.Kind, Is.EqualTo(MonsterDeathAftermathKind.Curse), "뒤끝 봉인→저주 카드 부여.");
            Assert.That(lionSpec.CursePool, Is.Not.Empty, "저주 뒤끝은 뽑을 카드가 있어야 성립한다.");
            Assert.That(lionSpec.Radius, Is.GreaterThan(0),
                "사거리 제한이 있어야 「붙어서 잡으면 대가」가 성립한다 — 음수는 무제한이다.");

            // 그슨새 — A059 전용 신형상(2026-09-04 사용자 확정).
            Assert.That(Pattern("M013", "A059").ShapeId, Is.EqualTo("claw-rake"));
        }

        [Category("ShippingData")]
        [Test]
        public void ConvertsPresentationCueDataAlongsideRuntimeCatalog()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CsvDirectory, CombatCsvPaths.PresentationDirectory);

            // V011~V018은 발주 갈래 1(에셋 개조 8규격) 반입분이다.
            // V019는 A013(돼지 뺨 때리기)를 A003(불가살)에서 떼어 낸 분리분 — 한 큐를 몸집이
            // 아주 다른 두 몬스터가 공유해 크기·높이를 따로 잡을 수 없었다(2026-08-11).
            // V020(A028 쇳가루 휩쓸기)·V021(미지의 장막)은 2026-08-18 신규 저작분 —
            // V021은 소유자를 전부 잃었다(A030 폐기 2026-09-03·A039 삭제 2026-09-04·A046 삭제 2026-09-04 피드백).
            // V011·V013 선례대로 큐 행은 CSV에 잔존한다.
            // V022~V025(터렛 머즐+착탄 LV1~3)는 미배정 VFX 1차 배치(2026-08-18).
            // 은신 진입 연막(2026-09-04 피드백)은 이 CSV에 없다 — 카드/패턴에 안 묶이는 큐라
            // 철조각·보스 페이즈 전환 선례대로 카탈로그 직접 저작이다(StealthShadowCloudVfxBuilder).
            // 2026-09-04: 공유 큐를 패턴 전용으로 분리(V026~V056·SplitSharedVfxCuesTool) — V001~V025 원본 +
            // 31개 분리분 = V001~V056 연속. 큐 값은 분리 시점 원본과 동일이라 인게임 그림은 무변경이고,
            // 얻은 것은 lab 편집이 다른 패턴으로 연쇄되지 않는 격리다.
            // 🔑 개수를 못 박지 않는다(2026-09-05): 큐가 늘 때마다 고쳐야 하는 숫자는 테스트가 아니라 핀이다.
            // 대신 <b>불변식</b>을 잰다 — 큐 id는 V001부터 빈 번호 없이 연속하고 중복이 없다.
            // 이 규약이 깨지면 「분리된 큐가 원본을 덮어썼다」·「번호를 건너뛰어 저작이 사라졌다」가 드러난다.
            var cueIds = bundle.VfxCues.Select(cue => cue.VfxCueId).ToArray();
            Assert.That(cueIds, Is.Unique);
            Assert.That(cueIds, Is.EqualTo(Enumerable.Range(1, cueIds.Length).Select(index => $"V{index:000}").ToArray()),
                "큐 id는 V001부터 빈 번호 없이 연속해야 한다.");
            // 핵심 불변식: 이제 어떤 큐도 두 패턴 이상이 공유하지 않는다(연쇄 편집의 근본 차단).
            foreach (var cueGroup in bundle.PatternVfxBindings.GroupBy(binding => binding.VfxCueId))
            {
                Assert.That(
                    cueGroup.Select(binding => binding.PatternId).Distinct().Count(),
                    Is.EqualTo(1),
                    $"cue {cueGroup.Key} must be bound by exactly one pattern (per-pattern cue policy).");
            }
            Assert.That(bundle.PatternVfxBindings.Where(binding => binding.PatternId == "A003").Select(binding => $"{binding.VfxCueId}:{binding.SpawnAnchor}"),
                Is.EqualTo(new[] { "V003:SourceAttack" }));
            Assert.That(bundle.PatternVfxBindings.Single(binding => binding.PatternId == "A003" && binding.VfxCueId == "V003").DelaySeconds, Is.EqualTo(0.2f).Within(0.0001f));
            foreach (var expected in new[]
                     {
                         ("V001", "SourceAttack"),
                         ("V002", "SourceAttack"),
                         ("V003", "SourceAttack"),
                         ("V004", "SourceAttack"),
                         ("V005", "SourceGround"),
                         // V006은 분리 후 keeper A018(SourceGround)만 남았다 — A057 몸부림은 전용 큐 V041로 분리됐다.
                         ("V006", "SourceGround"),
                         ("V007", "SourceAttack"),
                         ("V008", "SourceAttack"),
                         ("V009", "SourceAttack"),
                         ("V010", "SourceAttack"),
                         // V011(초장 브레스)은 2026-08-18 A020 삭제로 바인딩에서 빠졌다(큐 행은 CSV에 잔존).
                         ("V012", "SourceGround"),
                         // V013(균열)은 2026-08-18 A006이 A018 연출(V006+V014)로 통일되며 바인딩에서 빠졌다.
                         // 큐 행 자체는 CSV에 남아 있으므로 위의 V001~V019 목록 단언에는 계속 포함된다.
                         ("V014", "SourceGround"),
                         ("V015", "SourceGround"),
                         ("V016", "TargetGround"),
                         ("V017", "SourceAttack"),
                         ("V018", "SourceAttack"),
                         ("V019", "SourceAttack"),
                         // V020(2026-08-18): A028 지면 휩쓸기 = SourceGround.
                         // V021(미지의 장막)은 A046 삭제(2026-09-04 피드백)로 바인딩에서 빠졌다 —
                         // V011·V013 선례대로 큐 행은 잔존하므로 위의 V001~V026 목록 단언에는 계속 포함된다.
                         ("V020", "SourceGround"),
                         // V022~V025(터렛): 머즐은 시전자, 착탄은 타겟 몸통 — 사거리 1/2 차이를 앵커가 흡수한다.
                         ("V022", "SourceAttack"),
                         ("V023", "TargetHitCenter"),
                         ("V024", "TargetHitCenter"),
                         ("V025", "TargetHitCenter")
                     })
            {
                Assert.That(
                    bundle.PatternVfxBindings.Where(binding => binding.VfxCueId == expected.Item1)
                        .Select(binding => binding.SpawnAnchor)
                        .Distinct(),
                    Is.EqualTo(new[] { expected.Item2 }),
                    $"{expected.Item1} should use {expected.Item2}.");
            }
            // S005~S025 = 2026-09 발주 A 몬스터 타격음(cs:1374 반입). S024는 대상 패턴 A046이 삭제돼 큐 자체를 뺐다
            // (2026-09-05 사용자 결정). S001~S004는 임시 큐로 잔존한다(DEC-2026-07-26-02).
            Assert.That(
                bundle.SoundCues.Select(cue => cue.SoundCueId),
                Is.EqualTo(Enumerable.Range(1, 25).Where(index => index != 24).Select(index => $"S{index:000}")));
            var monster = bundle.MonsterCatalog.Entries.Single(entry => entry.Id == "M001");
            Assert.That(bundle.TryGetPatternPresentation("A003", out var presentation), Is.True);
            Assert.That(presentation.VfxCueId, Is.EqualTo("V003"));
            Assert.That(presentation.SoundWindupCueId, Is.EqualTo("S001"));
            Assert.That(presentation.SoundCastCueId, Is.EqualTo("S002"));
            Assert.That(presentation.SoundImpactCueId, Is.EqualTo("S006"), "A003 오른발톱 후려치기 = 불가살 발톱·참격 묶음.");
            Assert.That(presentation.AnimationTrigger, Is.EqualTo("Attack1"));
            Assert.That(monster.AttackPatterns[0].AnimationTrigger, Is.EqualTo("Attack1"));
            Assert.That(monster.AttackPatterns[1].AnimationTrigger, Is.EqualTo("Attack2"));

            var slowVfx = bundle.VfxCues.Single(cue => cue.VfxCueId == "V003");
            Assert.That(slowVfx.EffectKind, Is.EqualTo(EffectKind.Damage));
            Assert.That(slowVfx.PrefabPath, Is.EqualTo("Assets/Prefabs/Vfx/Combat/Monster/MonsterAttack_ClawSlash.prefab"));
            Assert.That(slowVfx.SourceRef, Is.EqualTo("monster.pattern.A003"));
            Assert.That(slowVfx.SpawnAnchor, Is.EqualTo(string.Empty));
            Assert.That(slowVfx.DesignerNote, Does.Contain("발톱 참격"));

            // followSourceAnchor(2026-08-18): V007 화염 브레스만 소스 앵커(머리) 추종. 나머지는 고정 스폰.
            Assert.That(bundle.VfxCues.Single(cue => cue.VfxCueId == "V007").FollowSourceAnchor, Is.True);
            Assert.That(bundle.VfxCues.Single(cue => cue.VfxCueId == "V006").FollowSourceAnchor, Is.False);

            var hitSound = bundle.SoundCues.Single(cue => cue.SoundCueId == "S003");
            Assert.That(hitSound.ClipPath, Is.EqualTo("Assets/Data/Audio/Clips/Placeholders/combat_player_hit_placeholder.wav"));
            Assert.That(hitSound.DesignerNote, Does.Contain("player hit impact"));
        }

        /// <summary>
        /// 2026-09 발주 A: 몬스터 패턴 타격음은 「타격형 = 그 요괴의 모든 피해 패턴 공용」이라 출하 패턴은
        /// 전부 실제 클립 큐를 물어야 한다. 예외는 저작상 「없음」(A900)과 보류된 터렛 사격(A017·A023·A024 —
        /// 클립이 오면 채운다)뿐이며, 그 넷은 빈 값(무음)이지 placeholder가 아니어야 한다.
        /// M013_버프형 클립은 대상 패턴 A046이 삭제돼 큐를 만들지 않았다(2026-09-05 사용자 결정).
        /// </summary>
        [Category("ShippingData")]
        [Test]
        public void ShippingPatternImpactCuesPointAtDeliveredClipsExceptTheHeldOnes()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CombatCsvPaths.MonsterDirectory, CombatCsvPaths.PresentationDirectory);
            var silentByDesign = new[] { "A900", "A017", "A023", "A024" };
            var clipByCue = bundle.SoundCues.ToDictionary(cue => cue.SoundCueId, cue => cue.ClipPath);

            foreach (var presentation in bundle.PatternPresentations)
            {
                if (silentByDesign.Contains(presentation.PatternId))
                {
                    Assert.That(presentation.SoundImpactCueId, Is.Empty, $"{presentation.PatternId}: 보류/없음 패턴은 빈 값이어야 한다(placeholder 금지).");
                    continue;
                }

                Assert.That(presentation.SoundImpactCueId, Is.Not.Empty, $"{presentation.PatternId}: 타격음이 비었다.");
                Assert.That(clipByCue.TryGetValue(presentation.SoundImpactCueId, out var clipPath), Is.True, presentation.PatternId);
                Assert.That(clipPath, Does.StartWith("Assets/Sounds/SFX/Monster/"), $"{presentation.PatternId} → {presentation.SoundImpactCueId}: 납품 클립이 아니다({clipPath}).");
                Assert.That(bundle.MonsterCatalog.TryGetPatternPresentation(presentation.PatternId, out var viaCatalog), Is.True,
                    $"{presentation.PatternId}: 런타임 카탈로그가 연출 저작을 못 든다 — 표현층이 타격음을 못 찾는다.");
                Assert.That(viaCatalog.SoundImpactCueId, Is.EqualTo(presentation.SoundImpactCueId));
            }
        }

        [Category("ShippingData")]
        [Test]
        public void MonsterVfxTuningWriterUpdatesTargetRowAndRoundTrips()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(
                    path,
                    "vfxCueId,effectKind,targetFilter,prefabPath,scaleMultiplier,scaleWithRadius,offsetX,offsetY,offsetZ,rotationX,rotationY,rotationZ,lifetimeOverride,sourceRef,matchSourceRefPrefix,spawnAnchor,designerNote\n" +
                    "V001,Damage,Player,Assets/Foo.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A001,false,Auto,keep me\n" +
                    "V002,Damage,Player,Assets/Bar.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A002,false,Auto,other row\n" +
                    "V003,StatusEffectApplied,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A003,false,Auto,third row\n" +
                    "V004,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A004,false,Auto,fixture row\n" +
                    "V005,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A005,false,Auto,fixture row\n" +
                    "V006,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A006,false,Auto,fixture row\n" +
                    "V007,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A007,false,Auto,fixture row\n" +
                    "V008,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A008,false,Auto,fixture row\n" +
                    "V009,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A009,false,Auto,fixture row\n" +
                    "V010,Damage,Player,Assets/Baz.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A010,false,Auto,fixture row\n");
                var beforeOtherRow = File.ReadAllLines(path)[2];

                CombatVfxCueTuningCsvWriter.UpdateFile(
                    path,
                    "V001",
                    new CombatVfxCueTuningUpdate(1.25f, false, 0.1f, 0.2f, 0.3f, 10f, 20f, 30f, 2.5f, "SourceAttack"));

                var source = new MonsterCatalogCsvSource(
                    Read("monster_catalog.csv"),
                    Read("monster_attack_patterns.csv"),
                    Read("monster_pattern_bindings.csv"),
                    File.ReadAllText(path),
                    Read("combat_sound_cues.csv"));
                var cue = MonsterCatalogCsvConverter.Convert(source).VfxCues.Single(item => item.VfxCueId == "V001");
                Assert.That(cue.ScaleMultiplier, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(cue.ScaleWithRadius, Is.False);
                Assert.That(cue.OffsetX, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(cue.OffsetY, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That(cue.OffsetZ, Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(cue.RotationX, Is.EqualTo(10f).Within(0.0001f));
                Assert.That(cue.RotationY, Is.EqualTo(20f).Within(0.0001f));
                Assert.That(cue.RotationZ, Is.EqualTo(30f).Within(0.0001f));
                Assert.That(cue.LifetimeOverride, Is.EqualTo(2.5f).Within(0.0001f));
                Assert.That(cue.SpawnAnchor, Is.EqualTo("SourceAttack"));
                Assert.That(File.ReadAllLines(path)[2], Is.EqualTo(beforeOtherRow));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }

        [Category("ShippingData")]
        [Test]
        public void MonsterVfxTuningWriterUpdatesPrefabPathOnlyWhenProvided()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(
                    path,
                    "vfxCueId,effectKind,targetFilter,prefabPath,scaleMultiplier,scaleWithRadius,offsetX,offsetY,offsetZ,rotationX,rotationY,rotationZ,lifetimeOverride,sourceRef,matchSourceRefPrefix,spawnAnchor,designerNote\n" +
                    "V001,Damage,Player,Assets/Foo.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A001,false,Auto,keep me\n" +
                    "V002,Damage,Player,Assets/Bar.prefab,1,true,0,0,0,0,0,0,0,monster.pattern.A002,false,Auto,other row\n");
                var beforeOtherRow = File.ReadAllLines(path)[2];

                CombatVfxCueTuningCsvWriter.UpdateFile(
                    path,
                    "V001",
                    new CombatVfxCueTuningUpdate(1f, true, 0f, 0f, 0f, 0f, 0f, 0f, 0f, prefabPath: "Assets/Repointed.prefab"));

                var lines = File.ReadAllLines(path);
                Assert.That(lines[1], Does.Contain("Assets/Repointed.prefab"));
                Assert.That(lines[1], Does.Not.Contain("Assets/Foo.prefab"));
                Assert.That(lines[2], Is.EqualTo(beforeOtherRow));

                // A null prefabPath (the default) must leave the repointed cell untouched.
                CombatVfxCueTuningCsvWriter.UpdateFile(
                    path,
                    "V001",
                    new CombatVfxCueTuningUpdate(1f, true, 0f, 0f, 0f, 0f, 0f, 0f, 0f));
                Assert.That(File.ReadAllLines(path)[1], Does.Contain("Assets/Repointed.prefab"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }

        [Category("ShippingData")]
        [Test]
        public void MonsterVfxTuningWriterRejectsMissingCueId()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(path, Read("combat_vfx_cues.csv"));
                var ex = Assert.Throws<ArgumentException>(() =>
                    CombatVfxCueTuningCsvWriter.UpdateFile(path, "V999", new CombatVfxCueTuningUpdate(1f, true, 0f, 0f, 0f, 0f, 0f, 0f, 0f)));
                Assert.That(ex.Message, Does.Contain("V999"));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }

        [Test]
        public void MonsterPatternVfxBindingWriterUpdatesSelectedAnchorOnly()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(
                    path,
                    "patternId,vfxCueId,spawnAnchor,order,enabled,designerNote\n" +
                    "A003,V003,SourceAttack,1,true,caster\n" +
                    "A003,V003,TargetHitCenter,2,true,target\n");
                var beforeSecond = File.ReadAllLines(path)[2];

                MonsterPatternVfxBindingCsvWriter.UpdateSpawnAnchor(path, "A003", "V003", 1, "SourceGround");

                var lines = File.ReadAllLines(path);
                Assert.That(lines[1], Does.Contain("SourceGround"));
                Assert.That(lines[2], Is.EqualTo(beforeSecond));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }

        [Category("ShippingData")]
        [Test]
        public void MonsterPatternVfxBindingWriterPersistsDelaySeconds()
        {
            var path = Path.GetTempFileName();
            try
            {
                File.WriteAllText(
                    path,
                    "patternId,vfxCueId,spawnAnchor,order,enabled,designerNote,delaySeconds\n" +
                    "A003,V003,SourceAttack,1,true,caster,0\n");

                MonsterPatternVfxBindingCsvWriter.UpdateSpawnAnchor(path, "A003", "V003", 1, "SourceAttack", createBackup: true, delaySeconds: 0.45f);

                var source = new MonsterCatalogCsvSource(
                    Read("monster_catalog.csv"),
                    Read("monster_attack_patterns.csv"),
                    Read("monster_pattern_bindings.csv"),
                    Read("combat_vfx_cues.csv"),
                    Read("combat_sound_cues.csv"),
                    File.ReadAllText(path));
                var binding = MonsterCatalogCsvConverter.Convert(source).PatternVfxBindings.Single(item => item.PatternId == "A003" && item.VfxCueId == "V003");
                Assert.That(binding.DelaySeconds, Is.EqualTo(0.45f).Within(0.0001f));
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
            }
        }

        [Category("ShippingData")]
        [Test]
        public void DesignerCsvReferencesExistingUnityAssetPaths()
        {
            var bundle = MonsterCatalogCsvConverter.ConvertDirectories(CsvDirectory, CombatCsvPaths.PresentationDirectory);
            var paths = bundle.MonsterCatalog.Entries
                .Select(entry => entry.VisualPrefabPath)
                .Concat(bundle.VfxCues.Select(cue => cue.PrefabPath))
                .Concat(bundle.SoundCues.Select(cue => cue.ClipPath))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct()
                .ToList();

            foreach (var path in paths)
            {
                Assert.That(File.Exists(path), Is.True, path);
            }
        }

        [Category("ShippingData")]
        [Test]
        public void MissingPatternBindingTargetReportsDesignerFriendlyError()
        {
            var source = new MonsterCatalogCsvSource(
                Read("monster_catalog.csv"),
                Read("monster_attack_patterns.csv"),
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\nM001,A999,1,true,,bad ref\n",
                Read("combat_vfx_cues.csv"),
                Read("combat_sound_cues.csv"));

            var ex = Assert.Throws<ArgumentException>(() => MonsterCatalogCsvConverter.Convert(source));
            Assert.That(ex.Message, Does.Contain("patternId"));
            Assert.That(ex.Message, Does.Contain("A999"));
        }

        [Category("ShippingData")]
        [Test]
        public void InvalidIdFormatReportsDesignerFriendlyError()
        {
            var source = new MonsterCatalogCsvSource(
                Read("monster_catalog.csv").Replace("M001", "three-eye-dog"),
                Read("monster_attack_patterns.csv"),
                Read("monster_pattern_bindings.csv").Replace("M001", "three-eye-dog"),
                Read("combat_vfx_cues.csv"),
                Read("combat_sound_cues.csv"));

            var ex = Assert.Throws<ArgumentException>(() => MonsterCatalogCsvConverter.Convert(source));
            Assert.That(ex.Message, Does.Contain("three-eye-dog"));
            Assert.That(ex.Message, Does.Contain("required format"));
        }

        [Category("ShippingData")]
        [Test]
        public void ParsesAttackPatternCooldownTurns()
        {
            var source = new MonsterCatalogCsvSource(
                "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath\n" +
                "M001,Tester,test-melee,B001,30,6,2,1,prototype,\n",
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,statusEffectDurationTurns,cooldownTurns\n" +
                "A001,Basic,1,3,attack.damage,player_in_range,V001,single,,2,0\n" +
                "A002,Stunner,1,3,attack.damage,player_in_range,V001,single,Stun,2,2\n",
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\n" +
                "M001,A001,1,true,,\n" +
                "M001,A002,2,true,,\n",
                Read("combat_vfx_cues.csv"),
                Read("combat_sound_cues.csv"));

            var monster = MonsterCatalogCsvConverter.Convert(source).MonsterCatalog.Entries.Single();
            Assert.That(monster.AttackPatterns.Single(pattern => pattern.Id == "A001").CooldownTurns, Is.EqualTo(0));
            Assert.That(monster.AttackPatterns.Single(pattern => pattern.Id == "A002").CooldownTurns, Is.EqualTo(2));
        }

        [Category("ShippingData")]
        [Test]
        public void ThrowsWhenMonsterHasNoCooldownZeroPattern()
        {
            var source = new MonsterCatalogCsvSource(
                "monsterId,displayName,archetype,behaviorProfileRef,hp,detectionRange,movePerTurn,attackSpeed,status,visualPrefabPath\n" +
                "M001,Tester,test-melee,B001,30,6,2,1,prototype,\n",
                "patternId,displayName,range,damage,effectRef,targeting,vfxCueId,shapeId,statusEffects,statusEffectDurationTurns,cooldownTurns\n" +
                "A001,Basic,1,3,attack.damage,player_in_range,V001,single,,2,1\n" +
                "A002,Stunner,1,3,attack.damage,player_in_range,V001,single,Stun,2,2\n",
                "monsterId,patternId,order,enabled,overrideWeight,designerNote\n" +
                "M001,A001,1,true,,\n" +
                "M001,A002,2,true,,\n",
                Read("combat_vfx_cues.csv"),
                Read("combat_sound_cues.csv"));

            var ex = Assert.Throws<ArgumentException>(() => MonsterCatalogCsvConverter.Convert(source));
            Assert.That(ex.Message, Does.Contain("cooldown-0"));
        }

        private static string Read(string fileName)
        {
            var directory = fileName == "combat_vfx_cues.csv" || fileName == "combat_sound_cues.csv"
                ? CombatCsvPaths.PresentationDirectory
                : CsvDirectory;
            return File.ReadAllText(Path.Combine(directory, fileName));
        }
    }
}



