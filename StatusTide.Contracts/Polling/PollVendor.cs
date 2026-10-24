namespace StatusTide.Contracts.Polling;

/// <summary>
/// Command sent by the Publisher: poll one vendor's status feed once. Exactly one is
/// queued per vendor per interval, however many users follow that vendor.
/// </summary>
public record PollVendor(Guid VendorId, DateTimeOffset ScheduledAt);
