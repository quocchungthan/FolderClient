using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using OpensourceLab.FileStorage.ServedServices;

public sealed class SyncApiClient
{
    private readonly HttpClient _httpClient;
    private readonly SyncClientOptions _options;

    public SyncApiClient(HttpClient httpClient, IOptions<SyncClientOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _httpClient.BaseAddress = new Uri(_options.ServerAddress.TrimEnd('/'));
        _httpClient.DefaultRequestHeaders.Remove(FileSyncHeaders.AccessKey);
        _httpClient.DefaultRequestHeaders.Add(FileSyncHeaders.AccessKey, _options.AccessKey);
    }

    public async Task<SyncHandshakeResponse> HandshakeAsync(string cursor, CancellationToken cancellationToken)
    {
        var request = new SyncHandshakeRequest
        {
            ClientId = Environment.MachineName,
            ClientVersion = "1.0.0",
            LocalFolderPath = _options.LocalFolderPath,
            KnownCursor = cursor
        };

        var response = await _httpClient.PostAsJsonAsync(FileSyncEndpoints.Handshake, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SyncHandshakeResponse>(cancellationToken) ?? new SyncHandshakeResponse { Succeeded = false, Error = "Handshake parse failed." };
    }

    public async Task<PullChangesResponse> PullAsync(string cursor, CancellationToken cancellationToken)
    {
        var request = new PullChangesRequest
        {
            Cursor = cursor,
            Limit = 500,
            IncludeContent = true
        };

        var response = await _httpClient.PostAsJsonAsync(FileSyncEndpoints.PullChanges, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PullChangesResponse>(cancellationToken) ?? new PullChangesResponse { Succeeded = false, Error = "Pull parse failed." };
    }

    public async Task<MetadataUpsertResponse> MetadataUpsertAsync(string path, string contentHash, long size, DateTime updatedAtUtc, CancellationToken cancellationToken)
    {
        var request = new MetadataUpsertRequest
        {
            Path = path,
            ContentHash = contentHash,
            Size = size,
            UpdatedAtUtc = updatedAtUtc
        };

        var response = await _httpClient.PostAsJsonAsync(FileSyncEndpoints.MetadataUpsert, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<MetadataUpsertResponse>(cancellationToken) ?? new MetadataUpsertResponse { Succeeded = false, Error = "Metadata parse failed." };
    }

    public async Task<UploadCommitResponse> UploadFileAsync(string path, string contentHash, string contentType, DateTime updatedAtUtc, CancellationToken cancellationToken)
    {
        var localPath = Path.Combine(_options.LocalFolderPath, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        var uploadId = Guid.NewGuid().ToString("N");
        var chunkSize = _options.UploadChunkBytes;
        var index = 0;

        await using (var input = File.OpenRead(localPath))
        {
            var buffer = new byte[chunkSize];
            int read;
            while ((read = await input.ReadAsync(buffer.AsMemory(0, chunkSize), cancellationToken)) > 0)
            {
                var bytes = buffer;
                if (read != chunkSize)
                {
                    bytes = buffer[..read];
                }

                var chunkRequest = new UploadChunkRequest
                {
                    UploadId = uploadId,
                    Path = path,
                    ChunkIndex = index,
                    ContentBase64 = Convert.ToBase64String(bytes),
                    ContentHash = contentHash,
                    TotalSize = input.Length,
                    UpdatedAtUtc = updatedAtUtc
                };

                var chunkResponse = await _httpClient.PostAsJsonAsync(FileSyncEndpoints.UploadChunk, chunkRequest, cancellationToken);
                chunkResponse.EnsureSuccessStatusCode();
                index++;
            }
        }

        var commitRequest = new UploadCommitRequest
        {
            UploadId = uploadId,
            Path = path,
            ContentHash = contentHash,
            Size = new FileInfo(localPath).Length,
            UpdatedAtUtc = updatedAtUtc,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType
        };

        var commitResponse = await _httpClient.PostAsJsonAsync(FileSyncEndpoints.UploadCommit, commitRequest, cancellationToken);
        commitResponse.EnsureSuccessStatusCode();
        return await commitResponse.Content.ReadFromJsonAsync<UploadCommitResponse>(cancellationToken) ?? new UploadCommitResponse { Succeeded = false, Error = "Commit parse failed." };
    }

    public async Task<TombstoneResponse> TombstoneAsync(string path, DateTime deletedAtUtc, CancellationToken cancellationToken)
    {
        var request = new TombstoneRequest
        {
            Path = path,
            DeletedAtUtc = deletedAtUtc
        };

        var response = await _httpClient.PostAsJsonAsync(FileSyncEndpoints.Tombstone, request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TombstoneResponse>(cancellationToken) ?? new TombstoneResponse { Succeeded = false, Error = "Tombstone parse failed." };
    }
}
