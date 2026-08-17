using System.Text;
using Amazon.S3;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using SmartDoc.Api.Features.Auth;
using SmartDoc.Api.Features.Chat;
using SmartDoc.Api.Features.Documents;
using SmartDoc.Api.Features.Search;
using SmartDoc.Infrastructure;
using SmartDoc.Infrastructure.Persistence;
using SmartDoc.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<CreateDocumentRequestValidator>();
builder.Services.AddOpenApi();
builder.Services.AddSingleton<JwtTokenGenerator>();

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
