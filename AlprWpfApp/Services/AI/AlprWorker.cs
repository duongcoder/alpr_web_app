using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using OpenCvSharp;
using AlprWpfApp.Models;

namespace AlprWpfApp.Services.AI
{
    /// <summary>
    /// AI Worker xử lý suy luận ngầm bằng Channel không chặn (DropOldest).
    /// Đảm bảo UI luôn đạt 60 FPS mượt mà và không bao giờ bị đơ (Zero UI Freezing).
    /// </summary>
    public class AlprWorker : IDisposable
    {
        private readonly AlprPipelineEngine _engine;
        private readonly Channel<Mat> _inferenceQueue;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processingTask;
        private bool _isDisposed;

        public bool IsAutoInference { get; set; } = true;
        public float ConfidenceThreshold { get; set; } = 0.01f;

        public event Action<PlateRecognitionResult>? PlateRecognized;

        public AlprWorker(AlprPipelineEngine engine)
        {
            _engine = engine;

            // BoundedChannel dung lượng 1: Luôn xử lý frame mới nhất và bỏ qua frame cũ khi AI đang bận
            var options = new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            };
            _inferenceQueue = Channel.CreateBounded<Mat>(options);

            _processingTask = Task.Run(() => ProcessingLoopAsync(_cts.Token));
        }

        public void EnqueueFrame(Mat frame, bool isManualTrigger = false)
        {
            if (_isDisposed || frame == null || frame.IsDisposed || frame.Empty())
                return;

            if (isManualTrigger || IsAutoInference)
            {
                // Clone frame để tránh xung đột giữa luồng camera và luồng AI
                var clone = frame.Clone();
                if (!_inferenceQueue.Writer.TryWrite(clone))
                {
                    clone.Dispose();
                }
            }
        }

        private async Task ProcessingLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && await _inferenceQueue.Reader.WaitToReadAsync(token))
            {
                while (_inferenceQueue.Reader.TryRead(out var frame))
                {
                    try
                    {
                        using (frame)
                        {
                            var result = _engine.ProcessFrame(frame, ConfidenceThreshold);
                            PlateRecognized?.Invoke(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AlprWorker Error]: {ex.Message}");
                    }
                }
            }
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                _cts.Cancel();
                _cts.Dispose();
                _engine.Dispose();
            }
            GC.SuppressFinalize(this);
        }
    }
}
