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
        public static readonly HashSet<string> ValidTwoLetterSeries = new(StringComparer.OrdinalIgnoreCase)
        {
            "LD", "DA", "MK", "KT", "NG", "QT", "CV", "NN", "CD", "TD", "HC"
        };

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
        /// Lam sach va chuan hoa chuoi tien to bien so xe (Prefix: 2 so tinh + 1 chu cai se-ri):
        /// - Loc bo ky tu dac biet, lay toi da 3 ky tu dau tien (hoac 4 neu la se-ri 2 chu cai dac biet nhu LD, DA, KT...).
        /// - 2 ky tu dau anh xa ve chu so (ma tinh).
        /// - Ky tu thu 3: Neu la chu cai hop le ('K', 'A', 'H', 'E'...), giu nguyen 100%, khong ep doi sang ky tu khac.
        /// </summary>
        public static string CleanPrefix(string rawPrefix)
        {
            if (string.IsNullOrWhiteSpace(rawPrefix))
                return string.Empty;

            string clean = CleanRegex.Replace(rawPrefix.ToUpperInvariant(), "");
            if (clean.Length < 3)
            {
                if (clean.Length == 2)
                {
                    char c0 = MapCharToDigit(clean[0]);
                    char c1 = MapCharToDigit(clean[1]);
                    int p = (c0 - '0') * 10 + (c1 - '0');
                    if (p < 11) c0 = '2';
                    return $"{c0}{c1}";
                }
                return clean;
            }

            // Nếu dính nhiễu viền/ốc ở đầu khiến chuỗi có dạng [Nhiễu][Mã tỉnh 2 số][Chữ cái sê-ri] (vd: '130H' -> '30H')
            // Ký tự thứ 3 là số nhưng ký tự thứ 4 là chữ cái -> Bỏ ký tự nhiễu đầu tiên
            if (clean.Length >= 4 && !char.IsLetter(clean[2]) && clean[2] != 'Đ' && clean[2] != 'đ' && (char.IsLetter(clean[3]) || clean[3] == 'Đ' || clean[3] == 'đ'))
            {
                clean = clean.Substring(1);
            }

            // 2 ky tu dau anh xa ve chu so (ma tinh)
            char d0 = MapCharToDigit(clean[0]);
            char d1 = MapCharToDigit(clean[1]);
            int prov = (d0 - '0') * 10 + (d1 - '0');
            if (prov < 11) d0 = '2';

            // Ky tu thu 3: Neu la chu cai hop le ('K', 'A', 'H', 'E'...), giu nguyen 100%, khong ep doi sang ky tu khac
            char rawSeries = clean[2];
            char s2 = (char.IsLetter(rawSeries) || rawSeries == 'Đ' || rawSeries == 'đ')
                ? char.ToUpperInvariant(rawSeries)
                : MapDigitToLetter(rawSeries);

            // Kiem tra se-ri 2 chu cai dac biet (vi du: 29LD)
            if (clean.Length >= 4 && (char.IsLetter(clean[3]) || clean[3] == 'Đ' || clean[3] == 'đ'))
            {
                string s2Letters = $"{s2}{char.ToUpperInvariant(clean[3])}";
                if (ValidTwoLetterSeries.Contains(s2Letters))
                {
                    return $"{d0}{d1}{s2Letters}";
                }
            }

            return $"{d0}{d1}{s2}";
        }


        /// <summary>
        /// Chuyển đổi ký tự chữ số bị đọc nhầm sang chữ cái (Dùng cho ký tự sê-ri)
        /// '0' -> 'C'; '1' -> 'T'; '2' -> 'Z'; '3' -> 'E'; '4' -> 'A'; '5' -> 'S'; '6' -> 'G'; '8' -> 'B'
        /// </summary>
        public static char MapDigitToLetter(char c)
        {
            if (char.IsLetter(c) || c == 'Đ' || c == 'đ') return char.ToUpperInvariant(c);
            return c switch
            {
                '0' => 'C',
                '1' => 'T',
                '2' => 'Z',
                '3' => 'E',
                '4' => 'A',
                '5' => 'S',
                '6' => 'G',
                '8' => 'B',
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
                // TRƯỜNG HỢP 1: BIỂN 2 DÒNG HOẶC 2 PHÂN ĐOẠN (Line 1: Mã tỉnh & Sê-ri, Line 2: Dãy số)
                // ==========================================
                if (validLines.Count >= 2)
                {
                    string line1 = validLines[0];
                    string line2 = validLines[1];

                    // Nếu dòng 1 chỉ toàn số và dòng 2 có chữ cái -> Đảo lại thứ tự
                    if (line1.All(char.IsDigit) && line2.Any(c => char.IsLetter(c) || c == 'Đ' || c == 'đ') && !line1.Any(c => char.IsLetter(c) || c == 'Đ' || c == 'đ'))
                    {
                        var temp = line1;
                        line1 = line2;
                        line2 = temp;
                    }

                    // Khử lặp chuỗi ở dòng 1 (vd: '20200' -> '200', '2020C' -> '20C', '20HH' -> '20H')
                    line1 = Regex.Replace(line1, @"^(\d{2})\1(.*)$", "$1$2");
                    line1 = Regex.Replace(line1, @"^(\d{2})([A-Z0-9])\2+$", "$1$2");

                    // Nếu dòng 1 bị đọc ngược chữ cái ra trước (vd: 'H02' -> '20H', 'C20' -> '20C')
                    if (line1.Length >= 3 && (char.IsLetter(line1[0]) || line1[0] == 'Đ' || line1[0] == 'đ') && char.IsDigit(line1[1]) && char.IsDigit(line1[2]))
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

                    // 1. Chuẩn hóa Dòng 1 / Phân đoạn Trái (Mã tỉnh + Sê-ri):
                    // Đảm bảo phần đầu (Prefix) có đúng 3 ký tự (2 số tỉnh + 1 chữ cái) hoặc 4 ký tự sê-ri đặc biệt
                    string cleanLine1;
                    if (line1.Length >= 3)
                    {
                        char d0 = MapCharToDigit(line1[0]);
                        char d1 = MapCharToDigit(line1[1]);

                        // Mã tỉnh Việt Nam từ 11 đến 99 (nếu ra 00..09 thì sửa 0 đầu thành 2)
                        int prov = (d0 - '0') * 10 + (d1 - '0');
                        if (prov < 11) d0 = '2';

                        // Ký tự thứ 3 (Sê-ri): Nếu OCR ra CHỮ CÁI hợp lệ -> GIỮ NGUYÊN 100%, KHÔNG MAP LẠI. Chỉ map khi là CHỮ SỐ.
                        char rawSeries = line1[2];
                        char s2 = (char.IsLetter(rawSeries) || rawSeries == 'Đ' || rawSeries == 'đ')
                            ? char.ToUpperInvariant(rawSeries)
                            : (char.IsDigit(rawSeries) ? MapDigitToLetter(rawSeries) : char.ToUpperInvariant(rawSeries));

                        // Dòng 1 Ô tô chuẩn 3 ký tự (2 số tỉnh + 1 chữ sê-ri, vd: 20C, 20H, 29R)
                        // Chỉ nhận sê-ri 2 chữ cái nếu là sê-ri hợp lệ (LD, DA, KT, NG, QT...)
                        if (line1.Length >= 4 && (char.IsLetter(line1[3]) || line1[3] == 'Đ' || line1[3] == 'đ'))
                        {
                            string s2Letters = $"{s2}{char.ToUpperInvariant(line1[3])}";
                            if (ValidTwoLetterSeries.Contains(s2Letters))
                            {
                                cleanLine1 = $"{d0}{d1}{s2Letters}";
                            }
                            else
                            {
                                cleanLine1 = $"{d0}{d1}{s2}";
                            }
                        }
                        else
                        {
                            // Dòng 1 Ô tô chuẩn 3 ký tự (2 số tỉnh + 1 chữ sê-ri, vd: 20C, 20H, 21A, 30H)
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

                    // 2. Chuẩn hóa Dòng 2 / Phân đoạn Phải (Dãy số đuôi): Ép 100% về CHỮ SỐ
                    var sbLine2 = new StringBuilder();
                    foreach (char c in line2)
                    {
                        sbLine2.Append(MapCharToDigit(c));
                    }
                    string numStr = sbLine2.ToString();
                    if (numStr.Length > 5)
                    {
                        // Nếu lặp số ở cuối (vd xe tải: 007844 -> lấy 5 số đầu 00784)
                        if (numStr.Length == 6 && numStr[4] == numStr[5])
                        {
                            numStr = numStr.Substring(0, 5);
                        }
                        // Nếu dính overlap/nét thừa ở đầu phân đoạn (vd: 330356 hoặc 130356 -> lấy 5 số cuối 30356)
                        else
                        {
                            numStr = numStr.Substring(numStr.Length - 5);
                        }
                    }

                    return $"{cleanLine1}{numStr}";
                }

                // ==========================================
                // TRƯỜNG HỢP 2: BIỂN 1 DÒNG (Full chuỗi liền nhau)
                // ==========================================
                return CleanLongPlate(validLines[0]);
            }
            catch
            {
                return rawTexts.Count > 0 ? CleanRegex.Replace(rawTexts[0].ToUpperInvariant(), "") : string.Empty;
            }
        }

        /// <summary>
        /// Chuẩn hóa dành riêng cho biển số dài 1 dòng (xe con) theo quy chuẩn Việt Nam:
        /// - Lọc bỏ mọi ký tự không hợp lệ, bỏ dấu gạch '-' và dấu chấm '.'.
        /// - Tách cấu trúc chuẩn biển số xe con Việt Nam:
        ///   * Phần Prefix (3 ký tự): 2 số tỉnh + 1 chữ cái sê-ri (21A, 30H, 30K...).
        ///     Nếu ký tự thứ 1 hoặc 2 bị đọc thành chữ cái, map về chữ số (MapCharToDigit).
        ///     Nếu ký tự thứ 3 là chữ số, map về chữ cái qua MapDigitToLetter. Nếu đã là chữ cái hợp lệ thì giữ nguyên 100%.
        ///     (Hỗ trợ sê-ri 2 chữ cái đặc biệt như LD, DA, KT...).
        ///   * Phần Dãy số (4 hoặc 5 ký tự đuôi): Ép toàn bộ các ký tự còn lại thành chữ số qua MapCharToDigit.
        ///   * Ràng buộc độ dài: Đảm bảo chuỗi kết quả có đúng định dạng quy chuẩn (^\d{2}[A-ZĐ]\d{4,5}$).
        /// </summary>
        public static string CleanLongPlate(string rawText)
        {
            if (string.IsNullOrWhiteSpace(rawText))
                return string.Empty;

            // 1. Lọc bỏ ký tự không hợp lệ, dấu gạch ngang, dấu chấm
            string clean = CleanRegex.Replace(rawText.ToUpperInvariant(), "");
            if (clean.Length < 6)
                return clean;

            // 2. Chuẩn hóa Prefix (2 số tỉnh + 1 chữ cái sê-ri):
            char d0 = MapCharToDigit(clean[0]);
            char d1 = MapCharToDigit(clean[1]);
            int prov = (d0 - '0') * 10 + (d1 - '0');
            if (prov < 11) d0 = '2';

            // Ký tự thứ 3: Nếu là chữ cái hợp lệ -> giữ nguyên 100%, nếu là số -> map qua MapDigitToLetter
            char rawSeries = clean[2];
            char s2 = (char.IsLetter(rawSeries) || rawSeries == 'Đ' || rawSeries == 'đ')
                ? char.ToUpperInvariant(rawSeries)
                : MapDigitToLetter(rawSeries);

            string seriesStr;
            string tailRaw;

            // Nếu 2 ký tự đầu là mã tỉnh hợp lệ và ký tự thứ 3 đọc ra 'K', giữ nguyên tuyệt đối chữ 'K'
            if (s2 == 'K')
            {
                seriesStr = "K";
                tailRaw = clean.Substring(3);
            }
            // Kiểm tra sê-ri 2 chữ cái đặc biệt (vd: 29LD12345)
            else if (clean.Length >= 7 && (char.IsLetter(clean[3]) || clean[3] == 'Đ' || clean[3] == 'đ'))
            {
                string s2Letters = $"{s2}{char.ToUpperInvariant(clean[3])}";
                if (ValidTwoLetterSeries.Contains(s2Letters))
                {
                    seriesStr = s2Letters;
                    tailRaw = clean.Substring(4);
                }
                else
                {
                    seriesStr = $"{s2}";
                    tailRaw = clean.Substring(3);
                }
            }
            else
            {
                seriesStr = $"{s2}";
                tailRaw = clean.Substring(3);
            }

            // 3. Chuẩn hóa Dãy số (4 hoặc 5 ký tự đuôi): Ép toàn bộ thành chữ số
            var sbTail = new StringBuilder();
            foreach (char c in tailRaw)
            {
                sbTail.Append(MapCharToDigit(c));
            }
            string tailStr = sbTail.ToString();

            // Ràng buộc độ dài dãy số đăng ký xe con Việt Nam (4 hoặc 5 chữ số)
            if (tailStr.Length > 5)
            {
                // Nếu lặp số ở đầu (vd: 114746 -> 14746)
                if (tailStr.Length == 6 && tailStr[0] == tailStr[1])
                {
                    tailStr = tailStr.Substring(1);
                }
                // Mặc định giữ trọn vẹn tiền tố và 3 chữ số đầu tiên (kể cả số 2 như 30A24473), cắt bỏ số đuôi thừa nếu có
                else
                {
                    tailStr = tailStr.Substring(0, 5);
                }
            }

            return $"{d0}{d1}{seriesStr}{tailStr}";
        }

        /// <summary>
        /// Chuẩn hóa chuỗi biển số xe từ 2 phân đoạn (Segmented Dual-Crop) chuẩn quy chuẩn xe Việt Nam:
        /// - Phân đoạn Trái: Đảm bảo phần đầu (Prefix) có đúng 3 ký tự (2 số tỉnh + 1 chữ cái) hoặc 4 ký tự với sê-ri đặc biệt.
        /// - Phân đoạn Phải: Ép 100% về chữ số. Vì phân đoạn Phải có vùng overlap từ 38%, nếu phát sinh hiện tượng
        ///   dính nét chữ cái sê-ri khiến chuỗi số đuôi vượt quá 5 ký tự (ví dụ thành 6 số), tự động lấy đúng 5 chữ số CUỐI CÙNG.
        /// </summary>
        public static string NormalizeDualSegmentPlate(string leftRaw, string rightRaw)
        {
            if (string.IsNullOrWhiteSpace(leftRaw) && string.IsNullOrWhiteSpace(rightRaw))
                return string.Empty;

            string leftClean = CleanRegex.Replace((leftRaw ?? string.Empty).ToUpperInvariant(), "");
            string rightClean = CleanRegex.Replace((rightRaw ?? string.Empty).ToUpperInvariant(), "");

            // 1. Chuẩn hóa Prefix (Mã tỉnh + Sê-ri): Đảm bảo đúng 3 ký tự (2 số + 1 chữ)
            string prefix;
            if (leftClean.Length >= 3)
            {
                char d0 = MapCharToDigit(leftClean[0]);
                char d1 = MapCharToDigit(leftClean[1]);
                int prov = (d0 - '0') * 10 + (d1 - '0');
                if (prov < 11) d0 = '2';

                char rawSeries = leftClean[2];
                char s2 = (char.IsLetter(rawSeries) || rawSeries == 'Đ' || rawSeries == 'đ')
                    ? char.ToUpperInvariant(rawSeries)
                    : MapDigitToLetter(rawSeries);

                if (leftClean.Length >= 4 && (char.IsLetter(leftClean[3]) || leftClean[3] == 'Đ' || leftClean[3] == 'đ'))
                {
                    string s2Letters = $"{s2}{char.ToUpperInvariant(leftClean[3])}";
                    prefix = ValidTwoLetterSeries.Contains(s2Letters) ? $"{d0}{d1}{s2Letters}" : $"{d0}{d1}{s2}";
                }
                else
                {
                    prefix = $"{d0}{d1}{s2}";
                }
            }
            else if (leftClean.Length == 2)
            {
                char d0 = MapCharToDigit(leftClean[0]);
                char d1 = MapCharToDigit(leftClean[1]);
                if (((d0 - '0') * 10 + (d1 - '0')) < 11) d0 = '2';
                prefix = $"{d0}{d1}C";
            }
            else
            {
                prefix = leftClean;
            }

            // 2. Chuẩn hóa Tail (Dãy số đăng ký): Ép 100% sang chữ số
            var sbRight = new StringBuilder();
            foreach (char c in rightClean)
            {
                sbRight.Append(MapCharToDigit(c));
            }
            string numStr = sbRight.ToString();

            // Ràng buộc chặt chẽ: Nếu phân đoạn Phải bị dính nét chữ cái sê-ri / overlap (> 5 ký tự),
            // tự động lấy đúng 5 chữ số CUỐI CÙNG (chuẩn 5 số đăng ký xe con)
            if (numStr.Length > 5)
            {
                numStr = numStr.Substring(numStr.Length - 5);
            }

            return $"{prefix}{numStr}";
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

            // Biển 1 chữ cái sê-ri chuẩn (vd: 20C22717, 29C12345, 30A12345, 29B112345)
            if (Regex.IsMatch(clean, @"^\d{2}[A-ZĐ]\d{4,6}$") || Regex.IsMatch(clean, @"^\d{2}[A-ZĐ]\d{1}\d{4,5}$"))
                return true;

            // Biển sê-ri 2 chữ cái đặc biệt (vd: 29LD12345, 80NG12345)
            var match2 = Regex.Match(clean, @"^\d{2}([A-Z]{2})\d{4,5}$");
            if (match2.Success)
            {
                string series2 = match2.Groups[1].Value;
                return ValidTwoLetterSeries.Contains(series2);
            }

            return false;
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
