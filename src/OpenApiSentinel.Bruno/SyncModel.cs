namespace OpenApiSentinel.Bruno;

/// <summary>Action taken for one API operation during sync.</summary>
public enum OperationAction
{
    Added,
    Updated,
    Unchanged,
    Stale,
    Removed,
}

/// <summary>An example-level change within an operation.</summary>
public sealed record ExampleChange(string Name, string Action, string? ScenarioKind = null);

/// <summary>Per-operation sync result used by reports and console output.</summary>
public sealed record OperationSyncOutcome
{
    public required OperationAction Action { get; init; }

    public required string Method { get; init; }

    public required string Path { get; init; }

    public string? OperationId { get; init; }

    public string? RequestName { get; init; }

    /// <summary>Contract-derived changes applied (zh-TW descriptions).</summary>
    public IReadOnlyList<string> Changes { get; init; } = [];

    /// <summary>User-maintained aspects preserved (zh-TW).</summary>
    public IReadOnlyList<string> Preserved { get; init; } = [];

    public IReadOnlyList<ExampleChange> Examples { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>A sync conflict that required a conservative decision.</summary>
public sealed record SyncConflict(string Kind, string Detail);

/// <summary>Aggregate outcome of one bruno sync run.</summary>
public sealed record BrunoSyncOutcome
{
    public bool Skipped { get; init; }

    public bool DryRun { get; init; }

    public bool CreatedCollection { get; init; }

    public required IReadOnlyList<OperationSyncOutcome> Operations { get; init; }

    public required IReadOnlyList<SyncConflict> Conflicts { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }

    public int AddedCount => Count(OperationAction.Added);

    public int UpdatedCount => Count(OperationAction.Updated);

    public int UnchangedCount => Count(OperationAction.Unchanged);

    public int StaleCount => Count(OperationAction.Stale);

    public int RemovedCount => Count(OperationAction.Removed);

    public int ExamplesAdded => Operations.Sum(o => o.Examples.Count(e => e.Action == "added"));

    public int ExamplesUpdated => Operations.Sum(o => o.Examples.Count(e => e.Action == "updated"));

    public int ManualPreservedCount => Operations.Count(o =>
        o.Action is OperationAction.Updated && o.Preserved.Count > 0);

    private int Count(OperationAction action) => Operations.Count(o => o.Action == action);
}
