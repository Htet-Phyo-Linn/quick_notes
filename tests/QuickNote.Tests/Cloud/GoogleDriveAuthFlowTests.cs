using System;
using System.Security.Cryptography;
using System.Text;
using QuickNote.Infrastructure.Cloud;
using Xunit;

namespace QuickNote.Tests.Cloud
{
    public class GoogleDriveAuthFlowTests
    {
        [Fact]
        public void GenerateCodeVerifier_ReturnsValidBase64Url()
        {
            var auth = new GoogleDriveAuthService();
            var verifier = auth.GenerateCodeVerifier();

            Assert.NotNull(verifier);
            Assert.NotEmpty(verifier);
            Assert.False(verifier.Contains('+'));
            Assert.False(verifier.Contains('/'));
            Assert.False(verifier.Contains('='));
            Assert.True(verifier.Length >= 43 && verifier.Length <= 128);
        }

        [Fact]
        public void GenerateCodeChallenge_ProducesDeterministicSha256()
        {
            var verifier = "test_code_verifier_1234567890_abcdefghijklmnopqrstuvwxyz";
            var challenge1 = GoogleDriveAuthService.GenerateCodeChallenge(verifier);
            var challenge2 = GoogleDriveAuthService.GenerateCodeChallenge(verifier);

            Assert.Equal(challenge1, challenge2);
            Assert.NotNull(challenge1);
            Assert.NotEmpty(challenge1);
            Assert.False(challenge1.Contains('+'));
            Assert.False(challenge1.Contains('/'));
            Assert.False(challenge1.Contains('='));
        }

        [Fact]
        public void BuildAuthorizationUrl_IncludesRequiredOAuthParameters()
        {
            var auth = new GoogleDriveAuthService();
            var url = auth.BuildAuthorizationUrl(null, out var verifier);

            Assert.NotNull(verifier);
            Assert.NotEmpty(verifier);
            Assert.Contains("accounts.google.com/o/oauth2/v2/auth", url);
            Assert.Contains("client_id=", url);
            Assert.Contains("response_type=code", url);
            Assert.Contains("redirect_uri=", url);
            Assert.Contains("scope=", url);
            Assert.Contains("code_challenge=", url);
            Assert.Contains("code_challenge_method=S256", url);
        }

        [Theory]
        [InlineData("4/0AY0e-gDummyCode12345", "4/0AY0e-gDummyCode12345")]
        [InlineData("http://localhost/?code=4/0AY0e-gUrlCode123", "4/0AY0e-gUrlCode123")]
        [InlineData("http://localhost/?code=4%2F0AY0e-gEncodedCode&scope=drive", "4/0AY0e-gEncodedCode")]
        [InlineData("http://localhost:80/?state=xyz&code=4/0AY0e-gCodeWithState&scope=email", "4/0AY0e-gCodeWithState")]
        public void ExtractCode_HandlesRawCodeAndRedirectUrls(string input, string expected)
        {
            var extracted = ExtractQueryParamCode(input);
            Assert.Equal(expected, extracted);
        }

        private static string ExtractQueryParamCode(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            input = input.Trim();

            if (input.Contains("code="))
            {
                var qIndex = input.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
                var sub = input.Substring(qIndex + 5);
                var ampIndex = sub.IndexOf('&');
                var rawCode = ampIndex >= 0 ? sub.Substring(0, ampIndex) : sub;
                return Uri.UnescapeDataString(rawCode);
            }

            return input;
        }
    }
}