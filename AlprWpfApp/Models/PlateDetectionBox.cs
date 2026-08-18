using OpenCvSharp;

namespace AlprWpfApp.Models
{
    /// <summary>
    /// Kết quả phát hiện vị trí biển số từ YOLOv8
    /// </summary>
    public class PlateDetectionBox
    {
        public Rect BoundingBox { get; set; }
        public float Confidence { get; set; }
        public int ClassId { get; set; }
        public string Label { get; set; } = "License Plate";

        public int X1 => BoundingBox.X;
        public int Y1 => BoundingBox.Y;
        public int X2 => BoundingBox.X + BoundingBox.Width;
        public int Y2 => BoundingBox.Y + BoundingBox.Height;
    }
}
