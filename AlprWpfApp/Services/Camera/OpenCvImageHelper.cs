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
