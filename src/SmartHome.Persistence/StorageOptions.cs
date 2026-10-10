using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace SmartHome.Persistence;

public sealed class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>
    /// The SQLite database, relative to the working directory. On the server it lives outside the deployed files
    /// (<c>/var/home-server-data</c>): a deployment must not be able to overwrite it.
    /// </summary>
    public string DatabasePath { get; init; } = "";

    /// <summary> Daily copies of the database; <c>backup</c> next to the database when empty. </summary>
    public string? BackupDirectory { get; init; }

    /// <summary> How many daily copies are kept. </summary>
    public int BackupCount { get; init; } = 7;
}

internal sealed class StorageOptionsValidator : IValidateOptions<StorageOptions>
{
    public ValidateOptionsResult Validate(string? name, StorageOptions options)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DatabasePath))
            errors.Add($"{StorageOptions.Section}:{nameof(StorageOptions.DatabasePath)} is required: the state of the house and scheduled actions live there");

        if (options.BackupCount < 1)
            errors.Add($"{StorageOptions.Section}:{nameof(StorageOptions.BackupCount)} must be at least 1");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}

/// <summary> Full paths of the database files. </summary>
internal sealed class DatabaseLocation(IOptions<StorageOptions> options, IHostEnvironment environment)
{
    public string DatabaseFile { get; } = Path.GetFullPath(options.Value.DatabasePath, environment.ContentRootPath);

    public string BackupDirectory => string.IsNullOrWhiteSpace(options.Value.BackupDirectory)
        ? Path.Combine(Path.GetDirectoryName(DatabaseFile)!, "backup")
        : Path.GetFullPath(options.Value.BackupDirectory, environment.ContentRootPath);

    /// <summary> No pooling: the database is touched a few times a minute, and a pooled connection keeps the file open. </summary>
    public string ConnectionString => ConnectionStringOf(DatabaseFile);

    public static string ConnectionStringOf(string file) => $"Data Source={file};Pooling=False";
}
