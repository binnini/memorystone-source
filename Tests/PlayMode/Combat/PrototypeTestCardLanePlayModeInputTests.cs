#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SeoulPlayup.Combat.Tests.PlayMode
{
    public sealed class PrototypeTestCardLanePlayModeInputTests
    {
        private const string ScenePath = "Assets/Scenes/Dev/PrototypeTest.unity";

        [UnityTest]
        public IEnumerator CardLaneReceivesEventSystemRaycastsWhenDeckOverlayClosedAndOverlayBlocksWhenOpen()
        {
            var scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(scene.IsValid(), Is.True, $"Failed to start loading {ScenePath} in play mode.");
            for (var guard = 0; !scene.isLoaded && guard < 600; guard++)
            {
                yield return null;
            }
            Assert.That(scene.isLoaded, Is.True, $"{ScenePath} did not finish loading in play mode.");

            for (var i = 0; i < 8; i++)
            {
                yield return null;
            }

            var eventSystems = Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None);
            Assert.That(eventSystems, Has.Length.EqualTo(1));
            Assert.That(eventSystems[0].GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(), Is.Not.Null);
            Assert.That(eventSystems[0].GetComponent<StandaloneInputModule>(), Is.Null);

            var controller = Object.FindFirstObjectByType<MapCombatController>();
            Assert.That(controller, Is.Not.Null);
            controller.ConfigurePresentationForTests(immediateSequences: true);

            var overlay = Object.FindFirstObjectByType<DeckPileListOverlayView>(FindObjectsInactive.Include);
            Assert.That(overlay, Is.Not.Null);
            if (overlay.IsOpen)
            {
                overlay.Close();
                yield return null;
            }
            Assert.That(overlay.gameObject.activeSelf, Is.False, "Closed Deck overlay must not leave a raycast backdrop over CardLane.");

            var lane = Object.FindFirstObjectByType<GameplayCardLaneView>();
            Assert.That(lane, Is.Not.Null, "PrototypeTest should expose a gameplay CardLane.");

            var card = Object.FindObjectsByType<HandCardInteraction>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(slot => slot.IsPlayable)
                .FirstOrDefault(slot => TryGetHomeInputRaycastPosition(lane, slot, out _));
            Assert.That(card, Is.Not.Null, "PrototypeTest should expose a playable CardLane card in play mode.");
            Assert.That(TryGetHomeInputRaycastPosition(lane, card, out var cardPosition), Is.True,
                "A playable card should expose an on-screen home-position input point even when its visual center is below the screen edge.");

            var closedHit = TopUiRaycast(cardPosition);
            Assert.That(closedHit.gameObject, Is.Not.Null, "Closed overlay raycast should hit the CardLane card or its home-position input proxy.");
            Assert.That(IsCardOrHomeInputProxyHit(closedHit, card), Is.True);

            var eventData = new PointerEventData(EventSystem.current)
            {
                position = cardPosition,
                pressPosition = cardPosition,
                button = PointerEventData.InputButton.Left
            };
            lane.ApplyResolvedHover(cardPosition);
            Assert.That(card.IsHovering, Is.True, "EventSystem pointer-enter path should drive CardLane hover.");
            Assert.That(IsCardDrawnAboveLaneSiblings(card), Is.True,
                "Hovered CardLane cards should render above sibling cards and the other card group.");

            var postHoverHomeHit = TopUiRaycast(cardPosition);
            Assert.That(IsCardOrHomeInputProxyHit(postHoverHomeHit, card), Is.True,
                "The card's authored home position should remain clickable after hover lifts the visual card.");
            ExecuteEvents.ExecuteHierarchy(postHoverHomeHit.gameObject, eventData, ExecuteEvents.pointerClickHandler);
            Assert.That(controller.SelectedCardId, Is.Not.Empty, "EventSystem pointer-click path should select/use the CardLane card.");

            lane.ApplyResolvedHover(cardPosition);
            ExecuteEvents.ExecuteHierarchy(closedHit.gameObject, eventData, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.ExecuteHierarchy(closedHit.gameObject, eventData, ExecuteEvents.beginDragHandler);
            Assert.That(card.IsDragging, Is.True, "EventSystem drag path should begin dragging the CardLane card.");
            eventData.position = cardPosition + Vector2.up * 220f;
            ExecuteEvents.ExecuteHierarchy(closedHit.gameObject, eventData, ExecuteEvents.dragHandler);
            ExecuteEvents.ExecuteHierarchy(closedHit.gameObject, eventData, ExecuteEvents.endDragHandler);
            Assert.That(card.IsDragging, Is.False, "EventSystem drag release should complete and snap the CardLane card home.");

            overlay.OpenDeckList();
            yield return null;
            var openHit = TopUiRaycast(cardPosition);
            Assert.That(openHit.gameObject, Is.Not.Null);
            Assert.That(openHit.gameObject.GetComponentInParent<DeckPileListOverlayView>(), Is.SameAs(overlay),
                "Open Deck overlay should intentionally receive input above the CardLane.");
        }

        private static bool TryGetHomeInputRaycastPosition(GameplayCardLaneView lane, HandCardInteraction card, out Vector2 position)
        {
            position = default;
            if (lane == null || card == null)
            {
                return false;
            }

            var root = card.transform.parent as RectTransform;
            if (root == null)
            {
                return false;
            }

            var center = (Vector2)RectTransformUtility.WorldToScreenPoint(null, root.TransformPoint(card.HomeAnchoredPosition));
            var y = Mathf.Clamp(card.HomeScreenTopY - 2f, 1f, Screen.height - 1f);
            var candidate = new Vector2(center.x, y);
            if (lane.ResolveHoveredSlot(candidate) != card)
            {
                return false;
            }

            var hit = TopUiRaycast(candidate);
            if (!IsCardOrHomeInputProxyHit(hit, card))
            {
                return false;
            }

            position = candidate;
            return true;
        }

        private static bool IsCardOrHomeInputProxyHit(RaycastResult hit, HandCardInteraction card)
        {
            if (hit.gameObject == null)
            {
                return false;
            }

            if (hit.gameObject.GetComponentInParent<HandCardInteraction>() == card)
            {
                return true;
            }

            return hit.gameObject.name == "CardLaneHomeInputProxy";
        }

        private static bool IsCardDrawnAboveLaneSiblings(HandCardInteraction card)
        {
            if (card == null || card.transform.parent == null)
            {
                return false;
            }

            var group = card.transform.parent;
            if (card.transform.GetSiblingIndex() != group.childCount - 1)
            {
                return false;
            }

            var groupParent = group.parent;
            return groupParent == null || group.GetSiblingIndex() == groupParent.childCount - 1;
        }

        private static RaycastResult TopUiRaycast(Vector2 screenPosition)
        {
            Canvas.ForceUpdateCanvases();
            var eventData = new PointerEventData(EventSystem.current) { position = screenPosition };
            var hits = new List<RaycastResult>();
            foreach (var raycaster in Object.FindObjectsByType<GraphicRaycaster>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                raycaster.Raycast(eventData, hits);
            }

            return hits
                .OrderByDescending(hit => hit.sortingOrder)
                .ThenByDescending(hit => hit.depth)
                .FirstOrDefault();
        }
    }
}
#endif

