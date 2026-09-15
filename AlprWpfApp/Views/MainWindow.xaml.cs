using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using AlprWpfApp.ViewModels;

namespace AlprWpfApp.Views
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// Hỗ trợ vẽ vùng ROI tương tác trực tiếp bằng chuột và quy đổi tọa độ chuẩn hóa [0..1]
    /// </summary>
    public partial class MainWindow : Window
    {
        private Point _startPoint;
        private bool _isDragging;

        public MainWindow()
        {
            InitializeComponent();
        }

        private void RoiDrawingCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isDragging = true;
                _startPoint = e.GetPosition(RoiDrawingCanvas);

                RoiDrawingCanvas.CaptureMouse();

                // Đặt vị trí ban đầu cho khung vẽ
                Canvas.SetLeft(RoiSelectionRect, _startPoint.X);
                Canvas.SetTop(RoiSelectionRect, _startPoint.Y);
                RoiSelectionRect.Width = 0;
                RoiSelectionRect.Height = 0;
                RoiSelectionRect.Visibility = Visibility.Visible;
            }
        }

        private void RoiDrawingCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                var currentPoint = e.GetPosition(RoiDrawingCanvas);

                double x = Math.Min(_startPoint.X, currentPoint.X);
                double y = Math.Min(_startPoint.Y, currentPoint.Y);
                double width = Math.Abs(_startPoint.X - currentPoint.X);
                double height = Math.Abs(_startPoint.Y - currentPoint.Y);

                Canvas.SetLeft(RoiSelectionRect, x);
                Canvas.SetTop(RoiSelectionRect, y);
                RoiSelectionRect.Width = width;
                RoiSelectionRect.Height = height;
            }
        }

        private void RoiDrawingCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                RoiDrawingCanvas.ReleaseMouseCapture();
                RoiSelectionRect.Visibility = Visibility.Collapsed;

                var endPoint = e.GetPosition(RoiDrawingCanvas);

                double x1 = Math.Min(_startPoint.X, endPoint.X);
                double x2 = Math.Max(_startPoint.X, endPoint.X);
                double y1 = Math.Min(_startPoint.Y, endPoint.Y);
                double y2 = Math.Max(_startPoint.Y, endPoint.Y);

                double drawWidth = x2 - x1;
                double drawHeight = y2 - y1;

                // Bỏ qua nếu người dùng chỉ click chuột nhầm mà không kéo thả vùng rõ ràng (< 8px)
                if (drawWidth < 8 || drawHeight < 8)
                {
                    return;
                }

                // Quy đổi tọa độ hiển thị WPF sang tọa độ chuẩn hóa [0..1] của ảnh gốc
                CalculateAndApplyNormalizedRoi(x1, y1, x2, y2);
            }
        }

        private void RoiDrawingCanvas_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                // Giữ nguyên trạng thái để mouse capture xử lý khi nhả chuột
            }
        }

        /// <summary>
        /// Tính toán tọa độ hiển thị thực tế của ảnh gốc (xử lý chuẩn Stretch Uniform & Letterboxing)
        /// và cập nhật sang MainViewModel dưới dạng tọa độ chuẩn hóa [0..1].
        /// </summary>
        private void CalculateAndApplyNormalizedRoi(double x1, double y1, double x2, double y2)
        {
            if (DataContext is not MainViewModel vm)
                return;

            double ctrlW = CameraDisplayImage.ActualWidth;
            double ctrlH = CameraDisplayImage.ActualHeight;

            if (ctrlW <= 0 || ctrlH <= 0)
            {
                ctrlW = RoiDrawingCanvas.ActualWidth;
                ctrlH = RoiDrawingCanvas.ActualHeight;
            }

            if (ctrlW <= 0 || ctrlH <= 0)
                return;

            float normX, normY, normW, normH;

            if (CameraDisplayImage.Source is BitmapSource bmpSource && bmpSource.PixelWidth > 0 && bmpSource.PixelHeight > 0)
            {
                double imgW = bmpSource.PixelWidth;
                double imgH = bmpSource.PixelHeight;

                // Tính toán tỷ lệ co giãn Uniform
                double scale = Math.Min(ctrlW / imgW, ctrlH / imgH);
                double renderedW = imgW * scale;
                double renderedH = imgH * scale;

                // Khoảng đen viền offset (Letterbox / Pillarbox)
                double offsetX = (ctrlW - renderedW) / 2.0;
                double offsetY = (ctrlH - renderedH) / 2.0;

                // Ánh xạ tọa độ từ Canvas sang hệ quy chiếu của CameraDisplayImage
                Point pTopLeft = RoiDrawingCanvas.TranslatePoint(new Point(x1, y1), CameraDisplayImage);
                Point pBottomRight = RoiDrawingCanvas.TranslatePoint(new Point(x2, y2), CameraDisplayImage);

                double boxLeft = pTopLeft.X;
                double boxTop = pTopLeft.Y;
                double boxRight = pBottomRight.X;
                double boxBottom = pBottomRight.Y;

                // Giới hạn (clamp) tọa độ nằm hoàn toàn bên trong vùng hiển thị thực tế của ảnh
                double clampedLeft = Math.Clamp(boxLeft, offsetX, offsetX + renderedW);
                double clampedRight = Math.Clamp(boxRight, offsetX, offsetX + renderedW);
                double clampedTop = Math.Clamp(boxTop, offsetY, offsetY + renderedH);
                double clampedBottom = Math.Clamp(boxBottom, offsetY, offsetY + renderedH);

                double actualW = clampedRight - clampedLeft;
                double actualH = clampedBottom - clampedTop;

                if (renderedW > 0 && renderedH > 0 && actualW >= 4 && actualH >= 4)
                {
                    normX = (float)((clampedLeft - offsetX) / renderedW);
                    normY = (float)((clampedTop - offsetY) / renderedH);
                    normW = (float)(actualW / renderedW);
                    normH = (float)(actualH / renderedH);
                }
                else
                {
                    return;
                }
            }
            else
            {
                // Fallback nếu chưa có frame ảnh: chuẩn hóa trực tiếp theo kích thước Canvas
                normX = (float)(x1 / ctrlW);
                normY = (float)(y1 / ctrlH);
                normW = (float)((x2 - x1) / ctrlW);
                normH = (float)((y2 - y1) / ctrlH);
            }

            // Cập nhật sang ViewModel
            vm.UpdateRoiFromDrawing(normX, normY, normW, normH);
        }

        /// <summary>
        /// Xử lý phím tắt duyệt ảnh test (Left / Right / Space) tập trung,
        /// ngoại trừ trường hợp người dùng đang nhập văn bản trong TextBox / PasswordBox.
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            // Bỏ qua nếu người dùng đang gõ trong TextBox hoặc PasswordBox
            if (Keyboard.FocusedElement is TextBoxBase or PasswordBox)
            {
                return;
            }

            if (DataContext is MainViewModel vm && vm.HasTestFolderImages)
            {
                if (e.Key is Key.Right or Key.Space)
                {
                    vm.NextTestImageCommand.Execute(null);
                    e.Handled = true;
                }
                else if (e.Key == Key.Left)
                {
                    vm.PreviousTestImageCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Dispose();
            }
            base.OnClosing(e);
        }
    }
}
