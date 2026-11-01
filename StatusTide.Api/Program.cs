using Serilog;
using StatusTide.Api.Features.Accounts;
using StatusTide.Api.Features.Dashboard;
using StatusTide.Api.Features.Demo;
using StatusTide.Api.Features.Guests;
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

builder.Services.AddStatusTideMessaging(
    builder.Configuration,
    bus => bus.AddConsumer<DashboardPushConsumer>());
builder.Services.AddDemo();
builder.Services.AddDashboard();

builder.Services.AddGuests();
builder.Services.AddAccounts(builder.Configuration);
builder.Services.AddScoped<MeQuery>();
builder.Services.AddControllers().AddJsonOptions(json => DashboardExtensions.ConfigureJson(json.JsonSerializerOptions));
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
