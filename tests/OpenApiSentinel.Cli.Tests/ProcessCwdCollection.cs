namespace OpenApiSentinel.Cli.Tests;

/// <summary>
/// Tests that temporarily change the PROCESS-WIDE Environment.CurrentDirectory must run
/// sequentially — xUnit runs classes in parallel otherwise, and one class's cleanup can
/// delete another's working directory mid-run.
/// </summary>
[CollectionDefinition("process-cwd")]
public sealed class ProcessCwdTests { }
