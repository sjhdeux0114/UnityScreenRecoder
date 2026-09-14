using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace HighQualityRecorder.Editor
{
    public static class FFmpegResolver
    {
        private static string _cachedPath = null;
        private static readonly HashSet<string> _supportedEncoders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _hasProbedEncoders = false;

        // Windows 64-bit static release link (Gyan.dev official ffmpeg-release-essentials.zip)
        private const string FFMPEG_DOWNLOAD_URL = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";
        private const string FFMPEG_GITHUB_MIRROR = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";

        public static string GetInstalledFFmpegDir()
        {
            // Install into project Library/FFmpeg so it's persistent across sessions and gitignored
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            return Path.Combine(projectRoot, "Library", "FFmpeg");
        }

        public static string ResolveFFmpegPath(string customPath = "")
        {
            // 1. Check custom path if configured
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                _cachedPath = customPath;
                return customPath;
            }

            if (!string.IsNullOrEmpty(_cachedPath) && File.Exists(_cachedPath))
            {
                return _cachedPath;
            }

            // 2. Check project Library/FFmpeg
            string projectFFmpeg = Path.Combine(GetInstalledFFmpegDir(), "bin", "ffmpeg.exe");
            if (File.Exists(projectFFmpeg))
            {
                _cachedPath = projectFFmpeg;
                return projectFFmpeg;
            }

            // Also check direct root of Library/FFmpeg
            projectFFmpeg = Path.Combine(GetInstalledFFmpegDir(), "ffmpeg.exe");
            if (File.Exists(projectFFmpeg))
            {
                _cachedPath = projectFFmpeg;
                return projectFFmpeg;
            }

            // 3. Check System PATH
            string pathFromWhere = FindInPath("ffmpeg.exe");
            if (!string.IsNullOrEmpty(pathFromWhere) && File.Exists(pathFromWhere))
            {
                _cachedPath = pathFromWhere;
                return pathFromWhere;
            }

            // 4. Check typical package directory if installed locally
            string packageBin = Path.GetFullPath("Packages/com.studio.unityrecorder/Binaries/ffmpeg.exe");
            if (File.Exists(packageBin))
            {
                _cachedPath = packageBin;
                return packageBin;
            }

            return null;
        }

        public static bool IsFFmpegAvailable(string customPath = "")
        {
            string path = ResolveFFmpegPath(customPath);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        private static string FindInPath(string exeName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = exeName,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        string output = process.StandardOutput.ReadLine();
                        process.WaitForExit(1000);
                        if (!string.IsNullOrEmpty(output) && File.Exists(output.Trim()))
                        {
                            return output.Trim();
                        }
                    }
                }
            }
            catch
            {
                // Ignored
            }
            return null;
        }

        public static void ProbeSupportedEncoders(string customPath = "")
        {
            string ffmpeg = ResolveFFmpegPath(customPath);
            if (string.IsNullOrEmpty(ffmpeg) || !File.Exists(ffmpeg)) return;

            _supportedEncoders.Clear();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpeg,
                    Arguments = "-encoders",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        string line;
                        while ((line = proc.StandardOutput.ReadLine()) != null)
                        {
                            // ffmpeg -encoders lines: V..... h264_nvenc ...
                            if (line.Contains("h264_nvenc")) _supportedEncoders.Add("h264_nvenc");
                            if (line.Contains("hevc_nvenc")) _supportedEncoders.Add("hevc_nvenc");
                            if (line.Contains("h264_qsv")) _supportedEncoders.Add("h264_qsv");
                            if (line.Contains("hevc_qsv")) _supportedEncoders.Add("hevc_qsv");
                            if (line.Contains("h264_amf")) _supportedEncoders.Add("h264_amf");
                            if (line.Contains("hevc_amf")) _supportedEncoders.Add("hevc_amf");
                            if (line.Contains("libx264")) _supportedEncoders.Add("libx264");
                            if (line.Contains("libx265")) _supportedEncoders.Add("libx265");
                        }
                        proc.WaitForExit(3000);
                        _hasProbedEncoders = true;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HighQualityRecorder] Failed to probe FFmpeg encoders: {ex.Message}");
            }
        }

        public static bool IsEncoderSupported(string encoderName, string customPath = "")
        {
            if (!_hasProbedEncoders)
            {
                ProbeSupportedEncoders(customPath);
            }
            return _supportedEncoders.Contains(encoderName);
        }

        public static async Task<bool> DownloadAndInstallFFmpegAsync(Action<float, string> onProgress = null)
        {
            string targetDir = GetInstalledFFmpegDir();
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string zipPath = Path.Combine(targetDir, "ffmpeg_download.zip");

            try
            {
                onProgress?.Invoke(0.1f, "Connecting to FFmpeg download server...");

                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = TimeSpan.FromMinutes(5);

                    string downloadUrl = FFMPEG_DOWNLOAD_URL;
                    HttpResponseMessage response = null;

                    try
                    {
                        response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                    }
                    catch
                    {
                        // Fallback to GitHub Mirror
                        onProgress?.Invoke(0.15f, "Trying GitHub mirror...");
                        downloadUrl = FFMPEG_GITHUB_MIRROR;
                        response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();
                    }

                    long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[65536];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                float progress = 0.2f + 0.6f * ((float)totalRead / totalBytes);
                                float mbRead = totalRead / (1024f * 1024f);
                                float mbTotal = totalBytes / (1024f * 1024f);
                                onProgress?.Invoke(progress, $"Downloading FFmpeg: {mbRead:F1}MB / {mbTotal:F1}MB...");
                            }
                            else
                            {
                                float mbRead = totalRead / (1024f * 1024f);
                                onProgress?.Invoke(0.5f, $"Downloading FFmpeg: {mbRead:F1}MB...");
                            }
                        }
                    }
                }

                onProgress?.Invoke(0.85f, "Extracting FFmpeg binaries...");

                // Extract ffmpeg.exe from zip
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.Name.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase) ||
                            entry.Name.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            string destFile = Path.Combine(targetDir, entry.Name);
                            entry.ExtractToFile(destFile, true);
                        }
                    }
                }

                // Clean up zip
                try
                {
                    if (File.Exists(zipPath)) File.Delete(zipPath);
                }
                catch { }

                _cachedPath = null;
                _hasProbedEncoders = false;

                string installedExe = ResolveFFmpegPath();
                if (!string.IsNullOrEmpty(installedExe) && File.Exists(installedExe))
                {
                    ProbeSupportedEncoders();
                    onProgress?.Invoke(1.0f, "FFmpeg installation completed successfully!");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HighQualityRecorder] Failed to download FFmpeg: {ex.Message}");
                onProgress?.Invoke(0.0f, $"Download error: {ex.Message}");
                return false;
            }
        }
    }
}
