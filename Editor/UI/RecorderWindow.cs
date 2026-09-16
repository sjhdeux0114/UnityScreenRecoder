using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HighQualityRecorder.Editor
{
    public class RecorderWindow : EditorWindow
    {
        private RecorderConfig _config;
        private Vector2 _scrollPos;
        private bool _isDownloadingFFmpeg = false;
        private float _downloadProgress = 0f;
        private string _downloadStatus = "";

        [MenuItem("Tools/High Quality Screen Recorder %#F9", false, 100)]
        [MenuItem("Window/Screen Recorder", false, 1000)]
        public static void Open()
        {
            var window = GetWindow<RecorderWindow>(false, "Screen Recorder", true);
            window.minSize = new Vector2(420, 580);
            EnsureValidPosition(window);
            window.Show();
            window.Focus();
        }

        [MenuItem("Tools/Reset Recorder Window Position", false, 102)]
        public static void ResetPosition()
        {
            var window = GetWindow<RecorderWindow>(false, "Screen Recorder", true);
            window.minSize = new Vector2(420, 580);

            Rect mainRect = GetMainWindowRect();
            float w = 440;
            float h = 620;
            float x = mainRect.width > 0 ? (mainRect.x + (mainRect.width - w) * 0.5f) : 150;
            float y = mainRect.height > 0 ? (mainRect.y + (mainRect.height - h) * 0.5f) : 150;
            window.position = new Rect(Mathf.Max(0, x), Mathf.Max(0, y), w, h);

            window.Show();
            window.Focus();
            Debug.Log("[HighQualityRecorder] Recorder Window position has been reset to center.");
        }

        private static Rect GetMainWindowRect()
        {
            try
            {
                return EditorGUIUtility.GetMainWindowPosition();
            }
            catch
            {
                return new Rect(0, 0, Screen.currentResolution.width, Screen.currentResolution.height);
            }
        }

        private static void EnsureValidPosition(EditorWindow window)
        {
            Rect pos = window.position;
            Rect mainRect = GetMainWindowRect();

            bool isInvalid = false;

            // 1. Invalid or collapsed size
            if (pos.width < 100 || pos.height < 100)
            {
                isInvalid = true;
            }

            // 2. Windows minimized coordinates (-32000) or extreme offscreen values
            if (pos.x < -10000 || pos.y < -10000 || pos.x > 30000 || pos.y > 30000)
            {
                isInvalid = true;
            }

            // 3. Completely outside the main editor window
            if (mainRect.width > 0 && mainRect.height > 0)
            {
                bool overlaps = (pos.x + pos.width > mainRect.x + 50) &&
                                (pos.x < mainRect.x + mainRect.width - 50) &&
                                (pos.y + pos.height > mainRect.y + 50) &&
                                (pos.y < mainRect.y + mainRect.height - 50);

                if (!overlaps)
                {
                    isInvalid = true;
                }
            }

            if (isInvalid)
            {
                float w = Mathf.Max(440, window.minSize.x);
                float h = Mathf.Max(620, window.minSize.y);
                float x = mainRect.width > 0 ? (mainRect.x + (mainRect.width - w) * 0.5f) : 150;
                float y = mainRect.height > 0 ? (mainRect.y + (mainRect.height - h) * 0.5f) : 150;
                window.position = new Rect(Mathf.Max(0, x), Mathf.Max(0, y), w, h);
            }
        }

        private void OnEnable()
        {
            _config = RecorderSettingsManager.GetSettings() ?? new RecorderConfig();
            EditorApplication.update += OnEditorUpdate;
            RecorderController.OnRecordingStarted += Repaint;
            RecorderController.OnRecordingFinished += OnRecordFinished;
            RecorderController.OnRecordingFailed += OnRecordFailed;
        }

        private void OnDisable()
        {
            SaveConfig();
            EditorApplication.update -= OnEditorUpdate;
            RecorderController.OnRecordingStarted -= Repaint;
            RecorderController.OnRecordingFinished -= OnRecordFinished;
            RecorderController.OnRecordingFailed -= OnRecordFailed;
        }

        private void OnEditorUpdate()
        {
            if (RecorderController.IsRecording)
            {
                Repaint();
            }
        }

        private void OnRecordFinished(string filePath)
        {
            Repaint();
        }

        private void OnRecordFailed(string error)
        {
            Repaint();
        }

        private void OnGUI()
        {
            if (_config == null)
            {
                _config = RecorderSettingsManager.GetSettings() ?? new RecorderConfig();
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space(6);

            DrawFFmpegStatusBox();
            EditorGUILayout.Space(6);

            DrawRecordingControlSection();
            EditorGUILayout.Space(6);

            EditorGUI.BeginDisabledGroup(RecorderController.IsRecording);
            
            // Track any change to automatically persist settings immediately
            EditorGUI.BeginChangeCheck();

            DrawEncoderSettingsBox();
            EditorGUILayout.Space(6);

            DrawResolutionAndFramerateBox();
            EditorGUILayout.Space(6);

            DrawOutputSettingsBox();

            if (EditorGUI.EndChangeCheck())
            {
                SaveConfig();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("High Quality Screen Recorder", EditorStyles.boldLabel);
            GUILayout.Label("OBS Studio-grade capture with GPU hardware acceleration & constant framerate", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        private void DrawFFmpegStatusBox()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("FFmpeg Engine Status", EditorStyles.boldLabel);

            bool isAvailable = FFmpegResolver.IsFFmpegAvailable(_config.customFFmpegPath);
            string ffmpegPath = FFmpegResolver.ResolveFFmpegPath(_config.customFFmpegPath);

            if (isAvailable)
            {
                GUI.color = new Color(0.3f, 1f, 0.3f);
                EditorGUILayout.LabelField("Status:", "Ready (FFmpeg detected)");
                GUI.color = Color.white;
                EditorGUILayout.LabelField("Path:", ffmpegPath, EditorStyles.miniLabel);

                // Hardware acceleration status badges
                EditorGUILayout.BeginHorizontal();
                DrawBadge("NVENC (NVIDIA)", FFmpegResolver.IsEncoderSupported("h264_nvenc", _config.customFFmpegPath));
                DrawBadge("QSV (Intel)", FFmpegResolver.IsEncoderSupported("h264_qsv", _config.customFFmpegPath));
                DrawBadge("AMF (AMD)", FFmpegResolver.IsEncoderSupported("h264_amf", _config.customFFmpegPath));
                DrawBadge("x264 (CPU)", FFmpegResolver.IsEncoderSupported("libx264", _config.customFFmpegPath));
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                EditorGUILayout.LabelField("Status:", "FFmpeg not detected!");
                GUI.color = Color.white;

                EditorGUILayout.HelpBox("FFmpeg is required to enable ultra-quality GPU encoding. Click below to download and setup automatically.", MessageType.Warning);

                if (_isDownloadingFFmpeg)
                {
                    EditorGUILayout.Space(4);
                    EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 20), _downloadProgress, _downloadStatus);
                }
                else
                {
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("⚡ High-Speed Auto Download", GUILayout.Height(28)))
                    {
                        StartDownloadFFmpeg();
                    }
                    if (GUILayout.Button("Install via WinGet (5s)", GUILayout.Height(28), GUILayout.Width(150)))
                    {
                        StartWinGetInstall();
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawBadge(string label, bool supported)
        {
            Color oldColor = GUI.backgroundColor;
            GUI.backgroundColor = supported ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.6f, 0.6f, 0.6f);
            GUILayout.Box(label, EditorStyles.miniButton, GUILayout.ExpandWidth(true));
            GUI.backgroundColor = oldColor;
        }

        private async void StartWinGetInstall()
        {
            _isDownloadingFFmpeg = true;
            _downloadProgress = 0.5f;
            _downloadStatus = "Installing FFmpeg via Windows WinGet...";
            Repaint();

            bool success = await FFmpegResolver.InstallViaWinGetAsync(status =>
            {
                _downloadStatus = status;
                Repaint();
            });

            _isDownloadingFFmpeg = false;
            Repaint();

            if (success)
            {
                EditorUtility.DisplayDialog("FFmpeg Installed", "FFmpeg has been successfully installed via WinGet and is ready to use!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("WinGet Failed", "WinGet installation failed. Please try the 'High-Speed Auto Download' button instead.", "OK");
            }
        }

        private async void StartDownloadFFmpeg()
        {
            _isDownloadingFFmpeg = true;
            _downloadProgress = 0f;
            _downloadStatus = "Initializing download...";
            Repaint();

            bool success = await FFmpegResolver.DownloadAndInstallFFmpegAsync((progress, status) =>
            {
                _downloadProgress = progress;
                _downloadStatus = status;
                Repaint();
            });

            _isDownloadingFFmpeg = false;
            Repaint();

            if (success)
            {
                EditorUtility.DisplayDialog("FFmpeg Installed", "FFmpeg has been successfully installed and configured!", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Download Failed", "Failed to download FFmpeg automatically. Please check your internet connection or configure a custom FFmpeg path.", "OK");
            }
        }

        private void DrawRecordingControlSection()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            if (RecorderController.State == RecordingState.Recording)
            {
                // Recording status display
                GUI.color = new Color(1f, 0.3f, 0.3f);
                TimeSpan elapsed = TimeSpan.FromSeconds(RecorderController.ElapsedTime);
                string timeStr = $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100}";
                
                GUILayout.Label($"● RECORDING  [{timeStr}]", EditorStyles.boldLabel);
                GUI.color = Color.white;

                EditorGUILayout.LabelField("Recorded Frames:", $"{RecorderController.RecordedFrames} frames");
                if (RecorderController.DroppedFrames > 0)
                {
                    GUI.color = Color.yellow;
                    EditorGUILayout.LabelField("Dropped Frames:", $"{RecorderController.DroppedFrames} frames");
                    GUI.color = Color.white;
                }

                EditorGUILayout.Space(4);

                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
                if (GUILayout.Button("■ STOP RECORDING (Ctrl+F9)", GUILayout.Height(40)))
                {
                    RecorderController.StopRecording();
                }
                GUI.backgroundColor = oldColor;
            }
            else if (RecorderController.State == RecordingState.Finishing)
            {
                GUI.color = Color.cyan;
                GUILayout.Label("Processing & Finalizing Video...", EditorStyles.boldLabel);
                GUI.color = Color.white;
                EditorGUILayout.HelpBox("Remuxing video and audio streams. Please wait a moment...", MessageType.Info);
            }
            else
            {
                // Idle state
                Color oldColor = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.3f, 0.85f, 0.3f);
                if (GUILayout.Button("● START RECORDING (Ctrl+F9)", GUILayout.Height(40)))
                {
                    SaveConfig();
                    RecorderController.StartRecording(_config);
                }
                GUI.backgroundColor = oldColor;

                if (!string.IsNullOrEmpty(RecorderController.LastRecordedFile) && File.Exists(RecorderController.LastRecordedFile))
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Open Folder", EditorStyles.miniButton))
                    {
                        EditorUtility.RevealInFinder(RecorderController.LastRecordedFile);
                    }
                    if (GUILayout.Button("Play Last Video", EditorStyles.miniButton))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = RecorderController.LastRecordedFile,
                            UseShellExecute = true
                        });
                    }
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEncoderSettingsBox()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Encoding & Quality Settings", EditorStyles.boldLabel);

            _config.encoderType = (EncoderType)EditorGUILayout.EnumPopup("Hardware Encoder", _config.encoderType);
            _config.videoCodec = (VideoCodec)EditorGUILayout.EnumPopup("Video Codec", _config.videoCodec);

            EditorGUILayout.Space(4);
            GUILayout.Label("Quality / Bitrate Control", EditorStyles.label);

            // Toggle between Quality Preset (CQP/CRF) and Explicit Bitrate (Mbps)
            string[] modes = { "Quality Preset (CRF/CQP)", "Target Bitrate (초당 Mbps)" };
            int currentModeIndex = (int)_config.qualityControlMode;
            int newModeIndex = GUILayout.Toolbar(currentModeIndex, modes);
            if (newModeIndex != currentModeIndex)
            {
                _config.qualityControlMode = (QualityControlMode)newModeIndex;
            }

            EditorGUILayout.Space(2);

            if (_config.qualityControlMode == QualityControlMode.QualityPreset)
            {
                _config.qualityPreset = (QualityPreset)EditorGUILayout.EnumPopup("Preset Level", _config.qualityPreset);
                switch (_config.qualityPreset)
                {
                    case QualityPreset.Lossless:
                        EditorGUILayout.HelpBox("Lossless (CQP 14): Near visually lossless master quality. Highest bitrate, pristine clarity.", MessageType.None);
                        break;
                    case QualityPreset.Ultra:
                        EditorGUILayout.HelpBox("Ultra (CQP 17): OBS Studio recommended high-quality setting. Zero block artifacts.", MessageType.None);
                        break;
                    case QualityPreset.High:
                        EditorGUILayout.HelpBox("High (CQP 20): Balanced quality and file size.", MessageType.None);
                        break;
                    case QualityPreset.Medium:
                        EditorGUILayout.HelpBox("Medium (CQP 24): Compact file size.", MessageType.None);
                        break;
                }
            }
            else
            {
                // Explicit target bitrate
                _config.targetBitrateMbps = EditorGUILayout.IntSlider("Bitrate (Mbps)", _config.targetBitrateMbps, 2, 200);

                // Quick bitrate presets
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("15 Mbps (HD)", EditorStyles.miniButton)) _config.targetBitrateMbps = 15;
                if (GUILayout.Button("35 Mbps (FHD)", EditorStyles.miniButton)) _config.targetBitrateMbps = 35;
                if (GUILayout.Button("60 Mbps (2K/4K)", EditorStyles.miniButton)) _config.targetBitrateMbps = 60;
                if (GUILayout.Button("100 Mbps (Master)", EditorStyles.miniButton)) _config.targetBitrateMbps = 100;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.HelpBox($"Target video bitrate set to {_config.targetBitrateMbps} Mbps (VBR peak: {Mathf.RoundToInt(_config.targetBitrateMbps * 1.5f)} Mbps).", MessageType.None);
            }

            EditorGUILayout.Space(4);

            _config.timingMode = (CaptureTimingMode)EditorGUILayout.EnumPopup("Timing Mode", _config.timingMode);
            if (_config.timingMode == CaptureTimingMode.ConstantFramerate)
            {
                EditorGUILayout.HelpBox("Constant Framerate locks Time.captureFramerate. Every frame is rendered with 0% dropped frames, creating silky-smooth 60fps/120fps video regardless of rendering load.", MessageType.Info);
            }

            _config.captureAudio = EditorGUILayout.Toggle("Capture Game Audio", _config.captureAudio);
            if (_config.captureAudio)
            {
                _config.audioBitrateKbps = EditorGUILayout.IntPopup("Audio Bitrate", _config.audioBitrateKbps, 
                    new string[] { "192 kbps", "256 kbps", "320 kbps (High Quality)" }, 
                    new int[] { 192, 256, 320 });
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawResolutionAndFramerateBox()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Resolution & Framerate", EditorStyles.boldLabel);

            _config.resolutionPreset = (ResolutionPreset)EditorGUILayout.EnumPopup("Resolution", _config.resolutionPreset);
            if (_config.resolutionPreset == ResolutionPreset.Custom)
            {
                EditorGUILayout.BeginHorizontal();
                _config.customWidth = EditorGUILayout.IntField("Width", _config.customWidth);
                _config.customHeight = EditorGUILayout.IntField("Height", _config.customHeight);
                EditorGUILayout.EndHorizontal();
            }

            _config.frameratePreset = (FrameratePreset)EditorGUILayout.EnumPopup("Framerate", _config.frameratePreset);
            if (_config.frameratePreset == FrameratePreset.Custom)
            {
                _config.customFramerate = EditorGUILayout.IntSlider("Target FPS", _config.customFramerate, 15, 144);
            }

            Vector2 gameView = RecorderController.GetGameViewSize();
            (int outW, int outH) = _config.GetTargetResolution((int)gameView.x, (int)gameView.y);
            EditorGUILayout.LabelField("Output Resolution:", $"{outW} x {outH} @ {_config.GetTargetFramerate()} FPS", EditorStyles.miniLabel);

            EditorGUILayout.Space(4);
            GUILayout.Label("화면 반전 / 회전 (Orientation & Flip)", EditorStyles.boldLabel);
            string[] flipLabels = { "None (원본)", "Flip Horizontal (좌우)", "Flip Vertical (상하)", "Rotate 180° (둘 다)" };
            _config.flipMode = (VideoFlipMode)GUILayout.Toolbar((int)_config.flipMode, flipLabels);

            EditorGUILayout.EndVertical();
        }

        private void DrawOutputSettingsBox()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("Output & File Settings", EditorStyles.boldLabel);

            _config.container = (OutputContainer)EditorGUILayout.EnumPopup("Container Format", _config.container);
            _config.fileNamePrefix = EditorGUILayout.TextField("File Prefix", _config.fileNamePrefix);

            EditorGUILayout.BeginHorizontal();
            string displayDir = string.IsNullOrEmpty(_config.outputDirectory) ? "Project Recordings/" : _config.outputDirectory;
            EditorGUILayout.TextField("Save Folder", displayDir);
            if (GUILayout.Button("Browse...", GUILayout.Width(75)))
            {
                string chosen = EditorUtility.OpenFolderPanel("Select Output Directory", _config.outputDirectory, "");
                if (!string.IsNullOrEmpty(chosen))
                {
                    _config.outputDirectory = chosen;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);
            _config.customFFmpegPath = EditorGUILayout.TextField("Custom FFmpeg Exe", _config.customFFmpegPath);

            EditorGUILayout.EndVertical();
        }

        private void SaveConfig()
        {
            if (_config != null)
            {
                RecorderSettingsManager.SaveSettings(_config);
            }
        }
    }
}
