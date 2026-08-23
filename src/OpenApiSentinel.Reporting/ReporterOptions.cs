using OpenApiSentinel.Core;

namespace OpenApiSentinel.Reporting;

/// <summary>Shared knobs for all reporters. Reporters never mutate the result.</summary>
public sealed record ReporterOptions
{
    /// <summary>Disable ANSI colors in console output (also auto-off when not a terminal).</summary>
    public bool NoColor { get; init; }

    /// <summary>Include impact/suggested action/values for every finding.</summary>
    public bool Verbose { get; init; }

    /// <summary>Include the suppressed-findings section.</summary>
    public bool IncludeSuppressed { get; init; } = true;

    public static ReporterOptions Default { get; } = new();
}

/// <summary>A reporter renders a ComparisonResult into a specific output format.</summary>
public interface IResultReporter
{
    /// <summary>Stable format identifier, e.g. "json", "markdown", "html".</summary>
    string FormatName { get; }

    /// <summary>Renders the result deterministically: same input + options → identical bytes.</summary>
    string Render(ComparisonResult result, ReporterOptions options);
}
