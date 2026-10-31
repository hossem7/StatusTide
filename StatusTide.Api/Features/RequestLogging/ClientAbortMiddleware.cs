namespace StatusTide.Api.Features.RequestLogging;

/// <summary>
/// Stops a client that hung up from being logged as a server error. Closing a tab or aborting a
/// fetch cancels <see cref="HttpContext.RequestAborted"/>, and the cancellation surfaces as an
/// exception that the request logger reports as a 500 no client ever received. Must run inside
/// the request logger, so it logs the 499 instead.
/// </summary>
public class ClientAbortMiddleware(RequestDelegate next)
{
    /// <summary>"Client Closed Request". Not an official status code; nginx made it common.</summary>
    public const int ClientClosedRequest = 499;

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        // The filter keeps real timeouts as errors: those cancel while the client is still waiting.
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ClientClosedRequest;
            }
        }
    }
}
