using System;

namespace ImageEditor
{
    class Utility
    {
        public static void Swap<T>(ref T a, ref T b)
        {
            T temp = a;
            a = b;
            b = temp;
        }

        public static int CountTrailingZeros(Int64 x)
        {
            return (int)(Math.Log(x & (-x)) / Math.Log(2.0));
        }

        public static T Clamp<T>(T low, T high, T value) where T : IComparable
        {
            if (value.CompareTo(high) > 0)
                return high;
            if (value.CompareTo(low) < 0)
                return low;
            return value;
        }
    }

    class Pair<T, U>
    {
        public T first;
        public U second;

        public Pair(T a, U b)
        {
            first = a;
            second = b;
        }
    }
}
