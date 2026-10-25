using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace StatusTide.Data.Alerts;

internal class AlertDeliveryConfiguration : IEntityTypeConfiguration<AlertDelivery>
{
    public void Configure(EntityTypeBuilder<AlertDelivery> delivery)
    {
        delivery.HasIndex(d => new { d.UserId, d.IncidentId, d.Channel, d.Kind }).IsUnique();
        delivery.HasIndex(d => d.IncidentId);
    }
}
