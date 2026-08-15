using SmartDoc.Infrastructure;
using SmartDoc.Worker;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddHostedService<ProcessingJobPollingWorker>();

var host = builder.Build();
host.Run();
