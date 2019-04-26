using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ImageEditor
{
    class Position
    {
        public int X;
        public int Y;

        public Position(int _x, int _y)
        {
            X = _x;
            Y = _y;
        }

        public Position(System.Windows.Point point)
        {
            X = (int)point.X;
            Y = (int)point.Y;
        }

        public int Distance(Position other)
        {
            return (int)Math.Sqrt((other.X - X) * (other.X - X) + (other.Y - Y) * (other.Y - Y));
        }

        public override string ToString()
        {
            return "x=" + X + ", y=" + Y;
        }
    }
}
