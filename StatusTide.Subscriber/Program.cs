using MassTransit;
using StatusTide.Common.Logging;
using StatusTide.Common.Messaging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);

// Every IConsumer<T> in this project is registered and gets its own queue. Consumers
// live in Features/<Feature>/ next to the code they use.
builder.Services.AddStatusTideMessaging(
    builder.Configuration,
    bus => bus.AddConsumers(typeof(Program).Assembly));

var host = builder.Build();
host.Run();
