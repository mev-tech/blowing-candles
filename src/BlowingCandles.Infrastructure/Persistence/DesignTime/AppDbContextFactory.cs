using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BlowingCandles.Infrastructure.Persistence.DesignTime;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var startupProjectPath = FindStartupProjectPath();
        var baseSettingsPath = Path.Combine(startupProjectPath, "appsettings.json");
        var developmentSettingsPath = Path.Combine(startupProjectPath, "appsettings.Development.json");

        var connectionString = ReadConnectionString(baseSettingsPath);
        var developmentConnectionString = ReadConnectionString(developmentSettingsPath);
        connectionString = string.IsNullOrWhiteSpace(developmentConnectionString)
            ? connectionString
            : developmentConnectionString;

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'ConnectionStrings:AppDb' is missing.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        });

        return new AppDbContext(optionsBuilder.Options);
    }

    private static string FindStartupProjectPath()
    {
        var currentDirectory = Directory.GetCurrentDirectory();

        while (currentDirectory is not null)
        {
            var candidate = Path.Combine(currentDirectory, "src", "BlowingCandles.Cli");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
        }

        return Directory.GetCurrentDirectory();
    }

    private static string? ReadConnectionString(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("ConnectionStrings", out var connectionStrings))
        {
            return null;
        }

        if (!connectionStrings.TryGetProperty("AppDb", out var appDb))
        {
            return null;
        }

        return appDb.ValueKind == JsonValueKind.String ? appDb.GetString() : null;
    }
}
