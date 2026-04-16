using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SalesforceDynamicsGPIntegration
{
    public static class SensitiveDataCodec
    {
        // Requested hardcoded key material.
        private const string HardcodedSecret = "MAX-SF-GP-HARDCODED-KEY-2026";
        private static readonly byte[] Salt = Encoding.UTF8.GetBytes("m4xSfGpS@lt2026");

        public static string Encode(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return plainText;
            }

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var deriveBytes = new Rfc2898DeriveBytes(HardcodedSecret, Salt, 10000))
                {
                    aes.Key = deriveBytes.GetBytes(32);
                }

                aes.GenerateIV();

                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var memoryStream = new MemoryStream())
                {
                    // Prefix IV to the payload so Decode can reconstruct decryption context.
                    memoryStream.Write(aes.IV, 0, aes.IV.Length);
                    using (var cryptoStream = new CryptoStream(memoryStream, encryptor, CryptoStreamMode.Write))
                    using (var writer = new StreamWriter(cryptoStream))
                    {
                        writer.Write(plainText);
                    }

                    return Convert.ToBase64String(memoryStream.ToArray());
                }
            }
        }

        public static string Decode(string encodedText)
        {
            if (string.IsNullOrEmpty(encodedText))
            {
                return encodedText;
            }

            var fullCipher = Convert.FromBase64String(encodedText);
            var iv = new byte[16];
            var cipher = new byte[fullCipher.Length - iv.Length];

            Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

            using (var aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;

                using (var deriveBytes = new Rfc2898DeriveBytes(HardcodedSecret, Salt, 10000))
                {
                    aes.Key = deriveBytes.GetBytes(32);
                }

                aes.IV = iv;

                using (var decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                using (var memoryStream = new MemoryStream(cipher))
                using (var cryptoStream = new CryptoStream(memoryStream, decryptor, CryptoStreamMode.Read))
                using (var reader = new StreamReader(cryptoStream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}