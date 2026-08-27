namespace OpenApiSentinel.Bruno.Reports;

/// <summary>Single source of truth for whether a sync outcome deserves a historical report.
/// Detail rows describe only operations whose synchronization state changed; aggregate counts
/// intentionally remain available on <see cref="BrunoSyncOutcome"/> for the report summary.</summary>
public static class BrunoSyncReportPolicy
{
    public static bool IsDetailOperation(OperationSyncOutcome operation) =>
        operation.Action != OperationAction.Unchanged;

    public static IReadOnlyList<OperationSyncOutcome> DetailOperations(BrunoSyncOutcome outcome) =>
        [.. outcome.Operations.Where(IsDetailOperation)];

    /// <summary>
    /// A meaningful result is an added, updated, stale, or removed operation, or an existing
    /// reportable conflict. Warnings remain in a report that is otherwise meaningful, but do
    /// not by themselves turn internal metadata/state maintenance into a reportable change.
    /// </summary>
    public static bool HasMeaningfulChanges(BrunoSyncOutcome outcome) =>
        DetailOperations(outcome).Count > 0 || outcome.Conflicts.Count > 0;
}
