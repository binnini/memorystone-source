using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatTimingProfileHitStopTests
    {
        [Test]
        public void HitStopResolversReturnZeroWhenDisabled()
        {
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            try
            {
                SetProfileValues(
                    profile,
                    enableHitStop: false,
                    playerSeconds: 0.07f,
                    monsterSeconds: 0.08f,
                    lethalSeconds: 0.12f);

                Assert.That(profile.ResolvePlayerAttackHitStopSeconds(lethal: false), Is.EqualTo(0f));
                Assert.That(profile.ResolvePlayerAttackHitStopSeconds(lethal: true), Is.EqualTo(0f));
                Assert.That(profile.ResolveMonsterAttackHitStopSeconds(lethal: false), Is.EqualTo(0f));
                Assert.That(profile.ResolveMonsterAttackHitStopSeconds(lethal: true), Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void HitStopResolversUsePerAttackAndLethalDurationsWhenEnabled()
        {
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            try
            {
                SetProfileValues(
                    profile,
                    enableHitStop: true,
                    playerSeconds: 0.07f,
                    monsterSeconds: 0.08f,
                    lethalSeconds: 0.12f);

                Assert.That(profile.ResolvePlayerAttackHitStopSeconds(lethal: false), Is.EqualTo(0.07f).Within(0.0001f));
                Assert.That(profile.ResolveMonsterAttackHitStopSeconds(lethal: false), Is.EqualTo(0.08f).Within(0.0001f));
                Assert.That(profile.ResolvePlayerAttackHitStopSeconds(lethal: true), Is.EqualTo(0.12f).Within(0.0001f));
                Assert.That(profile.ResolveMonsterAttackHitStopSeconds(lethal: true), Is.EqualTo(0.12f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void LethalHitStopFallsBackToMatchingAttackDurationWhenZero()
        {
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            try
            {
                SetProfileValues(
                    profile,
                    enableHitStop: true,
                    playerSeconds: 0.07f,
                    monsterSeconds: 0.08f,
                    lethalSeconds: 0f);

                Assert.That(profile.ResolvePlayerAttackHitStopSeconds(lethal: true), Is.EqualTo(0.07f).Within(0.0001f));
                Assert.That(profile.ResolveMonsterAttackHitStopSeconds(lethal: true), Is.EqualTo(0.08f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        private static void SetProfileValues(
            CombatTimingProfile profile,
            bool enableHitStop,
            float playerSeconds,
            float monsterSeconds,
            float lethalSeconds)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("enableHitStop").boolValue = enableHitStop;
            serialized.FindProperty("playerAttackHitStopSeconds").floatValue = playerSeconds;
            serialized.FindProperty("monsterAttackHitStopSeconds").floatValue = monsterSeconds;
            serialized.FindProperty("lethalHitStopSeconds").floatValue = lethalSeconds;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

