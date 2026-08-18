using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using OpenCvSharp;
using AlprWpfApp.Models;
using AlprWpfApp.Services.AI;
using AlprWpfApp.Services.Camera;

namespace AlprWpfApp.ViewModels
{
    public partial class MainViewModel : ObservableObject, IDisposable
    {
        private readonly AlprPipelineEngine _alprEngine;
        private readonly AlprWorker _alprWorker;
        private readonly CameraStreamService _cameraService;

        private int _frameCount;
        private readonly Stopwatch _fpsTimer = Stopwatch.StartNew();
        private Mat? _latestRawFrame;
        private readonly object _frameLock = new();

        [ObservableProperty]
        private BitmapSource? _liveVideoFrame;

        [ObservableProperty]
        private bool _isStreaming;

        [ObservableProperty]
        private bool _isAutoInference = true;

        [ObservableProperty]
        private string _cameraStatus = "Chưa kết nối";

        [ObservableProperty]
        private int _fps;

        [ObservableProperty]
        private CameraSourceType _selectedSourceType = CameraSourceType.ImageFile;

        [ObservableProperty]
        private int _webcamIndex = 0;

        [ObservableProperty]
        private string _rtspUrl = "rtsp://admin:password@192.168.1.100:554/stream1";

        [ObservableProperty]
        private string _videoFilePath = string.Empty;

        [ObservableProperty]
        private string _imageFilePath = string.Empty;

        // UI Recognition Properties
        [ObservableProperty]
        private string _lastPlateNumber = "---";

        [ObservableProperty]
        private string _lastPlateColor = "Trắng";

        [ObservableProperty]
        private string _lastVehicleType = "---";

        [ObservableProperty]
        private string _lastConfidence = "0%";

        [ObservableProperty]
        private string _lastInferenceTime = "0 ms";

        [ObservableProperty]
        private string _lastEngine = "YOLOv8 + PARSeq ONNX";

        [ObservableProperty]
        private BitmapSource? _lastCropThumbnail;

        [ObservableProperty]
        private bool _isPlateValid;

        [ObservableProperty]
        private double _yoloLatencyMs;

        [ObservableProperty]
        private double _parseqLatencyMs;

        [ObservableProperty]
        private double _totalLatencyMs;

        // RadioButton two-way bindings
        public bool IsSourceWebcam
        {
            get => SelectedSourceType == CameraSourceType.Webcam;
            set { if (value) SelectedSourceType = CameraSourceType.Webcam; }
        }

        public bool IsSourceRtsp
        {
            get => SelectedSourceType == CameraSourceType.RTSP;
            set { if (value) SelectedSourceType = CameraSourceType.RTSP; }
        }

        public bool IsSourceVideo
        {
            get => SelectedSourceType == CameraSourceType.VideoFile;
            set { if (value) SelectedSourceType = CameraSourceType.VideoFile; }
        }

        public bool IsSourceImage
        {
            get => SelectedSourceType == CameraSourceType.ImageFile;
            set { if (value) SelectedSourceType = CameraSourceType.ImageFile; }
        }

        partial void OnSelectedSourceTypeChanged(CameraSourceType value)
        {
            OnPropertyChanged(nameof(IsSourceWebcam));
            OnPropertyChanged(nameof(IsSourceRtsp));
            OnPropertyChanged(nameof(IsSourceVideo));
            OnPropertyChanged(nameof(IsSourceImage));
        }

        public ObservableCollection<PlateHistoryItemViewModel> History { get; } = new();

        public MainViewModel()
        {
            // Tìm kiếm đường dẫn mô hình
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string yoloPath = Path.Combine(baseDir, "Models", "yolov8_plate.onnx");
            string parseqPath = Path.Combine(baseDir, "Models", "parseq.onnx");

            // Khởi tạo các services
            _alprEngine = new AlprPipelineEngine(yoloPath, parseqPath);
            _alprWorker = new AlprWorker(_alprEngine)
            {
                IsAutoInference = _isAutoInference
            };
            _cameraService = new CameraStreamService();

            // Đăng ký các sự kiện
            _cameraService.FrameCaptured += OnCameraFrameCaptured;
            _cameraService.StatusChanged += status => Application.Current.Dispatcher.Invoke(() => CameraStatus = status);
            _cameraService.ErrorOccurred += err => Application.Current.Dispatcher.Invoke(() =>
            {
                CameraStatus = "Lỗi";
                MessageBox.Show(err, "Thông báo Camera", MessageBoxButton.OK, MessageBoxImage.Warning);
            });

            _alprWorker.PlateRecognized += OnPlateRecognized;
        }

