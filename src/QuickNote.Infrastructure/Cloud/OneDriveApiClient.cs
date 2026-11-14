using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace QuickNote.Infrastructure.Cloud
{
    public class OneDriveApiClient
    {
        private const string GraphBaseUrl = "https://graph.microsoft.com/v1.0";
        private readonly HttpClient _httpClient;
        private readonly Func<Task<string>> _getAccessToken;

        public OneDriveApiClient(Func<Task<string>> getAccessToken, HttpClient? httpClient = null)
        {
            _getAccessToken = getAccessToken ?? throw new ArgumentNullException(nameof(getAccessToken));
            _httpClient = httpClient ?? new HttpClient();
        }

        private async Task<HttpRequestMessage> CreateAuthorizedRequestAsync(HttpMethod method, string url)
        {
            var token = await _getAccessToken();
            var req = new HttpRequestMessage(method, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return req;
        }

        public async Task<DriveItemDto?> GetQuickNoteFolderAsync()
        {
            try
            {
                var url = $"{GraphBaseUrl}/me/drive/root:/QuickNote";
                using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(req);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<DriveItemDto>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OneDriveApiClient] Error finding QuickNote folder: {ex.Message}");
            }
            return null;
        }

        public async Task<DriveItemDto> CreateQuickNoteFolderAsync()
        {
            var url = $"{GraphBaseUrl}/me/drive/root/children";
            using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, url);

            var payload = new
            {
                name = "QuickNote",
                folder = new { },
                conflictBehavior = "replace"
            };
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to create QuickNote folder in OneDrive: {response.StatusCode} - {json}");
            }

            return JsonSerializer.Deserialize<DriveItemDto>(json) ?? throw new Exception("Empty folder response");
        }

        public async Task<DriveItemDto> EnsureQuickNoteFolderAsync()
        {
            var folder = await GetQuickNoteFolderAsync();
            if (folder != null) return folder;
            return await CreateQuickNoteFolderAsync();
        }

        public async Task<List<DriveItemDto>> ListFolderItemsAsync(string folderId)
        {
            var items = new List<DriveItemDto>();
            var url = $"{GraphBaseUrl}/me/drive/items/{folderId}/children?$select=id,name,eTag,cTag,size,lastModifiedDateTime,file,folder";

            while (!string.IsNullOrEmpty(url))
            {
                using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(req);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to list OneDrive folder items: {response.StatusCode} - {err}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var page = JsonSerializer.Deserialize<DriveItemListResponse>(json);

                if (page?.Value != null)
                {
                    items.AddRange(page.Value);
                }
                url = page?.NextLink;
            }

            return items;
        }

        public async Task<string> DownloadFileContentAsync(string fileId)
        {
            var url = $"{GraphBaseUrl}/me/drive/items/{fileId}/content";
            using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(req);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to download file from OneDrive: {response.StatusCode} - {err}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<DriveItemDto> UploadFileContentAsync(string folderId, string fileName, string content)
        {
            var encodedFileName = Uri.EscapeDataString(fileName);
            var url = $"{GraphBaseUrl}/me/drive/items/{folderId}:/{encodedFileName}:/content";
            using var req = await CreateAuthorizedRequestAsync(HttpMethod.Put, url);
            req.Content = new StringContent(content, Encoding.UTF8, "text/plain");

            using var response = await _httpClient.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to upload note '{fileName}' to OneDrive: {response.StatusCode} - {json}");
            }

            return JsonSerializer.Deserialize<DriveItemDto>(json) ?? throw new Exception("Empty upload response");
        }

        public async Task<bool> DeleteFileAsync(string fileId)
        {
            try
            {
                var url = $"{GraphBaseUrl}/me/drive/items/{fileId}";
                using var req = await CreateAuthorizedRequestAsync(HttpMethod.Delete, url);
                using var response = await _httpClient.SendAsync(req);
                return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound;
            }
            catch
            {
                return false;
            }
        }
    }
}
