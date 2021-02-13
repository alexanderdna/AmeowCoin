using EllipticCurve;
using System;
using System.Security.Cryptography;

namespace Ameow.Utils
{
    public static class AddressUtils
    {
        public static string AddressFromPrivateKey(PrivateKey privateKey)
        {
            return AddressFromPublicKey(privateKey.publicKey());
        }

        public static string AddressFromPublicKey(PublicKey publicKey)
        {
            using var ripemd160 = new DevHawk.Security.Cryptography.RIPEMD160();
            using var sha256 = SHA256.Create();

            byte[] publicKeyPlain = publicKey.toString(encoded: false);
            byte[] publicKeyCorrect = new byte[1 + publicKeyPlain.Length];
            publicKeyCorrect[0] = 0x04;
            Buffer.BlockCopy(publicKeyPlain, 0, publicKeyCorrect, 1, publicKeyPlain.Length);

            byte[] hash1 = ripemd160.ComputeHash(sha256.ComputeHash(publicKeyCorrect));

            byte[] hash2 = new byte[1 + hash1.Length];
            hash2[0] = 0x32;
            Buffer.BlockCopy(hash1, 0, hash2, 1, hash1.Length);

            byte[] hash3 = sha256.ComputeHash(sha256.ComputeHash(hash2));

            byte[] hash4 = new byte[hash2.Length + 4];
            Buffer.BlockCopy(hash2, 0, hash4, 0, hash2.Length);
            Buffer.BlockCopy(hash3, 0, hash4, hash4.Length - 4, 4);

            var sb = StringBuilderPool.Acquire();
            Base58Check.Encode(sb, hash4);
            return StringBuilderPool.GetStringAndRelease(sb);
        }

        public static bool VerifyAddress(string address, bool needChecksum = true)
        {
            if (string.IsNullOrEmpty(address)) return false;

            var hash4 = Base58Check.Decode(address);
            if (hash4 == null) return false;

            if (hash4.Length != 25
                || hash4[0] != 0x32)
                return false;

            if (needChecksum)
            {
                using var sha256 = SHA256.Create();
                var hash2 = new byte[hash4.Length - 4];
                Buffer.BlockCopy(hash4, 0, hash2, 0, hash2.Length);

                var hash3 = sha256.ComputeHash(sha256.ComputeHash(hash2));
                if (hash4[21] != hash3[0]
                    || hash4[22] != hash3[1]
                    || hash4[23] != hash3[2]
                    || hash4[24] != hash3[3])
                    return false;
            }

            return true;
        }

        public static (Signature, PublicKey) SignatureAndPublicKeyFromTxInSignature(string txInSignature)
        {
            int indexOfSeparator = txInSignature.IndexOf('.');
            if (indexOfSeparator < 0)
                return (null, null);

            var bytes = HexUtils.ByteArrayFromHex(txInSignature, 0, indexOfSeparator);
            var signature = Signature.fromDer(bytes);

            bytes = HexUtils.ByteArrayFromHex(txInSignature, indexOfSeparator + 1, txInSignature.Length - indexOfSeparator - 1);
            var publicKey = PublicKey.fromDer(bytes);

            return (signature, publicKey);
        }

        public static string TxInSignatureFromSignatureAndPublicKey(Signature signature, PublicKey publicKey)
        {
            var sb = StringBuilderPool.Acquire();
            var bytes = signature.toDer();
            HexUtils.AppendHexFromByteArray(sb, bytes);
            sb.Append('.');
            bytes = publicKey.toDer();
            HexUtils.AppendHexFromByteArray(sb, bytes);
            return StringBuilderPool.GetStringAndRelease(sb);
        }

        public static bool TxInSignatureContainsPublicKey(string txInSignature, PublicKey publicKey)
        {
            int indexOfDelimiter = txInSignature.IndexOf('.');
            if (indexOfDelimiter < 0) return false;

            var publicKeyString = HexUtils.HexFromByteArray(publicKey.toDer());
            if (txInSignature.Length < publicKeyString.Length) return false;

            return txInSignature.EndsWith(publicKeyString);
        }
    }
}