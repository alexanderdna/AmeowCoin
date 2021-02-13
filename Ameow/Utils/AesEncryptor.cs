using System;
using System.IO;
using System.Security.Cryptography;

namespace Ameow.Utils
{
    public static class AesEncryptor
    {
        public const int BlockSize = 16;
        public const int KeySize = 32;

        private static Random _rand = new Random();

        public static byte[] Encrypt(byte[] plainTextBytes, byte[] keyBytes)
        {
            if (keyBytes.Length != KeySize) throw new ArgumentException("Invalid key length", nameof(keyBytes));

            byte[] initialVectorBytes = new byte[BlockSize];
            _rand.NextBytes(initialVectorBytes);

            byte[] cipherTextBytes = null;
            using (var symmetricKey = Rijndael.Create())
            {
                symmetricKey.Mode = CipherMode.CFB;
                symmetricKey.IV = initialVectorBytes;
                symmetricKey.Key = keyBytes;
                symmetricKey.Padding = PaddingMode.Zeros;
                symmetricKey.BlockSize = BlockSize * 8;
                symmetricKey.FeedbackSize = BlockSize * 8;
                using ICryptoTransform encryptor = symmetricKey.CreateEncryptor();
                using MemoryStream memStream = new MemoryStream();
                using (CryptoStream cryptoStream = new CryptoStream(memStream, encryptor, CryptoStreamMode.Write))
                {
                    cryptoStream.Write(plainTextBytes, 0, plainTextBytes.Length);
                }
                cipherTextBytes = memStream.ToArray();
            }

            byte[] returnBytes = new byte[initialVectorBytes.Length + cipherTextBytes.Length];
            Array.Copy(initialVectorBytes, returnBytes, initialVectorBytes.Length);
            Array.Copy(cipherTextBytes, 0, returnBytes, initialVectorBytes.Length, cipherTextBytes.Length);

            return returnBytes;
        }

        public static byte[] Decrypt(byte[] cipherTextBytes, byte[] keyBytes)
        {
            if (keyBytes.Length != KeySize) throw new ArgumentException("Invalid key length", nameof(keyBytes));

            byte[] initialVectorBytes = new byte[BlockSize];
            Array.Copy(cipherTextBytes, initialVectorBytes, initialVectorBytes.Length);

            byte[] plainTextBytes = new byte[cipherTextBytes.Length - initialVectorBytes.Length];
            using (var symmetricKey = Rijndael.Create())
            {
                symmetricKey.Mode = CipherMode.CFB;
                symmetricKey.IV = initialVectorBytes;
                symmetricKey.Key = keyBytes;
                symmetricKey.Padding = PaddingMode.Zeros;
                symmetricKey.BlockSize = BlockSize * 8;
                symmetricKey.FeedbackSize = BlockSize * 8;
                using ICryptoTransform decryptor = symmetricKey.CreateDecryptor();
                using MemoryStream memStream = new MemoryStream(cipherTextBytes, initialVectorBytes.Length, plainTextBytes.Length);
                using CryptoStream cryptoStream = new CryptoStream(memStream, decryptor, CryptoStreamMode.Read);
                cryptoStream.Read(plainTextBytes, 0, plainTextBytes.Length);
            }

            return plainTextBytes;
        }
    }
}