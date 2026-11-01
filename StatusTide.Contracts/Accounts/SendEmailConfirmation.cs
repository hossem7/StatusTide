namespace StatusTide.Contracts.Accounts;

/// <summary>
/// Command sent by the API: email this account a confirmation link. The consumer creates the
/// token, so no secret ever sits in a queue or an error queue.
/// </summary>
public record SendEmailConfirmation(Guid UserId);
