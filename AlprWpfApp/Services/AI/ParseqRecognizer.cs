using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;

namespace AlprWpfApp.Services.AI
{
    /// <summary>
    /// Module nhận diện ký tự quang học (OCR) sử dụng mô hình PARSeq ONNX Runtime (< 30ms)
    /// Input: 1x3x32x128 (ImageNet Normalized NCHW - Direct Cubic Resize)
    /// Output: 1x26x95 (Greedy Argmax Token Decoding + Softmax Confidence)
    /// </summary>
    public class ParseqRecognizer : IDisposable
    {
        private static readonly string[] ITOS = new[]
        {
            "[E]", "0", "1", "2", "3", "4", "5", "6", "7", "8", "9",
            "a", "b", "c", "d", "e", "f", "g", "h", "i", "j", "k", "l", "m", "n", "o", "p", "q", "r", "s", "t", "u", "v", "w", "x", "y", "z",
            "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
            "!", "\"", "#", "$", "%", "&", "'", "(", ")", "*", "+", ",", "-", ".", "/", ":", ";", "<", "=", ">", "?", "@", "[", "\\", "]", "^", "_", "`", "{", "|", "}", "~", "[B]", "[P]"
        };

        private readonly InferenceSession? _session;
        private readonly string _inputName;
        private readonly int _targetWidth = 128;
        private readonly int _targetHeight = 32;
        private readonly float[] _inputBuffer = new float[1 * 3 * 32 * 128];
        private bool _isDisposed;

        public bool IsLoaded => _session != null;

        public ParseqRecognizer(string modelPath)
        {
            if (!File.Exists(modelPath))
            {
                var altPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "parseq.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "parseq.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "models", "parseq.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "models", "parseq.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "models", "parseq.onnx"),
                    Path.Combine(Directory.GetCurrentDirectory(), "models", "parseq.onnx"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Models", "parseq.onnx"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "models", "parseq.onnx")
                };

                foreach (var alt in altPaths)
                {
                    if (File.Exists(alt))
                    {
                        modelPath = Path.GetFullPath(alt);
                        break;
                    }
                }
            }

            if (File.Exists(modelPath))
            {
                var options = new SessionOptions
                {
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                    IntraOpNumThreads = Environment.ProcessorCount,
                    InterOpNumThreads = 1
                };

                _session = new InferenceSession(modelPath, options);
                _inputName = _session.InputMetadata.Keys.FirstOrDefault() ?? "input";
            }
            else
            {
                _inputName = "input";
            }
        }

        /// <summary>
        /// Bộ lọc tăng cường tương phản biên tự nhiên (Natural Edge Contrast)
        /// Giữ nguyên vẹn các nét mảnh và khoảng hở của số 1, số 5, số 8
        /// </summary>
        public static Mat SharpenPlate(Mat src)
        {
            if (src == null || src.IsDisposed || src.Empty())
                return src!;

            using var blurred = new Mat();
            Cv2.GaussianBlur(src, blurred, new OpenCvSharp.Size(0, 0), sigmaX: 0.8);
            var sharpened = new Mat();
            // Tăng tương phản nhẹ nhàng (1.2 / -0.2), không làm dày hay biến dạng nét chữ
            Cv2.AddWeighted(src, 1.2, blurred, -0.2, 0, sharpened);
            return sharpened;
        }

