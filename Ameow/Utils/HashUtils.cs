using System.Diagnostics;
using System.IO;
using System.Text;
using Crypto = System.Security.Cryptography;

namespace Ameow.Utils
{
    public static class HashUtils
    {
        private static readonly Stopwatch sw = new Stopwatch();

        public static double LastMiningMilliseconds => sw.Elapsed.TotalMilliseconds;

        public static string SHA256(Stream stream)
        {
            using var sha256 = Crypto.SHA256.Create();
            stream.Seek(0, SeekOrigin.Begin);
            var result = sha256.ComputeHash(stream);
            return HexUtils.HexFromByteArray(result);
        }

        public static string SHA256(byte[] data)
        {
            using var sha256 = Crypto.SHA256.Create();
            var result = sha256.ComputeHash(data);
            return HexUtils.HexFromByteArray(result);
        }

        public static string SHA256(string data)
        {
            return SHA256(Encoding.UTF8.GetBytes(data));
        }
    }
}