using StatusTide.Data.Users;
using StatusTide.Data.Vendors;

namespace StatusTide.Data.Follows;

/// <summary>
/// A user follows one component and picks how to be alerted about it.
/// Guests only get in-app alerts; email and webhooks need a confirmed account.
/// </summary>
public class Follow
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }
    public Guid ComponentId { get; set; }

    public bool AlertInApp { get; set; } = true;
    public bool AlertEmail { get; set; }
    public bool AlertWebhook { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public User? User { get; set; }
    public Component? Component { get; set; }
}
