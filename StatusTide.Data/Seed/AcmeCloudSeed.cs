using Microsoft.EntityFrameworkCore;

using StatusTide.Data.Vendors;

namespace StatusTide.Data.Seed;

/// <summary>
/// The demo vendor, Acme Cloud. We control it fully, so visitors can simulate an outage
/// without touching any real vendor. Seeded through migrations, so every database has it.
/// Ids and dates are fixed: seed data must be the same on every build or EF Core sees a
/// model change.
/// </summary>
public static class AcmeCloudSeed
{
    public static readonly Guid VendorId = new("0199f0a0-0000-7000-8000-00000000ac01");

    public static readonly Guid ApiComponentId = new("0199f0a0-0000-7000-8000-00000000ac11");
    public static readonly Guid DashboardComponentId = new("0199f0a0-0000-7000-8000-00000000ac12");
    public static readonly Guid WebhooksComponentId = new("0199f0a0-0000-7000-8000-00000000ac13");
    public static readonly Guid DatabaseComponentId = new("0199f0a0-0000-7000-8000-00000000ac14");

    private static readonly Guid VerificationId = new("0199f0a0-0000-7000-8000-00000000ac21");
    private static readonly DateTimeOffset SeededAt = new(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);

    internal static void Apply(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vendor>().HasData(new Vendor
        {
            Id = VendorId,
            Slug = "acme-cloud",
            Name = "Acme Cloud (demo)",
            Category = VendorCategory.Cloud,
            FeedUrl = "demo://acme-cloud",
            StatusPageUrl = "/vendors/acme-cloud",
            Format = FeedFormat.Demo,
            IsCatalog = true,
            IsEnabled = true,
            PollIntervalSeconds = 60,
            CreatedAt = SeededAt,
        });

        modelBuilder.Entity<Component>().HasData(
            Component(ApiComponentId, "api", "API"),
            Component(DashboardComponentId, "dashboard", "Dashboard"),
            Component(WebhooksComponentId, "webhooks", "Webhooks"),
            Component(DatabaseComponentId, "database", "Managed Database"));

        modelBuilder.Entity<VendorVerification>().HasData(new VendorVerification
        {
            Id = VerificationId,
            VendorId = VendorId,
            Decision = VerificationDecision.Approved,
            EndpointUrl = "demo://acme-cloud",
            Format = FeedFormat.Demo,
            Reason = "Internal demo vendor that StatusTide controls. No third-party terms apply.",
            ReviewedOn = DateOnly.FromDateTime(SeededAt.UtcDateTime),
        });
    }

    private static Component Component(Guid id, string externalId, string name) => new()
    {
        Id = id,
        VendorId = VendorId,
        ExternalId = externalId,
        Name = name,
        Status = ComponentStatus.Operational,
        StatusChangedAt = SeededAt,
    };
}
