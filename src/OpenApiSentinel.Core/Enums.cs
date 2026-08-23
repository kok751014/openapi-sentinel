namespace OpenApiSentinel.Core;

/// <summary>Severity of a single compatibility finding.</summary>
public enum Severity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
    /// <summary>The tool detected a potentially important change but cannot confidently classify its impact.</summary>
    Verify = 5,
}

/// <summary>Directional compatibility classification of a finding. Severity and classification are independent concepts.</summary>
public enum Compatibility
{
    /// <summary>Guaranteed to break at least one class of existing consumers.</summary>
    Breaking,
    /// <summary>Likely to break some consumers depending on their usage.</summary>
    PotentiallyBreaking,
    /// <summary>Safe for existing consumers.</summary>
    NonBreaking,
    /// <summary>Documentation/metadata level information.</summary>
    Informational,
    /// <summary>Impact could not be determined automatically.</summary>
    Unknown,
}

/// <summary>The kind of structural change detected.</summary>
public enum ChangeType
{
    Added,
    Removed,
    Changed,
    Renamed,
}

/// <summary>High-level category a finding belongs to.</summary>
public enum FindingCategory
{
    Endpoint,
    Parameter,
    RequestBody,
    Response,
    Schema,
    Security,
    Metadata,
}

/// <summary>Confidence the tool has that the reported impact classification is correct.</summary>
public enum Confidence
{
    Certain,
    Likely,
    Uncertain,
}
