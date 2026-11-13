using StatusTide.Data.Incidents;
using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.Incidents;

/// <summary>
/// The incident page: the vendor's facts about the incident next to what our own checks saw.
/// Never holds the vendor's written updates, only their statuses and times.
/// </summary>
/// <param name="Title">The vendor's own title. Untrusted: render it as text only.</param>
/// <param name="StartedAt">When the vendor first posted the incident.</param>
/// <param name="SourceUrl">The vendor's page with the full updates. Check its scheme before rendering it as a link.</param>
/// <param name="Components">The components the incident names, then the other ones the caller follows.</param>
/// <param name="Updates">The vendor's status changes, newest first.</param>
/// <param name="Checks">Our own checks around the incident, or null when we don't check this vendor.</param>
public record IncidentDto(
    Guid Id,
    string Title,
    IncidentStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? ResolvedAt,
    string SourceUrl,
    IncidentVendorDto Vendor,
    IReadOnlyList<IncidentComponentDto> Components,
    IReadOnlyList<VendorUpdateDto> Updates,
    IncidentChecksDto? Checks);

public record IncidentVendorDto(Guid Id, string Slug, string Name, bool IsDemo);

/// <param name="Status">The component's status now, not at the time of the incident.</param>
/// <param name="IsAffected">True when the vendor named this component in the incident.</param>
/// <param name="IsFollowed">True when the caller follows this component.</param>
public record IncidentComponentDto(Guid Id, string Name, ComponentStatus Status, bool IsAffected, bool IsFollowed);

/// <summary>One status change the vendor posted. The text of the update is never stored.</summary>
public record VendorUpdateDto(IncidentStatus Status, DateTimeOffset OccurredAt);

/// <summary>
/// Our checks of the vendor's health URL, once a minute, for the chart. Raw checks are kept
/// for 7 days, so an older incident has no points but may still have an outage.
/// </summary>
/// <param name="From">Start of the chart, a little before whichever came first: our first failure or the vendor's post.</param>
/// <param name="To">End of the chart: a little after the incident ended, or now while it is open.</param>
/// <param name="Points">Each check in the window, oldest first.</param>
/// <param name="Outage">The stretch our checks called the vendor down, or null when they never did.</param>
public record IncidentChecksDto(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<CheckPointDto> Points,
    EarlyDetectionDto? Outage);

/// <param name="StatusCode">Null when no response came back, such as a timeout.</param>
public record CheckPointDto(DateTimeOffset CheckedAt, bool Succeeded, int? StatusCode, int? ResponseTimeMs);

/// <summary>When our checks found the vendor down, compared with the vendor's first post.</summary>
/// <param name="FirstFailedAt">The first failed check of the streak.</param>
/// <param name="DetectedAt">The 3rd failure in a row, when we called the vendor down.</param>
/// <param name="RecoveredAt">The first passing check afterwards, or null while it lasts.</param>
/// <param name="VendorPostedAt">The vendor's first post about the incident.</param>
/// <param name="LeadSeconds">
/// How long before the vendor's post we called it down. Negative when the vendor posted first.
/// </param>
public record EarlyDetectionDto(
    DateTimeOffset FirstFailedAt,
    DateTimeOffset DetectedAt,
    DateTimeOffset? RecoveredAt,
    DateTimeOffset VendorPostedAt,
    int LeadSeconds);
