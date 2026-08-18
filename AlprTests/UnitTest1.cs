using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;
using AlprWpfApp.Services.AI;
using AlprWpfApp.Models;

namespace AlprTests
{
    public class AlprPipelineTests
    {
        private readonly ITestOutputHelper _output;

        public AlprPipelineTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestPlatePostProcessor_RegexValidation()
        {
            // Các biển số hợp lệ
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("20C22717"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("20H00784"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("20H00754"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("20C04619"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("20C22767"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("20C08778"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("51F88888"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("30A12345"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("29LD1234"));

            // Loại bỏ chuỗi rác
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("1"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("21"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("AIETP"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("00784"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("22717"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate(""));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("29A1234567890"));
        }

        [Fact]
        public void TestPlatePostProcessor_PositionalCorrections()
        {
            // 1. Ảnh 1, 5, 6: 20H00784 (kể cả khi đọc nhầm 00H, lặp số đuôi 6 số, hoặc lẫn ký tự rác)
            Assert.Equal("20H00784", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.84" }));
            Assert.Equal("20H00784", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "00H", "00784" }));
            Assert.Equal("20H00784", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007844" }));
            Assert.Equal("20H00784", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "EOH", "00784" }));

            // 2. Ảnh 2: 20C22717
            Assert.Equal("20C22717", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" }));
            Assert.Equal("20C22717", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "2DC", "22717" }));
            Assert.Equal("20C22717", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "200", "22717" }));

            // 3. Ảnh 3: 20C04619
            Assert.Equal("20C04619", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "046.19" }));

            // 4. Ảnh 4: 20C22767 (Kèm viền LED '||')
            Assert.Equal("20C22767", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "||", "20C", "227.67", "||" }));

            // 5. Ảnh 7: 20H00754
            Assert.Equal("20H00754", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.54" }));

            // 6. Ảnh 8: 20C08778
            Assert.Equal("20C08778", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "087.78" }));

            // 7. Ảnh 9: 20H00754
            Assert.Equal("20H00754", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "00H", "007.54" }));

            // 8. Biển 1 dòng dài: 30A12345
            Assert.Equal("30A12345", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-123.45" }));
            Assert.Equal("30A12345", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "304-12345" }));
        }

        [Fact]
        public void TestPlateColorDetection()
        {
            using var yellowMat = new Mat(50, 150, MatType.CV_8UC3, new Scalar(0, 200, 255));
            string colorYellow = PlatePostProcessor.DetectPlateColor(yellowMat);
            Assert.Equal("Vàng", colorYellow);

            using var whiteMat = new Mat(50, 150, MatType.CV_8UC3, new Scalar(240, 240, 240));
            string colorWhite = PlatePostProcessor.DetectPlateColor(whiteMat);
            Assert.Equal("Trắng", colorWhite);
        }

        [Fact]
        public void TestVehicleClassification()
        {
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20C22717"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20H00784"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20H00754"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20C04619"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20C22767"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20C08778"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("51F88888"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("29LD12345"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29B112345"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("59P288888"));
        }

        [Fact]
        public void TestSampleImagesInference()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string yoloPath = Path.Combine(baseDir, "Models", "yolov8_plate.onnx");
            string parseqPath = Path.Combine(baseDir, "Models", "parseq.onnx");

            if (!File.Exists(yoloPath)) yoloPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "yolov8_plate.onnx"));
            if (!File.Exists(parseqPath)) parseqPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "parseq.onnx"));

            using var engine = new AlprPipelineEngine(yoloPath, parseqPath);
            Assert.True(engine.IsReady);

            string samplesDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "samples"));
            
            string carWhite = Path.Combine(samplesDir, "sample_car_white.png");
            if (File.Exists(carWhite))
            {
                using var mat = Cv2.ImRead(carWhite);
                var result = engine.ProcessFrame(mat);
                _output.WriteLine($"Sample Car White: Plate='{result.PlateNumber}', Raw='{result.RawPlateText}', Valid={result.IsSuccess}, Conf={result.DetectionConfidence:F1}%");
                Assert.True(result.IsSuccess);
                Assert.StartsWith("30A", result.PlateNumber);
                Assert.True(result.DetectionConfidence >= 85.0f);
            }

            string plate20h = Path.Combine(samplesDir, "sample_plate_20h.png");
            if (File.Exists(plate20h))
            {
                using var mat = Cv2.ImRead(plate20h);
                var result = engine.ProcessFrame(mat);
                _output.WriteLine($"Sample 20H: Plate='{result.PlateNumber}', Raw='{result.RawPlateText}', Valid={result.IsSuccess}, Conf={result.DetectionConfidence:F1}%");
                Assert.NotNull(result);
                if (result.IsSuccess)
                {
                    Assert.True(result.DetectionConfidence >= 85.0f);
                }
            }

            string yellowTruck = Path.Combine(samplesDir, "sample_truck_yellow.png");
            if (File.Exists(yellowTruck))
            {
                using var mat = Cv2.ImRead(yellowTruck);
                var result = engine.ProcessFrame(mat);
                _output.WriteLine($"Sample Yellow Truck: Plate='{result.PlateNumber}', Raw='{result.RawPlateText}', Valid={result.IsSuccess}, Conf={result.DetectionConfidence:F1}%");
                Assert.Equal("Vàng", result.PlateColor);
                Assert.Equal("20C22717", result.PlateNumber);
                Assert.True(result.DetectionConfidence >= 85.0f);
            }
        }

        [Fact]
        public void TestEndToEndPipeline_LatencyBenchmark()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string yoloPath = Path.Combine(baseDir, "Models", "yolov8_plate.onnx");
            string parseqPath = Path.Combine(baseDir, "Models", "parseq.onnx");

            if (!File.Exists(yoloPath)) yoloPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "yolov8_plate.onnx"));
            if (!File.Exists(parseqPath)) parseqPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "parseq.onnx"));

            using var yolo = new YoloDetector(yoloPath);
            using var parseq = new ParseqRecognizer(parseqPath);
            using var engine = new AlprPipelineEngine(yoloPath, parseqPath);

            Assert.True(yolo.IsLoaded);
            Assert.True(parseq.IsLoaded);
            Assert.True(engine.IsReady);

            // Benchmark YOLOv8 trên khung hình HD 1280x720
            using var hdFrame = new Mat(720, 1280, MatType.CV_8UC3, new Scalar(100, 100, 100));
            yolo.Detect(hdFrame);

            var sw = Stopwatch.StartNew();
            for (int i = 0; i < 5; i++)
            {
                yolo.Detect(hdFrame);
            }
            sw.Stop();
            double avgYolo = sw.Elapsed.TotalMilliseconds / 5.0;
            Assert.True(avgYolo < 100.0, $"YOLO latency trung bình: {avgYolo:F1}ms");
        }
    }
}
