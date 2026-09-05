using System;
using System.Collections.Generic;
using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity
{
    [ExecuteAlways]
    public sealed class MonsterDesignTestController : MonoBehaviour
    {
        private const string DesignerMonsterId = "designer-monster";
        private const string DesignerDefinitionId = "designer-test-monster";
        private const string DesignerCatalogId = "monster-design-test-catalog";

        [Header("Board")]
        [SerializeField] private int mapRadius = 4;
        [SerializeField] private int dummyQ;
        [SerializeField] private int dummyR;
        [SerializeField] private int monsterQ = 3;
        [SerializeField] private int monsterR;

        [Header("Stats")]
        [SerializeField] private int dummyMaxHp = 30;
        [SerializeField] private int monsterMaxHp = 20;
        [SerializeField] private int monsterChaseRange = 6;
        [SerializeField] private int monsterAttackSpeed = 1;
        [SerializeField] private bool useOnlySelectedPattern = true;
        [SerializeField] private int selectedPatternIndex;

        [Header("Scene Markers")]
        [SerializeField] private Transform dummyMarker;
        [SerializeField] private Transform monsterMarker;
        [SerializeField] private float tileSpacing = 1.25f;
        [SerializeField] private float markerHeight = 0.25f;

        [Header("Editable Attack Patterns")]
        [SerializeField] private List<DesignerMonsterAttackPattern> attackPatterns = new List<DesignerMonsterAttackPattern>();

        [Header("Runtime Output")]
        [SerializeField, TextArea(4, 10)] private string statusText = "Press Reset Sandbox.";

        private CombatState state;
        private IReadOnlyList<HexCoord> currentAttackCells = Array.Empty<HexCoord>();

        public CombatState State => state;
        public IReadOnlyList<DesignerMonsterAttackPattern> AttackPatterns => attackPatterns;
        public string StatusText => statusText;
        public int SelectedPatternIndex => selectedPatternIndex;
        public bool UseOnlySelectedPattern => useOnlySelectedPattern;
        public HexCoord DummyCoord => new HexCoord(dummyQ, dummyR);
        public HexCoord MonsterCoord => new HexCoord(monsterQ, monsterR);
        public IReadOnlyList<HexCoord> CurrentAttackCells => currentAttackCells;

        private void Reset()
        {
            LoadRangedRecommendations();
            ResetSandbox();
        }

        private void OnValidate()
        {
            mapRadius = Mathf.Max(1, mapRadius);
            dummyMaxHp = Mathf.Max(1, dummyMaxHp);
            monsterMaxHp = Mathf.Max(1, monsterMaxHp);
            monsterChaseRange = Mathf.Max(0, monsterChaseRange);
            monsterAttackSpeed = Mathf.Max(1, monsterAttackSpeed);
            selectedPatternIndex = Mathf.Clamp(selectedPatternIndex, 0, Mathf.Max(0, attackPatterns.Count - 1));
            SyncMarkers();
        }

        private void OnEnable()
        {
            if (attackPatterns.Count == 0)
            {
                LoadRangedRecommendations();
            }

            SyncMarkers();
        }

        public void LoadRangedRecommendations()
        {
            attackPatterns = RecommendedMonsterAttackPatternLibrary
                .CreateTestRangedRecommendations(CreateConfig())
                .Select(DesignerMonsterAttackPattern.FromRuntime)
                .ToList();
            selectedPatternIndex = Mathf.Clamp(selectedPatternIndex, 0, Mathf.Max(0, attackPatterns.Count - 1));
            statusText = $"Loaded {attackPatterns.Count} ranged recommendation patterns. They are editable and not registered to the live catalog.";
            ResetSandbox();
        }

        public void LoadMeleeRecommendations()
        {
            attackPatterns = RecommendedMonsterAttackPatternLibrary
                .CreateTestMeleeRecommendations(CreateConfig())
                .Select(DesignerMonsterAttackPattern.FromRuntime)
                .ToList();
            selectedPatternIndex = Mathf.Clamp(selectedPatternIndex, 0, Mathf.Max(0, attackPatterns.Count - 1));
            statusText = $"Loaded {attackPatterns.Count} melee recommendation patterns. They are editable and not registered to the live catalog.";
            ResetSandbox();
        }

        public void AddBlankPattern()
        {
            var index = attackPatterns.Count + 1;
            attackPatterns.Add(new DesignerMonsterAttackPattern
            {
                Id = $"custom-pattern-{index}",
                DisplayName = $"Custom Pattern {index}",
                Range = 1,
                AreaRadius = 0,
                Damage = 1,
                EffectRef = "attack.damage.custom",
                Targeting = "player_in_range",
                Weight = 1,
                ShapeId = AttackShapeLibrary.Single
            });
            selectedPatternIndex = attackPatterns.Count - 1;
            ResetSandbox();
        }

        public void SelectPreviousPattern()
        {
            if (attackPatterns.Count == 0)
            {
                selectedPatternIndex = 0;
                return;
            }

            selectedPatternIndex = (selectedPatternIndex + attackPatterns.Count - 1) % attackPatterns.Count;
            ResetSandbox();
        }

        public void SelectNextPattern()
        {
            if (attackPatterns.Count == 0)
            {
                selectedPatternIndex = 0;
                return;
            }

            selectedPatternIndex = (selectedPatternIndex + 1) % attackPatterns.Count;
            ResetSandbox();
        }

        public void ResetSandbox()
        {
            var patterns = BuildRuntimePatterns();
            if (patterns.Count == 0)
            {
                statusText = "No attack patterns. Add or load at least one pattern.";
                state = null;
                currentAttackCells = Array.Empty<HexCoord>();
                SyncMarkers();
                return;
            }

            var config = CreateConfig();
            var catalog = new MonsterCatalogDefinition(
                DesignerCatalogId,
                "Monster Design Test Catalog",
                new[]
                {
                    new MonsterCatalogEntry(
                        DesignerDefinitionId,
                        "Designer Test Monster",
                        "designer",
                        "designer.manual_pattern_test",
                        monsterChaseRange,
                        1,
                        monsterMaxHp,
                        "design_test_only",
                        monsterAttackSpeed,
                        patterns.ToArray())
                });

            state = new CombatState(
                CombatState.CreateDemoMap(mapRadius),
                DummyCoord,
                new[] { new MonsterConfig(DesignerMonsterId, MonsterCoord, monsterMaxHp, DesignerCatalogId, DesignerDefinitionId) },
                config,
                monsterCatalog: catalog);
            RefreshPreviewText("Sandbox reset.");
            SyncMarkers();
        }

        public void ResolveMonsterTurn()
        {
            if (state == null)
            {
                ResetSandbox();
            }

            if (state == null)
            {
                return;
            }

            var hpBefore = state.Player.Hp;
            if (state.Phase == CombatPhase.PlayerMovement)
            {
                state.EndAction(); // -> MonsterMovement
            }

            if (state.Phase == CombatPhase.MonsterMovement)
            {
                state.ResolveMonsterMovement(); // -> PlayerAction (DEC-2026-07-03-02)
            }

            if (state.Phase == CombatPhase.PlayerAction)
            {
                state.EndAction(); // -> MonsterAction
            }

            state.ResolveMonsterAction();
            var record = state.LastMonsterActionRecords.FirstOrDefault(action => action.MonsterId == DesignerMonsterId);
            var delta = hpBefore - state.Player.Hp;
            RefreshPreviewText(record.AttackedPlayer
                ? $"Monster attacked dummy for {delta} damage. Dummy HP {state.Player.Hp}/{state.Player.MaxHp}."
                : $"Monster did not attack. Dummy HP {state.Player.Hp}/{state.Player.MaxHp}.");
            SyncFromState();
        }

        public void MoveDummyToMonsterRangeAndReset()
        {
            dummyQ = Mathf.Clamp(monsterQ - 1, -mapRadius, mapRadius);
            dummyR = monsterR;
            ResetSandbox();
        }

        public void MoveMonsterToRangedTestAndReset()
        {
            monsterQ = Mathf.Clamp(dummyQ + 3, -mapRadius, mapRadius);
            monsterR = dummyR;
            ResetSandbox();
        }

        public void RefreshPreview()
        {
            if (state == null)
            {
                ResetSandbox();
                return;
            }

            RefreshPreviewText("Preview refreshed.");
        }

        private void RefreshPreviewText(string prefix)
        {
            if (state == null)
            {
                statusText = prefix;
                currentAttackCells = Array.Empty<HexCoord>();
                return;
            }

            var preview = state.GetMonsterIntentPreviews(includeUnrevealed: true)
                .FirstOrDefault(item => item.MonsterId == DesignerMonsterId);
            currentAttackCells = preview.AttackRangeCoords ?? Array.Empty<HexCoord>();
            var monster = state.Monsters.FirstOrDefault(item => item.Id == DesignerMonsterId);
            var selected = BuildRuntimePatterns().ElementAtOrDefault(Mathf.Clamp(selectedPatternIndex, 0, Mathf.Max(0, BuildRuntimePatterns().Count - 1)));
            statusText = string.Join("\n", new[]
            {
                prefix,
                $"Selected Pattern: {selected.Id} / {selected.DisplayName}",
                $"Intent: {preview.IntentType}  WillMove: {preview.WillMove}  Predicted: {preview.PredictedMoveCoord}",
                $"Attack Cells: {string.Join(", ", currentAttackCells)}",
                $"Dummy HP: {state.Player.Hp}/{state.Player.MaxHp} @ {state.PlayerCoord}",
                $"Monster HP: {monster.Hp}/{monster.MaxHp} @ {monster.Coord}"
            });
        }

        private List<MonsterAttackPattern> BuildRuntimePatterns()
        {
            var source = attackPatterns
                .Where(pattern => pattern != null)
                .Select(pattern => pattern.ToRuntime())
                .ToList();
            if (source.Count == 0)
            {
                return source;
            }

            if (!useOnlySelectedPattern)
            {
                return source;
            }

            return new List<MonsterAttackPattern> { source[Mathf.Clamp(selectedPatternIndex, 0, source.Count - 1)] };
        }

        private CombatConfig CreateConfig()
        {
            return new CombatConfig(
                dummyMaxHp,
                monsterMaxHp,
                playerMovePoints: 2,
                attackRange: 1,
                attackDamage: 1,
                defenseBlock: 0,
                enemyChaseRange: monsterChaseRange,
                enemyAttackRange: 1,
                enemyAttackDamage: 1,
                actionBudget: 4,
                movementHandSize: 1,
                actionHandSize: 1,
                playerVisionRange: mapRadius + 2);
        }

        private void SyncFromState()
        {
            if (state == null)
            {
                return;
            }

            dummyQ = state.PlayerCoord.Q;
            dummyR = state.PlayerCoord.R;
            var monster = state.Monsters.FirstOrDefault(item => item.Id == DesignerMonsterId);
            if (!string.IsNullOrEmpty(monster.Id))
            {
                monsterQ = monster.Coord.Q;
                monsterR = monster.Coord.R;
            }

            SyncMarkers();
        }

        private void SyncMarkers()
        {
            if (dummyMarker != null)
            {
                dummyMarker.localPosition = ToWorld(DummyCoord) + Vector3.up * markerHeight;
                dummyMarker.name = "Damage Dummy Marker";
            }

            if (monsterMarker != null)
            {
                monsterMarker.localPosition = ToWorld(MonsterCoord) + Vector3.up * markerHeight;
                monsterMarker.name = "Monster Marker";
            }
        }

        private Vector3 ToWorld(HexCoord coord)
        {
            var x = tileSpacing * (coord.Q + coord.R * 0.5f);
            var z = tileSpacing * coord.R * 0.8660254f;
            return new Vector3(x, 0f, z);
        }

        private void OnDrawGizmos()
        {
            DrawBoardGizmos();
            DrawAttackGizmos();
        }

        private void DrawBoardGizmos()
        {
            Gizmos.color = new Color(0.18f, 0.18f, 0.18f, 0.35f);
            for (var q = -mapRadius; q <= mapRadius; q++)
            {
                for (var r = -mapRadius; r <= mapRadius; r++)
                {
                    var coord = new HexCoord(q, r);
                    if (Mathf.Abs(coord.S) > mapRadius)
                    {
                        continue;
                    }

                    Gizmos.DrawWireSphere(transform.TransformPoint(ToWorld(coord)), 0.08f);
                }
            }
        }

        private void DrawAttackGizmos()
        {
            Gizmos.color = new Color(1f, 0.15f, 0.05f, 0.55f);
            foreach (var coord in currentAttackCells ?? Array.Empty<HexCoord>())
            {
                Gizmos.DrawCube(transform.TransformPoint(ToWorld(coord) + Vector3.up * 0.03f), new Vector3(0.7f, 0.05f, 0.7f));
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.TransformPoint(ToWorld(DummyCoord) + Vector3.up * markerHeight), 0.35f);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.TransformPoint(ToWorld(MonsterCoord) + Vector3.up * markerHeight), 0.4f);
        }
    }

    [Serializable]
    public sealed class DesignerMonsterAttackPattern
    {
        public string Id = "custom-pattern";
        public string DisplayName = "Custom Pattern";
        public int Range = 1;
        public int AreaRadius;
        public int Damage = 1;
        public string EffectRef = "attack.damage";
        public string Targeting = "player_in_range";
        public int Weight = 1;
        public string ShapeId = AttackShapeLibrary.Single;

        public MonsterAttackPattern ToRuntime()
        {
            return new MonsterAttackPattern(
                Id,
                DisplayName,
                Range,
                AreaRadius,
                Damage,
                EffectRef,
                Targeting,
                Weight,
                string.IsNullOrWhiteSpace(ShapeId) ? null : ShapeId);
        }

        public static DesignerMonsterAttackPattern FromRuntime(MonsterAttackPattern pattern)
        {
            return new DesignerMonsterAttackPattern
            {
                Id = pattern.Id,
                DisplayName = pattern.DisplayName,
                Range = pattern.Range,
                AreaRadius = pattern.AreaRadius,
                Damage = pattern.Damage,
                EffectRef = pattern.EffectRef,
                Targeting = pattern.Targeting,
                Weight = pattern.Weight,
                ShapeId = pattern.ShapeId
            };
        }
    }
}
