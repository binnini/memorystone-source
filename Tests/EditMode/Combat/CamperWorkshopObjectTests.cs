#if UNITY_EDITOR
using System;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.CardCore;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 캠핑카·공작소(camper-workshop-plan.md P2·P3). 상점 GAP-2(배선 테스트 부재)를 반복하지 않도록
    /// 트리거→열기→서비스→소비 경로를 컨트롤러 레벨에서 처음부터 감시한다. 규칙(회복 30% 반올림·
    /// 오버힐 클램프·연마 후보 존재 판정)은 CombatState 레벨.
    /// </summary>
    public sealed class CamperWorkshopObjectTests
    {
        [TearDown]
        public void CleanUpPopups()
        {
            foreach (var popup in Object.FindObjectsByType<ServiceObjectPopupView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                Object.DestroyImmediate(popup.gameObject);
            }
        }

        // ── CombatState 규칙 ─────────────────────────────────────────────────────────

        [Test]
        public void CamperHealIsThirtyPercentOfMaxHpRoundedAwayFromZeroWithFloorOne()
        {
            var state = CombatState.CreateDefaultDemo();
            Assert.That(state.Player.MaxHp, Is.EqualTo(80), "기본 MaxHp 전제 — 바뀌면 아래 기대값도 갱신.");
            Assert.That(state.GetCamperHealAmount(), Is.EqualTo(24), "80의 30% = 24.");

            RaiseMaxHpTo(state, 81);
            Assert.That(state.GetCamperHealAmount(), Is.EqualTo(24), "81의 30% = 24.3 → 반올림 24.");

            RaiseMaxHpTo(state, 95);
            Assert.That(state.GetCamperHealAmount(), Is.EqualTo(29), "95의 30% = 28.5 → 반올림 29 (AwayFromZero — 은행가 반올림이면 28이 된다).");

            var tinyState = new CombatState(
                CombatState.CreateDemoMap(2), new HexCoord(0, 0), new HexCoord(1, 0),
                new CombatConfig(1, 30, 2, 1, 4, 4, 6, 1, 5, 4, 1, 5, 7, 4));
            Assert.That(tinyState.GetCamperHealAmount(), Is.EqualTo(1), "최소 1 보장(D-4).");
        }

        [Test]
        public void CamperHealClampsAtMaxHpAndReportsTheActualAmount()
        {
            var state = CombatState.CreateDefaultDemo();
            state.Player.ApplyDamage(3);

            Assert.That(state.TryUseCamperHeal(out var healed, out var reason), Is.True, reason);
            Assert.That(healed, Is.EqualTo(3), "회복량 24라도 실제로 오른 양은 잃은 3까지다(오버힐 클램프).");
            Assert.That(state.Player.Hp, Is.EqualTo(state.Player.MaxHp));

            Assert.That(state.TryUseCamperHeal(out healed, out reason), Is.True, "만피 사용은 막지 않는다 — 택1의 대가는 플레이어 선택이다.");
            Assert.That(healed, Is.Zero);
        }

        [Test]
        public void CamperHealRaisesTheSameHealEffectCardsDo()
        {
            // 🔴 2026-09-01 #11: 회복은 일어났는데 화면에는 아무것도 없었다. 규칙이 Player.Heal만 부르고
            //    이펙트를 안 올린 탓이다 — 회복 플로팅 「+N」은 정책상 절대 숨지 않으므로(카탈로그를
            //    잘못 저작해도 안 숨는다), 안 보였다는 것은 곧 「그 경로를 안 지났다」는 뜻이었다.
            var state = CombatState.CreateDefaultDemo();
            state.Player.ApplyDamage(5);
            var seen = new System.Collections.Generic.List<EffectResultEvent>();
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.Heal)
                {
                    seen.Add(resultEvent);
                }
            };

            Assert.That(state.TryUseCamperHeal(out var healed, out _), Is.True);

            var heal = seen.Single();
            Assert.That(heal.AppliedAmount, Is.EqualTo(healed), "플로팅 수치는 실제로 오른 양이다(오버힐 아님).");
            Assert.That(heal.TargetUnitId, Is.EqualTo("player"));
            Assert.That(heal.SourceRef, Is.EqualTo(CardEffectRefs.FieldHeal),
                "카드 회복과 같은 ref — 카탈로그의 스케일·오프셋을 그대로 물려받는다.");
        }

        [Test]
        public void CamperHealAtFullHpStaysSilent()
        {
            // 「+0」은 정보가 아니라 잡음이다. 만피 사용을 막지는 않되(택1의 대가는 플레이어 선택),
            // 화면에 아무 말도 하지 않는다.
            var state = CombatState.CreateDefaultDemo();
            var seen = 0;
            state.EffectResolved += resultEvent =>
            {
                if (resultEvent.Kind == EffectKind.Heal)
                {
                    seen++;
                }
            };

            Assert.That(state.TryUseCamperHeal(out var healed, out _), Is.True);

            Assert.That(healed, Is.Zero, "전제: 만피라 오른 양이 없다.");
            Assert.That(seen, Is.Zero);
        }

        [Test]
        public void TheHeartOnTheCamperOptionIsTheSameOneTheHudUses()
        {
            // 2026-09-01 #11(사용자 지정): 캠핑카 체력 표시의 하트는 <b>하단 HUD 체력 칸이 쓰는 그 그림</b>이다.
            // 🔴 정본은 RuntimeUiAssetCatalog다 — 런타임 생성 뷰가 AssetDatabase로 찾으면 에디터에서만
            //    살아 있고 빌드에서 조용히 null이 된다(2026-08-19 #C의 전례). 그래서 카탈로그에
            //    실려 있는지를 재고, 실제 파일까지 대조한다.
            var catalog = SeoulPlayup.Cards.Unity.RuntimeUiAssetCatalog.LoadDefault();
            Assert.That(catalog, Is.Not.Null, "런타임 UI 카탈로그를 Resources에서 못 읽었다.");
            Assert.That(catalog.HealthIconSprite, Is.Not.Null,
                "체력 하트가 카탈로그에 없다 — 빌드에서 캠핑카 체력 줄의 아이콘이 사라진다.");

            var hudIcon = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Art/UI/Icons/ui_health.png");
            Assert.That(hudIcon, Is.Not.Null, "HUD 체력 아이콘 에셋을 찾지 못했다.");
            Assert.That(catalog.HealthIconSprite, Is.EqualTo(hudIcon),
                "캠핑카가 HUD와 <b>다른</b> 하트를 쓰면 「체력」이 화면마다 다른 물건으로 읽힌다.");
        }

        [Test]
        public void CamperHealOptionTellsTheCurrentHealth()
        {
            // 2026-09-01 #11: 회복량만으로는 「지금 쓸 것인가」를 정할 수 없다 — 이 화면이 사이드바를
            // 덮어 체력을 볼 길이 없으므로 선택지가 직접 말한다.
            // 🔑 두 줄이다(2026-09-01 사용자 지정): 첫 줄이 「지금 내 체력」, 둘째 줄이 「누르면 무슨 일이」.
            //    한 줄로 이으면 폭에 밀려 아무 데서나 접히고 「(+29)」만 홀로 남는다 — 줄바꿈을 뜻으로 정한다.
            var wounded = MapCombatController.FormatCamperHealDetail(hp: 68, maxHp: 95, healAmount: 29)
                .Split('\n');
            Assert.That(wounded, Has.Length.EqualTo(2), "체력 줄과 효과 줄은 갈라져야 한다.");
            Assert.That(wounded[0], Is.EqualTo("체력 68/95"), "첫 줄은 현재 체력만 말한다.");
            Assert.That(wounded[1], Does.Contain("+29"), "둘째 줄이 무슨 일이 생기는지 말한다.");
            Assert.That(wounded[1], Does.Not.Contain("68/95"), "같은 사실을 두 줄이 겹쳐 말하지 않는다.");

            var full = MapCombatController.FormatCamperHealDetail(hp: 95, maxHp: 95, healAmount: 29)
                .Split('\n');
            Assert.That(full, Has.Length.EqualTo(2), "만피에도 줄 구조는 같다.");
            Assert.That(full[0], Is.EqualTo("체력 95/95"));
            Assert.That(full[1], Does.Contain("가득"), "만피에는 「+0」 대신 그 사실을 말한다.");
            Assert.That(full[1], Does.Not.Contain("+29"), "쓸 수 없는 수치를 광고하지 않는다.");
        }

        [Test]
        public void MaxHpBonusRelicsNaturallyRaiseTheCamperHeal()
        {
            var state = CombatState.CreateDefaultDemo();
            var before = state.GetCamperHealAmount();
            state.Player.IncreaseMaxHp(20);
            Assert.That(state.GetCamperHealAmount(), Is.EqualTo(30).And.GreaterThan(before),
                "D-4: 회복이 최대 체력 비율이라 최대 체력 증가 유물과 자연 연동된다.");
        }

        [Test]
        public void HasRefinableCardTracksTheRefineGate()
        {
            var state = CreateRefineState();
            Assert.That(state.HasRefinableCard(), Is.True);

            Assert.That(state.TryRefineCard("A01", out var reason), Is.True, reason);
            Assert.That(state.HasRefinableCard(), Is.False, "유일한 연마 후보를 연마하면 게이트가 닫혀야 한다.");
        }

        // ── 컨트롤러 배선: 트리거 → 열기 → 서비스 → 소비 ───────────────────────────────

        [Test]
        public void MovingOntoCamperVanOpensTheServicePopupWithHealAndRefineOptions()
        {
            RunControllerScenario("CamperVan", "camper-van-test", (controller, view) =>
            {
                Assert.That(view.IsOpen, Is.True, "캠핑카를 밟으면 서비스 모달이 열려야 한다.");
                Assert.That(FindButtonByLabel(view, "체력 회복"), Is.Not.Null);
                var refineButton = FindButtonByLabel(view, "카드 연마");
                Assert.That(refineButton, Is.Not.Null);
                Assert.That(refineButton.interactable, Is.False,
                    "출하 CSV에는 아직 연마 저작이 없으므로(P4 예정) 연마 선택지는 비활성이어야 한다 — HasRefinableCard 배선 검증.");
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Not.Contain("camper-van-test"),
                    "여는 것만으로는 소비되지 않는다 — 소비는 떠나는 순간이다.");
            });
        }

        [Test]
        public void LeavingTheCamperVanConsumesItEvenWithoutUsingAService()
        {
            RunControllerScenario("CamperVan", "camper-van-test", (controller, view) =>
            {
                ClickButtonByLabel(view, "떠나기");

                Assert.That(view.IsOpen, Is.False);
                Assert.That(controller.State.ClaimedEventObjectIds, Does.Contain("camper-van-test"),
                    "D-5: 이용 여부와 무관하게 떠나면 소비된다(상점 CR-6과 같은 원칙).");

                // 소비된 오브젝트는 다시 밟아도 열리지 않는다 — 플레이어가 그 칸에 선 채로
                // 트리거 체인을 다시 돌려도 아무것도 발동하지 않아야 한다.
                var triggered = (bool)typeof(MapCombatController)
                    .GetMethod("ResolveTileInteractionAtPlayerCoord",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Invoke(controller, null);
                Assert.That(triggered, Is.False, "소비 후 재진입 불가.");
                Assert.That(view.IsOpen, Is.False);
            });
        }

        [Test]
        public void CamperHealServiceHealsThenClosesAndConsumes()
        {
            RunControllerScenario("CamperVan", "camper-van-test", (controller, view) =>
            {
                var state = controller.State;
                state.Player.ApplyDamage(10);
                var expected = Math.Min(state.GetCamperHealAmount(), state.Player.MaxHp - state.Player.Hp);
                var hpBefore = state.Player.Hp;

                ClickButtonByLabel(view, "체력 회복");

                Assert.That(state.Player.Hp, Is.EqualTo(hpBefore + expected));
                Assert.That(view.IsOpen, Is.False, "택1: 서비스 하나를 쓰면 방문이 끝난다.");
                Assert.That(state.ClaimedEventObjectIds, Does.Contain("camper-van-test"));
            });
        }

        /// <summary>
        /// 공작소는 은퇴했다(2026-08-31 사용자 확정 · <see cref="ServiceObjectAvailability"/>).
        ///
        /// <para>
        /// 🔴 이 시험이 무는 것은 「기능이 사라졌다」가 아니라 <b>밟아도 아무 일이 없다</b>이다 —
        /// 데이터에는 공작소가 그대로 남아 있으므로, 스위치가 뒤집히면(또는 게이트가 한쪽만 걸리면)
        /// 은퇴한 화면이 실플레이에 다시 뜬다. 되살릴 때는 이 시험을 종전 것으로 되돌리면 된다.
        /// </para>
        /// </summary>
        [Test]
        public void RetiredWorkshopNeitherOpensNorConsumes()
        {
            Assert.That(
                ServiceObjectAvailability.WorkshopEnabled,
                Is.False,
                "공작소를 되살렸다면 이 시험을 종전의 제거 서비스 시험으로 되돌릴 것.");

            var host = new GameObject("Workshop retired test");
            var layers = new GameObject(GameplaySceneContract.GameplayLayerRootName, typeof(RectTransform));
            try
            {
                var controller = host.AddComponent<MapCombatController>();
                controller.ConfigurePresentationForTests(immediateSequences: true);
                controller.ConfigureMapForTests(CreateServiceObjectMap("Workshop", "workshop-test"));
                controller.InitializeIntegration();

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True, "칸 자체는 여전히 지나갈 수 있어야 한다.");

                var view = Object.FindFirstObjectByType<ServiceObjectPopupView>(FindObjectsInactive.Include);
                Assert.That(
                    view == null || !view.IsOpen,
                    Is.True,
                    "은퇴한 공작소를 밟아도 모달이 열려서는 안 된다.");
                Assert.That(
                    controller.State.ClaimedEventObjectIds,
                    Does.Not.Contain("workshop-test"),
                    "열리지 않았으니 소비도 없어야 한다 — 소비만 되면 조용히 사라지는 칸이 된다.");
            }
            finally
            {
                Object.DestroyImmediate(layers);
                Object.DestroyImmediate(host);
            }
        }

        // ── helpers ───────────────────────────────────────────────────────────────────

        private static void RunControllerScenario(
            string objectType,
            string objectId,
            Action<MapCombatController, ServiceObjectPopupView> scenario)
        {
            var host = new GameObject($"{objectType} wiring test");
            var layers = new GameObject(GameplaySceneContract.GameplayLayerRootName, typeof(RectTransform));
            try
            {
                var controller = host.AddComponent<MapCombatController>();
                controller.ConfigurePresentationForTests(immediateSequences: true);
                controller.ConfigureMapForTests(CreateServiceObjectMap(objectType, objectId));
                controller.InitializeIntegration();

                Assert.That(controller.BeginMoveSelection(), Is.True);
                Assert.That(controller.TryMoveTo(new HexCoord(1, 0)), Is.True);

                var view = Object.FindFirstObjectByType<ServiceObjectPopupView>(FindObjectsInactive.Include);
                Assert.That(view, Is.Not.Null, "서비스 모달이 생성돼야 한다.");
                scenario(controller, view);
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(layers);
            }
        }

        private static HexMapData CreateServiceObjectMap(string objectType, string objectId)
        {
            var cells = new[]
            {
                new HexCellData(new HexCoord(0, 0), "plain", "plain", 1, true, false),
                new HexCellData(new HexCoord(1, 0), "plain", "plain", 1, true, false),
                new HexCellData(new HexCoord(2, 0), "plain", "plain", 1, true, false)
            };
            var objects = new[]
            {
                new HexMapObjectData(
                    objectId,
                    objectType,
                    objectType == "CamperVan" ? "camper_van_tmp" : "workshop_tmp",
                    new HexCoord(1, 0),
                    interactable: true)
            };
            return new HexMapData(cells, objectRefs: objects);
        }

        private static void RaiseMaxHpTo(CombatState state, int targetMaxHp)
        {
            var delta = targetMaxHp - state.Player.MaxHp;
            Assert.That(delta, Is.GreaterThanOrEqualTo(0), $"기본 MaxHp({state.Player.MaxHp})가 목표({targetMaxHp})보다 크다 — 테스트 전제 갱신 필요.");
            if (delta > 0)
            {
                state.Player.IncreaseMaxHp(delta);
            }
        }

        private static CombatState CreateRefineState()
        {
            var upgraded = new CardCatalogEntry(
                "A01", "테스트 공격+", CardCategory.Action, CardEffectType.Attack,
                1, 1, 5, CardEffectRefs.AttackDamage, "living_monster_in_range", status: CardCatalogStatus.Approved);
            var catalog = new CardCatalogDefinition(
                "camper-refine-test",
                "Camper refine test catalog",
                new[]
                {
                    new CardCatalogEntry(
                        ApprovedCardCatalogFactory.MoveBasicId, "Move", CardCategory.Movement, CardEffectType.Move,
                        1, 2, 2, CardEffectRefs.MoveBasic, "reachable_known_hex", status: CardCatalogStatus.Approved),
                    new CardCatalogEntry(
                        "A01", "테스트 공격", CardCategory.Action, CardEffectType.Attack,
                        1, 1, 3, CardEffectRefs.AttackDamage, "living_monster_in_range",
                        status: CardCatalogStatus.Approved, upgradedEntry: upgraded),
                });
            return new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default,
                cardCatalog: catalog);
        }

        private static Button FindButtonByLabel(ServiceObjectPopupView view, string labelFragment)
        {
            return view.GetComponentsInChildren<Button>(includeInactive: true)
                .FirstOrDefault(button =>
                    button.GetComponentsInChildren<TMP_Text>(includeInactive: true)
                        .Any(text => text.text.Contains(labelFragment)));
        }

        private static void ClickButtonByLabel(ServiceObjectPopupView view, string labelFragment)
        {
            var button = FindButtonByLabel(view, labelFragment);
            Assert.That(button, Is.Not.Null, $"'{labelFragment}' 버튼이 있어야 한다.");
            button.onClick.Invoke();
        }
    }
}
#endif
