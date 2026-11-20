using System;
using QuickNote.Infrastructure.Security;
using Xunit;

namespace QuickNote.Tests.Security
{
    public class SecureStorageHelperTests
    {
        [Fact]
        public void ProtectAndUnprotect_RoundTripsCorrectly()
        {
            var secret = "my_super_secret_refresh_token_12345!@#$%^";
            var protectedText = SecureStorageHelper.Protect(secret);

            Assert.NotNull(protectedText);
            Assert.StartsWith("enc:", protectedText);
            Assert.NotEqual(secret, protectedText);

            var recovered = SecureStorageHelper.Unprotect(protectedText);
            Assert.Equal(secret, recovered);
        }

        [Fact]
        public void ProtectAndUnprotect_AesExplicit_RoundTripsCorrectly()
        {
            var secret = "aes_platform_salted_secret_998877";
            var encrypted = SecureStorageHelper.ProtectWithAes(secret);

            Assert.StartsWith(SecureStorageHelper.AesPrefix, encrypted);
            var recovered = SecureStorageHelper.Unprotect(encrypted);
            Assert.Equal(secret, recovered);
        }

        [Fact]
        public void Unprotect_LegacyPlaintext_ReturnsPlaintextDirectly()
        {
            var legacyPlaintext = "1//04ABCDEF123456_legacy_token";
            var result = SecureStorageHelper.Unprotect(legacyPlaintext);

            Assert.Equal(legacyPlaintext, result);
        }

        [Fact]
        public void Protect_NullOrEmpty_ReturnsEmptyString()
        {
            Assert.Equal(string.Empty, SecureStorageHelper.Protect(null));
            Assert.Equal(string.Empty, SecureStorageHelper.Protect(""));
            Assert.Equal(string.Empty, SecureStorageHelper.Unprotect(null));
            Assert.Equal(string.Empty, SecureStorageHelper.Unprotect(""));
        }

        [Fact]
        public void Protect_AlreadyEncryptedString_DoesNotDoubleEncrypt()
        {
            var secret = "test_token";
            var protectedText = SecureStorageHelper.Protect(secret);
            var reProtected = SecureStorageHelper.Protect(protectedText);

            Assert.Equal(protectedText, reProtected);
        }
    }
}
