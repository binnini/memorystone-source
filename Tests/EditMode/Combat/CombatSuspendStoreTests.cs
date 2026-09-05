using System.IO;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class CombatSuspendStoreTests
    {
        private string tempDirectory;
        private CombatSuspendStore store;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "seoul-playup-suspend-tests", Path.GetRandomFileName());
            store = new CombatSuspendStore(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        private static CombatSuspendEnvelope CreateEnvelope(string stageId = "stage-1", int turn = 3, int hp = 42)
        {
            var combat = new CombatSuspendData
            {
                OverallTurn = turn,
                Phase = CombatPhase.PlayerMovement,
                ActionCostRemaining = 2
            };
            combat.Player.Vitals = new PlayerVitalsSaveData { Hp = hp, MaxHp = 80 };
            combat.Monsters.Add(new MonsterRuntimeSaveData { Id = "m1", DefinitionId = "M001", Hp = 10, MaxHp = 30 });
            return CombatSuspendEnvelope.Create(stageId, turn, combat);
        }

        [Test]
        public void SaveThenLoadRoundTripsEnvelope()
        {
            Assert.That(store.HasSave, Is.False);
            Assert.That(store.TrySave(CreateEnvelope(turn: 9, hp: 55), out var saveReason), Is.True, saveReason);
            Assert.That(store.HasSave, Is.True);

            Assert.That(store.TryLoad(out var loaded, out var loadReason), Is.True, loadReason);
            Assert.That(loaded.StageId, Is.EqualTo("stage-1"));
            Assert.That(loaded.OverallTurn, Is.EqualTo(9));
            Assert.That(loaded.Combat.Player.Vitals.Hp, Is.EqualTo(55));
            Assert.That(loaded.Combat.Monsters, Has.Count.EqualTo(1));
            Assert.That(loaded.Combat.Monsters[0].Hp, Is.EqualTo(10));
        }

        [Test]
        public void SaveOverwritesPreviousSlot()
        {
            Assert.That(store.TrySave(CreateEnvelope(turn: 1, hp: 10), out _), Is.True);
            Assert.That(store.TrySave(CreateEnvelope(turn: 2, hp: 20), out _), Is.True);

            Assert.That(store.TryLoad(out var loaded, out _), Is.True);
            Assert.That(loaded.OverallTurn, Is.EqualTo(2));
            Assert.That(loaded.Combat.Player.Vitals.Hp, Is.EqualTo(20));
        }

        [Test]
        public void LoadReturnsFalseWhenNoFileExists()
        {
            Assert.That(store.TryLoad(out var loaded, out var reason), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(reason, Does.Contain("No save"));
        }

        [Test]
        public void LoadRejectsCorruptFile()
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(store.FilePath, "{ this is not valid json ]");

            Assert.That(store.TryLoad(out var loaded, out var reason), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void LoadRejectsSchemaMismatch()
        {
            var envelope = CreateEnvelope();
            envelope.SchemaVersion = CombatSuspendEnvelope.CurrentSchemaVersion + 1;
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(store.FilePath, UnityEngine.JsonUtility.ToJson(envelope));

            Assert.That(store.TryLoad(out _, out var reason), Is.False);
            Assert.That(reason, Does.Contain("schema version"));
        }

        [Test]
        public void DeleteIsIdempotentAndRemovesSlot()
        {
            Assert.That(store.TrySave(CreateEnvelope(), out _), Is.True);
            Assert.That(store.HasSave, Is.True);

            store.Delete();
            Assert.That(store.HasSave, Is.False);

            Assert.DoesNotThrow(() => store.Delete());
            Assert.That(store.HasSave, Is.False);
        }
    }
}
