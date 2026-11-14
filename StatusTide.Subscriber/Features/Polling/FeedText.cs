using StatusTide.Data.Vendors;

namespace StatusTide.Subscriber.Features.Polling;

/// <summary>
/// Cleanup shared by every reader. Feed values are untrusted, so text is trimmed and cut to our
/// column sizes, and links are kept only when they are plain http or https.
/// </summary>
public static class FeedText
{
    // Match the column sizes in StatusTide.Data.
    public const int MaxIdLength = 200;
    public const int MaxNameLength = 200;
    public const int MaxTitleLength = 500;
    public const int MaxUrlLength = 2048;

    /// <summary>The trimmed value cut to <paramref name="maxLength"/>, or null when it is empty.</summary>
    public static string? Clean(string? value, int maxLength)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    /// <summary>The link when it is an absolute http or https URL that fits the column, otherwise null.</summary>
    public static string? HttpUrl(string? value)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var link)
            && (link.Scheme == Uri.UriSchemeHttps || link.Scheme == Uri.UriSchemeHttp)
            && link.AbsoluteUri.Length <= MaxUrlLength)
        {
            return link.AbsoluteUri;
        }

        return null;
    }

    /// <summary>The worse of two component statuses, for components hit by more than one incident.</summary>
    public static ComponentStatus Worse(ComponentStatus a, ComponentStatus b) => Rank(a) >= Rank(b) ? a : b;

    private static int Rank(ComponentStatus status) => status switch
    {
        ComponentStatus.MajorOutage => 5,
        ComponentStatus.PartialOutage => 4,
        ComponentStatus.DegradedPerformance => 3,
        ComponentStatus.Unknown => 2,
        ComponentStatus.UnderMaintenance => 1,
        _ => 0,
    };
}
