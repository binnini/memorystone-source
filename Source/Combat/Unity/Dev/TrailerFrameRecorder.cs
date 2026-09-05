using System.IO;
using Unity.Collections;
using UnityEngine;

namespace SeoulPlayup.Combat.Unity.Dev
{
    /// <summary>
    /// Dev-only deterministic frame capture for trailer takes: writes one PNG per frame while a take plays,
    /// plus (optionally) the matching audio mixdown as a sibling WAV, which an external tool (ffmpeg) then
    /// muxes into a video. See docs/trailer-capture-plan.md.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="Time.captureDeltaTime"/> rather than recording wall-clock frames. That pins every
    /// frame to an exact 1/fps step, so the take advances the same amount per captured frame no matter how
    /// long the editor actually spends rendering it — a slow frame stretches real time, never the footage.
    /// It also answers the question the plan flagged (does the intro's unscaled timing survive fixed-rate
    /// capture?) — measured answer: NO. captureDeltaTime pins Time.deltaTime to 1/fps but leaves
    /// Time.unscaledDeltaTime on the wall clock, so an unscaled-timed cinematic keeps running at realtime
    /// while frames are written far slower, and the footage comes out time-compressed (a 5.0s take recorded
    /// as 48 frames = 0.8s). Callers must therefore also move the cinematic onto the captured clock via
    /// MapCombatController.DebugSetCinematicCaptureTimeSource(true) for the duration of the take.
    ///
    /// The component must outlive a single script call (capture spans many frames), which is why it is a
    /// scene MonoBehaviour holding the state rather than a static helper.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class TrailerFrameRecorder : MonoBehaviour
    {
        [Tooltip("캡처한 PNG를 쓸 폴더. 비어 있으면 녹화 시작할 때 지정한 경로를 씁니다.")]
        [SerializeField] private string outputFolder = string.Empty;

        [Tooltip("초당 프레임. 캡처 중에는 이 값이 곧 게임 시간의 진행 단위가 됩니다.")]
        [SerializeField] private int frameRate = 60;

        [Tooltip("게임 뷰 해상도의 배수(1 = 그대로). 2로 올리면 4배 크기로 찍힙니다.")]
        [SerializeField] private int superSize = 1;

        [Tooltip("프레임과 같은 클럭으로 오디오 믹스다운을 받아 <프레임폴더>.wav 로 씁니다. ffmpeg에서 영상과 함께 먹싱하세요. 캡처 중에는 스피커 출력이 잠깁니다.")]
        [SerializeField] private bool captureAudio = true;

        private bool recording;
        private int frameIndex;
        private float previousCaptureDeltaTime;
        private System.Func<bool> autoStopTakePlayingProbe;
        private bool sawTakeStart;
        private int autoStopFrameLimit;

        private bool audioEngaged;
        private BinaryWriter wavWriter;
        private int wavChannels;
        private int wavSampleRate;
        private int wavFrameCount;

        public bool IsRecording => recording;
        public int FrameIndex => frameIndex;
        public string OutputFolder => outputFolder;

        /// <summary>Path of the WAV written alongside the frames, or empty when audio capture is off.</summary>
        public string AudioPath => string.IsNullOrWhiteSpace(outputFolder) || !captureAudio
            ? string.Empty
            : outputFolder.TrimEnd('/', '\\') + ".wav";

        /// <summary>True while the audio mixdown is actually being drained (Start() can refuse).</summary>
        public bool IsCapturingAudio => audioEngaged;

        /// <summary>Per-channel sample frames written so far — divide by the sample rate for wall seconds.</summary>
        public int AudioSampleFrames => wavFrameCount;

        /// <summary>
        /// Records until <paramref name="controller"/>'s cinematic ends, then stops itself on that exact
        /// frame. Polling from outside cannot do this: every probe costs a round trip, so the observed end
        /// lands tens of frames late and the trailing idle frames get muxed into the video (or a guessed
        /// trim length cuts the deceleration off). <paramref name="frameLimit"/> is a safety stop in case
        /// the take never starts.
        /// </summary>
        public void StartRecordingUntilTakeEnds(
            string folder, int fps, int size, MapCombatController controller, int frameLimit)
        {
            StartRecordingUntilProbeEnds(folder, fps, size, () => controller.IsStageIntroPlaying, frameLimit);
        }

        /// <summary>
        /// Same auto-stop, but for a <see cref="TrailerShotRunner"/> take (cuts #4-#5): those never set the
        /// controller's stage-intro flag, so the probe watches the runner's own routine instead. IsTakePlaying,
        /// not IsFilming — the filming state deliberately outlives the routine to hold the final pose.
        /// </summary>
        public void StartRecordingUntilTakeEnds(
            string folder, int fps, int size, TrailerShotRunner runner, int frameLimit)
        {
            StartRecordingUntilProbeEnds(folder, fps, size, () => runner.IsTakePlaying, frameLimit);
        }

        /// <summary>
        /// Same auto-stop, for the memory-stone victory cinematic. That sequence runs on
        /// <see cref="MapCombatController"/>'s own coroutine and sets neither the stage-intro flag nor a
        /// runner take, so neither existing probe ever sees it start.
        /// </summary>
        /// <remarks>
        /// The flag stays raised through the final hold — the result screen is the last beat of the shot,
        /// not the thing that follows it — and drops in the same frame the teardown hands the camera back,
        /// which the pre-capture stop decision above is exactly what keeps out of the footage.
        /// </remarks>
        public void StartRecordingUntilVictoryPresentationEnds(
            string folder, int fps, int size, MapCombatController controller, int frameLimit)
        {
            StartRecordingUntilProbeEnds(
                folder, fps, size, () => controller.MemoryStoneVictoryVirtualCameraActive, frameLimit);
        }

        /// <summary>
        /// Records one combat presentation sequence — a monster phase, a player attack — and stops on the
        /// frame it ends. Added for the camera lab (docs/monster-action-camera-focus-plan.md P4): camera
        /// framing has to be judged in motion, and a still cannot show a pan.
        ///
        /// Probing <see cref="MapCombatController.IsSequencePlaying"/> rather than timing it from outside is
        /// the same lesson the intro takes learned: a round-trip-polled stop lands tens of frames late and
        /// welds a frozen tail onto the clip.
        /// </summary>
        public void StartRecordingUntilPresentationEnds(
            string folder, int fps, int size, MapCombatController controller, int frameLimit)
        {
            StartRecordingUntilProbeEnds(
                folder, fps, size, () => controller.IsSequencePlaying, frameLimit);
        }

        private void StartRecordingUntilProbeEnds(
            string folder, int fps, int size, System.Func<bool> takePlayingProbe, int frameLimit)
        {
            autoStopTakePlayingProbe = takePlayingProbe;
            sawTakeStart = false;
            autoStopFrameLimit = Mathf.Max(1, frameLimit);
            StartRecording(folder, fps, size);
        }

        public void StartRecording(string folder, int fps, int size)
        {
            if (recording)
            {
                StopRecording();
            }

            outputFolder = string.IsNullOrWhiteSpace(folder) ? outputFolder : folder;
            frameRate = Mathf.Max(1, fps);
            superSize = Mathf.Max(1, size);

            if (string.IsNullOrWhiteSpace(outputFolder))
            {
                Debug.LogWarning("[TrailerFrameRecorder] no output folder; not recording.");
                return;
            }

            Directory.CreateDirectory(outputFolder);
            foreach (var stale in Directory.GetFiles(outputFolder, "frame_*.png"))
            {
                File.Delete(stale);
            }

            frameIndex = 0;
            previousCaptureDeltaTime = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / frameRate;
            recording = true;
            BeginAudioCapture();
        }

        public void StopRecording()
        {
            if (!recording)
            {
                return;
            }

            recording = false;
            autoStopTakePlayingProbe = null;
            sawTakeStart = false;
            // Restore rather than zeroing blindly: something else may have been driving capture rate.
            Time.captureDeltaTime = previousCaptureDeltaTime;
            EndAudioCapture();
        }

        // --- Audio ------------------------------------------------------------------------------------
        // AudioRenderer hands over exactly one captured frame's worth of the final mixdown per call, on the
        // same captureDeltaTime clock the PNGs are written on. That is what keeps a 20-second take's audio
        // the same 20 seconds as its video even though the editor spent minutes of wall time rendering it —
        // sampling the live output instead would stretch to wall time and desync by orders of magnitude.
        private void BeginAudioCapture()
        {
            if (!captureAudio)
            {
                return;
            }

            var path = AudioPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            wavChannels = ResolveChannelCount(AudioSettings.speakerMode);
            wavSampleRate = AudioSettings.outputSampleRate;
            wavFrameCount = 0;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                wavWriter = new BinaryWriter(new FileStream(path, FileMode.Create, FileAccess.Write));
                WriteWavHeaderPlaceholder();
            }
            catch (IOException e)
            {
                Debug.LogWarning($"[TrailerFrameRecorder] audio file '{path}' not writable ({e.Message}); recording video only.");
                wavWriter = null;
                return;
            }

            // Start() can refuse (another renderer already attached); video capture must still proceed.
            audioEngaged = AudioRenderer.Start();
            if (!audioEngaged)
            {
                Debug.LogWarning("[TrailerFrameRecorder] AudioRenderer.Start() refused; recording video only.");
                CloseWavWriter();
            }
        }

