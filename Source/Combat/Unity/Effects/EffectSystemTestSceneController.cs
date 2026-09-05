using System.Collections.Generic;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

namespace SeoulPlayup.Combat.Unity
{
    public sealed class EffectSystemTestSceneController : MonoBehaviour
    {
        private const int BoardRadius = 2;
        private readonly Dictionary<HexCoord, GameObject> tileObjects = new Dictionary<HexCoord, GameObject>();
        private readonly Dictionary<string, Transform> effectTargets = new Dictionary<string, Transform>();
        private EffectRuntime runtime;
        private HexVisibilityRuntime visibility;
        private CombatantState player;
        private CombatantState monster;
        private EffectPresentationController presentation;
        private TextMeshProUGUI statusText;
        private GameObject playerMarker;
        private GameObject monsterMarker;
        private Material fogMaterial;
        private Material revealedMaterial;
        private Material highlightedMaterial;
        private Material playerMaterial;
        private Material monsterMaterial;
        private EffectDefinition damageDefinition;
        private EffectDefinition blockDefinition;
        private EffectDefinition healDefinition;
        private EffectDefinition fogRevealDefinition;
        private EffectDefinition fieldRevealDefinition;
        private EffectDefinition fieldDamageDefinition;
        private EffectDefinition fieldHealDefinition;
        private EffectDefinition fieldImmobilizeDefinition;
        private EffectDefinition reflectDefinition;
        private EffectDefinition immobilizeDefinition;
        private EffectDefinition agilityDefinition;

        private void Awake()
        {
            EnsureSceneCamera();
            EnsureEventSystem();
            runtime = new EffectRuntime();
            visibility = new HexVisibilityRuntime(CreateRadiusMap(BoardRadius), new HexCoord(0, 0), 0);
            player = new CombatantState("player", 20);
            monster = new CombatantState("monster", 14);
            damageDefinition = new EffectDefinition("scene.damage", EffectType.Instant, EffectKind.Damage, amount: 4, sourceRef: "scene.damage");
            blockDefinition = new EffectDefinition("scene.block", EffectType.Instant, EffectKind.Block, amount: 5, sourceRef: "scene.block");
            healDefinition = new EffectDefinition("scene.heal", EffectType.Instant, EffectKind.Heal, amount: 5, sourceRef: "scene.heal");
            fogRevealDefinition = new EffectDefinition("scene.fog-reveal", EffectType.Instant, EffectKind.FogReveal, radius: 1, sourceRef: "scene.fog-reveal");
            fieldRevealDefinition = new EffectDefinition("scene.field-reveal", EffectType.FieldObject, EffectKind.FogReveal, radius: 1, durationTurns: 2, sourceRef: "scene.field-reveal");
            fieldDamageDefinition = new EffectDefinition("scene.field-damage", EffectType.FieldObject, EffectKind.Damage, amount: 2, radius: 1, durationTurns: 2, sourceRef: "scene.field-damage");
            fieldHealDefinition = new EffectDefinition("scene.field-heal", EffectType.FieldObject, EffectKind.Heal, amount: 3, radius: 1, durationTurns: 2, sourceRef: "scene.field-heal");
            fieldImmobilizeDefinition = new EffectDefinition("scene.field-immobilize", EffectType.FieldObject, StatusEffectKind.Immobilize, amount: 1, radius: 1, durationTurns: 2, sourceRef: "scene.field-immobilize");
            reflectDefinition = new EffectDefinition("scene.reflect", EffectType.Instant, EffectKind.ReflectDamage, amount: 50, sourceRef: "scene.reflect");
            immobilizeDefinition = new EffectDefinition("scene.immobilize", EffectType.Duration, StatusEffectKind.Immobilize, durationTurns: 2, sourceRef: "scene.immobilize");
            agilityDefinition = new EffectDefinition("scene.agility", EffectType.Duration, StatusEffectKind.Agility, amount: 2, durationTurns: 2, sourceRef: "scene.agility");
            presentation = GetComponent<EffectPresentationController>() ?? gameObject.AddComponent<EffectPresentationController>();
            runtime.EffectResolved += OnEffectResolved;
            CreateMaterials();
            BuildBoard();
            BuildUi();
            RefreshStatus();
        }

        private void OnDestroy()
        {
            DestroyMaterial(fogMaterial);
            DestroyMaterial(revealedMaterial);
            DestroyMaterial(highlightedMaterial);
            DestroyMaterial(playerMaterial);
            DestroyMaterial(monsterMaterial);
        }

        public void TriggerDamage()
        {
            runtime.Apply(damageDefinition, target: monster);
            RefreshStatus();
        }

        public void TriggerBlock()
        {
            runtime.Apply(blockDefinition, target: player);
            RefreshStatus();
        }

        public void TriggerHeal()
        {
            runtime.ApplyDamage(player, 3, "scene.self-damage-setup");
            runtime.Apply(healDefinition, target: player);
            RefreshStatus();
        }

