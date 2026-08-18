using Serilog;
using SmartDoc.Infrastructure;
using SmartDoc.Worker;

var builder = Host.CreateApplicationBuilder(args);

// ADR 0020: same Serilog wiring as SmartDoc.Api, so worker logs (job attempts, retries,
// permanent failures — see ProcessingJobProcessor) get the same structured console output
// instead of the Generic Host's default plain-text logger.
builder.Services.AddSerilog((services, configuration) => configuration
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ProcessingJobPollingWorker>();

var host = builder.Build();
host.Run();
