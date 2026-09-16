using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HighQualityRecorder.Editor
{
    /// <summary>
    /// Manages the FFmpeg subprocess, pipes raw RGBA frames to stdin, and handles remuxing.
    /// </summary>
    public class FFmpegEncoderProcess : IDisposable
    {
        private Process _process;
        private Stream _stdinStream;
        private readonly object _streamLock = new object();
        private bool _isDisposed = false;
        private readonly StringBuilder _errorLog = new StringBuilder();

        public bool IsRunning => _process != null && !_process.HasExited;

        public bool Start(string ffmpegExe, RecorderConfig config, int width, int height, int fps, string outputVideoPath)
        {
            if (string.IsNullOrEmpty(ffmpegExe) || !File.Exists(ffmpegExe))
            {
                Debug.LogError($"[HighQualityRecorder] FFmpeg executable not found at: {ffmpegExe}");
                return false;
            }

            string arguments = BuildVideoArguments(config, width, height, fps, outputVideoPath);
            Debug.Log($"[HighQualityRecorder] Launching FFmpeg with command: {ffmpegExe} {arguments}");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegExe,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            try
            {
                _process = Process.Start(psi);
                if (_process == null) return false;

                _stdinStream = new BufferedStream(_process.StandardInput.BaseStream, 1024 * 1024 * 4);

                // Async read standard error to prevent buffer lockups
                _process.ErrorDataReceived += (sender, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        lock (_errorLog)
                        {
                            if (_errorLog.Length < 10000)
                            {
                                _errorLog.AppendLine(e.Data);
                            }
                        }
                    }
                };
                _process.BeginErrorReadLine();

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HighQualityRecorder] Failed to start FFmpeg process: {ex.Message}");
                return false;
            }
        }

        private string BuildVideoArguments(RecorderConfig config, int width, int height, int fps, string outputPath)
        {
            var sb = new StringBuilder();

            // Overwrite output file if exists
            sb.Append("-y ");

            // Input: Raw RGBA video from stdin
            sb.Append($"-f rawvideo -pixel_format rgba -video_size {width}x{height} -framerate {fps} -i - ");

            // Determine actual encoder
            string encoder = DetermineEncoder(config);
            sb.Append($"-c:v {encoder} ");

            // Quality / Bitrate settings
            ApplyQualitySettings(sb, config, encoder);

            // Apply Orientation / Flip Filter
            switch (config.flipMode)
            {
                case VideoFlipMode.FlipHorizontal:
                    sb.Append("-vf \"hflip\" ");
                    break;
                case VideoFlipMode.FlipVertical:
                    sb.Append("-vf \"vflip\" ");
                    break;
                case VideoFlipMode.Rotate180:
                    sb.Append("-vf \"vflip,hflip\" ");
                    break;
                case VideoFlipMode.None:
                default:
                    // No filter (raw buffer)
                    break;
            }

            // Pixel format for maximum player compatibility (OBS standard: yuv420p)
            sb.Append("-pix_fmt yuv420p ");

            // Output path
            sb.Append($"\"{outputPath}\"");

            return sb.ToString();
        }

        private string DetermineEncoder(RecorderConfig config)
        {
            switch (config.encoderType)
            {
                case EncoderType.NvidiaNVENC:
                    return config.videoCodec == VideoCodec.HEVC ? "hevc_nvenc" : "h264_nvenc";

                case EncoderType.IntelQSV:
                    return config.videoCodec == VideoCodec.HEVC ? "hevc_qsv" : "h264_qsv";

                case EncoderType.AmdAMF:
                    return config.videoCodec == VideoCodec.HEVC ? "hevc_amf" : "h264_amf";

                case EncoderType.SoftwareX264:
                    return config.videoCodec == VideoCodec.HEVC ? "libx265" : "libx264";

                case EncoderType.Auto:
                default:
                    // Auto detect GPU hardware acceleration
                    if (config.videoCodec == VideoCodec.HEVC)
                    {
                        if (FFmpegResolver.IsEncoderSupported("hevc_nvenc", config.customFFmpegPath)) return "hevc_nvenc";
                        if (FFmpegResolver.IsEncoderSupported("hevc_qsv", config.customFFmpegPath)) return "hevc_qsv";
                        if (FFmpegResolver.IsEncoderSupported("hevc_amf", config.customFFmpegPath)) return "hevc_amf";
                        return "libx265";
                    }
                    else
                    {
                        if (FFmpegResolver.IsEncoderSupported("h264_nvenc", config.customFFmpegPath)) return "h264_nvenc";
                        if (FFmpegResolver.IsEncoderSupported("h264_qsv", config.customFFmpegPath)) return "h264_qsv";
                        if (FFmpegResolver.IsEncoderSupported("h264_amf", config.customFFmpegPath)) return "h264_amf";
                        return "libx264";
                    }
            }
        }

        private void ApplyQualitySettings(StringBuilder sb, RecorderConfig config, string encoder)
        {
            bool isNvenc = encoder.Contains("nvenc");
            bool isQsv = encoder.Contains("qsv");
            bool isAmf = encoder.Contains("amf");
            bool isSoftware = encoder.Contains("libx26");

            if (config.qualityControlMode == QualityControlMode.TargetBitrate)
            {
                // Explicit Bitrate mode (Mbps)
                int mbps = Mathf.Clamp(config.targetBitrateMbps, 1, 300);
                int maxRate = Mathf.RoundToInt(mbps * 1.5f);
                int bufSize = mbps * 2;

                if (isNvenc)
                {
                    // NVIDIA NVENC VBR mode with specified bitrate
                    sb.Append($"-rc vbr -b:v {mbps}M -maxrate {maxRate}M -bufsize {bufSize}M -preset p6 -tune hq ");
                }
                else if (isQsv)
                {
                    sb.Append($"-b:v {mbps}M -maxrate {maxRate}M -bufsize {bufSize}M -preset veryfast ");
                }
                else if (isAmf)
                {
                    sb.Append($"-rc vbr_latency -b:v {mbps}M -maxrate {maxRate}M -quality quality ");
                }
                else
                {
                    // Software libx264 / libx265
                    sb.Append($"-b:v {mbps}M -maxrate {maxRate}M -bufsize {bufSize}M -preset veryfast ");
                }
                return;
            }

            // Quality Preset mode (CQP / CRF)
            int qp = 17; // Default Ultra (OBS High Quality)
            switch (config.qualityPreset)
            {
                case QualityPreset.Lossless: qp = 14; break;
                case QualityPreset.Ultra: qp = 17; break;
                case QualityPreset.High: qp = 20; break;
                case QualityPreset.Medium: qp = 24; break;
            }

            if (isNvenc)
            {
                // NVIDIA NVENC CQP Ultra settings
                sb.Append($"-preset p6 -tune hq -rc constqp -qp {qp} ");
            }
            else if (isQsv)
            {
                sb.Append($"-global_quality {qp} -preset veryfast ");
            }
            else if (isAmf)
            {
                sb.Append($"-rc cqp -qp_i {qp} -qp_p {qp} -quality quality ");
            }
            else
            {
                // libx264 / libx265
                sb.Append($"-preset veryfast -crf {qp} ");
            }
        }

        public bool WriteFrame(byte[] buffer, int offset, int length)
        {
            if (_isDisposed || _stdinStream == null || !IsRunning) return false;

            lock (_streamLock)
            {
                if (_isDisposed || _stdinStream == null) return false;
                try
                {
                    _stdinStream.Write(buffer, offset, length);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[HighQualityRecorder] Failed to write frame to FFmpeg: {ex.Message}");
                    return false;
                }
            }
        }

        public void CloseInputAndStop(int timeoutMs = 5000)
        {
            lock (_streamLock)
            {
                if (_stdinStream != null)
                {
                    try
                    {
                        _stdinStream.Flush();
                        _stdinStream.Close();
                    }
                    catch { }
                    _stdinStream = null;
                }
            }

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    if (!_process.WaitForExit(timeoutMs))
                    {
                        Debug.LogWarning("[HighQualityRecorder] FFmpeg did not exit in time. Forcing kill.");
                        _process.Kill();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[HighQualityRecorder] Error stopping FFmpeg: {ex.Message}");
                }
            }

            if (_process != null)
            {
                if (_process.ExitCode != 0)
                {
                    lock (_errorLog)
                    {
                        Debug.LogWarning($"[HighQualityRecorder] FFmpeg exit code {_process.ExitCode}. Log:\n{_errorLog}");
                    }
                }
                _process.Dispose();
                _process = null;
            }
        }

        public static bool RemuxVideoAndAudio(string ffmpegExe, string videoPath, string audioPath, string outputPath, int audioBitrateKbps = 320)
        {
            if (!File.Exists(videoPath))
            {
                Debug.LogError($"[HighQualityRecorder] Video file missing for remux: {videoPath}");
                return false;
            }

            bool hasAudio = !string.IsNullOrEmpty(audioPath) && File.Exists(audioPath);

            string arguments;
            if (hasAudio)
            {
                // Stream-copy video (no re-encoding, 0% quality loss, instant), encode audio to high-bitrate AAC
                arguments = $"-y -i \"{videoPath}\" -i \"{audioPath}\" -c:v copy -c:a aac -b:a {audioBitrateKbps}k -shortest \"{outputPath}\"";
            }
            else
            {
                // Just copy video to final destination
                arguments = $"-y -i \"{videoPath}\" -c:v copy \"{outputPath}\"";
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegExe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string err = proc.StandardError.ReadToEnd();
                        proc.WaitForExit(120000); // Allow up to 2 minutes for large videos or external drives
                        if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
                        {
                            return true;
                        }
                        if (proc.HasExited && proc.ExitCode != 0)
                        {
                            Debug.LogError($"[HighQualityRecorder] Remux failed with exit code {proc.ExitCode}: {err}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HighQualityRecorder] Remux exception: {ex.Message}");
            }

            // Fallback: check if output file exists and is valid anyway
            return File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            CloseInputAndStop(1000);
        }
    }
}