        partial void OnIsAutoInferenceChanged(bool value)
        {
            _alprWorker.IsAutoInference = value;
        }

        private void OnCameraFrameCaptured(Mat frame)
        {
            lock (_frameLock)
            {
                _latestRawFrame?.Dispose();
                _latestRawFrame = frame.Clone();
            }

            // Gửi sang AI Worker
            _alprWorker.EnqueueFrame(frame, false);

            // Tính FPS
            _frameCount++;
            if (_fpsTimer.ElapsedMilliseconds >= 1000)
            {
                int currentFps = (int)(_frameCount * 1000.0 / _fpsTimer.ElapsedMilliseconds);
                _frameCount = 0;
                _fpsTimer.Restart();
                Application.Current.Dispatcher.InvokeAsync(() => Fps = currentFps);
            }

            // Render hình ảnh lên UI
            var bmp = OpenCvImageHelper.MatToBitmapSource(frame);
            if (bmp != null)
            {
                Application.Current.Dispatcher.InvokeAsync(() => LiveVideoFrame = bmp);
            }
        }

        private void OnPlateRecognized(PlateRecognitionResult result)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                UpdateUiWithResult(result);
            });
        }

        /// <summary>
        /// Cập nhật toàn bộ các trường kết quả trên UI và thêm vào bảng lịch sử
        /// </summary>
        public void UpdateUiWithResult(PlateRecognitionResult result)
        {
            LastPlateNumber = result.PlateNumber;
            LastPlateColor = result.IsSuccess ? result.PlateColor : "---";
            LastVehicleType = result.IsSuccess ? result.VehicleType : "---";
            LastConfidence = result.IsSuccess ? $"{result.DetectionConfidence:F1}%" : "0%";
            LastInferenceTime = $"{result.TotalProcessingMs:F1} ms";
            LastEngine = result.EngineUsed;
            IsPlateValid = result.IsSuccess;

            YoloLatencyMs = Math.Round(result.YoloInferenceMs, 1);
            ParseqLatencyMs = Math.Round(result.ParseqInferenceMs, 1);
            TotalLatencyMs = Math.Round(result.TotalProcessingMs, 1);

            if (result.PlateCropImage != null)
            {
                LastCropThumbnail = result.PlateCropImage;
            }
            else if (!result.IsSuccess)
            {
                LastCropThumbnail = null;
            }

            // Chỉ thêm vào bảng lịch sử khi nhận diện được biển số xe hợp lệ
            if (result.IsSuccess)
            {
                var latest = History.FirstOrDefault();
                if (latest == null || latest.PlateNumber != result.PlateNumber || (DateTime.Now - latest.Timestamp).TotalSeconds > 2.0)
                {
                    var historyItem = PlateHistoryItemViewModel.FromResult(result);
                    History.Insert(0, historyItem);

                    if (History.Count > 200)
                    {
                        History.RemoveAt(History.Count - 1);
                    }
                }
            }
        }

        /// <summary>
        /// Xử lý trực tiếp 1 khung hình Mat đồng bộ và cập nhật UI ngay lập tức
        /// </summary>
        public void ProcessSingleFrame(Mat mat)
        {
            if (mat == null || mat.IsDisposed || mat.Empty())
                return;

            lock (_frameLock)
            {
                _latestRawFrame?.Dispose();
                _latestRawFrame = mat.Clone();
            }

            LiveVideoFrame = OpenCvImageHelper.MatToBitmapSource(mat);

            var res = _alprEngine.ProcessFrame(mat);
            UpdateUiWithResult(res);
        }

        [RelayCommand]
        public void StartCamera()
        {
            var config = new CameraSourceConfig
            {
                SourceType = SelectedSourceType,
                WebcamIndex = WebcamIndex,
                RtspUrl = RtspUrl,
                VideoFilePath = VideoFilePath,
                ImageFilePath = ImageFilePath
            };

            _cameraService.Start(config);
            IsStreaming = true;
        }

        [RelayCommand]
        public void StopCamera()
        {
            _cameraService.Stop();
            IsStreaming = false;
            Fps = 0;
        }

        [RelayCommand]
        public void Snap()
        {
            Mat? frameToProcess = null;
            lock (_frameLock)
            {
                if (_latestRawFrame != null && !_latestRawFrame.IsDisposed && !_latestRawFrame.Empty())
                {
                    frameToProcess = _latestRawFrame.Clone();
                }
            }

            if (frameToProcess != null)
            {
                using (frameToProcess)
                {
                    var res = _alprEngine.ProcessFrame(frameToProcess);
                    UpdateUiWithResult(res);
                }
            }
            else if (!string.IsNullOrEmpty(ImageFilePath) && File.Exists(ImageFilePath))
            {
                using var mat = Cv2.ImRead(ImageFilePath, ImreadModes.Color);
                if (!mat.Empty())
                {
                    ProcessSingleFrame(mat);
                }
            }
            else
            {
                MessageBox.Show("Chưa có khung hình camera hoặc ảnh để nhận diện!\nVui lòng nhấn 'Chọn Ảnh Test' hoặc bắt đầu luồng Camera.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        [RelayCommand]
        public void CaptureSnap()
        {
            Snap();
        }

        [RelayCommand]
        public void SelectTestImage()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png;*.bmp;*.webp)|*.jpg;*.jpeg;*.png;*.bmp;*.webp|All Files (*.*)|*.*",
                Title = "Chọn file Ảnh biển số xe"
            };

            // Mặc định mở thư mục samples nếu có
            string samplesDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "samples");
            if (Directory.Exists(samplesDir))
            {
                dlg.InitialDirectory = Path.GetFullPath(samplesDir);
            }

            if (dlg.ShowDialog() == true)
            {
                ImageFilePath = dlg.FileName;
                SelectedSourceType = CameraSourceType.ImageFile;

                using var mat = Cv2.ImRead(ImageFilePath, ImreadModes.Color);
                if (!mat.Empty())
                {
                    ProcessSingleFrame(mat);
                }
                else
                {
                    MessageBox.Show($"Không thể đọc file ảnh: {ImageFilePath}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        [RelayCommand]
        public void SelectImage()
        {
            SelectTestImage();
        }

        [RelayCommand]
        public void BrowseImageFile()
        {
            SelectTestImage();
        }

        [RelayCommand]
        public void BrowseVideoFile()
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Video Files (*.mp4;*.avi;*.mkv;*.mov)|*.mp4;*.avi;*.mkv;*.mov|All Files (*.*)|*.*",
                Title = "Chọn file Video biển số xe"
            };

            if (dlg.ShowDialog() == true)
            {
                VideoFilePath = dlg.FileName;
                SelectedSourceType = CameraSourceType.VideoFile;
            }
        }

        [RelayCommand]
        public void ClearHistory()
        {
            History.Clear();
        }

        [RelayCommand]
        public void ExportCsv()
        {
            if (History.Count == 0)
            {
                MessageBox.Show("Chưa có dữ liệu lịch sử để xuất file!", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"ALPR_History_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Lưu lịch sử nhận diện ALPR"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Thời gian,Biển số,Loại phương tiện,Màu biển,Thời gian xử lý (ms),Độ tin cậy (%),Mô hình");

                    foreach (var item in History)
                    {
                        sb.AppendLine($"\"{item.FormattedTime}\",\"{item.PlateNumber}\",\"{item.VehicleType}\",\"{item.PlateColor}\",{item.ProcessingMs},{item.Confidence},\"{item.EngineUsed}\"");
                    }

                    File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                    MessageBox.Show($"Đã xuất thành công {History.Count} bản ghi ra file:\n{dlg.FileName}", "Thành công", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Lỗi khi xuất file: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public void Dispose()
        {
            _cameraService.Dispose();
            _alprWorker.Dispose();
            _alprEngine.Dispose();
            lock (_frameLock)
            {
                _latestRawFrame?.Dispose();
                _latestRawFrame = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
