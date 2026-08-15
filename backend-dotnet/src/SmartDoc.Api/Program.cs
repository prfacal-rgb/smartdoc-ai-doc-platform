using FluentValidation;
using Scalar.AspNetCore;
using SmartDoc.Api.Features.Documents;
using SmartDoc.Infrastructure;
using SmartDoc.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddValidatorsFromAssemblyContaining<CreateDocumentRequestValidator>();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => "Hello World!");
app.MapDocumentEndpoints();

var seedUserEmail = builder.Configuration["Jwt:SeedUserEmail"]
    ?? throw new InvalidOperationException("Jwt:SeedUserEmail is not configured.");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<SmartDocDbContext>();
    await SmartDocDbContextSeeder.SeedAsync(db, seedUserEmail);
}

app.Run();

// Exposed for WebApplicationFactory<Program> in SmartDoc.IntegrationTests.
public partial class Program
{
}
