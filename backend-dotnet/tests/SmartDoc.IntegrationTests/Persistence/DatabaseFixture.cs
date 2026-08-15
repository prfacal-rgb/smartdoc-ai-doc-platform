using Microsoft.EntityFrameworkCore;
using SmartDoc.Infrastructure.Persistence;

namespace SmartDoc.IntegrationTests.Persistence;

/// <summary>
/// Provides <see cref="SmartDocDbContext"/> instances pointed at the real Postgres
/// database used for local development (see docker-compose.yml). Requires
/// `docker compose up -d` to be running.
/// </summary>
public sealed class DatabaseFixture
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=smartdoc;Username=smartdoc;Password=smartdoc_dev_password";

    public string ConnectionString { get; } =
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres") ?? DefaultConnectionString;

    public SmartDocDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<SmartDocDbContext>()
            .UseNpgsql(ConnectionString, o => o.UseVector())
            .Options;

        return new SmartDocDbContext(options);
    }
}
