using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SmartDoc.Infrastructure.Persistence;
using SmartDoc.Infrastructure.Processing;

namespace SmartDoc.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException(
                "Connection string 'Postgres' is not configured. Set ConnectionStrings:Postgres.");

        services.AddDbContext<SmartDocDbContext>(options =>
            options.UseNpgsql(connectionString, o => o.UseVector()));

        services.AddScoped<ProcessingJobProcessor>();

        return services;
    }
}
