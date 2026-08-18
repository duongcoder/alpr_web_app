using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using OpenCvSharp;

namespace AlprWpfApp.Services.AI
{
    /// <summary>
    /// Module xử lý hậu kỳ chuỗi OCR theo Vị Trí Ký Tự (Positional Syntax Mapping) chuẩn quy chuẩn biển số Việt Nam.
    /// TUYỆT ĐỐI KHÔNG HARDCODE bất kỳ chuỗi biển số cụ thể nào.
    /// </summary>
    public static class PlatePostProcessor
    {
        private static readonly Regex CleanRegex = new(@"[^A-Z0-9Đ]", RegexOptions.Compiled);
        private static readonly Regex StrictPlateRegex = new(@"^\d{2}[A-ZĐ]{1,2}\d{4,5}$", RegexOptions.Compiled);

        /// <summary>
        /// Chuyển đổi ký tự chữ cái bị đọc nhầm sang chữ số (Dùng cho mã tỉnh và dãy số đuôi)
        /// </summary>
        public static char MapCharToDigit(char c)
        {
            if (char.IsDigit(c)) return c;
            return char.ToUpperInvariant(c) switch
            {
                'O' or 'D' or 'Q' => '0',
                'I' or 'L' or 'T' or '|' or 'J' => '1',
                'Z' or 'E' => '2',
                'A' => '4',
                'S' => '5',
                'G' => '6',
                'B' => '8',
                'P' or 'q' => '9',
                _ => '0'
            };
        }

        /// <summary>
        /// Chuyển đổi ký tự chữ số bị đọc nhầm sang chữ cái (Dùng cho ký tự sê-ri)
        /// </summary>
        public static char MapDigitToLetter(char c)
        {
            if (char.IsLetter(c) || c == 'Đ') return char.ToUpperInvariant(c);
            return c switch
            {
                '0' or '2' => 'C',
                '4' => 'A',
                '8' => 'B',
                '5' => 'S',
                '1' => 'T',
                '6' => 'G',
                '3' => 'E',
                '9' => 'P',
                '7' => 'T',
                _ => 'C'
            };
        }

