using System;
using System.IO;
using UnityEngine;

namespace SeoulPlayup.Codex
{
    /// <summary>
    /// <see cref="CodexProgress"/>의 디스크 저장소. 기존 두 저장소
    /// (<c>PlayerRunSaveStore</c> · <c>CombatSuspendStore</c>)의 관례를 그대로 따른다 —
    /// <c>Application.persistentDataPath</c> + <c>JsonUtility</c> + <b>임시 파일에 쓰고 옮기기</b>
    /// (쓰다 죽어도 원본이 안 깨진다).
    /// <para>
    /// 🔴<b>실패는 삼킨다.</b> 도감 진행도는 있으면 좋은 것이지 런을 막을 것이 아니다 — 저장이
    /// 안 되면 경고만 남기고 게임은 그대로 간다. 기존 두 저장소는 이유 문자열을 호출자에게
    /// 돌려주고 로깅을 맡기는데, 여기서는 호출자가 할 수 있는 일이 없으므로 이 층에서 끝낸다.
    /// </para>
    /// <para>
    /// 🔑<b>과정에서 진행도가 하나뿐이라는 사실</b>이 <see cref="Shared"/>의 근거다. 전투(획득 신호)와
    /// 로비(도감 화면)가 서로를 모른 채 같은 집합을 봐야 하는데, 둘 사이에 놓인 씬 전환이
    /// 참조를 들고 다닐 방법을 주지 않는다. 에셋 참조가 아니므로 빌드에서 <c>null</c>이 되는
    /// <c>Resources.Load</c> 함정과는 무관하다.
    /// </para>
    /// </summary>
    public sealed class CodexProgressStore
    {
        public const string DefaultFileName = "codex-progress.json";

        private static CodexProgressStore shared;

        private readonly string filePath;

        public CodexProgressStore() : this(Application.persistentDataPath) { }

        public CodexProgressStore(string directory, string fileName = DefaultFileName)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ArgumentException("Directory is required.", nameof(directory));
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new ArgumentException("File name is required.", nameof(fileName));
            }

            filePath = Path.Combine(directory, fileName);
            Progress = new CodexProgress();
        }

        /// <summary>
        /// 이 게임 실행에서 단 하나뿐인 진행도. 처음 닿는 순간 디스크에서 읽는다.
        /// </summary>
        public static CodexProgressStore Shared
        {
            get
            {
                if (shared == null)
                {
                    shared = new CodexProgressStore();
                    shared.Load();
                    // 마지막 그물. 전투 종료·로비 복귀에서 이미 쓰지만, 게임을 그 사이에서
                    // 끄는 것이 가장 흔한 종료 방식이다.
                    Application.quitting += () => shared?.SaveIfDirty();
                }

                return shared;
            }
        }

        public CodexProgress Progress { get; }

        public string FilePath => filePath;

        private string TempFilePath => filePath + ".tmp";

        public bool HasSave => File.Exists(filePath);

        /// <summary>
        /// 디스크에서 읽어 <see cref="Progress"/>를 덮어쓴다. 파일이 없으면 빈 진행도로 시작한다 —
        /// 첫 실행이 정상 상태이지 오류가 아니다.
        /// </summary>
        public void Load()
        {
            if (!File.Exists(filePath))
            {
                Progress.LoadFrom(null);
                return;
            }

            try
            {
                var parsed = JsonUtility.FromJson<CodexProgressSaveData>(File.ReadAllText(filePath));
                Progress.LoadFrom(parsed);
            }
            catch (Exception exception)
            {
                // 깨진 파일은 지우지 않고 둔다(들여다볼 수 있게). 다음 저장이 덮어쓴다.
                Debug.LogWarning($"[Codex] 진행도를 읽지 못해 빈 상태로 시작한다 ('{filePath}'): {exception.Message}");
                Progress.LoadFrom(null);
            }
        }

        /// <summary>
        /// 새로 열린 것이 있을 때만 쓴다. 신호마다 쓰면 페이즈마다 파일 입출력이 돈다 —
        /// 구간이 끝나는 자리(전투 종료 · 로비 복귀 · 앱 종료)에서 부를 것.
        /// </summary>
        public bool SaveIfDirty()
        {
            return Progress.IsDirty && Save();
        }

        public bool Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(TempFilePath, JsonUtility.ToJson(Progress.ToSaveData()));
                if (File.Exists(filePath))
                {
                    File.Replace(TempFilePath, filePath, destinationBackupFileName: null);
                }
                else
                {
                    File.Move(TempFilePath, filePath);
                }

                Progress.MarkSaved();
                return true;
            }
            catch (Exception exception)
            {
                // 🔴여기서 던지면 도감이 런을 죽인다. 다음 기회에 다시 쓰면 되므로 IsDirty를 지우지 않는다.
                Debug.LogWarning($"[Codex] 진행도를 저장하지 못했다 ('{filePath}'): {exception.Message}");
                return false;
            }
        }

        public void Delete()
        {
            TryDeleteFile(TempFilePath);
            TryDeleteFile(filePath);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Codex] 파일을 지우지 못했다 ('{path}'): {exception.Message}");
            }
        }
    }
}
