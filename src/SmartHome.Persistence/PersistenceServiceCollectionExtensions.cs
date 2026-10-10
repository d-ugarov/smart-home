using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SmartHome.Core.Storage;

namespace SmartHome.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary> SQLite database of the house (<see cref="StorageOptions"/>) and its daily backup. </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services)
    {
        services.AddOptions<StorageOptions>().BindConfiguration(StorageOptions.Section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<StorageOptions>, StorageOptionsValidator>();

        services.AddSingleton<DatabaseLocation>();
        services.AddSingleton<DatabaseGate>();
        services.AddDbContextFactory<HomeDbContext>((x, options) => options.UseSqlite(x.GetRequiredService<DatabaseLocation>().ConnectionString));
        services.AddSingleton<IHomeStorage, SqliteHomeStorage>();
        services.AddHostedService<DatabaseBackupService>();

        return services;
    }
}
