using System.Diagnostics;
using System.Text;
using OpenApiSentinel.Bruno.Model;
using OpenApiSentinel.Bruno.Reports;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>
/// Adversarial inputs and scale: hostile metadata must never break the YAML/HTML
/// structure or leak into reports as executable markup, and hundreds of operations
/// must sync well under interactive latency.
/// </summary>
public class BrunoSafetyTests
{
    private static (BrunoCollectionDocument Collection, BrunoSyncOutcome Outcome) Run(
        string spec,
        BrunoExampleSettings? exampleSettings = null)
    {
        var doc = TestSupport.NormalizeSpec(spec);
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(),
            exampleSettings ?? new BrunoExampleSettings { Enabled = false },
            "test");
        return engine.Sync(null, doc);
    }

    [Fact]
    public void Hostile_Metadata_Stays_Structurally_Safe()
    {
        var spec = "openapi: 3.0.3\n" +
                   "info:\n" +
                   "  title: \"T: {injected}\\nitems: [boom]\"\n" +
                   "  version: \"1\"\n" +
                   "paths:\n" +
                   "  \"/x'\" :\n" +
                   "    get:\n" +
                   "      operationId: \"opid:x\\nscript: alert(1)\"\n" +
                   "      summary: \"<img src=x onerror=alert(1)>\"\n" +
                   "      tags:\n" +
                   "        - \"../../etc/passwd\"\n" +
                   "      responses:\n" +
                   "        '200': { description: OK }\n";

        // Must not throw; emitted YAML must re-parse cleanly.
        var (collection, outcome) = Run(spec);
        Assert.Single(outcome.Operations);

        var reparsed = BrunoCollectionDocument.Parse(collection.ToYaml(), "hostile.yml");

        // traversal-style tag segments must never survive as path-like folder labels
        Assert.DoesNotContain(reparsed.AllRequests(), i => i.IsFolder && (i.Name == ".." || i.Name == "."));
        Assert.Contains(reparsed.AllRequests(), i => i.IsHttpRequest);

        var request = reparsed.AllRequests().Single(i => i.IsHttpRequest);
        Assert.NotNull(request.IdentityOf());
        // The hostile operationId is preserved verbatim INSIDE a scalar value,
        // never interpreted as YAML structure.
        Assert.Contains("script: alert(1)", request.IdentityOf()!.OperationId, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_Report_Escapes_Spec_Derived_Payloads()
    {
        const string spec = """
            openapi: 3.0.3
            info:
              title: t
              version: "1"
            paths:
              /s:
                get:
                  operationId: s
                  summary: "<script>alert('xss')</script>"
                  tags:
                    - "<b>bold</b>"
                  responses:
                    '200': { description: OK }
            """;

        var (collection, outcome) = Run(spec);
        var report = BrunoWorkflowBridge.Build(outcome);

        var html = BrunoSyncHtmlReporter.Render(report);
        Assert.DoesNotContain("<script>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);

        var json = BrunoSyncJsonReporter.Render(report);
        Assert.Contains("<script>", json, StringComparison.Ordinal); // JSON keeps raw text for machines
    }

    [Fact]
    public void Cyclic_Schema_Does_Not_Hang_The_Generator()
    {
        const string spec = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            paths:
              /node:
                post:
                  operationId: createNode
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/Node'
                  responses:
                    '200': { description: OK }
            components:
              schemas:
                Node:
                  type: object
                  required: [name]
                  properties:
                    name: { type: string }
                    child: { $ref: '#/components/schemas/Node' }
            """;

        var sw = Stopwatch.StartNew();
        var (_, outcome) = Run(spec, exampleSettings: new BrunoExampleSettings());
        sw.Stop();

        Assert.Equal(OperationAction.Added, Enumerable.Single(outcome.Operations).Action);
        Assert.True(sw.ElapsedMilliseconds < 10_000, $"generator took {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void Deeply_Nested_Body_Is_Bounded()
    {
        const int depth = 30;
        var spec = new StringBuilder();
        spec.Append("openapi: 3.0.3\ninfo: { title: t, version: \"1\" }\n");
        spec.Append("paths:\n  /deep:\n    post:\n      operationId: deep\n");
        spec.Append("      requestBody:\n        required: true\n        content:\n");
        spec.Append("          application/json:\n            schema:\n");

        for (var i = 0; i <= depth; i++)
        {
            var pad = new string(' ', 14 + (i * 2));
            spec.Append(pad).Append("type: object\n");
            spec.Append(pad).Append("required: [l").Append(i).Append("]\n");
            spec.Append(pad).Append("properties:\n");
            spec.Append(pad).Append("  l").Append(i).Append(":\n");
        }

        spec.Append(new string(' ', 14 + ((depth + 1) * 2))).Append("type: string\n");
        spec.Append("      responses:\n        '200': { description: OK }\n");
        var specText = spec.ToString();

        var (_, outcome) = Run(specText, exampleSettings: new BrunoExampleSettings());
        Assert.Single(outcome.Operations);
    }

    [Fact]
    public void Weird_Tag_Names_Create_Safe_Folder_Labels()
    {
        const string spec = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            paths:
              /a:
                get:
                  operationId: a
                  tags: ["CON", "NUL/test", ".."]
                  responses:
                    '200': { description: OK }
              /b:
                post:
                  operationId: b
                  tags: ["con"]   # case variant of Windows reserved name
                  responses:
                    '200': { description: OK }
            """;

        // Folder names live inside YAML values (no filesystem mapping), so even Windows
        // reserved device names are inert here; the run must succeed cleanly.
        var (collection, outcome) = Run(spec);
        Assert.Equal(2, outcome.AddedCount);
        _ = BrunoCollectionDocument.Parse(collection.ToYaml(), "weird.yml"); // still valid YAML
    }
}

/// <summary>Bridges to the CLI-side report builder without a project reference cycle.</summary>
internal static class BrunoWorkflowBridge
{
    internal static BrunoSyncReport Build(BrunoSyncOutcome outcome)
    {
        var operations = outcome.Operations.Select(o => new BrunoSyncReportOperation
        {
            Action = o.Action switch
            {
                OperationAction.Added => "added",
                OperationAction.Updated => "updated",
                OperationAction.Unchanged => "unchanged",
                OperationAction.Stale => "stale",
                OperationAction.Removed => "removed",
                _ => o.Action.ToString().ToLowerInvariant(),
            },
            Method = o.Method,
            Path = o.Path,
            OperationId = o.OperationId,
            RequestName = o.RequestName,
            Changes = [.. o.Changes],
            Preserved = [.. o.Preserved],
            Warnings = [.. o.Warnings],
        }).ToList();

        return new BrunoSyncReport
        {
            ToolVersion = "test",
            GeneratedAtUtc = "2026-01-01T00:00:00Z",
            Source = "spec.yaml",
            CollectionPath = "collection.yml",
            Summary = new BrunoSyncReportSummary(),
            Operations = operations,
            Conflicts = [],
            Warnings = [],
        };
    }
}

public class BrunoPerformanceTests
{
    [Fact]
    public void Five_Hundred_Operations_Sync_Under_A_Few_Seconds_And_Skip_Fast()
    {
        var sb = new StringBuilder();
        sb.Append("openapi: 3.0.3\ninfo: { title: big, version: \"1\" }\npaths:\n");

        for (var i = 0; i < 500; i++)
        {
            sb.Append($@"
              /resource{i}:
                post:
                  operationId: createResource{i}
                  tags: [群組{i % 20}]
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          type: object
                          required: [id]
                          properties:
                            id: {{ type: integer, minimum: 1 }}
                            flag: {{ type: boolean }}
              ");
            sb.Append($@"
                get:
                  operationId: getResource{i}
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema: {{ type: integer }}
                  responses:
                    '200': {{ description: OK }}
              ");
        }

        var doc = TestSupport.NormalizeSpec(sb.ToString());
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings(),
            new BrunoExampleSettings { Enabled = true, MaxPerOperation = 4 },
            "perf");

        var sw = Stopwatch.StartNew();
        var (collection, first) = engine.Sync(null, doc);
        sw.Stop();
        Assert.Equal(1000, first.AddedCount);
        Assert.True(sw.ElapsedMilliseconds < 15_000, $"full sync took {sw.ElapsedMilliseconds} ms");

        var swSkip = Stopwatch.StartNew();
        var (_, second) = engine.Sync(collection, doc);
        swSkip.Stop();

        Assert.Equal(1000, second.UnchangedCount);
        Assert.True(swSkip.ElapsedMilliseconds < 5_000, $"unchanged resync took {swSkip.ElapsedMilliseconds} ms");

        // serialization of the whole tree stays deterministic
        Assert.Equal(collection.ToYaml(), collection.ToYaml());
    }
}

public class BrunoPathRobustnessTests
{
    [Fact]
    public void Relative_Paths_Resolve_Against_Base_Directory()
    {
        var resolved = OpenApiSentinel.Bruno.BrunoPaths.Resolve("./api-contract/c.yml", "/base/config-dir");
        Assert.StartsWith("/base/config-dir", resolved, StringComparison.Ordinal);
        Assert.EndsWith("c.yml", resolved, StringComparison.Ordinal);
    }

    [Fact]
    public void Absolute_Paths_Pass_Through_Unchanged()
    {
        var absolute = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "x", "collection.yml"));
        Assert.Equal(absolute, OpenApiSentinel.Bruno.BrunoPaths.Resolve(absolute, "/somewhere"));
    }

    [Fact]
    public void Traversal_Normalizes_But_Never_Escapes_Filesystem_Root()
    {
        var resolved = OpenApiSentinel.Bruno.BrunoPaths.Resolve("../../outside.yml", "/base");
        Assert.False(resolved.Contains("..", StringComparison.Ordinal)); // GetFullPath collapses segments deterministically
    }

    [Fact]
    public void Collection_Target_Extension_Is_Checked_Regardless_Of_Separator_Style()
    {
        Assert.Throws<OpenApiSentinel.Bruno.BrunoSyncException>(() =>
            OpenApiSentinel.Bruno.BrunoPaths.ValidateCollectionTarget(
                Path.Combine(Path.GetTempPath(), "collection.json")));

        // .YAML uppercase must be accepted by extension check (existence checked elsewhere)
        OpenApiSentinel.Bruno.BrunoPaths.ValidateCollectionTarget(
            Path.Combine(Path.GetTempPath(), "COLLECTION.YAML"));
    }
}
