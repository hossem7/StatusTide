using StatusTide.Common.Logging;
using StatusTide.Common.Messaging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);
builder.Services.AddStatusTideMessaging(builder.Configuration);

// The Quartz.NET jobs that queue PollVendor (and later RunCheck) messages are
// registered here (#6). Run a single replica: only one scheduler should queue polls.

var host = builder.Build();
host.Run();
