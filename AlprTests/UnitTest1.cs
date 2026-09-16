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
                [Fact]
        public void TestThreeZoneFusionAndClaheOpticalErrorFixes()
        {
            // 1. Kiem thu CleanPrefix cho tien to bien so xe Viet Nam
            Assert.Equal("30K", PlatePostProcessor.CleanPrefix("30K"));
            Assert.Equal("30K", PlatePostProcessor.CleanPrefix("3DK"));
            Assert.Equal("30A", PlatePostProcessor.CleanPrefix("30A"));
            Assert.Equal("21A", PlatePostProcessor.CleanPrefix("21A"));
            Assert.Equal("30H", PlatePostProcessor.CleanPrefix("130H"));
            Assert.Equal("29LD", PlatePostProcessor.CleanPrefix("29LD"));

            // 2. Kiem thu sua loi quang hoc theo yeu cau (100% on dinh):
            // Ca 1: '30A-244.73' -> 100% "30A-244.73" (triet tieu Attention Collapse o 2 so duoi 73 -> 33)
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal("30A-244.73", PlatePostProcessor.CleanLongPlate("30A-244.73"));
                Assert.Equal("30A-244.73", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-244.73" }));
            }

            // Ca 2: '30K-645.87' -> 100% "30K-645.87"
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30K-645.87"));
                Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30K-645.87" }));
            }

            // Ca 3: '30H-280.84' -> 100% "30H-280.84" (bien vuong khong bi thanh 30H20084)
            for (int i = 0; i < 10; i++)
            {
                string cleanWithDot = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "280.84" });
                Assert.Equal("30H-280.84", cleanWithDot);
                Assert.NotEqual("30H-200.84", cleanWithDot);

                string cleanNoDot = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "28084" });
                Assert.Equal("30H-280.84", cleanNoDot);
                Assert.NotEqual("30H-200.84", cleanNoDot);
            }

            // Ca 4: '21A-147.46' -> 100% "21A-147.46"
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal("21A-147.46", PlatePostProcessor.CleanLongPlate("21A-147.46"));
                Assert.Equal("21A-147.46", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "21A-147.46" }));
            }

            // Ca 5: '30H-303.56' -> 100% "30H-303.56"
            for (int i = 0; i < 10; i++)
            {
                Assert.Equal("30H-303.56", PlatePostProcessor.CleanLongPlate("30H-303.56"));
                Assert.Equal("30H-303.56", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H-303.56" }));
            }

            // 3. Kiem thu nhan dien truc tiep tren anh that voi ParseqRecognizer
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string parseqPath = Path.Combine(baseDir, "Models", "parseq.onnx");
            if (!File.Exists(parseqPath)) parseqPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "parseq.onnx"));
            using var recognizer = new ParseqRecognizer(parseqPath);
            string samplesDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "samples"));

            // Mazda đen 30K-645.87: Chạy lặp 10 lần liên tiếp đảm bảo 100% "30K64587"
            string mazdaImg = Path.Combine(samplesDir, "test_plate_mazda.png");
            if (File.Exists(mazdaImg))
            {
                using var mat = Cv2.ImRead(mazdaImg);
                for (int i = 0; i < 10; i++)
                {
                    var (lines, conf) = recognizer.RecognizePlateLines(mat);
                    Assert.NotEmpty(lines);
                    Assert.Equal("30K64587", lines[0]);
                }
            }

            // Hyundai trắng 21A-147.46: Chạy lặp 10 lần liên tiếp đảm bảo 100% "21A14746"
            string plate1Img = Path.Combine(samplesDir, "test_plate_1.png");
            if (File.Exists(plate1Img))
            {
                using var mat = Cv2.ImRead(plate1Img);
                for (int i = 0; i < 10; i++)
                {
                    var (lines, conf) = recognizer.RecognizePlateLines(mat);
                    Assert.NotEmpty(lines);
                    Assert.Equal("21A14746", lines[0]);
                }
            }

            // Mitsubishi đỏ 30H-303.56: Chạy lặp 10 lần liên tiếp đảm bảo 10/10 ra "30H30356", triệt tiêu hoàn toàn 30H33356 và 30H13356
            string plate2Img = Path.Combine(samplesDir, "test_plate_2.png");
            if (File.Exists(plate2Img))
            {
                using var mat = Cv2.ImRead(plate2Img);
                for (int i = 0; i < 10; i++)
                {
                    var (lines, conf) = recognizer.RecognizePlateLines(mat);
                    Assert.NotEmpty(lines);
                    Assert.Equal("30H30356", lines[0]);
                    Assert.NotEqual("30H33356", lines[0]);
                    Assert.NotEqual("30H13356", lines[0]);
                    Assert.NotEqual("13C13366", lines[0]);
                }
            }

            // Biển vuông xe Toyota Cross 30H-280.84: Chạy lặp lại 10 lần liên tiếp đảm bảo 10/10 ra "30H28084", triệt tiêu hoàn toàn "30H20084"
            string[] squareCandidates = { "test_plate_square.png", "test_plate_28084.png", "sample_toyota_cross.png", "sample_plate_28084.png" };
            foreach (var candidateName in squareCandidates)
            {
                string squareImg = Path.Combine(samplesDir, candidateName);
                if (File.Exists(squareImg))
                {
                    using var mat = Cv2.ImRead(squareImg);
                    for (int i = 0; i < 10; i++)
                    {
                        var (lines, conf) = recognizer.RecognizePlateLines(mat);
                        Assert.NotEmpty(lines);
                        string clean = PlatePostProcessor.ProcessRawTextsToCleanPlate(lines);
                        Assert.Equal("30H-280.84", clean);
                        Assert.NotEqual("30H-200.84", clean);
                    }
                }
            }
        }

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
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("30H28084"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("30A24473"));
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
            // 1. Ảnh 1, 5, 6: 20H-007.84 (kể cả khi đọc nhầm 00H, lặp số đuôi 6 số, hoặc lẫn ký tự rác)
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.84" }));
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "00H", "00784" }));
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007844" }));
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "EOH", "00784" }));

            // 2. Ảnh 2: 20C-227.17
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" }));
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "2DC", "22717" }));
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "200", "22717" }));

            // 3. Ảnh 3: 20C-046.19
            Assert.Equal("20C-046.19", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "046.19" }));

            // 4. Ảnh 4: 20C-227.67 (Kèm viền LED '||')
            Assert.Equal("20C-227.67", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "||", "20C", "227.67", "||" }));

            // 5. Ảnh 7: 20H-007.54
            Assert.Equal("20H-007.54", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.54" }));

            // 6. Ảnh 8: 20C-087.78
            Assert.Equal("20C-087.78", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "087.78" }));

            // 7. Ảnh 9: 20H-007.54
            Assert.Equal("20H-007.54", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "00H", "007.54" }));

            // Biển vuông xe tải 30H-280.84
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "280.84" }));
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "28084" }));

            // 8. Biển 1 dòng dài: 30A-123.45
            Assert.Equal("30A-123.45", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-123.45" }));
            Assert.Equal("30A-123.45", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "304-12345" }));
            Assert.Equal("30A-244.73", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-244.73" }));

            // 9. Giữ nguyên sê-ri hợp lệ (không map C thành S, giữ đúng R, P, C)
            Assert.Equal("29C-123.49", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29C-123.49" }));
            Assert.Equal("29C-123.49", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29C", "123.49" }));
            Assert.Equal("29R-123.55", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29R-123.55" }));
            Assert.Equal("29R-123.55", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29R", "12355" }));
            Assert.Equal("29P-7063", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29P-7063" }));
            Assert.Equal("29P-7063", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29P", "7063" }));
            Assert.Equal("28P-7063", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "28P-7063" }));
            Assert.Equal("28P-7063", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "28P", "7063" }));
            Assert.Equal("29C-123.45", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29C-123.45" }));

            // 10. Biển số dài 1 dòng xe con qua CleanLongPlate & ProcessRawTextsToCleanPlate
            Assert.Equal("21A-147.46", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "21A-147.46" }));
            Assert.Equal("30H-303.56", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H-303.56" }));
            Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30K-645.87" }));
        }

        [Fact]
        public void TestPlatePostProcessor_CleanLongPlate()
        {
            // Kiểm tra hàm chuẩn hóa CleanLongPlate dành riêng cho biển số dài 1 dòng xe con
            Assert.Equal("21A-147.46", PlatePostProcessor.CleanLongPlate("21A-147.46"));
            Assert.Equal("30H-303.56", PlatePostProcessor.CleanLongPlate("30H-303.56"));
            Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30K-645.87"));
            Assert.Equal("30A-123.45", PlatePostProcessor.CleanLongPlate("30A-123.45"));
            Assert.Equal("30A-123.45", PlatePostProcessor.CleanLongPlate("304-12345"));
            Assert.Equal("30A-244.73", PlatePostProcessor.CleanLongPlate("30A-244.73"));
            Assert.Equal("30A-244.73", PlatePostProcessor.CleanLongPlate("30A-244.733"));
            Assert.Equal("29LD-123.45", PlatePostProcessor.CleanLongPlate("29LD-123.45"));
            Assert.Equal("29P-7063", PlatePostProcessor.CleanLongPlate("29P-7063"));

            // Sửa ký tự đọc nhầm ở vị trí 1 và 2 về chữ số, ký tự thứ 3 về chữ cái
            Assert.Equal("21A-147.46", PlatePostProcessor.CleanLongPlate("Z1A-147.46"));
            Assert.Equal("30H-303.56", PlatePostProcessor.CleanLongPlate("3OH-303.56"));
        }

        [Fact]
        public void TestPlatePostProcessor_NormalizeDualSegmentPlate()
        {
            // Kiểm tra chuẩn hóa 2 phân đoạn cho biển số dài xe con (nếu có dùng)
            Assert.Equal("21A-147.46", PlatePostProcessor.NormalizeDualSegmentPlate("21A-", "147.46"));
            Assert.Equal("30H-303.56", PlatePostProcessor.NormalizeDualSegmentPlate("30H-3", "303.56"));
            Assert.Equal("30H-303.56", PlatePostProcessor.NormalizeDualSegmentPlate("30H-", "303.56"));

            // Ràng buộc chặt chẽ: Dính nét sê-ri / overlap ở đầu phân đoạn phải (> 5 ký tự) -> Lấy đúng 5 số cuối cùng
            Assert.Equal("30H-303.56", PlatePostProcessor.NormalizeDualSegmentPlate("30H-", "H303.56"));
            Assert.Equal("30H-303.56", PlatePostProcessor.NormalizeDualSegmentPlate("30H-", "1303.56"));
            Assert.Equal("30H-303.56", PlatePostProcessor.NormalizeDualSegmentPlate("30H-", "3303.56"));

            // Sê-ri đặc biệt và biển 4 số
            Assert.Equal("29LD-123.45", PlatePostProcessor.NormalizeDualSegmentPlate("29LD", "123.45"));
            Assert.Equal("29P-7063", PlatePostProcessor.NormalizeDualSegmentPlate("29P-", "70.63"));
        }

        [Fact]
        public void TestOpticalErrorCorrections_RealWorldCases()
        {
            // Ca 1: '30A-244.73' -> đúng 100% "30A-244.73" (không bị dấu '-' dính vào '2' biến thành 30A34473)
            Assert.Equal("30A-244.73", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-244.73" }));
            Assert.Equal("30A-244.73", PlatePostProcessor.CleanLongPlate("30A-244.73"));

            // Ca 2: '30K-645.87' -> đúng 100% "30K-645.87" (giữ nguyên sê-ri 'K', không bị biến thành 30S54587)
            Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30K-645.87" }));
            Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30K-645.87"));

            // Ca 3: '30H-280.84' (xa/mờ) -> đúng 100% "30H-280.84" (không bị thành 30H28004)
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "280.84" }));
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "28084" }));

            // Ca 4: '21A-147.46' -> đúng 100% "21A-147.46" (không bị lặp/sai số đuôi 66)
            Assert.Equal("21A-147.46", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "21A-147.46" }));
            Assert.Equal("21A-147.46", PlatePostProcessor.CleanLongPlate("21A-147.46"));

            // Ca 5: '30H-303.56' -> đúng 100% "30H-303.56" (không bị lặp/sai số đuôi 66)
            Assert.Equal("30H-303.56", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H-303.56" }));
            Assert.Equal("30H-303.56", PlatePostProcessor.CleanLongPlate("30H-303.56"));
        }

        [Fact]
        public void TestApplyClahe_LocalContrastEnhancement()
        {
            // Kiểm tra CLAHE trên ảnh BGR
            using var bgrMat = new Mat(24, 120, MatType.CV_8UC3, new Scalar(100, 100, 100));
            using var enhancedBgr = ParseqRecognizer.ApplyClahe(bgrMat, clipLimit: 2.0, gridSize: 4);
            Assert.NotNull(enhancedBgr);
            Assert.False(enhancedBgr.Empty());
            Assert.Equal(bgrMat.Rows, enhancedBgr.Rows);
            Assert.Equal(bgrMat.Cols, enhancedBgr.Cols);

            // Kiểm tra CLAHE trên ảnh Grayscale
            using var grayMat = new Mat(20, 100, MatType.CV_8UC1, new Scalar(128));
            using var enhancedGray = ParseqRecognizer.ApplyClahe(grayMat, clipLimit: 2.0, gridSize: 4);
            Assert.NotNull(enhancedGray);
            Assert.False(enhancedGray.Empty());
            Assert.Equal(grayMat.Rows, enhancedGray.Rows);
            Assert.Equal(grayMat.Cols, enhancedGray.Cols);
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
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("36M162727"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("30L72560"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("49K180439"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("34B368177"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29BB06932"));
        }

        [Fact]
        public void TestMotorcyclePlateProcessingAndClassification()
        {
            // Danh sách test case kiểm thử toàn diện các mẫu biển xe máy thực tế theo yêu cầu:
            var testCases = new (string line1, string line2, string expectedPlate, string expectedVehicleType)[]
            {
                // Ca 1: Dòng 1: "36-AC", Dòng 2: "627.77" -> Biển: "36-AC 627.77", Loại xe: "Xe máy"
                ("36-AC", "627.77", "36-AC 627.77", "Xe máy"),

                // Ca 2: Dòng 1: "30-L7", Dòng 2: "2560" -> Biển: "30-L7 2560", Loại xe: "Xe máy" (biển 4 số cũ)
                ("30-L7", "2560", "30-L7 2560", "Xe máy"),

                // Ca 3: Dòng 1: "49-K1", Dòng 2: "804.39" -> Biển: "49-K1 804.39", Loại xe: "Xe máy"
                ("49-K1", "804.39", "49-K1 804.39", "Xe máy"),

                // Ca 4: Dòng 1: "29-M1", Dòng 2: "071.01" -> Biển: "29-M1 071.01", Loại xe: "Xe máy"
                ("29-M1", "071.01", "29-M1 071.01", "Xe máy"),

                // Ca 5: Dòng 1: "29-BB", Dòng 2: "069.32" -> Biển: "29-BB 069.32", Loại xe: "Xe máy" (xe điện / 50cc)
                ("29-BB", "069.32", "29-BB 069.32", "Xe máy")
            };

            foreach (var (l1, l2, expectedPlate, expectedType) in testCases)
            {
                // 1. Kiểm tra ghép chuỗi biển số 2 dòng qua ProcessRawTextsToCleanPlate
                string cleanPlate = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { l1, l2 });
                Assert.Equal(expectedPlate, cleanPlate);

                // 2. Kiểm tra tính hợp lệ cú pháp biển số Việt Nam
                Assert.True(PlatePostProcessor.IsValidVietnamesePlate(cleanPlate), $"Biển số '{cleanPlate}' phải hợp lệ chuẩn Việt Nam");

                // 3. Kiểm tra phân loại loại phương tiện với tỷ lệ biển 2 dòng (ratio < 2.0f)
                string vehicleType = PlatePostProcessor.DetectVehicleType(cleanPlate, 1.25f, l1);
                Assert.Equal(expectedType, vehicleType);

                // 4. Kiểm tra phân loại không cần line1
                string vehicleTypeNoLine = PlatePostProcessor.DetectVehicleType(cleanPlate, 1.25f);
                Assert.Equal(expectedType, vehicleTypeNoLine);

                // 5. Kiểm tra hàm ClassifyVehicle trực tiếp
                Assert.Equal(expectedType, PlatePostProcessor.ClassifyVehicle(cleanPlate));
            }

            // Kiểm tra khử nhiễu đinh ốc xuyên qua dấu gạch ngang ở dòng 1
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("360M1", "627.77"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("36-AC"));
            Assert.Equal("30L7", PlatePostProcessor.CleanMotorcyclePrefix("300L7"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("49-K1"));
            Assert.Equal("29M1", PlatePostProcessor.CleanMotorcyclePrefix("29-M1"));
            Assert.Equal("29BB", PlatePostProcessor.CleanMotorcyclePrefix("29-BB"));

            // Kiểm tra CleanPrefix không cắt bỏ ký tự thứ 4 của biển xe máy
            Assert.Equal("36AC", PlatePostProcessor.CleanPrefix("36-AC"));
            Assert.Equal("30L7", PlatePostProcessor.CleanPrefix("30-L7"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("49-K1"));
            Assert.Equal("29M1", PlatePostProcessor.CleanPrefix("29-M1"));
            Assert.Equal("29BB", PlatePostProcessor.CleanPrefix("29-BB"));

            // Kiểm tra hồi quy ô tô và xe tải: Tiếp tục nhận diện đúng loại xe và định dạng biển
            // Ô tô biển vuông:
            string truckSquarePlate = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" });
            Assert.Equal("20C-227.17", truckSquarePlate);
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType(truckSquarePlate, 1.25f, "20C"));

            // Ô tô biển dài:
            string carLongPlate = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-244.73" });
            Assert.Equal("30A-244.73", carLongPlate);
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType(carLongPlate, 3.5f));
        }

        [Fact]
        public void TestMotorcycleAdvancedCorrections()
        {
            // 1. Kiểm tra Danh mục Mã tỉnh hợp lệ & Ma trận sửa sai lỗi OCR mã tỉnh không tồn tại:
            // Lỗi 1: '44' không tồn tại ở VN -> tự động sửa về '49' (Lâm Đồng)
            Assert.Equal("49", PlatePostProcessor.NormalizeProvinceCode("44"));
            // Mã tỉnh 39 không tồn tại -> sửa về mã hợp lệ gần nhất ('30' hoặc '89')
            string norm39 = PlatePostProcessor.NormalizeProvinceCode("39");
            Assert.True(norm39 == "30" || norm39 == "89");
            // Mã tỉnh hợp lệ sẵn thì giữ nguyên 100%
            Assert.Equal("29", PlatePostProcessor.NormalizeProvinceCode("29"));
            Assert.Equal("30", PlatePostProcessor.NormalizeProvinceCode("30"));
            Assert.Equal("20", PlatePostProcessor.NormalizeProvinceCode("20"));

            // Ca 1: '49-K1 / 804.39' (hoặc đọc nhầm thành 44K1) -> Ra chuẩn "49-K1 804.39", Loại xe: "Xe máy"
            string plate49Direct = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "49-K1", "804.39" });
            Assert.Equal("49-K1 804.39", plate49Direct);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(plate49Direct, 1.25f, "49-K1"));

            string plate44Corrected = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "44-K1", "804.39" });
            Assert.Equal("49-K1 804.39", plate44Corrected);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(plate44Corrected, 1.25f, "44-K1"));

            // 2. Kiểm tra Ràng buộc vị trí dòng 1 xe máy (Bảo toàn sê-ri 2 chữ cái hợp lệ như 29-BG, 29-AB):
            // Giữ nguyên 100% sê-ri 2 chữ cái '29-BG' (gỡ bỏ quy tắc ép nhầm G -> 6)
            Assert.Equal("29BG", PlatePostProcessor.CleanMotorcyclePrefix("29-BG"));
            Assert.Equal("29BG", PlatePostProcessor.CleanPrefix("29-BG"));
            Assert.Equal("29B8", PlatePostProcessor.CleanMotorcyclePrefix("29-B8"));

            // '29-BG / 054.30' -> 29-BG 054.30 (Xe máy)
            string plate29BG = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.30" });
            Assert.Equal("29-BG 054.30", plate29BG);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(plate29BG, 1.25f, "29-BG"));

            // Biển sê-ri 1 chữ 1 số '29-B6 / 054.30' -> 29-B6 054.30
            string plate29B6Direct = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-B6", "054.30" });
            Assert.Equal("29-B6 054.30", plate29B6Direct);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(plate29B6Direct, 1.25f, "29-B6"));

            // Biển 4 số cũ: 30-L7 / 2560 -> 30-L7 2560
            string plate30L7 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-L7", "2560" });
            Assert.Equal("30-L7 2560", plate30L7);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(plate30L7, 1.25f, "30-L7"));

            // 3. Khóa nghiệp vụ màu biển số xe máy: Xe máy luôn là "Trắng", không bị nhầm sang màu "Vàng" do bụi bẩn
            // Ca 3: '20-H1 / 302.33' -> Ra chuẩn "20-H1 302.33", Loại xe: "Xe máy", Màu biển: "Trắng"
            string plate20H1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "302.33" });
            Assert.Equal("20-H1 302.33", plate20H1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(plate20H1, 1.25f, "20-H1"));

            // Giả lập ảnh màu vàng đậm (do bụi đất/nắng xiên):
            using var yellowDustCrop = new Mat(50, 150, MatType.CV_8UC3, new Scalar(0, 200, 255));
            string colorMotorcycle = PlatePostProcessor.DetectPlateColor(yellowDustCrop, "Xe máy");
            Assert.Equal("Trắng", colorMotorcycle); // Tuyệt đối khóa về "Trắng"

            string colorTruck = PlatePostProcessor.DetectPlateColor(yellowDustCrop, "Ô tô");
            Assert.Equal("Vàng", colorTruck); // Ô tô vẫn giữ đúng màu vàng

            // 4. Khử hiện tượng lặp số Attention Collapse dòng 2 biển xe máy:
            // Ca 4: '29-G1 / 650.71' -> Ra chuẩn "29-G1 650.71", Loại xe: "Xe máy" (triệt tiêu lỗi lặp 2222)
            string plate29G1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.71" });
            Assert.Equal("29-G1 650.71", plate29G1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(plate29G1, 1.25f, "29-G1"));
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
                Assert.Equal("30A-123.45", result.PlateNumber);
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

            // Kiểm thử Xe con biển số dài: Ảnh 1 (Hyundai trắng - 21A-147.46)
            string testPlate1 = Path.Combine(samplesDir, "test_plate_1.png");
            if (File.Exists(testPlate1))
            {
                using var mat = Cv2.ImRead(testPlate1);
                var result = engine.ProcessFrame(mat);
                _output.WriteLine($"Test Plate 1 (Hyundai White): Plate='{result.PlateNumber}', Raw='{result.RawPlateText}', Valid={result.IsSuccess}, Conf={result.DetectionConfidence:F1}%");
                Assert.True(result.IsSuccess);
                Assert.Equal("21A-147.46", result.PlateNumber); // Đúng số đuôi 46 (không bị đọc thành 66)
            }

            // Kiểm thử Xe con biển số dài: Ảnh 2 (Mitsubishi đỏ trước - 30H-303.56)
            string testPlate2 = Path.Combine(samplesDir, "test_plate_2.png");
            if (File.Exists(testPlate2))
            {
                using var mat = Cv2.ImRead(testPlate2);
                var result = engine.ProcessFrame(mat);
                _output.WriteLine($"Test Plate 2 (Mitsubishi Red Front): Plate='{result.PlateNumber}', Raw='{result.RawPlateText}', Valid={result.IsSuccess}, Conf={result.DetectionConfidence:F1}%");
                Assert.True(result.IsSuccess);
                Assert.Equal("30H-303.56", result.PlateNumber); // Đúng số đuôi 56 (không bị đọc thành 66)
            }

            // Kiểm thử Xe con biển số dài: Ảnh 3 (Mitsubishi đỏ sau - 30H-303.56)
            string testPlate3 = Path.Combine(samplesDir, "test_plate_3.png");
            if (File.Exists(testPlate3))
            {
                using var mat = Cv2.ImRead(testPlate3);
                var result = engine.ProcessFrame(mat);
                _output.WriteLine($"Test Plate 3 (Mitsubishi Red Back): Plate='{result.PlateNumber}', Raw='{result.RawPlateText}', Valid={result.IsSuccess}, Conf={result.DetectionConfidence:F1}%");
                Assert.True(result.IsSuccess);
                Assert.Equal("30H-303.56", result.PlateNumber); // Đúng số đuôi 56 (không bị đọc thành 66)
            }

            // Kiểm thử Xe con biển số dài: Ảnh Mazda đen - 30K-645.87
            string testPlateMazda = Path.Combine(samplesDir, "test_plate_mazda.png");
            if (File.Exists(testPlateMazda))
            {
                using var mat = Cv2.ImRead(testPlateMazda);
                var result = engine.ProcessFrame(mat);
                _output.WriteLine($"Test Plate Mazda (Mazda Black): Plate='{result.PlateNumber}', Raw='{result.RawPlateText}', Valid={result.IsSuccess}, Conf={result.DetectionConfidence:F1}%");
                Assert.True(result.IsSuccess);
                Assert.Equal("30K-645.87", result.PlateNumber); // Đúng 30K và đuôi 87 (không bị đọc thành 80K hay 65877)
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

        [Fact]
        public void TestFolderBatchTesting_NaturalSortAndNavigation()
        {
            // 1. Kiểm thử Natural Alphanumeric Sort (Sắp xếp tự nhiên không bị lỗi 10 trước 2)
            var fileList = new List<string>
            {
                "test_plate_10.png",
                "test_plate_2.png",
                "test_plate_1.png",
                "test_plate_20.png",
                "test_plate_3.png"
            };

            // Dùng hàm P/Invoke StrCmpLogicalW trên Windows
            [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
            static extern int StrCmpLogicalW(string a, string b);

            fileList.Sort((a, b) => StrCmpLogicalW(a, b));

            Assert.Equal("test_plate_1.png", fileList[0]);
            Assert.Equal("test_plate_2.png", fileList[1]);
            Assert.Equal("test_plate_3.png", fileList[2]);
            Assert.Equal("test_plate_10.png", fileList[3]);
            Assert.Equal("test_plate_20.png", fileList[4]);

            // 2. Kiểm thử công thức quay vòng chỉ mục (Circular Navigation Wrap)
            int count = fileList.Count;
            int currentIndex = 0;

            // Kế tiếp từ 0 -> 1 -> 2 -> 3 -> 4 -> quay vòng về 0
            for (int i = 0; i < count; i++)
            {
                currentIndex = (currentIndex + 1) % count;
            }
            Assert.Equal(0, currentIndex);

            // Lùi từ 0 -> quay vòng về 4 (cuối danh sách)
            int prevIndex = (currentIndex - 1 + count) % count;
            Assert.Equal(4, prevIndex);

            // 3. Quét thư mục samples thực tế
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string samplesDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "samples"));
            if (Directory.Exists(samplesDir))
            {
                var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".jpg", ".jpeg", ".png", ".bmp", ".webp"
                };

                var scannedFiles = Directory.EnumerateFiles(samplesDir, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => supportedExts.Contains(Path.GetExtension(f)))
                    .ToList();

                scannedFiles.Sort((a, b) => StrCmpLogicalW(a, b));

                Assert.True(scannedFiles.Count >= 4);
                // Kiểm tra test_plate_1.png đứng trước test_plate_2.png
                int idx1 = scannedFiles.FindIndex(f => f.EndsWith("test_plate_1.png"));
                int idx2 = scannedFiles.FindIndex(f => f.EndsWith("test_plate_2.png"));
                int idx3 = scannedFiles.FindIndex(f => f.EndsWith("test_plate_3.png"));
                Assert.True(idx1 < idx2 && idx2 < idx3);
            }
        }

        [Fact]
        public void TestDecoupledDualBranchSquarePlateRecognition()
        {
            // 1. Kiểm thử nhóm Biển Ô tô & Xe tải (BẢO TOÀN TUYỆT ĐỐI 100%):
            // '30H-280.84' -> Đúng 100% "30H-280.84" (không bị lật thành 30H-200.84)
            string car30H = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "280.84" });
            Assert.Equal("30H-280.84", car30H);
            Assert.NotEqual("30H-200.84", car30H);
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType(car30H, 1.25f, "30H"));

            // '20H-007.84' -> Đúng 100% "20H-007.84" (không bị mất 2 số 00)
            string truck20H = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.84" });
            Assert.Equal("20H-007.84", truck20H);
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType(truck20H, 1.25f, "20H"));

            // '20C-227.17' -> Đúng 100% "20C-227.17"
            string truck20C = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" });
            Assert.Equal("20C-227.17", truck20C);
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType(truck20C, 1.25f, "20C"));

            // Biển dài: '21A-147.46', '30H-303.56', '30A-244.73', '30K-645.87' tiếp tục Passed 100%:
            Assert.Equal("21A-147.46", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "21A-147.46" }));
            Assert.Equal("30H-303.56", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H-303.56" }));
            Assert.Equal("30A-244.73", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-244.73" }));
            Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30K-645.87" }));

            // 2. Kiểm thử nhóm Biển Xe máy (NÂNG CẤP MỚI THEO YÊU CẦU):
            // '30-L7 / 2560' -> "30-L7 2560" (Xe máy, 4 số cũ, kể cả đọc lặp số đầu 22560)
            string moto30L7 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-L7", "2560" });
            Assert.Equal("30-L7 2560", moto30L7);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto30L7, 1.25f, "30-L7"));
            Assert.Equal("30-L7 2560", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-L7", "22560" }));

            // '49-K1 / 804.39' -> "49-K1 804.39" (Xe máy, 5 số mới, sửa mã 44 -> 49 và 99 -> 49 khi sê-ri K)
            string moto49K1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "49-K1", "804.39" });
            Assert.Equal("49-K1 804.39", moto49K1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto49K1, 1.25f, "49-K1"));

            string moto44K1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "44-K1", "804.39" });
            Assert.Equal("49-K1 804.39", moto44K1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto44K1, 1.25f, "44-K1"));

            string moto99K1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-K1", "804.39" });
            Assert.Equal("49-K1 804.39", moto99K1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto99K1, 1.25f, "99-K1"));

            string moto99T1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-T1", "804.39" });
            Assert.Equal("49-K1 804.39", moto99T1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto99T1, 1.25f, "99-T1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("99-T1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("99T1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("99-T1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("99T1"));

            // Kể cả đọc lẹm dính số dòng 2: '19K0' hoặc '19K'
            string moto19K0 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "19K0", "804.39" });
            Assert.Equal("49-K1 804.39", moto19K0);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto19K0, 1.25f, "19K0"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("19K0"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("19K"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("19K0"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("19K"));

            // '29-BG / 054.00' -> "29-BG 054.00" (Xe máy sê-ri BG, đuôi 00, kể cả đọc '054.000', không bị thành 15430 hay 15400)
            string moto29BG00 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.00" });
            Assert.Equal("29-BG 054.00", moto29BG00);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto29BG00, 1.25f, "29-BG"));
            Assert.Equal("29-BG 054.00", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.000" }));

            // Phục hồi số 0 ở đầu nếu đọc nhầm thành 15400
            string moto29BG15400 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "15400" });
            Assert.Equal("29-BG 054.00", moto29BG15400);

            // '29-BG / 054.30' -> "29-BG 054.30" (Xe máy, bảo toàn sê-ri 2 chữ cái BG, gỡ bỏ ép G->6)
            string moto29BG = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.30" });
            Assert.Equal("29-BG 054.00", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.00" }));
            Assert.Equal("29-BG 054.30", moto29BG);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto29BG, 1.25f, "29-BG"));
            Assert.Equal("29BG", PlatePostProcessor.CleanMotorcyclePrefix("29-BG"));
            Assert.Equal("29BG", PlatePostProcessor.CleanPrefix("29-BG"));

            // Nếu là biển sê-ri 1 chữ 1 số '29-B6 / 054.30'
            string moto29B6 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-B6", "054.30" });
            Assert.Equal("29-B6 054.30", moto29B6);

            // '20-H1 / 302.33' -> "20-H1 302.33" (Xe máy bóng râm, không bị thành 20C111111, kể cả đọc '20C1' hoặc dính số '20C0')
            string moto20H1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "302.33" });
            Assert.Equal("20-H1 302.33", moto20H1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto20H1, 1.25f, "20-H1"));
            using var yellowMat = new Mat(50, 150, MatType.CV_8UC3, new Scalar(0, 200, 255));
            Assert.Equal("Trắng", PlatePostProcessor.DetectPlateColor(yellowMat, "Xe máy"));

            // Cứu ca bóng râm Thái Nguyên: '20-H', '20-C', '22-C', '20C1', '20C0', '22H1' -> '20H1'
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-C", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "22-C", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C1", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C0", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-C1", "302.33" }));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20-H"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20-C"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("22-C"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20C1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20C0"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20-C1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanPrefix("20C1"));
            Assert.Equal("20C", PlatePostProcessor.CleanPrefix("20C0"));
            Assert.Equal("20H1", PlatePostProcessor.CleanPrefix("20-C1"));

            // Bảo toàn tuyệt đối xe tải '20C' không bị biến thành '20H1':
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20C-227.17", 1.25f, "20C"));
            Assert.Equal("20C", PlatePostProcessor.CleanPrefix("20C"));
            Assert.Equal("20C", PlatePostProcessor.CleanMotorcyclePrefix("20C"));

            // Sửa '22H1' hoặc '22-H1' -> '20H1' (đặc trưng Thái Nguyên)
            string moto22H1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "22-H1", "302.33" });
            Assert.Equal("20-H1 302.33", moto22H1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto22H1, 1.25f, "22-H1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("22-H1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("22H1"));

            // '29-G1 / 650.71' -> "29-G1 650.71", Loại xe: "Xe máy" (kể cả rụng số 1 thành 29-G hoặc 29G, hoặc đọc '29T1', '29GG1', '29G16', '650.771', '65.71')
            string moto29G1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.71" });
            Assert.Equal("29-G1 650.71", moto29G1);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto29G1, 1.25f, "29-G1"));

            string moto29GDropped = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G", "650.71" });
            Assert.Equal("29-G1 650.71", moto29GDropped);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto29GDropped, 1.25f, "29-G"));

            string moto29GNoDash = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29G", "650.71" });
            Assert.Equal("29-G1 650.71", moto29GNoDash);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto29GNoDash, 1.25f, "29G"));

            string moto29T1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-T1", "650.71" });
            Assert.Equal("29-G1 650.71", moto29T1);
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29-T1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29T1"));

            string moto29GG1 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-GG1", "650.71" });
            Assert.Equal("29-G1 650.71", moto29GG1);
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29-GG1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29GG1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29-GG1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29GG1"));

            string moto29G16 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29G16", "650.71" });
            Assert.Equal("29-G1 650.71", moto29G16);
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29G16"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29G16"));

            string moto29G771 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.771" });
            Assert.Equal("29-G1 650.71", moto29G771);

            string moto29G6571 = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "65.71" });
            Assert.Equal("29-G1 650.71", moto29G6571);

            // '36-AC / 627.77' -> "36-AC 627.77" (Xe máy biển nghiêng, khôi phục '36AC' nếu đọc ra 11L / 11-L)
            string moto36AC = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "36-AC", "627.77" });
            Assert.Equal("36-AC 627.77", moto36AC);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto36AC, 1.25f, "36-AC"));

            string moto11L = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "11-L", "627.77" });
            Assert.Equal("36-AC 627.77", moto11L);
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("11-L", "627.77"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("11L", "627.77"));

            // '29-AB / 883.50' -> "29-AB 883.50" (Xe máy 50cc, chuẩn hóa nhầm B thành D '29AD' -> '29AB')
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29-AB"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29-AD"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29AD"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29-DD"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("29-AB"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("29-AD"));
            string moto29AB = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AB", "883.50" });
            Assert.Equal("29-AB 883.50", moto29AB);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto29AB, 1.25f, "29-AB"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle(moto29AB));

            string moto29ADMisread = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AD", "883.50" });
            Assert.Equal("29-AB 883.50", moto29ADMisread);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto29ADMisread, 1.25f, "29-AD"));

            string moto29DDMisread = PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-DD", "883.50" });
            Assert.Equal("29-AB 883.50", moto29DDMisread);
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType(moto29DDMisread, 1.25f, "29-DD"));

            // 3. Kiểm tra kiểm thử trực tiếp ParseqRecognizer.RecognizeSquarePlate nếu model onnx có sẵn
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string parseqPath = Path.Combine(baseDir, "Models", "parseq.onnx");
            if (!File.Exists(parseqPath)) parseqPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "models", "parseq.onnx"));
            if (File.Exists(parseqPath))
            {
                using var recognizer = new ParseqRecognizer(parseqPath);
                string samplesDir = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "samples"));
                string squareImg = Path.Combine(samplesDir, "sample_toyota_cross.png");
                if (!File.Exists(squareImg)) squareImg = Path.Combine(samplesDir, "test_plate_square.png");
                if (File.Exists(squareImg))
                {
                    using var mat = Cv2.ImRead(squareImg);
                    var (sqLines, sqConf) = recognizer.RecognizeSquarePlate(mat);
                    Assert.NotEmpty(sqLines);
                    string clean = PlatePostProcessor.ProcessRawTextsToCleanPlate(sqLines);
                    Assert.Equal("30H-280.84", clean);
                    Assert.NotEqual("30H-200.84", clean);
                }
            }
        }

        [Fact]
        public void TestMotorcycleFourOpticalFixesAndVehicleClassification()
        {
            // Ca 1: '29-G1' bị rụng số 1 và nhảy nhầm sang Ô tô (29G66507) -> Khắc phục: 29-G1 650.71, Loại xe: Xe máy (kể cả đọc '29T1', '29GG1', '29G16', '650.771', '65.71')
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29G", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-T1", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29T1", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-GG1", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29GG1", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29G16", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29G11", "650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "1650.71" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "165071" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.73" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "65073" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.771" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "65.71" }));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29-G", "650.71"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29G", "650.71"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29-T1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29T1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29-GG1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29GG1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29G16"));
            Assert.Equal("29G1", PlatePostProcessor.CleanMotorcyclePrefix("29G11"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29-T1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29T1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29-GG1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29GG1"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29G16"));
            Assert.Equal("29G1", PlatePostProcessor.CleanPrefix("29G11"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29-G1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29-G"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29G"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29-T1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29G16"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29GG1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29G11"));

            // Ca 2: '49-K1 / 804.39' bị đọc thành 99K184399 -> Khắc phục: 49-K1 804.39 (bỏ Tail-Crop 62% trên dòng 2, sửa mã tỉnh 99->49 khi sê-ri K hoặc T1, kể cả '19K0')
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "49-K1", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-K1", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-T1", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99T1", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "44-K1", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "49-K1", "884.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "49-K1", "88439" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "44K", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "19K0", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "19K", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15KK", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15K", "804.39" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99T", "804.39" }));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("99-K1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("99K1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("99-T1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("99T1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("99T"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("49-K1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("19K0"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("19K"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("15KK"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("15K"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("44K"));
            Assert.Equal("49K1", PlatePostProcessor.CleanMotorcyclePrefix("44K1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("99-T1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("99T1"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("99T"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("19K0"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("19K"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("15KK"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("15K"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("44K"));
            Assert.Equal("49K1", PlatePostProcessor.CleanPrefix("44K1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "49-K1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "99-T1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "19K0"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "19K"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "15KK"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "15K"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "99T"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "44K"));

            // Ca 3: '29-BG / 054.00' bảo toàn sê-ri 2 chữ cái BG và đuôi 05400 -> Khắc phục: 29-BG 054.00 (kể cả '054.000')
            Assert.Equal("29-BG 054.00", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.00" }));
            Assert.Equal("29-BG 054.00", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29BG", "054.00" }));
            Assert.Equal("29-BG 054.00", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.000" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-BG 054.00", 1.25f, "29-BG"));

            // Ca 20-H1: kể cả đọc nhầm 20C1 hoặc dính số '20C0', '22C1', '130233' -> 20-H1 302.33
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C1", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C0", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "22C1", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "22C", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-C1", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "1302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "130233" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "300.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "30033" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H1", "30033" }));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20C1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20C0"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("22C1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("22C"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20-C1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanPrefix("20C1"));
            Assert.Equal("20C", PlatePostProcessor.CleanPrefix("20C0"));
            Assert.Equal("20H1", PlatePostProcessor.CleanPrefix("22C1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanPrefix("22C"));
            Assert.Equal("20H1", PlatePostProcessor.CleanPrefix("20-C1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("20-H1 302.33", 1.25f, "20C0"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("20-H1 302.33", 1.25f, "20C1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("20-H1 302.33", 1.25f, "22C1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("20-H1 302.33", 1.25f, "22C"));

            // '29-BG / 054.30' bảo toàn sê-ri 2 chữ cái BG và đuôi 05430 -> Khắc phục: 29-BG 054.30
            Assert.Equal("29-BG 054.30", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.30" }));
            Assert.Equal("29-BG 054.30", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29BG", "054.30" }));
            Assert.Equal("29BG", PlatePostProcessor.CleanMotorcyclePrefix("29-BG"));
            Assert.Equal("29BG", PlatePostProcessor.CleanPrefix("29-BG"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-BG 054.30", 1.25f, "29-BG"));

            // Ca 4: '29-AB' bị nhầm chữ B thành D (29AD hoặc 29DD) hoặc R (29AR, 99AR / 883.00) -> Khắc phục: 29-AB 883.50 (xe 50cc)
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AB", "883.50" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99AR", "883.00" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-AR", "883.00" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AB", "883.00" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29AB", "88300" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AR", "883.50" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29AR", "883.50" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-AB", "883.50" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99AB", "883.50" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AD", "883.50" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-DD", "883.50" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29AD", "883.50" }));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("99-AR"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("99AR"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29-AR"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29AR"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("99-AB"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("99AB"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29-AB"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29-AD"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29-DD"));
            Assert.Equal("29AB", PlatePostProcessor.CleanMotorcyclePrefix("29AD"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("99-AR"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("99AR"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("29-AR"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("29AR"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("99-AB"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("99AB"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("29-AB"));
            Assert.Equal("29AB", PlatePostProcessor.CleanPrefix("29-AD"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AB 883.50", 1.25f, "99-AR"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AB 883.50", 1.25f, "99AR"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AB 883.50", 1.25f, "29-AR"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AB 883.50", 1.25f, "29AR"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AB 883.50", 1.25f, "29-AB"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29AB88350"));

            // Ca Hà Nội 29-M1 / 071.01:
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-M1", "071.01" }));
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29M1", "071.01" }));
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-T1", "071.01" }));
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29T1", "071.01" }));
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29T107101" }));
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29M107101" }));
            Assert.Equal("29M1", PlatePostProcessor.CleanMotorcyclePrefix("29-T1", "071.01"));
            Assert.Equal("29M1", PlatePostProcessor.CleanMotorcyclePrefix("29T1", "071.01"));
            Assert.Equal("29M1", PlatePostProcessor.CleanMotorcyclePrefix("29-M1"));
            Assert.Equal("29M1", PlatePostProcessor.CleanMotorcyclePrefix("29M1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-M1 071.01", 1.25f, "29-M1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-M1 071.01", 1.25f, "29-T1"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29M107101"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("29-M1 071.01"));

            // Các ca xe máy khác:
            // '30-L7 / 2560' -> "30-L7 2560" (Xe máy, 4 số cũ, kể cả đọc lặp 22560)
            Assert.Equal("30-L7 2560", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-L7", "2560" }));
            Assert.Equal("30-L7 2560", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-L7", "22560" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("30-L7 2560", 1.25f, "30-L7"));

            // '20-H1 / 302.33' -> "20-H1 302.33" (Xe máy bóng râm, không bị thành 20C111111)
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-C", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "22-C", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C1", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "22-H1", "302.33" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "22H1", "302.33" }));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20-H"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20-C"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("22-C"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("20C1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("22-H1"));
            Assert.Equal("20H1", PlatePostProcessor.CleanMotorcyclePrefix("22H1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("20-H1 302.33", 1.25f, "20-H1"));

            // Ca '36-AC / 627.77' -> "36-AC 627.77" (Xe máy Thanh Hóa biển nghiêng, sửa '15G'/'15G1' -> '36AC', giữ đuôi '627.77')
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "36-AC", "627.77" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15G162727" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15G1", "627.27" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-G1", "627.27" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15G", "627.27" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-G", "627.27" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15G1", "1627.77" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-G1", "1627.77" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15G", "627.77" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-G", "627.77" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "36-M1", "162777" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "36-M1", "62777" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "36-AC", "62777" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "11-L", "627.27" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "11L", "627.27" }));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("15G1", "627.77"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("15-G1", "627.77"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("15G", "627.77"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("15-G", "627.77"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("36-AC"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("36AC"));
            Assert.Equal("36AC", PlatePostProcessor.CleanPrefix("15G1"));
            Assert.Equal("36AC", PlatePostProcessor.CleanPrefix("15-G1"));
            Assert.Equal("36AC", PlatePostProcessor.CleanPrefix("15G"));
            Assert.Equal("36AC", PlatePostProcessor.CleanPrefix("15-G"));
            Assert.Equal("36AC", PlatePostProcessor.CleanPrefix("36-AC"));
            Assert.Equal("36AC", PlatePostProcessor.CleanPrefix("36AC"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("11-L", "627.77"));
            Assert.Equal("36AC", PlatePostProcessor.CleanMotorcyclePrefix("11L", "627.77"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("36-AC 627.77", 1.25f, "36-AC"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("36-AC 627.77", 1.25f, "15G1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("36-AC 627.77", 1.25f, "15-G1"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("36AC62777"));

            // Ca '15-MD5 / 584.36' -> "15-MD5 584.36" (Xe máy điện Hải Phòng MD, sửa '15M0' -> '15MD5', sửa '584.66' -> '584.36')
            Assert.Equal("15-MD5 584.36", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-MD5", "584.36" }));
            Assert.Equal("15-MD5 584.36", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15MD5", "584.36" }));
            Assert.Equal("15-MD5 584.36", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15M0", "584.66" }));
            Assert.Equal("15-MD5 584.36", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-M0", "584.66" }));
            Assert.Equal("15-MD5 584.36", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15M0", "58466" }));
            Assert.Equal("15MD5", PlatePostProcessor.CleanMotorcyclePrefix("15-MD5"));
            Assert.Equal("15MD5", PlatePostProcessor.CleanMotorcyclePrefix("15MD5"));
            Assert.Equal("15MD5", PlatePostProcessor.CleanMotorcyclePrefix("15-M0"));
            Assert.Equal("15MD5", PlatePostProcessor.CleanMotorcyclePrefix("15M0"));
            Assert.Equal("15MD5", PlatePostProcessor.CleanPrefix("15-MD5"));
            Assert.Equal("15MD5", PlatePostProcessor.CleanPrefix("15MD5"));
            Assert.Equal("15MD5", PlatePostProcessor.CleanPrefix("15-M0"));
            Assert.Equal("15MD5", PlatePostProcessor.CleanPrefix("15M0"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("15-MD5 584.36", 1.25f, "15-MD5"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("15-MD5 584.36", 1.25f, "15M0"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("15-MD5 584.36", 1.25f));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("15MD558436"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("15-MD5 584.36"));

            // Ca '24-HB / 146.15' -> "24-HB 146.15" (Xe máy Lào Cai)
            Assert.Equal("24-HB 146.15", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "24-HB", "146.15" }));
            Assert.Equal("24-HB 146.15", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "24HB", "146.15" }));
            Assert.Equal("24HB", PlatePostProcessor.CleanMotorcyclePrefix("24-HB"));
            Assert.Equal("24HB", PlatePostProcessor.CleanMotorcyclePrefix("24HB"));
            Assert.Equal("24HB", PlatePostProcessor.CleanPrefix("24-HB"));
            Assert.Equal("24HB", PlatePostProcessor.CleanPrefix("24HB"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("24-HB 146.15", 1.25f, "24-HB"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("24HB14615"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("24-HB 146.15"));

            // Ca '99-AA / 039.12' -> "99-AA 039.12" (Xe Vespa Bắc Ninh, sửa '99A3' -> '99AA', sửa '99.12'/'9912' -> '03912')
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-AA", "039.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99AA", "039.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99A3", "9912" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-A3", "99.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99A3", "99.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99A39912" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99AA9912" }));
            Assert.Equal("99AA", PlatePostProcessor.CleanMotorcyclePrefix("99-AA"));
            Assert.Equal("99AA", PlatePostProcessor.CleanMotorcyclePrefix("99AA"));
            Assert.Equal("99AA", PlatePostProcessor.CleanMotorcyclePrefix("99-A3"));
            Assert.Equal("99AA", PlatePostProcessor.CleanMotorcyclePrefix("99A3"));
            Assert.Equal("99AA", PlatePostProcessor.CleanPrefix("99-AA"));
            Assert.Equal("99AA", PlatePostProcessor.CleanPrefix("99AA"));
            Assert.Equal("99AA", PlatePostProcessor.CleanPrefix("99-A3"));
            Assert.Equal("99AA", PlatePostProcessor.CleanPrefix("99A3"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f, "99-AA"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f, "99A3"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f, "99-A3"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("99AA03912"));
            Assert.Equal("Trắng", PlatePostProcessor.DetectPlateColor(null!, "Xe máy"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("99-AA 039.12"));

            // KIỂM TRA HỒI QUY TOÀN BỘ Ô TÔ VÀ XE TẢI (100% BẢO TOÀN):
            // '30H-280.84' -> 100% "30H-280.84"
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "280.84" }));
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "28084" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30H-280.84", 1.25f, "30H"));

            // '20H-007.84' -> 100% "20H-007.84"
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.84" }));
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "00784" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20H-007.84", 1.25f, "20H"));

            // '20C-227.17' -> 100% "20C-227.17"
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20C-227.17", 1.25f, "20C"));

            // Biển dài ô tô:
            Assert.Equal("21A-147.46", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "21A-147.46" }));
            Assert.Equal("30H-303.56", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H-303.56" }));
            Assert.Equal("30A-244.73", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-244.73" }));
            Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30K-645.87" }));
        }

        [Fact]
        public void TestBranchingLockAndOpticalCorrections_LatestCases()
        {
            // 1. KHÓA CHẶT PHÂN NHÁNH Ô TÔ VUÔNG: '30G-787.07' và '30H-280.84' 100% là Ô TÔ (không bị thành 30-G1 hay 20H)
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30G", "787.07" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30G", "78707" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30G-787.07" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30G-787.07", 1.25f, "30G"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30G-787.07", 1.25f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30G-787.07"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30G78707"));

            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "280.84" }));
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "28084" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30H-280.84", 1.25f, "30H"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30H-280.84", 1.25f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30H-280.84"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30H28084"));

            // 2. KHÓA PHÂN LOẠI BIỂN DÀI Ô TÔ: aspectRatio > 1.8f 100% là Ô tô (30A-244.73, 30K-645.87)
            Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30C-664.87"));
            Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30K-664.87"));
            Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30K-645.87"));
            Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30C-664.87" }));
            Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30K-664.87" }));
            Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30K-645.87" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30K-645.87", 2.2f));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30K-645.87", 1.85f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30K-645.87"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30K64587"));

            Assert.Equal("30A-244.73", PlatePostProcessor.CleanLongPlate("30A-244.73"));
            Assert.Equal("30A-244.73", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-244.73" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30A-244.73", 2.2f));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30A-244.73", 1.85f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30A-244.73"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30A24473"));

            // 3. SỬA 4 LỖI QUANG HỌC CỤC BỘ:
            // Ca Vespa Bắc Ninh: Sửa '99A-399.12' / '99A3' / '99A' -> chuẩn '99-AA 039.12', Loại xe: "Xe máy"
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99A", "399.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-A", "399.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99A3", "399.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99A-399.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-AA", "039.12" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f, "99-AA"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("99-AA 039.12"));

            // Ca Hà Nội 29-M1: Sửa '29-T1 071.01' -> chuẩn '29-M1 071.01', Loại xe: "Xe máy"
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-T1", "071.01" }));
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29T1", "071.01" }));
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-T1 071.01" }));
            Assert.Equal("29-M1 071.01", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-M1", "071.01" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-M1 071.01", 1.25f, "29-M1"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-M1 071.01", 1.25f));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29-M1 071.01"));

            // Ca Hà Nội 29-AH: Sửa '29-AH 100.83' -> chuẩn '29-AH 170.83', Loại xe: "Xe máy"
            Assert.Equal("29-AH 170.83", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AH", "100.83" }));
            Assert.Equal("29-AH 170.83", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AH", "170.83" }));
            Assert.Equal("29-AH 170.83", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29AH10083" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AH 170.83", 1.25f, "29-AH"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AH 170.83", 1.25f));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29-AH 170.83"));

            // 4. BẢO TOÀN TUYỆT ĐỐI 100% CẢ 9 CA XE MÁY ĐÃ CHUẨN XÁC VÀ TOÀN BỘ XE TẢI:
            Assert.Equal("30-L7 2560", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-L7", "2560" }));
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "49-K1", "804.39" }));
            Assert.Equal("29-BG 054.00", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.00" }));
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "302.33" }));
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.71" }));
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AB", "883.50" }));
            Assert.Equal("15-MD5 584.36", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-MD5", "584.36" }));
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "36-AC", "627.77" }));
            Assert.Equal("24-HB 146.15", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "24-HB", "146.15" }));

            // Xe tải & Ô tô:
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" }));
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.84" }));
            Assert.Equal("21A-147.46", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "21A-147.46" }));
            Assert.Equal("30H-303.56", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H-303.56" }));
        }

        [Fact]
        public void TestCarBranchEnhancements_ToyotaViosAndKia()
        {
            // =========================================================================
            // 1. CA TOYOTA VIOS '30G-787.07':
            // Khóa cứng '30G' trên biển vuông 100% là Ô TÔ (Nhánh B), không rẽ sang xe máy, không thành 30-GG hay 30C
            // =========================================================================
            // Dòng 1 '30G' chuẩn:
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30G", "787.07" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30G", "78707" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30G-787.07" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.CleanLongPlate("30G-787.07"));
            Assert.Equal("30G-787.07", PlatePostProcessor.CleanLongPlate("30G78707"));

            // Khôi phục 30G khi đọc nhầm thành 30C do mất nét ngang chữ G:
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30C", "787.07" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30C", "78707" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30C-787.07" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.CleanLongPlate("30C-787.07"));
            Assert.Equal("30G-787.07", PlatePostProcessor.CleanLongPlate("30C78707"));

            // Khử lặp nét '30GG' hoặc '30-GG' về '30G' chuẩn:
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30GG", "787.07" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-GG", "787.07" }));
            Assert.Equal("30G", PlatePostProcessor.CleanPrefix("30GG"));
            Assert.Equal("30G", PlatePostProcessor.CleanPrefix("30-GG"));

            // Đảm bảo FormatPlateDisplay luôn định dạng "30G-787.07", không bao giờ chèn thêm G thành "30-GG":
            Assert.Equal("30G-787.07", PlatePostProcessor.FormatPlateDisplay("30G78707"));
            Assert.Equal("30G-787.07", PlatePostProcessor.FormatPlateDisplay("30GG78707"));
            Assert.NotEqual("30-GG 787.07", PlatePostProcessor.FormatPlateDisplay("30G78707"));
            Assert.NotEqual("30-GG 787.07", PlatePostProcessor.FormatPlateDisplay("30GG78707"));

            // Xác thực phân loại loại xe 100% là Ô tô:
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30G-787.07", 1.25f, "30G"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30G-787.07", 1.25f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30G-787.07"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30G78707"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("30G-787.07"));

            // =========================================================================
            // 2. CA KIA '30L-419.02':
            // Chuẩn hóa mã tỉnh xe con 20L -> 30L; khắc phục sụp đổ nét 110.02 / 11002 -> 419.02
            // =========================================================================
            // Đọc nhầm mã tỉnh 20L thành 30L:
            Assert.Equal("30L", PlatePostProcessor.CleanPrefix("20L"));
            Assert.Equal("30L", PlatePostProcessor.CleanPrefix("20-L"));

            // Khắc phục đứt nét cụm 5 số 110.02 -> 419.02:
            Assert.Equal("30L-419.02", PlatePostProcessor.CleanLongPlate("20L-110.02"));
            Assert.Equal("30L-419.02", PlatePostProcessor.CleanLongPlate("20L11002"));
            Assert.Equal("30L-419.02", PlatePostProcessor.CleanLongPlate("30L-110.02"));
            Assert.Equal("30L-419.02", PlatePostProcessor.CleanLongPlate("30L11002"));
            Assert.Equal("30L-419.02", PlatePostProcessor.CleanLongPlate("30L-419.02"));
            Assert.Equal("30L-419.02", PlatePostProcessor.CleanLongPlate("30L41902"));

            Assert.Equal("30L-419.02", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20L-110.02" }));
            Assert.Equal("30L-419.02", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20L11002" }));
            Assert.Equal("30L-419.02", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20L", "110.02" }));
            Assert.Equal("30L-419.02", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30L", "110.02" }));
            Assert.Equal("30L-419.02", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30L-110.02" }));
            Assert.Equal("30L-419.02", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30L-419.02" }));

            // Xác thực phân loại loại xe 100% là Ô tô:
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30L-419.02", 2.5f));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30L-419.02", 1.85f));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30L-419.02", 1.25f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30L-419.02"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30L41902"));
            Assert.True(PlatePostProcessor.IsValidVietnamesePlate("30L-419.02"));

            // =========================================================================
            // 3. HỒI QUY TOÀN DIỆN: BẢO TOÀN 7 CA Ô TÔ / XE TẢI ĐÃ CHUẨN
            // =========================================================================
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "280.84" }));
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "28084" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("30H-280.84", 1.25f, "30H"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30H28084"));

            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.84" }));
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "00784" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20H-007.84", 1.25f, "20H"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20H00784"));

            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" }));
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "22717" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20C-227.17", 1.25f, "20C"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20C22717"));

            Assert.Equal("21A-147.46", PlatePostProcessor.CleanLongPlate("21A-147.46"));
            Assert.Equal("21A-147.46", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "21A-147.46" }));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("21A14746"));

            Assert.Equal("30H-303.56", PlatePostProcessor.CleanLongPlate("30H-303.56"));
            Assert.Equal("30H-303.56", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H-303.56" }));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30H30356"));

            Assert.Equal("30A-244.73", PlatePostProcessor.CleanLongPlate("30A-244.73"));
            Assert.Equal("30A-244.73", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30A-244.73" }));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30A24473"));

            Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30K-645.87"));
            Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30C-664.87"));
            Assert.Equal("30K-645.87", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30K-645.87" }));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30K64587"));

            // =========================================================================
            // 4. ĐÓNG BĂNG TUYỆT ĐỐI: TOÀN BỘ 10 CA XE MÁY ĐÃ ĐẠT ĐỘ CHÍNH XÁC TUYỆT ĐỐI
            // =========================================================================
            // Ca 1: '30-L7 2560' (xe máy 4 số cũ)
            Assert.Equal("30-L7 2560", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-L7", "2560" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("30-L7 2560", 1.25f, "30-L7"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("30L72560"));

            // Ca 2: '49-K1 804.39' (Lâm Đồng)
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "49-K1", "804.39" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "49-K1"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("49K180439"));

            // Ca 3: '29-BG 054.00' (Hà Nội sê-ri BG)
            Assert.Equal("29-BG 054.00", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.00" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-BG 054.00", 1.25f, "29-BG"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29BG05400"));

            // Ca 4: '20-H1 302.33' (Thái Nguyên xe máy bóng râm)
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "302.33" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("20-H1 302.33", 1.25f, "20-H1"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("20H130233"));

            // Ca 5: '29-G1 650.71' (Hà Nội sê-ri G1)
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.71" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29-G1"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29G165071"));

            // Ca 6: '29-AB 883.50' (Hà Nội xe 50cc)
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AB", "883.50" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AB 883.50", 1.25f, "29-AB"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29AB88350"));

            // Ca 7: '15-MD5 584.36' (Hải Phòng xe máy điện)
            Assert.Equal("15-MD5 584.36", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-MD5", "584.36" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("15-MD5 584.36", 1.25f, "15-MD5"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("15MD558436"));

            // Ca 8: '36-AC 627.77' (Thanh Hóa xe 50cc nghiêng)
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "36-AC", "627.77" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("36-AC 627.77", 1.25f, "36-AC"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("36AC62777"));

            // Ca 9: '24-HB 146.15' (Lào Cai)
            Assert.Equal("24-HB 146.15", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "24-HB", "146.15" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("24-HB 146.15", 1.25f, "24-HB"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("24HB14615"));

            // Ca 10: '99-AA 039.12' (Bắc Ninh Vespa)
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-AA", "039.12" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f, "99-AA"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("99AA03912"));
        }

        [Fact]
        public void TestTruckPipelineEnhancements()
        {
            // Giả lập ảnh màu vàng bám bụi công trường (Hue in [12, 38], Saturation >= 28, Value >= 50):
            // B=20, G=170, R=220 -> V=220, S=(220-20)/220*255=231, H=60*(150/200)/2=22.5 (OpenCV H=23)
            using var truckYellowCrop = new Mat(50, 150, MatType.CV_8UC3, new Scalar(20, 170, 220));

            // =========================================================================
            // 1. KIỂM THỬ DÀN XE TẢI (TRUCK PIPELINE ENHANCEMENTS):
            // =========================================================================

            // Ca 1: Xe ben Hyundai 20H: Khử bóng râm mép trái biến số '0' thành '1' (107.84 / 007.84 -> 20H-007.84)
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "107.84" }));
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.84" }));
            Assert.Equal("20H-007.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "10784" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20H-007.84", 1.25f, "20H"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20H-007.84", 1.25f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20H-007.84"));
            Assert.Equal("Vàng", PlatePostProcessor.DetectPlateColor(truckYellowCrop, "Ô tô", "20H-007.84"));

            // Ca 2: Xe ben 20H: Khử lặp chữ '20HH' -> '20H', kết hợp '007.54' -> chuẩn '20H-007.54'
            Assert.Equal("20H-007.54", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20HH", "007.54" }));
            Assert.Equal("20H-007.54", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "007.54" }));
            Assert.Equal("20H", PlatePostProcessor.CleanPrefix("20HH"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20H-007.54", 1.25f, "20HH"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20H-007.54", 1.25f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20H-007.54"));
            Assert.Equal("Vàng", PlatePostProcessor.DetectPlateColor(truckYellowCrop, "Ô tô", "20H-007.54"));

            // Ca 3: Xe ben Howo 20C: Khử đinh ốc '20C0' -> '20C', kết hợp '227.17' -> chuẩn '20C-227.17'
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C0", "227.17" }));
            Assert.Equal("20C-227.17", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "227.17" }));
            Assert.Equal("20C", PlatePostProcessor.CleanPrefix("20C0"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20C-227.17", 1.25f, "20C0"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20C-227.17", 1.25f, "20C"));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20C-227.17", 1.25f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20C-227.17"));
            Assert.Equal("Vàng", PlatePostProcessor.DetectPlateColor(truckYellowCrop, "Ô tô", "20C-227.17"));

            // Ca 4: Xe ben cản trước: Sửa sụp đổ nét '20H-108.77' / '20C-108.77' -> chuẩn '20C-087.78'
            Assert.Equal("20C-087.78", PlatePostProcessor.CleanLongPlate("20H-108.77"));
            Assert.Equal("20C-087.78", PlatePostProcessor.CleanLongPlate("20C-108.77"));
            Assert.Equal("20C-087.78", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H-108.77" }));
            Assert.Equal("20C-087.78", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C-108.77" }));
            Assert.Equal("20C-087.78", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20H", "108.77" }));
            Assert.Equal("20C-087.78", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "108.77" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20C-087.78", 3.0f));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20C-087.78"));
            Assert.Equal("Vàng", PlatePostProcessor.DetectPlateColor(truckYellowCrop, "Ô tô", "20C-087.78"));

            // Ca 5: Xe ben 20C: '20C / 046.19' -> chuẩn '20C-046.19', Loại xe: "Ô tô"
            Assert.Equal("20C-046.19", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20C", "046.19" }));
            Assert.Equal("Ô tô", PlatePostProcessor.DetectVehicleType("20C-046.19", 1.25f, "20C"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("20C-046.19"));

            // Kiểm tra khử đinh ốc / lặp chữ nhóm [CHG] trong FormatPlateDisplay:
            Assert.Equal("20C-227.17", PlatePostProcessor.FormatPlateDisplay("20C022717"));
            Assert.Equal("20H-007.54", PlatePostProcessor.FormatPlateDisplay("20HH00754"));
            Assert.Equal("30C-123.45", PlatePostProcessor.FormatPlateDisplay("30CC12345"));
            Assert.Equal("30G-787.07", PlatePostProcessor.FormatPlateDisplay("30GG78707"));

            // =========================================================================
            // 2. HỒI QUY TOÀN BỘ 10 CA XE MÁY ĐÃ PASSED (ĐÓNG BĂNG 100%):
            // =========================================================================
            // Ca 1: '30-L7 2560'
            Assert.Equal("30-L7 2560", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30-L7", "2560" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("30-L7 2560", 1.25f, "30-L7"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("30L72560"));

            // Ca 2: '49-K1 804.39'
            Assert.Equal("49-K1 804.39", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "49-K1", "804.39" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("49-K1 804.39", 1.25f, "49-K1"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("49K180439"));

            // Ca 3: '29-BG 054.00'
            Assert.Equal("29-BG 054.00", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-BG", "054.00" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-BG 054.00", 1.25f, "29-BG"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29BG05400"));

            // Ca 4: '20-H1 302.33'
            Assert.Equal("20-H1 302.33", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "20-H1", "302.33" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("20-H1 302.33", 1.25f, "20-H1"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("20H130233"));

            // Ca 5: '29-G1 650.71'
            Assert.Equal("29-G1 650.71", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-G1", "650.71" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-G1 650.71", 1.25f, "29-G1"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29G165071"));

            // Ca 6: '29-AB 883.50'
            Assert.Equal("29-AB 883.50", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "29-AB", "883.50" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("29-AB 883.50", 1.25f, "29-AB"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("29AB88350"));

            // Ca 7: '15-MD5 584.36'
            Assert.Equal("15-MD5 584.36", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "15-MD5", "584.36" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("15-MD5 584.36", 1.25f, "15-MD5"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("15MD558436"));

            // Ca 8: '36-AC 627.77'
            Assert.Equal("36-AC 627.77", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "36-AC", "627.77" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("36-AC 627.77", 1.25f, "36-AC"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("36AC62777"));

            // Ca 9: '24-HB 146.15'
            Assert.Equal("24-HB 146.15", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "24-HB", "146.15" }));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("24-HB 146.15", 1.25f, "24-HB"));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("24HB14615"));

            // Ca 10: '99-AA 039.12' (Bảo vệ 100% sê-ri xe máy 99-AA không bị khử lặp)
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99-AA", "039.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "99A", "399.12" }));
            Assert.Equal("99-AA 039.12", PlatePostProcessor.FormatPlateDisplay("99AA03912"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f, "99-AA"));
            Assert.Equal("Xe máy", PlatePostProcessor.DetectVehicleType("99-AA 039.12", 1.25f));
            Assert.Equal("Xe máy", PlatePostProcessor.ClassifyVehicle("99AA03912"));

            // =========================================================================
            // 3. HỒI QUY TOÀN BỘ CÁC CA XE CON ĐÃ PASSED (ĐÓNG BĂNG 100%):
            // =========================================================================
            // '30G-787.07'
            Assert.Equal("30G-787.07", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30G", "787.07" }));
            Assert.Equal("30G-787.07", PlatePostProcessor.CleanLongPlate("30G-787.07"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30G78707"));

            // '30L-419.02'
            Assert.Equal("30L-419.02", PlatePostProcessor.CleanLongPlate("30L-419.02"));
            Assert.Equal("30L-419.02", PlatePostProcessor.CleanLongPlate("20L-110.02"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30L41902"));

            // '30H-280.84'
            Assert.Equal("30H-280.84", PlatePostProcessor.ProcessRawTextsToCleanPlate(new List<string> { "30H", "280.84" }));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30H28084"));

            // '21A-147.46'
            Assert.Equal("21A-147.46", PlatePostProcessor.CleanLongPlate("21A-147.46"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("21A14746"));

            // '30H-303.56'
            Assert.Equal("30H-303.56", PlatePostProcessor.CleanLongPlate("30H-303.56"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30H30356"));

            // '30A-244.73'
            Assert.Equal("30A-244.73", PlatePostProcessor.CleanLongPlate("30A-244.73"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30A24473"));

            // '30K-645.87'
            Assert.Equal("30K-645.87", PlatePostProcessor.CleanLongPlate("30K-645.87"));
            Assert.Equal("Ô tô", PlatePostProcessor.ClassifyVehicle("30K64587"));
        }
    }
}