        public void TriggerFogReveal()
        {
            runtime.Apply(fogRevealDefinition, visibility: visibility, center: new HexCoord(1, 0));
            RefreshTileVisibility();
            RefreshStatus();
        }

        public void TriggerFieldReveal()
        {
            runtime.Apply(fieldRevealDefinition, visibility: visibility, center: new HexCoord(0, 1));
            RefreshTileVisibility();
            RefreshStatus();
        }

        public void TriggerFieldDamage()
        {
            runtime.Apply(fieldDamageDefinition, center: new HexCoord(1, 0));
            RefreshStatus();
        }

        public void TriggerFieldHeal()
        {
            runtime.ApplyDamage(player, 4, "scene.field-heal-setup");
            runtime.Apply(fieldHealDefinition, center: new HexCoord(0, 0));
            RefreshStatus();
        }

        public void TriggerFieldImmobilize()
        {
            runtime.Apply(fieldImmobilizeDefinition, center: new HexCoord(1, 0));
            RefreshStatus();
        }

        public void TriggerFieldTick()
        {
            runtime.TickFieldObjects(visibility, CreateFieldObjectTargets());
            RefreshTileVisibility();
            RefreshStatus();
        }

        public void TriggerReflect()
        {
            runtime.Apply(reflectDefinition, source: monster, contextAmount: 8);
            RefreshStatus();
        }

        public void TriggerImmobilize()
        {
            runtime.Apply(immobilizeDefinition, target: monster);
            RefreshStatus();
        }

        public void TriggerAgility()
        {
            runtime.Apply(agilityDefinition, target: player);
            RefreshStatus();
        }


        private void OnEffectResolved(EffectResultEvent resultEvent)
        {
            var position = effectTargets.TryGetValue(resultEvent.TargetUnitId, out var target)
                ? target.position
                : transform.position + Vector3.up * 0.65f;
            if (resultEvent.Center.HasValue && tileObjects.TryGetValue(resultEvent.Center.Value, out var tile))
            {
                position = tile.transform.position + Vector3.up * 0.15f;
            }

            presentation.Play(resultEvent, position);
        }

        private void BuildBoard()
        {
            foreach (var cell in visibility.Map.AllCells)
            {
                var tile = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tile.name = $"Effect Test Tile {cell.Coord}";
                tile.transform.SetParent(transform, false);
                tile.transform.position = HexToWorld(cell.Coord);
                tile.transform.localScale = new Vector3(0.95f, 0.06f, 0.95f);
                var renderer = tile.GetComponent<Renderer>();
                renderer.sharedMaterial = GetTileMaterial(cell.Coord, highlighted: false);
                tileObjects[cell.Coord] = tile;
            }

            playerMarker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            playerMarker.name = "Effect Test Player";
            playerMarker.transform.SetParent(transform, false);
            playerMarker.transform.position = HexToWorld(new HexCoord(0, 0)) + Vector3.up * 0.65f;
            playerMarker.transform.localScale = new Vector3(0.45f, 0.8f, 0.45f);
            playerMarker.GetComponent<Renderer>().sharedMaterial = playerMaterial;
            effectTargets[player.Id] = playerMarker.transform;

            monsterMarker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            monsterMarker.name = "Effect Test Monster";
            monsterMarker.transform.SetParent(transform, false);
            monsterMarker.transform.position = HexToWorld(new HexCoord(1, 0)) + Vector3.up * 0.65f;
            monsterMarker.transform.localScale = new Vector3(0.45f, 0.8f, 0.45f);
            monsterMarker.GetComponent<Renderer>().sharedMaterial = monsterMaterial;
            effectTargets[monster.Id] = monsterMarker.transform;
        }

