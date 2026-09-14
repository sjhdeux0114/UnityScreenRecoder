using System;
using UnityEngine;

namespace HighQualityRecorder
{
    public enum EncoderType
    {
        Auto = 0,
        NvidiaNVENC = 1,
        AmdAMF = 2,
        IntelQSV = 3,
        SoftwareX264 = 4
    }

    public enum VideoCodec
    {
        H264 = 0,
        HEVC = 1 // H.265
    }

    public enum QualityControlMode
    {
        QualityPreset = 0, // CQP / CRF (OBS High Quality Constant Rate Factor)
        TargetBitrate = 1  // Specify explicit bitrate in Mbps (VBR)
    }

    public enum QualityPreset
    {
        Lossless = 0,  // CQP 14 / CRF 14
        Ultra = 1,     // CQP 17 / CRF 17 (OBS High Quality equivalent)
        High = 2,      // CQP 20 / CRF 20 (Balanced)
        Medium = 3     // CQP 24 / CRF 24
    }

    public enum ResolutionPreset
    {
        MatchGameView = 0,
        FHD_1080p = 1,   // 1920x1080
        QHD_1440p = 2,   // 2560x1440
        UHD_4K_2160p = 3,// 3840x2160
        Custom = 4
    }

    public enum FrameratePreset
    {
        FPS_30 = 30,
        FPS_60 = 60,
        FPS_120 = 120,
        Custom = 0
    }

    public enum CaptureTimingMode
    {
        /// <summary>
        /// Captures gameplay in real-time as you play.
        /// </summary>
        Realtime = 0,

        /// <summary>
        /// Locks Time.captureFramerate to eliminate any dropped frames or stuttering,
        /// rendering every single frame smoothly even under heavy rendering load.
        /// Ideal for high-end trailers, showcases, and cinematics.
        /// </summary>
        ConstantFramerate = 1
    }

    public enum OutputContainer
    {
        MP4 = 0,
        MKV = 1,
        MOV = 2
    }

    public enum VideoFlipMode
    {
        None = 0,             // No flip (Raw buffer)
        FlipHorizontal = 1,   // Horizontal Flip (hflip)
        FlipVertical = 2,     // Vertical Flip (vflip)
        Rotate180 = 3         // Both flips / 180° rotation (vflip, hflip)
    }

    [Serializable]
    public class RecorderConfig
    {
        public EncoderType encoderType = EncoderType.Auto;
        public VideoCodec videoCodec = VideoCodec.H264;

        public VideoFlipMode flipMode = VideoFlipMode.FlipHorizontal; // Corrects inverted orientation

        public QualityControlMode qualityControlMode = QualityControlMode.QualityPreset;
        public QualityPreset qualityPreset = QualityPreset.Ultra;
        public int targetBitrateMbps = 40; // Target bitrate in Mbps (e.g. 10 ~ 200)

        public ResolutionPreset resolutionPreset = ResolutionPreset.MatchGameView;
        public int customWidth = 1920;
        public int customHeight = 1080;

        public FrameratePreset frameratePreset = FrameratePreset.FPS_60;
        public int customFramerate = 60;

        public CaptureTimingMode timingMode = CaptureTimingMode.Realtime;
        public OutputContainer container = OutputContainer.MP4;

        public bool captureAudio = true;
        public int audioBitrateKbps = 320;

        public string outputDirectory = "";
        public string fileNamePrefix = "UnityRecord";

        public string customFFmpegPath = "";

        public int GetTargetFramerate()
        {
            if (frameratePreset == FrameratePreset.Custom)
                return Mathf.Clamp(customFramerate, 1, 240);
            return (int)frameratePreset;
        }

        public (int width, int height) GetTargetResolution(int gameViewWidth, int gameViewHeight)
        {
            int w, h;
            switch (resolutionPreset)
            {
                case ResolutionPreset.FHD_1080p:
                    w = 1920; h = 1080; break;
                case ResolutionPreset.QHD_1440p:
                    w = 2560; h = 1440; break;
                case ResolutionPreset.UHD_4K_2160p:
                    w = 3840; h = 2160; break;
                case ResolutionPreset.Custom:
                    w = Mathf.Max(128, customWidth);
                    h = Mathf.Max(128, customHeight);
                    break;
                case ResolutionPreset.MatchGameView:
                default:
                    w = gameViewWidth;
                    h = gameViewHeight;
                    break;
            }

            // Dimensions must be even for H.264 / YUV420p
            if (w % 2 != 0) w += 1;
            if (h % 2 != 0) h += 1;

            return (Mathf.Max(2, w), Mathf.Max(2, h));
        }

        public string GetContainerExtension()
        {
            switch (container)
            {
                case OutputContainer.MKV: return ".mkv";
                case OutputContainer.MOV: return ".mov";
                case OutputContainer.MP4:
                default: return ".mp4";
            }
        }
    }
}
