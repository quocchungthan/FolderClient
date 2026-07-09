using System.Security.Cryptography;
using Microsoft.Extensions.Options;

public sealed class ScannedFile
{
    public string Path { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class LocalFileScanner
{
    private readonly SyncClientOptions _options;

    public LocalFileScanner(IOptions<SyncClientOptions> options)
    {
        _options = options.Value;
    }

    public Task<Dictionary<string, ScannedFile>> ScanAsync(CancellationToken cancellationToken)
    {
        var root = _options.LocalFolderPath;
        var map = new Dictionary<string, ScannedFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileName(file);
            if (string.Equals(fileName, ".sync-client-state.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var virtualPath = "/" + relative.TrimStart('/');
            var info = new FileInfo(file);
            map[virtualPath] = new ScannedFile
            {
                Path = virtualPath,
                ContentHash = ComputeHash(file),
                Size = info.Length,
                UpdatedAtUtc = info.LastWriteTimeUtc
            };
        }

        return Task.FromResult(map);
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
