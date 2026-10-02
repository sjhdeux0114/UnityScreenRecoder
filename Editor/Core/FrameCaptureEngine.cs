using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace HighQualityRecorder.Editor
{
    /// <summary>
    /// Captures rendered frames from Camera / RenderPipeline using AsyncGPUReadback without GPU stalls.
    /// Utilizes a triple-buffered RenderTexture ring to eliminate GPU write-read race conditions and screen flickering.
    /// Hooks endFrameRendering for Scriptable Render Pipelines (URP/HDRP) to ensure all camera stacks and UI overlays
    /// are fully composited before capture with uniform orientation.
    /// </summary>
    public class FrameCaptureEngine : IDisposable
    {
        private readonly FFmpegEncoderProcess _encoder;
        private readonly int _width;
        private readonly int _height;
        private readonly int _targetFps;
        private readonly CaptureTimingMode _timingMode;
        private readonly double _frameInterval;

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private double _nextCaptureTime = 0;
        private int _lastCapturedEngineFrame = -1;

        // Triple-buffered RenderTexture ring to eliminate GPU race conditions (flickering)
        private const int RING_BUFFER_SIZE = 3;
        private RenderTexture[] _ringRts;
        private int _ringIndex = 0;

        private Thread _workerThread;
        private readonly BlockingCollection<byte[]> _frameQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 30);
        private readonly ConcurrentBag<byte[]> _bufferPool = new ConcurrentBag<byte[]>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private bool _isCapturing = false;
        private bool _isDisposed = false;
        private bool _isSRPAvailable = false;

        private int _frameByteSize;
        private int _recordedFrames = 0;
        private int _droppedFrames = 0;

        public int RecordedFrames => _recordedFrames;
        public int DroppedFrames => _droppedFrames;
        public bool IsCapturing => _isCapturing;

        public FrameCaptureEngine(FFmpegEncoderProcess encoder, int width, int height, int targetFps, CaptureTimingMode timingMode)
        {
            _encoder = encoder;
            _width = width;
            _height = height;
            _targetFps = targetFps;
            _timingMode = timingMode;
            _frameInterval = 1.0 / Mathf.Max(1, _targetFps);
            _frameByteSize = _width * _height * 4; // RGBA32: 4 bytes per pixel

            // Pre-allocate buffer pool to eliminate GC allocations during recording
            for (int i = 0; i < 15; i++)
            {
                _bufferPool.Add(new byte[_frameByteSize]);
            }
        }

        public void Start()
        {
            if (_isCapturing) return;
            _isCapturing = true;

            _stopwatch.Restart();
            _nextCaptureTime = 0;
            _lastCapturedEngineFrame = -1;

            // Initialize triple-buffered RenderTexture ring
            InitializeRingBuffers();

            // Detect Scriptable Render Pipeline (URP / HDRP)
            _isSRPAvailable = GraphicsSettings.currentRenderPipeline != null || QualitySettings.renderPipeline != null;

            if (_isSRPAvailable)
            {
                // CRITICAL FOR URP / HDRP:
                // Hook endFrameRendering rather than endCameraRendering.
                // endFrameRendering is called ONCE per engine frame after ALL cameras (Base + Overlay stacks, Post-Processing)
                // and UI passes are completely finished. This completely prevents interleaving, camera fight, and flip flicker.
                RenderPipelineManager.endFrameRendering += OnEndFrameRenderingSRP;
            }
            else
            {
                // Hook Built-in Render Pipeline camera completion
                Camera.onPostRender += OnPostRenderBuiltIn;
            }

            // Start background worker thread feeding FFmpeg
            _workerThread = new Thread(WorkerLoop)
            {
                Name = "HighQualityRecorder_FFmpegWorker",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.AboveNormal
            };
            _workerThread.Start();
        }

        private void InitializeRingBuffers()
        {
            _ringRts = new RenderTexture[RING_BUFFER_SIZE];
            for (int i = 0; i < RING_BUFFER_SIZE; i++)
            {
                _ringRts[i] = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = $"[HighQualityRecorder_RingRT_{i}]"
                };
                _ringRts[i].Create();
            }
            _ringIndex = 0;
        }

        private void ReleaseRingBuffers()
        {
            try
            {
                // Ensure all in-flight GPU readbacks complete cleanly before destroying textures
                AsyncGPUReadback.WaitAllRequests();
            }
            catch { }

            if (_ringRts != null)
            {
                for (int i = 0; i < _ringRts.Length; i++)
                {
                    if (_ringRts[i] != null)
                    {
                        _ringRts[i].Release();
                        UnityEngine.Object.DestroyImmediate(_ringRts[i]);
                        _ringRts[i] = null;
                    }
                }
                _ringRts = null;
            }
        }

        private void OnEndFrameRenderingSRP(ScriptableRenderContext context, Camera[] cameras)
        {
            if (!_isCapturing) return;
            if (cameras == null || cameras.Length == 0) return;

            // Ensure at least one game or main camera was rendered in this frame
            bool hasValidGameCamera = false;
            for (int i = 0; i < cameras.Length; i++)
            {
                var cam = cameras[i];
                if (cam != null && (cam.cameraType == CameraType.Game || cam == Camera.main))
                {
                    hasValidGameCamera = true;
                    break;
                }
            }
            if (!hasValidGameCamera) return;

            if (CheckFrameRateTiming())
            {
                CaptureActiveFrame();
            }
        }

        private void OnPostRenderBuiltIn(Camera cam)
        {
            if (!_isCapturing) return;
            if (_isSRPAvailable) return; // Ignore built-in callbacks if SRP is handling capture

            if (ShouldCaptureCameraBuiltIn(cam) && CheckFrameRateTiming())
            {
                CaptureActiveFrame();
            }
        }

        private bool ShouldCaptureCameraBuiltIn(Camera cam)
        {
            if (cam == null) return false;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection)
                return false;

            // Priority: Main camera
            if (Camera.main != null)
            {
                return cam == Camera.main;
            }

            // Fallback: Game camera
            return cam.cameraType == CameraType.Game;
        }

        private bool CheckFrameRateTiming()
        {
            // CRITICAL: Prevent capturing more than once within the EXACT SAME engine frame.
            // This guarantees zero duplicate/alternating camera captures even under complex rendering pipelines.
            int currentEngineFrame = Time.frameCount;
            if (currentEngineFrame == _lastCapturedEngineFrame)
            {
                return false;
            }

            if (_timingMode == CaptureTimingMode.ConstantFramerate)
            {
                _lastCapturedEngineFrame = currentEngineFrame;
                return true;
            }

            // Realtime mode: throttle strictly according to targetFps interval (e.g. 1/30s, 1/60s, 1/120s)
            double currentTime = _stopwatch.Elapsed.TotalSeconds;
            if (currentTime < _nextCaptureTime)
            {
                return false;
            }

            _lastCapturedEngineFrame = currentEngineFrame;
            _nextCaptureTime += _frameInterval;

            // Prevent runaway catch-up if the engine had a momentary freeze
            if (currentTime - _nextCaptureTime > _frameInterval * 2)
            {
                _nextCaptureTime = currentTime + _frameInterval;
            }

            return true;
        }

        private void CaptureActiveFrame()
        {
            if (!_isCapturing || _ringRts == null || _isDisposed) return;

            // Round-robin selection of ring buffer target
            var targetRt = _ringRts[_ringIndex];
            _ringIndex = (_ringIndex + 1) % RING_BUFFER_SIZE;

            bool captured = false;
            try
            {
                // Primary capture method: ScreenCapture.CaptureScreenshotIntoRenderTexture
                // This captures the fully composed Game View backbuffer with all camera stacks,
                // post-processing, and screen-space overlay UI, with consistent graphics API orientation (no upside-down flipping).
                ScreenCapture.CaptureScreenshotIntoRenderTexture(targetRt);
                captured = true;
            }
            catch
            {
                // Fallback if ScreenCapture is temporarily unavailable in current editor state
                captured = false;
            }

            if (!captured)
            {
                // Safe Fallback: Blit from active render texture
                Graphics.Blit(RenderTexture.active, targetRt);
            }

            // Async GPU readback request (Non-blocking!)
            AsyncGPUReadback.Request(targetRt, 0, TextureFormat.RGBA32, OnReadbackComplete);
        }

        private void OnReadbackComplete(AsyncGPUReadbackRequest request)
        {
            if (!_isCapturing || _isDisposed) return;

            if (request.hasError)
            {
                Interlocked.Increment(ref _droppedFrames);
                return;
            }

            NativeArray<byte> rawData = request.GetData<byte>();
            if (!rawData.IsCreated || rawData.Length != _frameByteSize)
            {
                return;
            }

            // Get buffer from pool or allocate new if starved
            if (!_bufferPool.TryTake(out byte[] frameBuffer) || frameBuffer.Length != _frameByteSize)
            {
                frameBuffer = new byte[_frameByteSize];
            }

            // Copy native array to managed buffer
            rawData.CopyTo(frameBuffer);

            // Enqueue to background worker. If queue is full, drop frame to avoid runaway memory.
            if (!_frameQueue.TryAdd(frameBuffer))
            {
                Interlocked.Increment(ref _droppedFrames);
                _bufferPool.Add(frameBuffer);
            }
        }

        private void WorkerLoop()
        {
            var token = _cts.Token;
            try
            {
                if (_timingMode == CaptureTimingMode.Realtime)
                {
                    RunRealtimeWorker(token);
                }
                else
                {
                    RunConstantFramerateWorker(token);
                }
            }
            catch (OperationCanceledException)
            {
                // Clean exit requested
            }
            catch (Exception ex)
            {
                Debug.LogError($"[HighQualityRecorder] Exception in worker loop: {ex.Message}");
            }
        }

        private void RunRealtimeWorker(CancellationToken token)
        {
            byte[] activeFrame = null;
            var stopwatch = Stopwatch.StartNew();
            long frameIndex = 0;
            double targetIntervalSec = 1.0 / Mathf.Max(1, _targetFps);

            // Wait until the first frame is captured by Unity
            while (!token.IsCancellationRequested && activeFrame == null)
            {
                if (_frameQueue.TryTake(out byte[] firstFrame, 50, token))
                {
                    activeFrame = new byte[_frameByteSize];
                    Buffer.BlockCopy(firstFrame, 0, activeFrame, 0, _frameByteSize);
                    _bufferPool.Add(firstFrame);
                    break;
                }
            }

            if (activeFrame == null) return;

            stopwatch.Restart();

            while (!token.IsCancellationRequested)
            {
                double scheduledTimeSec = frameIndex * targetIntervalSec;
                double elapsedSec = stopwatch.Elapsed.TotalSeconds;

                if (elapsedSec < scheduledTimeSec)
                {
                    double waitMs = (scheduledTimeSec - elapsedSec) * 1000.0;
                    if (waitMs > 2.0)
                    {
                        Thread.Sleep((int)(waitMs - 1.0));
                    }
                    else
                    {
                        Thread.SpinWait(20);
                    }
                    continue;
                }

                // Pick up the latest available frame from the queue
                while (_frameQueue.TryTake(out byte[] newFrame))
                {
                    Buffer.BlockCopy(newFrame, 0, activeFrame, 0, _frameByteSize);
                    _bufferPool.Add(newFrame);
                }

                // Feed frame to FFmpeg (even if new frame didn't arrive, activeFrame will duplicate to maintain exact playback speed)
                _encoder.WriteFrame(activeFrame, 0, _frameByteSize);
                frameIndex++;
                _recordedFrames = (int)frameIndex;
            }

            // Flush remaining queue at exit
            while (_frameQueue.TryTake(out byte[] remainingFrame))
            {
                Buffer.BlockCopy(remainingFrame, 0, activeFrame, 0, _frameByteSize);
                _bufferPool.Add(remainingFrame);
            }
            if (activeFrame != null)
            {
                _encoder.WriteFrame(activeFrame, 0, _frameByteSize);
            }
        }

        private void RunConstantFramerateWorker(CancellationToken token)
        {
            while (!token.IsCancellationRequested || _frameQueue.Count > 0)
            {
                if (_frameQueue.TryTake(out byte[] frame, 50, token))
                {
                    if (frame != null)
                    {
                        _encoder.WriteFrame(frame, 0, frame.Length);
                        _bufferPool.Add(frame);
                        Interlocked.Increment(ref _recordedFrames);
                    }
                }
            }
        }

        public void UnhookRenderCallbacks()
        {
            if (!_isCapturing) return;
            _isCapturing = false;

            RenderPipelineManager.endFrameRendering -= OnEndFrameRenderingSRP;
            Camera.onPostRender -= OnPostRenderBuiltIn;

            ReleaseRingBuffers();
        }

        public void StopAndFlushWorker()
        {
            UnhookRenderCallbacks();

            // Signal cancellation and wait for remaining frames to flush
            _cts.Cancel();
            if (_workerThread != null && _workerThread.IsAlive)
            {
                _workerThread.Join(2000);
            }
        }

        public void Stop()
        {
            StopAndFlushWorker();
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopAndFlushWorker();
            _cts.Dispose();
            _frameQueue.Dispose();
        }
    }
}