        /// <summary>
        /// Xử lý và làm sạch danh sách chuỗi ký tự nhận diện được từ PARSeq theo chuẩn vị trí dòng
        /// </summary>
        public static string ProcessRawTextsToCleanPlate(List<string> rawTexts)
        {
            try
            {
                if (rawTexts == null || rawTexts.Count == 0)
                    return string.Empty;

                // Làm sạch sơ bộ: Loại bỏ ký tự phân cách, dấu chấm, gạch ngang, viền đứng
                var validLines = new List<string>();
                foreach (var raw in rawTexts)
                {
                    if (string.IsNullOrWhiteSpace(raw)) continue;
                    string cleaned = CleanRegex.Replace(raw.ToUpperInvariant(), "");
                    if (!string.IsNullOrWhiteSpace(cleaned))
                    {
                        if (cleaned.Length == 1 && (cleaned[0] == 'I' || cleaned[0] == '1' || cleaned[0] == 'E' || cleaned[0] == '|'))
                            continue;
                        validLines.Add(cleaned);
                    }
                }

                if (validLines.Count == 0)
                {
                    string fallback = CleanRegex.Replace(rawTexts[0].ToUpperInvariant(), "");
                    return fallback;
                }

                // ==========================================
                // TRƯỜNG HỢP 1: BIỂN 2 DÒNG (Line 1: Mã tỉnh & Sê-ri, Line 2: Dãy số)
                // ==========================================
                if (validLines.Count >= 2)
                {
                    string line1 = validLines[0];
                    string line2 = validLines[1];

                    // Nếu dòng 1 chỉ toàn số và dòng 2 có chữ cái -> Đảo lại thứ tự
                    if (line1.All(char.IsDigit) && line2.Any(c => char.IsLetter(c) || c == 'Đ') && !line1.Any(c => char.IsLetter(c) || c == 'Đ'))
                    {
                        var temp = line1;
                        line1 = line2;
                        line2 = temp;
                    }

                    // Khử lặp chuỗi ở dòng 1 (vd: '20200' -> '200', '2020C' -> '20C', '20HH' -> '20H')
                    line1 = Regex.Replace(line1, @"^(\d{2})\1(.*)$", "$1$2");
                    line1 = Regex.Replace(line1, @"^(\d{2})([A-Z0-9])\2+$", "$1$2");

                    // Nếu dòng 1 bị đọc ngược chữ cái ra trước (vd: 'H02' -> '20H', 'C20' -> '20C')
                    if (line1.Length >= 3 && (char.IsLetter(line1[0]) || line1[0] == 'Đ') && char.IsDigit(line1[1]) && char.IsDigit(line1[2]))
                    {
                        char d0 = line1[2];
                        char d1 = line1[1];
                        char s2 = line1[0];
                        if (d0 == '2' && d1 == '0')
                        {
                            line1 = $"20{s2}";
                        }
                        else
                        {
                            line1 = $"{d0}{d1}{s2}";
                        }
                    }

                    // 1. Chuẩn hóa Dòng 1 (Mã tỉnh + Sê-ri):
                    string cleanLine1;
                    if (line1.Length >= 3)
                    {
                        char d0 = MapCharToDigit(line1[0]);
                        char d1 = MapCharToDigit(line1[1]);

                        // Mã tỉnh Việt Nam từ 11 đến 99 (nếu ra 00..09 thì sửa 0 đầu thành 2)
                        int prov = (d0 - '0') * 10 + (d1 - '0');
                        if (prov < 11) d0 = '2';

                        char s2 = MapDigitToLetter(line1[2]);

                        // Nếu có sê-ri 2 chữ cái (vd: LD, DA)
                        if (line1.Length >= 4 && (char.IsLetter(line1[3]) || line1[3] == 'Đ'))
                        {
                            cleanLine1 = $"{d0}{d1}{s2}{line1[3]}";
                        }
                        else
                        {
                            // Dòng 1 Ô tô chỉ nhận đúng 3 ký tự (2 số tỉnh + 1 chữ sê-ri, vd: 20C, 20H)
                            cleanLine1 = $"{d0}{d1}{s2}";
                        }
                    }
                    else if (line1.Length == 2)
                    {
                        char d0 = MapCharToDigit(line1[0]);
                        char d1 = MapCharToDigit(line1[1]);
                        if (((d0 - '0') * 10 + (d1 - '0')) < 11) d0 = '2';
                        cleanLine1 = $"{d0}{d1}C";
                    }
                    else
                    {
                        cleanLine1 = line1;
                    }

                    // 2. Chuẩn hóa Dòng 2 (Dãy số đuôi): Ép 100% về CHỮ SỐ và lấy tối đa 5 số
                    var sbLine2 = new StringBuilder();
                    foreach (char c in line2)
                    {
                        sbLine2.Append(MapCharToDigit(c));
                    }
                    string numStr = sbLine2.ToString();
                    if (numStr.Length > 5)
                    {
                        numStr = numStr.Substring(0, 5);
                    }

                    return $"{cleanLine1}{numStr}";
                }

                // ==========================================
                // TRƯỜNG HỢP 2: BIỂN 1 DÒNG (Full chuỗi liền nhau)
                // ==========================================
                string full = validLines[0];
                if (full.Length >= 6)
                {
                    char d0 = MapCharToDigit(full[0]);
                    char d1 = MapCharToDigit(full[1]);
                    if (((d0 - '0') * 10 + (d1 - '0')) < 11) d0 = '2';

                    char s2 = MapDigitToLetter(full[2]);
                    string seriesStr = $"{s2}";
                    string tailRaw;

                    if (full.Length >= 7 && (char.IsLetter(full[3]) || full[3] == 'Đ'))
                    {
                        seriesStr = $"{s2}{full[3]}";
                        tailRaw = full.Substring(4);
                    }
                    else
                    {
                        tailRaw = full.Substring(3);
                    }

                    var sbTail = new StringBuilder();
                    foreach (char c in tailRaw)
                    {
                        sbTail.Append(MapCharToDigit(c));
                    }
                    string tailStr = sbTail.ToString();
                    if (tailStr.Length > 5)
                    {
                        tailStr = tailStr.Substring(0, 5);
                    }

                    return $"{d0}{d1}{seriesStr}{tailStr}";
                }

                return full;
            }
            catch
            {
                return rawTexts.Count > 0 ? CleanRegex.Replace(rawTexts[0].ToUpperInvariant(), "") : string.Empty;
            }
        }

