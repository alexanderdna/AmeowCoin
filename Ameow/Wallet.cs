using System;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using Ameow.Utils;

namespace Ameow
{
    /// <summary>
    /// Represents a wallet file. Provides interface for creating, loading and verifying a wallet.
    /// </summary>
    public sealed class Wallet
    {
        private readonly byte[] checksum;
        private readonly byte[] payload;

        /// <summary>
        /// Tries to load a wallet file and returns the wallet object.
        /// </summary>
        /// <param name="path">Path of the wallet file.</param>
        /// <returns>The wallet object, or null if the file does not exist.</returns>
        public static Wallet TryOpen(string path)
        {
            if (!File.Exists(path)) return null;

            var raw = File.ReadAllBytes(path);
            var wallet = new Wallet(raw);
            return wallet;
        }

        /// <summary>
        /// Saves the given wallet into persistent storage.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="wallet"></param>
        public static void Save(string path, Wallet wallet)
        {
            var raw = wallet.toRaw();
            File.WriteAllBytes(path, raw);
        }

        /// <summary>
        /// Constructs a wallet object from raw data.
        /// </summary>
        private Wallet(byte[] rawData)
        {
            if (rawData.Length < (32 + 1)) throw new ArgumentException("Invalid raw data length", nameof(rawData));

            checksum = new byte[32];
            payload = new byte[rawData.Length - checksum.Length];

            Buffer.BlockCopy(rawData, 0, checksum, 0, checksum.Length);
            Buffer.BlockCopy(rawData, checksum.Length, payload, 0, payload.Length);
        }

        /// <summary>
        /// Constructs a wallet object from a passphrase and private key secret.
        /// </summary>
        public Wallet(string passphrase, BigInteger secret)
        {
            using var sha256 = SHA256.Create();
            var ppBytes = Encoding.UTF8.GetBytes(passphrase);
            var ppHash = sha256.ComputeHash(ppBytes);
            var secretBytes = secret.ToByteArray();
            payload = AesEncryptor.Encrypt(secretBytes, ppHash);
            checksum = sha256.ComputeHash(secretBytes);
        }

        /// <summary>
        /// Decrypts the wallet data, verifies checksum and returns the private key secret.
        /// </summary>
        /// <param name="passphrase">Passphrase to decrypt the wallet.</param>
        /// <returns>The decrypted secret, or null if decryption or checksum failed.</returns>
        public BigInteger? TryGetSecret(string passphrase)
        {
            using var sha256 = SHA256.Create();
            var ppBytes = Encoding.UTF8.GetBytes(passphrase);
            var ppHash = sha256.ComputeHash(ppBytes);
            var decryptedPayload = AesEncryptor.Decrypt(payload, ppHash);
            var decryptedChecksum = sha256.ComputeHash(decryptedPayload);
            if (equalBytes(decryptedChecksum, checksum))
                return new BigInteger(decryptedPayload);
            else
                return null;
        }

        /// <summary>
        /// Generates a random secret for wallet private key.
        /// </summary>
        /// <returns>The generated secret.</returns>
        public static BigInteger GenerateSecret(Random random)
        {
            // Although we will make sure the BigInteger is unsigned,
            // it will automatically returns a 33-byte array if the
            // highest byte is greater than 127. And that will mess up
            // the AES encryptor/decryptor which uses 16-byte blocks.

            // Also, we want all bytes greater than 0 to prevent
            // BigInteger from returning a zero-truncated array.

            var bytes = new byte[32];
            bytes[0] = (byte)(random.Next(126) + 1);
            for (int i = 1, c = bytes.Length; i < c; ++i)
            {
                bytes[i] = (byte)(random.Next(255) + 1);
            }
            return new BigInteger(bytes, isUnsigned: true, isBigEndian: true);
        }

        /// <summary>
        /// Returns the raw byte array of the wallet.
        /// </summary>
        private byte[] toRaw()
        {
            byte[] raw = new byte[checksum.Length + payload.Length];
            Buffer.BlockCopy(checksum, 0, raw, 0, checksum.Length);
            Buffer.BlockCopy(payload, 0, raw, checksum.Length, payload.Length);
            return raw;
        }

        /// <summary>
        /// Returns true if two given byte arrays are equal.
        /// </summary>
        private static bool equalBytes(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0, c = a.Length; i < c; ++i)
                if (a[i] != b[i]) return false;

            return true;
        }
    }
}