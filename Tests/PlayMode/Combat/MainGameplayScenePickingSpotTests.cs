#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using SeoulPlayup.Map.Runtime;
using SeoulPlayup.Map.Unity;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace SeoulPlayup.Combat.Tests.PlayMode
{
    /// <summary>
    /// Picking-accuracy spot test on the canonical MainGameplay scene (P6 §5-⑤). After the
    /// collider fallback removal, math projection is the only picking path, so on-screen cells
    /// — including map-boundary cells, height-step cells and near-edge points — must round-trip
    /// through <see cref="AtlasTilePresentationView.TryScreenToHex"/> with the real scene camera.
    /// A mismatch is legitimate only when a strictly taller cell occludes the target along the
    /// camera ray (top-face-only picking contract).
    /// </summary>
    public sealed class MainGameplayScenePickingSpotTests
    {
        private const string ScenePath = "Assets/Scenes/Game/MainGameplay.unity";
        private const int WarmupFrames = 20;
        private const int MaxTestedCells = 48;
        private const int MinTestedCells = 8;

        [UnityTest]
        public IEnumerator TryScreenToHex_RoundTripsOnScreenBoundaryAndHeightStepCells()
        {
            var scene = EditorSceneManager.LoadSceneInPlayMode(ScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(scene.IsValid(), Is.True, $"Failed to start loading {ScenePath} in play mode.");
            for (var guard = 0; !scene.isLoaded && guard < 3600; guard++)
            {
                yield return null;
            }

            Assert.That(scene.isLoaded, Is.True, $"{ScenePath} did not finish loading in play mode.");
            for (var i = 0; i < WarmupFrames; i++)
            {
                yield return null;
            }

            var controller = Object.FindFirstObjectByType<MapCombatController>();
            Assert.That(controller, Is.Not.Null, "MainGameplay scene must contain a MapCombatController.");
            Assert.That(controller.LoadedMap, Is.Not.Null.And.Property("Count").GreaterThan(0),
                "Controller should have loaded the real map by play-mode start.");
            var view = Object.FindFirstObjectByType<AtlasTilePresentationView>();
            Assert.That(view, Is.Not.Null, "MainGameplay scene must contain an AtlasTilePresentationView.");
            var camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null, "MainGameplay scene must contain a camera for picking.");

            var map = controller.LoadedMap;
            var tested = 0;
            var exactMatches = 0;
            var occludedByTaller = 0;
            var boundaryTested = 0;
            var heightStepTested = 0;
            var failures = new List<string>();
            var hasAnchor = false;
            var anchorWorld = Vector3.zero;
            var anchorCenterDistance = float.MaxValue;
            var screenCenter = new Vector2(camera.pixelRect.width * 0.5f, camera.pixelRect.height * 0.5f);

            foreach (var cell in map.AllCells)
            {
                if (tested >= MaxTestedCells)
                {
                    break;
                }

                var hasMissingNeighbor = false;
                var heightStepNeighbor = default(HexCoord);
                var hasHeightStepNeighbor = false;
                foreach (var neighbor in cell.Coord.NeighborsInDirectionOrder())
                {
                    if (!map.TryGetCell(neighbor, out var neighborCell))
                    {
                        hasMissingNeighbor = true;
                    }
                    else if (!hasHeightStepNeighbor && neighborCell.HeightLevel != cell.HeightLevel)
                    {
                        hasHeightStepNeighbor = true;
                        heightStepNeighbor = neighbor;
                    }
                }

                // Spot focus: only map-boundary cells and height-step cells; plain interior
                // cells are covered by the pure-math unit tests already.
                if (!hasMissingNeighbor && !hasHeightStepNeighbor)
                {
                    continue;
                }

                var world = view.ProjectTop(cell.Coord);
                if (!TryWorldToOnScreenPoint(camera, world, out var screen))
                {
                    continue;
                }

                // Remember the on-screen point closest to the screen centre: the reframe pass
                // teleports the camera so a target cell lands exactly where this anchor was,
                // which keeps it inside the frustum regardless of what the camera frames.
                var centerDistance = Vector2.Distance(screen, screenCenter);
                if (centerDistance < anchorCenterDistance)
                {
                    anchorCenterDistance = centerDistance;
                    anchorWorld = world;
                    hasAnchor = true;
                }

                tested++;
                if (hasMissingNeighbor)
                {
                    boundaryTested++;
                }

                if (hasHeightStepNeighbor)
                {
                    heightStepTested++;
                }

                Classify(view, map, camera, screen, cell, "center", ref exactMatches, ref occludedByTaller, failures);

                // Near-edge probe toward the height-step neighbour: 30% of the way to the
                // neighbour centre stays inside the cell footprint (edge sits at 50%) but
                // exercises the axial rounding right where height steps meet.
                if (hasHeightStepNeighbor)
                {
                    var neighborWorld = view.ProjectTop(heightStepNeighbor);
                    var nearEdge = Vector3.Lerp(world, neighborWorld, 0.3f);
                    nearEdge.y = world.y;
                    if (TryWorldToOnScreenPoint(camera, nearEdge, out var edgeScreen))
                    {
                        tested++;
                        Classify(view, map, camera, edgeScreen, cell, "near-edge", ref exactMatches, ref occludedByTaller, failures);
                    }
                }
            }

            // The start-of-game camera framing may show a height-uniform area. Height-step
            // picking still needs real-camera coverage, so reframe the camera (same rotation
            // and offset, so same projection geometry) over a height-step cluster and probe
            // there. Projection math reads camera matrices directly — no rendered frame needed.
            var reframeCandidates = 0;
            var reframeOffscreen = 0;
            var reframeDebug = "";
            if (heightStepTested == 0 && hasAnchor)
            {
                var cameraOffset = camera.transform.position - anchorWorld;
                var originalCameraPosition = camera.transform.position;
                reframeDebug = $"cameraOffset={cameraOffset}, anchorWorld={anchorWorld}, cameraPos={originalCameraPosition}, pixelRect={camera.pixelRect}";
                foreach (var cell in map.AllCells)
                {
                    if (heightStepTested >= MaxTestedCells / 2)
                    {
                        break;
                    }

                    var heightStepNeighbor = default(HexCoord);
                    var hasHeightStepNeighbor = false;
                    foreach (var neighbor in cell.Coord.NeighborsInDirectionOrder())
                    {
                        if (map.TryGetCell(neighbor, out var neighborCell) && neighborCell.HeightLevel != cell.HeightLevel)
                        {
                            hasHeightStepNeighbor = true;
                            heightStepNeighbor = neighbor;
                            break;
                        }
                    }

                    if (!hasHeightStepNeighbor)
                    {
                        continue;
                    }

                    reframeCandidates++;
                    var world = view.ProjectTop(cell.Coord);
                    camera.transform.position = world + cameraOffset;
                    if (!TryWorldToOnScreenPoint(camera, world, out var screen))
                    {
                        reframeOffscreen++;
                        if (reframeOffscreen == 1)
                        {
                            var raw = camera.WorldToScreenPoint(world);
                            reframeDebug += $" | first offscreen: cell={cell.Coord} world={world} rawScreen={raw}";
                        }

                        continue;
                    }

                    tested++;
                    heightStepTested++;
                    Classify(view, map, camera, screen, cell, "center(reframed)", ref exactMatches, ref occludedByTaller, failures);

                    var neighborWorld = view.ProjectTop(heightStepNeighbor);
                    var nearEdge = Vector3.Lerp(world, neighborWorld, 0.3f);
                    nearEdge.y = world.y;
                    if (TryWorldToOnScreenPoint(camera, nearEdge, out var edgeScreen))
                    {
                        tested++;
                        Classify(view, map, camera, edgeScreen, cell, "near-edge(reframed)", ref exactMatches, ref occludedByTaller, failures);
                    }
                }

                camera.transform.position = originalCameraPosition;
            }

            Assert.That(failures, Is.Empty,
                $"Picking mismatches that no taller occluder explains:\n{string.Join("\n", failures)}");
            var heightHistogram = new Dictionary<int, int>();
            foreach (var cell in map.AllCells)
            {
                heightHistogram.TryGetValue(cell.HeightLevel, out var count);
                heightHistogram[cell.HeightLevel] = count + 1;
            }

            var histogramText = string.Join(", ", System.Linq.Enumerable.Select(
                System.Linq.Enumerable.OrderBy(heightHistogram, pair => pair.Key),
                pair => $"h{pair.Key}:{pair.Value}"));
            Assert.That(heightStepTested, Is.GreaterThanOrEqualTo(1),
                $"No height-step cell was testable even after reframing — map heights or camera setup changed. Loaded map height histogram: {histogramText}. candidates={reframeCandidates}, offscreen={reframeOffscreen}, {reframeDebug}");
            Assert.That(tested, Is.GreaterThanOrEqualTo(MinTestedCells),
                $"Too few on-screen spot cells ({tested}) — camera framing changed? boundary={boundaryTested}, heightStep={heightStepTested}.");
            Assert.That(exactMatches, Is.GreaterThanOrEqualTo((tested * 7) / 10),
                $"Exact round-trip rate too low: {exactMatches}/{tested} (occluded-by-taller: {occludedByTaller}).");
            Debug.Log(
                $"[PickingSpot] tested={tested} exact={exactMatches} occludedByTaller={occludedByTaller} boundary={boundaryTested} heightStep={heightStepTested}");
        }

        private static void Classify(
            AtlasTilePresentationView view,
            HexMapData map,
            Camera camera,
            Vector2 screen,
            HexCellData cell,
            string kind,
            ref int exactMatches,
            ref int occludedByTaller,
            List<string> failures)
        {
            if (!view.TryScreenToHex(camera, screen, out var picked))
            {
                failures.Add($"{cell.Coord} ({kind}): TryScreenToHex returned no hit.");
                return;
            }

            if (picked.Equals(cell.Coord))
            {
                exactMatches++;
                return;
            }

            if (map.TryGetCell(picked, out var pickedCell) && pickedCell.HeightLevel > cell.HeightLevel)
            {
                occludedByTaller++;
                return;
            }

            failures.Add($"{cell.Coord} ({kind}) -> {picked}: mismatch without a taller occluder.");
        }

        private static bool TryWorldToOnScreenPoint(Camera camera, Vector3 world, out Vector2 screen)
        {
            var projected = camera.WorldToScreenPoint(world);
            screen = new Vector2(projected.x, projected.y);
            return projected.z > 0f && camera.pixelRect.Contains(screen);
        }
    }
}
#endif
