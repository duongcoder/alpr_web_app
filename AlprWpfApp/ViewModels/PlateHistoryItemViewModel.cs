using System;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AlprWpfApp.Models;

namespace AlprWpfApp.ViewModels
{
    public partial class PlateHistoryItemViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _plateNumber = string.Empty;

        [ObservableProperty]
        private string _vehicleType = string.Empty;

        [ObservableProperty]
        private string _plateColor = "Trắng";

        [ObservableProperty]
        private double _processingMs;

        [ObservableProperty]
        private float _confidence;

        [ObservableProperty]
        private string _engineUsed = string.Empty;

        [ObservableProperty]
        private DateTime _timestamp;

        [ObservableProperty]
        private BitmapSource? _cropThumbnail;

        [ObservableProperty]
        private bool _isValid;

        public string FormattedTime => Timestamp.ToString("HH:mm:ss.fff");

        public static PlateHistoryItemViewModel FromResult(PlateRecognitionResult result)
        {
            return new PlateHistoryItemViewModel
            {
                PlateNumber = result.PlateNumber,
                VehicleType = result.VehicleType,
                PlateColor = result.PlateColor,
                ProcessingMs = Math.Round(result.TotalProcessingMs, 1),
                Confidence = (float)Math.Round(result.DetectionConfidence, 1),
                EngineUsed = result.EngineUsed,
                Timestamp = result.Timestamp,
                CropThumbnail = result.PlateCropImage,
                IsValid = result.IsSuccess
            };
        }
    }
}
