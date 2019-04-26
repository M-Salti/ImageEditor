using System;
using System.Numerics;

namespace ImageEditor
{
    class DrawingTools
    {
        private EditableBitmap image;
        public uint thickness;
        public Color[] colors;
        public int usedColor; // 0: primary, 1:secondary

        public bool mirrorStrokes;
        public int spiralCount;

        public Position p0, p1;

        internal EditableBitmap Image { get => image; set => image = value; }

        public DrawingTools()
        {
            thickness = 1;
            colors = new Color[2];
            colors[0] = Color.Black;
            colors[1] = Color.White;
            usedColor = 0;

            mirrorStrokes = false;
            spiralCount = 1;

            p0 = new Position(-1, -1);
            p1 = new Position(-1, -1);
        }

        public void DrawRectangle(Position p0, Position p1)
        {
            int x0 = p0.X, y0 = p0.Y, x1 = p1.X, y1 = p1.Y;
            DrawRectangle(x0, y0, x1, y1);
        }

        public void DrawRectangle(int x0, int y0, int x1, int y1)
        {
            AliasedLine(x0, y0, x1, y0);
            AliasedLine(x1, y0, x1, y1);
            AliasedLine(x1, y1, x0, y1);
            AliasedLine(x0, y1, x0, y0);
        }

        public void DrawCircle(Position p0, int r)
        {
            DrawCircle(p0.X, p0.Y, r);
        }

        public void DrawCircle(int xm, int ym, int r)
        {
            int x = -r, y = 0, err = 2 - 2 * r;
            do
            {
                PlotPenDot(xm - x, ym + y);
                PlotPenDot(xm - y, ym - x);
                PlotPenDot(xm + x, ym - y);
                PlotPenDot(xm + y, ym + x);
                r = err;
                if (r <= y) err += ++y * 2 + 1;
                if (r > x || err > y)
                    err += ++x * 2 + 1;
            } while (x < 0);
        }

        public void DrawEllipse(Position p0, Position p1)
        {
            DrawEllipse(p0.X, p0.Y, p1.X, p1.Y);
        }

        public void DrawEllipse(int x0, int y0, int x1, int y1)
        {
            int a = Math.Abs(x1 - x0), b = Math.Abs(y1 - y0), b1 = b & 1;
            double dx = 4 * (1.0 - a) * b * b, dy = 4 * (b1 + 1) * a * a;
            double err = dx + dy + b1 * a * a, e2;

            if (x0 > x1) { x0 = x1; x1 += a; }
            if (y0 > y1) y0 = y1;
            y0 += (b + 1) / 2; y1 = y0 - b1;
            a = 8 * a * a; b1 = 8 * b * b;

            do
            {
                PlotPenDot(x1, y0);
                PlotPenDot(x0, y0);
                PlotPenDot(x0, y1);
                PlotPenDot(x1, y1);
                e2 = 2 * err;
                if (e2 <= dy) { y0++; y1--; err += dy += a; }
                if (e2 >= dx || 2 * err > dy) { x0++; x1--; err += dx += b1; }
            } while (x0 <= x1);

            while (y0 - y1 <= b)
            {
                PlotPenDot(x0 - 1, y0);
                PlotPenDot(x1 + 1, y0++);
                PlotPenDot(x0 - 1, y1);
                PlotPenDot(x1 + 1, y1--);
            }
        }

        #region Aliased Line

        public void AliasedLine(Position p0, Position p1)
        {
            int x0 = p0.X, y0 = p0.Y, x1 = p1.X, y1 = p1.Y;
            AliasedLine(x0, y0, x1, y1);
        }

        public void AliasedLine(int x0, int y0, int x1, int y1) // Bresenham's Algorithm
        {
            if (y0 == y1)
                PlotHorizontalLine(x0, x1, y0);
            else if (x0 == x1)
                PlotVerticalLine(y0, y1, x0);
            else if (Math.Abs(y1 - y0) < Math.Abs(x1 - x0))
            {
                if (x0 > x1)
                    PlotLineLow(x1, y1, x0, y0);
                else
                    PlotLineLow(x0, y0, x1, y1);
            }
            else
            {
                if (y0 > y1)
                    PlotLineHigh(x1, y1, x0, y0);
                else
                    PlotLineHigh(x0, y0, x1, y1);
            }
        }

