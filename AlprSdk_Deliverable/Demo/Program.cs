using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using AlprSdk;
using AlprSdk.Models;

namespace AlprSdkDemo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("======================================================================");
            Console.WriteLine("  ALPR SDK DEMO - HỆ THỐNG NHẬN DIỆN BIỂN SỐ XE TRẠM CÂN XE TẢI");
            Console.WriteLine("  Pipeline AI: YOLOv8 Detect + PARSeq OCR + HSV Color + TCVN Rules");
            Console.WriteLine("======================================================================\n");

            // 1. Xác định đường dẫn thư mục Models và Samples
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDir = ResolveDirectory(baseDir, "Models");
            string samplesDir = ResolveDirectory(baseDir, "Samples");

            Console.WriteLine($"[1] Thư mục Models : {modelsDir}");
            Console.WriteLine($"[2] Thư mục Samples: {samplesDir}\n");

            // 2. Khởi tạo Engine AlprDetector (Chỉ khởi tạo 1 lần duy nhất khi bật ứng dụng)
            Console.WriteLine("--> Đang khởi tạo AlprDetector...");
            using var detector = new AlprDetector(modelsDir);
            Console.WriteLine("--> AlprDetector đã sẵn sàng!\n");

            // 3. Chuẩn bị thư mục xuất kết quả ảnh crop
            string outputDir = Path.Combine(baseDir, "output");
            Directory.CreateDirectory(outputDir);

            // 4. Danh sách các ảnh xe mẫu để kiểm thử
            string[] testFiles = new[]
            {
                Path.Combine(samplesDir, "sample_truck_20h.png"),
                Path.Combine(samplesDir, "sample_car_30g.png")
            };

            int testIndex = 1;
            foreach (var imgPath in testFiles)
            {
                if (!File.Exists(imgPath))
                {
                    Console.WriteLine($"[CẢNH BÁO] Không tìm thấy file: {imgPath}");
                    continue;
                }

                Console.WriteLine($"----------------------------------------------------------------------");
                Console.WriteLine($"TEST #{testIndex++}: Xử lý ảnh '{Path.GetFileName(imgPath)}'");

                // Cách 1: Gọi nhận diện qua System.Drawing.Image (Chuẩn WinForms / GDI+)
                using (var srcImage = Image.FromFile(imgPath))
                using (var result = detector.get_plate(srcImage))
                {
                    PrintPlateResult(result);

                    // Lưu ảnh crop biển số và ảnh toàn cảnh đã vẽ bounding box
                    if (result.Image_plate != null)
                    {
                        string cropPath = Path.Combine(outputDir, $"crop_{Path.GetFileNameWithoutExtension(imgPath)}.png");
                        result.Image_plate.Save(cropPath, ImageFormat.Png);
                        Console.WriteLine($"   [Lưu ảnh crop biển số] -> {cropPath}");
                    }

                    if (result.Image != null)
                    {
                        string fullPath = Path.Combine(outputDir, $"bbox_{Path.GetFileNameWithoutExtension(imgPath)}.png");
                        result.Image.Save(fullPath, ImageFormat.Png);
                        Console.WriteLine($"   [Lưu ảnh toàn cảnh vẽ box] -> {fullPath}");
                    }
                }

                // Cách 2: Gọi nhận diện trực tiếp qua đường dẫn file ảnh
                using (var fileResult = detector.get_plate(imgPath))
                {
                    Console.WriteLine($"   [Overload Path]: Biển số='{fileResult.Plate_text}', Độ trễ={fileResult.Latency_ms:F1}ms");
                }

                Console.WriteLine();
            }

            Console.WriteLine("======================================================================");
            Console.WriteLine("  TẤT CẢ CÁC BƯỚC TEST ĐÃ HOÀN TẤT THÀNH CÔNG VỚI ALPR SDK!");
            Console.WriteLine("======================================================================");
        }

        static void PrintPlateResult(pr_plate result)
        {
            Console.WriteLine($"   [KẾT QUẢ NHẬN DIỆN PHẦN MỀM CÂN]:");
            Console.WriteLine($"   * Biển số chuẩn TCVN   : {result.Plate_text}");
            Console.WriteLine($"   * Biển số thô dạng liền: {result.Raw_plate_text}");
            Console.WriteLine($"   * Loại phương tiện     : {result.Vehicle_type}");
            Console.WriteLine($"   * Màu biển số          : {result.Plate_color}");
            Console.WriteLine($"   * Độ tin cậy OCR       : {result.Confidence:F1}%");
            Console.WriteLine($"   * Thời gian xử lý      : {result.Latency_ms:F1} ms");
            Console.WriteLine($"   * Trạng thái hợp lệ    : {(result.Is_valid ? "HỢP LỆ THEO TCVN" : "CẦN KIỂM TRA")}");
        }

        static string ResolveDirectory(string startDir, string folderName)
        {
            string current = startDir;
            for (int i = 0; i < 8; i++)
            {
                string candidate = Path.Combine(current, folderName);
                if (Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any())
                {
                    return Path.GetFullPath(candidate);
                }
                var parent = Directory.GetParent(current);
                if (parent == null) break;
                current = parent.FullName;
            }

            return Path.GetFullPath(Path.Combine(startDir, folderName));
        }
    }
}
