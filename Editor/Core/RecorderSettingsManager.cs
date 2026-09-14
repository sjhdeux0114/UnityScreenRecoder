using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace HighQualityRecorder.Editor
{
    /// <summary>
    /// Persists RecorderConfig settings reliably across editor restarts, domain reloads, and git operations.
    /// Stores in both UserSettings/UnityRecorderSettings.json and EditorPrefs.
    /// </summary>
    public static class RecorderSettingsManager
    {
        private static RecorderConfig _cachedConfig = null;
        private const string PREFS_KEY = "HighQualityRecorder_Config_v2";

        private static string GetUserSettingsPath()
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string userSettingsDir = Path.Combine(projectRoot, "UserSettings");
            if (!Directory.Exists(userSettingsDir))
            {
                Directory.CreateDirectory(userSettingsDir);
            }
            return Path.Combine(userSettingsDir, "UnityRecorderSettings.json");
        }

        public static RecorderConfig GetSettings()
        {
            if (_cachedConfig == null)
            {
                _cachedConfig = LoadSettings();
            }
            return _cachedConfig;
        }

        public static RecorderConfig LoadSettings()
        {
            // 1. Try UserSettings/UnityRecorderSettings.json
            string filePath = GetUserSettingsPath();
            if (File.Exists(filePath))
            {
                try
                {
                    string json = File.ReadAllText(filePath);
                    if (!string.IsNullOrEmpty(json))
                    {
                        var config = JsonUtility.FromJson<RecorderConfig>(json);
                        if (config != null)
                        {
                            _cachedConfig = config;
                            return config;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HighQualityRecorder] Failed to read settings from {filePath}: {ex.Message}");
                }
            }

            // 2. Fallback to EditorPrefs
            string prefsJson = EditorPrefs.GetString(PREFS_KEY, "");
            if (!string.IsNullOrEmpty(prefsJson))
            {
                try
                {
                    var config = JsonUtility.FromJson<RecorderConfig>(prefsJson);
                    if (config != null)
                    {
                        _cachedConfig = config;
                        return config;
                    }
                }
                catch { }
            }

            // 3. Fallback to default
            _cachedConfig = new RecorderConfig();
            return _cachedConfig;
        }

        public static void SaveSettings(RecorderConfig config = null)
        {
            if (config != null)
            {
                _cachedConfig = config;
            }
            if (_cachedConfig == null) return;

            try
            {
                string json = JsonUtility.ToJson(_cachedConfig, true);

                // Save to UserSettings file
                string filePath = GetUserSettingsPath();
                File.WriteAllText(filePath, json);

                // Save to EditorPrefs
                EditorPrefs.SetString(PREFS_KEY, json);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HighQualityRecorder] Failed to save settings: {ex.Message}");
            }
        }
    }
}
