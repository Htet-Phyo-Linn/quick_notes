using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuickNote.Infrastructure.Cloud
{
    public class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = string.Empty;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    public class GoogleUserInfoDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string Email { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("picture")]
        public string? Picture { get; set; }
    }

    public class GoogleDriveFileDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("mimeType")]
        public string MimeType { get; set; } = string.Empty;

        [JsonPropertyName("modifiedTime")]
        public DateTimeOffset ModifiedTime { get; set; }

        [JsonPropertyName("md5Checksum")]
        public string? Md5Checksum { get; set; }

        [JsonPropertyName("size")]
        public string? Size { get; set; }

        [JsonPropertyName("trashed")]
        public bool Trashed { get; set; }

        [JsonPropertyName("parents")]
        public List<string>? Parents { get; set; }

        public bool IsFolder => string.Equals(MimeType, "application/vnd.google-apps.folder", StringComparison.OrdinalIgnoreCase);
    }

    public class GoogleDriveFileListResponse
    {
        [JsonPropertyName("files")]
        public List<GoogleDriveFileDto> Files { get; set; } = new();

        [JsonPropertyName("nextPageToken")]
        public string? NextPageToken { get; set; }
    }
}
