using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCvSharp;
using AlprSdk.Engine;
using AlprSdk.Models;
using AlprSdk.Utils;

namespace AlprSdk
{
    /// <summary>
    /// Bộ nhận diện biển số xe chuyên dụng cho trạm cân xe tải (ALPR SDK).
    /// Đóng gói trọn vẹn: YOLOv8 Detect + PARSeq Decoupled OCR + PlatePostProcessor TCVN.
    /// Thread-safe và tối ưu hóa không rò rỉ bộ nhớ (Zero Memory Leak) cho hệ thống cân 24/7.
    /// </summary>
    public class AlprDetector : IDisposable
    {
        private readonly AlprPipelineEngine _engine;
        private readonly object _syncLock = new();
        private bool _isDisposed;

        static AlprDetector()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] searchDirs = new[]
                {
                    Path.Combine(baseDir, "runtimes", "win-x64", "native"),
                    Path.Combine(baseDir, "..", "Bin", "runtimes", "win-x64", "native"),
                    baseDir
                };

                foreach (var dir in searchDirs)
                {
                    if (Directory.Exists(dir) && File.Exists(Path.Combine(dir, "OpenCvSharpExtern.dll")))
                    {
                        SetDllDirectory(Path.GetFullPath(dir));
                        break;
                    }
                }
            }
            catch { }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        /// <summary>
        /// Khởi tạo bộ nhận diện biển số ALPR từ thư mục chứa các mô hình ONNX.
        /// Tự động tìm kiếm 'yolov8_plate.onnx' và 'parseq.onnx' trong thư mục chỉ định hoặc các thư mục chuẩn.
        /// </summary>
        /// <param name="modelsPath">Đường dẫn thư mục chứa 2 file ONNX (hoặc null để tìm tự động)</param>
        public AlprDetector(string? modelsPath = null)
        {
            string? resolvedYolo = null;
            string? resolvedParseq = null;

            // 1. Kiểm tra nếu người dùng truyền đường dẫn thư mục models cụ thể
            if (!string.IsNullOrWhiteSpace(modelsPath))
            {
                if (Directory.Exists(modelsPath))
                {
                    string yolo = Path.Combine(modelsPath, "yolov8_plate.onnx");
                    string parseq = Path.Combine(modelsPath, "parseq.onnx");
                    if (File.Exists(yolo)) resolvedYolo = yolo;
                    if (File.Exists(parseq)) resolvedParseq = parseq;

                    if (resolvedYolo == null)
                    {
                        string subYolo = Path.Combine(modelsPath, "Models", "yolov8_plate.onnx");
                        if (File.Exists(subYolo)) resolvedYolo = subYolo;
                        else
                        {
                            subYolo = Path.Combine(modelsPath, "models", "yolov8_plate.onnx");
                            if (File.Exists(subYolo)) resolvedYolo = subYolo;
                        }
                    }

                    if (resolvedParseq == null)
                    {
                        string subParseq = Path.Combine(modelsPath, "Models", "parseq.onnx");
                        if (File.Exists(subParseq)) resolvedParseq = subParseq;
                        else
                        {
                            subParseq = Path.Combine(modelsPath, "models", "parseq.onnx");
                            if (File.Exists(subParseq)) resolvedParseq = subParseq;
                        }
                    }
                }
            }

            // 2. Tìm kiếm tự động tại các vị trí mặc định nếu chưa xác định được
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            if (resolvedYolo == null)
            {
                string[] searchYolo = new[]
                {
                    Path.Combine(baseDir, "yolov8_plate.onnx"),
                    Path.Combine(baseDir, "Models", "yolov8_plate.onnx"),
                    Path.Combine(baseDir, "models", "yolov8_plate.onnx"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "yolov8_plate.onnx")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "models", "yolov8_plate.onnx")),
                    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "models", "yolov8_plate.onnx")),
                    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Models", "yolov8_plate.onnx"))
                };
                resolvedYolo = searchYolo.FirstOrDefault(File.Exists);
            }

            if (resolvedParseq == null)
            {
                string[] searchParseq = new[]
                {
                    Path.Combine(baseDir, "parseq.onnx"),
                    Path.Combine(baseDir, "Models", "parseq.onnx"),
                    Path.Combine(baseDir, "models", "parseq.onnx"),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "parseq.onnx")),
                    Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "models", "parseq.onnx")),
                    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "models", "parseq.onnx")),
                    Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Models", "parseq.onnx"))
                };
                resolvedParseq = searchParseq.FirstOrDefault(File.Exists);
            }

            if (string.IsNullOrEmpty(resolvedYolo) || !File.Exists(resolvedYolo))
                throw new FileNotFoundException($"Không tìm thấy file mô hình YOLOv8 'yolov8_plate.onnx' tại: {modelsPath ?? baseDir}");

            if (string.IsNullOrEmpty(resolvedParseq) || !File.Exists(resolvedParseq))
                throw new FileNotFoundException($"Không tìm thấy file mô hình PARSeq 'parseq.onnx' tại: {modelsPath ?? baseDir}");

            _engine = new AlprPipelineEngine(resolvedYolo, resolvedParseq);
        }

        /// <summary>
        /// Khởi tạo với đường dẫn chính xác tới từng file mô hình
        /// </summary>
        public AlprDetector(string yoloModelPath, string parseqModelPath)
        {
            if (!File.Exists(yoloModelPath))
                throw new FileNotFoundException("Không tìm thấy file mô hình YOLOv8", yoloModelPath);
            if (!File.Exists(parseqModelPath))
                throw new FileNotFoundException("Không tìm thấy file mô hình PARSeq", parseqModelPath);

            _engine = new AlprPipelineEngine(yoloModelPath, parseqModelPath);
        }

        #region Standard Interface for Weighing Station Software

        /// <summary>
        /// Phương thức nhận diện cốt lõi theo đặc tả chuẩn phần mềm trạm cân xe tải.
        /// </summary>
        /// <param name="im">Đối tượng Image chứa khung hình cần nhận diện</param>
        /// <returns>Đối tượng pr_plate chứa Image, Image_plate, Plate_text và các trường nghiệp vụ cân</returns>
        public pr_plate get_plate(Image im)
        {
            if (im == null) throw new ArgumentNullException(nameof(im));

            using var mat = AlprImageConverter.ImageToMat(im);
            return get_plate(mat);
        }

        /// <summary>
        /// Alias viết hoa chuẩn C# cho get_plate(Image)
        /// </summary>
        public pr_plate GetPlate(Image im) => get_plate(im);

        /// <summary>
        /// Nhận diện biển số trực tiếp từ đường dẫn file ảnh
        /// </summary>
        public pr_plate get_plate(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath))
                throw new ArgumentException("Đường dẫn ảnh không được để trống", nameof(imagePath));
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Không tìm thấy file ảnh", imagePath);

            using var mat = Cv2.ImRead(imagePath, ImreadModes.Color);
            return get_plate(mat);
        }

        /// <summary>
        /// Alias viết hoa chuẩn C# cho get_plate(string)
        /// </summary>
        public pr_plate GetPlate(string imagePath) => get_plate(imagePath);

        /// <summary>
        /// Nhận diện biển số từ mảng byte ảnh (luồng camera TCP/RTSP hoặc HTTP snapshot)
        /// </summary>
        public pr_plate get_plate(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("Dữ liệu byte ảnh không hợp lệ", nameof(imageBytes));

            using var mat = AlprImageConverter.BytesToMat(imageBytes);
            return get_plate(mat);
        }

        /// <summary>
        /// Alias viết hoa chuẩn C# cho get_plate(byte[])
        /// </summary>
        public pr_plate GetPlate(byte[] imageBytes) => get_plate(imageBytes);

        /// <summary>
        /// Nhận diện biển số trực tiếp từ OpenCvSharp Mat
        /// </summary>
        public pr_plate get_plate(Mat mat)
        {
            if (mat == null || mat.IsDisposed || mat.Empty())
            {
                return new pr_plate
                {
                    Plate_text = string.Empty,
                    Raw_plate_text = string.Empty,
                    Vehicle_type = "Không xác định",
                    Plate_color = "Không xác định",
                    Confidence = 0f,
                    Latency_ms = 0,
                    Is_valid = false
                };
            }

            lock (_syncLock)
            {
                if (_isDisposed)
                    throw new ObjectDisposedException(nameof(AlprDetector));

                // Thực thi ALPR Pipeline toàn trình: YOLOv8 -> Crop -> PARSeq -> TCVN PostProcessor
                var result = _engine.ProcessFrame(mat);

                // Chuyển ảnh crop biển số sang System.Drawing.Image độc lập
                Image? plateImage = null;
                if (result.PlateCropMat != null && !result.PlateCropMat.IsDisposed && !result.PlateCropMat.Empty())
                {
                    plateImage = AlprImageConverter.MatToImage(result.PlateCropMat);
                }

                // Tạo ảnh toàn cảnh có vẽ bounding box biển số (hoặc clone ảnh gốc nếu không có box)
                Image? annotatedImage = null;
                if (result.IsSuccess && result.BoundingBox.HasValue && result.BoundingBox.Value.Width > 0 && result.BoundingBox.Value.Height > 0)
                {
                    using var annotatedMat = AlprImageConverter.DrawPlateBox(mat, result.BoundingBox.Value, result.PlateNumber);
                    annotatedImage = AlprImageConverter.MatToImage(annotatedMat);
                }
                else
                {
                    annotatedImage = AlprImageConverter.MatToImage(mat);
                }

                // Giải phóng ngay lập tức Mat trung gian tránh rò rỉ RAM unmanaged
                result.PlateCropMat?.Dispose();

                string cleanPlate = result.IsSuccess ? result.PlateNumber : string.Empty;
                string rawPlate = result.IsSuccess
                    ? Regex.Replace(result.PlateNumber, @"[^A-Z0-9Đđ]", "")
                    : string.Empty;

                bool isValid = result.IsSuccess && PlatePostProcessor.IsValidVietnamesePlate(cleanPlate);

                return new pr_plate
                {
                    Image = annotatedImage,
                    Image_plate = plateImage,
                    Plate_text = cleanPlate,
                    Raw_plate_text = rawPlate,
                    Vehicle_type = result.VehicleType,
                    Plate_color = result.PlateColor,
                    Confidence = result.DetectionConfidence,
                    Latency_ms = result.TotalProcessingMs,
                    Is_valid = isValid
                };
            }
        }

        /// <summary>
        /// Alias viết hoa chuẩn C# cho get_plate(Mat)
        /// </summary>
        public pr_plate GetPlate(Mat mat) => get_plate(mat);

        #endregion

        /// <summary>
        /// Giải phóng toàn bộ tài nguyên OnnxRuntime InferenceSession và mô hình
        /// </summary>
        public void Dispose()
        {
            lock (_syncLock)
            {
                if (!_isDisposed)
                {
                    _isDisposed = true;
                    _engine.Dispose();
                }
            }
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Lớp bí danh (Alias) cho AlprDetector giúp tương thích linh hoạt với các quy ước đặt tên khác nhau
    /// </summary>
    public class AlprEngine : AlprDetector
    {
        public AlprEngine(string? modelsPath = null) : base(modelsPath) { }
        public AlprEngine(string yoloModelPath, string parseqModelPath) : base(yoloModelPath, parseqModelPath) { }
    }
}
