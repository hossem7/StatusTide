using System.ComponentModel.DataAnnotations;

using MassTransit;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using StatusTide.Api.Features.Guests;
using StatusTide.Api.Features.Users;
using StatusTide.Contracts.Accounts;
using StatusTide.Data.Users;

namespace StatusTide.Api.Features.Accounts;

/// <summary>Accounts: sign up (or upgrade a guest), sign in, refresh, sign out and confirm an email.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController(
    RegisterCommand register,
    SignInCommand signIn,
    ConfirmEmailCommand confirmEmail,
    RefreshSessions sessions,
    AccessTokenIssuer accessTokens,
    MeQuery me,
    IPublishEndpoint publisher,
    ILogger<AuthController> logger) : ControllerBase
{
    /// <summary>Signs up. When a guest calls this, the guest becomes the account and keeps their follows.</summary>
    [HttpPost("register")]
    [EnableRateLimiting(AccountsExtensions.RateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (AccountEmail.Normalize(request.Email) is not { } email)
        {
            return Problem("Enter a valid email address.", statusCode: StatusCodes.Status400BadRequest);
        }

        var (outcome, user) = await register.ExecuteAsync(email, request.Password, User.UserId(), cancellationToken);
        switch (outcome)
        {
            case RegisterOutcome.AlreadySignedIn:
                return Problem("You are already signed in.", statusCode: StatusCodes.Status409Conflict);
            case RegisterOutcome.EmailTaken:
                return Problem("That email already has an account.", statusCode: StatusCodes.Status409Conflict);
            case RegisterOutcome.UpgradedGuest:
                GuestCookie.Delete(Response);
                break;
        }

        await QueueConfirmationEmailAsync(user!.Id, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, await StartSessionAsync(user, cancellationToken));
    }

    [HttpPost("login")]
    [EnableRateLimiting(AccountsExtensions.RateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = AccountEmail.Normalize(request.Email) is { } email
            ? await signIn.ExecuteAsync(email, request.Password, cancellationToken)
            : null;
        return user is null
            ? Problem("Wrong email or password.", statusCode: StatusCodes.Status401Unauthorized)
            : Ok(await StartSessionAsync(user, cancellationToken));
    }

    /// <summary>Swaps the refresh cookie for a new access token, and usually a new cookie.</summary>
    [HttpPost("refresh")]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(CancellationToken cancellationToken)
    {
        var result = await sessions.RotateAsync(Request.Cookies[RefreshCookie.Name], cancellationToken);
        if (result is null || await me.GetAsync(result.UserId, cancellationToken) is not { } user)
        {
            RefreshCookie.Delete(Response);
            return Problem("Sign in again.", statusCode: StatusCodes.Status401Unauthorized);
        }

        if (result.NewToken is { } newToken)
        {
            RefreshCookie.Write(Response, newToken);
        }

        var (token, expiresAt) = accessTokens.Create(user.Id);
        return Ok(new AuthResponse(token, expiresAt, user));
    }

    /// <summary>Ends this sign-in. An access token already issued works until it expires.</summary>
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessions.EndAsync(Request.Cookies[RefreshCookie.Name], cancellationToken);
        RefreshCookie.Delete(Response);
        return NoContent();
    }

    /// <summary>Takes the token from an emailed link. Needs no sign-in, since the token proves it.</summary>
    [HttpPost("confirm-email")]
    [EnableRateLimiting(AccountsExtensions.RateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request, CancellationToken cancellationToken) =>
        await confirmEmail.ExecuteAsync(request.Token, cancellationToken)
            ? NoContent()
            : Problem("This link has expired or was already used.", statusCode: StatusCodes.Status400BadRequest);

    [HttpPost("resend-confirmation")]
    [EnableRateLimiting(AccountsExtensions.RateLimitPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ResendConfirmation(CancellationToken cancellationToken)
    {
        if (User.UserId() is not { } userId || await me.GetAsync(userId, cancellationToken) is not { IsGuest: false })
        {
            return Problem("Sign in first.", statusCode: StatusCodes.Status401Unauthorized);
        }

        await publisher.Publish(new SendEmailConfirmation(userId), cancellationToken);
        return Accepted();
    }

    private async Task<AuthResponse> StartSessionAsync(User user, CancellationToken cancellationToken)
    {
        RefreshCookie.Write(Response, await sessions.StartAsync(user.Id, cancellationToken));
        var (token, expiresAt) = accessTokens.Create(user.Id);
        return new AuthResponse(token, expiresAt, MeDto.From(user));
    }

    // The account is already saved, so a broker hiccup must not turn sign-up into an error.
    // The person can ask for the email again.
    private async Task QueueConfirmationEmailAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await publisher.Publish(new SendEmailConfirmation(userId), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Could not queue the confirmation email for user {UserId}", userId);
        }
    }
}

public record RegisterRequest(
    [Required, MaxLength(AccountEmail.MaxLength)] string Email,
    // The upper limit keeps a huge password from tying up the hasher.
    [Required, StringLength(128, MinimumLength = 10)] string Password);

public record LoginRequest(
    [Required, MaxLength(AccountEmail.MaxLength)] string Email,
    [Required, MaxLength(128)] string Password);

public record ConfirmEmailRequest([Required] string Token);

public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, MeDto User);
