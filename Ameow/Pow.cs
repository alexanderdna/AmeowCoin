using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace Ameow
{
    /// <summary>
    /// Proof-of-Work helper class.
    /// </summary>
    public static class Pow
    {
        private static readonly Stopwatch sw = new Stopwatch();

        public static double LastMiningMilliseconds => sw.Elapsed.TotalMilliseconds;

        /// <summary>
        /// Computes and returns the hash of the given block header.
        /// </summary>
        /// <param name="stream">Data stream of the block header.</param>
        public static byte[] Hash(Stream stream)
        {
            using var hashAlgo = SHA256.Create();
            stream.Seek(0, SeekOrigin.Begin);
            sw.Restart();
            var hash = hashAlgo.ComputeHash(stream);
            sw.Stop();
            return hash;
        }

        /// <summary>
        /// Calculates and returns the difficulty of the given block index.
        /// Difficulty is the number of 0 bits a block hash must start with to be
        /// considered valid.
        /// </summary>
        public static int CalculateDifficulty(int index)
        {
            if (index == 0) return 0;
            else if (index < 50) return 20;
            else if (index < 100) return 24;
            else if (index < 1000) return 28;
            else if (index < 10000) return 32;
            else return 36;
        }

        /// <summary>
        /// Numbers of leading (significant) 0 bits for values from 0 to 255.
        /// </summary>
        private static readonly int[] zeroMap =
        {
            8, 7, 6, 6, 5, 5, 5, 5,
            4, 4, 4, 4, 4, 4, 4, 4,
            3, 3, 3, 3, 3, 3, 3, 3,
            3, 3, 3, 3, 3, 3, 3, 3,
            2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2,
            2, 2, 2, 2, 2, 2, 2, 2,
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0,
        };

        /// <summary>
        /// Returns true if the given block hash satisfies the given difficulty.
        /// </summary>
        public static bool IsValidHash(byte[] hash, int difficulty)
        {
            long nZeroes = 0;
            for (int i = 0, c = hash.Length; i < c; ++i)
            {
                byte b = hash[i];

                int nZeroesThisByte = zeroMap[b];
                nZeroes += nZeroesThisByte;

                if (nZeroesThisByte < 8)
                    break;
            }

            return nZeroes >= difficulty;
        }
    }
}