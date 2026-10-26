using MassTransit;

using SerilogLogContext = Serilog.Context.LogContext;

namespace StatusTide.Common.Messaging;

/// <summary>
/// Adds the message's CorrelationId to every log line written while a consumer runs.
/// The Publisher sets one per command, so a poll can be followed from the scheduler
/// to the worker that handled it.
/// </summary>
public class CorrelationLogFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    public const string PropertyName = "CorrelationId";

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        using (SerilogLogContext.PushProperty(PropertyName, context.CorrelationId ?? context.MessageId))
        {
            await next.Send(context);
        }
    }

    public void Probe(ProbeContext context) => context.CreateFilterScope("correlationLog");
}
