namespace KoreEngine
{
    public class Math
    {
        public static float Lerp(float start, float end, float t)
        {
            return start + t * (end - start);
        }

        #region Max
        public static float Max(float val1, float val2)
        {
            if (val1 != val2)
            {
                if (!float.IsNaN(val1))
                {
                    return val2 < val1 ? val1 : val2;
                }

                return val1;
            }

            return float.IsNegative(val2) ? val1 : val2;
        }

        public static byte Max(byte val1, byte val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static double Max(double val1, double val2)
        {
            if (val1 != val2)
            {
                if (!double.IsNaN(val1))
                {
                    return val2 < val1 ? val1 : val2;
                }

                return val1;
            }

            return double.IsNegative(val2) ? val1 : val2;
        }

        public static short Max(short val1, short val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static int Max(int val1, int val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }

        public static long Max(long val1, long val2)
        {
            return (val1 >= val2) ? val1 : val2;
        }
        #endregion

        #region Clamp
        public static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        public static short Clamp(short value, short min, short max)
        {
            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        public static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        public static long Clamp(long value, long min, long max)
        {
            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }
            else if (value > max)
            {
                return max;
            }

            return value;
        }
        #endregion
    }
}