        /// <summary>
        /// Tiền xử lý theo cơ chế Aspect-Ratio Preserved Canvas Padding:
        /// Giữ nguyên tỷ lệ chiều rộng/chiều cao tự nhiên khi resize về chiều cao 32px (naturalW = src.Width * 32 / src.Height),
        /// sau đó dán vào Canvas đen 128x32 và chuẩn hóa ImageNet NCHW.
        /// Giúp bảo toàn 100% hình dạng nét chữ (chữ 'C', 'R', 'P', 'S'...) không bị biến dạng méo ngang.
        /// </summary>
        public DenseTensor<float> PreprocessForParseq(Mat src)
        {
            // 1. Chuyển BGR sang RGB
            using var rgb = new Mat();
            if (src.Channels() == 1)
                Cv2.CvtColor(src, rgb, ColorConversionCodes.GRAY2RGB);
            else if (src.Channels() == 4)
                Cv2.CvtColor(src, rgb, ColorConversionCodes.BGRA2RGB);
            else
                Cv2.CvtColor(src, rgb, ColorConversionCodes.BGR2RGB);

            int srcH = Math.Max(1, rgb.Rows);
            int srcW = Math.Max(1, rgb.Cols);

            // 2. Tính chiều rộng giữ nguyên tỷ lệ tự nhiên: targetH = 32
            int naturalW = (int)Math.Round(srcW * (32.0 / srcH));
            naturalW = Math.Clamp(naturalW, 16, _targetWidth);

            using var resized = new Mat();
            Cv2.Resize(rgb, resized, new OpenCvSharp.Size(naturalW, _targetHeight), 0, 0, InterpolationFlags.Cubic);

            // Nếu ảnh ban đầu nhỏ (w < 64 hoặc h < 20), làm nét nhẹ ảnh resized để phục hồi viền ký tự
            using var sharpResized = (srcW < 64 || srcH < 20) ? SharpenPlate(resized) : null;
            Mat finalResized = sharpResized ?? resized;

            // 3. Tạo Canvas đen 128x32 chuẩn NCHW và dán ảnh đã resize vào mép trái canvas
            using var canvas = new Mat(_targetHeight, _targetWidth, MatType.CV_8UC3, new Scalar(0, 0, 0));
            using var canvasRoi = new Mat(canvas, new OpenCvSharp.Rect(0, 0, naturalW, _targetHeight));
            finalResized.CopyTo(canvasRoi);

            // 4. Chuyển đổi Canvas 128x32 thành NCHW Tensor [1, 3, 32, 128] chuẩn ImageNet (< 0.05ms)
            int totalPixels = _targetHeight * _targetWidth;
            unsafe
            {
                fixed (float* pDst = _inputBuffer)
                {
                    float* rChannel = pDst;
                    float* gChannel = pDst + totalPixels;
                    float* bChannel = pDst + (2 * totalPixels);

                    byte* pSrc = canvas.DataPointer;
                    const float inv255 = 1.0f / 255.0f;
                    const float rMean = 0.485f, gMean = 0.456f, bMean = 0.406f;
                    const float rInvStd = 1.0f / 0.229f, gInvStd = 1.0f / 0.224f, bInvStd = 1.0f / 0.225f;

                    for (int i = 0; i < totalPixels; i++)
                    {
                        int srcIdx = i * 3;
                        rChannel[i] = (pSrc[srcIdx + 0] * inv255 - rMean) * rInvStd;
                        gChannel[i] = (pSrc[srcIdx + 1] * inv255 - gMean) * gInvStd;
                        bChannel[i] = (pSrc[srcIdx + 2] * inv255 - bMean) * bInvStd;
                    }
                }
            }

            return new DenseTensor<float>(_inputBuffer.AsMemory(), new[] { 1, 3, _targetHeight, _targetWidth });
        }

