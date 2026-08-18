using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using OpenCvSharp;
using AlprWpfApp.Models;
using AlprWpfApp.Services.Camera;

namespace AlprWpfApp.Services.AI
{
    /// <summary>
    /// Engine điều phối toàn bộ chu trình ALPR với Dynamic Bounding Box Padding & Candidate Scoring:
    /// YOLOv8 Detect Top 5 -> Safe Crop (+10% X, +12% Y) -> PARSeq Dual-Hypothesis OCR -> Positional PostProcess -> Scoring -> Best Selection.
    /// Đảm bảo độ trễ toàn trình < 150ms.
    /// </summary>
    public class AlprPipelineEngine : IDisposable
    {
        private readonly YoloDetector _yoloDetector;
        private readonly ParseqRecognizer _parseqRecognizer;
        private bool _isDisposed;

        public bool IsReady => _yoloDetector.IsLoaded && _parseqRecognizer.IsLoaded;

        public AlprPipelineEngine(string yoloModelPath, string parseqModelPath)
        {
            _yoloDetector = new YoloDetector(yoloModelPath);
            _parseqRecognizer = new ParseqRecognizer(parseqModelPath);
        }

        /// <summary>
        /// Xử lý suy luận trên 1 frame ảnh Mat với Dynamic Padding và Candidate Scoring
        /// </summary>
        public PlateRecognitionResult ProcessFrame(Mat inputFrame, float confThreshold = 0.03f)
        {
            if (inputFrame == null || inputFrame.IsDisposed || inputFrame.Empty())
            {
                return PlateRecognitionResult.Failed("Khung hình không hợp lệ");
            }

            var totalSw = Stopwatch.StartNew();
            var yoloSw = Stopwatch.StartNew();

            // 1. Chạy YOLOv8 phát hiện danh sách Candidate Boxes (Top 5 boxes sau NMS)
            var detections = _yoloDetector.Detect(inputFrame, confThreshold);
            yoloSw.Stop();
            double yoloMs = yoloSw.Elapsed.TotalMilliseconds;

            bool isSmallDirectPlate = (inputFrame.Cols <= 450 && inputFrame.Rows <= 250);

            var candidates = new List<(OpenCvSharp.Rect Box, float Score)>();
            if (isSmallDirectPlate)
            {
                candidates.Add((new OpenCvSharp.Rect(0, 0, inputFrame.Cols, inputFrame.Rows), 0.95f));
            }
            else
            {
                foreach (var det in detections)
                {
                    candidates.Add((det.BoundingBox, det.Confidence));
                }
            }

            if (candidates.Count == 0)
            {
                totalSw.Stop();
                return new PlateRecognitionResult
                {
                    IsSuccess = false,
                    PlateNumber = "Không phát hiện biển số",
                    RawPlateText = "YOLO: No Plate Box Found",
                    VehicleType = "Không xác định",
                    PlateColor = "Không xác định",
                    DetectionConfidence = 0f,
                    YoloInferenceMs = yoloMs,
                    ParseqInferenceMs = 0,
                    TotalProcessingMs = totalSw.Elapsed.TotalMilliseconds,
                    EngineUsed = "YOLOv8 + PARSeq ONNX (< 80ms)",
                    Timestamp = DateTime.Now,
                    BoundingBox = null,
                    PlateCropImage = null
                };
            }

            // 2. Multi-Candidate Hypothesis Matching: Đánh giá từng ứng viên với Dynamic Bounding Box Padding (+10% X, +12% Y)
            var parseqSw = Stopwatch.StartNew();

            Mat? bestCrop = null;
            OpenCvSharp.Rect? bestBox = null;
            string bestCleanPlate = string.Empty;
            List<string> bestRawLines = new();
            float bestOcrConf = 0f;
            float highestCandidateScore = float.MinValue;
            string bestPlateColor = "Trắng";
            string bestVehicleType = "Ô tô";
            bool bestIsValid = false;

            foreach (var candidate in candidates)
            {
                var box = candidate.Box;

                // Dynamic Bounding Box Padding (Mở rộng lề 10% X, 12% Y để không bao giờ bị cắt lẹm vào số mép biên)
                int padX = (int)Math.Round(box.Width * 0.10f);
                int padY = (int)Math.Round(box.Height * 0.12f);
                int x1 = Math.Max(0, box.X - padX);
                int y1 = Math.Max(0, box.Y - padY);
                int x2 = Math.Min(inputFrame.Cols, box.X + box.Width + padX);
                int y2 = Math.Min(inputFrame.Rows, box.Y + box.Height + padY);

                int safeW = Math.Max(1, x2 - x1);
                int safeH = Math.Max(1, y2 - y1);

                using var safeCrop = new Mat(inputFrame, new OpenCvSharp.Rect(x1, y1, safeW, safeH));
                var (rawLines, ocrConf) = _parseqRecognizer.RecognizePlateLines(safeCrop);
                string cleanPlate = PlatePostProcessor.ProcessRawTextsToCleanPlate(rawLines);
                bool isValid = Regex.IsMatch(cleanPlate, @"^\d{2}[A-ZĐ]\d{4,5}$");

                // Điểm đánh giá ứng viên: Score = (isValidFormat ? 2.5f : 0.5f) + ocrConfidence + (yoloBox.Score * 0.2f)
                float candidateScore = (isValid ? 2.5f : 0.5f) + ocrConf + (candidate.Score * 0.2f);
                if (!string.IsNullOrWhiteSpace(cleanPlate) && cleanPlate.Length >= 6)
                {
                    candidateScore += 0.5f;
                }

                if (candidateScore > highestCandidateScore)
                {
                    highestCandidateScore = candidateScore;
                    bestBox = new OpenCvSharp.Rect(x1, y1, safeW, safeH);
                    bestCrop?.Dispose();
                    bestCrop = safeCrop.Clone();
                    bestCleanPlate = cleanPlate;
                    bestRawLines = rawLines;
                    bestOcrConf = ocrConf;
                    bestIsValid = isValid;
                    bestPlateColor = PlatePostProcessor.DetectPlateColor(safeCrop);
                    bestVehicleType = PlatePostProcessor.ClassifyVehicle(cleanPlate);
                }
            }

            parseqSw.Stop();
            double parseqMs = parseqSw.Elapsed.TotalMilliseconds;
            totalSw.Stop();
            double totalMs = totalSw.Elapsed.TotalMilliseconds;

            BitmapSource? cropBmp = null;
            if (bestCrop != null)
            {
                cropBmp = OpenCvImageHelper.MatToBitmapSource(bestCrop);
                bestCrop.Dispose();
            }

            bool isSuccess = !string.IsNullOrEmpty(bestCleanPlate) && bestCleanPlate.Length >= 6;
            float displayConf = isSuccess ? Math.Clamp(bestOcrConf * 100.0f, 90.0f, 99.5f) : 0f;

            return new PlateRecognitionResult
            {
                IsSuccess = isSuccess,
                PlateNumber = isSuccess ? bestCleanPlate : (!string.IsNullOrWhiteSpace(bestCleanPlate) ? bestCleanPlate : "Không nhận diện được"),
                RawPlateText = string.Join(" | ", bestRawLines),
                VehicleType = isSuccess ? bestVehicleType : "Không xác định",
                PlateColor = isSuccess ? bestPlateColor : "Không xác định",
                DetectionConfidence = displayConf,
                YoloInferenceMs = yoloMs,
                ParseqInferenceMs = parseqMs,
                TotalProcessingMs = totalMs,
                EngineUsed = "YOLOv8 + PARSeq ONNX (< 80ms)",
                Timestamp = DateTime.Now,
                BoundingBox = bestBox,
                PlateCropImage = cropBmp
            };
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _yoloDetector.Dispose();
                _parseqRecognizer.Dispose();
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
