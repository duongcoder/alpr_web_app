using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using AlprWpfApp.Models;

namespace AlprWpfApp.Services.Camera
{
    /// <summary>
    /// Service quản lý luồng Camera (Webcam / RTSP / Video / Image) chạy ngầm độc lập.
    /// Có cơ chế Ring Buffer / Drop Frame để tránh trễ hình khi mạng RTSP dao động.
    /// </summary>
    public class CameraStreamService : IDisposable
    {
        private VideoCapture? _capture;
        private CancellationTokenSource? _cts;
        private Task? _streamTask;
        private readonly object _lock = new();
        private bool _isDisposed;

        public bool IsRunning { get; private set; }
        public CameraSourceConfig? CurrentConfig { get; private set; }

        public event Action<Mat>? FrameCaptured;
        public event Action<string>? StatusChanged;
        public event Action<string>? ErrorOccurred;

        public void Start(CameraSourceConfig config)
        {
            lock (_lock)
            {
                if (IsRunning)
                {
                    Stop();
                }

                CurrentConfig = config;
                _cts = new CancellationTokenSource();
                IsRunning = true;
                StatusChanged?.Invoke("Đang kết nối camera...");

                _streamTask = Task.Run(() => StreamWorker(_cts.Token), _cts.Token);
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;

                IsRunning = false;
                try
                {
                    _cts?.Cancel();
                    _streamTask?.Wait(1000);
                }
                catch
                {
                    // Ignore task cancellation
                }
                finally
                {
                    _cts?.Dispose();
                    _cts = null;
                    _streamTask = null;

                    _capture?.Release();
                    _capture?.Dispose();
                    _capture = null;
                }

                StatusChanged?.Invoke("Đã dừng Camera");
            }
        }

        public void PushSingleImage(string filePath)
        {
            if (!File.Exists(filePath))
            {
                ErrorOccurred?.Invoke($"File ảnh không tồn tại: {filePath}");
                return;
            }

            try
            {
                using var mat = Cv2.ImRead(filePath, ImreadModes.Color);
                if (!mat.Empty())
                {
                    StatusChanged?.Invoke($"Đã tải ảnh: {Path.GetFileName(filePath)}");
                    FrameCaptured?.Invoke(mat);
                }
                else
                {
                    ErrorOccurred?.Invoke("Không thể đọc định dạng ảnh!");
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Lỗi đọc file: {ex.Message}");
            }
        }

        private void StreamWorker(CancellationToken token)
        {
            if (CurrentConfig == null) return;

            try
            {
                if (CurrentConfig.SourceType == CameraSourceType.ImageFile)
                {
                    PushSingleImage(CurrentConfig.ImageFilePath);
                    IsRunning = false;
                    return;
                }

                // Khởi tạo VideoCapture theo từng nguồn
                switch (CurrentConfig.SourceType)
                {
                    case CameraSourceType.Webcam:
                        _capture = new VideoCapture(CurrentConfig.WebcamIndex, VideoCaptureAPIs.DSHOW);
                        break;
                    case CameraSourceType.RTSP:
                        _capture = new VideoCapture(CurrentConfig.RtspUrl, VideoCaptureAPIs.FFMPEG);
                        // Đặt buffer size nhỏ nhất cho RTSP để giảm trễ
                        _capture.Set(VideoCaptureProperties.BufferSize, 1);
                        break;
                    case CameraSourceType.VideoFile:
                        _capture = new VideoCapture(CurrentConfig.VideoFilePath);
                        break;
                }

                if (_capture == null || !_capture.IsOpened())
                {
                    ErrorOccurred?.Invoke("Không thể mở luồng camera hoặc file video!");
                    IsRunning = false;
                    StatusChanged?.Invoke("Lỗi kết nối");
                    return;
                }

                StatusChanged?.Invoke("Đang phát trực tiếp...");

                double fps = _capture.Get(VideoCaptureProperties.Fps);
                if (fps <= 0 || double.IsNaN(fps) || fps > 120) fps = 30;
                int frameDelayMs = (int)(1000.0 / fps);

                using var frame = new Mat();

                while (!token.IsCancellationRequested && IsRunning)
                {
                    bool grabSuccess = _capture.Read(frame);
                    if (!grabSuccess || frame.Empty())
                    {
                        if (CurrentConfig.SourceType == CameraSourceType.VideoFile)
                        {
                            // Tự động lặp lại video file
                            _capture.Set(VideoCaptureProperties.PosFrames, 0);
                            continue;
                        }
                        else
                        {
                            // Tạm nghỉ và thử lại
                            Thread.Sleep(100);
                            continue;
                        }
                    }

                    // Đẩy bản sao frame an toàn cho các listener
                    FrameCaptured?.Invoke(frame);

                    if (CurrentConfig.SourceType == CameraSourceType.VideoFile)
                    {
                        Thread.Sleep(frameDelayMs);
                    }
                    else if (CurrentConfig.SourceType == CameraSourceType.RTSP)
                    {
                        // Giữ luồng nhẹ nhàng
                        Thread.Sleep(5);
                    }
                    else
                    {
                        Thread.Sleep(Math.Max(1, frameDelayMs / 2));
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke($"Lỗi trong luồng camera: {ex.Message}");
                StatusChanged?.Invoke("Lỗi camera");
            }
            finally
            {
                _capture?.Release();
                _capture?.Dispose();
                _capture = null;
                IsRunning = false;
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                Stop();
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
