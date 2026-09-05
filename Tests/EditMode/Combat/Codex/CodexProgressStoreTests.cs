using System.IO;
using NUnit.Framework;
using SeoulPlayup.Codex;
using SeoulPlayup.Combat.Runtime;

namespace SeoulPlayup.Combat.Tests.EditMode.Codex
{
    /// <summary>
    /// 도감 진행도 저장소의 왕복(<c>docs/codex-plan.md</c> P3 완료 조건 5).
    /// <para>
    /// 🔑<b>이 저장소가 지켜야 하는 것은 "껐다 켜도 남는다" 하나</b>다. 그래서 테스트는 인스턴스를
    /// 재사용하지 않고 <b>매번 새 저장소를 같은 디렉터리에 세운다</b> — 같은 객체를 다시 물으면
    /// 메모리에 남은 집합을 읽을 뿐이라 디스크를 한 번도 지나지 않는다.
    /// </para>
    /// </summary>
    public sealed class CodexProgressStoreTests
    {
        private string directory;

        [SetUp]
        public void CreateScratchDirectory()
        {
            directory = Path.Combine(Path.GetTempPath(), "codex-progress-tests-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void RemoveScratchDirectory()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        private CodexProgressStore NewStore()
        {
            var store = new CodexProgressStore(directory);
            store.Load();
            return store;
        }

        [Test]
        public void FreshStore_HasNothingUnlockedAndDoesNotThrow()
        {
            var store = NewStore();

            Assert.That(store.HasSave, Is.False, "첫 실행에 파일이 없는 것은 정상이지 오류가 아니다.");
            Assert.That(store.Progress.TotalCount, Is.Zero);
            Assert.That(store.Progress.IsDirty, Is.False);
        }

        [Test]
        public void SavedProgress_SurvivesAnEntirelyNewStore()
        {
            var first = NewStore();
            first.Progress.MarkSeen(CodexDomainIds.Card, "C001");
            first.Progress.MarkSeen(CodexDomainIds.Card, "C002");
            first.Progress.MarkSeen(CodexDomainIds.Monster, "M001");
            Assert.That(first.Save(), Is.True);

            // 게임을 껐다 켠 것과 같은 일 — 새 객체가 디스크만 보고 같은 집합을 복원해야 한다.
            var second = NewStore();

            Assert.That(second.Progress.TotalCount, Is.EqualTo(3));
            Assert.That(second.Progress.IsUnlocked(CodexDomainIds.Card, "C001"), Is.True);
            Assert.That(second.Progress.IsUnlocked(CodexDomainIds.Card, "C002"), Is.True);
            Assert.That(second.Progress.IsUnlocked(CodexDomainIds.Monster, "M001"), Is.True);
            Assert.That(second.Progress.CountFor(CodexDomainIds.Card), Is.EqualTo(2));
            Assert.That(second.Progress.CountFor(CodexDomainIds.Relic), Is.Zero);
        }

        [Test]
        public void DomainsDoNotBleedIntoEachOther()
        {
            var first = NewStore();
            first.Progress.MarkSeen(CodexDomainIds.Card, "shared-id");
            first.Save();

            var second = NewStore();

            Assert.That(second.Progress.IsUnlocked(CodexDomainIds.Card, "shared-id"), Is.True);
            Assert.That(
                second.Progress.IsUnlocked(CodexDomainIds.Relic, "shared-id"), Is.False,
                "도메인마다 id 체계가 달라 같은 문자열이 겹칠 수 있다 — 집합이 하나면 유물이 카드로 열린다.");
        }

        [Test]
        public void DirtyFlag_TracksUnsavedChangesOnly()
        {
            var store = NewStore();
            Assert.That(store.Progress.IsDirty, Is.False);

            store.Progress.MarkSeen(CodexDomainIds.Trap, "trap_spike");
            Assert.That(store.Progress.IsDirty, Is.True);

            Assert.That(store.SaveIfDirty(), Is.True);
            Assert.That(store.Progress.IsDirty, Is.False);

            // 🔴이미 열린 것을 다시 표시해도 더러워지면 안 된다 — 훑기가 페이즈마다 도는데
            // 그때마다 파일을 쓰게 된다.
            store.Progress.MarkSeen(CodexDomainIds.Trap, "trap_spike");
            Assert.That(store.Progress.IsDirty, Is.False);
            Assert.That(store.SaveIfDirty(), Is.False, "쓸 것이 없으면 쓰지 않는다.");
        }

        [Test]
        public void EmptyAndWhitespaceIds_AreIgnored()
        {
            var store = NewStore();

            store.Progress.MarkSeen(CodexDomainIds.Trap, string.Empty);
            store.Progress.MarkSeen(CodexDomainIds.Trap, "   ");
            store.Progress.MarkSeen(string.Empty, "trap_spike");
            store.Progress.MarkSeen(null, null);

            Assert.That(
                store.Progress.TotalCount, Is.Zero,
                "프리셋 없는 런타임 함정이 빈 presetId로 들어온다 — 그것이 항목 하나로 서면 안 된다.");
            Assert.That(store.Progress.IsDirty, Is.False);
        }

        [Test]
        public void CorruptFile_LeavesTheGamePlayable()
        {
            File.WriteAllText(Path.Combine(directory, CodexProgressStore.DefaultFileName), "{ this is not json");

            CodexProgressStore store = null;
            Assert.DoesNotThrow(() => store = NewStore(), "🔴도감 진행도가 런을 죽이면 안 된다.");
            Assert.That(store.Progress.TotalCount, Is.Zero);

            // 깨진 파일 위에도 다시 쓸 수 있어야 한다(원자적 교체 경로).
            store.Progress.MarkSeen(CodexDomainIds.Card, "C001");
            Assert.That(store.Save(), Is.True);
            Assert.That(NewStore().Progress.IsUnlocked(CodexDomainIds.Card, "C001"), Is.True);
        }

        [Test]
        public void LoadReplacesRatherThanMerges()
        {
            var first = NewStore();
            first.Progress.MarkSeen(CodexDomainIds.Card, "C001");
            first.Save();

            var second = NewStore();
            second.Progress.MarkSeen(CodexDomainIds.Card, "C999");
            // 저장하지 않고 다시 읽으면 디스크가 이긴다 — 로드는 "이 파일이 진행도의 전부"라는 뜻이다.
            second.Load();

            Assert.That(second.Progress.IsUnlocked(CodexDomainIds.Card, "C001"), Is.True);
            Assert.That(second.Progress.IsUnlocked(CodexDomainIds.Card, "C999"), Is.False);
            Assert.That(second.Progress.IsDirty, Is.False);
        }
    }
}
