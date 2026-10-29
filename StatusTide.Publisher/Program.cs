using StatusTide.Common.Logging;
using StatusTide.Common.Messaging;
using StatusTide.Common.Redis;
using StatusTide.Data;
using StatusTide.Publisher.Features.Guests;
using StatusTide.Publisher.Features.Polling;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);
builder.Services.AddStatusTideData(builder.Configuration);
builder.Services.AddStatusTideRedis(builder.Configuration);
builder.Services.AddStatusTideMessaging(builder.Configuration);

// Run a single replica. The Redis scheduler lock also stops a second one from queuing
// polls twice, for example during a rollout.
builder.AddPollScheduler();
builder.AddGuestCleanup();

var host = builder.Build();
host.Run();
