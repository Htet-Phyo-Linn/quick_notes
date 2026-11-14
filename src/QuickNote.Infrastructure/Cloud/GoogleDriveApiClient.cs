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
    public class GoogleDriveApiClient
    {
        private const string DriveApiBaseUrl = "https://www.googleapis.com/drive/v3/files";
        private const string UploadApiBaseUrl = "https://www.googleapis.com/upload/drive/v3/files";

        private readonly HttpClient _httpClient;
        private readonly Func<Task<string>> _getAccessToken;

        public GoogleDriveApiClient(Func<Task<string>> getAccessToken, HttpClient? httpClient = null)
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

        public async Task<string> EnsureQuickNoteFolderAsync()
        {
            // 1. Search for existing QuickNote folder
            var query = Uri.EscapeDataString("name = 'QuickNote' and mimeType = 'application/vnd.google-apps.folder' and trashed = false");
            var searchUrl = $"{DriveApiBaseUrl}?q={query}&fields=files(id,name,mimeType)";

            using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, searchUrl);
            using var response = await _httpClient.SendAsync(req);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<GoogleDriveFileListResponse>(json);
                if (list?.Files != null && list.Files.Count > 0)
                {
                    return list.Files[0].Id;
                }
            }

            // 2. Folder does not exist -> Create it
            using var createReq = await CreateAuthorizedRequestAsync(HttpMethod.Post, DriveApiBaseUrl);
            var payload = new
            {
                name = "QuickNote",
                mimeType = "application/vnd.google-apps.folder"
            };
            createReq.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var createRes = await _httpClient.SendAsync(createReq);
            var createJson = await createRes.Content.ReadAsStringAsync();

            if (!createRes.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to create 'QuickNote' folder in Google Drive: {createRes.StatusCode} - {createJson}");
            }

            var createdFolder = JsonSerializer.Deserialize<GoogleDriveFileDto>(createJson);
            return createdFolder?.Id ?? throw new Exception("Empty folder creation response from Google Drive");
        }

        public async Task<string> EnsureSubfolderAsync(string parentFolderId, string folderName)
        {
            var cleanName = folderName.Trim().Replace("'", "\\'");
            var query = Uri.EscapeDataString($"'{parentFolderId}' in parents and name = '{cleanName}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false");
            var searchUrl = $"{DriveApiBaseUrl}?q={query}&fields=files(id,name,mimeType)";

            using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, searchUrl);
            using var response = await _httpClient.SendAsync(req);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<GoogleDriveFileListResponse>(json);
                if (list?.Files != null && list.Files.Count > 0)
                {
                    return list.Files[0].Id;
                }
            }

            // Create subfolder
            using var createReq = await CreateAuthorizedRequestAsync(HttpMethod.Post, DriveApiBaseUrl);
            var payload = new
            {
                name = folderName,
                mimeType = "application/vnd.google-apps.folder",
                parents = new[] { parentFolderId }
            };
            createReq.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var createRes = await _httpClient.SendAsync(createReq);
            var createJson = await createRes.Content.ReadAsStringAsync();

            if (!createRes.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to create folder '{folderName}' in Google Drive: {createRes.StatusCode} - {createJson}");
            }

            var created = JsonSerializer.Deserialize<GoogleDriveFileDto>(createJson);
            return created?.Id ?? throw new Exception("Empty folder creation response from Google Drive");
        }

        public async Task<string> EnsureFolderPathAsync(string rootFolderId, string relativeDirectory)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectory)) return rootFolderId;
            var segments = relativeDirectory.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            var currentParent = rootFolderId;
            foreach (var segment in segments)
            {
                currentParent = await EnsureSubfolderAsync(currentParent, segment);
            }
            return currentParent;
        }

        public async Task<List<(string RelativePath, GoogleDriveFileDto File)>> ListAllNotesRecursiveAsync(string rootFolderId, string prefix = "")
        {
            var result = new List<(string RelativePath, GoogleDriveFileDto File)>();
            var items = await ListFolderNotesAsync(rootFolderId);

            foreach (var item in items)
            {
                if (item.IsFolder)
                {
                    var subPrefix = string.IsNullOrEmpty(prefix) ? item.Name : $"{prefix}/{item.Name}";
                    var subItems = await ListAllNotesRecursiveAsync(item.Id, subPrefix);
                    result.AddRange(subItems);
                }
                else if (item.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                {
                    var relPath = string.IsNullOrEmpty(prefix) ? item.Name : $"{prefix}/{item.Name}";
                    result.Add((relPath, item));
                }
            }

            return result;
        }

        public async Task<List<GoogleDriveFileDto>> ListFolderNotesAsync(string folderId)
        {
            var result = new List<GoogleDriveFileDto>();
            string? pageToken = null;

            do
            {
                var query = Uri.EscapeDataString($"'{folderId}' in parents and trashed = false");
                var url = $"{DriveApiBaseUrl}?q={query}&fields=files(id,name,mimeType,modifiedTime,md5Checksum,size)&pageSize=1000";
                if (!string.IsNullOrEmpty(pageToken))
                {
                    url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
                }

                using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(req);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    throw new Exception($"Failed to list Google Drive files: {response.StatusCode} - {err}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<GoogleDriveFileListResponse>(json);

                if (list?.Files != null)
                {
                    result.AddRange(list.Files);
                }
                pageToken = list?.NextPageToken;
            }
            while (!string.IsNullOrEmpty(pageToken));

            return result;
        }

        public async Task<string> DownloadNoteTextAsync(string fileId)
        {
            var url = $"{DriveApiBaseUrl}/{fileId}?alt=media";
            using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, url);
            using var response = await _httpClient.SendAsync(req);

            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to download note from Google Drive: {response.StatusCode} - {err}");
            }

            return await response.Content.ReadAsStringAsync();
        }

        public async Task<GoogleDriveFileDto> UploadNewNoteAsync(string folderId, string fileName, string content)
        {
            var url = $"{UploadApiBaseUrl}?uploadType=multipart";
            using var req = await CreateAuthorizedRequestAsync(HttpMethod.Post, url);

            var boundary = "----QuickNoteBoundary" + Guid.NewGuid().ToString("N");
            var multipartContent = new MultipartContent("related", boundary);

            // Part 1: Metadata JSON
            var metadata = new
            {
                name = fileName,
                parents = new[] { folderId },
                mimeType = "text/markdown"
            };
            var metaContent = new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json");
            multipartContent.Add(metaContent);

            // Part 2: File Body
            var fileContent = new StringContent(content, Encoding.UTF8, "text/markdown");
            multipartContent.Add(fileContent);

            req.Content = multipartContent;

            using var response = await _httpClient.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to upload note '{fileName}' to Google Drive: {response.StatusCode} - {json}");
            }

            return JsonSerializer.Deserialize<GoogleDriveFileDto>(json) ?? throw new Exception("Empty upload response");
        }

        public async Task<GoogleDriveFileDto> UpdateExistingNoteAsync(string fileId, string content)
        {
            var url = $"{UploadApiBaseUrl}/{fileId}?uploadType=media";
            using var req = await CreateAuthorizedRequestAsync(HttpMethod.Patch, url);
            req.Content = new StringContent(content, Encoding.UTF8, "text/markdown");

            using var response = await _httpClient.SendAsync(req);
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to update note in Google Drive: {response.StatusCode} - {json}");
            }

            return JsonSerializer.Deserialize<GoogleDriveFileDto>(json) ?? throw new Exception("Empty update response");
        }

        public async Task<bool> DeleteNoteAsync(string fileId)
        {
            try
            {
                var url = $"{DriveApiBaseUrl}/{fileId}";
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
