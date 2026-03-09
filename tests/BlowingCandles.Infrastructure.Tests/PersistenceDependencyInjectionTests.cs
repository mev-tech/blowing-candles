using BlowingCandles.Infrastructure.Persistence;
using BlowingCandles.Infrastructure.Persistence.HealthChecks;
using BlowingCandles.Infrastructure.Persistence.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace BlowingCandles.Infrastructure.Tests;

public sealed class PersistenceDependencyInjectionTests
{
    [Fact]
    public void PersistenceOptions_DefaultValuesMatchSpecification()
    {
        var options = new PersistenceOptions();

        Assert.Equal(30, options.CommandTimeoutSeconds);
        Assert.False(options.EnableDetailedErrors);
        Assert.False(options.EnableSensitiveDataLogging);
    }

    [Fact]
    public void AddPersistence_MissingConnectionString_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddPersistence(configuration));

        Assert.Contains("AppDb", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPersistence_ValidConnectionString_ResolvesAppDbContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:AppDb"] =
                        "Host=localhost;Port=5432;Database=blowing_candles;Username=postgres;Password=postgres",
                    ["Persistence:CommandTimeoutSeconds"] = "45",
                    ["Persistence:EnableDetailedErrors"] = "true",
                    ["Persistence:EnableSensitiveDataLogging"] = "true"
                })
            .Build();

        var services = new ServiceCollection();
        services.AddPersistence(configuration);

        using var serviceProvider = services.BuildServiceProvider();
        using var scope = serviceProvider.CreateScope();

        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<PersistenceOptions>>().Value;

        Assert.NotNull(dbContext);
        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", dbContext.Database.ProviderName);
        Assert.Equal(45, options.CommandTimeoutSeconds);
        Assert.True(options.EnableDetailedErrors);
        Assert.True(options.EnableSensitiveDataLogging);
    }

    [Fact]
    public async Task PostgreSqlHealthCheck_UnreachableDatabase_ReturnsUnhealthy()
    {
        var dbContextOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                "Host=127.0.0.1;Port=1;Database=healthcheck;Username=postgres;Password=postgres;Timeout=1;Command Timeout=1")
            .Options;

        await using var dbContext = new AppDbContext(dbContextOptions);
        var healthCheck = new PostgreSqlHealthCheck(dbContext);

        var result = await healthCheck.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.NotNull(result.Exception);
    }
}
