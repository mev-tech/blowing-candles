using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BlowingCandles.Infrastructure.Persistence.HealthChecks;

public sealed class PostgreSqlHealthCheck : IHealthCheck
{
    private readonly AppDbContext _dbContext;

    public PostgreSqlHealthCheck(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = context;

        try
        {
            await _dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL connection is available.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "PostgreSQL connection is unavailable.",
                exception);
        }
    }
}
