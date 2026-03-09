using BlowingCandles.Infrastructure.Persistence.HealthChecks;
using BlowingCandles.Infrastructure.Persistence.Options;
using BlowingCandles.Infrastructure.Persistence.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BlowingCandles.Infrastructure.Persistence;

public static class DependencyInjection
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<PersistenceOptions>(
            configuration.GetSection(PersistenceOptions.SectionName));

        var connectionString = configuration.GetConnectionString("AppDb")
            ?? throw new InvalidOperationException(
                "Connection string 'AppDb' is missing from configuration.");

        services.AddDbContext<AppDbContext>((serviceProvider, options) =>
        {
            var persistenceOptions = serviceProvider
                .GetRequiredService<IOptions<PersistenceOptions>>()
                .Value;

            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npgsql.CommandTimeout(persistenceOptions.CommandTimeoutSeconds);
            });

            if (persistenceOptions.EnableDetailedErrors)
            {
                options.EnableDetailedErrors();
            }

            if (persistenceOptions.EnableSensitiveDataLogging)
            {
                options.EnableSensitiveDataLogging();
            }
        });

        services.AddHealthChecks()
            .AddCheck<PostgreSqlHealthCheck>(
                "postgresql",
                tags: ["ready", "db"]);

        services.AddScoped<SignalRunPersistenceService>();
        services.AddScoped<SignalRunReadService>();
        services.AddScoped<TradeGovernorDbStateStoreFactory>();

        return services;
    }
}
