using Serilog;
using StackExchange.Redis;
using StatusTide.Api.Features.Accounts;
using StatusTide.Api.Features.Catalog;
using StatusTide.Api.Features.Dashboard;
using StatusTide.Api.Features.Demo;
using StatusTide.Api.Features.Follows;
using StatusTide.Api.Features.Guests;
using StatusTide.Api.Features.Incidents;
using StatusTide.Api.Features.RateLimiting;
using StatusTide.Api.Features.RequestLogging;
using StatusTide.Api.Features.Users;
using StatusTide.Common.Logging;
using StatusTide.Common.Messaging;
using StatusTide.Common.Redis;
using StatusTide.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);
builder.Services.AddStatusTideData(builder.Configuration);
builder.Services.AddStatusTideRedis(builder.Configuration);
builder.Services.AddTrustedProxies(builder.Configuration);
builder.Services.AddStatusTideRateLimiting(builder.Configuration);

builder.Services.AddStatusTideMessaging(
    builder.Configuration,
    bus => bus.AddConsumer<DashboardPushConsumer>());
builder.Services.AddDemo();
builder.Services.AddDashboard();

builder.Services.AddGuests();
builder.Services.AddAccounts(builder.Configuration);
builder.Services.AddScoped<MeQuery>();
builder.Services.AddScoped<CatalogQuery>();
builder.Services.AddScoped<IncidentQuery>();
builder.Services.AddFollows();
builder.Services.AddControllers().AddJsonOptions(json => DashboardExtensions.ConfigureJson(json.JsonSerializerOptions));
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Connect to Redis now, not on the first request. Every request passes the rate limiter, so
// otherwise a burst right after startup piles up behind the blocking connect and starves the
// thread pool (seen in the k6 test as 15 s waits).
app.Services.GetRequiredService<IConnectionMultiplexer>();

// Only the API migrates, so the Publisher and Subscriber never race it. On in development
// and the local stacks; production will run migrations as a separate deploy step.
if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateStatusTideDatabaseAsync();
}

// First, so logs and rate limits see the client address instead of the proxy's.
app.UseForwardedHeaders();
app.UseSerilogRequestLogging();
app.UseMiddleware<ClientAbortMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapControllers();
app.MapHub<StatusHub>(StatusHub.Path);

// Liveness only for now. Dependency checks (PostgreSQL, Redis, RabbitMQ) join a
// readiness endpoint once the app actually uses those services.
app.MapHealthChecks("/health");

app.Run();
