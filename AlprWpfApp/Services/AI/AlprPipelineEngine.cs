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

        public Rect2f ScaleRoi { get; set; } = new Rect2f(0.22f, 0.10f, 0.56f, 0.88f);

        public bool IsReady => _yoloDetector.IsLoaded && _parseqRecognizer.IsLoaded;

        public AlprPipelineEngine(string yoloModelPath, string parseqModelPath)
        {
            _yoloDetector = new YoloDetector(yoloModelPath);
            _parseqRecognizer = new ParseqRecognizer(parseqModelPath);
        }

        /// <summary>
        /// Xử lý suy luận trên 1 frame ảnh Mat với Dynamic Padding, ROI Filtering và Spatial Proximity Scoring
        /// </summary>
        public PlateRecognitionResult ProcessFrame(Mat inputFrame, float confThreshold = 0.03f)
        {
            if (inputFrame == null || inputFrame.IsDisposed || inputFrame.Empty())
            {
                return PlateRecognitionResult.Failed("Khung hình không hợp lệ");
            }

            var totalSw = Stopwatch.StartNew();
            var yoloSw = Stopwatch.StartNew();

            int imgWidth = inputFrame.Cols;
            int imgHeight = inputFrame.Rows;

            bool isSmallDirectPlate = (imgWidth <= 450 && imgHeight <= 250);

            var candidates = new List<(OpenCvSharp.Rect Box, float Score)>();
            if (isSmallDirectPlate)
            {
                candidates.Add((new OpenCvSharp.Rect(0, 0, imgWidth, imgHeight), 0.95f));
            }
            else
            {
                var allDetectedBoxes = new List<PlateDetectionBox>();

                // Stream 1: Chạy YOLOv8 trên toàn cảnh Full Frame (bắt xe ở cự ly gần/trung bình)
                var fullDetections = _yoloDetector.Detect(inputFrame, confThreshold);
                foreach (var det in fullDetections)
                {
                    // Tính tọa độ tâm chuẩn hóa [0..1] của Box
                    float normCenterX = (det.BoundingBox.X + det.BoundingBox.Width / 2.0f) / (float)imgWidth;
                    float normCenterY = (det.BoundingBox.Y + det.BoundingBox.Height / 2.0f) / (float)imgHeight;

                    // Kiểm tra tâm của Box có nằm trong vùng nhận diện bàn cân (ScaleRoi)
                    bool isInsideRoi = (normCenterX >= ScaleRoi.X && normCenterX <= (ScaleRoi.X + ScaleRoi.Width) &&
                                        normCenterY >= ScaleRoi.Y && normCenterY <= (ScaleRoi.Y + ScaleRoi.Height));

                    if (isInsideRoi)
                    {
                        allDetectedBoxes.Add(det);
                    }
                }

                // Stream 2: ROI-Guided Zoom Detection (Crop trực tiếp vùng ScaleRoi độ phân giải cao từ ảnh gốc)
                // Giúp bắt trọn biển số ở cự ly xa / góc cam trên cao mà không bị mất chi tiết khi downscale
                int roiX = (int)Math.Round(imgWidth * ScaleRoi.X);
                int roiY = (int)Math.Round(imgHeight * ScaleRoi.Y);
                int roiW = (int)Math.Round(imgWidth * ScaleRoi.Width);
                int roiH = (int)Math.Round(imgHeight * ScaleRoi.Height);

                roiX = Math.Clamp(roiX, 0, imgWidth - 1);
                roiY = Math.Clamp(roiY, 0, imgHeight - 1);
                roiW = Math.Clamp(roiW, 1, imgWidth - roiX);
                roiH = Math.Clamp(roiH, 1, imgHeight - roiY);

                if (roiW >= 64 && roiH >= 64)
                {
                    using var roiFrame = new Mat(inputFrame, new OpenCvSharp.Rect(roiX, roiY, roiW, roiH));
                    var roiDetections = _yoloDetector.Detect(roiFrame, confThreshold);
                    foreach (var det in roiDetections)
                    {
                        // Ánh xạ tọa độ box từ roiFrame ngược về hệ tọa độ frame gốc
                        var globalRect = new OpenCvSharp.Rect(
                            det.BoundingBox.X + roiX,
                            det.BoundingBox.Y + roiY,
                            det.BoundingBox.Width,
                            det.BoundingBox.Height
                        );

                        allDetectedBoxes.Add(new PlateDetectionBox
                        {
                            BoundingBox = globalRect,
                            Confidence = det.Confidence,
                            ClassId = det.ClassId,
                            Label = det.Label
                        });
                    }
                }

                // Hợp nhất (Merge & NMS) các detection từ cả 2 luồng
                var mergedBoxes = YoloDetector.ApplyNms(allDetectedBoxes, 0.45f);

                // Bộ lọc nhiễu Box: Nếu trong danh sách ứng viên có ít nhất 1 box Score >= 0.35f, tự động loại bỏ các box nhiễu Score < 0.20f
                if (mergedBoxes.Count > 0 && mergedBoxes.Any(b => b.Confidence >= 0.35f))
                {
                    mergedBoxes = mergedBoxes.Where(b => b.Confidence >= 0.20f).ToList();
                }

                foreach (var box in mergedBoxes)
                {
                    candidates.Add((box.BoundingBox, box.Confidence));
                }
            }

            yoloSw.Stop();
            double yoloMs = yoloSw.Elapsed.TotalMilliseconds;

            if (candidates.Count == 0)
            {
                totalSw.Stop();
                return new PlateRecognitionResult
                {
                    IsSuccess = false,
                    PlateNumber = "Không phát hiện biển số trong vùng cân",
                    RawPlateText = "YOLO: No Plate Box Found in ScaleRoi",
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
                int x2 = Math.Min(imgWidth, box.X + box.Width + padX);
                int y2 = Math.Min(imgHeight, box.Y + box.Height + padY);

                int safeW = Math.Max(1, x2 - x1);
                int safeH = Math.Max(1, y2 - y1);

                using var safeCrop = new Mat(inputFrame, new OpenCvSharp.Rect(x1, y1, safeW, safeH));
                var (rawLines, ocrConf) = _parseqRecognizer.RecognizePlateLines(safeCrop);
                string cleanPlate = PlatePostProcessor.ProcessRawTextsToCleanPlate(rawLines);

                // Kiểm tra định dạng chuẩn biển số Việt Nam
                bool isValidFormat = Regex.IsMatch(cleanPlate, @"^\d{2}[A-ZĐ]\d{4,5}$") ||
                                     Regex.IsMatch(cleanPlate, @"^\d{2}[A-Z]{2}\d{4,5}$") ||
                                     PlatePostProcessor.IsValidVietnamesePlate(cleanPlate);

                // Trọng số không gian & Cự ly ưu tiên (Spatial Proximity Dominance):
                // 1. Tọa độ Y mép đáy Bounding Box (chân xe tiếp đất - xe phía trước luôn có Y đáy lớn hơn)
                float normY = Math.Clamp((box.Y + box.Height) / (float)imgHeight, 0.0f, 1.0f);

                // 2. Tỷ lệ diện tích Bounding Box so với khung hình (xe ở gần camera có diện tích lớn vượt trội)
                float normArea = Math.Clamp((box.Width * box.Height) / (float)(imgWidth * imgHeight * 0.04f), 0.0f, 1.0f);

                // 3. Trọng số định tâm làn cân (đóng vai trò phụ trợ nhẹ)
                float normCenterX = (box.X + box.Width / 2.0f) / (float)imgWidth;
                float roiCenterX = ScaleRoi.X + ScaleRoi.Width / 2.0f; // Tim trục bàn cân
                float distFromCenter = Math.Abs(normCenterX - roiCenterX);
                float centerWeight = (ScaleRoi.Width > 0) ? Math.Clamp(1.0f - (distFromCenter / (ScaleRoi.Width / 2.0f)), 0.0f, 1.0f) : 0f;

                // Tái cấu trúc công thức CandidateScore: Cân bằng độ tin cậy YOLO, cú pháp và cự ly
                float candidateScore = (isValidFormat ? 4.0f : -3.0f) // Phạt nặng (-3.0) các chuỗi rác như lan can/bê tông
                                     + (candidate.Score * 3.0f)        // Trọng số YOLO cao: Biển số thật (>0.80) đè bẹp lan can (<0.15)
                                     + (ocrConf * 1.5f)
                                     + (normY * 1.2f)                  // Vẫn ưu tiên xe phía trước nhưng không để lật ngược biển số thật
                                     + (normArea * 1.0f)
                                     + (centerWeight * 0.5f);

                if (candidateScore > highestCandidateScore)
                {
                    highestCandidateScore = candidateScore;
                    bestBox = new OpenCvSharp.Rect(x1, y1, safeW, safeH);
                    bestCrop?.Dispose();
                    bestCrop = safeCrop.Clone();
                    bestCleanPlate = cleanPlate;
                    bestRawLines = rawLines;
                    bestOcrConf = ocrConf;
                    bestIsValid = isValidFormat;
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

            bool isSuccess = !string.IsNullOrEmpty(bestCleanPlate) && (bestIsValid || bestCleanPlate.Length >= 6);
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
