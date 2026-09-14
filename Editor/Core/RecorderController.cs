using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace HighQualityRecorder.Editor
{
    public enum RecordingState
    {
        Idle,
        Recording,
        Finishing
    }

    public static class RecorderController
    {
        private static RecordingState _state = RecordingState.Idle;
        private static FFmpegEncoderProcess _encoderProcess;
        private static FrameCaptureEngine _captureEngine;
        private static double _recordStartTime;
        private static int _prevCaptureFramerate = 0;

        private static string _tempVideoPath;
        private static string _tempAudioPath;
        private static string _finalOutputPath;
        private static RecorderConfig _activeConfig;

        public static RecordingState State => _state;
        public static bool IsRecording => _state == RecordingState.Recording;
        public static string LastRecordedFile { get; private set; }
        public static double ElapsedTime => IsRecording ? (EditorApplication.timeSinceStartup - _recordStartTime) : 0;
        public static int RecordedFrames => _captureEngine != null ? _captureEngine.RecordedFrames : 0;
        public static int DroppedFrames => _captureEngine != null ? _captureEngine.DroppedFrames : 0;

        public static event Action OnRecordingStarted;
        public static event Action<string> OnRecordingFinished;
        public static event Action<string> OnRecordingFailed;

        public static bool StartRecording(RecorderConfig config)
        {
            if (_state != RecordingState.Idle)
            {
                Debug.LogWarning("[HighQualityRecorder] Recording is already in progress.");
                return false;
            }

            string ffmpegExe = FFmpegResolver.ResolveFFmpegPath(config.customFFmpegPath);
            if (string.IsNullOrEmpty(ffmpegExe) || !File.Exists(ffmpegExe))
            {
                string msg = "FFmpeg executable not found! Please download it using the button in the Recorder Window.";
                EditorUtility.DisplayDialog("FFmpeg Not Found", msg, "OK");
                OnRecordingFailed?.Invoke(msg);
                return false;
            }

            _activeConfig = config;

            // Resolve target directory
            string outputDir = config.outputDirectory;
            if (string.IsNullOrEmpty(outputDir))
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                outputDir = Path.Combine(projectRoot, "Recordings");
            }
            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            // Create filenames
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string extension = config.GetContainerExtension();
            string fileName = $"{config.fileNamePrefix}_{timestamp}{extension}";
            _finalOutputPath = Path.Combine(outputDir, fileName);

            string tempDir = Path.Combine(outputDir, ".temp");
            if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
            _tempVideoPath = Path.Combine(tempDir, $"temp_vid_{Guid.NewGuid():N}.mp4");
            _tempAudioPath = Path.Combine(tempDir, $"temp_aud_{Guid.NewGuid():N}.wav");

            // Calculate resolution
            Vector2 gameViewSize = GetGameViewSize();
            (int width, int height) = config.GetTargetResolution((int)gameViewSize.x, (int)gameViewSize.y);
            int fps = config.GetTargetFramerate();

            Debug.Log($"[HighQualityRecorder] Starting recording: {width}x{height} @ {fps}fps. TimingMode: {config.timingMode}");

            // 1. Initialize FFmpeg video encoder
            _encoderProcess = new FFmpegEncoderProcess();
            if (!_encoderProcess.Start(ffmpegExe, config, width, height, fps, _tempVideoPath))
            {
                _encoderProcess.Dispose();
                _encoderProcess = null;
                OnRecordingFailed?.Invoke("Failed to launch FFmpeg encoder.");
                return false;
            }

            // 2. Setup Audio capture if enabled and in Play mode
            if (config.captureAudio && Application.isPlaying)
            {
                var audioListener = AudioCaptureListener.EnsureInstance();
                if (audioListener != null)
                {
                    audioListener.StartCapture(_tempAudioPath);
                }
            }

            // 3. Setup Constant Framerate mode if requested
            if (config.timingMode == CaptureTimingMode.ConstantFramerate)
            {
                _prevCaptureFramerate = Time.captureFramerate;
                Time.captureFramerate = fps;
            }

            // 4. Start Frame Capture Engine with precise timing synchronization
            _captureEngine = new FrameCaptureEngine(_encoderProcess, width, height, fps, config.timingMode);
            _captureEngine.Start();

            _recordStartTime = EditorApplication.timeSinceStartup;
            _state = RecordingState.Recording;

            OnRecordingStarted?.Invoke();
            return true;
        }

        public static void StopRecording()
        {
            if (_state != RecordingState.Recording) return;
            _state = RecordingState.Finishing;

            Debug.Log("[HighQualityRecorder] Stopping recording asynchronously. Editor will remain responsive!");

            // 1. Immediately unhook render callbacks to stop capturing new frames on main thread
            var captureEngine = _captureEngine;
            _captureEngine = null;

            if (captureEngine != null)
            {
                captureEngine.UnhookRenderCallbacks();
            }

            // 2. Restore framerate mode immediately on main thread
            if (_activeConfig != null && _activeConfig.timingMode == CaptureTimingMode.ConstantFramerate)
            {
                Time.captureFramerate = _prevCaptureFramerate;
            }

            // 3. Stop audio capture on main thread
            if (AudioCaptureListener.Instance != null && AudioCaptureListener.Instance.IsCapturing)
            {
                AudioCaptureListener.Instance.StopCapture();
            }

            var encoder = _encoderProcess;
            _encoderProcess = null;

            string tempVid = _tempVideoPath;
            string tempAud = _tempAudioPath;
            string finalOut = _finalOutputPath;
            var config = _activeConfig;

            // 4. Run heavy finalizing operations (flushing queues, waiting for FFmpeg, Remuxing) in background thread!
            Task.Run(() =>
            {
                try
                {
                    // Flush remaining frames and stop worker
                    if (captureEngine != null)
                    {
                        captureEngine.StopAndFlushWorker();
                        captureEngine.Dispose();
                    }

                    // Flush and close FFmpeg video encoder
                    if (encoder != null)
                    {
                        encoder.CloseInputAndStop(10000);
                        encoder.Dispose();
                    }

                    // Remux Video and Audio
                    string ffmpegExe = FFmpegResolver.ResolveFFmpegPath(config?.customFFmpegPath ?? "");
                    bool success = false;

                    if (File.Exists(tempVid))
                    {
                        bool hasAudio = File.Exists(tempAud) && new FileInfo(tempAud).Length > 100;
                        if (hasAudio)
                        {
                            success = FFmpegEncoderProcess.RemuxVideoAndAudio(
                                ffmpegExe,
                                tempVid,
                                tempAud,
                                finalOut,
                                config?.audioBitrateKbps ?? 320
                            );
                        }
                        else
                        {
                            // If no audio was recorded, move temp to final directly (instantaneous, 0 disk copy)
                            if (File.Exists(finalOut)) File.Delete(finalOut);
                            File.Move(tempVid, finalOut);
                            success = true;
                        }
                    }

                    // Clean up temporary files
                    try
                    {
                        if (File.Exists(tempVid)) File.Delete(tempVid);
                        if (File.Exists(tempAud)) File.Delete(tempAud);
                        string tempDir = Path.GetDirectoryName(tempVid);
                        if (Directory.Exists(tempDir) && Directory.GetFiles(tempDir).Length == 0)
                        {
                            Directory.Delete(tempDir);
                        }
                    }
                    catch { }

                    // Post results safely back to main thread
                    EditorApplication.delayCall += () =>
                    {
                        _state = RecordingState.Idle;
                        if (success && File.Exists(finalOut))
                        {
                            LastRecordedFile = finalOut;
                            Debug.Log($"[HighQualityRecorder] Recording saved successfully to: {finalOut}");
                            OnRecordingFinished?.Invoke(finalOut);
                        }
                        else
                        {
                            Debug.LogError("[HighQualityRecorder] Failed to generate final output video file.");
                            OnRecordingFailed?.Invoke("Failed to generate final video file.");
                        }
                    };
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[HighQualityRecorder] Exception during background finalize: {ex.Message}");
                    EditorApplication.delayCall += () =>
                    {
                        _state = RecordingState.Idle;
                        OnRecordingFailed?.Invoke(ex.Message);
                    };
                }
            });
        }

        public static Vector2 GetGameViewSize()
        {
            // Try modern Unity 2021+ PlayModeView API
            Type playModeViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.PlayModeView");
            if (playModeViewType != null)
            {
                MethodInfo getTargetSizeMethod = playModeViewType.GetMethod(
                    "GetMainPlayModeViewTargetSize",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public
                );
                if (getTargetSizeMethod != null)
                {
                    object result = getTargetSizeMethod.Invoke(null, null);
                    if (result is Vector2 vec && vec.x > 0 && vec.y > 0)
                    {
                        return vec;
                    }
                }
            }

            // Fallback: Reflection on GameView Window
            Type gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType != null)
            {
                EditorWindow gameView = EditorWindow.GetWindow(gameViewType, false, null, false);
                if (gameView != null)
                {
                    PropertyInfo prop = gameViewType.GetProperty("targetSize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (prop != null)
                    {
                        object val = prop.GetValue(gameView);
                        if (val is Vector2 size && size.x > 0 && size.y > 0)
                        {
                            return size;
                        }
                    }
                    return new Vector2(gameView.position.width, gameView.position.height);
                }
            }

            // Last resort fallback
            return new Vector2(Screen.width > 0 ? Screen.width : 1920, Screen.height > 0 ? Screen.height : 1080);
        }
    }
}
