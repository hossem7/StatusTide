using MassTransit;
using StatusTide.Common.Logging;
using StatusTide.Common.Messaging;
using StatusTide.Common.Redis;
using StatusTide.Data;
using StatusTide.Subscriber.Features.Accounts;
using StatusTide.Subscriber.Features.Polling;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddStatusTideLogging(builder.Configuration, builder.Environment);
builder.Services.AddStatusTideData(builder.Configuration);
builder.Services.AddStatusTideRedis(builder.Configuration);
builder.Services.AddPolling(builder.Configuration);
builder.Services.AddSingleton<IEmailSender, SmtpEmailSender>();

// Every IConsumer<T> in this project is registered and gets its own queue, along with its
// ConsumerDefinition when it has one. Consumers live in Features/<Feature>/ next to the code they use.
builder.Services.AddStatusTideMessaging(
    builder.Configuration,
    bus => bus.AddConsumers(typeof(Program).Assembly));

var host = builder.Build();
host.Run();
