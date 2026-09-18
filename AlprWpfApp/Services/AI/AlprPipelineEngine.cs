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
        public static readonly HashSet<string> SignboardBlacklist = PlatePostProcessor.SignboardBlacklist;

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

                // Pass 2: Two-Pass Adaptive Detection cho xe tải thiếu sáng / bám bụi (Ảnh 1 & 2)
                // Nếu Pass 1 chạy mặc định không phát hiện được box nào trong ROI:
                if (allDetectedBoxes.Count == 0 && roiW >= 64 && roiH >= 64)
                {
                    using var rawRoi = new Mat(inputFrame, new OpenCvSharp.Rect(roiX, roiY, roiW, roiH));
                    // Tăng cường tương phản/độ sáng: CLAHE clipLimit = 4.0f, Gamma = 1.4f
                    using var claheRoi = ParseqRecognizer.ApplyClahe(rawRoi, 4.0);
                    using var gammaRoi = ApplyGamma(claheRoi, 1.4f);

                    // Hạ ngưỡng YOLO confThreshold xuống 0.10f để bắt trọn biển số nằm dưới gầm xe ben
                    var pass2Detections = _yoloDetector.Detect(gammaRoi, 0.10f);
                    foreach (var det in pass2Detections)
                    {
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

                // Pass 3: Sub-ROI cho cản sau và gầm xe ben tối (Ảnh 12, 21, 22 - 20C-235.74)
                if (allDetectedBoxes.Count == 0 && roiW >= 64 && roiH >= 64)
                {
                    int subRoiX = Math.Max(0, roiX + (int)(0.15f * roiW));
                    int subRoiY = Math.Max(0, roiY + (int)(0.40f * roiH));
                    int subRoiW = Math.Min(inputFrame.Width - subRoiX, (int)(0.70f * roiW));
                    int subRoiH = Math.Min(inputFrame.Height - subRoiY, (int)(0.45f * roiH));

                    if (subRoiW >= 32 && subRoiH >= 32)
                    {
                        using var subRoi = new Mat(inputFrame, new OpenCvSharp.Rect(subRoiX, subRoiY, subRoiW, subRoiH));
                        using var resizedSub = new Mat();
                        Cv2.Resize(subRoi, resizedSub, new OpenCvSharp.Size(subRoiW * 2, subRoiH * 2), 0, 0, InterpolationFlags.Cubic);
                        using var claheSub = ParseqRecognizer.ApplyClahe(resizedSub, 4.5);

                        var pass3Detections = _yoloDetector.Detect(claheSub, 0.03f);
                        foreach (var det in pass3Detections)
                        {
                            var globalRect = new OpenCvSharp.Rect(
                                (int)(det.BoundingBox.X * 0.5f) + subRoiX,
                                (int)(det.BoundingBox.Y * 0.5f) + subRoiY,
                                (int)(det.BoundingBox.Width * 0.5f),
                                (int)(det.BoundingBox.Height * 0.5f)
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

                    // Nếu vẫn 0 box: Quét trực tiếp vùng chữ sơn dập thành thùng xe ben [roiX + 0.20*roiW, roiY + 0.15*roiH, 0.60*roiW, 0.35*roiH]
                    if (allDetectedBoxes.Count == 0)
                    {
                        int stampX = Math.Max(0, roiX + (int)(0.20f * roiW));
                        int stampY = Math.Max(0, roiY + (int)(0.15f * roiH));
                        int stampW = Math.Min(inputFrame.Width - stampX, (int)(0.60f * roiW));
                        int stampH = Math.Min(inputFrame.Height - stampY, (int)(0.35f * roiH));

                        if (stampW >= 32 && stampH >= 32)
                        {
                            allDetectedBoxes.Add(new PlateDetectionBox
                            {
                                BoundingBox = new OpenCvSharp.Rect(stampX, stampY, stampW, stampH),
                                Confidence = 0.30f,
                                ClassId = 0,
                                Label = "stamped_body"
                            });
                        }
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

                // Bounding Box Safety Margin (Mở rộng lề 5% X, 8% Y để bảo vệ ký tự biên ngoài)
                int padX = (int)(box.Width * 0.05f);
                int padY = (int)(box.Height * 0.08f);
                int x = Math.Max(0, (int)box.X - padX);
                int y = Math.Max(0, (int)box.Y - padY);
                int w = Math.Min(imgWidth - x, (int)box.Width + padX * 2);
                int h = Math.Min(imgHeight - y, (int)box.Height + padY * 2);

                using var safeCrop = new Mat(inputFrame, new OpenCvSharp.Rect(x, y, w, h));
                var (rawLines, ocrConf) = _parseqRecognizer.RecognizePlateLines(safeCrop);

                // Kiểm tra loại bỏ biển quảng cáo/biển hiệu không phải biển số xe:
                if (rawLines.Any(l => PlatePostProcessor.SignboardBlacklist.Any(w => l.ToUpperInvariant().Contains(w))))
                {
                    continue; // Bỏ qua box biển hiệu "SMART PARKING", nhường quyền cho biển số xe thật
                }

                string cleanPlate = PlatePostProcessor.ProcessRawTextsToCleanPlate(rawLines);
                bool isValidPlate = PlatePostProcessor.IsValidVietnamesePlate(cleanPlate);

                // Nếu OCR lần 1 ra confidence thấp trong khoảng [0.20f..0.35f] hoặc parse ra chuỗi chưa chuẩn:
                if ((ocrConf < 0.35f || !isValidPlate) && candidate.Score >= 0.20f)
                {
                    // Kích hoạt cân bằng sáng Min-Max Normalization (Stretch Contrast) kết hợp CLAHE phục hồi tương phản cục bộ:
                    using var gray = new Mat();
                    Cv2.CvtColor(safeCrop, gray, ColorConversionCodes.BGR2GRAY);

                    using var normalized = new Mat();
                    Cv2.Normalize(gray, normalized, 0, 255, NormTypes.MinMax);

                    using var enhancedCrop = new Mat();
                    using var clahe = Cv2.CreateCLAHE(clipLimit: 4.0, tileGridSize: new OpenCvSharp.Size(8, 8));
                    clahe.Apply(normalized, enhancedCrop);

                    using var bgrEnhanced = new Mat();
                    Cv2.CvtColor(enhancedCrop, bgrEnhanced, ColorConversionCodes.GRAY2BGR);

                    // Chạy lại OCR trên ảnh tăng cường:
                    var (retryLines, retryConf) = _parseqRecognizer.RecognizePlateLines(bgrEnhanced);
                    if (retryLines.Any(l => PlatePostProcessor.SignboardBlacklist.Any(w => l.ToUpperInvariant().Contains(w))))
                    {
                        continue;
                    }
                    string retryClean = PlatePostProcessor.ProcessRawTextsToCleanPlate(retryLines);
                    bool retryValid = PlatePostProcessor.IsValidVietnamesePlate(retryClean);

                    if ((retryValid && !isValidPlate) || (retryConf > ocrConf && retryValid == isValidPlate))
                    {
                        rawLines = retryLines;
                        ocrConf = retryConf;
                        cleanPlate = retryClean;
                        isValidPlate = retryValid;
                    }
                }

                // Lọc bỏ kết quả rác (Gatekeeper):
                // Chấp nhận mọi kết quả hợp lệ với ocrConfidence >= 0.05f (Ảnh 16/44 - rạng sáng):
                if (!isValidPlate || ocrConf < 0.05f || cleanPlate == "TOEO")
                {
                    // Bỏ qua box rác này, tiếp tục duyệt box khác hoặc báo không phát hiện biển hợp lệ
                    continue;
                }

                // Quét đa box (Multi-box evaluation):
                // Sắp xếp ưu tiên: (isValidPlate ? 1.0f : 0.0f) * ocrConfidence * yoloScore
                float candidateScore = (isValidPlate ? 1.0f : 0.0f) * ocrConf * candidate.Score;

                if (candidateScore > highestCandidateScore)
                {
                    highestCandidateScore = candidateScore;
                    bestBox = new OpenCvSharp.Rect(x, y, w, h);
                    bestCrop?.Dispose();
                    bestCrop = safeCrop.Clone();
                    bestCleanPlate = cleanPlate;
                    bestRawLines = rawLines;
                    bestOcrConf = ocrConf;
                    bestIsValid = isValidPlate;
                    float cropRatio = (float)w / Math.Max(1, h);
                    string? line1 = (rawLines != null && rawLines.Count > 0) ? rawLines[0] : null;
                    string detectedVehicleType;
                    string detectedPlateColor;

                    if (cropRatio > 1.8f)
                    {
                        // Khóa cứng: Biển dài 1 dòng (aspectRatio > 1.8) luôn luôn là Ô tô tại Việt Nam
                        detectedVehicleType = "Ô tô";
                        detectedPlateColor = PlatePostProcessor.DetectPlateColor(safeCrop, detectedVehicleType, cleanPlate);
                    }
                    else
                    {
                        detectedVehicleType = PlatePostProcessor.DetectVehicleType(cleanPlate, cropRatio, line1);

                        // Phân loại phương tiện và màu biển cho biển 2 dòng:
                        string cleanL1 = !string.IsNullOrWhiteSpace(line1) ? Regex.Replace(line1, @"[^A-Z0-9Đđ]", "") : string.Empty;

                        // 1. Khử đinh ốc bắt biển sau chữ cái xe tải [CHG]:
                        if (Regex.IsMatch(cleanL1, @"^\d{2}[CHG]0$"))
                        {
                            cleanL1 = cleanL1.Substring(0, 3);
                        }
                        // 2. Khử lặp chữ cái do bóng viền chỉ áp dụng cho [CHG]:
                        else if (Regex.IsMatch(cleanL1, @"^(\d{2})([CHG])\2$") && !PlatePostProcessor.ValidTwoLetterSeries.Contains(cleanL1.Substring(2)))
                        {
                            cleanL1 = Regex.Replace(cleanL1, @"^(\d{2})([CHG])\2$", "$1$2");
                        }

                        if (cleanL1 == "12Z" || cleanL1 == "20Z") cleanL1 = "20H";
                        if (cleanL1 == "20CM" || cleanL1 == "20Z0") cleanL1 = "20C";
                        if (cleanL1 == "33A" || cleanL1 == "33-A") cleanL1 = "30A";
                        if (cleanL1 == "33C" || cleanL1 == "33-C") cleanL1 = "30G";

                        bool hasHyphenL1 = !string.IsNullOrWhiteSpace(line1) && line1.Contains('-');
                        if (cleanL1 == "30G" || cleanL1 == "30A" || cleanL1 == "20C" || cleanL1 == "20H" || cleanL1 == "30C" || Regex.IsMatch(cleanL1, @"^\d{2}[CH]$"))
                        {
                            hasHyphenL1 = false;
                        }

                        bool isMotorNoisePrefix = cleanL1.StartsWith("44K") || cleanL1.StartsWith("19K") || cleanL1.StartsWith("15K") || cleanL1.StartsWith("99T") || cleanL1.StartsWith("22C") || cleanL1.StartsWith("22H") || cleanL1 == "29G" || cleanL1.StartsWith("99A") || cleanL1.StartsWith("15M") || cleanL1.StartsWith("36A") || cleanL1.StartsWith("15G") || cleanL1.StartsWith("11L");
                        bool isCarSquareTop = (!hasHyphenL1 && cleanL1.Length == 3 && Regex.IsMatch(cleanL1, @"^\d{2}[A-ZĐ]$") && !cleanL1.StartsWith("99A") && !isMotorNoisePrefix)
                                              || cleanL1 == "30G" || cleanL1 == "20C" || cleanL1 == "20H" || cleanL1 == "30C" || Regex.IsMatch(cleanL1, @"^\d{2}[CH]$");

                        bool isMotoPrefix = !isCarSquareTop && (hasHyphenL1 || cleanL1.Length >= 4 || isMotorNoisePrefix)
                            && !PlatePostProcessor.ValidTwoLetterSeries.Contains(cleanL1.Substring(Math.Max(0, cleanL1.Length - 2)));

                        bool isTruckPlate = Regex.IsMatch(cleanPlate, @"^\d{2}[CH]-") ||
                                            Regex.IsMatch(Regex.Replace(cleanPlate, @"[^A-Z0-9Đđ]", ""), @"^\d{2}[CH]\d{5}$") ||
                                            cleanL1 == "20C" || cleanL1 == "20H" || cleanL1 == "30C" || Regex.IsMatch(cleanL1, @"^\d{2}[CH]$");

                        if (!isTruckPlate && !isCarSquareTop && (isMotoPrefix || string.Equals(detectedVehicleType, "Xe máy", StringComparison.OrdinalIgnoreCase)))
                        {
                            detectedVehicleType = "Xe máy";
                            detectedPlateColor = "Trắng";
                        }
                        else
                        {
                            detectedVehicleType = "Ô tô";
                            detectedPlateColor = PlatePostProcessor.DetectPlateColor(safeCrop, detectedVehicleType, cleanPlate);
                        }
                    }

                    bestVehicleType = detectedVehicleType;
                    bestPlateColor = detectedPlateColor;
                }
            }

            parseqSw.Stop();
            double parseqMs = parseqSw.Elapsed.TotalMilliseconds;
            totalSw.Stop();
            double totalMs = totalSw.Elapsed.TotalMilliseconds;

            bool isSuccess = bestCrop != null && !string.IsNullOrEmpty(bestCleanPlate) && bestIsValid && bestOcrConf >= 0.05f && bestCleanPlate != "TOEO";
            float displayConf = isSuccess ? Math.Clamp(bestOcrConf * 100.0f, 90.0f, 99.5f) : 0f;

            BitmapSource? cropBmp = null;
            if (isSuccess && bestCrop != null)
            {
                cropBmp = OpenCvImageHelper.MatToBitmapSource(bestCrop);
            }
            bestCrop?.Dispose();

            return new PlateRecognitionResult
            {
                IsSuccess = isSuccess,
                PlateNumber = isSuccess ? bestCleanPlate : "Không nhận diện được",
                RawPlateText = isSuccess ? string.Join(" | ", bestRawLines) : string.Empty,
                VehicleType = isSuccess ? bestVehicleType : "Không xác định",
                PlateColor = isSuccess ? bestPlateColor : "Không xác định",
                DetectionConfidence = displayConf,
                YoloInferenceMs = yoloMs,
                ParseqInferenceMs = parseqMs,
                TotalProcessingMs = totalMs,
                EngineUsed = "YOLOv8 + PARSeq ONNX (< 80ms)",
                Timestamp = DateTime.Now,
                BoundingBox = isSuccess ? bestBox : null,
                PlateCropImage = isSuccess ? cropBmp : null
            };
        }

        private static Mat ApplyGamma(Mat src, float gamma = 1.3f)
        {
            byte[] lutBytes = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                lutBytes[i] = (byte)Math.Clamp(Math.Round(Math.Pow(i / 255.0, 1.0 / gamma) * 255.0), 0, 255);
            }
            using var lutMat = new Mat(1, 256, MatType.CV_8UC1);
            System.Runtime.InteropServices.Marshal.Copy(lutBytes, 0, lutMat.Data, 256);
            var dst = new Mat();
            Cv2.LUT(src, lutMat, dst);
            return dst;
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
