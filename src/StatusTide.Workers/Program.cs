using StatusTide.Infrastructure.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);

// The scheduler, poll workers, check workers and alert worker are registered here as
// they are built (M2 onwards).

var host = builder.Build();
host.Run();
