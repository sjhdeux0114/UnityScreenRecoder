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

        private const string PREFS_KEY = "HighQualityRecorder_Config_v1";

        [MenuItem("Tools/High Quality Screen Recorder %#r", false, 100)]
        public static void Open()
        {
            var window = GetWindow<RecorderWindow>("Screen Recorder");
            window.minSize = new Vector2(420, 560);
            window.Show();
        }

        private void OnEnable()
        {
            LoadConfig();
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
            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            DrawHeader();
            EditorGUILayout.Space(6);

            DrawFFmpegStatusBox();
            EditorGUILayout.Space(6);

            DrawRecordingControlSection();
            EditorGUILayout.Space(6);

            EditorGUI.BeginDisabledGroup(RecorderController.IsRecording);
            DrawEncoderSettingsBox();
            EditorGUILayout.Space(6);

            DrawResolutionAndFramerateBox();
            EditorGUILayout.Space(6);

            DrawOutputSettingsBox();
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
                    if (GUILayout.Button("Download & Setup FFmpeg Automatically", GUILayout.Height(28)))
                    {
                        StartDownloadFFmpeg();
                    }
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
                if (GUILayout.Button("■ STOP RECORDING (F9)", GUILayout.Height(40)))
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
                if (GUILayout.Button("● START RECORDING (F9)", GUILayout.Height(40)))
                {
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
            _config.qualityPreset = (QualityPreset)EditorGUILayout.EnumPopup("Quality Preset", _config.qualityPreset);

            if (_config.qualityPreset == QualityPreset.CustomBitrate)
            {
                _config.customBitrateMbps = EditorGUILayout.IntSlider("Bitrate (Mbps)", _config.customBitrateMbps, 5, 200);
            }

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

        private void LoadConfig()
        {
            string json = EditorPrefs.GetString(PREFS_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    _config = JsonUtility.FromJson<RecorderConfig>(json);
                }
                catch { }
            }
            if (_config == null)
            {
                _config = new RecorderConfig();
            }
        }

        private void SaveConfig()
        {
            if (_config != null)
            {
                string json = JsonUtility.ToJson(_config);
                EditorPrefs.SetString(PREFS_KEY, json);
            }
        }
    }
}
