using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    /// <summary>
    /// 사망 애니메이션이 <b>끝까지 재생되는가</b>의 배선 계약(2026-09-05 실플레이 피드백:
    /// "두억시니 사망 애니메이션이 재생이 안됐음").
    ///
    /// <para>🔴 원인은 저작이 아니라 <b>전이 조건</b>이었다. 여섯 종 전부 사망 클립도, Death 상태도,
    /// <c>Any→Death</c> 전이도 있었다 — 그런데 피격·넉백·공격 전이가 전부 AnyState에서 자기 트리거
    /// 하나만 보고 있었고, Death는 AnyState의 또 다른 목적지일 뿐이라 우선권이 없다. 유니티 트리거는
    /// 소비될 때까지 남으므로, 죽는 순간까지 쌓여 있던 피격 트리거가 Death 진입 직후 다시 발화해
    /// 시체가 「맞는 자세」로 돌아갔다. 죽이는 데 여러 대가 필요한 정예에서 특히 잘 재현된다.</para>
    ///
    /// <para>이 스위트는 「Death는 종착역이다」를 잰다. 컨트롤러는 CSV에서 <b>생성</b>되므로
    /// (<c>MonsterAnimatorSetupUtility</c>) 여기서 red가 나면 손으로 고치지 말고 생성기를 고치고
    /// <c>Seoul Playup/Combat/Setup Monster Animators</c>를 다시 돌릴 것.</para>
    /// </summary>
    [Category("ShippingData")]
    public sealed class MonsterDeathAnimatorWiringTests
    {
        private const string AnimationSetsPath = "Assets/Data/Combat/Monsters/Source/monster_animation_sets.csv";

        [Test]
        public void EveryDeathClipIsAuthoredAndBoundToADeathState()
        {
            var rows = ReadEnabledRows();
            Assert.That(rows, Is.Not.Empty, $"{AnimationSetsPath}에 활성 행이 없다.");

            foreach (var row in rows)
            {
                var controller = LoadController(row);
                var death = FindState(controller, "Death");
                Assert.That(death, Is.Not.Null, $"{row["monsterId"]}: Death 상태가 없다.");
                Assert.That(death.motion, Is.Not.Null,
                    $"{row["monsterId"]}: Death 상태에 클립이 안 붙었다 — 죽어도 아무 자세가 안 나온다.");
                Assert.That(death.transitions, Is.Empty,
                    $"{row["monsterId"]}: Death에서 나가는 전이가 있다 — 사망은 종착역이어야 한다.");
            }
        }

        [Test]
        public void NothingCanStealThePoseOnceTheMonsterIsDead()
        {
            var rows = ReadEnabledRows();
            foreach (var row in rows)
            {
                var monsterId = row["monsterId"];
                var deathParameter = row["deathParameter"];
                Assert.That(deathParameter, Is.Not.Empty, $"{monsterId}: deathParameter가 비었다.");

                var controller = LoadController(row);
                var death = FindState(controller, "Death");
                var anyTransitions = controller.layers[0].stateMachine.anyStateTransitions;
                Assert.That(anyTransitions, Is.Not.Empty, $"{monsterId}: AnyState 전이가 없다.");

                foreach (var transition in anyTransitions)
                {
                    if (transition.destinationState == death)
                    {
                        continue;
                    }

                    Assert.That(
                        transition.conditions.Any(condition =>
                            condition.mode == AnimatorConditionMode.IfNot &&
                            condition.parameter == deathParameter),
                        Is.True,
                        $"{monsterId}: '{transition.destinationState?.name}'로 가는 AnyState 전이에 " +
                        $"'{deathParameter} == false' 가드가 없다 — 죽은 뒤에도 이 전이가 사망 자세를 빼앗는다.");
                }
            }
        }

        private static AnimatorController LoadController(IReadOnlyDictionary<string, string> row)
        {
            var path = row["controllerPath"];
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            Assert.That(controller, Is.Not.Null, $"{row["monsterId"]}: 컨트롤러가 없다 ({path}).");
            Assert.That(controller.layers, Is.Not.Empty, $"{row["monsterId"]}: 레이어가 없다.");
            return controller;
        }

        private static AnimatorState FindState(AnimatorController controller, string name)
        {
            return controller.layers[0].stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(state => state != null && state.name == name);
        }

        /// <summary>활성 행만 읽는다 — 비활성 행은 컨트롤러를 만들지 않으므로 계약 대상이 아니다.</summary>
        private static List<Dictionary<string, string>> ReadEnabledRows()
        {
            var lines = File.ReadAllLines(AnimationSetsPath);
            var header = SplitCsvLine(lines[0].TrimStart('﻿'));
            var rows = new List<Dictionary<string, string>>();
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                {
                    continue;
                }

                var cells = SplitCsvLine(lines[i]);
                var row = new Dictionary<string, string>();
                for (var c = 0; c < header.Count && c < cells.Count; c++)
                {
                    row[header[c]] = cells[c];
                }

                if (row.TryGetValue("enabled", out var enabled) &&
                    string.Equals(enabled.Trim(), "true", System.StringComparison.OrdinalIgnoreCase))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        private static List<string> SplitCsvLine(string line)
        {
            var cells = new List<string>();
            var current = new System.Text.StringBuilder();
            var quoted = false;
            foreach (var character in line)
            {
                if (character == '"')
                {
                    quoted = !quoted;
                }
                else if (character == ',' && !quoted)
                {
                    cells.Add(current.ToString().Trim());
                    current.Clear();
                }
                else
                {
                    current.Append(character);
                }
            }

            cells.Add(current.ToString().Trim());
            return cells;
        }
    }
}
