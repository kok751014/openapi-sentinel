using System.Text.Json.Nodes;
using OpenApiSentinel.Bruno.Model;
using OpenApiSentinel.OpenApi.Model;
using Model = OpenApiSentinel.Bruno.Model;

namespace OpenApiSentinel.Bruno.Tests;

/// <summary>Blocker D — multipart/form-data and form-urlencoded request bodies.</summary>
public class MultipartFormBodyTests
{
    [Fact]
    public void Multipart_Form_Request_Is_Generated()
    {
        var op = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals).Operation("createAnimal");
        var canonical = CanonicalBodyFactory.Create(op.RequestBody!, SampleValueFactory.Depth.Full);
        Assert.Equal(BodyKind.MultipartForm, canonical.Kind);
        Assert.Contains(canonical.Rows!, r => r.Name == "SpeciesId");
    }

    [Fact]
    public void Multipart_Form_Full_Example_Includes_All_Fields()
    {
        var (collection, _) = SyncAnimals();
        var item = TestSupport.FindRequest(collection, "新增動物")!;
        var rows = new BrunoRequestView(item.Node).FormRows();

        foreach (var name in new[] { "SpeciesId", "Name", "Gender", "Identification", "AreasId", "Files", "Remark" })
        {
            Assert.Contains(rows, r => r.Name == name);
        }
    }

    [Fact]
    public void Multipart_Form_Required_Only_Example_Includes_Required_Fields_Only()
    {
        var (collection, _) = SyncAnimals();
        var item = TestSupport.FindRequest(collection, "新增動物")!;
        var examples = BrunoYaml.GetOrCreateSequence(item.Node, "examples");

        YamlDotNet.RepresentationModel.YamlMappingNode? body = null;
        foreach (var map in BrunoYaml.MappingItems(examples))
        {
            if (BrunoYaml.AsString(BrunoYaml.Get(map, "name")) == "成功－僅輸入必填參數" &&
                BrunoYaml.MappingOf(map, "request") is { } request)
            {
                body = BrunoYaml.MappingOf(request, "body");
            }
        }

        Assert.NotNull(body);
        var data = BrunoYaml.SequenceOf(body!, "data");
        Assert.NotNull(data);
        var names = BrunoYaml.MappingItems(data!)
            .Select(r => BrunoYaml.AsString(BrunoYaml.Get(r, "name"))).ToList();
        Assert.Equal(RequiredNames, names);
            }

    [Fact]
    public void Multipart_Form_File_Field_Is_Safe_Placeholder()
    {
        var (collection, _) = SyncAnimals();
        var item = TestSupport.FindRequest(collection, "新增動物")!;
        var rows = new BrunoRequestView(item.Node).FormRows();

        var files = rows.Where(r => r.Name == "Files").ToList();
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.True(f.IsFile));
        Assert.All(files, f =>
        {
            Assert.DoesNotContain('/', f.Value);
            Assert.DoesNotContain('\\', f.Value);
            Assert.StartsWith(CanonicalBodyFactory.FilePlaceholder, f.Value, StringComparison.Ordinal);
        });
            }

    [Fact]
    public void Multipart_Form_Array_Field_Repeats_Part_Name()
    {
        var op = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals).Operation("createAnimal");
        var rows = CanonicalBodyFactory.Create(op.RequestBody!, SampleValueFactory.Depth.Full)
            .Rows!;
        Assert.Equal(2, rows.Count(r => r.Name == "AreasId"));
    }

    [Fact]
    public void Multipart_Form_Enum_Field_Uses_Declared_Value()
    {
        var op = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals).Operation("createAnimal");
        var rows = CanonicalBodyFactory.Create(op.RequestBody!, SampleValueFactory.Depth.Full).Rows!;
        Assert.Equal("1", rows.First(r => r.Name == "Gender").Value);
    }

    [Fact]
    public void Form_UrlEncoded_Request_Is_Generated()
    {
        var op = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals).Operation("login");
        var canonical = CanonicalBodyFactory.Create(op.RequestBody!, SampleValueFactory.Depth.Full);

        Assert.Equal(BodyKind.FormUrlEncoded, canonical.Kind);
        Assert.NotNull(canonical.ContentType);
        Assert.Contains("x-www-form-urlencoded", canonical.ContentType!);
        Assert.Contains(canonical.Rows!, r => r.Name == "username");

        // The bundled YAML body mapping matches the official schema shape.
        var requiredOnly = CanonicalBodyFactory.Create(op.RequestBody!, SampleValueFactory.Depth.RequiredOnly);
        Assert.Equal(UrlEncodedNames, requiredOnly.Rows!.Select(r => r.Name));
    }

    private static readonly string[] RequiredNames = ["SpeciesId", "Name", "Gender"];

    private static readonly string[] UrlEncodedNames = ["username", "password"];

    private static (Model.BrunoCollectionDocument, BrunoSyncOutcome) SyncAnimals()
    {
        var doc = ReleaseBlockerSpec.Parse(ReleaseBlockerSpec.Animals);
        var generator = new ScenarioGenerator(new BrunoExampleSettings { MaxPerOperation = 32 });
        var engine = new BrunoSyncEngine(new BrunoSyncSettings(), new BrunoExampleSettings { Enabled = true },
            toolVersion: "test", generator);
        return engine.Sync(null, doc);
    }
}
