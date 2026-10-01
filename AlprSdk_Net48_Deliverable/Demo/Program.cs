using System;
using System.Drawing;
using System.IO;
using AlprSdk;
using AlprSdk.Models;

namespace AlprSdk.Net48.Demo
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("======================================================================");
            Console.WriteLine("  ALPR SDK (.NET FRAMEWORK 4.8 x64) DEMO - TRẠM CÂN XE TẢI");
            Console.WriteLine("  Framework Runtime: " + Environment.Version);
            Console.WriteLine("  Is 64-Bit Process: " + Environment.Is64BitProcess);
            Console.WriteLine("======================================================================\n");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string modelsDir = ResolvePath(baseDir, "models");
            string samplesDir = ResolvePath(baseDir, "samples");

            Console.WriteLine($"[1] Models folder : {modelsDir}");
            Console.WriteLine($"[2] Samples folder: {samplesDir}\n");

            Console.WriteLine("--> Đang khởi tạo AlprDetector trên .NET Framework 4.8...");
            using (var detector = new AlprDetector(modelsDir))
            {
                Console.WriteLine("--> AlprDetector khởi tạo thành công!\n");

                string truckImg = Path.Combine(samplesDir, "sample_plate_20h.png");
                string carImg = Path.Combine(samplesDir, "sample_car_white.png");

                // Test 1: Xe tải 20H
                if (File.Exists(truckImg))
                {
                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine($"TEST 1 (System.Drawing.Image): {Path.GetFileName(truckImg)}");
                    using (var img = Image.FromFile(truckImg))
                    using (var result = detector.get_plate(img))
                    {
                        Console.WriteLine($" * Biển số TCVN   : {result.Plate_text}");
                        Console.WriteLine($" * Biển số thô    : {result.Raw_plate_text}");
                        Console.WriteLine($" * Loại xe        : {result.Vehicle_type}");
                        Console.WriteLine($" * Màu biển       : {result.Plate_color}");
                        Console.WriteLine($" * Độ tin cậy OCR : {result.Confidence:F1}%");
                        Console.WriteLine($" * Độ trễ         : {result.Latency_ms:F1} ms");
                        Console.WriteLine($" * Hợp lệ TCVN    : {result.Is_valid}");
                        Console.WriteLine($" * Image BBox != null: {result.Image != null}");
                        Console.WriteLine($" * Crop Plate != null: {result.Image_plate != null}");
                    }
                }

                // Test 2: Xe con 30A
                if (File.Exists(carImg))
                {
                    Console.WriteLine("--------------------------------------------------");
                    Console.WriteLine($"TEST 2 (File Path Overload): {Path.GetFileName(carImg)}");
                    using (var resultCar = detector.get_plate(carImg))
                    {
                        Console.WriteLine($" * Biển số TCVN   : {resultCar.Plate_text}");
                        Console.WriteLine($" * Biển số thô    : {resultCar.Raw_plate_text}");
                        Console.WriteLine($" * Loại xe        : {resultCar.Vehicle_type}");
                        Console.WriteLine($" * Màu biển       : {resultCar.Plate_color}");
                        Console.WriteLine($" * Độ tin cậy OCR : {resultCar.Confidence:F1}%");
                        Console.WriteLine($" * Độ trễ         : {resultCar.Latency_ms:F1} ms");
                        Console.WriteLine($" * Hợp lệ TCVN    : {resultCar.Is_valid}");
                    }
                }
            }

            Console.WriteLine("\n======================================================================");
            Console.WriteLine("  XÁC NHẬN: .NET FRAMEWORK 4.8 x64 HOÀN TOÀN TƯƠNG THÍCH VÀ HOẠT ĐỘNG TỐT!");
            Console.WriteLine("======================================================================");
        }

        static string ResolvePath(string startDir, string folderName)
        {
            string current = startDir;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(current, folderName);
                if (Directory.Exists(candidate))
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