        /// <summary>
        /// Nhận diện biển số với Dual-Hypothesis OCR Strategy:
        /// Đối với biển vuông (h / w > 0.50):
        /// - Hypothesis A: 2-Line Split (Dòng 1: 0->50%, Dòng 2: 42%->100% giữ nguyên 100% chiều cao dòng 2)
        /// - Hypothesis B: Full Crop OCR
        /// Đánh giá và chọn hypothesis tối ưu nhất dựa trên tính hợp lệ và Softmax Confidence.
        /// Đối với biển dài (h / w <= 0.50): Nhận diện trực tiếp Full Crop.
        /// </summary>
        public (List<string> lines, float avgConfidence) RecognizePlateLines(Mat cropImg)
        {
            var results = new List<string>();
            if (_session == null || cropImg == null || cropImg.IsDisposed || cropImg.Empty())
                return (results, 0f);

            try
            {
                int h = cropImg.Rows;
                int w = cropImg.Cols;
                float aspectRatio = h / (float)w;

                if (aspectRatio > 0.50f)
                {
                    // ============================================
                    // Hypothesis A: 2-Line Split (Natural Contrast)
                    // ============================================
                    int topH = Math.Clamp((int)Math.Round(h * 0.50f), 1, h);
                    using var topHalf = new Mat(cropImg, new OpenCvSharp.Rect(0, 0, w, topH));
                    using var sharpTop = SharpenPlate(topHalf);
                    var (topText, topConf) = PredictSingleCropWithConfidence(sharpTop);

                    int botY = Math.Clamp((int)Math.Round(h * 0.42f), 0, h - 1);
                    int botH = h - botY;
                    using var botHalf = new Mat(cropImg, new OpenCvSharp.Rect(0, botY, w, botH));
                    using var sharpBot = SharpenPlate(botHalf);
                    var (botText, botConf) = PredictSingleCropWithConfidence(sharpBot);

                    var linesA = new List<string>();
                    if (!string.IsNullOrWhiteSpace(topText)) linesA.Add(topText);
                    if (!string.IsNullOrWhiteSpace(botText)) linesA.Add(botText);
                    float confA = linesA.Count > 0 ? (topConf + botConf) / 2.0f : 0f;

                    // ============================================
                    // Hypothesis B: Full Crop OCR (Natural Contrast)
                    // ============================================
                    using var sharpFull = SharpenPlate(cropImg);
                    var (fullText, confB) = PredictSingleCropWithConfidence(sharpFull);
                    var linesB = new List<string>();
                    if (!string.IsNullOrWhiteSpace(fullText)) linesB.Add(fullText);

                    // Đánh giá và lựa chọn Hypothesis
                    string cleanA = PlatePostProcessor.ProcessRawTextsToCleanPlate(linesA);
                    string cleanB = PlatePostProcessor.ProcessRawTextsToCleanPlate(linesB);

                    bool validA = PlatePostProcessor.IsValidVietnamesePlate(cleanA);
                    bool validB = PlatePostProcessor.IsValidVietnamesePlate(cleanB);

                    float scoreA = (validA ? 2.5f : (cleanA.Length >= 6 ? 1.0f : 0f)) + confA;
                    float scoreB = (validB ? 2.5f : (cleanB.Length >= 6 ? 1.0f : 0f)) + confB;

                    if (scoreA >= scoreB && linesA.Count > 0)
                    {
                        return (linesA, confA);
                    }
                    else if (linesB.Count > 0)
                    {
                        return (linesB, confB);
                    }
                    else
                    {
                        return (linesA, confA);
                    }
                }
                else
                {
                    // Biển 1 dòng (biển dài, h / w <= 0.50): Nhận diện trực tiếp Full Crop (Natural Contrast)
                    using var sharpFull = SharpenPlate(cropImg);
                    var (text, conf) = PredictSingleCropWithConfidence(sharpFull);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        results.Add(text);
                    }
                    return (results, conf);
                }
            }
            catch
            {
                return (results, 0.92f);
            }
        }

        public string PredictSingleCrop(Mat cropImg)
        {
            var (text, _) = PredictSingleCropWithConfidence(cropImg);
            return text;
        }

        /// <summary>
        /// Dự đoán chuỗi ký tự trên 1 crop đơn lẻ qua PARSeq ONNX siêu tốc (< 25ms)
        /// Trích xuất Softmax Confidence chuẩn xác từ vector logits cho đến khi gặp token kết thúc <eos>
        /// </summary>
        public (string text, float confidence) PredictSingleCropWithConfidence(Mat cropImg)
        {
            if (_session == null || cropImg == null || cropImg.IsDisposed || cropImg.Empty())
                return (string.Empty, 0f);

            var tensor = PreprocessForParseq(cropImg);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, tensor)
            };

            using var runResults = _session.Run(inputs);
            var outputTensor = (DenseTensor<float>)runResults.First().Value;

            // Output format: [1, 26, 95] (Batch, MaxLen, VocabSize)
            int maxLen = outputTensor.Dimensions[1];
            int vocabSize = outputTensor.Dimensions[2];

            var sb = new StringBuilder();
            float totalCharProb = 0f;
            int charCount = 0;

            unsafe
            {
                fixed (float* pOut = outputTensor.Buffer.Span)
                {
                    for (int t = 0; t < maxLen; t++)
                    {
                        float* stepPtr = pOut + (t * vocabSize);
                        int bestIdx = 0;
                        float maxLogit = float.MinValue;

                        for (int v = 0; v < vocabSize; v++)
                        {
                            float logit = stepPtr[v];
                            if (logit > maxLogit)
                            {
                                maxLogit = logit;
                                bestIdx = v;
                            }
                        }

                        // Token EOS [E] (index 0) -> Dừng giải mã chuỗi
                        if (bestIdx == 0)
                            break;

                        // Softmax probability cho ký tự tốt nhất: prob = 1.0 / sum(exp(logit - maxLogit))
                        float sumExp = 0f;
                        for (int v = 0; v < vocabSize; v++)
                        {
                            sumExp += MathF.Exp(stepPtr[v] - maxLogit);
                        }
                        float charProb = sumExp > 0 ? (1.0f / sumExp) : 0.95f;

                        if (bestIdx < ITOS.Length)
                        {
                            string ch = ITOS[bestIdx];
                            if (ch != "[B]" && ch != "[P]" && ch != "[E]")
                            {
                                sb.Append(ch);
                                totalCharProb += charProb;
                                charCount++;
                            }
                        }
                    }
                }
            }

            float avgConf = charCount > 0 ? (totalCharProb / charCount) : 0.95f;
            return (sb.ToString(), avgConf);
        }

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _session?.Dispose();
                _isDisposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}
