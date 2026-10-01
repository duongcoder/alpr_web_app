using System;
using OpenCvSharp;

namespace AlprSdk.Models
{
    /// <summary>
    /// Kết quả hoàn chỉnh sau khi chạy toàn bộ pipeline nhận diện biển số xe (ALPR)
    /// </summary>
    public class PlateRecognitionResult : IDisposable
    {
        public bool IsSuccess { get; set; }
        public string PlateNumber { get; set; } = "Không nhận diện được";
        public string RawPlateText { get; set; } = string.Empty;
        public string VehicleType { get; set; } = "Không xác định";
        public string PlateColor { get; set; } = "Trắng";
        public float DetectionConfidence { get; set; }
        public double YoloInferenceMs { get; set; }
        public double ParseqInferenceMs { get; set; }
        public double TotalProcessingMs { get; set; }
        public string EngineUsed { get; set; } = "YOLOv8 + PARSeq ONNX (< 80ms)";
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public Rect? BoundingBox { get; set; }
        public Mat? PlateCropMat { get; set; }
        public bool IsValid { get; set; }

        public static PlateRecognitionResult Failed(string reason = "Không phát hiện biển số")
        {
            return new PlateRecognitionResult
            {
                IsSuccess = false,
                PlateNumber = reason,
                VehicleType = "Không xác định",
                PlateColor = "Trắng",
                EngineUsed = "N/A",
                TotalProcessingMs = 0
            };
        }

        public void Dispose()
        {
            PlateCropMat?.Dispose();
            PlateCropMat = null;
            GC.SuppressFinalize(this);
        }
    }
}
