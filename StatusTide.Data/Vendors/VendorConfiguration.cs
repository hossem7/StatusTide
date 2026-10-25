using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StatusTide.Data.Vendors;

internal class VendorConfiguration : IEntityTypeConfiguration<Vendor>
{
    public void Configure(EntityTypeBuilder<Vendor> vendor)
    {
        vendor.Property(v => v.Slug).HasMaxLength(100);
        vendor.Property(v => v.Name).HasMaxLength(200);
        vendor.Property(v => v.FeedUrl).HasMaxLength(2048);
        vendor.Property(v => v.StatusPageUrl).HasMaxLength(2048);
        vendor.Property(v => v.CheckUrl).HasMaxLength(2048);

        vendor.HasIndex(v => v.Slug).IsUnique();
        vendor.HasIndex(v => v.FeedUrl).IsUnique();

        // The scheduler's query: enabled vendors whose next poll is due.
        vendor.HasIndex(v => v.NextPollAt).HasFilter("is_enabled");
    }
}

internal class ComponentConfiguration : IEntityTypeConfiguration<Component>
{
    public void Configure(EntityTypeBuilder<Component> component)
    {
        component.Property(c => c.ExternalId).HasMaxLength(200);
        component.Property(c => c.Name).HasMaxLength(200);

        // Each poll matches components by the vendor's own id.
        component.HasIndex(c => new { c.VendorId, c.ExternalId }).IsUnique();
    }
}

internal class VendorVerificationConfiguration : IEntityTypeConfiguration<VendorVerification>
{
    public void Configure(EntityTypeBuilder<VendorVerification> verification)
    {
        verification.Property(v => v.EndpointUrl).HasMaxLength(2048);
        verification.Property(v => v.Clause).HasMaxLength(200);
        verification.Property(v => v.Reason).HasMaxLength(500);
        verification.Property(v => v.ReviewerNotes).HasMaxLength(2000);

        verification.HasIndex(v => new { v.VendorId, v.ReviewedOn });
    }
}
