using StatusTide.Data.Vendors;

namespace StatusTide.Api.Features.Dashboard;

/// <summary>The dashboard screen: the vendors the user follows at least one component of.</summary>
public record DashboardDto(IReadOnlyList<VendorCardDto> Vendors);

/// <summary>
/// One vendor card, as the user sees it. The same shape is pushed over the hub when the vendor
/// changes, so a live update replaces the card in place.
/// </summary>
/// <param name="Status">The worst status among the components this user follows.</param>
/// <param name="ComponentCount">All the vendor's components, for "3 of 10 components".</param>
/// <param name="LastCheckedAt">The last successful poll, or null when we have none on record.</param>
/// <param name="IsDemo">True for Acme Cloud, which offers "Simulate an outage".</param>
/// <param name="ActiveIncidentCount">
/// Open incidents on the components this user follows, plus open incidents that name no components.
/// </param>
/// <param name="ActiveIncident">The most recent of those incidents, or null when there is none.</param>
public record VendorCardDto(
    Guid Id,
    string Slug,
    string Name,
    ComponentStatus Status,
    IReadOnlyList<FollowedComponentDto> FollowedComponents,
    int ComponentCount,
    DateTimeOffset? LastCheckedAt,
    bool IsDemo,
    int ActiveIncidentCount,
    ActiveIncidentDto? ActiveIncident);

public record FollowedComponentDto(Guid Id, string Name, ComponentStatus Status);

/// <param name="Title">The vendor's own title. Untrusted: render it as text only.</param>
public record ActiveIncidentDto(Guid Id, string Title, DateTimeOffset StartedAt);
