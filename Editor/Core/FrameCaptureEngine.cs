using System;
using System.Collections.Concurrent;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace HighQualityRecorder.Editor
{
    /// <summary>
    /// Captures rendered frames from Camera / RenderPipeline using AsyncGPUReadback without GPU stalls.
    /// Passes captured frame buffers to a dedicated worker thread feeding FFmpeg.
    /// </summary>
    public class FrameCaptureEngine : IDisposable
    {
        private readonly FFmpegEncoderProcess _encoder;
        private readonly int _width;
        private readonly int _height;
        private readonly int _targetFps;

        private RenderTexture _captureRt;
        private Thread _workerThread;
        private readonly BlockingCollection<byte[]> _frameQueue = new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>(), 30);
        private readonly ConcurrentBag<byte[]> _bufferPool = new ConcurrentBag<byte[]>();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private bool _isCapturing = false;
        private bool _isDisposed = false;

        private int _frameByteSize;
        private int _recordedFrames = 0;
        private int _droppedFrames = 0;

        public int RecordedFrames => _recordedFrames;
        public int DroppedFrames => _droppedFrames;
        public bool IsCapturing => _isCapturing;

        public FrameCaptureEngine(FFmpegEncoderProcess encoder, int width, int height, int targetFps)
        {
            _encoder = encoder;
            _width = width;
            _height = height;
            _targetFps = targetFps;
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

            // Create temporary capture render target
            _captureRt = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "[HighQualityRecorder_CaptureRT]"
            };
            _captureRt.Create();

            // Hook render callbacks for both Built-in and Scriptable Render Pipeline (URP/HDRP)
            RenderPipelineManager.endCameraRendering += OnEndCameraRenderingSRP;
            Camera.onPostRender += OnPostRenderBuiltIn;

            // Start background worker thread
            _workerThread = new Thread(WorkerLoop)
            {
                Name = "HighQualityRecorder_FFmpegWorker",
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.AboveNormal
            };
            _workerThread.Start();
        }

        private void OnPostRenderBuiltIn(Camera cam)
        {
            if (!_isCapturing) return;
            if (ShouldCaptureCamera(cam))
            {
                CaptureFromActiveTexture(cam.activeTexture);
            }
        }

        private void OnEndCameraRenderingSRP(ScriptableRenderContext context, Camera cam)
        {
            if (!_isCapturing) return;
            if (ShouldCaptureCamera(cam))
            {
                CaptureFromActiveTexture(cam.activeTexture);
            }
        }

        private bool ShouldCaptureCamera(Camera cam)
        {
            if (cam == null) return false;
            // Ignore preview and reflection cameras
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection)
                return false;

            // Priority: Main camera
            if (cam == Camera.main) return true;

            // If no MainCamera tagged, capture Game camera
            if (cam.cameraType == CameraType.Game) return true;

            return false;
        }

        private void CaptureFromActiveTexture(RenderTexture src)
        {
            if (!_isCapturing || _captureRt == null) return;

            // Blit current camera output into our capture RenderTexture
            if (src != null)
            {
                Graphics.Blit(src, _captureRt);
            }
            else
            {
                // Active backbuffer
                Graphics.Blit(null, _captureRt);
            }

            // Async GPU readback request (Non-blocking!)
            AsyncGPUReadback.Request(_captureRt, 0, TextureFormat.RGBA32, OnReadbackComplete);
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
            if (_frameQueue.TryAdd(frameBuffer))
            {
                Interlocked.Increment(ref _recordedFrames);
            }
            else
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
                while (!token.IsCancellationRequested || _frameQueue.Count > 0)
                {
                    if (_frameQueue.TryTake(out byte[] frame, 50, token))
                    {
                        if (frame != null)
                        {
                            _encoder.WriteFrame(frame, 0, frame.Length);
                            // Return buffer to pool for reuse
                            _bufferPool.Add(frame);
                        }
                    }
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

        public void UnhookRenderCallbacks()
        {
            if (!_isCapturing) return;
            _isCapturing = false;

            RenderPipelineManager.endCameraRendering -= OnEndCameraRenderingSRP;
            Camera.onPostRender -= OnPostRenderBuiltIn;

            if (_captureRt != null)
            {
                _captureRt.Release();
                UnityEngine.Object.DestroyImmediate(_captureRt);
                _captureRt = null;
            }
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
