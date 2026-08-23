namespace OpenApiSentinel.Cli;

/// <summary>Stable process exit codes documented in docs/cli-reference.md.</summary>
public static class ExitCodes
{
    /// <summary>Comparison completed and the configured policy passed.</summary>
    public const int Ok = 0;

    /// <summary>The failure threshold (--fail-on / config failOn) was violated.</summary>
    public const int PolicyViolated = 1;

    /// <summary>An input specification was missing, unreadable, or invalid.</summary>
    public const int InvalidInput = 2;

    /// <summary>The configuration file was missing values or contained errors.</summary>
    public const int InvalidConfiguration = 3;

    /// <summary>A network fetch (URL spec or remote $ref) failed or was rejected by policy.</summary>
    public const int NetworkFailure = 4;

    /// <summary>An unexpected internal error occurred (bug); rerun with --debug.</summary>
    public const int InternalError = 5;
}

/// <summary>Output abstraction so commands are unit-testable without a real console.</summary>
public interface IConsoleWriter
{
    void Out(string value = "");

    void WriteError(string value = "");
}

public sealed class ConsoleWriter : IConsoleWriter
{
    public static readonly ConsoleWriter Instance = new();

    public void Out(string value = "") => System.Console.Out.WriteLine(value);

    public void WriteError(string value = "") => System.Console.Error.WriteLine(value);
}
