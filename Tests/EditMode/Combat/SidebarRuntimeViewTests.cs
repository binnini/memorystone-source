#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class SidebarRuntimeViewTests
    {
        /// <summary>
        /// 출하 프리팹의 사이드바 버튼이 <b>정말 눌리는가</b>(2026-09-01 실플레이 #2).
        ///
        /// <para>🔴🔴 가방·유물 버튼의 <c>Button</c> <b>컴포넌트가 꺼져</b> 있었다. 이 고장은 어떤 기존
        /// 검사에도 걸리지 않는다: GameObject는 켜져 있고, <c>interactable</c> 플래그도 참이며, 콜아웃
        /// 배선(key·controller·panel)도 전부 성했다. 화면에도 아무 차이가 없다 — 버튼은 평소처럼 그려진다.
        /// 다만 <c>Selectable.IsActive()</c>가 <c>enabled &amp;&amp; activeInHierarchy</c>라서 <c>Press()</c>가
        /// 조용히 되돌아가고, 클릭만 아무 일도 하지 않는다.</para>
        ///
        /// <para>🔑 그래서 재는 것은 <c>interactable</c>이 아니라 <c>enabled</c>다. 실측으로 가른 방법도
        /// 같다: <c>onClick.Invoke()</c>는 열리는데 <c>ExecuteEvents.pointerClickHandler</c>는 안 열렸다 —
        /// 배선이 아니라 <b>입력 관문</b>이 막힌 모양이다.</para>
        ///
        /// <para>⚠️ 재화 버튼은 <b>일부러</b> 꺼져 있다(표시 전용 — 콜아웃 패널이 없다). 예외를 목록으로
        /// 적어 두는 이유는, 「전부 켜라」로 고치면 그 의도가 다음 사람 손에 지워지기 때문이다.</para>
        /// </summary>
        /// <summary>
        /// 아이템을 쓴 뒤 설명창이 안 사라지는 결함(2026-09-01 실플레이 #7)의 핀.
        ///
        /// <para>🔴 툴팁은 「들어왔다/나갔다」 <b>이벤트</b>로만 여닫혔다. 소모품을 다 쓰면 칸이 비면서
        /// 콜백만 지워지는데(<c>Bind(null, null)</c>), 포인터는 그대로 안에 있으니 나가는 이벤트가
        /// 영영 오지 않고 닫을 콜백마저 사라져 설명창이 화면에 남았다.</para>
        ///
        /// <para>🔑 가방과 유물 칸이 <b>이 컴포넌트 하나</b>를 공유하므로 고치는 자리도 하나다 —
        /// 패널 쪽에서 고치면 두 벌이 되고, 한쪽만 고치면 다른 쪽에 같은 버그가 남는다.</para>
        /// </summary>
        [Test]
        public void EmptyingASlotUnderTheCursorClosesItsTooltip()
        {
            var host = new GameObject("hover-slot");
            try
            {
                var hover = host.AddComponent<SidebarRelicSlotHover>();
                var shown = 0;
                var hidden = 0;
                hover.Bind(() => shown++, () => hidden++);

                hover.OnPointerEnter(null);
                Assert.That(shown, Is.EqualTo(1), "전제: 칸에 들어가면 설명창이 뜬다.");
                Assert.That(hidden, Is.Zero);

                // 다 써서 칸이 비었다 — 패널이 콜백을 지운다. 포인터는 아직 안에 있다.
                hover.Bind(null, null);

                Assert.That(hidden, Is.EqualTo(1),
                    "칸이 비면 떠 있던 설명창을 닫아야 한다 — 안 닫으면 마우스를 빼도 닫을 콜백이 없다.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void RebindingASlotUnderTheCursorRefreshesItsTooltip()
        {
            // 개수가 줄기만 한 경우도 같은 구멍이었다 — 「3개 보유」가 그대로 떠 있었다.
            var host = new GameObject("hover-slot-rebind");
            try
            {
                var hover = host.AddComponent<SidebarRelicSlotHover>();
                var log = new List<string>();
                hover.Bind(() => log.Add("show:3개"), () => log.Add("hide:3개"));
                hover.OnPointerEnter(null);

                hover.Bind(() => log.Add("show:2개"), () => log.Add("hide:2개"));

                Assert.That(log, Is.EqualTo(new[] { "show:3개", "hide:3개", "show:2개" }),
                    "내용이 바뀌면 옛 설명을 닫고 새 설명을 다시 연다 — 마우스를 움직이지 않아도 화면이 사실을 따라온다.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void LeavingASlotThatWasNeverEnteredDoesNotFireAnExit()
        {
            // 역방향 가드: 「언제나 닫는다」로 고치면 안 뜬 툴팁을 닫으러 다니게 되고,
            // 이웃 칸이 띄운 창을 남의 Bind가 꺼 버린다(프리젠터는 한 개를 나눠 쓴다).
            var host = new GameObject("hover-slot-cold");
            try
            {
                var hover = host.AddComponent<SidebarRelicSlotHover>();
                var hidden = 0;
                hover.Bind(() => { }, () => hidden++);

                hover.Bind(null, null);

                Assert.That(hidden, Is.Zero, "포인터가 안에 없던 칸은 닫을 것이 없다.");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        [Category("ShippingData")]
        public void ShippedSidebarPrefabKeepsItsPanelButtonsClickable()
        {
            const string prefabPath = "Assets/Prefabs/UI/Prototype/SidebarSystem.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"사이드바 프리팹을 찾지 못했다: {prefabPath}");

            // 표시 전용이라 눌리지 않는 것이 정상인 버튼.
            var displayOnly = new HashSet<string> { "Sidebar Button currency" };

            var panelButtons = prefab.GetComponentsInChildren<SidebarPanelButton>(includeInactive: true);
            Assert.That(panelButtons, Is.Not.Empty, "사이드바에 패널 버튼이 하나도 없다 — 프리팹이 갈렸다.");

            foreach (var panelButton in panelButtons)
            {
                if (displayOnly.Contains(panelButton.gameObject.name))
                {
                    continue;
                }

                var button = panelButton.GetComponent<Button>();
                Assert.That(button, Is.Not.Null, $"{panelButton.gameObject.name}: Button 컴포넌트가 없다.");
                Assert.That(button.enabled, Is.True,
                    $"{panelButton.gameObject.name}: Button 컴포넌트가 꺼져 있다 — 버튼은 평소처럼 그려지지만 "
                    + "클릭이 Selectable.IsActive()에서 조용히 막힌다(2026-09-01 가방·유물이 이 상태였다).");
                Assert.That(button.interactable, Is.True, $"{panelButton.gameObject.name}: interactable이 꺼져 있다.");
                Assert.That(panelButton.gameObject.activeSelf, Is.True, $"{panelButton.gameObject.name}: 오브젝트가 꺼져 있다.");
                Assert.That(string.IsNullOrEmpty(panelButton.PanelKey), Is.False,
                    $"{panelButton.gameObject.name}: panelKey가 비어 있다 — 눌러도 열 판을 모른다.");
            }
        }

        [Test]
        public void RefreshProjectsCombatPlayerStateIntoSidebarPanels()
        {
            var roots = new List<GameObject>();
            try
            {
                var sidebar = CreateRect("Sidebar", null, roots);
                var settings = sidebar.gameObject.AddComponent<SidebarCalloutPanelSettings>();
                var controller = sidebar.gameObject.AddComponent<SidebarCalloutPanelController>();
                var view = sidebar.gameObject.AddComponent<SidebarRuntimeView>();
                var panelLayer = CreateRect("Sidebar Callout Panel Layer", null, roots);

                var entries = new[]
                {
                    CreateEntry("bag", panelLayer),
                    CreateEntry("deck", panelLayer),
                    CreateEntry("relic_curse", panelLayer),
                    CreateEntry("settings", panelLayer)
                };
                controller.Bind(sidebar, panelLayer, entries);
                view.Bind(controller, settings);

                var state = CreateState();
                view.Refresh(state, state.CreatePlayerStateSnapshot(), null);

                // 리뉴얼 이후 칸은 그림 한 장이고 이름은 호버 상세 줄이 말한다 — 칸에서 확인할 수 있는
                // 것은 「몇 칸 중 몇 칸을 썼는가」와 겹친 개수다.
                AssertPanelContains(entries, "bag", "1 / 3 슬롯");
                AssertPanelContains(entries, "bag", "×2");
                AssertPanelDoesNotContainRuntimeRoot(entries, "bag");
                AssertPanelContains(entries, "deck", "Move 1");
                AssertPanelDoesNotContainRuntimeRoot(entries, "deck");
                // 효과 문구는 칩이 아니라 상세 줄이 말한다 — 올려 봐야 확인된다.
                AssertHoverRevealsText(entries, "relic_curse", "공격 피해 +1");
                AssertHoverRevealsText(entries, "bag", "debug-token");
                AssertPanelDoesNotContainRuntimeRoot(entries, "relic_curse");
                // Settings panel was redesigned to host sound settings only (SoundSettingsPanelView);
                // it no longer projects combat-state text rows, so only the structural contracts apply.
                AssertPanelDoesNotContainRuntimeRoot(entries, "settings");
                AssertRuntimePanelsUseDefaultImages(entries, "settings");
            }
            finally
            {
                foreach (var root in roots.Where(root => root != null))
                {
                    Object.DestroyImmediate(root);
                }

                DestroyTooltipHud();
            }
        }

        [Test]
        public void RefreshRetriesWhenPanelBindingsArriveAfterState()
        {
            var roots = new List<GameObject>();
            try
            {
                var sidebar = CreateRect("Sidebar", null, roots);
                var settings = sidebar.gameObject.AddComponent<SidebarCalloutPanelSettings>();
                var controller = sidebar.gameObject.AddComponent<SidebarCalloutPanelController>();
                var view = sidebar.gameObject.AddComponent<SidebarRuntimeView>();
                var state = CreateState();

                view.Refresh(state, state.CreatePlayerStateSnapshot(), null);

                var panelLayer = CreateRect("Sidebar Callout Panel Layer", null, roots);
                var entries = new[]
                {
                    CreateEntry("bag", panelLayer),
                    CreateEntry("deck", panelLayer),
                    CreateEntry("relic_curse", panelLayer),
                    CreateEntry("settings", panelLayer)
                };
                controller.Bind(sidebar, panelLayer, entries);
                view.Bind(controller, settings);
                view.Refresh(state, state.CreatePlayerStateSnapshot(), null);

                // 리뉴얼 이후 칸은 그림 한 장이고 이름은 호버 상세 줄이 말한다 — 칸에서 확인할 수 있는
                // 것은 「몇 칸 중 몇 칸을 썼는가」와 겹친 개수다.
                AssertPanelContains(entries, "bag", "1 / 3 슬롯");
                AssertPanelContains(entries, "bag", "×2");
                AssertPanelDoesNotContainRuntimeRoot(entries, "bag");
                AssertPanelContains(entries, "deck", "Move 1");
                AssertPanelDoesNotContainRuntimeRoot(entries, "deck");
                // 효과 문구는 칩이 아니라 상세 줄이 말한다 — 올려 봐야 확인된다.
                AssertHoverRevealsText(entries, "relic_curse", "공격 피해 +1");
                AssertHoverRevealsText(entries, "bag", "debug-token");
                AssertPanelDoesNotContainRuntimeRoot(entries, "relic_curse");
                // Settings panel was redesigned to host sound settings only (SoundSettingsPanelView);
                // it no longer projects combat-state text rows, so only the structural contracts apply.
                AssertPanelDoesNotContainRuntimeRoot(entries, "settings");
                AssertRuntimePanelsUseDefaultImages(entries, "settings");
            }
            finally
            {
                foreach (var root in roots.Where(root => root != null))
                {
                    Object.DestroyImmediate(root);
                }

                DestroyTooltipHud();
            }
        }

        // 재화 버튼은 패널이 없는 표시 전용 버튼이라, 이 라벨 갱신이 그 버튼이 하는 일의 전부다.
        // 저작된 자리표시("1234")가 그대로 남는 회귀를 막는다.
        [Test]
        public void RefreshWritesWalletBalanceIntoCurrencyLabel()
        {
            var roots = new List<GameObject>();
            try
            {
                var sidebar = CreateRect("Sidebar", null, roots);
                var currencyButton = CreateRect("Sidebar Button currency", sidebar, null);
                var label = CreateText("Label", currencyButton);
                label.text = "1234";

                var view = sidebar.gameObject.AddComponent<SidebarRuntimeView>();
                var state = CreateState();
                state.PlayerInventory.Wallet.Add(87);

                view.Refresh(state, state.CreatePlayerStateSnapshot(), null);

                Assert.That(label.text, Is.EqualTo("87"));

                state.PlayerInventory.Wallet.Add(1000);
                view.Refresh(state, state.CreatePlayerStateSnapshot(), null);

                Assert.That(label.text, Is.EqualTo("1,087"), "지갑이 움직이면 라벨도 따라와야 한다(시그니처 캐시 회귀).");
            }
            finally
            {
                foreach (var root in roots.Where(root => root != null))
                {
                    Object.DestroyImmediate(root);
                }

                DestroyTooltipHud();
            }
        }

        /// <summary>
        /// 유물 칩의 그림은 <b>반드시 그 칸의 자식</b>이어야 한다. 칸 밖(사이드바 루트 등)에 붙으면
        /// anchors 0~1이 부모 전체로 늘어나 유물 그림 한 장이 화면을 통째로 덮는다 —
        /// 실제로 MainGameplay 씬에 그 상태로 8장이 저장돼 있었다(2026-08-31에 걷어냄).
        /// </summary>
        [Test]
        public void RefreshKeepsRelicIconsInsideTheirSlots()
        {
            var roots = new List<GameObject>();
            try
            {
                var sidebar = CreateRect("Sidebar", null, roots);
                var settings = sidebar.gameObject.AddComponent<SidebarCalloutPanelSettings>();
                var controller = sidebar.gameObject.AddComponent<SidebarCalloutPanelController>();
                var view = sidebar.gameObject.AddComponent<SidebarRuntimeView>();
                var panelLayer = CreateRect("Sidebar Callout Panel Layer", null, roots);
                var entries = new[] { CreateEntry("relic_curse", panelLayer) };
                controller.Bind(sidebar, panelLayer, entries);
                view.Bind(controller, settings);

                var state = CreateState();
                view.Refresh(state, state.CreatePlayerStateSnapshot(), null);

                var icons = panelLayer.GetComponentsInChildren<RectTransform>(true)
                    .Concat(sidebar.GetComponentsInChildren<RectTransform>(true))
                    .Where(rect => rect.name == "Relic Icon")
                    .ToArray();
                foreach (var icon in icons)
                {
                    Assert.That(icon.parent, Is.Not.Null);
                    Assert.That(icon.parent.name, Does.StartWith("Relic Curse Slot"),
                        $"유물 아이콘이 칸 밖('{icon.parent.name}')에 생기면 화면을 덮는다.");
                }
            }
            finally
            {
                foreach (var root in roots.Where(root => root != null))
                {
                    Object.DestroyImmediate(root);
                }

                DestroyTooltipHud();
            }
        }

        /// <summary>
        /// 칸에 마우스를 올린 것과 같은 경로로 호버를 발생시키고, <b>커서 옆 툴팁</b>이 그 문구를
        /// 말하는지 본다. 리뉴얼 이후 이름·효과의 유일한 출구가 이 툴팁이라, 이 경로가 끊기면
        /// 플레이어는 자기가 무엇을 가졌는지 영영 못 읽는다.
        /// </summary>
        private static void AssertHoverRevealsText(
            IEnumerable<SidebarCalloutPanelController.PanelEntry> entries,
            string key,
            string expected)
        {
            var panel = entries.Single(entry => entry.Key == key).Panel;
            var hover = panel.GetComponentsInChildren<SidebarRelicSlotHover>(true).FirstOrDefault();
            Assert.That(hover, Is.Not.Null, $"{key} 패널의 칸에 호버 핸들러가 붙어 있어야 한다.");

            SidebarSlotTooltip.Hide();
            hover.OnPointerEnter(null);

            var hud = GameObject.Find("SidebarSlotTooltipHud");
            Assert.That(hud, Is.Not.Null, "호버는 공유 툴팁을 띄워야 한다.");
            var texts = hud.GetComponentsInChildren<TMP_Text>(true).Select(text => text.text);
            Assert.That(string.Join(" | ", texts), Does.Contain(expected));
        }

        /// <summary>공유 툴팁 호스트는 필요할 때 만들어지는 씬 오브젝트다 — 테스트가 남기지 않는다.</summary>
        private static void DestroyTooltipHud()
        {
            var hud = GameObject.Find("SidebarSlotTooltipHud");
            if (hud != null)
            {
                Object.DestroyImmediate(hud);
            }
        }

        private static CombatState CreateState()
        {
            var relics = new PlayerRelicCurseInventory();
            Assert.That(relics.TryAdd(new PlayerPermanentItemState(
                "relic-test",
                PlayerPermanentItemKind.Relic,
                "Tiger",
                "Test relic",
                PlayerPermanentItemEffectKind.AttackDamageBonus,
                1), out var relicReason), Is.True, relicReason);

            var bag = new PlayerBagState();
            bag.AddPlaceholderStack("debug-token", 2);

            var state = new CombatState(
                CombatState.CreateDemoMap(2),
                new HexCoord(0, 0),
                new HexCoord(1, 0),
                CombatConfig.Default,
                playerInventory: new PlayerInventoryState(relics, bag));

            ActiveEffectProbe.Registry(state).Add(new ActiveEffect(EffectType.Duration, StatusEffectKind.Slow, state.Player.Id, 2, 1, "test"));
            return state;
        }

        private static SidebarCalloutPanelController.PanelEntry CreateEntry(
            string key,
            RectTransform parent)
        {
            var panel = CreateRect("Sidebar Callout Panel " + key, parent, null);
            if (key == "bag")
            {
                CreateAuthoredBagPanel(panel);
            }

            if (key == "deck")
            {
                CreateAuthoredDeckPanel(panel);
            }

            if (key == "relic_curse")
            {
                CreateAuthoredRelicCursePanel(panel);
            }

            if (key == "settings")
            {
                CreateAuthoredSettingsPanel(panel);
            }

            var entry = new SidebarCalloutPanelController.PanelEntry();
            entry.Bind(key, null, panel);
            return entry;
        }

        private static void CreateAuthoredDeckPanel(RectTransform panel)
        {
            panel.gameObject.AddComponent<SidebarDeckPanelView>();
            var scrollRoot = CreateRect("Panel Text Scroll", panel, null);
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            var viewport = CreateRect("Viewport", scrollRoot, null);
            var content = CreateRect("Content", viewport, null);
            scroll.viewport = viewport;
            scroll.content = content;

            for (var i = 0; i < 12; i++)
            {
                CreateAuthoredRow(content, $"Deck Row {i}");
            }
        }

        private static void CreateAuthoredRelicCursePanel(RectTransform panel)
        {
            panel.gameObject.AddComponent<SidebarRelicCursePanelView>();
            var grid = CreateRect("Relic Curse Grid", panel, null);
            grid.gameObject.AddComponent<GridLayoutGroup>();

            for (var i = 0; i < 10; i++)
            {
                var slot = CreateRect($"Relic Curse Slot {i}", grid, null);
                slot.gameObject.AddComponent<Image>();
                CreateText("Icon Label", slot);
                CreateText("Icon Meta", slot);
            }
        }

        private static void CreateAuthoredSettingsPanel(RectTransform panel)
        {
            panel.gameObject.AddComponent<SidebarSettingsPanelView>();
            var content = CreateRect("Settings Content", panel, null);
            CreateAuthoredRow(content, "Settings Row Progress");
            CreateAuthoredRow(content, "Settings Row Camera");
            CreateAuthoredRow(content, "Settings Row Input");
            CreateAuthoredRow(content, "Settings Row Audio");
        }

        private static void CreateAuthoredRow(RectTransform parent, string name)
        {
            var row = CreateRect(name, parent, null);
            row.gameObject.AddComponent<Image>();
            CreateText("Left", row);
            CreateText("Right", row);
        }

        private static void CreateAuthoredBagPanel(RectTransform panel)
        {
            panel.gameObject.AddComponent<SidebarBagPanelView>();

            var content = CreateRect("Bag Content", panel, null);
            var title = CreateText("Bag Title", content);
            title.text = "가방";
            CreateText("Bag Status", content);
            var grid = CreateRect("Bag Slot Grid", content, null);
            grid.gameObject.AddComponent<GridLayoutGroup>();

            for (var i = 0; i < 5; i++)
            {
                var slot = CreateRect($"Bag Slot {i}", grid, null);
                slot.gameObject.AddComponent<Image>();
                CreateText("Bag Slot Label", slot);
                CreateText("Bag Slot Count", slot);
            }
        }

        private static TMP_Text CreateText(string name, RectTransform parent)
        {
            var rect = CreateRect(name, parent, null);
            return rect.gameObject.AddComponent<TextMeshProUGUI>();
        }

        private static RectTransform CreateRect(string name, Transform parent, ICollection<GameObject> roots)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            if (parent == null)
            {
                roots?.Add(rect.gameObject);
            }

            return rect;
        }


        private static void AssertRuntimePanelsUseDefaultImages(
            IEnumerable<SidebarCalloutPanelController.PanelEntry> entries,
            string key)
        {
            var panel = entries.Single(entry => entry.Key == key).Panel;
            foreach (var image in panel.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                Assert.That(image.sprite, Is.Null, $"{key} runtime UI must use default Unity Image panels, not legacy image sources.");
            }
        }

        private static void AssertPanelDoesNotContainRuntimeRoot(
            IEnumerable<SidebarCalloutPanelController.PanelEntry> entries,
            string key)
        {
            var panel = entries.Single(entry => entry.Key == key).Panel;
            Assert.That(
                panel.Find("Sidebar Runtime Content"),
                Is.Null,
                $"{key} must use its authored view instead of rebuilding runtime content.");
        }

        private static void AssertPanelContains(
            IEnumerable<SidebarCalloutPanelController.PanelEntry> entries,
            string key,
            string expected)
        {
            var panel = entries.Single(entry => entry.Key == key).Panel;
            var text = string.Join("\n", panel.GetComponentsInChildren<TMP_Text>(true).Select(label => label.text));
            Assert.That(text, Does.Contain(expected));
        }

    }
}
#endif



