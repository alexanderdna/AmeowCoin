using System.Numerics;
using System.Text;

namespace Ameow.Utils
{
    public static class Base58Check
    {
        private const string digits = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";

        public static string Encode(byte[] data)
        {
            var sb = StringBuilderPool.Acquire();
            Encode(sb, data);
            return StringBuilderPool.GetStringAndRelease(sb);
        }

        public static void Encode(StringBuilder sb, byte[] data)
        {
            var big = new BigInteger();
            for (int i = 0, c = data.Length; i < c; ++i)
            {
                big = big * 256 + data[i];
            }

            int startIndex = sb.Length;
            while (!big.IsZero)
            {
                int remainder = (int)(big % 58);
                big /= 58;
                sb.Insert(startIndex, digits[remainder]);
            }

            for (int i = 0, c = data.Length; i < c && data[i] == 0; ++i)
            {
                sb.Insert(startIndex, digits[0]);
            }
        }

        public static byte[] Decode(string str)
        {
            BigInteger big = new BigInteger();
            for (int i = 0, c = str.Length; i < c; ++i)
            {
                int digitValue = digitToValue(str[i]);
                if (digitValue < 0) return null;

                big *= 58;
                big += digitValue;
            }

            int nLeadingZeroes = 0;
            for (int i = 0, c = str.Length; i < c; ++i)
            {
                if (str[i] == digits[0]) ++nLeadingZeroes;
                else break;
            }

            var bigBytes = big.ToByteArray();
            int nZeroBytesToSkip = 0;
            for (int i = bigBytes.Length - 1; i >= 0; --i)
            {
                if (bigBytes[i] == 0) ++nZeroBytesToSkip;
                else break;
            }

            var result = new byte[bigBytes.Length - nZeroBytesToSkip + nLeadingZeroes];
            for (int i = 0; i < nLeadingZeroes; ++i)
            {
                result[i] = 0;
            }
            for (int i = 0, c = bigBytes.Length - nZeroBytesToSkip, j = result.Length - 1; i < c; ++i, --j)
            {
                result[j] = bigBytes[i];
            }
            return result;
        }

        private static int digitToValue(char ch) => ch switch
        {
            '1' => 0,
            '2' => 1,
            '3' => 2,
            '4' => 3,
            '5' => 4,
            '6' => 5,
            '7' => 6,
            '8' => 7,
            '9' => 8,
            'A' => 9,
            'B' => 10,
            'C' => 11,
            'D' => 12,
            'E' => 13,
            'F' => 14,
            'G' => 15,
            'H' => 16,
            'J' => 17,
            'K' => 18,
            'L' => 19,
            'M' => 20,
            'N' => 21,
            'P' => 22,
            'Q' => 23,
            'R' => 24,
            'S' => 25,
            'T' => 26,
            'U' => 27,
            'V' => 28,
            'W' => 29,
            'X' => 30,
            'Y' => 31,
            'Z' => 32,
            'a' => 33,
            'b' => 34,
            'c' => 35,
            'd' => 36,
            'e' => 37,
            'f' => 38,
            'g' => 39,
            'h' => 40,
            'i' => 41,
            'j' => 42,
            'k' => 43,
            'm' => 44,
            'n' => 45,
            'o' => 46,
            'p' => 47,
            'q' => 48,
            'r' => 49,
            's' => 50,
            't' => 51,
            'u' => 52,
            'v' => 53,
            'w' => 54,
            'x' => 55,
            'y' => 56,
            'z' => 57,
            _ => -1,
        };
    }
}