        private void BuildUi()
        {
            var canvasGo = new GameObject("Effect System Test UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);

            var panel = new GameObject("Controls", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.anchoredPosition = new Vector2(24f, -24f);
            panelRect.sizeDelta = new Vector2(320f, 540f);
            panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);

            statusText = CreateText(panel.transform, "Status", new Vector2(12f, -12f), new Vector2(296f, 72f), 20f);
            CreateButton(panel.transform, "Damage", new Vector2(12f, -96f), TriggerDamage);
            CreateButton(panel.transform, "Block", new Vector2(164f, -96f), TriggerBlock);
            CreateButton(panel.transform, "Heal", new Vector2(12f, -152f), TriggerHeal);
            CreateButton(panel.transform, "FogReveal", new Vector2(164f, -152f), TriggerFogReveal);
            CreateButton(panel.transform, "Reflect", new Vector2(12f, -208f), TriggerReflect);
            CreateButton(panel.transform, "Immobilize", new Vector2(164f, -208f), TriggerImmobilize);
            CreateButton(panel.transform, "Agility", new Vector2(12f, -264f), TriggerAgility);
            CreateButton(panel.transform, "FieldReveal", new Vector2(12f, -320f), TriggerFieldReveal);
            CreateButton(panel.transform, "FieldDamage", new Vector2(164f, -320f), TriggerFieldDamage);
            CreateButton(panel.transform, "FieldHeal", new Vector2(12f, -376f), TriggerFieldHeal);
            CreateButton(panel.transform, "FieldRoot", new Vector2(164f, -376f), TriggerFieldImmobilize);
            CreateButton(panel.transform, "Tick Fields", new Vector2(12f, -432f), TriggerFieldTick);
        }

        private TextMeshProUGUI CreateText(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, float fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var text = go.GetComponent<TextMeshProUGUI>();
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            return text;
        }

        private void CreateButton(Transform parent, string label, Vector2 anchoredPosition, UnityEngine.Events.UnityAction action)
        {
            var go = new GameObject($"{label} Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(140f, 42f);
            go.GetComponent<Image>().color = new Color(0.12f, 0.18f, 0.24f, 0.95f);
            go.GetComponent<Button>().onClick.AddListener(action);
            var text = CreateText(go.transform, "Label", new Vector2(0f, -6f), new Vector2(140f, 32f), 18f);
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
        }

        private void RefreshStatus()
        {
            if (statusText != null)
            {
                statusText.text = $"EffectSystemTest\\nPlayer HP {player.Hp}/{player.MaxHp} Block {player.Block}\\nMonster HP {monster.Hp}/{monster.MaxHp}\\nActive Duration {runtime.ActiveEffects.Count} FieldObjects {runtime.FieldObjects.Objects.Count}";
            }
        }

        private IEnumerable<FieldObjectTarget> CreateFieldObjectTargets()
        {
            yield return new FieldObjectTarget(player, new HexCoord(0, 0), FieldObjectTargetKind.Player);
            yield return new FieldObjectTarget(monster, new HexCoord(1, 0), FieldObjectTargetKind.Monster);
        }

        private void RefreshTileVisibility()
        {
            foreach (var pair in tileObjects)
            {
                pair.Value.GetComponent<Renderer>().sharedMaterial = GetTileMaterial(
                    pair.Key,
                    highlighted: visibility.GetVisibility(pair.Key) == HexCellVisibility.Revealed);
            }
        }

        private static HexMapData CreateRadiusMap(int radius)
        {
            var cells = new List<HexCellData>();
            for (var q = -radius; q <= radius; q++)
            {
                for (var r = -radius; r <= radius; r++)
                {
                    var coord = new HexCoord(q, r);
                    if (new HexCoord(0, 0).DistanceTo(coord) <= radius)
                    {
                        cells.Add(new HexCellData(coord, "ground", "street", 1, true, false));
                    }
                }
            }

            return new HexMapData(cells);
        }

        private static Vector3 HexToWorld(HexCoord coord)
        {
            const float spacing = 1.15f;
            var x = spacing * (Mathf.Sqrt(3f) * coord.Q + Mathf.Sqrt(3f) / 2f * coord.R);
            var z = spacing * (1.5f * coord.R);
            return new Vector3(x, 0f, z);
        }

        private void CreateMaterials()
        {
            fogMaterial = CreateMaterial(new Color(0.08f, 0.08f, 0.1f));
            revealedMaterial = CreateMaterial(new Color(0.3f, 0.35f, 0.4f));
            highlightedMaterial = CreateMaterial(new Color(0.92f, 0.78f, 0.32f));
            playerMaterial = CreateMaterial(new Color(0.2f, 0.7f, 1f));
            monsterMaterial = CreateMaterial(new Color(1f, 0.25f, 0.18f));
        }

        private Material GetTileMaterial(HexCoord coord, bool highlighted)
        {
            if (highlighted)
            {
                return highlightedMaterial;
            }

            return visibility.GetVisibility(coord) == HexCellVisibility.Revealed
                ? revealedMaterial
                : fogMaterial;
        }

        private static Material CreateMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            SetMaterialColor(material, color);
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            material.color = color;
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(material);
            }
            else
            {
                DestroyImmediate(material);
            }
        }

        private static void EnsureSceneCamera()
        {
            if (Camera.main != null)
            {
                return;
            }

            var cameraGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraGo.tag = "MainCamera";
            cameraGo.transform.position = new Vector3(0f, 7.5f, -7.5f);
            cameraGo.transform.rotation = Quaternion.Euler(55f, 0f, 0f);
            cameraGo.GetComponent<Camera>().clearFlags = CameraClearFlags.SolidColor;
            cameraGo.GetComponent<Camera>().backgroundColor = new Color(0.04f, 0.05f, 0.07f);

            var lightGo = new GameObject("Directional Light", typeof(Light));
            lightGo.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            lightGo.GetComponent<Light>().type = LightType.Directional;
            lightGo.GetComponent<Light>().intensity = 1.2f;
        }

        private static void EnsureEventSystem()
        {
            var eventSystem = Object.FindFirstObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                eventSystem = new GameObject("Effect System Test EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            }

            eventSystem.gameObject.SetActive(true);
#if ENABLE_INPUT_SYSTEM
            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            }
#else
            if (eventSystem.GetComponent<StandaloneInputModule>() == null)
            {
                eventSystem.gameObject.AddComponent<StandaloneInputModule>();
            }
#endif
        }
    }
}


