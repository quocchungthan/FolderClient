using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<SyncClientOptions>()
    .Bind(builder.Configuration.GetSection(SyncClientOptions.SectionName))
    .ValidateDataAnnotations()
    .Validate(options => Uri.TryCreate(options.ServerAddress, UriKind.Absolute, out _), "Sync:ServerAddress must be a valid absolute URL.")
    .Validate(options => Directory.Exists(options.LocalFolderPath), "Sync:LocalFolderPath must exist.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.AccessKey), "Sync:AccessKey is required.")
    .ValidateOnStart();

builder.Services.AddHttpClient<SyncApiClient>();
builder.Services.AddSingleton<LocalStateStore>();
builder.Services.AddSingleton<LocalFileScanner>();
builder.Services.AddHostedService<SyncWorker>();

var app = builder.Build();
await app.RunAsync();

public sealed class SyncClientOptions
{
    public const string SectionName = "Sync";

    [Required]
    public string ServerAddress { get; set; } = string.Empty;

    [Required]
    public string LocalFolderPath { get; set; } = string.Empty;

    [Required]
    public string AccessKey { get; set; } = string.Empty;

    [Range(2, 3600)]
    public int PollSeconds { get; set; } = 10;

    [Range(1024, 4 * 1024 * 1024)]
    public int UploadChunkBytes { get; set; } = 256 * 1024;
}
