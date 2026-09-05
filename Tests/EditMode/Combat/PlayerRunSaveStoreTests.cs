using System.IO;
using NUnit.Framework;
using SeoulPlayup.Combat.Runtime;
using SeoulPlayup.Combat.Unity;
using UnityEngine;

namespace SeoulPlayup.Combat.Tests.EditMode
{
    public sealed class PlayerRunSaveStoreTests
    {
        private string tempDirectory;
        private PlayerRunSaveStore store;

        [SetUp]
        public void SetUp()
        {
            tempDirectory = Path.Combine(Path.GetTempPath(), "seoul-playup-save-tests", Path.GetRandomFileName());
            store = new PlayerRunSaveStore(tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }

        private static PlayerRunSaveEnvelope CreateEnvelope(string stageId = "stage-1", int hp = 42, int turn = 3)
        {
            var player = new PlayerRunSaveData
            {
                Vitals = new PlayerVitalsSaveData { Hp = hp, MaxHp = 80 }
            };
            return PlayerRunSaveEnvelope.Create(stageId, turn, player);
        }

        [Test]
        public void SaveThenLoadRoundTripsEnvelope()
        {
            Assert.That(store.HasSave, Is.False);
            Assert.That(store.TrySave(CreateEnvelope(hp: 55, turn: 9), out var saveReason), Is.True, saveReason);
            Assert.That(store.HasSave, Is.True);

            Assert.That(store.TryLoad(out var loaded, out var loadReason), Is.True, loadReason);
            Assert.That(loaded.StageId, Is.EqualTo("stage-1"));
            Assert.That(loaded.OverallTurn, Is.EqualTo(9));
            Assert.That(loaded.Player.Vitals.Hp, Is.EqualTo(55));
        }

        [Test]
        public void SaveOverwritesPreviousSlot()
        {
            Assert.That(store.TrySave(CreateEnvelope(hp: 10, turn: 1), out _), Is.True);
            Assert.That(store.TrySave(CreateEnvelope(hp: 20, turn: 2), out _), Is.True);

            Assert.That(store.TryLoad(out var loaded, out _), Is.True);
            Assert.That(loaded.Player.Vitals.Hp, Is.EqualTo(20));
            Assert.That(loaded.OverallTurn, Is.EqualTo(2));
            Assert.That(File.Exists(store.FilePath + ".tmp"), Is.False, "temp file must not linger after save");
        }

        [Test]
        public void LoadMissingFileFailsWithoutThrowing()
        {
            Assert.That(store.TryLoad(out var loaded, out var reason), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(reason, Does.Contain("No save file"));
        }

        [Test]
        public void LoadCorruptFileFailsAndKeepsFileForInspection()
        {
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(store.FilePath, "{ not valid json !!");

            Assert.That(store.TryLoad(out var loaded, out var reason), Is.False);
            Assert.That(loaded, Is.Null);
            Assert.That(reason, Is.Not.Empty);
            Assert.That(File.Exists(store.FilePath), Is.True);
        }

        [Test]
        public void LoadUnsupportedSchemaVersionFails()
        {
            var stale = CreateEnvelope();
            stale.SchemaVersion = PlayerRunSaveEnvelope.CurrentSchemaVersion + 1;
            Directory.CreateDirectory(tempDirectory);
            File.WriteAllText(store.FilePath, JsonUtility.ToJson(stale));

            Assert.That(store.TryLoad(out _, out var reason), Is.False);
            Assert.That(reason, Does.Contain("schema version"));
        }

        [Test]
        public void TrySaveRejectsNullAndInvalidEnvelopes()
        {
            Assert.That(store.TrySave(null, out var nullReason), Is.False);
            Assert.That(nullReason, Is.Not.Empty);

            var invalid = new PlayerRunSaveEnvelope { StageId = " " };
            Assert.That(store.TrySave(invalid, out var invalidReason), Is.False);
            Assert.That(invalidReason, Is.Not.Empty);
            Assert.That(store.HasSave, Is.False);
        }

        [Test]
        public void DeleteRemovesSaveAndIsIdempotent()
        {
            Assert.That(store.TrySave(CreateEnvelope(), out _), Is.True);
            store.Delete();
            Assert.That(store.HasSave, Is.False);

            store.Delete();
            Assert.That(store.HasSave, Is.False);
        }
    }
}
