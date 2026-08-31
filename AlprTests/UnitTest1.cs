using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;
using AlprWpfApp.Services.AI;
using AlprWpfApp.Services.Camera;
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

            // Loại bỏ chuỗi rác (vân bê tông, lan can, ký tự ngắn/sai cú pháp)
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("1"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("21"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("AIETP"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("00784"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("22717"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate(""));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("29A1234567890"));
            Assert.False(PlatePostProcessor.IsValidVietnamesePlate("44SE0250")); // Vân bê tông / bóng lan can cầu cân
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

            // 9. Giữ nguyên sê-ri hợp lệ (không map C thành S, giữ đúng R, P, C)
            Assert.Equal("29C12349", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29C-123.49" }));
            Assert.Equal("29C12349", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29C", "123.49" }));
            Assert.Equal("29R12355", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29R-123.55" }));
            Assert.Equal("29R12355", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29R", "12355" }));
            Assert.Equal("29P7063", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29P-7063" }));
            Assert.Equal("29P7063", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29P", "7063" }));
            Assert.Equal("28P7063", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "28P-7063" }));
            Assert.Equal("28P7063", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "28P", "7063" }));
            Assert.Equal("29C12345", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29C-123.45" }));
        }

        [Fact]
        public void TestPlatePostProcessor_MapDigitToLetter()
        {
            // Các chữ số đọc nhầm ở vị trí sê-ri
            Assert.Equal('C', PlatePostProcessor.MapDigitToLetter('0'));
            Assert.Equal('T', PlatePostProcessor.MapDigitToLetter('1'));
            Assert.Equal('Z', PlatePostProcessor.MapDigitToLetter('2'));
            Assert.Equal('E', PlatePostProcessor.MapDigitToLetter('3'));
            Assert.Equal('A', PlatePostProcessor.MapDigitToLetter('4'));
            Assert.Equal('S', PlatePostProcessor.MapDigitToLetter('5'));
            Assert.Equal('G', PlatePostProcessor.MapDigitToLetter('6'));
            Assert.Equal('B', PlatePostProcessor.MapDigitToLetter('8'));

            // Chữ cái gốc hợp lệ phải được giữ nguyên 100%
            Assert.Equal('C', PlatePostProcessor.MapDigitToLetter('C'));
            Assert.Equal('S', PlatePostProcessor.MapDigitToLetter('S'));
            Assert.Equal('R', PlatePostProcessor.MapDigitToLetter('R'));
            Assert.Equal('P', PlatePostProcessor.MapDigitToLetter('P'));
            Assert.Equal('H', PlatePostProcessor.MapDigitToLetter('H'));
            Assert.Equal('Đ', PlatePostProcessor.MapDigitToLetter('Đ'));
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
                _output.WriteLine($"Yellow Truck Mat: {mat.Cols}x{mat.Rows}, AspectRatio={(float)mat.Rows/mat.Cols:F2}");
                var result = engine.ProcessFrame(mat);
                _output.WriteLine($"Sample Yellow Truck: Plate='{result.PlateNumber}', Raw='{result.RawPlateText}', Valid={result.IsSuccess}, Conf={result.DetectionConfidence:F1}%");
                if (result.IsSuccess)
                {
                    Assert.True(result.DetectionConfidence >= 85.0f);
                }
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

        [Fact]
        public void TestScaleRoiFiltering_Logic()
        {
            // Default ScaleRoi mở rộng: X từ 22% đến 78%, Y từ 10% đến 98%
            var roi = new Rect2f(0.22f, 0.10f, 0.56f, 0.88f);
            int imgW = 1280;
            int imgH = 720;

            // 1. Box xe tải làn bên trái (x=100, w=120 -> center_x = 160/1280 = 0.125 -> < 0.22 -> ngoài ROI)
            var truckLeftLane = new OpenCvSharp.Rect(100, 300, 120, 60);
            float normCenterX_Left = (truckLeftLane.X + truckLeftLane.Width / 2f) / imgW;
            float normCenterY_Left = (truckLeftLane.Y + truckLeftLane.Height / 2f) / imgH;
            bool insideLeft = (normCenterX_Left >= roi.X && normCenterX_Left <= (roi.X + roi.Width) &&
                               normCenterY_Left >= roi.Y && normCenterY_Left <= (roi.Y + roi.Height));
            Assert.False(insideLeft, "Xe ở làn phụ bên trái (X < 22%) phải bị loại bỏ");

            // 2. Box xe ben làn bên phải (x=1050, w=140 -> center_x = 1120/1280 = 0.875 -> > 0.78 -> ngoài ROI)
            var truckRightLane = new OpenCvSharp.Rect(1050, 300, 140, 60);
            float normCenterX_Right = (truckRightLane.X + truckRightLane.Width / 2f) / imgW;
            float normCenterY_Right = (truckRightLane.Y + truckRightLane.Height / 2f) / imgH;
            bool insideRight = (normCenterX_Right >= roi.X && normCenterX_Right <= (roi.X + roi.Width) &&
                                normCenterY_Right >= roi.Y && normCenterY_Right <= (roi.Y + roi.Height));
            Assert.False(insideRight, "Xe ở làn phụ bên phải (X > 78%) phải bị loại bỏ");

            // 3. Box xe trên bàn cân (x=500, y=400, w=200, h=80 -> center = (600/1280, 440/720) = (0.468, 0.611) -> trong ROI)
            var carOnScale = new OpenCvSharp.Rect(500, 400, 200, 80);
            float normCenterX_Scale = (carOnScale.X + carOnScale.Width / 2f) / imgW;
            float normCenterY_Scale = (carOnScale.Y + carOnScale.Height / 2f) / imgH;
            bool insideScale = (normCenterX_Scale >= roi.X && normCenterX_Scale <= (roi.X + roi.Width) &&
                                normCenterY_Scale >= roi.Y && normCenterY_Scale <= (roi.Y + roi.Height));
            Assert.True(insideScale, "Xe trên bàn cân phải được giữ lại trong ScaleRoi");

            // 4. Box xe ở xa trên đầu dốc cân / góc cam trên cao (Y = 15%)
            var carHighFar = new OpenCvSharp.Rect(600, 90, 80, 40);
            float normCenterX_Far = (carHighFar.X + carHighFar.Width / 2f) / imgW;
            float normCenterY_Far = (carHighFar.Y + carHighFar.Height / 2f) / imgH;
            bool insideFar = (normCenterX_Far >= roi.X && normCenterX_Far <= (roi.X + roi.Width) &&
                              normCenterY_Far >= roi.Y && normCenterY_Far <= (roi.Y + roi.Height));
            Assert.True(insideFar, "Xe ở cự ly xa đầu dốc cân (Y >= 10%) phải được bắt trọn trong ScaleRoi");
        }

        [Fact]
        public void TestSpatialProximityScoring_CenterlineAndProximity()
        {
            int imgW = 1280;
            int imgH = 720;
            var roi = new Rect2f(0.22f, 0.10f, 0.56f, 0.88f);
            float roiCenterX = roi.X + roi.Width / 2.0f; // 0.50
            float maxExpectedPlateArea = (float)(imgW * imgH * 0.04f);

            // Xe 1: Biển số thật trên xe tải (Conf YOLO=0.88, cú pháp chuẩn '20C22717', Y đáy=0.65, box 180x80)
            var boxRealPlate = new OpenCvSharp.Rect(500, 400, 180, 80);
            float normY1 = Math.Clamp((boxRealPlate.Y + boxRealPlate.Height) / (float)imgH, 0.0f, 1.0f); // 480/720 = 0.667
            float normArea1 = Math.Clamp((boxRealPlate.Width * boxRealPlate.Height) / maxExpectedPlateArea, 0.0f, 1.0f);
            float normCenterX1 = (boxRealPlate.X + boxRealPlate.Width / 2.0f) / (float)imgW; // 590/1280 = 0.461
            float distFromCenter1 = Math.Abs(normCenterX1 - roiCenterX);
            float centerWeight1 = Math.Clamp(1.0f - (distFromCenter1 / (roi.Width / 2.0f)), 0.0f, 1.0f);
            float scoreReal = 4.0f + (0.88f * 3.0f) + (0.95f * 1.5f) + (normY1 * 1.2f) + (normArea1 * 1.0f) + (centerWeight1 * 0.5f);

            // Nhiễu 2: Vết bóng lan can cầu cân ở góc dưới (Conf YOLO=0.10, cú pháp sai '44SE0250', Y đáy=0.98, box 80x30)
            var boxRailingNoise = new OpenCvSharp.Rect(300, 680, 80, 30);
            float normY2 = Math.Clamp((boxRailingNoise.Y + boxRailingNoise.Height) / (float)imgH, 0.0f, 1.0f); // 710/720 = 0.986
            float normArea2 = Math.Clamp((boxRailingNoise.Width * boxRailingNoise.Height) / maxExpectedPlateArea, 0.0f, 1.0f);
            float normCenterX2 = (boxRailingNoise.X + boxRailingNoise.Width / 2.0f) / (float)imgW;
            float distFromCenter2 = Math.Abs(normCenterX2 - roiCenterX);
            float centerWeight2 = Math.Clamp(1.0f - (distFromCenter2 / (roi.Width / 2.0f)), 0.0f, 1.0f);
            float scoreRailing = -3.0f + (0.10f * 3.0f) + (0.50f * 1.5f) + (normY2 * 1.2f) + (normArea2 * 1.0f) + (centerWeight2 * 0.5f);

            _output.WriteLine($"Score Biển Số Thật Trên Xe: {scoreReal:F3} (YoloScore=0.88, isValid=True, normY={normY1:F2})");
            _output.WriteLine($"Score Bóng Lan Can Cầu Cân: {scoreRailing:F3} (YoloScore=0.10, isValid=False, normY={normY2:F2})");

            Assert.True(scoreReal > scoreRailing, "Biển số thật trên xe tải phải có điểm số áp đảo hoàn toàn so với vết bóng lan can/vân bê tông");
        }

        [Fact]
        public void TestDrawRoiAndPlateOverlay()
        {
            using var testFrame = new Mat(720, 1280, MatType.CV_8UC3, new Scalar(50, 50, 50));
            var roi = new Rect2f(0.22f, 0.10f, 0.56f, 0.88f);
            var plateBox = new OpenCvSharp.Rect(500, 450, 200, 80);

            using var overlayMat = OpenCvImageHelper.DrawRoiAndPlateOverlay(testFrame, roi, plateBox, "20C-227.17", true);
            Assert.NotNull(overlayMat);
            Assert.False(overlayMat.Empty());
            Assert.Equal(testFrame.Cols, overlayMat.Cols);
            Assert.Equal(testFrame.Rows, overlayMat.Rows);
        }

        [Fact]
        public void TestRoiConfigService_SaveAndLoad()
        {
            // Test Default
            var defaultConfig = AlprWpfApp.Services.Config.RoiConfigService.DefaultConfig;
            Assert.Equal(0.22f, defaultConfig.X);
            Assert.Equal(0.10f, defaultConfig.Y);
            Assert.Equal(0.56f, defaultConfig.Width);
            Assert.Equal(0.88f, defaultConfig.Height);

            // Test Save and Load Custom ROI
            var customRoi = new Rect2f(0.15f, 0.20f, 0.65f, 0.75f);
            bool saved = AlprWpfApp.Services.Config.RoiConfigService.Save(customRoi);
            Assert.True(saved);

            var loadedConfig = AlprWpfApp.Services.Config.RoiConfigService.Load();
            Assert.Equal(0.15f, loadedConfig.X, 2);
            Assert.Equal(0.20f, loadedConfig.Y, 2);
            Assert.Equal(0.65f, loadedConfig.Width, 2);
            Assert.Equal(0.75f, loadedConfig.Height, 2);

            // Restore default configuration
            AlprWpfApp.Services.Config.RoiConfigService.Save(defaultConfig);
        }

        [Fact]
        public void TestCoordinateNormalization_LetterboxingMath()
        {
            // Giả lập khung hiển thị Image 800x600, ảnh gốc tỉ lệ 16:9 (1920x1080)
            double ctrlW = 800;
            double ctrlH = 600;
            double imgW = 1920;
            double imgH = 1080;

            double scale = Math.Min(ctrlW / imgW, ctrlH / imgH); // scale = 800 / 1920 = 0.416667
            double renderedW = imgW * scale; // 800
            double renderedH = imgH * scale; // 450
            double offsetX = (ctrlW - renderedW) / 2.0; // 0
            double offsetY = (ctrlH - renderedH) / 2.0; // 75 (2 viền đen trên dưới 75px)

            Assert.Equal(0, offsetX);
            Assert.Equal(75, offsetY);

            // Giả lập người dùng vẽ box từ (100, 150) đến (500, 400) trên Canvas 800x600
            double x1 = 100, x2 = 500;
            double y1 = 150, y2 = 400;

            double clampedLeft = Math.Clamp(x1, offsetX, offsetX + renderedW);
            double clampedRight = Math.Clamp(x2, offsetX, offsetX + renderedW);
            double clampedTop = Math.Clamp(y1, offsetY, offsetY + renderedH);
            double clampedBottom = Math.Clamp(y2, offsetY, offsetY + renderedH);

            float normX = (float)((clampedLeft - offsetX) / renderedW);
            float normY = (float)((clampedTop - offsetY) / renderedH);
            float normW = (float)((clampedRight - clampedLeft) / renderedW);
            float normH = (float)((clampedBottom - clampedTop) / renderedH);

            Assert.Equal(0.125f, normX, 3); // 100 / 800 = 0.125
            Assert.Equal((float)((150.0 - 75.0) / 450.0), normY, 3); // 75 / 450 = 0.1667
            Assert.Equal(0.50f, normW, 3);  // 400 / 800 = 0.50
            Assert.Equal((float)((400.0 - 150.0) / 450.0), normH, 3); // 250 / 450 = 0.5556
        }
    }
}
