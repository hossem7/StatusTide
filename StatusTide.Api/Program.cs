using Serilog;
using StatusTide.Api.Features.Demo;
using StatusTide.Common.Logging;
using StatusTide.Common.Messaging;
using StatusTide.Common.Redis;
using StatusTide.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);
builder.Services.AddStatusTideData(builder.Configuration);
builder.Services.AddStatusTideRedis(builder.Configuration);

// Publish only for now. The API gets consumers when the dashboard goes live (issue #7).
builder.Services.AddStatusTideMessaging(builder.Configuration);
builder.Services.AddDemo();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Only the API migrates, so the Publisher and Subscriber never race it. On in development
// and the local stacks; production will run migrations as a separate deploy step.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateStatusTideDatabaseAsync();
}

app.UseSerilogRequestLogging();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();

// Liveness only for now. Dependency checks (PostgreSQL, Redis, RabbitMQ) join a
// readiness endpoint once the app actually uses those services.
app.MapHealthChecks("/health");

app.Run();
