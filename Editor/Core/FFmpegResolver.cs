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

        // High-speed GitHub CDN links (Essential build is ~35MB lightweight, significantly faster than full 200MB builds)
        private const string FFMPEG_LIGHT_URL = "https://github.com/GyanD/codexffmpeg/releases/download/7.1/ffmpeg-7.1-essentials_build.zip";
        private const string FFMPEG_GITHUB_MIRROR = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        private const string FFMPEG_FALLBACK_URL = "https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip";

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

            // 4. Check typical WinGet / Scoop / Local paths
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            string[] commonPaths = new[]
            {
                Path.Combine(localAppData, "Microsoft", "WinGet", "Links", "ffmpeg.exe"),
                Path.Combine(userProfile, "scoop", "shims", "ffmpeg.exe"),
                @"C:\ProgramData\chocolatey\bin\ffmpeg.exe"
            };

            foreach (var p in commonPaths)
            {
                if (File.Exists(p))
                {
                    _cachedPath = p;
                    return p;
                }
            }

            // Also check WinGet Packages directory (e.g. Gyan.FFmpeg)
            string wingetPackages = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetPackages))
            {
                try
                {
                    string[] found = Directory.GetFiles(wingetPackages, "ffmpeg.exe", SearchOption.AllDirectories);
                    if (found != null && found.Length > 0 && File.Exists(found[0]))
                    {
                        _cachedPath = found[0];
                        return found[0];
                    }
                }
                catch { }
            }

            // 5. Check typical package directory if installed locally
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

        public static async Task<bool> InstallViaWinGetAsync(Action<string> onStatus = null)
        {
            onStatus?.Invoke("Installing FFmpeg via Windows WinGet...");
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget.exe",
                    Arguments = "install Gyan.FFmpeg --accept-source-agreements --accept-package-agreements --silent",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var proc = Process.Start(psi))
                {
                    if (proc != null)
                    {
                        await Task.Run(() => proc.WaitForExit(120000));
                        _cachedPath = null;
                        _hasProbedEncoders = false;

                        if (IsFFmpegAvailable())
                        {
                            ProbeSupportedEncoders();
                            onStatus?.Invoke("WinGet installation succeeded!");
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[HighQualityRecorder] WinGet install error: {ex.Message}");
            }
            return false;
        }

        public static async Task<bool> DownloadAndInstallFFmpegAsync(Action<float, string> onProgress = null)
        {
            string targetDir = GetInstalledFFmpegDir();
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            string zipPath = Path.Combine(targetDir, "ffmpeg_download.zip");

            // Check if Windows curl.exe is available for hardware socket acceleration
            bool hasCurl = File.Exists(Path.Combine(Environment.SystemDirectory, "curl.exe"));

            try
            {
                string[] urls = new[] { FFMPEG_LIGHT_URL, FFMPEG_GITHUB_MIRROR, FFMPEG_FALLBACK_URL };
                bool downloadSuccess = false;

                foreach (var downloadUrl in urls)
                {
                    onProgress?.Invoke(0.1f, $"Connecting to high-speed CDN ({Path.GetFileName(downloadUrl)})...");

                    if (hasCurl)
                    {
                        onProgress?.Invoke(0.2f, "Downloading FFmpeg using accelerated curl socket...");
                        var psi = new ProcessStartInfo
                        {
                            FileName = "curl.exe",
                            Arguments = $"-L --retry 3 --connect-timeout 10 -o \"{zipPath}\" \"{downloadUrl}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using (var proc = Process.Start(psi))
                        {
                            if (proc != null)
                            {
                                await Task.Run(() => proc.WaitForExit(120000));
                                if (proc.ExitCode == 0 && File.Exists(zipPath) && new FileInfo(zipPath).Length > 1024 * 1024)
                                {
                                    downloadSuccess = true;
                                    break;
                                }
                            }
                        }
                    }

                    if (!downloadSuccess)
                    {
                        // Fallback to optimized C# HttpClient with 512KB buffer
                        try
                        {
                            using (var httpClient = new HttpClient())
                            {
                                httpClient.Timeout = TimeSpan.FromMinutes(4);
                                var response = await httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
                                response.EnsureSuccessStatusCode();

                                long totalBytes = response.Content.Headers.ContentLength ?? -1L;

                                using (var contentStream = await response.Content.ReadAsStreamAsync())
                                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 524288, true))
                                {
                                    byte[] buffer = new byte[524288]; // 512KB buffer
                                    long totalRead = 0;
                                    int bytesRead;

                                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                                    {
                                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                                        totalRead += bytesRead;

                                        float progress = totalBytes > 0 ? (0.2f + 0.6f * ((float)totalRead / totalBytes)) : 0.5f;
                                        float mbRead = totalRead / (1024f * 1024f);
                                        float mbTotal = totalBytes > 0 ? (totalBytes / (1024f * 1024f)) : 0f;
                                        onProgress?.Invoke(progress, $"Downloading FFmpeg: {mbRead:F1}MB / {mbTotal:F1}MB...");
                                    }
                                }
                                downloadSuccess = true;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[HighQualityRecorder] Mirror {downloadUrl} failed: {ex.Message}. Trying next...");
                        }
                    }
                }

                if (!downloadSuccess || !File.Exists(zipPath))
                {
                    throw new Exception("All high-speed download mirrors failed. Please check your network connection.");
                }

                onProgress?.Invoke(0.85f, "Extracting FFmpeg binaries...");

                // Extract ffmpeg.exe from zip
                await Task.Run(() =>
                {
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
                });

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
