using System;
using System.IO;
using System.Text.Json;
using OpenCvSharp;

namespace AlprWpfApp.Services.Config
{
    public class RoiConfig
    {
        public float X { get; set; } = 0.22f;
        public float Y { get; set; } = 0.10f;
        public float Width { get; set; } = 0.56f;
        public float Height { get; set; } = 0.88f;
    }

    public static class RoiConfigService
    {
        private const string ConfigFileName = "roi_config.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static readonly RoiConfig DefaultConfig = new()
        {
            X = 0.22f,
            Y = 0.10f,
            Width = 0.56f,
            Height = 0.88f
        };

        public static string GetConfigFilePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);
        }

        /// <summary>
        /// Nạp cấu hình ROI từ file roi_config.json. Nếu không tồn tại hoặc lỗi, trả về giá trị mặc định (0.22f, 0.10f, 0.56f, 0.88f).
        /// </summary>
        public static RoiConfig Load()
        {
            try
            {
                string path = GetConfigFilePath();
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<RoiConfig>(json);
                    if (config != null && config.Width > 0 && config.Height > 0)
                    {
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RoiConfigService] Lỗi khi nạp roi_config.json: {ex.Message}");
            }

            return new RoiConfig
            {
                X = DefaultConfig.X,
                Y = DefaultConfig.Y,
                Width = DefaultConfig.Width,
                Height = DefaultConfig.Height
            };
        }

        /// <summary>
        /// Nạp cấu hình ROI dưới dạng OpenCvSharp.Rect2f
        /// </summary>
        public static Rect2f LoadAsRect2f()
        {
            var config = Load();
            return new Rect2f(config.X, config.Y, config.Width, config.Height);
        }

        /// <summary>
        /// Lưu cấu hình ROI ra file roi_config.json với định dạng JSON thụt lề
        /// </summary>
        public static bool Save(RoiConfig config)
        {
            if (config == null) return false;

            try
            {
                string path = GetConfigFilePath();
                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RoiConfigService] Lỗi khi lưu roi_config.json: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Lưu cấu hình ROI từ OpenCvSharp.Rect2f
        /// </summary>
        public static bool Save(Rect2f roi)
        {
            return Save(new RoiConfig
            {
                X = roi.X,
                Y = roi.Y,
                Width = roi.Width,
                Height = roi.Height
            });
        }

        /// <summary>
        /// Lưu cấu hình ROI từ các giá trị tọa độ chuẩn hóa [0..1]
        /// </summary>
        public static bool Save(float x, float y, float width, float height)
        {
            return Save(new RoiConfig
            {
                X = x,
                Y = y,
                Width = width,
                Height = height
            });
        }
    }
}
