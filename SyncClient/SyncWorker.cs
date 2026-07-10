using Microsoft.Extensions.Options;

public sealed class SyncWorker : BackgroundService
{
    private readonly ILogger<SyncWorker> _logger;
    private readonly SyncApiClient _apiClient;
    private readonly LocalStateStore _stateStore;
    private readonly LocalFileScanner _scanner;
    private readonly SyncClientOptions _options;
    private VirtualPathAccessScope _accessScope;

    public SyncWorker(
        ILogger<SyncWorker> logger,
        SyncApiClient apiClient,
        LocalStateStore stateStore,
        LocalFileScanner scanner,
        IOptions<SyncClientOptions> options)
    {
        _logger = logger;
        _apiClient = apiClient;
        _stateStore = stateStore;
        _scanner = scanner;
        _options = options.Value;
        _accessScope = VirtualPathAccessScope.PublicOnly();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var state = await _stateStore.LoadAsync(stoppingToken);
        var handshake = await _apiClient.HandshakeAsync(state.Cursor, stoppingToken);
        if (!handshake.Succeeded)
        {
            throw new InvalidOperationException($"Handshake failed: {handshake.Error}");
        }

        if (!string.IsNullOrWhiteSpace(handshake.Cursor))
        {
            state.Cursor = handshake.Cursor;
        }

        _accessScope = VirtualPathAccessScope.FromWildcards(handshake.AllowedPathWildcards);
        state.AllowedPathWildcards = handshake.AllowedPathWildcards ?? new List<string>();
        await _stateStore.SaveAsync(state, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                state = await _stateStore.LoadAsync(stoppingToken);
                await PushLocalChangesAsync(state, stoppingToken);
                await PullRemoteChangesAsync(state, stoppingToken);
                await _stateStore.SaveAsync(state, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Sync cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), stoppingToken);
        }
    }

    private async Task PushLocalChangesAsync(LocalSyncState state, CancellationToken cancellationToken)
    {
        var scanned = await _scanner.ScanAsync(_accessScope, cancellationToken);
        var knownPaths = state.Files.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var currentPaths = scanned.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in scanned.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var needsPush = !state.Files.TryGetValue(entry.Path, out var known)
                || !string.Equals(known.ContentHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase)
                || known.Size != entry.Size;

            if (!needsPush)
            {
                continue;
            }

            var metadataResult = await _apiClient.MetadataUpsertAsync(entry.Path, entry.ContentHash, entry.Size, entry.UpdatedAtUtc, cancellationToken);
            if (!metadataResult.Succeeded)
            {
                _logger.LogWarning("Metadata upsert failed for {Path}: {Error}", entry.Path, metadataResult.Error);
                continue;
            }

            var commit = await _apiClient.UploadFileAsync(entry.Path, entry.ContentHash, GuessContentType(entry.Path), entry.UpdatedAtUtc, cancellationToken);
            if (!commit.Succeeded)
            {
                _logger.LogWarning("Upload commit failed for {Path}: {Error}", entry.Path, commit.Error);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(commit.Cursor))
            {
                state.Cursor = commit.Cursor;
            }

            state.Files[entry.Path] = new LocalFileState
            {
                ContentHash = entry.ContentHash,
                Size = entry.Size,
                UpdatedAtUtc = entry.UpdatedAtUtc
            };
        }

        var deleted = knownPaths.Except(currentPaths, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var path in deleted)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_accessScope.IsAllowed(path))
            {
                state.Files.Remove(path);
                continue;
            }

            var tombstone = await _apiClient.TombstoneAsync(path, DateTime.UtcNow, cancellationToken);
            if (!tombstone.Succeeded)
            {
                _logger.LogWarning("Tombstone failed for {Path}: {Error}", path, tombstone.Error);
                continue;
            }

            if (!string.IsNullOrWhiteSpace(tombstone.Cursor))
            {
                state.Cursor = tombstone.Cursor;
            }

            state.Files.Remove(path);
        }
    }

    private async Task PullRemoteChangesAsync(LocalSyncState state, CancellationToken cancellationToken)
    {
        var pull = await _apiClient.PullAsync(state.Cursor, cancellationToken);
        if (!pull.Succeeded)
        {
            _logger.LogWarning("Pull failed: {Error}", pull.Error);
            return;
        }

        foreach (var change in pull.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.IsNullOrWhiteSpace(change.Cursor))
            {
                state.Cursor = change.Cursor;
            }

            if (!_accessScope.IsAllowed(change.Path))
            {
                continue;
            }

            var localPath = Path.Combine(_options.LocalFolderPath, change.Path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (change.IsDeleted)
            {
                if (File.Exists(localPath))
                {
                    File.Delete(localPath);
                }

                state.Files.Remove(change.Path);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                if (!string.IsNullOrWhiteSpace(change.ContentBase64))
                {
                    var bytes = Convert.FromBase64String(change.ContentBase64);
                    await File.WriteAllBytesAsync(localPath, bytes, cancellationToken);
                }

                state.Files[change.Path] = new LocalFileState
                {
                    ContentHash = change.ContentHash,
                    Size = change.Size,
                    UpdatedAtUtc = change.UpdatedAtUtc
                };
            }
        }

        if (pull.Changes.Count == 0 && !string.IsNullOrWhiteSpace(pull.Cursor))
        {
            state.Cursor = pull.Cursor;
        }
    }

    private static string GuessContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => "application/octet-stream"
        };
    }
}
