using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatRulesTests
    {
        [Test]
        public void DefaultCombatBalanceUsesPlayerAndThreeEyeDogBaseline()
        {
            Assert.That(CombatConfig.Default.PlayerMaxHp, Is.EqualTo(80));
            Assert.That(CombatConfig.Default.EnemyMaxHp, Is.EqualTo(30));
            Assert.That(CombatConfig.Default.EnemyAttackDamage, Is.EqualTo(5));
        }

        [Test]
        public void BlockMitigatesDamageBeforeHpAndCanReset()
        {
            var player = new CombatantState("player", 20);
            player.AddBlock(4);

            player.ApplyDamage(3);
            Assert.That(player.Hp, Is.EqualTo(20));
            Assert.That(player.Block, Is.EqualTo(1));

            player.ApplyDamage(5);
            Assert.That(player.Hp, Is.EqualTo(16));
            Assert.That(player.Block, Is.EqualTo(0));

            player.AddBlock(2);
            player.ClearBlock();
            Assert.That(player.Block, Is.EqualTo(0));
        }

        [Test]
        public void DamageFloorsHpAtZeroAndMarksDead()
        {
            var enemy = new CombatantState("enemy", 5);
            enemy.ApplyDamage(99);
            Assert.That(enemy.Hp, Is.EqualTo(0));
            Assert.That(enemy.IsDead, Is.True);
        }
    }
}