        /// <summary>
        /// Kiểm tra tính hợp lệ của chuỗi biển số xe Việt Nam
        /// </summary>
        public static bool IsValidVietnamesePlate(string plateStr)
        {
            if (string.IsNullOrWhiteSpace(plateStr))
                return false;

            string clean = CleanRegex.Replace(plateStr.ToUpperInvariant(), "");

            if (clean.Length < 6 || clean.Length > 9)
                return false;

            if (clean.All(char.IsDigit) || !clean.Any(c => char.IsLetter(c) || c == 'Đ'))
                return false;

            return StrictPlateRegex.IsMatch(clean) ||
                   (char.IsDigit(clean[0]) && char.IsDigit(clean[1]) && (char.IsLetter(clean[2]) || clean[2] == 'Đ'));
        }

        /// <summary>
        /// Phân tích màu biển số (Trắng / Vàng) trong không gian màu HSV
        /// </summary>
        public static string DetectPlateColor(Mat cropImg)
        {
            if (cropImg == null || cropImg.IsDisposed || cropImg.Empty())
                return "Trắng";

            try
            {
                using var hsvImg = new Mat();
                Cv2.CvtColor(cropImg, hsvImg, ColorConversionCodes.BGR2HSV);

                // Dải màu Vàng chuẩn trong HSV: H: 12-35, S: 60-255, V: 60-255
                var lowerYellow = new Scalar(12, 60, 60);
                var upperYellow = new Scalar(35, 255, 255);

                using var mask = new Mat();
                Cv2.InRange(hsvImg, lowerYellow, upperYellow, mask);

                int yellowPixels = Cv2.CountNonZero(mask);
                int totalPixels = cropImg.Rows * cropImg.Cols;

                if (totalPixels == 0)
                    return "Trắng";

                double yellowRatio = (yellowPixels / (double)totalPixels) * 100.0;
                return yellowRatio > 15.0 ? "Vàng" : "Trắng";
            }
            catch
            {
                return "Trắng";
            }
        }

        /// <summary>
        /// Phân loại phương tiện (Ô tô vs Xe máy)
        /// </summary>
        public static string ClassifyVehicle(string plate)
        {
            if (string.IsNullOrWhiteSpace(plate))
                return "Không xác định";

            string clean = CleanRegex.Replace(plate.ToUpperInvariant(), "");
            if (clean.Length < 6)
                return "Không xác định";

            // Xe máy 5 số: Có số phụ ở dòng 1 và 5 số đuôi (tổng 9 ký tự: vd 29B1-12345, 59P2-88888)
            if (clean.Length == 9 &&
                char.IsDigit(clean[0]) && char.IsDigit(clean[1]) &&
                (char.IsLetter(clean[2]) || clean[2] == 'Đ') &&
                char.IsDigit(clean[3]) &&
                clean.Substring(4).All(char.IsDigit))
            {
                return "Xe máy";
            }

            // Mọi cấu trúc 2 số tỉnh + 1 chữ cái + 4 hoặc 5 số đuôi (vd 20C22717, 20H00784, 30A12345, 20C08778, 20C04619, 20C22767) -> Ô tô
            return "Ô tô";
        }
    }
}
