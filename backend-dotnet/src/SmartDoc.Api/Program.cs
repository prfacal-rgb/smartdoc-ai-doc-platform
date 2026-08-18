using System.Text;
using Amazon.S3;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;
using Serilog.Filters;
using Serilog.Formatting.Compact;
using SmartDoc.Api;
using SmartDoc.Api.Features.Auth;
using SmartDoc.Api.Features.Chat;
using SmartDoc.Api.Features.Documents;
using SmartDoc.Api.Features.Search;
using SmartDoc.Infrastructure;
using SmartDoc.Infrastructure.Persistence;
using SmartDoc.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

// ADR 0020: Serilog.AspNetCore was already referenced but never actually wired up — the Api
// was running on the default console logger this whole time. ReadFrom.Configuration binds
// the "Serilog" section in appsettings*.json (Console + File sinks), matching how
// Rag/Worker config already works in this project instead of hardcoding config in code.
// The Distance sub-logger below is the one piece that stays in code: filtering by source
// category isn't expressible as cleanly through config, and it needs its own file
// (logs/distance-.log) separate from the general app log for later threshold calibration
// (ADR 0016) — it still also flows into the general log via the outer configuration, this
// only adds a second, isolated destination for RagDistanceLog.CategoryName events.
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Logger(distanceLogger => distanceLogger
        .Filter.ByIncludingOnly(Matching.FromSource(RagDistanceLog.CategoryName))
        .WriteTo.File(
            new CompactJsonFormatter(),
            "logs/distance-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30)));

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<CreateDocumentRequestValidator>();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<JwtTokenGenerator>();

// ADR 0020: global exception handling — see GlobalExceptionHandler for what gets mapped
// where and why.
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddProblemDetails();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, ASP.NET Core silently remaps short claim names (e.g. "sub") to
        // long legacy XML-namespace URIs (ClaimTypes.NameIdentifier) — a classic JWT-in-.NET
        // gotcha. Keeping MapInboundClaims off means the claims read back exactly as issued
        // (JwtRegisteredClaimNames.Sub, .Email), matching JwtTokenGenerator.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Single internal service, not a multi-issuer setup — no separate
            // issuer/audience to validate against.
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Exception handler first — it needs to sit outside everything else in the pipeline to catch
// exceptions thrown by any of it (request logging included).
app.UseExceptionHandler();
app.UseSerilogRequestLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "Hello World!");
app.MapAuthEndpoints();
app.MapDocumentEndpoints();
app.MapSearchEndpoints();
app.MapChatEndpoints();

var seedUserEmail = builder.Configuration["Jwt:SeedUserEmail"]
    ?? throw new InvalidOperationException("Jwt:SeedUserEmail is not configured.");

var seedUserPassword = builder.Configuration["Jwt:SeedUserPassword"]
    ?? throw new InvalidOperationException("Jwt:SeedUserPassword is not configured.");

var bucketName = builder.Configuration["Minio:BucketName"]
    ?? throw new InvalidOperationException("Minio:BucketName is not configured.");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmartDocDbContext>();
    await SmartDocDbContextSeeder.SeedAsync(db, seedUserEmail, seedUserPassword);

    var s3Client = scope.ServiceProvider.GetRequiredService<IAmazonS3>();
    await MinioBucketInitializer.EnsureBucketExistsAsync(s3Client, bucketName);
}

app.Run();

// Exposed for WebApplicationFactory<Program> in SmartDoc.IntegrationTests.
public partial class Program
{
}