        private void PlotLineLow(int x0, int y0, int x1, int y1)
        {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int yi = 1;
            if (dy < 0)
            {
                yi = -1;
                dy = -dy;
            }
            int D = 2 * dy - dx;
            int y = y0;

            for (int x = x0; x <= x1; x++)
            {
                PlotPenDot(x, y);
                if (D > 0)
                {
                    y = y + yi;
                    D = D - 2 * dx;
                }
                D = D + 2 * dy;
            }
        }

        private void PlotLineHigh(int x0, int y0, int x1, int y1)
        {
            int dx = x1 - x0;
            int dy = y1 - y0;
            int xi = 1;
            if (dx < 0)
            {
                xi = -1;
                dx = -dx;
            }
            int D = 2 * dx - dy;
            int x = x0;

            for (int y = y0; y <= y1; ++y)
            {
                PlotPenDot(x, y);
                if (D > 0)
                {
                    x = x + xi;
                    D = D - 2 * dy;
                }
                D = D + 2 * dx;
            }
        }

        private void PlotVerticalLine(int y0, int y1, int x)
        {
            if (y0 > y1)
                Utility.Swap(ref y0, ref y1);
            for (int i = y0; i <= y1; ++i)
                PlotPenDot(x, i);
        }

        private void PlotHorizontalLine(int x0, int x1, int y)
        {
            if (x0 > x1)
                Utility.Swap(ref x0, ref x1);
            for (int i = x0; i <= x1; ++i)
                PlotPenDot(i, y);
        }

        #endregion

        public void PlotPenDot(Position p)
        {
            PlotPenDot(p.X, p.Y);
        }

        private void PlotPenDot(int x, int y)
        {
            switch (thickness)
            {
                case 1:
                    PlotPixel(x, y);
                    break;

                case 2:
                    PlotPixel(x, y);
                    PlotPixel(x - 1, y);
                    PlotPixel(x - 1, y - 1);
                    PlotPixel(x, y - 1);
                    break;

                case 3:
                    PlotPixel(x, y);
                    PlotPixel(x + 1, y);
                    PlotPixel(x - 1, y);
                    PlotPixel(x, y + 1);
                    PlotPixel(x, y - 1);
                    break;

                case 4:
                    /*
                        **
                       ****
                       **x*
                        ** 
                     */
                    PlotPixel(x, y);
                    PlotPixel(x, y + 1);
                    PlotPixel(x, y - 1);
                    PlotPixel(x, y - 2);

                    PlotPixel(x - 1, y);
                    PlotPixel(x - 1, y + 1);
                    PlotPixel(x - 1, y - 1);
                    PlotPixel(x - 1, y - 2);

                    PlotPixel(x - 2, y);
                    PlotPixel(x - 2, y - 1);

                    PlotPixel(x + 1, y);
                    PlotPixel(x + 1, y - 1);
                    break;

                default:
                    PlotPixel(x, y);
                    break;
            }
        }

        private void PlotPixel(int x, int y)
        {
            if (x >= 0 && x < image.Width && y >= 0 && y < image.Height)
            {
                image.SetPixelXY(x, y, colors[usedColor]);
                if (mirrorStrokes)
                    image.SetPixelXY(image.Width - x, y, colors[usedColor]);
            }

            Complex rotate = Complex.Exp(Complex.ImaginaryOne * 2.0 * Math.PI / spiralCount);
            Complex currentPos = new Complex(x - image.Width / 2, y - image.Height / 2);

            for (int i = 1; i < spiralCount; i++)
            {
                currentPos *= rotate;
                x = (int)Math.Round(currentPos.Real) + image.Width / 2;
                y = (int)Math.Round(currentPos.Imaginary) + image.Height / 2;

                if (x >= 0 && x < image.Width && y >= 0 && y < image.Height)
                {
                    image.SetPixelXY(x, y, colors[usedColor]);
                    if (mirrorStrokes)
                        image.SetPixelXY(image.Width - x, y, colors[usedColor]);
                }
            }
        }
    }
}
