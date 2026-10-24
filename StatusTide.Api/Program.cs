using Serilog;
using StatusTide.Infrastructure.Logging;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Liveness only for now. Dependency checks (PostgreSQL, Redis, RabbitMQ) join a
// readiness endpoint once the app actually uses those services.
app.MapHealthChecks("/health");

app.Run();
