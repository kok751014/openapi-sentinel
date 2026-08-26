namespace OpenApiSentinel.Bruno;

/// <summary>Typed view of the <c>bruno:</c> configuration section.</summary>
public sealed record BrunoConfiguration
{
    public bool Enabled { get; init; } = true;

    /// <summary>OpenAPI/Swagger source (file path, URL). Null = fall back to CLI argument.</summary>
    public string? Source { get; init; }

    /// <summary>Bruno collection target: an OpenCollection YAML file path.</summary>
    public string? CollectionPath { get; init; }

    /// <summary>
    /// Directory of the folder-based OpenCollection workspace Bruno Desktop opens directly
    /// (opencollection.yml + one YAML file per request/folder). Null = default
    /// ./api-contract/bruno-workspace next to the config. Generated from the SAME canonical
    /// model as the bundled collection so the two never drift.
    /// </summary>
    public string? WorkspacePath { get; init; }

    /// <summary>Directory for bruno-sync-report.html / .json.</summary>
    public string? ReportPath { get; init; }

    /// <summary>Only "auto" (OpenCollection YAML) is currently supported; validated by the loader.</summary>
    public string Format { get; init; } = "auto";

    public BrunoSyncSettings Sync { get; init; } = new();

    public BrunoExampleSettings Examples { get; init; } = new();
}

/// <summary><c>bruno.sync:</c> preservation and deletion policy.</summary>
public sealed record BrunoSyncSettings
{
    public bool PreserveManualValues { get; init; } = true;

    public bool PreserveScripts { get; init; } = true;

    public bool PreserveTests { get; init; } = true;

    public bool PreserveDocs { get; init; } = true;

    public bool RemoveDeletedOperations { get; init; }

    /// <summary>Explicit incremental-state file location; null = next to the collection file.</summary>
    public string? StatePath { get; init; }
}

/// <summary><c>bruno.examples:</c> Layer-1/2 scenario generation policy.</summary>
public sealed record BrunoExampleSettings
{
    public bool Enabled { get; init; } = true;

    public string Language { get; init; } = ScenarioTexts.LanguageTag;

    /// <summary>Authentication scenarios are never auto-generated; the flag documents intent
    /// and reserves the switch. See docs/bruno-scenarios.md.</summary>
    public bool IgnoreAuthentication { get; init; } = true;

    public int MaxPerOperation { get; init; } = 8;

    /// <summary>Optional business-scenario file (Layer 2).</summary>
    public string? ScenariosPath { get; init; }
}
