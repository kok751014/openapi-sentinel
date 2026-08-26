using OpenApiSentinel.Bruno.Model;
using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>
/// §6C folder-based Bruno workspace: generated from the SAME canonical model as the
/// bundled collection, incrementally updated without nuking user content, and stable
/// across read → write → read cycles.
/// </summary>
public class BrunoWorkspaceTests
{
    private const string Spec = """
        openapi: 3.0.3
        info:
          title: t
          version: "1"
        servers:
          - url: https://api.example.com/v1
        paths:
          /orders:
            post:
              operationId: createOrder
              summary: 建立訂單
              tags: [訂單]
              requestBody:
                required: true
                content:
                  application/json:
                    schema:
                      type: object
                      required: [id]
                      properties:
                        id: { type: integer, minimum: 1 }
              responses:
                '200': { description: OK }
          /users/{id}:
            get:
              operationId: getUser
              summary: 查詢使用者
              tags: [使用者]
              parameters:
                - name: id
                  in: path
                  required: true
                  schema: { type: integer }
              responses:
                '200': { description: OK }
        """;

    private static (BrunoCollectionDocument Collection, string WsPath, TempDir Scratch) BuildWorkspace()
    {
        var doc = TestSupport.NormalizeSpec(Spec);
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings(), "t");
        var (collection, _) = engine.Sync(null, doc);
        var scratch = new TempDir();
        BrunoWorkspaceWriter.Execute(BrunoWorkspaceWriter.Plan(collection, scratch.Path));
        return (collection, scratch.Path, scratch);
    }

    [Fact]
    public void Layout_Matches_Official_Bruno_OpenCollection_Filesystem_Format()
    {
        var (_, wsPath, scratch) = BuildWorkspace();

        Assert.True(File.Exists(Path.Combine(wsPath, "opencollection.yml")));
        Assert.True(Directory.Exists(Path.Combine(wsPath, "訂單")));
        Assert.True(File.Exists(Path.Combine(wsPath, "訂單", "folder.yml")));
        Assert.True(File.Exists(Path.Combine(wsPath, "訂單", "建立訂單.yml")));

        // Root file declares the unbundled folder format.
        var root = File.ReadAllText(Path.Combine(wsPath, "opencollection.yml"));
        Assert.Contains("bundled: false", root, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_Write_Read_Is_Byte_Stable()
    {
        var (_, wsPath, scratch) = BuildWorkspace();

        var firstRead = BrunoWorkspaceReader.Read(wsPath).ToYaml();
        // Re-materialize from the read-back model and compare every file byte-for-byte.
        BrunoWorkspaceWriter.Execute(BrunoWorkspaceWriter.Plan(BrunoWorkspaceReader.Read(wsPath), wsPath));
        var secondRead = BrunoWorkspaceReader.Read(wsPath).ToYaml();

        Assert.Equal(firstRead, secondRead);
        Assert.True(File.Exists(Path.Combine(wsPath, "opencollection.yml")));
        Assert.True(Directory.Exists(Path.Combine(wsPath, "訂單")));
        Assert.True(File.Exists(Path.Combine(wsPath, "訂單", "folder.yml")));
        Assert.True(File.Exists(Path.Combine(wsPath, "訂單", "建立訂單.yml")));
        Assert.True(Directory.Exists(Path.Combine(wsPath, "使用者")));
    }

    [Fact]
    public void Workspace_And_Bundled_Collection_Come_From_One_Canonical_Model()
    {
        var (collection, wsPath, scratch) = BuildWorkspace();

        // Every request in the bundled model has an exact-content twin on disk.
        foreach (var request in WalkRequests(collection.RootItems.ToList()))
        {
            var name = BrunoYaml.AsString(BrunoYaml.Get(
                BrunoYaml.MappingOf(request.Node, "info") ?? [], "name")) ?? "";
            var file = Directory.EnumerateFiles(wsPath, "*.yml", SearchOption.AllDirectories)
                .FirstOrDefault(f => Path.GetFileName(f) == $"{BrunoWorkspaceNames.Sanitize(name)}.yml");
            Assert.NotNull(file);

            var diskDoc = BrunoYaml.ParseDocument(File.ReadAllText(file!), file!);
            Assert.Equal(
                BrunoYaml.ToYaml(request.Node),
                BrunoYaml.ToYaml(diskDoc),
                ignoreLineEndingDifferences: false);
        }
    }

    [Fact]
    public void Manual_Edits_In_Workspace_Survive_Next_Sync()
    {
        var (collection, wsPath, scratch) = BuildWorkspace();

        // User edits a value inside the workspace request file.
        var requestFile = Path.Combine(wsPath, "使用者", "查詢使用者.yml");
        var original = File.ReadAllText(requestFile);
        File.WriteAllText(requestFile, original.Replace("value: \"1\"", "value: \"999\"", StringComparison.Ordinal));

        // Next sync reads the WORKSPACE as the canonical source.
        var existing = BrunoWorkspaceReader.Read(wsPath);
        var specDoc = TestSupport.NormalizeSpec(Spec);
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings(), "t");
        var (synced, _) = engine.Sync(existing, specDoc);
        BrunoWorkspaceWriter.Execute(BrunoWorkspaceWriter.Plan(synced, wsPath));

        var after = File.ReadAllText(requestFile);
        Assert.Contains("value: \"999\"", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Removed_Operation_File_Is_Cleaned_Up_When_Removal_Enabled()
    {
        var (collection, wsPath, _) = BuildWorkspace();

        const string shrunkSpec = """
            openapi: 3.0.3
            info: { title: t, version: "1" }
            servers:
              - url: https://api.example.com/v1
            paths:
              /users/{id}:
                get:
                  operationId: getUser
                  parameters:
                    - name: id
                      in: path
                      required: true
                      schema: { type: integer }
                  responses:
                    '200': { description: OK }
            """;

        var existing = BrunoWorkspaceReader.Read(wsPath);
        var engine = new BrunoSyncEngine(
            new BrunoSyncSettings { RemoveDeletedOperations = true },
            new BrunoExampleSettings { Enabled = false }, "t");
        var (synced, outcome) = engine.Sync(existing, TestSupport.NormalizeSpec(shrunkSpec));

        Assert.Single(outcome.Operations, o => o.Action == OperationAction.Removed);
        var plan = BrunoWorkspaceWriter.Plan(synced, wsPath);
        Assert.DoesNotContain(plan.OrphanedFiles, f => !f.EndsWith("建立訂單.yml", StringComparison.Ordinal));
        Assert.Contains(plan.OrphanedFiles, f => f.EndsWith("建立訂單.yml", StringComparison.Ordinal));

        BrunoWorkspaceWriter.Execute(plan);
        Assert.False(File.Exists(Path.Combine(wsPath, "訂單", "建立訂單.yml")));
        // The now-empty folder itself stays (its folder.yml is user-visible structure,
        // matching Bruno's own preservation of manually created folders).
        Assert.True(File.Exists(Path.Combine(wsPath, "訂單", "folder.yml")));
    }

    [Fact]
    public void Unbound_User_Files_Are_Never_Deleted()
    {
        var (_, wsPath, _) = BuildWorkspace();

        const string userFile = """
            info:
              name: 我自己的請求
              type: http
              seq: 99
            http:
              method: GET
              url: https://example.com/mine
            """;
        var path = Path.Combine(wsPath, "我自己的請求.yml");
        File.WriteAllText(path, userFile);

        var existing = BrunoWorkspaceReader.Read(wsPath);
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = false }, "t");
        var (synced, _) = engine.Sync(existing, TestSupport.NormalizeSpec(Spec));

        // The unbound file survives the full plan/execute cycle untouched…
        var plan = BrunoWorkspaceWriter.Plan(synced, wsPath);
        Assert.DoesNotContain(plan.OrphanedFiles, f => f == Path.GetFullPath(path));

        // …and it round-trips through the model back onto disk.
        BrunoWorkspaceWriter.Execute(plan);
        Assert.True(File.Exists(path));
        Assert.Contains("我自己的請求", File.ReadAllText(path), StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_Produces_Traversal_Proof_Windows_Safe_Names()
    {
        Assert.Equal("untitled", BrunoWorkspaceNames.Sanitize(""));
        Assert.Equal("untitled", BrunoWorkspaceNames.Sanitize("/"));
        // Slashes become dashes, so a hostile label can never form path segments.
        Assert.Equal("..-..-etc-passwd", BrunoWorkspaceNames.Sanitize("../../etc/passwd"));
        Assert.Equal("a-b---b", BrunoWorkspaceNames.Sanitize("a<b>:\"b")); // each invalid char maps to one dash
        Assert.StartsWith("-CON", BrunoWorkspaceNames.Sanitize("CON")); // reserved device name defused
        Assert.Equal("x", BrunoWorkspaceNames.Sanitize("  x. ."));

        foreach (var raw in new[] { "", "..", ".", "con", "aux", "a?b*c" })
        {
            var sanitized = BrunoWorkspaceNames.Sanitize(raw);
            Assert.DoesNotContain(sanitized, System.IO.Path.GetInvalidFileNameChars());
            Assert.NotEqual("..", sanitized);
            Assert.NotEqual(".", sanitized);
        }
    }

    [Fact]
    public void IsWorkspace_Detects_Root_File_Only()
    {
        var (_, wsPath, _) = BuildWorkspace();
        Assert.True(BrunoWorkspaceReader.IsWorkspace(wsPath));
        Assert.False(BrunoWorkspaceReader.IsWorkspace(Path.Combine(wsPath, "訂單")));
        using var scratch = new TempDir();
        Assert.False(BrunoWorkspaceReader.IsWorkspace(scratch.Path));
    }

    private static List<BrunoItem> WalkRequests(List<BrunoItem> items)
    {
        var result = new List<BrunoItem>();
        foreach (var item in items)
        {
            if (item.IsFolder)
            {
                result.AddRange(WalkRequests(item.Children.ToList()));
            }
            else
            {
                result.Add(item);
            }
        }

        return result;
    }
}
