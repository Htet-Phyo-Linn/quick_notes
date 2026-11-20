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

        private static readonly Random _jitterRandom = new();

        private static async Task DelayWithJitterAsync(int attempt)
        {
            int baseDelayMs = (int)(Math.Pow(2, attempt) * 400); // 400ms, 800ms, 1600ms
            int jitterMs;
            lock (_jitterRandom)
            {
                jitterMs = _jitterRandom.Next(50, 250);
            }
            await Task.Delay(baseDelayMs + jitterMs).ConfigureAwait(false);
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
            // 1. Search for existing QuickNote folder directly in root (My Drive)
            var query = Uri.EscapeDataString("'root' in parents and name = 'QuickNote' and mimeType = 'application/vnd.google-apps.folder' and trashed = false");
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

            // 2. Folder does not exist in root -> Create it under 'root'
            using var createReq = await CreateAuthorizedRequestAsync(HttpMethod.Post, DriveApiBaseUrl);
            var payload = new
            {
                name = "QuickNote",
                mimeType = "application/vnd.google-apps.folder",
                parents = new[] { "root" }
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

        public async Task<string> EnsureFolderPathCachedAsync(string rootFolderId, string relativeDirectory, Dictionary<string, string> folderCache)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectory)) return rootFolderId;
            var normalized = relativeDirectory.Replace('\\', '/').Trim('/');
            if (folderCache.TryGetValue(normalized, out var cachedId))
            {
                return cachedId;
            }

            var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var currentPath = "";
            var currentParentId = rootFolderId;

            foreach (var segment in segments)
            {
                currentPath = string.IsNullOrEmpty(currentPath) ? segment : $"{currentPath}/{segment}";
                if (folderCache.TryGetValue(currentPath, out var segId))
                {
                    currentParentId = segId;
                }
                else
                {
                    currentParentId = await EnsureSubfolderAsync(currentParentId, segment);
                    folderCache[currentPath] = currentParentId;
                }
            }

            return currentParentId;
        }

        public async Task<RemoteSyncSnapshot> FetchRemoteSnapshotAsync(string rootFolderId)
        {
            var snapshot = new RemoteSyncSnapshot();
            snapshot.RemoteFolders[""] = rootFolderId;

            try
            {
                // Single-call optimization: Fetch all files & folders created by QuickNote in 1 API call
                var allItems = new List<GoogleDriveFileDto>();
                string? pageToken = null;

                do
                {
                    var query = Uri.EscapeDataString("trashed = false and (mimeType = 'application/vnd.google-apps.folder' or name contains '.md')");
                    var url = $"{DriveApiBaseUrl}?q={query}&fields=nextPageToken,files(id,name,mimeType,modifiedTime,md5Checksum,size,parents)&pageSize=1000";
                    if (!string.IsNullOrEmpty(pageToken))
                    {
                        url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
                    }

                    using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, url);
                    using var response = await _httpClient.SendAsync(req);

                    if (!response.IsSuccessStatusCode)
                    {
                        break; // Fallback to BFS queue below
                    }

                    var json = await response.Content.ReadAsStringAsync();
                    var list = JsonSerializer.Deserialize<GoogleDriveFileListResponse>(json);
                    if (list?.Files != null)
                    {
                        allItems.AddRange(list.Files);
                    }
                    pageToken = list?.NextPageToken;
                } while (!string.IsNullOrEmpty(pageToken));

                if (allItems.Count > 0)
                {
                    // Map folder IDs to parent IDs and names
                    var folderMap = new Dictionary<string, (string Name, string? ParentId)>(StringComparer.OrdinalIgnoreCase);
                    var files = new List<GoogleDriveFileDto>();

                    foreach (var item in allItems)
                    {
                        var parentId = item.Parents != null && item.Parents.Count > 0 ? item.Parents[0] : null;
                        if (item.IsFolder)
                        {
                            if (!string.Equals(item.Id, rootFolderId, StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(item.Name, "QuickNote", StringComparison.OrdinalIgnoreCase))
                            {
                                folderMap[item.Id] = (item.Name, parentId);
                            }
                        }
                        else if (item.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        {
                            files.Add(item);
                        }
                    }

                    // Resolve folder paths relative to rootFolderId
                    var resolvedFolders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // folderId -> relativePath
                    resolvedFolders[rootFolderId] = "";

                    string? ResolveFolderPath(string folderId, HashSet<string> visited)
                    {
                        if (resolvedFolders.TryGetValue(folderId, out var existingPath)) return existingPath;
                        if (!folderMap.TryGetValue(folderId, out var info) || !visited.Add(folderId)) return null;

                        if (string.IsNullOrEmpty(info.ParentId)) return null;
                        var parentRelPath = ResolveFolderPath(info.ParentId, visited);
                        if (parentRelPath == null) return null; // Not under rootFolderId

                        var currentRelPath = string.IsNullOrEmpty(parentRelPath) ? info.Name : $"{parentRelPath}/{info.Name}";
                        resolvedFolders[folderId] = currentRelPath;
                        snapshot.RemoteFolders[currentRelPath] = folderId;
                        return currentRelPath;
                    }

                    foreach (var fId in folderMap.Keys)
                    {
                        ResolveFolderPath(fId, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                    }

                    // Map files to their resolved relative paths
                    foreach (var file in files)
                    {
                        var pId = file.Parents != null && file.Parents.Count > 0 ? file.Parents[0] : null;
                        if (pId != null && resolvedFolders.TryGetValue(pId, out var folderRelPath))
                        {
                            var relFilePath = string.IsNullOrEmpty(folderRelPath) ? file.Name : $"{folderRelPath}/{file.Name}";
                            snapshot.RemoteFiles[relFilePath] = file;
                        }
                    }

                    return snapshot;
                }
            }
            catch (Exception ex)
            {
                SyncLogger.Log($"Single-call snapshot fallback: {ex.Message}");
            }

            // Fallback: BFS folder-by-folder scan
            var queue = new Queue<(string FolderId, string RelativePath)>();
            queue.Enqueue((rootFolderId, ""));

            while (queue.Count > 0)
            {
                var (currentFolderId, currentRelPath) = queue.Dequeue();
                var items = await ListFolderNotesAsync(currentFolderId);

                foreach (var item in items)
                {
                    if (item.IsFolder)
                    {
                        if (string.Equals(item.Name, "QuickNote", StringComparison.OrdinalIgnoreCase))
                        {
                            // Skip any recursive QuickNote subfolder inside the root QuickNote folder
                            continue;
                        }

                        var relFolderPath = string.IsNullOrEmpty(currentRelPath) ? item.Name : $"{currentRelPath}/{item.Name}";
                        if (!snapshot.RemoteFolders.ContainsKey(relFolderPath))
                        {
                            snapshot.RemoteFolders[relFolderPath] = item.Id;
                            queue.Enqueue((item.Id, relFolderPath));
                        }
                    }
                    else if (item.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        var relFilePath = string.IsNullOrEmpty(currentRelPath) ? item.Name : $"{currentRelPath}/{item.Name}";
                        snapshot.RemoteFiles[relFilePath] = item;
                    }
                }
            }

            return snapshot;
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
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(req);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                int statusCode = (int)response.StatusCode;
                if ((statusCode == 429 || statusCode >= 500) && attempt < 2)
                {
                    await DelayWithJitterAsync(attempt);
                    continue;
                }

                var err = await response.Content.ReadAsStringAsync();
                throw new Exception($"Failed to download note from Google Drive: {response.StatusCode} - {err}");
            }
            throw new Exception("Failed to download note from Google Drive after retries.");
        }

        public async Task<GoogleDriveFileDto> UploadNewNoteAsync(string folderId, string fileName, string content)
        {
            var url = $"{UploadApiBaseUrl}?uploadType=multipart";
            for (int attempt = 0; attempt < 3; attempt++)
            {
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

                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<GoogleDriveFileDto>(json) ?? throw new Exception("Empty upload response");
                }

                int statusCode = (int)response.StatusCode;
                if ((statusCode == 429 || statusCode >= 500) && attempt < 2)
                {
                    await DelayWithJitterAsync(attempt);
                    continue;
                }

                throw new Exception($"Failed to upload note '{fileName}' to Google Drive: {response.StatusCode} - {json}");
            }
            throw new Exception($"Failed to upload note '{fileName}' after retries.");
        }

        public async Task<GoogleDriveFileDto> UpdateExistingNoteAsync(string fileId, string content)
        {
            var url = $"{UploadApiBaseUrl}/{fileId}?uploadType=media";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using var req = await CreateAuthorizedRequestAsync(HttpMethod.Patch, url);
                req.Content = new StringContent(content, Encoding.UTF8, "text/markdown");

                using var response = await _httpClient.SendAsync(req);
                var json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<GoogleDriveFileDto>(json) ?? throw new Exception("Empty update response");
                }

                int statusCode = (int)response.StatusCode;
                if ((statusCode == 429 || statusCode >= 500) && attempt < 2)
                {
                    await DelayWithJitterAsync(attempt);
                    continue;
                }

                throw new Exception($"Failed to update note in Google Drive: {response.StatusCode} - {json}");
            }
            throw new Exception($"Failed to update note in Google Drive after retries.");
        }

        public async Task<bool> DeleteNoteAsync(string fileId)
        {
            var url = $"{DriveApiBaseUrl}/{fileId}";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var req = await CreateAuthorizedRequestAsync(HttpMethod.Delete, url);
                    using var response = await _httpClient.SendAsync(req);
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return true;
                    }
                    int statusCode = (int)response.StatusCode;
                    if ((statusCode == 429 || statusCode >= 500) && attempt < 2)
                    {
                        await DelayWithJitterAsync(attempt);
                        continue;
                    }
                    return false;
                }
                catch when (attempt < 2)
                {
                    await DelayWithJitterAsync(attempt);
                }
                catch
                {
                    return false;
                }
            }
            return false;
        }

        public async Task<GoogleDriveFileDto> MoveOrRenameNoteAsync(string fileId, string newName, string? newParentFolderId = null, string? oldParentFolderId = null)
        {
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(newParentFolderId))
            {
                queryParams.Add($"addParents={Uri.EscapeDataString(newParentFolderId)}");
            }
            if (!string.IsNullOrEmpty(oldParentFolderId))
            {
                queryParams.Add($"removeParents={Uri.EscapeDataString(oldParentFolderId)}");
            }
            queryParams.Add("fields=id,name,parents,modifiedTime,md5Checksum,size");

            var url = $"{DriveApiBaseUrl}/{fileId}?{string.Join("&", queryParams)}";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                using var req = await CreateAuthorizedRequestAsync(HttpMethod.Patch, url);
                var body = new { name = newName };
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                using var response = await _httpClient.SendAsync(req);
                var json = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<GoogleDriveFileDto>(json) ?? throw new Exception("Empty move/rename response");
                }

                int statusCode = (int)response.StatusCode;
                if ((statusCode == 429 || statusCode >= 500) && attempt < 2)
                {
                    await DelayWithJitterAsync(attempt);
                    continue;
                }

                throw new Exception($"Failed to move/rename note in Google Drive: {response.StatusCode} - {json}");
            }
            throw new Exception("Failed to move/rename note in Google Drive after retries.");
        }

        public const string CatalogFileName = "quicknote_catalog.json";

        public async Task<(string? FileId, string? JsonContent)> GetCatalogFileAsync(string rootFolderId)
        {
            try
            {
                var query = Uri.EscapeDataString($"'{rootFolderId}' in parents and name = '{CatalogFileName}' and trashed = false");
                var url = $"{DriveApiBaseUrl}?q={query}&fields=files(id,name,modifiedTime)&pageSize=1";
                using var req = await CreateAuthorizedRequestAsync(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(req);
                if (!response.IsSuccessStatusCode) return (null, null);

                var json = await response.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<GoogleDriveFileListResponse>(json);
                var catalogFile = list?.Files != null && list.Files.Count > 0 ? list.Files[0] : null;
                if (catalogFile == null || string.IsNullOrEmpty(catalogFile.Id))
                {
                    return (null, null);
                }

                // Download catalog content
                var contentUrl = $"{DriveApiBaseUrl}/{catalogFile.Id}?alt=media";
                using var contentReq = await CreateAuthorizedRequestAsync(HttpMethod.Get, contentUrl);
                using var contentRes = await _httpClient.SendAsync(contentReq);
                if (!contentRes.IsSuccessStatusCode) return (catalogFile.Id, null);

                var catalogJson = await contentRes.Content.ReadAsStringAsync();
                return (catalogFile.Id, catalogJson);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[GoogleDriveApiClient] Error getting catalog: {ex.Message}");
                return (null, null);
            }
        }

        public async Task<string> UploadOrUpdateCatalogAsync(string rootFolderId, string? existingCatalogFileId, string jsonContent)
        {
            if (!string.IsNullOrEmpty(existingCatalogFileId))
            {
                // Patch existing catalog with retry
                var url = $"{UploadApiBaseUrl}/{existingCatalogFileId}?uploadType=media";
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    using var req = await CreateAuthorizedRequestAsync(HttpMethod.Patch, url);
                    req.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                    using var response = await _httpClient.SendAsync(req);
                    if (response.IsSuccessStatusCode)
                    {
                        return existingCatalogFileId;
                    }

                    int statusCode = (int)response.StatusCode;
                    if ((statusCode == 429 || statusCode >= 500) && attempt < 2)
                    {
                        await DelayWithJitterAsync(attempt);
                        continue;
                    }
                    break;
                }
            }

            // Create new catalog using multipart
            var createUrl = $"{UploadApiBaseUrl}?uploadType=multipart";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var boundary = "----QuickNoteBoundary" + Guid.NewGuid().ToString("N");
                var multipartContent = new MultipartContent("related", boundary);

                var metadataObj = new
                {
                    name = CatalogFileName,
                    mimeType = "application/json",
                    parents = new[] { rootFolderId }
                };
                var metaContent = new StringContent(JsonSerializer.Serialize(metadataObj), Encoding.UTF8, "application/json");
                multipartContent.Add(metaContent);

                var fileContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                multipartContent.Add(fileContent);

                using var createReq = await CreateAuthorizedRequestAsync(HttpMethod.Post, createUrl);
                createReq.Content = multipartContent;

                using var createRes = await _httpClient.SendAsync(createReq);
                var resJson = await createRes.Content.ReadAsStringAsync();
                if (createRes.IsSuccessStatusCode)
                {
                    var created = JsonSerializer.Deserialize<GoogleDriveFileDto>(resJson);
                    return created?.Id ?? string.Empty;
                }

                int statusCode = (int)createRes.StatusCode;
                if ((statusCode == 429 || statusCode >= 500) && attempt < 2)
                {
                    await DelayWithJitterAsync(attempt);
                    continue;
                }

                throw new Exception($"Failed to upload catalog: {createRes.StatusCode} - {resJson}");
            }
            throw new Exception("Failed to upload catalog after retries.");
        }
    }

    public class RemoteSyncSnapshot
    {
        public Dictionary<string, string> RemoteFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, GoogleDriveFileDto> RemoteFiles { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
