================================================================================
  ALPR SDK - GÓI TÍCH HỢP PHẦN MỀM TRẠM CÂN XE TẢI (VERSION 1.0 RELEASE)
  Cung cấp bởi: ALPR Engineering Team
  Công nghệ: YOLOv8 + PARSeq + HSV Plate Color + TCVN Rules
================================================================================

1. GIỚI THIỆU
-------------
Gói bàn giao ALPR SDK được thiết kế "Cắm-và-Chạy" (Plug-and-Play), chuyên dụng
để tích hợp nhận diện biển số xe tự động vào các phần mềm quản lý trạm cân xe tải.
Hỗ trợ đầy đủ các dòng xe: Xe tải nặng (20H, 20C...), xe con (30A, 30G, 30F...),
xe container, xe khách và xe máy.

2. CẤU TRÚC THƯ MỤC
-------------------
AlprSdk_Deliverable/
├── Bin/                       # Toàn bộ DLLs đã build Release (.NET 10 / Windows x64)
│   ├── AlprSdk.dll            # Thư viện lõi ALPR (Chỉ cần Add Reference file này)
│   ├── AlprSdk.xml            # Tài liệu IntelliSense tự động hiển thị gợi ý mã
│   ├── OpenCvSharp.dll        # Thư viện thị giác máy tính
│   ├── Microsoft.ML.OnnxRuntime.dll # Bộ tăng tốc suy luận AI ONNX Runtime
│   ├── System.Drawing.Common.dll # Thư viện xử lý Image/Bitmap chuẩn GDI+
│   ├── OpenCvSharpExtern.dll  # Native C++ runtime cho Windows x64
│   ├── onnxruntime.dll        # Native C++ runtime ONNX
│   └── runtimes/              # Thư mục runtimes chuẩn .NET cho Windows x64
│       └── win-x64/native/
│           ├── OpenCvSharpExtern.dll
│           └── onnxruntime.dll
├── Models/                    # Bộ đôi mô hình AI ONNX
│   ├── yolov8_plate.onnx      # Model phát hiện vị trí biển số xe (< 25ms)
│   └── parseq.onnx            # Model nhận diện chữ số OCR (< 30ms)
├── Samples/                   # Ảnh mẫu biển số xe thực tế
│   ├── sample_truck_20h.png   # Biển số xe tải 20H
│   └── sample_car_30g.png     # Biển số xe con 30A
├── Demo/                      # Project Demo mẫu (C# Console) chạy được ngay
│   ├── AlprSdkDemo.csproj     # Project file mẫu
│   └── Program.cs             # Mã nguồn mẫu gọi SDK
├── integration_guide.md       # Tài liệu đặc tả API và hướng dẫn tích hợp chi tiết
└── README.txt                 # Hướng dẫn nhanh này

3. CHẠY THỬ CHƯƠNG TRÌNH DEMO NGAY LẬP TỨC
------------------------------------------
Từ dòng lệnh (Terminal/PowerShell), chuyển vào thư mục Demo và chạy lệnh:
   cd Demo
   dotnet run

Chương trình Demo sẽ:
- Khởi tạo AlprDetector.
- Nạp 2 ảnh mẫu trong thư mục Samples.
- Đọc biển số, loại xe, màu biển, độ tin cậy và thời gian xử lý.
- Tự động lưu ảnh cắt cận cảnh biển số và ảnh vẽ khung nhận diện vào thư mục Demo/output/.

4. 3 DÒNG CODE TÍCH HỢP VÀO PHẦN MỀM CÂN CỦA BẠN
------------------------------------------------
Trong ứng dụng WinForms / WPF / Console của bạn:

   // Bước 1: Khởi tạo Detector (Chỉ gọi 1 lần khi mở phần mềm trạm cân)
   using var detector = new AlprSdk.AlprDetector(@"C:\DuongDan\Models");

   // Bước 2: Gọi nhận diện từ ảnh camera cân (System.Drawing.Image hoặc file path)
   using var result = detector.get_plate(cameraImage);

   // Bước 3: Lấy dữ liệu biển số và ảnh chứng từ lưu vào phiếu cân
   txtBienSo.Text      = result.Plate_text;      // VD: "20H-007.84"
   txtLoaiXe.Text      = result.Vehicle_type;    // VD: "Xe tải", "Ô tô"
   txtMauBien.Text     = result.Plate_color;     // VD: "Trắng", "Vàng"
   picXeToanCanh.Image = result.Image;           // Ảnh toàn cảnh có vẽ khung nhận diện
   picBienSoCrop.Image = result.Image_plate;     // Ảnh crop biển số lưu CSDL

5. ĐẶC TẢ DỮ LIỆU ĐẦU RA (pr_plate)
-----------------------------------
- Image: System.Drawing.Image (Ảnh toàn cảnh vẽ bounding box)
- Image_plate: System.Drawing.Image (Ảnh crop cận cảnh biển số độ phân giải cao)
- Plate_text: string (Biển số chuẩn TCVN, VD: "20H-007.84")
- Raw_plate_text: string (Biển số ký tự liền, VD: "20H00784")
- Vehicle_type: string ("Ô tô", "Xe tải", "Xe máy")
- Plate_color: string ("Trắng", "Vàng", "Xanh", "Đỏ")
- Confidence: float (Độ tin cậy OCR 0 - 100%)
- Latency_ms: double (Độ trễ toàn trình AI < 80ms)
- Is_valid: bool (Cờ hợp lệ theo quy chuẩn đăng kiểm VN)

6. LƯU Ý KHI TRIỂN KHAI VẬN HÀNH 24/7 TẠI TRẠM CÂN
--------------------------------------------------
- Sao chép toàn bộ file trong thư mục `Bin/` và `Models/` vào thư mục Release của trạm cân.
- Thư viện đã được khóa đồng bộ đa luồng (Thread-Safe Lock), hỗ trợ gọi đồng thời
  từ nhiều camera (camera trước, camera sau, camera biển phụ).
- Giải phóng `using var result = ...` hoặc gọi `result.Dispose()` sau khi in phiếu cân
  để hệ thống chạy liên tục nhiều tháng mà RAM hoàn toàn ổn định (Zero Memory Leak).

Xem hướng dẫn chi tiết tại file: integration_guide.md
