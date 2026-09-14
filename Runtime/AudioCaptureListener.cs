using System;
using UnityEngine;

namespace HighQualityRecorder
{
    /// <summary>
    /// Component attached to the active AudioListener or a dedicated audio capture GameObject.
    /// Intercepts master output audio via OnAudioFilterRead and passes samples to WavAudioWriter.
    /// </summary>
    [ExecuteAlways]
    public class AudioCaptureListener : MonoBehaviour
    {
        private static AudioCaptureListener _instance;
        private WavAudioWriter _writer;
        private bool _isCapturing = false;

        public static AudioCaptureListener Instance => _instance;
        public bool IsCapturing => _isCapturing;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                DestroyImmediate(gameObject);
                return;
            }
            _instance = this;
            hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        }

        public static AudioCaptureListener EnsureInstance()
        {
            if (_instance != null) return _instance;

            // Search for existing AudioListener in scene
            var listener = FindObjectOfType<AudioListener>();
            if (listener != null)
            {
                var comp = listener.GetComponent<AudioCaptureListener>();
                if (comp == null)
                {
                    comp = listener.gameObject.AddComponent<AudioCaptureListener>();
                }
                _instance = comp;
                return comp;
            }

            // Fallback: create dedicated GameObject
            var go = new GameObject("[HighQualityRecorder_AudioCapture]");
            _instance = go.AddComponent<AudioCaptureListener>();
            DontDestroyOnLoad(go);
            return _instance;
        }

        public void StartCapture(string wavFilePath)
        {
            int channels = 2;
            int sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;

            switch (AudioSettings.speakerMode)
            {
                case AudioSpeakerMode.Mono: channels = 1; break;
                case AudioSpeakerMode.Stereo: channels = 2; break;
                case AudioSpeakerMode.Quad: channels = 4; break;
                case AudioSpeakerMode.Surround: channels = 5; break;
                case AudioSpeakerMode.Mode5point1: channels = 6; break;
                case AudioSpeakerMode.Mode7point1: channels = 8; break;
                default: channels = 2; break;
            }

            _writer = new WavAudioWriter(wavFilePath, channels, sampleRate);
            _isCapturing = true;
        }

        public void StopCapture()
        {
            _isCapturing = false;
            if (_writer != null)
            {
                _writer.FinalizeAndClose();
                _writer = null;
            }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_isCapturing || _writer == null) return;
            _writer.WriteSamples(data, data.Length);
        }

        private void OnDestroy()
        {
            StopCapture();
            if (_instance == this)
            {
                _instance = null;
            }
        }
    }
}
