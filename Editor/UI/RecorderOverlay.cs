using System;
using UnityEditor;
using UnityEngine;

namespace HighQualityRecorder.Editor
{
    [InitializeOnLoad]
    public static class RecorderOverlay
    {
        private static GUIStyle _recBadgeStyle;

        static RecorderOverlay()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            // If exiting play mode while recording, safely stop and finalize
            if (state == PlayModeStateChange.ExitingPlayMode && RecorderController.IsRecording)
            {
                RecorderController.StopRecording();
            }
        }

        [MenuItem("Tools/Toggle Recording _F9", false, 101)]
        private static void ToggleRecordingHotkey()
        {
            if (RecorderController.IsRecording)
            {
                RecorderController.StopRecording();
            }
            else
            {
                // Load existing configuration from EditorPrefs or fallback
                string json = EditorPrefs.GetString("HighQualityRecorder_Config_v1", "");
                RecorderConfig config = null;
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        config = JsonUtility.FromJson<RecorderConfig>(json);
                    }
                    catch { }
                }

                if (config == null)
                {
                    config = new RecorderConfig();
                }

                RecorderController.StartRecording(config);
            }
        }

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (!RecorderController.IsRecording) return;

            Handles.BeginGUI();

            if (_recBadgeStyle == null)
            {
                _recBadgeStyle = new GUIStyle(GUI.skin.box)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
                _recBadgeStyle.normal.textColor = Color.white;
            }

            TimeSpan elapsed = TimeSpan.FromSeconds(RecorderController.ElapsedTime);
            string timeText = $"● REC {elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

            Rect badgeRect = new Rect(10, 10, 100, 26);
            Color oldBg = GUI.backgroundColor;
            // Pulsing red effect
            float pulse = Mathf.PingPong((float)EditorApplication.timeSinceStartup * 2f, 1f);
            GUI.backgroundColor = new Color(0.8f + 0.2f * pulse, 0.1f, 0.1f, 0.9f);

            GUI.Box(badgeRect, timeText, _recBadgeStyle);

            GUI.backgroundColor = oldBg;
            Handles.EndGUI();
        }
    }
}
