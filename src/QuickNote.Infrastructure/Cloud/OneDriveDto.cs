using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace QuickNote.Infrastructure.Cloud
{
    public class OAuthTokenResponse
    {
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = "Bearer";

        [JsonPropertyName("scope")]
        public string Scope { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("ext_expires_in")]
        public int ExtExpiresIn { get; set; }

        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("error_description")]
        public string? ErrorDescription { get; set; }
    }

    public class UserProfileDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("userPrincipalName")]
        public string UserPrincipalName { get; set; } = string.Empty;

        [JsonPropertyName("mail")]
        public string? Mail { get; set; }

        public string EffectiveEmail => !string.IsNullOrWhiteSpace(Mail) ? Mail : UserPrincipalName;
    }

    public class DriveItemDto
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("eTag")]
        public string ETag { get; set; } = string.Empty;

        [JsonPropertyName("cTag")]
        public string CTag { get; set; } = string.Empty;

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("lastModifiedDateTime")]
        public DateTimeOffset LastModifiedDateTime { get; set; }

        [JsonPropertyName("folder")]
        public FolderFacet? Folder { get; set; }

        [JsonPropertyName("file")]
        public FileFacet? File { get; set; }

        [JsonPropertyName("@microsoft.graph.downloadUrl")]
        public string? DownloadUrl { get; set; }

        public bool IsFolder => Folder != null;
    }

    public class FolderFacet
    {
        [JsonPropertyName("childCount")]
        public int ChildCount { get; set; }
    }

    public class FileFacet
    {
        [JsonPropertyName("mimeType")]
        public string? MimeType { get; set; }
    }

    public class DriveItemListResponse
    {
        [JsonPropertyName("value")]
        public List<DriveItemDto> Value { get; set; } = new();

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }
}
