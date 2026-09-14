using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace HighQualityRecorder
{
    /// <summary>
    /// Writes raw float audio samples from Unity AudioListener to a standard PCM 16-bit WAV file.
    /// Thread-safe for writing samples from the Unity audio thread.
    /// </summary>
    public class WavAudioWriter : IDisposable
    {
        private FileStream _fileStream;
        private BinaryWriter _writer;
        private readonly object _lock = new object();
        private int _sampleCount = 0;
        private readonly int _channels;
        private readonly int _sampleRate;
        private bool _isDisposed = false;

        public string FilePath { get; private set; }

        public WavAudioWriter(string filePath, int channels, int sampleRate)
        {
            FilePath = filePath;
            _channels = Mathf.Clamp(channels, 1, 8);
            _sampleRate = sampleRate;

            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read);
            _writer = new BinaryWriter(_fileStream);

            WriteHeaderPlaceholder();
        }

        private void WriteHeaderPlaceholder()
        {
            // RIFF header placeholder (44 bytes standard PCM wav)
            _writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            _writer.Write(0); // Placeholder for File size - 8
            _writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            // fmt chunk
            _writer.Write(Encoding.ASCII.GetBytes("fmt "));
            _writer.Write(16); // Subchunk1Size for PCM
            _writer.Write((short)1); // AudioFormat: 1 = PCM
            _writer.Write((short)_channels);
            _writer.Write(_sampleRate);
            _writer.Write(_sampleRate * _channels * 2); // ByteRate (sampleRate * channels * 16bit / 8)
            _writer.Write((short)(_channels * 2)); // BlockAlign
            _writer.Write((short)16); // BitsPerSample: 16-bit

            // data chunk placeholder
            _writer.Write(Encoding.ASCII.GetBytes("data"));
            _writer.Write(0); // Placeholder for Subchunk2Size
            _fileStream.Flush();
        }

        public void WriteSamples(float[] samples, int count)
        {
            if (_isDisposed || _writer == null) return;

            lock (_lock)
            {
                if (_isDisposed || _writer == null) return;

                for (int i = 0; i < count; i++)
                {
                    // Clamp float -1.0 .. 1.0 to 16-bit signed integer
                    float f = Mathf.Clamp(samples[i], -1.0f, 1.0f);
                    short pcm = (short)(f * 32767f);
                    _writer.Write(pcm);
                }
                _sampleCount += count;
            }
        }

        public void FinalizeAndClose()
        {
            lock (_lock)
            {
                if (_isDisposed) return;
                _isDisposed = true;

                if (_writer != null && _fileStream != null && _fileStream.CanSeek)
                {
                    try
                    {
                        long dataSize = _sampleCount * 2; // 2 bytes per 16-bit sample
                        long fileSize = 36 + dataSize;

                        // Seek and write total file size - 8
                        _fileStream.Seek(4, SeekOrigin.Begin);
                        _writer.Write((uint)fileSize);

                        // Seek and write data chunk size
                        _fileStream.Seek(40, SeekOrigin.Begin);
                        _writer.Write((uint)dataSize);

                        _fileStream.Flush();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[HighQualityRecorder] Error finalizing WAV header: {ex.Message}");
                    }
                }

                _writer?.Close();
                _fileStream?.Close();
                _writer = null;
                _fileStream = null;
            }
        }

        public void Dispose()
        {
            FinalizeAndClose();
        }
    }
}
