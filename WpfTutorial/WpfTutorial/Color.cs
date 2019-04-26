using System;

namespace ImageEditor
{
    class Color
    {
        public static Color White = FromArgb(255, 255, 255);
        public static Color Black = FromArgb(0, 0, 0);

        public static Color Red = FromArgb(255, 0, 0);
        public static Color Green = FromArgb(0, 255, 0);
        public static Color Blue = FromArgb(0, 0, 255);

        private const int AlphaShift = 24;
        private const int RedShift = 16;
        private const int GreenShift = 8;
        private const int BlueShift = 0;

        private readonly int Value;

        private Color(int value) => Value = value;

        public byte R => (byte)((Value >> RedShift) & 0xFF);
        public byte G => (byte)((Value >> GreenShift) & 0xFF);
        public byte B => (byte)((Value >> BlueShift) & 0xFF);
        public byte A => (byte)((Value >> AlphaShift) & 0xFF);

        public Color Clone()
        {
            return new Color(Value);
        }

        public static Color FromArgb(int argb)
        {
            return new Color(argb);
        }

        public static Color FromArgb(int alpha, int red, int green, int blue)
        {
            return new Color((red << RedShift) | (green << GreenShift) | (blue << BlueShift) | (alpha << AlphaShift));
        }

        public static Color FromArgb(int red, int green, int blue)
        {
            return FromArgb(255, red, green, blue);
        }

        public float GetBrightness()
        {
            float r = R / 255.0f;
            float g = G / 255.0f;
            float b = B / 255.0f;

            float max, min;

            max = r; min = r;

            if (g > max) max = g;
            if (b > max) max = b;

            if (g < min) min = g;
            if (b < min) min = b;

            return (max + min) / 2;
        }

        public float GetHue()
        {
            if (R == G && G == B)
                return 0;

            float r = R / 255.0f;
            float g = G / 255.0f;
            float b = B / 255.0f;

            float max, min;
            float delta;
            float hue = 0.0f;

            max = r; min = r;

            if (g > max) max = g;
            if (b > max) max = b;

            if (g < min) min = g;
            if (b < min) min = b;

            delta = max - min;

            if (r == max)
            {
                hue = (g - b) / delta;
            }
            else if (g == max)
            {
                hue = 2 + (b - r) / delta;
            }
            else if (b == max)
            {
                hue = 4 + (r - g) / delta;
            }
            hue *= 60;

            if (hue < 0.0f)
            {
                hue += 360.0f;
            }
            return hue;
        }

        public float GetSaturation()
        {
            float r = R / 255.0f;
            float g = G / 255.0f;
            float b = B / 255.0f;

            float max, min;
            float l, s = 0;

            max = r; min = r;

            if (g > max) max = g;
            if (b > max) max = b;

            if (g < min) min = g;
            if (b < min) min = b;

            if (max != min)
            {
                l = (max + min) / 2;

                if (l <= .5)
                {
                    s = (max - min) / (max + min);
                }
                else
                {
                    s = (max - min) / (2 - max - min);
                }
            }
            return s;
        }

        public int ToArgb()
        {
            return Value;
        }

        public override string ToString()
        {
            return "[A=" + A + ", R=" + R + ", G=" + G + ", B=" + B + "]";
        }
    }

}
