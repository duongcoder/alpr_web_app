# HƯỚNG DẪN TÍCH HỢP ALPR SDK VÀO PHẦN MỀM TRẠM CÂN XE TẢI

> **Thư viện**: `AlprSdk.dll` (.NET 10 / .NET 8 / C#)  
> **Pipeline AI**: YOLOv8 Detect + PARSeq Decoupled OCR + HSV Plate Color + TCVN PostProcessor  
> **Đặc tả nghiệp vụ**: Chuẩn giao tiếp phần mềm cân xe tải 24/7 (Zero Memory Leak, Thread-Safe)

---

## 1. Danh Mục Tệp Triển Khai (Deployment Files)

Sao chép toàn bộ các tệp từ thư mục build Release (`AlprSdk/bin/Release/net10.0-windows/`) vào thư mục chạy của phần mềm trạm cân:

```text
├── [Thư mục ứng dụng trạm cân]
│   ├── TramCanXeTai.exe
│   ├── AlprSdk.dll                        <-- Thư viện nhận diện chính
│   ├── OpenCvSharp.dll                    <-- Xử lý ảnh thị giác máy tính
│   ├── Microsoft.ML.OnnxRuntime.dll       <-- Bộ tăng tốc suy luận AI ONNX
│   ├── System.Drawing.Common.dll          <-- Chuẩn ảnh GDI+
│   ├── runtimes/                          <-- DLL native C++ cho Windows x64
│   │   └── win-x64/native/
│   │       ├── onnxruntime.dll
│   │       └── OpenCvSharpExtern.dll
│   └── Models/ (hoặc models/)
│       ├── yolov8_plate.onnx              <-- Mô hình phát hiện biển số
│       └── parseq.onnx                    <-- Mô hình đọc chữ số OCR
```

---

## 2. Cách Add Reference `AlprSdk.dll`

### Cách 1: Thêm Reference trong Visual Studio
1. Nhấp chuột phải vào dự án phần mềm trạm cân trong **Solution Explorer** -> chọn **Add** -> **Project Reference...** (hoặc **Reference...**).
2. Nhấn nút **Browse...** -> trỏ đến file `AlprSdk.dll`.
3. Nhấn **OK**.

### Cách 2: Thêm trực tiếp vào file `.csproj`
```xml
<ItemGroup>
  <Reference Include="AlprSdk">
    <HintPath>path\to\AlprSdk.dll</HintPath>
  </Reference>
</ItemGroup>
```

---

## 3. Code Tích Hợp 3 Dòng Siêu Nhanh

```csharp
using AlprSdk;
using AlprSdk.Models;

// BƯỚC 1: Khởi tạo Detector (Khởi tạo duy nhất 1 lần khi khởi động phần mềm trạm cân)
using var detector = new AlprDetector(@"C:\WeighStation\Models");

// BƯỚC 2: Gọi nhận diện từ ảnh chụp camera trạm cân
using var result = detector.get_plate(cameraImage);

// BƯỚC 3: Điền kết quả vào phiếu cân và lưu trữ ảnh chứng từ
txtBienSo.Text      = result.Plate_text;      // VD: "20H-007.84" hoặc "30G-787.07"
txtLoaiXe.Text      = result.Vehicle_type;    // VD: "Ô tô", "Xe tải", "Xe máy"
txtMauBien.Text     = result.Plate_color;     // VD: "Trắng", "Vàng", "Xanh", "Đỏ"
picXeToanCanh.Image = result.Image;           // Ảnh toàn cảnh có vẽ bounding box vị trí biển số
picBienSoCrop.Image = result.Image_plate;     // Ảnh cắt cận cảnh biển số lưu cơ sở dữ liệu
```

---

## 4. Đặc Tả Dữ Liệu Đầu Ra `pr_plate`

Lớp kết quả trả về `pr_plate` tuân thủ chính xác đặc tả tích hợp:

| Thuộc Tính | Kiểu Dữ Liệu | Ý Nghĩa / Ví Dụ Nghiệp Vụ Cân |
| :--- | :--- | :--- |
| `Image` | `System.Drawing.Image` | Ảnh đầu vào đã vẽ khung Bounding Box xanh lá tại vị trí biển |
| `Image_plate` | `System.Drawing.Image` | Ảnh crop cận cảnh biển số xe (độ nét cao, không vỡ hạt) |
| `Plate_text` | `string` | Biển số đã chuẩn hóa theo TCVN (VD: `20H-007.84`, `30G-787.07`) |
| `Raw_plate_text` | `string` | Biển số dạng ký tự liền (VD: `20H00784`, `30G78707`) |
| `Vehicle_type` | `string` | Phân loại xe: `"Ô tô"`, `"Xe tải"`, `"Xe máy"` |
| `Plate_color` | `string` | Màu biển số theo HSV: `"Trắng"`, `"Vàng"`, `"Xanh"`, `"Đỏ"` |
| `Confidence` | `float` | Độ tin cậy OCR (thang 0.0 - 100.0%) |
| `Latency_ms` | `double` | Tổng thời gian suy luận toàn trình AI (ms) (< 80ms) |
| `Is_valid` | `bool` | Cờ hợp lệ quy chuẩn đăng kiểm và biển số xe cơ giới Việt Nam |

---

## 5. Danh Sách Các Overload Nhận Diện Linh Hoạt

Hỗ trợ tất cả các định dạng nguồn ảnh camera trạm cân:

```csharp
// 1. Nhận diện từ đối tượng System.Drawing.Image / Bitmap (WinForms / GDI+)
pr_plate res1 = detector.get_plate(image);
pr_plate res1Alias = detector.GetPlate(image);

// 2. Nhận diện trực tiếp từ đường dẫn file ảnh trên ổ cứng
pr_plate res2 = detector.get_plate(@"D:\Snapshots\cam_front_01.jpg");
pr_plate res2Alias = detector.GetPlate(@"D:\Snapshots\cam_front_01.jpg");

// 3. Nhận diện từ mảng byte (Snapshot qua TCP/HTTP/RTSP của camera IP)
byte[] imageBytes = File.ReadAllBytes(@"D:\Snapshots\cam_front_01.jpg");
pr_plate res3 = detector.get_plate(imageBytes);
pr_plate res3Alias = detector.GetPlate(imageBytes);

// 4. Nhận diện trực tiếp từ OpenCvSharp.Mat (Xử lý frame video siêu tốc)
using var mat = Cv2.ImRead(@"D:\Snapshots\cam_front_01.jpg");
pr_plate res4 = detector.get_plate(mat);
pr_plate res4Alias = detector.GetPlate(mat);
```

---

## 6. Đảm Bảo Vận Hành 24/7: Thread-Safe & Zero Memory Leak

1. **Khóa luồng an toàn (Thread-Safe Lock)**:
   - `AlprDetector` tích hợp cơ chế đồng bộ hóa `lock` tự động bên trong.
   - Có thể dùng chung 1 instance `detector` duy nhất cho nhiều camera (Camera trước, Camera sau, Camera thùng xe) gọi đồng thời mà không bị crash hay xung đột vùng nhớ.

2. **Chống rò rỉ bộ nhớ (Zero Memory Leak)**:
   - Toàn bộ `OpenCvSharp.Mat` và buffer tính toán trung gian được tự động giải phóng (`Dispose`) ngay lập tức sau khi hoàn tất trích xuất.
   - Các ảnh `Image` và `Image_plate` được chuyển đổi thành `Bitmap` độc lập (detached memory), ngắt đứt hoàn toàn với `MemoryStream` bên dưới.
   - Để trạm cân chạy liên tục nhiều tháng không tăng RAM, hãy giải phóng đối tượng `pr_plate` sau khi in phiếu cân hoặc lưu DB:
     ```csharp
     using (var result = detector.get_plate(frame))
     {
         // Lưu CSDL, hiển thị UI...
     } // result tự động giải phóng Image và Image_plate
     ```
   - Khi tắt phần mềm trạm cân, gọi `detector.Dispose()` để giải phóng `InferenceSession` sạch sẽ.
