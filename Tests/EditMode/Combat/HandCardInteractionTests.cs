#if UNITY_EDITOR
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class HandCardInteractionTests
    {
        /// <summary>
        /// 대상 선택 중 레인이 내려가도 핀 카드는 제자리에 남아야 한다(2026-09-05 실플레이 #8) —
        /// 외부 리프트는 호버 리프트와 <b>더해지고</b>, 0으로 돌리면 홈으로 정확히 돌아온다.
        /// </summary>
        [Test]
        public void ExternalLiftStacksWithHoverLiftAndClearsBackToHome()
        {
            var cardObject = new GameObject("MoveCard_01", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            try
            {
                var rect = cardObject.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(10f, 20f);

                var interaction = cardObject.GetComponent<HandCardInteraction>();
                interaction.ConfigureHoverMotion(96f, 1.12f, 1.13f);
                interaction.Initialize((_, _, _) => { });
                interaction.Configure(
                    new CombatCardSnapshot("move-1", CombatCardKind.Move, "Move", "Move card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);

                interaction.SetExternalLift(250f);
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 270f)), "외부 리프트만큼 홈에서 올라간다.");

                interaction.OnPointerEnter(new PointerEventData(EventSystem.current));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 366f)), "호버 리프트는 외부 리프트 위에 더해진다.");

                interaction.SetExternalLift(0f);
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 116f)), "외부 리프트를 걷으면 호버 리프트만 남는다.");

                interaction.OnPointerExit(new PointerEventData(EventSystem.current) { position = new Vector2(0f, -1000f) });
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 20f)), "홈 위치는 외부 리프트에 오염되지 않는다.");
            }
            finally
            {
                Object.DestroyImmediate(cardObject);
            }
        }

        [Test]
        public void PointerHoverLiftsOnlyTheHoveredCardAndRestoresHomePosition()
        {
            var cardObject = new GameObject("MoveCard_01", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            try
            {
                var rect = cardObject.GetComponent<RectTransform>();
                rect.anchoredPosition = new Vector2(10f, 20f);

                var interaction = cardObject.GetComponent<HandCardInteraction>();
                interaction.ConfigureHoverMotion(96f, 1.12f, 1.13f);
                interaction.Initialize((_, _, _) => { });
                interaction.Configure(
                    new CombatCardSnapshot("move-1", CombatCardKind.Move, "Move", "Move card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);

                interaction.OnPointerEnter(new PointerEventData(EventSystem.current));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 116f)));
                Assert.That(rect.localScale.x, Is.EqualTo(1.12f).Within(0.001f));
                Assert.That(rect.localScale.y, Is.EqualTo(1.12f).Within(0.001f));

                interaction.OnPointerExit(new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(0f, -1000f)
                });
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 20f)));
                Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
            }
            finally
            {
                Object.DestroyImmediate(cardObject);
            }
        }

        [Test]
        public void PointerExitInsideOriginalCardFootprintDoesNotDropLiftedCard()
        {
            var cardObject = new GameObject("ActionCard_01", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            try
            {
                var rect = cardObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200f, 320f);
                rect.anchoredPosition = Vector2.zero;

                var interaction = cardObject.GetComponent<HandCardInteraction>();
                interaction.ConfigureHoverMotion(170f, 1.12f, 1.13f);
                interaction.Initialize((_, _, _) => { });
                interaction.Configure(
                    new CombatCardSnapshot("attack-1", CombatCardKind.Attack, "Attack", "Attack card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);

                interaction.OnPointerEnter(new PointerEventData(EventSystem.current));
                Assert.That(interaction.IsHovering, Is.True);
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(0f, 170f)));

                var originalCardCenter = new PointerEventData(EventSystem.current)
                {
                    position = Vector2.zero
                };
                interaction.OnPointerExit(originalCardCenter);

                Assert.That(interaction.IsHovering, Is.True, "Lift should not immediately cancel when the pointer is still over the card's home footprint.");
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(0f, 170f)));

                var outsideCard = new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(0f, -1000f)
                };
                interaction.OnPointerExit(outsideCard);

                Assert.That(interaction.IsHovering, Is.False);
                Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
            }
            finally
            {
                Object.DestroyImmediate(cardObject);
            }
        }

        [Test]
        public void RepeatedPointerEnterDoesNotAccumulateHoverLiftAndKeepsCardInFront()
        {
            var parent = new GameObject("Card Parent", typeof(RectTransform));
            var left = new GameObject("MoveCard_00", typeof(RectTransform), typeof(Image));
            var cardObject = new GameObject("MoveCard_01", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            var right = new GameObject("MoveCard_02", typeof(RectTransform), typeof(Image));
            try
            {
                left.transform.SetParent(parent.transform, false);
                cardObject.transform.SetParent(parent.transform, false);
                right.transform.SetParent(parent.transform, false);
                left.transform.SetSiblingIndex(0);
                cardObject.transform.SetSiblingIndex(1);
                right.transform.SetSiblingIndex(2);

                var rect = cardObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200f, 320f);
                rect.anchoredPosition = new Vector2(10f, 20f);

                var interaction = cardObject.GetComponent<HandCardInteraction>();
                interaction.ConfigureHoverMotion(96f, 1.12f, 1.13f);
                interaction.Initialize((_, _, _) => { });
                interaction.Configure(
                    new CombatCardSnapshot("move-1", CombatCardKind.Move, "Move", "Move card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);

                interaction.OnPointerEnter(new PointerEventData(EventSystem.current));
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 116f)));
                Assert.That(cardObject.transform.GetSiblingIndex(), Is.EqualTo(parent.transform.childCount - 1),
                    "Hovered cards should render in front of overlapped sibling cards.");

                interaction.OnPointerEnter(new PointerEventData(EventSystem.current));
                interaction.OnPointerEnter(new PointerEventData(EventSystem.current));

                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 116f)),
                    "Repeated hover enter events must not use the lifted position as a new home pose.");
                Assert.That(cardObject.transform.GetSiblingIndex(), Is.EqualTo(parent.transform.childCount - 1));

                interaction.OnPointerExit(new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(0f, -1000f)
                });

                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(10f, 20f)));
                Assert.That(cardObject.transform.GetSiblingIndex(), Is.EqualTo(1),
                    "When hover ends the card should return to its authored fan order.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void HoveringSecondCardClearsFirstCardHover()
        {
            var parent = new GameObject("Card Parent", typeof(RectTransform));
            var firstObject = new GameObject("MoveCard_01", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            var secondObject = new GameObject("MoveCard_02", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            try
            {
                firstObject.transform.SetParent(parent.transform, false);
                secondObject.transform.SetParent(parent.transform, false);
                firstObject.transform.SetSiblingIndex(0);
                secondObject.transform.SetSiblingIndex(1);

                var firstRect = firstObject.GetComponent<RectTransform>();
                var secondRect = secondObject.GetComponent<RectTransform>();
                firstRect.sizeDelta = new Vector2(200f, 320f);
                secondRect.sizeDelta = new Vector2(200f, 320f);
                firstRect.anchoredPosition = new Vector2(-40f, 20f);
                secondRect.anchoredPosition = new Vector2(40f, 30f);

                var first = firstObject.GetComponent<HandCardInteraction>();
                var second = secondObject.GetComponent<HandCardInteraction>();
                first.ConfigureHoverMotion(96f, 1.12f, 1.13f);
                second.ConfigureHoverMotion(96f, 1.12f, 1.13f);
                first.Initialize((_, _, _) => { });
                second.Initialize((_, _, _) => { });
                first.Configure(
                    new CombatCardSnapshot("move-1", CombatCardKind.Move, "Move 1", "Move card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);
                second.Configure(
                    new CombatCardSnapshot("move-2", CombatCardKind.Move, "Move 2", "Move card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);

                first.OnPointerEnter(new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(-40f, 20f)
                });
                Assert.That(first.IsHovering, Is.True);
                Assert.That(firstRect.anchoredPosition, Is.EqualTo(new Vector2(-40f, 116f)));

                second.OnPointerEnter(new PointerEventData(EventSystem.current)
                {
                    position = new Vector2(100f, 30f)
                });

                Assert.That(first.IsHovering, Is.False, "Only one card should stay hovered at a time.");
                Assert.That(firstRect.anchoredPosition, Is.EqualTo(new Vector2(-40f, 20f)));
                Assert.That(second.IsHovering, Is.True);
                Assert.That(secondRect.anchoredPosition, Is.EqualTo(new Vector2(40f, 126f)));
                Assert.That(secondObject.transform.GetSiblingIndex(), Is.EqualTo(parent.transform.childCount - 1));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void SetHoverFromLaneLiftsScalesAndRestoresHomePosition()
        {
            var cardObject = new GameObject("MoveCard_01", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            try
            {
                var rect = cardObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200f, 320f);
                rect.anchoredPosition = new Vector2(8f, 12f);

                var interaction = cardObject.GetComponent<HandCardInteraction>();
                interaction.ConfigureHoverMotion(170f, 1.12f, 1.13f);
                interaction.Initialize((_, _, _) => { });
                interaction.Configure(
                    new CombatCardSnapshot("move-1", CombatCardKind.Move, "Move", "Move card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);
                interaction.SetLaneHoverControlled(true);
                interaction.SetHomePoseFromLayout(new Vector2(8f, 12f), siblingIndex: 0);

                interaction.SetHoverFromLane(true);

                Assert.That(interaction.IsHovering, Is.True);
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(8f, 182f)));
                Assert.That(rect.localScale.x, Is.EqualTo(1.12f).Within(0.001f));

                interaction.SetHoverFromLane(false);

                Assert.That(interaction.IsHovering, Is.False);
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(8f, 12f)));
                Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
            }
            finally
            {
                Object.DestroyImmediate(cardObject);
            }
        }

        [Test]
        public void LaneControlledPointerEnterDoesNotOverrideCentralHoverOwner()
        {
            var parent = new GameObject("Card Parent", typeof(RectTransform));
            var firstObject = new GameObject("MoveCard_01", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            var secondObject = new GameObject("MoveCard_02", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            try
            {
                firstObject.transform.SetParent(parent.transform, false);
                secondObject.transform.SetParent(parent.transform, false);
                firstObject.transform.SetSiblingIndex(0);
                secondObject.transform.SetSiblingIndex(1);

                var firstRect = firstObject.GetComponent<RectTransform>();
                var secondRect = secondObject.GetComponent<RectTransform>();
                firstRect.sizeDelta = new Vector2(200f, 320f);
                secondRect.sizeDelta = new Vector2(200f, 320f);
                firstRect.anchoredPosition = Vector2.zero;
                secondRect.anchoredPosition = new Vector2(80f, 0f);

                var first = firstObject.GetComponent<HandCardInteraction>();
                var second = secondObject.GetComponent<HandCardInteraction>();
                first.ConfigureHoverMotion(170f, 1.12f, 1.13f);
                second.ConfigureHoverMotion(170f, 1.12f, 1.13f);
                first.Initialize((_, _, _) => { });
                second.Initialize((_, _, _) => { });
                first.Configure(
                    new CombatCardSnapshot("move-1", CombatCardKind.Move, "Move 1", "Move card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);
                second.Configure(
                    new CombatCardSnapshot("move-2", CombatCardKind.Move, "Move 2", "Move card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);
                first.SetLaneHoverControlled(true);
                second.SetLaneHoverControlled(true);

                first.SetHoverFromLane(true);
                second.OnPointerEnter(new PointerEventData(EventSystem.current) { position = new Vector2(80f, 0f) });

                Assert.That(first.IsHovering, Is.True, "Central lane ownership should ignore visual-raycast pointer enter noise.");
                Assert.That(second.IsHovering, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void PointerHoverLiftsNonPlayableCardButClickStillDoesNotInvoke()
        {
            var cardObject = new GameObject("ActionCard_Disabled", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            try
            {
                var rect = cardObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200f, 320f);
                rect.anchoredPosition = new Vector2(4f, 8f);

                var clicked = false;
                var invalidClicked = false;
                var interaction = cardObject.GetComponent<HandCardInteraction>();
                interaction.ConfigureHoverMotion(96f, 1.12f, 1.13f);
                interaction.Initialize(
                    (_, _, _) => { },
                    (_, _) => clicked = true,
                    onInvalidClicked: (_, _) => invalidClicked = true);
                interaction.Configure(
                    new CombatCardSnapshot("attack-1", CombatCardKind.Attack, "Attack", "Attack card text", 1, false, false, "Not playable"),
                    playable: false,
                    selected: false);

                interaction.OnPointerEnter(new PointerEventData(EventSystem.current));
                Assert.That(interaction.IsHovering, Is.True, "Visible hand cards should provide hover feedback even when current phase/cost blocks use.");
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(4f, 104f)));

                interaction.OnPointerClick(new PointerEventData(EventSystem.current));
                Assert.That(clicked, Is.False, "Non-playable cards should still reject click/use.");
                Assert.That(invalidClicked, Is.True, "Non-playable cards should still emit invalid-click feedback.");
            }
            finally
            {
                Object.DestroyImmediate(cardObject);
            }
        }

        [Test]
        public void PointerClickClearsHoverBeforeInvokingUseHandler()
        {
            var cardObject = new GameObject("ActionCard_Click", typeof(RectTransform), typeof(Image), typeof(HandCardInteraction));
            try
            {
                var rect = cardObject.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200f, 320f);
                rect.anchoredPosition = new Vector2(2f, 6f);

                var clickedWhileHovering = true;
                var interaction = cardObject.GetComponent<HandCardInteraction>();
                interaction.ConfigureHoverMotion(96f, 1.12f, 1.13f);
                interaction.Initialize((_, _, _) => { }, (source, _) => clickedWhileHovering = source.IsHovering);
                interaction.Configure(
                    new CombatCardSnapshot("defend-1", CombatCardKind.Defend, "Defend", "Defend card text", 1, true, false, "Ready"),
                    playable: true,
                    selected: false);

                interaction.OnPointerEnter(new PointerEventData(EventSystem.current));
                Assert.That(interaction.IsHovering, Is.True);

                interaction.OnPointerClick(new PointerEventData(EventSystem.current));

                Assert.That(clickedWhileHovering, Is.False, "Click use should clear hover before state/UI refresh runs.");
                Assert.That(interaction.IsHovering, Is.False);
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(2f, 6f)));
            }
            finally
            {
                Object.DestroyImmediate(cardObject);
            }
        }
    }
}
#endif

