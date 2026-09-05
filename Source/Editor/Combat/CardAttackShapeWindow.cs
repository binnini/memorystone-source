using System.Linq;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Map.Runtime;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Editor.Combat
{
    public class CardAttackShapeWindow : EditorWindow
    {
        private static readonly string[] ShapeIds =
        {
            AttackShapeLibrary.Single,
            AttackShapeLibrary.Line2,
            AttackShapeLibrary.Line3,
            AttackShapeLibrary.Line4,
            AttackShapeLibrary.ConeNear,
            AttackShapeLibrary.ConeMid,
            AttackShapeLibrary.TForward,
            AttackShapeLibrary.CrossNear,
        };

        private int _selectedIndex;

        [MenuItem("서울 플레이업/카드 공격 형태 미리보기")]
        private static void Open() => GetWindow<CardAttackShapeWindow>("공격 형태 미리보기");

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            _selectedIndex = EditorGUILayout.Popup("형태 ID", _selectedIndex, ShapeIds);
            var shapeId = ShapeIds[_selectedIndex];

            if (!AttackShapeLibrary.TryGet(shapeId, out var shape))
            {
                EditorGUILayout.HelpBox("알 수 없는 형태 ID", MessageType.Warning);
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("셀 좌표 (Q,R):", EditorStyles.miniLabel);
            var coordList = string.Join("  ", shape.Offsets.Select(c => $"({c.Q},{c.R})"));
            EditorGUILayout.LabelField(coordList, EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(8);
            var previewRect = GUILayoutUtility.GetRect(position.width, 240);
            DrawHexPreview(previewRect, shape.Offsets);
        }

        private static void DrawHexPreview(Rect area, HexCoord[] offsets)
        {
            EditorGUI.DrawRect(area, new Color(0.13f, 0.13f, 0.13f));

            const float cellSize = 24f;
            const float sqrt3 = 1.7320508f;
            var center = new Vector2(area.x + area.width * 0.5f, area.y + area.height * 0.5f);
            var hitSet = new System.Collections.Generic.HashSet<HexCoord>(offsets);

            // flat-top axial: East = (1,0) = right
            for (var q = -4; q <= 4; q++)
            for (var r = -4; r <= 4; r++)
            {
                var px = center.x + cellSize * (1.5f * q);
                var py = center.y + cellSize * (sqrt3 * 0.5f * q + sqrt3 * r);

                if (px < area.xMin || px > area.xMax || py < area.yMin || py > area.yMax)
                    continue;

                var coord = new HexCoord(q, r);
                Color fill;
                if (q == 0 && r == 0)
                    fill = new Color(0.2f, 0.55f, 1f);   // 플레이어
                else if (hitSet.Contains(coord))
                    fill = new Color(1f, 0.3f, 0.25f);   // 피격
                else
                    fill = new Color(0.3f, 0.3f, 0.3f);  // 빈 칸

                var side = cellSize * 0.82f;
                EditorGUI.DrawRect(new Rect(px - side * 0.5f, py - side * 0.5f, side, side), fill);
            }

            // 범례
            var lx = area.x + 8f;
            var ly = area.yMax - 26f;
            EditorGUI.DrawRect(new Rect(lx, ly + 4f, 12f, 12f), new Color(0.2f, 0.55f, 1f));
            GUI.Label(new Rect(lx + 16f, ly, 70f, 20f), "플레이어", EditorStyles.whiteLabel);
            EditorGUI.DrawRect(new Rect(lx + 90f, ly + 4f, 12f, 12f), new Color(1f, 0.3f, 0.25f));
            GUI.Label(new Rect(lx + 106f, ly, 70f, 20f), "피격 범위", EditorStyles.whiteLabel);
        }
    }
}
