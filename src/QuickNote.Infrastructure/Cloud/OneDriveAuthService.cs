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
    public class OneDriveAuthService
    {
        // Microsoft Graph public client application constants
        public const string DefaultClientId = "b68b7532-a5e2-4720-94a2-11c0f0d7e7e6";
        public const string RedirectUri = "https://login.microsoftonline.com/common/oauth2/nativeclient";
        public const string AuthorizeEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
        public const string TokenEndpoint = "https://login.microsoftonline.com/common/oauth2/v2.0/token";
        public const string Scopes = "Files.ReadWrite Files.ReadWrite.AppFolder offline_access User.Read";

        private readonly HttpClient _httpClient;
        private string? _currentVerifier;

        public OneDriveAuthService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            _currentVerifier = Base64UrlEncode(bytes);
            return _currentVerifier;
        }

        public static string GenerateCodeChallenge(string codeVerifier)
        {
            using var sha256 = SHA256.Create();
            var challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
            return Base64UrlEncode(challengeBytes);
        }

        public string BuildAuthorizationUrl(string clientId, out string codeVerifier)
        {
            codeVerifier = GenerateCodeVerifier();
            var challenge = GenerateCodeChallenge(codeVerifier);

            var query = new StringBuilder();
            query.Append($"{AuthorizeEndpoint}?");
            query.Append($"client_id={Uri.EscapeDataString(clientId)}");
            query.Append("&response_type=code");
            query.Append($"&redirect_uri={Uri.EscapeDataString(RedirectUri)}");
            query.Append("&response_mode=query");
            query.Append($"&scope={Uri.EscapeDataString(Scopes)}");
            query.Append($"&code_challenge={Uri.EscapeDataString(challenge)}");
            query.Append("&code_challenge_method=S256");
            query.Append("&prompt=select_account");

            return query.ToString();
        }

        public async Task<OAuthTokenResponse> ExchangeCodeForTokenAsync(string clientId, string code, string codeVerifier)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", RedirectUri),
                new KeyValuePair<string, string>("code_verifier", codeVerifier)
            });

            using var response = await _httpClient.PostAsync(TokenEndpoint, content);
            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(json);

            if (!response.IsSuccessStatusCode)
            {
                var err = tokenResponse?.ErrorDescription ?? tokenResponse?.Error ?? response.ReasonPhrase ?? "Failed to acquire token";
                throw new Exception($"Microsoft authentication failed: {err}");
            }

            return tokenResponse ?? throw new Exception("Empty token response received from Microsoft");
        }

        public async Task<OAuthTokenResponse> RefreshTokenAsync(string clientId, string refreshToken)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("redirect_uri", RedirectUri),
                new KeyValuePair<string, string>("scope", Scopes)
            });

            using var response = await _httpClient.PostAsync(TokenEndpoint, content);
            var json = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(json);

            if (!response.IsSuccessStatusCode)
            {
                var err = tokenResponse?.ErrorDescription ?? tokenResponse?.Error ?? response.ReasonPhrase ?? "Failed to refresh token";
                throw new Exception($"Failed to refresh Microsoft token: {err}");
            }

            return tokenResponse ?? throw new Exception("Empty token response received from Microsoft");
        }

        public async Task<UserProfileDto?> GetUserProfileAsync(string accessToken)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://graph.microsoft.com/v1.0/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<UserProfileDto>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OneDriveAuthService] Error fetching user profile: {ex.Message}");
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
