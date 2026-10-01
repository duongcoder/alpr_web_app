using System;

namespace AlprSdk.Utils
{
    /// <summary>
    /// Các hàm toán học mở rộng tương thích với .NET Framework 4.8
    /// Cung cấp hàm Clamp thay thế cho System.Math.Clamp (chỉ có từ .NET Core / .NET Standard 2.1)
    /// </summary>
    public static class MathExtensions
    {
        public static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static double Clamp(double value, double min, double max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static byte Clamp(byte value, byte min, byte max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        public static float Exp(float x)
        {
            return (float)Math.Exp(x);
        }
    }
}
