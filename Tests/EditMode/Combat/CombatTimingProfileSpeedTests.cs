using NUnit.Framework;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatTimingProfileSpeedTests
    {
        [Test]
        public void ScaleDuration_DefaultSpeed_IsIdentity()
        {
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            try
            {
                Assert.That(profile.GlobalSpeedMultiplier, Is.EqualTo(1f).Within(1e-5f));
                Assert.That(profile.ScaleDuration(0.4f), Is.EqualTo(0.4f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ScaleDuration_FasterSpeed_ShortensWaits()
        {
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            try
            {
                profile.TunableGlobalSpeedMultiplier = 2f;
                Assert.That(profile.ScaleDuration(0.4f), Is.EqualTo(0.2f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void GlobalSpeedMultiplier_ClampedToSaneRange()
        {
            var profile = ScriptableObject.CreateInstance<CombatTimingProfile>();
            try
            {
                profile.TunableGlobalSpeedMultiplier = 100f;
                Assert.That(profile.GlobalSpeedMultiplier, Is.EqualTo(8f).Within(1e-5f));
                profile.TunableGlobalSpeedMultiplier = 0f;
                Assert.That(profile.GlobalSpeedMultiplier, Is.EqualTo(0.05f).Within(1e-5f));
            }
            finally
            {
                Object.DestroyImmediate(profile);
            }
        }
    }
}
