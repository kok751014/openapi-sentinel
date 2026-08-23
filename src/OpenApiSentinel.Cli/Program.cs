namespace OpenApiSentinel.Cli;

/// <summary>Thin executable shim; all behavior lives in <see cref="CommandLineRunner"/> for testability.</summary>
public static class Program
{
    public static int Main(string[] args) => CommandLineRunner.Run(args);
}
