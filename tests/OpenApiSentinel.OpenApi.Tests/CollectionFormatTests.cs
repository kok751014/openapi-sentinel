using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Swagger 2.0 <c>collectionFormat</c> controls array wire format (csv/multi/ssv/pipes).
/// Real changes must surface as CG2013-routed style events; explicit-vs-default (absent ↔
/// "csv") and non-array usage must stay silent (§55 false-positive control).
/// </summary>
public class CollectionFormatTests
{
    private const string Base = """
        swagger: "2.0"
        info:
          title: Legacy API
          version: "1.0"
        paths:
          /search:
            get:
              operationId: search
              parameters:
                - name: tags
                  in: query
                  type: array
                  collectionFormat: multi
                  items:
                    type: string
              responses:
                '200':
                  description: ok
        """;

    [Fact]
    public void CollectionFormat_Change_IsDetected()
    {
        var changes = TestSpecHelper.Compare(Base, Base.Replace("collectionFormat: multi", "collectionFormat: csv"));

        var c = Assert.Single(changes);
        Assert.Equal(ChangeKinds.ParameterStyleChanged, c.Kind);
        Assert.Equal("multi", c.OldValue);
        Assert.Equal("csv", c.NewValue);
        Assert.Equal("tags", c.ParameterName);
        Assert.StartsWith("GET /search parameters.tags", c.Location);
    }

    [Fact]
    public void ExplicitCsv_Versus_Absent_IsSilent()
    {
        // Absent collectionFormat defaults to csv per the 2.0 spec — no semantic change.
        // Newlines normalized: raw strings carry the checkout's line endings.
        var csvSpec = Base.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("collectionFormat: multi", "collectionFormat: csv");
        var changes = TestSpecHelper.Compare(csvSpec, csvSpec.Replace("          collectionFormat: csv\n", ""));
        Assert.Empty(changes);
    }

    [Fact]
    public void CollectionFormat_OnNonArrayParam_IsIgnored()
    {
        const string oldSpec = """
        swagger: "2.0"
        info:
          title: Legacy API
          version: "1.0"
        paths:
          /search:
            get:
              operationId: search
              parameters:
                - name: q
                  in: query
                  type: string
              responses:
                '200':
                  description: ok
        """;
        const string newSpec = """
        swagger: "2.0"
        info:
          title: Legacy API
          version: "1.0"
        paths:
          /search:
            get:
              operationId: search
              parameters:
                - name: q
                  in: query
                  type: string
                  collectionFormat: multi
              responses:
                '200':
                  description: ok
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.Empty(changes);
    }
}
