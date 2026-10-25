namespace StatusTide.Data.Vendors;

/// <summary>
/// One run of the vendor verification process. Vendors are re-verified every six months,
/// so a vendor collects several rows over time and the latest one counts.
/// </summary>
public class VendorVerification
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public Guid VendorId { get; set; }

    public VerificationDecision Decision { get; set; }

    /// <summary>The endpoint fetched by hand during the review.</summary>
    public required string EndpointUrl { get; set; }

    public FeedFormat Format { get; set; }

    /// <summary>Whether the endpoint sends ETag or Last-Modified, so polls can be conditional.</summary>
    public bool SupportsConditionalRequests { get; set; }

    /// <summary>Terms of service, acceptable use and API policy pages that were read.</summary>
    public string[] TermsUrls { get; set; } = [];

    /// <summary>The relevant section name. Never the copied clause text.</summary>
    public string? Clause { get; set; }

    /// <summary>One-line reason for the decision.</summary>
    public required string Reason { get; set; }

    public string? ReviewerNotes { get; set; }
    public DateOnly ReviewedOn { get; set; }

    public Vendor? Vendor { get; set; }
}
