using OpenApiSentinel.OpenApi;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// 3.x content-based parameters declare one schema per media type. Every media type is
/// now diffed — closing the documented "first media type only" false negative — while
/// single-media params keep byte-identical legacy behavior.
/// </summary>
public class ParameterContentTests
{
    private const string Base = """
        openapi: 3.0.3
        info:
          title: Content API
          version: "1.0"
        paths:
          /things:
            get:
              operationId: list
              parameters:
                - name: filter
                  in: query
                  content:
                    application/json:
                      schema:
                        type: string
                        maxLength: 10
                    application/xml:
                      schema:
                        type: string
              responses:
                '200':
                  description: ok
        """;

    [Fact]
    public void SchemaChange_In_Second_MediaType_IsDetected()
    {
        // The old implementation kept only the FIRST media type's schema; a constraint
        // change in application/xml was invisible. Now it surfaces.
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Content API
          version: "1.0"
        paths:
          /things:
            get:
              operationId: list
              parameters:
                - name: filter
                  in: query
                  content:
                    application/json:
                      schema:
                        type: string
                        maxLength: 10
                    application/xml:
                      schema:
                        type: string
                        maxLength: 5
              responses:
                '200':
                  description: ok
        """;

        var changes = TestSpecHelper.Compare(Base, newSpec);
        var c = Assert.Single(changes);
        Assert.Equal(ChangeKinds.ConstraintChanged, c.Kind);
        Assert.Contains("application/xml", c.Location, StringComparison.Ordinal);
        Assert.DoesNotContain("content.parameters", c.Location, StringComparison.Ordinal);
        Assert.Equal("filter", c.ParameterName);
    }

    [Fact]
    public void SecondMediaType_KeyRemoved_IsDetected()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Content API
          version: "1.0"
        paths:
          /things:
            get:
              operationId: list
              parameters:
                - name: filter
                  in: query
                  content:
                    application/json:
                      schema:
                        type: string
                        maxLength: 10
              responses:
                '200':
                  description: ok
        """;

        var changes = TestSpecHelper.Compare(Base, newSpec);
        var removed = Assert.Single(changes);
        Assert.Equal(ChangeKinds.ParameterContentRemoved, removed.Kind);
        Assert.Equal("application/xml", removed.OldValue);
    }

    [Fact]
    public void SecondMediaType_KeyAdded_IsDetected()
    {
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Content API
          version: "1.0"
        paths:
          /things:
            get:
              operationId: list
              parameters:
                - name: filter
                  in: query
                  content:
                    application/json:
                      schema:
                        type: string
                        maxLength: 10
              responses:
                '200':
                  description: ok
        """;
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Content API
          version: "1.0"
        paths:
          /things:
            get:
              operationId: list
              parameters:
                - name: filter
                  in: query
                  content:
                    application/json:
                      schema:
                        type: string
                        maxLength: 10
                    application/xml:
                      schema:
                        type: string
              responses:
                '200':
                  description: ok
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.Single(changes, c => c.Kind == ChangeKinds.ParameterContentAdded &&
                                    c.NewValue == "application/xml");
    }

    [Fact]
    public void SingleMedia_Param_Behavior_Unchanged()
    {
        // A single-media content param still diffs through the legacy Schema path:
        // its constraint change must surface exactly once with the parameter kind.
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Content API
          version: "1.0"
        paths:
          /things:
            get:
              operationId: list
              parameters:
                - name: q
                  in: query
                  content:
                    application/json:
                      schema:
                        type: string
                        maxLength: 10
              responses:
                '200':
                  description: ok
        """;

        var changes = TestSpecHelper.Compare(oldSpec, oldSpec.Replace("maxLength: 10", "maxLength: 20"));
        var c = Assert.Single(changes);
        Assert.Equal(ChangeKinds.ParameterConstraintChanged, c.Kind); // legacy CG2011 routing preserved
        Assert.DoesNotContain(changes, x => x.Location.Contains("content."));
    }
}
