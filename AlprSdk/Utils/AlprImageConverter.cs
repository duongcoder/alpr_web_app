using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using OpenCvSharp;

namespace AlprSdk.Utils
{
    /// <summary>
    /// Tiện ích chuyển đổi bộ nhớ siêu tốc và an toàn tuyệt đối (Zero Memory Leak)
    /// giữa System.Drawing.Image/Bitmap, byte buffer và OpenCvSharp Mat.
    /// Thiết kế đặc biệt cho phần mềm trạm cân xe tải chạy liên tục 24/7.
    /// </summary>
    public static class AlprImageConverter
    {
        /// <summary>
        /// Chuyển đổi an toàn từ System.Drawing.Image sang OpenCvSharp Mat (BGR).
        /// Giải phóng toàn bộ bộ nhớ tạm sau khi hoàn tất.
        /// </summary>
        public static Mat ImageToMat(Image image)
        {
            if (image == null) throw new ArgumentNullException(nameof(image));

            using var ms = new MemoryStream();
            // Lưu ảnh dưới dạng BMP trong RAM để đảm bảo tính toàn vẹn pixel và tương thích mọi pixel format
            image.Save(ms, ImageFormat.Bmp);
            byte[] bytes = ms.ToArray();
            return Cv2.ImDecode(bytes, ImreadModes.Color);
        }

        /// <summary>
        /// Chuyển đổi an toàn từ OpenCvSharp Mat sang System.Drawing.Image độc lập hoàn toàn
        /// Không bị khóa luồng file hay stream (Tránh triệt để lỗi "A generic error occurred in GDI+").
        /// </summary>
        public static Image? MatToImage(Mat? mat)
        {
            if (mat == null || mat.IsDisposed || mat.Empty())
                return null;

            Cv2.ImEncode(".png", mat, out byte[] buf);
            using var ms = new MemoryStream(buf);
            using var tempBmp = new Bitmap(ms);
            // Tạo bản sao độc lập (detached bitmap) ngắt đứt hoàn toàn tham chiếu tới MemoryStream
            return new Bitmap(tempBmp);
        }

        /// <summary>
        /// Giải mã mảng byte ảnh sang OpenCvSharp Mat (BGR).
        /// </summary>
        public static Mat BytesToMat(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException("Mảng byte ảnh không được rỗng", nameof(bytes));

            return Cv2.ImDecode(bytes, ImreadModes.Color);
        }

        /// <summary>
        /// Mã hóa OpenCvSharp Mat thành mảng byte ảnh.
        /// </summary>
        public static byte[] MatToBytes(Mat mat, string extension = ".jpg")
        {
            if (mat == null || mat.IsDisposed || mat.Empty())
                return Array.Empty<byte>();

            Cv2.ImEncode(extension, mat, out byte[] buf);
            return buf;
        }

        /// <summary>
        /// Vẽ bounding box nhận diện lên ảnh bản sao phục vụ hiển thị/lưu trữ trạm cân
        /// </summary>
        public static Mat DrawPlateBox(Mat source, Rect box, string? label = null, Scalar? boxColor = null)
        {
            if (source == null || source.IsDisposed || source.Empty())
                return source?.Clone() ?? new Mat();

            var display = source.Clone();
            var color = boxColor ?? new Scalar(0, 255, 0); // Green BGR

            // Đảm bảo box nằm gọn trong kích thước frame
            int bx = Math.Clamp(box.X, 0, display.Cols - 1);
            int by = Math.Clamp(box.Y, 0, display.Rows - 1);
            int bw = Math.Clamp(box.Width, 1, display.Cols - bx);
            int bh = Math.Clamp(box.Height, 1, display.Rows - by);
            var cleanBox = new Rect(bx, by, bw, bh);

            // Vẽ viền chữ nhật dày 3px
            Cv2.Rectangle(display, cleanBox, color, 3, LineTypes.AntiAlias);

            // Vẽ nhãn văn bản nếu có
            if (!string.IsNullOrWhiteSpace(label))
            {
                int baseLine = 0;
                var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.7, 2, out baseLine);
                int labelY = Math.Max(cleanBox.Y, textSize.Height + 8);
                var labelRect = new Rect(cleanBox.X, labelY - textSize.Height - 8, textSize.Width + 12, textSize.Height + 10);
                
                labelRect.X = Math.Clamp(labelRect.X, 0, display.Cols - labelRect.Width);
                labelRect.Y = Math.Clamp(labelRect.Y, 0, display.Rows - labelRect.Height);

                Cv2.Rectangle(display, labelRect, color, -1);
                Cv2.PutText(display, label, new OpenCvSharp.Point(labelRect.X + 6, labelRect.Y + textSize.Height + 4),
                    HersheyFonts.HersheySimplex, 0.7, new Scalar(0, 0, 0), 2, LineTypes.AntiAlias);
            }

            return display;
        }

        /// <summary>
        /// Cắt ảnh an toàn có padding bảo vệ biên
        /// </summary>
        public static Mat? CropMatSafe(Mat src, Rect box, int padding = 5)
        {
            if (src == null || src.IsDisposed || src.Empty())
                return null;

            int x1 = Math.Max(0, box.X - padding);
            int y1 = Math.Max(0, box.Y - padding);
            int x2 = Math.Min(src.Cols, box.X + box.Width + padding);
            int y2 = Math.Min(src.Rows, box.Y + box.Height + padding);

            if (x2 <= x1 || y2 <= y1)
                return null;

            var cropRect = new Rect(x1, y1, x2 - x1, y2 - y1);
            return new Mat(src, cropRect).Clone();
        }
    }
}
