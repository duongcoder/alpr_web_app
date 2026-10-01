using System;
using System.Drawing;

namespace AlprSdk.Models
{
    /// <summary>
    /// Đối tượng kết quả nhận diện biển số xe (ALPR) phục vụ tích hợp phần mềm quản lý trạm cân xe tải.
    /// </summary>
    public class pr_plate : IDisposable
    {
        // Thuộc tính bắt buộc theo chuẩn giao tiếp phần mềm cân:
        
        /// <summary>
        /// Ảnh đầu vào (hoặc ảnh đã vẽ bounding box vị trí biển số)
        /// </summary>
        public Image? Image { get; set; }

        /// <summary>
        /// Ảnh crop cận cảnh biển số xe phục vụ lưu trữ chứng từ cân
        /// </summary>
        public Image? Image_plate { get; set; }

        /// <summary>
        /// Chữ trên biển số đã chuẩn hóa theo TCVN (VD: 20H-007.84 hoặc 30G-787.07)
        /// </summary>
        public string Plate_text { get; set; } = string.Empty;

        // Các thuộc tính nghiệp vụ mở rộng cho trạm cân:

        /// <summary>
        /// Biển số thô dạng ký tự liền không dấu gạch/chấm (VD: 20H00784)
        /// </summary>
        public string Raw_plate_text { get; set; } = string.Empty;

        /// <summary>
        /// Loại phương tiện: "Ô tô", "Xe máy", "Xe tải"
        /// </summary>
        public string Vehicle_type { get; set; } = "Ô tô";

        /// <summary>
        /// Màu biển: "Trắng", "Vàng", "Xanh", "Đỏ"
        /// </summary>
        public string Plate_color { get; set; } = "Trắng";

        /// <summary>
        /// Độ tin cậy nhận diện OCR (0.0 - 100.0%)
        /// </summary>
        public float Confidence { get; set; }

        /// <summary>
        /// Tổng độ trễ suy luận toàn trình (ms)
        /// </summary>
        public double Latency_ms { get; set; }

        /// <summary>
        /// Cờ đánh dấu biển số hợp lệ theo quy chuẩn biển số xe cơ giới Việt Nam
        /// </summary>
        public bool Is_valid { get; set; }

        /// <summary>
        /// Giải phóng bộ nhớ ảnh GDI+ an toàn tránh phình bộ nhớ khi trạm cân vận hành 24/7
        /// </summary>
        public void Dispose()
        {
            Image?.Dispose();
            Image = null;
            Image_plate?.Dispose();
            Image_plate = null;
            GC.SuppressFinalize(this);
        }
    }
}
