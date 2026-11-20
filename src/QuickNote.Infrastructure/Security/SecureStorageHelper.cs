using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QuickNote.Infrastructure.Security
{
    /// <summary>
    /// Provides secure platform-specific encryption for sensitive credentials
    /// (OAuth refresh tokens, secrets) using Windows DPAPI on Windows and salted AES on mobile/cross-platform.
    /// </summary>
    public static class SecureStorageHelper
    {
        public const string DpapiPrefix = "enc:dpapi:";
        public const string AesPrefix = "enc:aes:";
        public const string LegacyPrefix = "enc:";

        public static string Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return string.Empty;
            if (IsEncrypted(plainText)) return plainText;

            try
            {
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        var plainBytes = Encoding.UTF8.GetBytes(plainText);
                        var cipherBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
                        return DpapiPrefix + Convert.ToBase64String(cipherBytes);
                    }
                    catch
                    {
                        // Fall back to device AES if DPAPI fails
                    }
                }

                return ProtectWithAes(plainText);
            }
            catch
            {
                return plainText;
            }
        }

        public static string Unprotect(string? cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return string.Empty;
            if (!IsEncrypted(cipherText)) return cipherText;

            try
            {
                if (cipherText.StartsWith(DpapiPrefix, StringComparison.Ordinal))
                {
                    if (OperatingSystem.IsWindows())
                    {
                        var rawBase64 = cipherText.Substring(DpapiPrefix.Length);
                        var cipherBytes = Convert.FromBase64String(rawBase64);
                        var plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
                        return Encoding.UTF8.GetString(plainBytes);
                    }
                    else
                    {
                        // Cannot unprotect DPAPI on non-Windows
                        return string.Empty;
                    }
                }

                if (cipherText.StartsWith(AesPrefix, StringComparison.Ordinal))
                {
                    return UnprotectWithAes(cipherText.Substring(AesPrefix.Length));
                }

                if (cipherText.StartsWith(LegacyPrefix, StringComparison.Ordinal))
                {
                    return UnprotectWithAes(cipherText.Substring(LegacyPrefix.Length));
                }

                return cipherText;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool IsEncrypted(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            return text.StartsWith(DpapiPrefix, StringComparison.Ordinal) ||
                   text.StartsWith(AesPrefix, StringComparison.Ordinal) ||
                   text.StartsWith(LegacyPrefix, StringComparison.Ordinal);
        }

        public static string ProtectWithAes(string plainText)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            var key = DeriveDeviceKey();

            using var aes = Aes.Create();
            aes.Key = key;
            aes.GenerateIV();

            using var ms = new MemoryStream();
            ms.Write(aes.IV, 0, aes.IV.Length);

            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
                cs.FlushFinalBlock();
            }

            return AesPrefix + Convert.ToBase64String(ms.ToArray());
        }

        public static string UnprotectWithAes(string base64Payload)
        {
            var cipherBytes = Convert.FromBase64String(base64Payload);

            using var ms = new MemoryStream(cipherBytes);
            var iv = new byte[16];
            int bytesRead = ms.Read(iv, 0, 16);
            if (bytesRead < 16) return string.Empty;

            var key = DeriveDeviceKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;

            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var reader = new StreamReader(cs, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        private static byte[] DeriveDeviceKey()
        {
            var seed = (Environment.MachineName ?? "Device") +
                       (Environment.UserName ?? "User") +
                       Environment.OSVersion.VersionString +
                       "_QuickNoteKey_Salt2026";
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
        }
    }
}
