using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

using StatusTide.Api.Features.RequestLogging;

namespace StatusTide.UnitTests.RequestLogging;

public class ClientAbortMiddlewareTests
{
    private static HttpContext Context(bool clientGone)
    {
        return new DefaultHttpContext { RequestAborted = new CancellationToken(canceled: clientGone) };
    }

    private static Task Run(HttpContext context, Exception thrown)
    {
        return new ClientAbortMiddleware(_ => throw thrown).InvokeAsync(context);
    }

    [Fact]
    public async Task A_client_that_hung_up_is_a_499_and_not_an_exception()
    {
        var context = Context(clientGone: true);

        await Run(context, new OperationCanceledException());

        Assert.Equal(ClientAbortMiddleware.ClientClosedRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_canceled_task_counts_too()
    {
        var context = Context(clientGone: true);

        await Run(context, new TaskCanceledException());

        Assert.Equal(ClientAbortMiddleware.ClientClosedRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_timeout_while_the_client_is_still_waiting_stays_an_error()
    {
        var context = Context(clientGone: false);

        await Assert.ThrowsAsync<OperationCanceledException>(() => Run(context, new OperationCanceledException()));
    }

    [Fact]
    public async Task A_real_failure_stays_an_error_even_if_the_client_left()
    {
        var context = Context(clientGone: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(context, new InvalidOperationException()));
    }

    [Fact]
    public async Task A_response_already_on_its_way_keeps_its_status()
    {
        var context = Context(clientGone: true);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Features.Set<IHttpResponseFeature>(new StartedResponse());

        await Run(context, new OperationCanceledException());

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task A_normal_request_passes_through()
    {
        var context = Context(clientGone: false);
        var middleware = new ClientAbortMiddleware(c =>
        {
            c.Response.StatusCode = StatusCodes.Status201Created;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
    }

    private sealed class StartedResponse : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }
}
