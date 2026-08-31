using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using AlprWpfApp.Models;

namespace AlprWpfApp.Services.AI
{
    /// <summary>
    /// Module phát hiện vị trí biển số xe sử dụng mô hình YOLOv8 ONNX Runtime (< 30ms)
    /// Input: 1x3x640x640 (RGB Float32 NCHW 0.0 - 1.0)
    /// Output: 1x5x8400 (cx, cy, w, h, score)
    /// </summary>
    public class YoloDetector : IDisposable
    {
        private readonly InferenceSession? _session;
        private readonly string _inputName;
        private readonly int _inputWidth = 640;
        private readonly int _inputHeight = 640;
        private readonly float[] _inputBuffer = new float[1 * 3 * 640 * 640];
        private readonly float _confThreshold = 0.03f;
        private readonly float _nmsThreshold = 0.45f;
        private bool _isDisposed;

        public bool IsLoaded => _session != null;

        public YoloDetector(string modelPath)
        {
            if (!File.Exists(modelPath))
            {
                var altPaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Models", "yolov8_plate.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "models", "yolov8_plate.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "models", "yolov8_plate.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "models", "yolov8_plate.onnx"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "models", "yolov8_plate.onnx"),
                    Path.Combine(Directory.GetCurrentDirectory(), "models", "yolov8_plate.onnx"),
                    Path.Combine(Directory.GetCurrentDirectory(), "Models", "yolov8_plate.onnx"),
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "models", "yolov8_plate.onnx")
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
                _inputName = _session.InputMetadata.Keys.FirstOrDefault() ?? "images";
            }
            else
            {
                _inputName = "images";
            }
        }

        /// <summary>
        /// Tiền xử lý khung hình Mat sang chuẩn NCHW Letterbox 640x640 tách 3 mặt phẳng Red, Green, Blue
        /// </summary>
        public DenseTensor<float> Preprocess(Mat image, out float scale, out float padLeft, out float padTop, out int originalWidth, out int originalHeight)
        {
            originalWidth = image.Cols;
            originalHeight = image.Rows;

            // 1. Tiền xử lý Letterbox 640x640 giữ nguyên tỷ lệ khung hình
            scale = Math.Min((float)_inputWidth / originalWidth, (float)_inputHeight / originalHeight);
            int newWidth = (int)Math.Round(originalWidth * scale);
            int newHeight = (int)Math.Round(originalHeight * scale);
            padLeft = (_inputWidth - newWidth) / 2.0f;
            padTop = (_inputHeight - newHeight) / 2.0f;

            using var resizedMat = new Mat();
            Cv2.Resize(image, resizedMat, new OpenCvSharp.Size(newWidth, newHeight), 0, 0, InterpolationFlags.Linear);

            using var letterboxMat = new Mat(new OpenCvSharp.Size(_inputWidth, _inputHeight), MatType.CV_8UC3, new Scalar(114, 114, 114));
            var roiRect = new OpenCvSharp.Rect((int)padLeft, (int)padTop, newWidth, newHeight);
            using var subMat = new Mat(letterboxMat, roiRect);
            resizedMat.CopyTo(subMat);

            // Chuyển BGR -> RGB
            using var rgbMat = new Mat();
            Cv2.CvtColor(letterboxMat, rgbMat, ColorConversionCodes.BGR2RGB);

            // 2. Chuyển thành NCHW Tensor [1, 3, 640, 640] tách biệt 3 mặt phẳng R, G, B
            int channelStride = _inputHeight * _inputWidth;
            unsafe
            {
                fixed (float* pDst = _inputBuffer)
                {
                    float* rChannel = pDst;
                    float* gChannel = pDst + channelStride;
                    float* bChannel = pDst + (2 * channelStride);

                    byte* pSrc = rgbMat.DataPointer;
                    const float inv255 = 1.0f / 255.0f;

                    for (int y = 0; y < _inputHeight; y++)
                    {
                        int rowOffset = y * _inputWidth * 3;
                        int idxOffset = y * _inputWidth;
                        for (int x = 0; x < _inputWidth; x++)
                        {
                            int srcIdx = rowOffset + (x * 3);
                            int index = idxOffset + x;

                            rChannel[index] = pSrc[srcIdx + 0] * inv255; // Red plane
                            gChannel[index] = pSrc[srcIdx + 1] * inv255; // Green plane
                            bChannel[index] = pSrc[srcIdx + 2] * inv255; // Blue plane
                        }
                    }
                }
            }

            return new DenseTensor<float>(_inputBuffer.AsMemory(), new[] { 1, 3, _inputHeight, _inputWidth });
        }

        /// <summary>
        /// Hậu xử lý giải mã Tensor [1, 5, 8400], lọc hình học biển số và trả về Top 5 Candidate Boxes
        /// </summary>
        public List<PlateDetectionBox> Postprocess(DenseTensor<float> outputTensor, float scale, float padLeft, float padTop, int originalWidth, int originalHeight, float confThreshold = 0.03f, float nmsThreshold = 0.45f)
        {
            int numAnchors = outputTensor.Dimensions[2]; // 8400
            var candidateBoxes = new List<PlateDetectionBox>();

            unsafe
            {
                fixed (float* pOut = outputTensor.Buffer.Span)
                {
                    float* cxPtr = pOut + (0 * numAnchors);
                    float* cyPtr = pOut + (1 * numAnchors);
                    float* wPtr = pOut + (2 * numAnchors);
                    float* hPtr = pOut + (3 * numAnchors);
                    float* scorePtr = pOut + (4 * numAnchors);

                    for (int i = 0; i < numAnchors; i++)
                    {
                        float score = scorePtr[i];
                        if (score < confThreshold)
                            continue;

                        float cx = cxPtr[i];
                        float cy = cyPtr[i];
                        float w = wPtr[i];
                        float h = hPtr[i];

                        // Un-letterbox về tọa độ ảnh gốc
                        float x1 = (cx - w / 2.0f - padLeft) / scale;
                        float y1 = (cy - h / 2.0f - padTop) / scale;
                        float x2 = (cx + w / 2.0f - padLeft) / scale;
                        float y2 = (cy + h / 2.0f - padTop) / scale;

                        // Kẹp biên Math.Clamp trong phạm vi ảnh gốc
                        x1 = Math.Clamp(x1, 0f, (float)originalWidth - 1);
                        y1 = Math.Clamp(y1, 0f, (float)originalHeight - 1);
                        x2 = Math.Clamp(x2, 0f, (float)originalWidth);
                        y2 = Math.Clamp(y2, 0f, (float)originalHeight);

                        int boxW = (int)Math.Max(1, x2 - x1);
                        int boxH = (int)Math.Max(1, y2 - y1);

                        // 1. Kích thước tối thiểu trên ảnh gốc: w >= 12, h >= 8 (bắt được biển số ở cự ly xa / cam trên cao)
                        if (boxW < 12 || boxH < 8)
                            continue;

                        // 2. Bộ lọc hình học Biển số: ratio in [0.8, 6.0]
                        float ratio = (float)boxW / boxH;
                        if (ratio < 0.8f || ratio > 6.0f)
                            continue;

                        candidateBoxes.Add(new PlateDetectionBox
                        {
                            BoundingBox = new OpenCvSharp.Rect((int)x1, (int)y1, boxW, boxH),
                            Confidence = score,
                            ClassId = 0,
                            Label = "Biển số xe"
                        });
                    }
                }
            }

            // Lọc NMS và trả về Top 5 Candidate Boxes
            var nmsResult = ApplyNms(candidateBoxes, nmsThreshold);
            return nmsResult.Take(5).ToList();
        }

        public List<PlateDetectionBox> Detect(Mat image, float? confThreshold = null, float? nmsThreshold = null)
        {
            if (_session == null || image == null || image.IsDisposed || image.Empty())
                return new List<PlateDetectionBox>();

            var tensor = Preprocess(image, out float scale, out float padLeft, out float padTop, out int origW, out int origH);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(_inputName, tensor)
            };

            using var results = _session.Run(inputs);
            var outputTensor = (DenseTensor<float>)results.First().Value;

            return Postprocess(outputTensor, scale, padLeft, padTop, origW, origH, confThreshold ?? _confThreshold, nmsThreshold ?? _nmsThreshold);
        }

        public static List<PlateDetectionBox> ApplyNms(List<PlateDetectionBox> boxes, float nmsThreshold)
        {
            if (boxes == null || boxes.Count == 0)
                return new List<PlateDetectionBox>();

            // Sắp xếp theo Confidence Score giảm dần
            var sorted = boxes.OrderByDescending(b => b.Confidence).ToList();
            var result = new List<PlateDetectionBox>();

            while (sorted.Count > 0)
            {
                var current = sorted[0];
                result.Add(current);
                sorted.RemoveAt(0);

                for (int i = sorted.Count - 1; i >= 0; i--)
                {
                    if (CalculateIou(current.BoundingBox, sorted[i].BoundingBox) > nmsThreshold)
                    {
                        sorted.RemoveAt(i);
                    }
                }
            }

            return result;
        }

        private static float CalculateIou(OpenCvSharp.Rect boxA, OpenCvSharp.Rect boxB)
        {
            int x1 = Math.Max(boxA.X, boxB.X);
            int y1 = Math.Max(boxA.Y, boxB.Y);
            int x2 = Math.Min(boxA.Right, boxB.Right);
            int y2 = Math.Min(boxA.Bottom, boxB.Bottom);

            int interWidth = Math.Max(0, x2 - x1);
            int interHeight = Math.Max(0, y2 - y1);
            int interArea = interWidth * interHeight;

            int areaA = boxA.Width * boxA.Height;
            int areaB = boxB.Width * boxB.Height;
            int unionArea = areaA + areaB - interArea;

            return unionArea <= 0 ? 0f : (float)interArea / unionArea;
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
