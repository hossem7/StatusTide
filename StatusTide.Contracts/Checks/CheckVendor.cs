namespace StatusTide.Contracts.Checks;

/// <summary>
/// Command sent by the Publisher once a minute: call one vendor's health URL once and record
/// the result. This is our own check, separate from polling the vendor's status feed.
/// </summary>
public record CheckVendor(Guid VendorId, DateTimeOffset ScheduledAt);
