using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 보스 HUD 뷰 계약. 프리팹 저작은 사람이 검증하지만(갤러리 캡처·실플레이), 뷰가 <b>규칙 상태를 어떻게
    /// 읽는가</b>는 여기서 고정한다: 보스 없으면 숨김, 있으면 이름·체력·페이즈 핍·지표 게이지,
    /// 그리고 어떤 그래픽도 raycast를 먹지 않는다는 것.
    ///
    /// 하네스는 출하 프리팹이 아니라 같은 이름 규약으로 만든 최소 계층이다 — 뷰의 이름 폴백 해소가
    /// 실제로 동작하는지까지 함께 검증된다(프리팹을 다시 만들어도 배선이 끊기지 않아야 한다).
    /// </summary>
    public sealed class BossHudViewTests
    {
        private const string BossDefinitionId = "M002";
        private const string BossUnitId = "boss-01";
        private const int BossHp = 60;

        private GameObject root;

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
                root = null;
            }
        }

        [Test]
        public void HidesItselfWhenThereIsNoState()
        {
            var view = CreateView();

            view.Refresh(null);

            Assert.That(view.IsVisible, Is.False);
            Assert.That(view.BoundBossUnitId, Is.Empty);
        }

        [Test]
        public void HidesItselfWhenTheCombatHasNoBoss()
        {
            var view = CreateView();

            view.Refresh(CreateStateWithoutBoss());

            Assert.That(view.IsVisible, Is.False);
        }

        [Test]
        public void ShowsTheAuthoredBossNameHealthAndPhaseFromTheRulesState()
        {
            var view = CreateView();
            var state = CreateBossState();

            view.Refresh(state);

            Assert.That(view.IsVisible, Is.True);
            Assert.That(view.BoundBossUnitId, Is.EqualTo(BossUnitId));
            Assert.That(FindText(BossHudView.NameLabelName).text, Is.EqualTo("불가살"), "프로필 표시명을 쓴다(정의 id 노출 금지).");
            Assert.That(FindText(BossHudView.BossTagLabelName).text, Is.EqualTo("보스"));
            Assert.That(FindText(BossHudView.HealthValueLabelName).text, Is.EqualTo($"{BossHp} / {BossHp}"));
            // 1페이즈: 첫 핍만 켜짐. 임계 0/1/2 프로필이라 다음 임계는 1.
            Assert.That(FindText(BossHudView.MetricLabelName).text, Is.EqualTo("경과 0 / 1"));
        }

        [Test]
        public void PhasePipsLightUpAsThePhaseRises()
        {
            var view = CreateView();
            var state = CreateBossState();
            view.Refresh(state);
            var pipsAtPhaseOne = LitPipCount();

            AdvanceToPhase(state, 2);
            view.Refresh(state);
            var pipsAtPhaseTwo = LitPipCount();

            AdvanceToPhase(state, 3);
            view.Refresh(state);
            var pipsAtPhaseThree = LitPipCount();

            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(3));
            Assert.That(new[] { pipsAtPhaseOne, pipsAtPhaseTwo, pipsAtPhaseThree }, Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void MetricGaugeCaptionIsEmptyOnTheFinalPhase()
        {
            var view = CreateView();
            var state = CreateBossState();
            AdvanceToPhase(state, 3);

            view.Refresh(state);

            Assert.That(state.BossPhases.Single().IsFinalPhase, Is.True);
            Assert.That(FindText(BossHudView.MetricLabelName).text, Is.Empty, "다음 페이즈가 없으면 게이지 캡션을 비운다.");
        }

        // ------------------------------------------------------------------------------------------
        // 전멸기 카운트다운 (§17)
        // ------------------------------------------------------------------------------------------

        [Test]
        public void AnnihilationCountdownIsEmptyWhileNothingIsTelegraphed()
        {
            var view = CreateView();

            view.Refresh(CreateBossState());

            Assert.That(
                FindText(BossHudView.AnnihilationLabelName).text, Is.Empty,
                "예고가 없을 때 남는 글자는 낡은 카운트다운으로 읽힌다.");
        }

        [Test]
        public void AnnihilationCountdownShowsRemainingTurnsAndDamage()
        {
            // 지금까지 남은 턴을 아는 방법은 타일의 '!' 표식을 보드에서 눈으로 세는 것뿐이었다.
            var view = CreateView();
            var state = CreateAnnihilationBossState();
            AdvanceToFirstAnnihilationTelegraph(state);
            var telegraph = state.GetBossAnnihilationTelegraphs().Single();

            view.Refresh(state);

            var label = FindText(BossHudView.AnnihilationLabelName);
            Assert.That(label.text, Does.Contain(telegraph.Damage.ToString()), "얼마나 아픈지가 빠지면 도망칠 이유가 안 읽힌다.");
            Assert.That(label.text, Does.Contain("전멸기"));
            Assert.That(
                telegraph.TurnsRemaining <= 1 ? label.text.Contains("임박") : label.text.Contains($"{telegraph.TurnsRemaining}턴"),
                Is.True,
                "남은 턴이 문면에 드러나야 한다.");
        }

        [Test]
        public void AnnihilationCountdownTurnsRedWhenItIsAboutToFire()
        {
            // 숫자만으로는 "곧이다"가 읽히지 않는다 — 색이 고조를 대신 말한다.
            var view = CreateView();
            var state = CreateAnnihilationBossState();
            AdvanceToFirstAnnihilationTelegraph(state);
            // §20-B에서 예고가 2턴(점프 턴 + 반응 턴)이 되었다 — 첫 예고는 아직 임박이 아니다.
            // 한 턴을 더 돌려 "다음 몬스터 행동에 터진다" 상태로 만든다.
            RunFullTurn(state);
            Assert.That(
                state.GetBossAnnihilationTelegraphs().Single().TurnsRemaining, Is.LessThanOrEqualTo(1),
                "전제: 예고가 한 턴 남은 상태 = 임박.");

            view.Refresh(state);

            var color = FindText(BossHudView.AnnihilationLabelName).color;
            Assert.That(color.r, Is.GreaterThan(0.9f));
            Assert.That(color.g, Is.LessThan(0.5f), "임박 색은 붉어야 한다(평시 흰색과 구분).");
        }

        [Test]
        public void NoGraphicAcceptsRaycastsSoTheTopBandNeverSwallowsMapClicks()
        {
            var view = CreateView();
            // 저작 실수를 흉내내서 켜 둔다 — 뷰가 빌드 시점에 강제로 내려야 한다.
            foreach (var graphic in view.GetComponentsInChildren<Graphic>(includeInactive: true))
            {
                graphic.raycastTarget = true;
            }

            view.Refresh(CreateBossState());

            Assert.That(
                view.GetComponentsInChildren<Graphic>(includeInactive: true).All(graphic => !graphic.raycastTarget),
                Is.True);
        }

        [Test]
        public void HidingIsAlphaOnlySoTheObjectStaysActiveForTheCinematicHider()
        {
            var view = CreateView();
            view.Refresh(CreateBossState());
            Assert.That(view.IsVisible, Is.True);

            view.Hide();

            Assert.That(view.IsVisible, Is.False);
            Assert.That(view.gameObject.activeSelf, Is.True, "SetActive로 숨기면 시네마틱 하이더의 복원 대상과 뒤섞인다.");
        }

        // ------------------------------------------------------------------------------------------

        private static void RunFullTurn(CombatState state)
        {
            state.EndAction();
            state.ResolveMonsterMovement();
            state.EndAction();
            state.ResolveMonsterAction();
        }

        /// <summary>
        /// 목표 페이즈에 도달할 때까지 턴을 돌린다. 지표(TurnCount)가 결의 시점에 평가되므로 "N턴 = N페이즈"가
        /// 아니라서, 턴 수를 세는 대신 조건으로 돌린다 — 임계값을 조정해도 테스트가 깨지지 않는다.
        /// </summary>
        private static void AdvanceToPhase(CombatState state, int targetPhase)
        {
            for (var guard = 0; guard < 20 && !state.IsTerminal; guard++)
            {
                if (state.BossPhases.Single().CurrentPhase >= targetPhase)
                {
                    return;
                }

                RunFullTurn(state);
            }

            Assert.That(state.BossPhases.Single().CurrentPhase, Is.EqualTo(targetPhase), "목표 페이즈에 도달하지 못했다.");
        }

        private int LitPipCount()
        {
            var pipRoot = root.GetComponentsInChildren<Transform>(true)
                .First(child => child.name == BossHudView.PhasePipRootName);
            // 켜진 핍은 금색(PhasePipOnColor)이고 꺼진 핍은 어두운 회청색이라 밝기로 구분한다.
            return pipRoot.GetComponentsInChildren<Image>(true).Count(pip => pip.color.r > 0.5f);
        }

        private TMP_Text FindText(string objectName) =>
            root.GetComponentsInChildren<TMP_Text>(true).First(text => text.name == objectName);

        /// <summary>출하 프리팹과 같은 이름 규약의 최소 계층. 직렬화 슬롯은 비워 두어 이름 폴백을 검증한다.</summary>
        private BossHudView CreateView()
        {
            root = new GameObject(BossHudView.RootObjectName, typeof(RectTransform), typeof(CanvasGroup));
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(760f, 96f);

            AddText(BossHudView.NameLabelName, rootRect);
            AddText(BossHudView.BossTagLabelName, rootRect);
            AddText(BossHudView.HealthValueLabelName, rootRect);
            AddText(BossHudView.MetricLabelName, rootRect);
            AddText(BossHudView.AnnihilationLabelName, rootRect);

            var healthTrack = AddImage(BossHudView.HealthTrackName, rootRect, new Vector2(700f, 22f));
            AddImage(BossHudView.HealthFillName, healthTrack.rectTransform, new Vector2(700f, 22f));

            var metricTrack = AddImage(BossHudView.MetricTrackName, rootRect, new Vector2(700f, 8f));
            AddImage(BossHudView.MetricFillName, metricTrack.rectTransform, new Vector2(0f, 8f));

            var pipRoot = new GameObject(BossHudView.PhasePipRootName, typeof(RectTransform));
            pipRoot.transform.SetParent(rootRect, false);
            for (var i = 0; i < 3; i++)
            {
                AddImage($"BossHud_PhasePip{i + 1}", (RectTransform)pipRoot.transform, new Vector2(12f, 12f));
            }

            return root.AddComponent<BossHudView>();
        }

        private static TMP_Text AddText(string name, RectTransform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go.AddComponent<TextMeshProUGUI>();
        }

        private static Image AddImage(string name, RectTransform parent, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.sizeDelta = size;
            return go.AddComponent<Image>();
        }

        /// <summary>예고가 처음 뜰 때까지 턴을 돌린다. 주기·예고 턴을 바꿔도 테스트가 깨지지 않게 조건으로 돈다.</summary>
        private static void AdvanceToFirstAnnihilationTelegraph(CombatState state)
        {
            for (var guard = 0; guard < 20 && !state.IsTerminal; guard++)
            {
                if (state.GetBossAnnihilationTelegraphs().Count > 0)
                {
                    return;
                }

                RunFullTurn(state);
            }

            Assert.Fail("전멸기 예고가 뜨지 않았다 — 픽스처를 고칠 것.");
        }

        /// <summary>
        /// 전멸기 단독 보스(아레나 없음 → fallback 원판). 1페이즈부터 활성, 주기 3·예고 1·피해 9.
        /// 관찰 대상은 HUD 문면이라 보스 공격은 피해 0이다.
        /// </summary>
        private static CombatState CreateAnnihilationBossState()
        {
            const string mechanicParams =
                "annihilationPhaseMin=1;annihilationIntervalTurns=4;annihilationTelegraphTurns=2;" +
                "annihilationDamage=9;annihilationCandidateCells=3;annihilationRealSafeCells=2;" +
                "annihilationCandidateSpacing=3;annihilationSafeReach=2;annihilationFallbackRadius=3;" +
                "annihilationLandingBlastRadius=1;annihilationLandingBlastDamage=0";
            const string profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + ",불가살,TurnCount,annihilation," + mechanicParams + ",,,music.boss.bulgasal,\n";
            const string phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n";

            return new CombatState(
                CombatState.CreateDemoMap(7),
                new HexCoord(1, 0),
                new[] { new MonsterConfig(BossUnitId, new HexCoord(3, 0), BossHp, definitionId: BossDefinitionId, spawnRole: MonsterSpawnRoles.Boss) },
                new CombatConfig(200, BossHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: new MonsterCatalogDefinition(
                    "boss-hud-annihilation-catalog",
                    "Boss HUD Annihilation Catalog",
                    new[]
                    {
                        new MonsterCatalogEntry(
                            BossDefinitionId,
                            "불가살",
                            "test-melee",
                            "B001",
                            detectionRange: 8,
                            movePerTurn: 0,
                            hp: BossHp,
                            attackSpeed: 1,
                            attackPatterns: new[] { new MonsterAttackPattern("A100", "발톱", 1, 0, 0, cooldownTurns: 0, phaseMin: 0) })
                    }),
                bossCatalog: BossCatalogCsvConverter.Convert(
                    new BossCatalogCsvSource(profiles, phases, "boss-hud-annihilation", "Boss HUD Annihilation")),
                drawOpeningHands: false);
        }

        private static CombatState CreateStateWithoutBoss()
        {
            return new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default);
        }

        private static CombatState CreateBossState()
        {
            var playerCoord = new HexCoord(0, 0);
            var bossCoord = new HexCoord(2, 0);
            var map = new HexMapData(HexArea.CellsWithin(new HexCoord(1, 0), 3)
                .Select(coord => new HexCellData(coord, "tile", "street", 1, true, false))
                .ToList());

            var monsterCatalog = new MonsterCatalogDefinition(
                "boss-hud-test-catalog",
                "Boss HUD Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        BossDefinitionId,
                        "불가살",
                        "test-melee",
                        "B001",
                        detectionRange: 6,
                        movePerTurn: 1,
                        hp: BossHp,
                        attackPatterns: new[] { new MonsterAttackPattern("A100", "발톱", 1, 0, 2) })
                });

            const string profiles =
                BossCsv.ProfilesHeader + "\n" +
                BossDefinitionId + ",불가살,TurnCount,,,,,music.boss.bulgasal,\n";
            const string phases =
                BossCsv.PhasesHeader + "\n" +
                BossDefinitionId + ",1,0,0,0,0,1,0,,\n" +
                BossDefinitionId + ",2,1,0,0,1,1,0,,\n" +
                BossDefinitionId + ",3,2,0,0,2,1,0,,\n";

            return new CombatState(
                map,
                playerCoord,
                new[] { new MonsterConfig(BossUnitId, bossCoord, BossHp, definitionId: BossDefinitionId, spawnRole: MonsterSpawnRoles.Boss) },
                new CombatConfig(200, BossHp, 2, 1, 4, 4, 5, 1, 3, playerVisionRange: 6),
                monsterCatalog: monsterCatalog,
                bossCatalog: BossCatalogCsvConverter.Convert(
                    new BossCatalogCsvSource(profiles, phases, "boss-hud-test", "Boss HUD Test")),
                drawOpeningHands: false);
        }
    }
}
