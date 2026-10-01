# HƯỚNG DẪN TÍCH HỢP ALPR SDK CHO DỰ ÁN .NET FRAMEWORK 4.8 (WINFORMS / WPF)

> **Thư viện**: `AlprSdk.Net48.dll`  
> **Nền tảng**: .NET Framework 4.8 (Windows x64)  
> **Pipeline AI**: YOLOv8 Detect + PARSeq Decoupled OCR + HSV Color + PlatePostProcessor TCVN  
> **Đặc tả nghiệp vụ**: Chuẩn giao tiếp phần mềm trạm cân xe tải 24/7 (Zero Memory Leak, Thread-Safe)

---

## 1. YÊU CẦU CẤU HÌNH BẮT BUỘC TRONG VISUAL STUDIO (QUAN TRỌNG NHẤT)

> [!CAUTION]
> Các mô hình AI (YOLOv8, PARSeq) và lõi thị giác máy tính OpenCV C++ yêu cầu môi trường thực thi **64-bit (x64)**.
> Nếu phần mềm cân chạy ở chế độ 32-bit (x86), ứng dụng sẽ phát sinh lỗi `BadImageFormatException`.

Vui lòng thực hiện 2 thao tác sau trên project trạm cân của bạn:

1. **Chuyển Platform target sang x64**:
   - Nhấp chuột phải vào Project trong Visual Studio -> Chọn **Properties**.
   - Chọn tab **Build**.
   - Tại mục **Platform target**: Chuyển từ `Any CPU` (hoặc `x86`) thành **`x64`**.

2. **Bỏ chọn "Prefer 32-bit"**:
   - Trong tab **Build**, nếu có ô chọn **Prefer 32-bit**, **BẮT BUỘC BỎ CHỌN** (Uncheck).

---

## 2. Danh Mục Các Tệp Triển Khai (Deployment Files)

Sao chép toàn bộ các tệp từ thư mục Release `AlprSdk.Net48\bin\Release\net48\` vào thư mục chạy `.exe` của phần mềm cân:

```text
├── [Thư mục ứng dụng trạm cân của bạn]
│   ├── TramCanXeTai.exe
│   │
│   ├── AlprSdk.Net48.dll               <-- Hạt nhân thư viện ALPR (.NET 4.8)
│   ├── AlprSdk.Net48.xml               <-- Tài liệu gợi ý code IntelliSense
│   ├── OpenCvSharp.dll                 <-- Thư viện OpenCV C#
│   ├── OpenCvSharpExtern.dll           <-- Native C++ OpenCV (x64)
│   ├── Microsoft.ML.OnnxRuntime.dll    <-- Bộ tăng tốc suy luận AI ONNX
│   ├── onnxruntime.dll                 <-- Native C++ ONNX Runtime (x64)
│   │
│   ├── System.Memory.dll               <-- Thư viện đệm bộ nhớ tối ưu
│   ├── System.Buffers.dll
│   ├── System.Numerics.Vectors.dll
│   ├── System.Runtime.CompilerServices.Unsafe.dll
│   ├── System.ValueTuple.dll
│   │
│   └── Models/ (hoặc models/)
│       ├── yolov8_plate.onnx           <-- Mô hình phát hiện biển số (< 25ms)
│       └── parseq.onnx                 <-- Mô hình đọc chữ số OCR (< 30ms)
```

---

## 3. Cách Thêm Reference Vào Project WinForms .NET 4.8

1. Trong Visual Studio, nhấp chuột phải mục **References** của project trạm cân -> Chọn **Add Reference...**.
2. Nhấn nút **Browse...** -> Trỏ đến tệp **`AlprSdk.Net48.dll`**.
3. Thêm các tham chiếu hệ thống chuẩn nếu chưa có (mặc định WinForms đã có sẵn):
   - `System.Drawing`
   - `System.Windows.Forms`
4. Nhấn **OK** để hoàn tất.

---

## 4. Code Mẫu 3 Dòng Tích Hợp Vào WinForms

```csharp
using System;
using System.Drawing;
using System.Windows.Forms;
using AlprSdk;
using AlprSdk.Models;

namespace TramCanXeTai
{
    public partial class FormMain : Form
    {
        // 1. Khởi tạo Detector duy nhất 1 lần khi mở phần mềm cân
        private AlprDetector _detector;

        public FormMain()
        {
            InitializeComponent();
        }

        private void FormMain_Load(object sender, EventArgs e)
        {
            // Trỏ đường dẫn đến thư mục chứa 2 file ONNX (Models/)
            string modelsPath = Application.StartupPath + @"\Models";
            _detector = new AlprDetector(modelsPath);
        }

