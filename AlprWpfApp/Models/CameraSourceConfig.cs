namespace AlprWpfApp.Models
{
    public enum CameraSourceType
    {
        Webcam,
        RTSP,
        VideoFile,
        ImageFile
    }

    public class CameraSourceConfig
    {
        public CameraSourceType SourceType { get; set; } = CameraSourceType.Webcam;
        public int WebcamIndex { get; set; } = 0;
        public string RtspUrl { get; set; } = "rtsp://admin:password@192.168.1.100:554/stream1";
        public string VideoFilePath { get; set; } = string.Empty;
        public string ImageFilePath { get; set; } = string.Empty;
        public int FrameRateLimit { get; set; } = 30;
    }
}
