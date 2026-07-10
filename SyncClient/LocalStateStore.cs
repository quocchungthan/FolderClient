using System.Text.Json;
using Microsoft.Extensions.Options;

public sealed class LocalSyncState
{
    public string Cursor { get; set; } = string.Empty;
    public List<string> AllowedPathWildcards { get; set; } = new();
    public Dictionary<string, LocalFileState> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class LocalFileState
{
    public string ContentHash { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LocalStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _statePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LocalStateStore(IOptions<SyncClientOptions> options)
    {
        var stateDirectory = Path.Combine(AppContext.BaseDirectory, "state");
        Directory.CreateDirectory(stateDirectory);
        _statePath = Path.Combine(stateDirectory, ".sync-client-state.json");
    }

    public async Task<LocalSyncState> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_statePath))
            {
                return new LocalSyncState();
            }

            await using var stream = File.OpenRead(_statePath);
            var state = await JsonSerializer.DeserializeAsync<LocalSyncState>(stream, JsonOptions, cancellationToken)
                ?? new LocalSyncState();
            state.AllowedPathWildcards ??= new List<string>();
            state.Files ??= new Dictionary<string, LocalFileState>(StringComparer.OrdinalIgnoreCase);
            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tmpPath = _statePath + ".tmp";
            await using (var stream = File.Create(tmpPath))
            {
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
            }

            File.Move(tmpPath, _statePath, true);
        }
        finally
        {
            _gate.Release();
        }
    }
}
