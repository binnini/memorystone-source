using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class StatusEffectCatalogCsvTests
    {
        [Category("ShippingData")]
        [Test]
        public void ConvertsDesignerStatusEffectCsv()
        {
            var catalog = StatusEffectCatalogCsv.ConvertFile(CombatCsvPaths.StatusEffectsCsv);

            Assert.That(catalog.Entries.Select(entry => entry.Kind), Is.EquivalentTo(new[]
            {
                StatusEffectKind.Immobilize,
                StatusEffectKind.Poison,
                StatusEffectKind.Stun,
                StatusEffectKind.Slow,
                StatusEffectKind.Rupture,
                StatusEffectKind.Reflect,
                StatusEffectKind.Agility,
                StatusEffectKind.Strength,
                StatusEffectKind.Blind,
                StatusEffectKind.Disarm,
                StatusEffectKind.Weaken,
                StatusEffectKind.Vulnerable,
                StatusEffectKind.Seal,
                StatusEffectKind.TorchLight,
                StatusEffectKind.BossAura,
                StatusEffectKind.Might,
                StatusEffectKind.Unknown,
                StatusEffectKind.Guard,
                StatusEffectKind.Stealth,
                StatusEffectKind.Invincible,
            }));
            Assert.That(catalog.TryGet((StatusEffectKind)999, out _), Is.False);
            Assert.That(catalog.TryGet((StatusEffectKind)998, out _), Is.False);

            Assert.That(catalog.TryGet(StatusEffectKind.Rupture, out var rupture), Is.True);
            Assert.That(rupture.DisplayNameKo, Is.EqualTo("\uD30C\uC5F4"));
            Assert.That(rupture.DefaultAmount, Is.EqualTo(2));
            Assert.That(rupture.ValueMode, Is.EqualTo(StatusEffectValueMode.BlockGainPenalty));
            Assert.That(rupture.Timing, Is.EqualTo(StatusEffectTiming.OnBlockGain));

            Assert.That(catalog.TryGet(StatusEffectKind.Agility, out var agility), Is.True);
            Assert.That(agility.DisplayNameKo, Is.EqualTo("\uBBFC\uCCA9"));
            Assert.That(agility.DefaultAmount, Is.EqualTo(2));
            Assert.That(agility.ValueMode, Is.EqualTo(StatusEffectValueMode.MoveRangeBonus));
            Assert.That(agility.ExpirePolicy, Is.EqualTo(StatusEffectExpirePolicy.TurnEnd), "P1.5 실측: 민첩은 이동 후 소멸하지 않는다 — 지속시간으로만 사라진다.");

            Assert.That(catalog.TryGet(StatusEffectKind.Strength, out var strength), Is.True);
            Assert.That(strength.DisplayNameKo, Is.EqualTo("강화"));
            Assert.That(strength.DefaultAmount, Is.EqualTo(100));
            Assert.That(strength.ValueMode, Is.EqualTo(StatusEffectValueMode.DamageDealtBonusPercent));
            Assert.That(strength.Timing, Is.EqualTo(StatusEffectTiming.OnOutgoingDamage));

            Assert.That(catalog.TryGet(StatusEffectKind.Blind, out var blind), Is.True);
            Assert.That(blind.DisplayNameKo, Is.EqualTo("실명"));
            Assert.That(blind.DefaultAmount, Is.EqualTo(1), "D-7: 실명은 시야 −1이 기본이다.");
            Assert.That(blind.ValueMode, Is.EqualTo(StatusEffectValueMode.VisionRangePenalty));
            Assert.That(blind.Timing, Is.EqualTo(StatusEffectTiming.BeforeVisionCalc));

            Assert.That(catalog.TryGet(StatusEffectKind.Disarm, out var disarm), Is.True);
            Assert.That(disarm.DisplayNameKo, Is.EqualTo("무장 해제"));
            Assert.That(disarm.ValueMode, Is.EqualTo(StatusEffectValueMode.None), "존재형이다 — Amount를 읽지 않는다.");
            Assert.That(disarm.Timing, Is.EqualTo(StatusEffectTiming.OnActionCheck));

            Assert.That(catalog.TryGet(StatusEffectKind.Weaken, out var weaken), Is.True);
            Assert.That(weaken.DisplayNameKo, Is.EqualTo("쇠약"));
            Assert.That(weaken.DefaultAmount, Is.EqualTo(30), "O-10 확정: 나가는 피해 −30%.");
            Assert.That(weaken.ValueMode, Is.EqualTo(StatusEffectValueMode.DamageDealtPenaltyPercent));
            Assert.That(weaken.Timing, Is.EqualTo(StatusEffectTiming.OnOutgoingDamage));

            Assert.That(catalog.TryGet(StatusEffectKind.Vulnerable, out var vulnerable), Is.True);
            Assert.That(vulnerable.DisplayNameKo, Is.EqualTo("허점"));
            Assert.That(vulnerable.DefaultAmount, Is.EqualTo(2), "O-10 확정: 받는 타격당 +2 고정(배율 아님 — D-10).");
            Assert.That(vulnerable.ValueMode, Is.EqualTo(StatusEffectValueMode.IncomingDamageBonusFlat));
            Assert.That(vulnerable.Timing, Is.EqualTo(StatusEffectTiming.OnIncomingDamage));
        }

        [Category("ShippingData")]
        [Test]
        public void MissingRequiredColumnReportsDesignerFriendlyError()
        {
            var csv = File.ReadAllText(CombatCsvPaths.StatusEffectsCsv)
                .Replace("valueMode", "valueModeMissing");

            var ex = Assert.Throws<ArgumentException>(() => StatusEffectCatalogCsv.ConvertText(csv));
            Assert.That(ex.Message, Does.Contain("valueMode"));
            Assert.That(ex.Message, Does.Contain("missing required column"));
        }

        [Category("ShippingData")]
        [Test]
        public void UnknownEffectKindReportsDesignerFriendlyError()
        {
            var csv = File.ReadAllText(CombatCsvPaths.StatusEffectsCsv).Replace("Poison,\uC911\uB3C5", "Bleed,\uCD9C\uD608");

            var ex = Assert.Throws<ArgumentException>(() => StatusEffectCatalogCsv.ConvertText(csv));
            Assert.That(ex.Message, Does.Contain("Bleed"));
            Assert.That(ex.Message, Does.Contain("unknown value"));
        }

        [Category("ShippingData")]
        [Test]
        public void TextAssetSourceCanCreateStatusEffectCatalog()
        {
            var source = ScriptableObject.CreateInstance<CombatCatalogTextAssetSource>();
            var statusEffects = new TextAsset(File.ReadAllText(CombatCsvPaths.StatusEffectsCsv)) { name = "status_effects" };

            typeof(CombatCatalogTextAssetSource)
                .GetField("statusEffects", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .SetValue(source, statusEffects);

            var catalog = source.CreateStatusEffectCatalog();

            Assert.That(catalog.TryGet(StatusEffectKind.Slow, out var slow), Is.True);
            Assert.That(slow.DisplayNameKo, Is.EqualTo("\uB454\uD654"));
            UnityEngine.Object.DestroyImmediate(source);
            UnityEngine.Object.DestroyImmediate(statusEffects);
        }
    }
}



