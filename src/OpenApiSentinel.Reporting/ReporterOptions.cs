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

    /// <summary>Language used only by the HTML reporter. Other reporters retain their stable contract.</summary>
    public HtmlLanguage HtmlLanguage { get; init; } = HtmlLanguage.ZhTw;

    /// <summary>Presentation-only status of a requested snapshot advancement.</summary>
    public HtmlSnapshotStatus SnapshotStatus { get; init; } = HtmlSnapshotStatus.NotRequested;

    public string? SnapshotPath { get; init; }

    public string? SnapshotSource { get; init; }

    public static ReporterOptions Default { get; } = new();
}

public enum HtmlLanguage { ZhTw, En }

public enum HtmlSnapshotStatus { NotRequested, Pending, PolicyFailed, Updated }

/// <summary>A reporter renders a ComparisonResult into a specific output format.</summary>
public interface IResultReporter
{
    /// <summary>Stable format identifier, e.g. "json", "markdown", "html".</summary>
    string FormatName { get; }

    /// <summary>Renders the result deterministically: same input + options → identical bytes.</summary>
    string Render(ComparisonResult result, ReporterOptions options);
}
