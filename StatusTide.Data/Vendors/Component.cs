namespace StatusTide.Data.Vendors;

/// <summary>A part of a vendor that users can follow on its own, such as "Actions" for GitHub.</summary>
public class Component
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid VendorId { get; set; }

    /// <summary>The vendor's own id for the component, used to match it on each poll.</summary>
    public required string ExternalId { get; set; }

    public required string Name { get; set; }
    public ComponentStatus Status { get; set; }
    public DateTimeOffset StatusChangedAt { get; set; }

    public Vendor? Vendor { get; set; }
}