        private void CaptureAudioFrame()
        {
            if (!audioEngaged || wavWriter == null)
            {
                return;
            }

            // Render is called unconditionally, exactly as Unity's own capture sample does — including on
            // the first frame, where the count is still 0. Start() switches the audio system to offline
            // rendering, in which the DSP clock only advances when Render() pumps it; guarding the call
            // behind "count > 0" therefore deadlocks the two against each other and the whole take comes
            // back silent (measured: dspTime frozen for 1000+ frames, WAV left at its 44-byte header).
            var sampleFrames = AudioRenderer.GetSampleCountForCaptureFrame();
            var buffer = new NativeArray<float>(Mathf.Max(0, sampleFrames) * wavChannels, Allocator.Temp);
            try
            {
                AudioRenderer.Render(buffer);
                for (var i = 0; i < buffer.Length; i++)
                {
                    wavWriter.Write((short)Mathf.Clamp(buffer[i] * 32767f, -32768f, 32767f));
                }

                wavFrameCount += Mathf.Max(0, sampleFrames);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        private void EndAudioCapture()
        {
            if (audioEngaged)
            {
                audioEngaged = false;
                AudioRenderer.Stop();
            }

            if (wavWriter == null)
            {
                return;
            }

            // The header could not know the length up front; patch the two size fields now.
            var dataBytes = wavFrameCount * wavChannels * 2;
            wavWriter.Seek(4, SeekOrigin.Begin);
            wavWriter.Write(36 + dataBytes);
            wavWriter.Seek(40, SeekOrigin.Begin);
            wavWriter.Write(dataBytes);
            CloseWavWriter();
        }

        private void CloseWavWriter()
        {
            wavWriter?.Dispose();
            wavWriter = null;
        }

        private void WriteWavHeaderPlaceholder()
        {
            var blockAlign = (short)(wavChannels * 2);
            wavWriter.Write(new[] { 'R', 'I', 'F', 'F' });
            wavWriter.Write(0);                                   // patched in EndAudioCapture
            wavWriter.Write(new[] { 'W', 'A', 'V', 'E' });
            wavWriter.Write(new[] { 'f', 'm', 't', ' ' });
            wavWriter.Write(16);                                  // PCM fmt chunk size
            wavWriter.Write((short)1);                            // PCM
            wavWriter.Write((short)wavChannels);
            wavWriter.Write(wavSampleRate);
            wavWriter.Write(wavSampleRate * blockAlign);          // byte rate
            wavWriter.Write(blockAlign);
            wavWriter.Write((short)16);                           // bits per sample
            wavWriter.Write(new[] { 'd', 'a', 't', 'a' });
            wavWriter.Write(0);                                   // patched in EndAudioCapture
        }

        private static int ResolveChannelCount(AudioSpeakerMode mode)
        {
            switch (mode)
            {
                case AudioSpeakerMode.Mono: return 1;
                case AudioSpeakerMode.Quad: return 4;
                case AudioSpeakerMode.Surround: return 5;
                case AudioSpeakerMode.Mode5point1: return 6;
                case AudioSpeakerMode.Mode7point1: return 8;
                default: return 2;
            }
        }

        private void OnDisable()
        {
            // Leaving captureDeltaTime set would silently pin the editor's frame pacing after a take.
            StopRecording();
        }

        // LateUpdate, so the take's camera pose for this frame (applied from its coroutine) is already in.
        // ScreenCapture grabs the frame at end-of-render; with captureDeltaTime set, Unity holds the next
        // frame until the write completes, which is what keeps the sequence gap-free.
        private void LateUpdate()
        {
            if (!recording)
            {
                return;
            }

            // The stop decision runs BEFORE the capture: the take's teardown executes earlier in the same
            // frame it ends on, so by LateUpdate that frame already shows the restored HUD/fog/camera —
            // capturing it put one frame of UI at the tail of every intro-path video.
            if (autoStopTakePlayingProbe != null)
            {
                if (autoStopTakePlayingProbe())
                {
                    sawTakeStart = true;
                }
                else if (sawTakeStart || frameIndex >= autoStopFrameLimit)
                {
                    StopRecording();
                    return;
                }
            }

            // Audio first, and only on frames that also get a picture, so the two streams stay 1:1. The
            // early return above skips both together on the stop frame.
            CaptureAudioFrame();
            ScreenCapture.CaptureScreenshot(
                Path.Combine(outputFolder, $"frame_{frameIndex:D5}.png"), superSize);
            frameIndex++;
        }
    }
}