        // 2. Nhận diện khi xe vào bàn cân (ấn nút Cân hoặc tự động từ Camera IP)
        private void btnNhanDien_Click(object sender, EventArgs e)
        {
            // Nguồn ảnh: từ PictureBox hoặc trực tiếp file ảnh chụp camera
            Image cameraFrame = picCamera.Image;

            using (pr_plate result = _detector.get_plate(cameraFrame))
            {
                // 3. Đổ dữ liệu lên phiếu cân
                txtBienSo.Text      = result.Plate_text;      // VD: "20H-007.84"
                txtBienSoTho.Text   = result.Raw_plate_text;  // VD: "20H00784"
                txtLoaiXe.Text      = result.Vehicle_type;    // VD: "Ô tô", "Xe tải"
                txtMauBien.Text     = result.Plate_color;     // VD: "Trắng", "Vàng"
                lblDoTinCay.Text    = result.Confidence.ToString("F1") + "%";
                lblDoTre.Text       = result.Latency_ms.ToString("F1") + " ms";

                // Hiển thị ảnh chụp có vẽ khung nhận diện và ảnh cắt cận cảnh biển
                picToanCanh.Image   = (Image)result.Image.Clone();
                picBienSoCrop.Image = (Image)result.Image_plate.Clone();
            }
        }

        // Giải phóng tài nguyên AI khi tắt trạm cân
        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            _detector?.Dispose();
        }
    }
}
```

---

## 5. Danh Sách Các Overload Nhận Diện Linh Hoạt

Hỗ trợ mọi định dạng nguồn ảnh từ camera trạm cân:

```csharp
// 1. Từ System.Drawing.Image / Bitmap (GDI+ chuẩn WinForms)
pr_plate res1 = _detector.get_plate(myImage);
pr_plate res1Alias = _detector.GetPlate(myImage);

// 2. Trực tiếp từ đường dẫn file ảnh trên ổ cứng
pr_plate res2 = _detector.get_plate(@"D:\Snapshots\xe_vao_01.jpg");
pr_plate res2Alias = _detector.GetPlate(@"D:\Snapshots\xe_vao_01.jpg");

// 3. Từ mảng byte (luồng snapshot HTTP / RTSP từ Camera IP)
byte[] imageBytes = File.ReadAllBytes(@"D:\Snapshots\xe_vao_01.jpg");
pr_plate res3 = _detector.get_plate(imageBytes);
pr_plate res3Alias = _detector.GetPlate(imageBytes);

// 4. Từ OpenCvSharp.Mat (Xử lý frame video trực tiếp không qua GDI+)
using var mat = OpenCvSharp.Cv2.ImRead(@"D:\Snapshots\xe_vao_01.jpg");
pr_plate res4 = _detector.get_plate(mat);
```

---

## 6. Đặc Tả Dữ Liệu Kết Quả `pr_plate`

| Thuộc Tính | Kiểu Dữ Liệu | Ý Nghĩa / Nghiệp Vụ Trạm Cân |
| :--- | :--- | :--- |
| `Image` | `System.Drawing.Image` | Ảnh đầu vào đã vẽ khung Bounding Box xanh lá tại vị trí biển |
| `Image_plate` | `System.Drawing.Image` | Ảnh crop cận cảnh biển số xe phục vụ lưu trữ chứng từ cân |
| `Plate_text` | `string` | Biển số chuẩn hóa TCVN (VD: `20H-007.84`, `30G-787.07`) |
| `Raw_plate_text` | `string` | Biển số thô dạng ký tự liền (VD: `20H00784`, `30G78707`) |
| `Vehicle_type` | `string` | Phân loại xe: `"Ô tô"`, `"Xe tải"`, `"Xe máy"` |
| `Plate_color` | `string` | Màu biển số theo HSV: `"Trắng"`, `"Vàng"`, `"Xanh"`, `"Đỏ"` |
| `Confidence` | `float` | Độ tin cậy OCR (thang 0.0 - 100.0%) |
| `Latency_ms` | `double` | Tổng thời gian suy luận toàn trình AI (< 80ms) |
| `Is_valid` | `bool` | Cờ hợp lệ quy chuẩn đăng kiểm và biển số xe Việt Nam |

---

## 7. Khắc Phục Sự Cố Thường Gặp (Troubleshooting)

1. **Lỗi `BadImageFormatException`**:
   - *Nguyên nhân*: Ứng dụng đang chạy ở chế độ 32-bit (x86).
   - *Cách khắc phục*: Vào **Project Properties** -> **Build** -> chuyển **Platform target** thành **x64** và bỏ chọn **Prefer 32-bit**.

2. **Lỗi `DllNotFoundException: Unable to load DLL 'OpenCvSharpExtern'` hoặc `'onnxruntime'`**:
   - *Nguyên nhân*: Chưa copy file native DLL vào thư mục chạy của file `.exe`.
   - *Cách khắc phục*: Đảm bảo hai file `OpenCvSharpExtern.dll` và `onnxruntime.dll` nằm cùng thư mục với file `.exe` (hoặc trong thư mục con `dll\x64\`).

3. **Vận hành liên tục 24/7/365 không rò rỉ RAM (Zero Memory Leak)**:
   - Sau khi in phiếu cân hoặc lưu dữ liệu vào SQL Server, luôn sử dụng khối `using (var result = _detector.get_plate(...))` hoặc gọi `result.Dispose()` để giải phóng bộ nhớ ảnh GDI+.
