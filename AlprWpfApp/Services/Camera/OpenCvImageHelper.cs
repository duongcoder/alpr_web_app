using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCvSharp;

namespace AlprWpfApp.Services.Camera
{
    public static class OpenCvImageHelper
    {
        /// <summary>
        /// Chuyển đổi siêu tốc từ OpenCvSharp Mat sang WPF BitmapSource (Direct Memory Zero-GDI+ Leak) với Freeze() dùng cho đa luồng UI.
        /// </summary>
        public static BitmapSource? MatToBitmapSource(Mat? mat)
        {
            if (mat == null || mat.IsDisposed || mat.Empty())
                return null;

            try
            {
                PixelFormat format;
                if (mat.Channels() == 1)
                    format = PixelFormats.Gray8;
                else if (mat.Channels() == 3)
                    format = PixelFormats.Bgr24;
                else if (mat.Channels() == 4)
                    format = PixelFormats.Bgra32;
                else
                    return null;

                var bs = BitmapSource.Create(
                    mat.Cols,
                    mat.Rows,
                    96,
                    96,
                    format,
                    null,
                    mat.Data,
                    (int)mat.Step() * mat.Rows,
                    (int)mat.Step()
                );

                if (bs.CanFreeze && !bs.IsFrozen)
                {
                    bs.Freeze();
                }
                return bs;
            }
            catch
            {
                // Fallback qua bộ đệm PNG/BMP nếu cần
                try
                {
                    Cv2.ImEncode(".bmp", mat, out byte[] buf);
                    using var ms = new MemoryStream(buf);
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = ms;
                    bi.EndInit();
                    bi.Freeze();
                    return bi;
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Tạo một bản vẽ overlay bounding box trên frame Mat
        /// </summary>
        public static Mat DrawBoundingBox(Mat source, OpenCvSharp.Rect box, string label, Scalar boxColor, Scalar textColor)
        {
            var displayMat = source.Clone();
            
            // Vẽ hình chữ nhật viền dày 2px
            Cv2.Rectangle(displayMat, box, boxColor, 2, LineTypes.AntiAlias);

            // Vẽ background cho nhãn chữ
            var baseLine = 0;
            var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.6, 2, out baseLine);
            var labelY = Math.Max(box.Y, textSize.Height + 5);
            var labelRect = new OpenCvSharp.Rect(box.X, labelY - textSize.Height - 5, textSize.Width + 10, textSize.Height + 8);
            
            // Đảm bảo không vượt quá biên ảnh
            labelRect.X = Math.Max(0, Math.Min(labelRect.X, source.Cols - labelRect.Width));
            labelRect.Y = Math.Max(0, Math.Min(labelRect.Y, source.Rows - labelRect.Height));

            Cv2.Rectangle(displayMat, labelRect, boxColor, -1); // Filled
            Cv2.PutText(displayMat, label, new OpenCvSharp.Point(labelRect.X + 5, labelRect.Y + textSize.Height + 2),
                HersheyFonts.HersheySimplex, 0.6, textColor, 2, LineTypes.AntiAlias);

            return displayMat;
        }

        /// <summary>
        /// Vẽ vùng nhận diện bàn cân (Scale ROI) và Bounding Box của biển số chiến thắng
        /// </summary>
        public static Mat DrawRoiAndPlateOverlay(Mat source, Rect2f scaleRoi, OpenCvSharp.Rect? winningBox, string? plateLabel, bool showRoi = true)
        {
            if (source == null || source.IsDisposed || source.Empty())
                return source?.Clone() ?? new Mat();

            var displayMat = source.Clone();

            // 1. Vẽ Vùng nhận diện bàn cân (Scale ROI)
            if (showRoi)
            {
                int roiX = (int)Math.Round(scaleRoi.X * displayMat.Cols);
                int roiY = (int)Math.Round(scaleRoi.Y * displayMat.Rows);
                int roiW = (int)Math.Round(scaleRoi.Width * displayMat.Cols);
                int roiH = (int)Math.Round(scaleRoi.Height * displayMat.Rows);

                // Giới hạn trong kích thước ảnh
                roiX = Math.Max(0, Math.Min(roiX, displayMat.Cols - 1));
                roiY = Math.Max(0, Math.Min(roiY, displayMat.Rows - 1));
                roiW = Math.Max(1, Math.Min(roiW, displayMat.Cols - roiX));
                roiH = Math.Max(1, Math.Min(roiH, displayMat.Rows - roiY));

                var roiRect = new OpenCvSharp.Rect(roiX, roiY, roiW, roiH);

                // Vẽ viền ngoài ROI màu Amber / Cyan (BGR: 255, 191, 0)
                Cv2.Rectangle(displayMat, roiRect, new Scalar(255, 191, 0), 2, LineTypes.AntiAlias);

                // Vẽ nhãn ROI bàn cân
                string roiTag = "ROI: VUNG BAN CAN";
                int roiBaseline = 0;
                var roiTagSize = Cv2.GetTextSize(roiTag, HersheyFonts.HersheySimplex, 0.45, 1, out roiBaseline);
                var roiTagRect = new OpenCvSharp.Rect(roiX, Math.Max(0, roiY - roiTagSize.Height - 6), roiTagSize.Width + 8, roiTagSize.Height + 6);

                Cv2.Rectangle(displayMat, roiTagRect, new Scalar(255, 191, 0), -1);
                Cv2.PutText(displayMat, roiTag, new OpenCvSharp.Point(roiTagRect.X + 4, roiTagRect.Y + roiTagSize.Height + 1),
                    HersheyFonts.HersheySimplex, 0.45, new Scalar(15, 23, 42), 1, LineTypes.AntiAlias);
            }

            // 2. Vẽ Bounding Box biển số xe chiến thắng
            if (winningBox.HasValue && winningBox.Value.Width > 0 && winningBox.Value.Height > 0)
            {
                var wb = winningBox.Value;
                int bx = Math.Max(0, Math.Min(wb.X, displayMat.Cols - 1));
                int by = Math.Max(0, Math.Min(wb.Y, displayMat.Rows - 1));
                int bw = Math.Max(1, Math.Min(wb.Width, displayMat.Cols - bx));
                int bh = Math.Max(1, Math.Min(wb.Height, displayMat.Rows - by));
                var cleanBox = new OpenCvSharp.Rect(bx, by, bw, bh);

                // Khung viền Xanh Neon (BGR: 0, 255, 127) dày 3px
                Cv2.Rectangle(displayMat, cleanBox, new Scalar(0, 255, 127), 3, LineTypes.AntiAlias);

                // Vẽ nhãn Biển số
                string labelText = string.IsNullOrWhiteSpace(plateLabel) ? "XE TREN CAN" : $"XE TREN CAN: {plateLabel}";
                int baseLine = 0;
                var textSize = Cv2.GetTextSize(labelText, HersheyFonts.HersheySimplex, 0.55, 2, out baseLine);
                int labelY = Math.Max(cleanBox.Y, textSize.Height + 6);
                var labelRect = new OpenCvSharp.Rect(cleanBox.X, labelY - textSize.Height - 6, textSize.Width + 10, textSize.Height + 8);

                labelRect.X = Math.Max(0, Math.Min(labelRect.X, displayMat.Cols - labelRect.Width));
                labelRect.Y = Math.Max(0, Math.Min(labelRect.Y, displayMat.Rows - labelRect.Height));

                Cv2.Rectangle(displayMat, labelRect, new Scalar(0, 255, 127), -1);
                Cv2.PutText(displayMat, labelText, new OpenCvSharp.Point(labelRect.X + 5, labelRect.Y + textSize.Height + 2),
                    HersheyFonts.HersheySimplex, 0.55, new Scalar(0, 0, 0), 2, LineTypes.AntiAlias);
            }

            return displayMat;
        }

        /// <summary>
        /// Cắt ảnh an toàn có padding
        /// </summary>
        public static Mat? CropMatSafe(Mat src, OpenCvSharp.Rect box, int padding = 5)
        {
            if (src == null || src.IsDisposed || src.Empty())
                return null;

            int x1 = Math.Max(0, box.X - padding);
            int y1 = Math.Max(0, box.Y - padding);
            int x2 = Math.Min(src.Cols, box.X + box.Width + padding);
            int y2 = Math.Min(src.Rows, box.Y + box.Height + padding);

            if (x2 <= x1 || y2 <= y1)
                return null;

            var cropRect = new OpenCvSharp.Rect(x1, y1, x2 - x1, y2 - y1);
            return new Mat(src, cropRect).Clone();
        }
    }
}
