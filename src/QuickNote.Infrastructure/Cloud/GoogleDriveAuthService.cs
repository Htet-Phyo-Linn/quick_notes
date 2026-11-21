using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuickNote.Infrastructure.Cloud
{
    public class GoogleDriveAuthService
    {
        public const string DefaultClientId = "614992283073-sc1hivoo37bjt5rap571kuo7j1kjjv86.apps.googleusercontent.com";
        public const string DefaultClientSecret = "";
        public const string RedirectUri = "http://localhost";
        public const string AuthorizeEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        public const string TokenEndpoint = "https://oauth2.googleapis.com/token";
        public const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v2/userinfo";
        public const string Scopes = "https://www.googleapis.com/auth/drive.file https://www.googleapis.com/auth/userinfo.email https://www.googleapis.com/auth/userinfo.profile openid";

        private readonly HttpClient _httpClient;

        public GoogleDriveAuthService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Base64UrlEncode(bytes);
        }

        public static string GenerateCodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            return Base64UrlEncode(challengeBytes);
        }

        public string BuildAuthorizationUrl(string? clientId, out string codeVerifier)
        {
            var effectiveClientId = !string.IsNullOrWhiteSpace(clientId) ? clientId : DefaultClientId;
            codeVerifier = GenerateCodeVerifier();
            var challenge = GenerateCodeChallenge(codeVerifier);

            var query = new StringBuilder();
            query.Append($"{AuthorizeEndpoint}?");
            query.Append($"client_id={Uri.EscapeDataString(effectiveClientId)}");
            query.Append("&response_type=code");
            query.Append($"&redirect_uri={Uri.EscapeDataString(RedirectUri)}");
            query.Append($"&scope={Uri.EscapeDataString(Scopes)}");
            query.Append("&access_type=offline");
            query.Append("&prompt=consent");
            query.Append($"&code_challenge={Uri.EscapeDataString(challenge)}");
            query.Append("&code_challenge_method=S256");

            return query.ToString();
        }

        public async Task<GoogleTokenResponse> ExchangeCodeForTokenAsync(string? clientId, string? clientSecret, string code, string codeVerifier)
        {
            var effectiveClientId = !string.IsNullOrWhiteSpace(clientId) ? clientId : DefaultClientId;
            var effectiveSecret = !string.IsNullOrWhiteSpace(clientSecret) ? clientSecret : DefaultClientSecret;

            var parameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("client_id", effectiveClientId),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", RedirectUri),
                new KeyValuePair<string, string>("code_verifier", codeVerifier)
            };

            if (!string.IsNullOrWhiteSpace(effectiveSecret))
            {
                parameters.Add(new KeyValuePair<string, string>("client_secret", effectiveSecret));
            }

            var content = new FormUrlEncodedContent(parameters);

            using var response = await _httpClient.PostAsync(TokenEndpoint, content);
            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(json);

            if (!response.IsSuccessStatusCode)
            {
                var err = tokenResponse?.ErrorDescription ?? tokenResponse?.Error ?? response.ReasonPhrase ?? "Failed to acquire Google token";
                throw new Exception($"Google authentication failed: {err}");
            }

            return tokenResponse ?? throw new Exception("Empty token response received from Google");
        }

        public async Task<GoogleTokenResponse> RefreshTokenAsync(string? clientId, string? clientSecret, string refreshToken)
        {
            var effectiveClientId = !string.IsNullOrWhiteSpace(clientId) ? clientId : DefaultClientId;
            var effectiveSecret = !string.IsNullOrWhiteSpace(clientSecret) ? clientSecret : DefaultClientSecret;

            var parameters = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("client_id", effectiveClientId),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            };

            if (!string.IsNullOrWhiteSpace(effectiveSecret))
            {
                parameters.Add(new KeyValuePair<string, string>("client_secret", effectiveSecret));
            }

            var content = new FormUrlEncodedContent(parameters);

            using var response = await _httpClient.PostAsync(TokenEndpoint, content);
            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(json);

            if (!response.IsSuccessStatusCode)
            {
                var err = tokenResponse?.ErrorDescription ?? tokenResponse?.Error ?? response.ReasonPhrase ?? "Failed to refresh Google token";
                throw new Exception($"Failed to refresh Google token: {err}");
            }

            return tokenResponse ?? throw new Exception("Empty token response received from Google");
        }

        public async Task<GoogleUserInfoDto?> GetUserProfileAsync(string accessToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<GoogleUserInfoDto>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GoogleDriveAuthService] Error fetching user profile: {ex.Message}");
            }
            return null;
        }

        private static string Base64UrlEncode(byte[] input)
        {
            var base64 = Convert.ToBase64String(input);
            return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
