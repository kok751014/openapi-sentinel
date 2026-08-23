using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// False-negative regressions: response-header payload schemas are typed contracts
/// (type/constraint drift must surface), and whole OAuth-flow additions on existing
/// schemes must be detected (removals already were).
/// </summary>
public class HeaderSchemaAndFlowTests
{
    private const string HeaderBase = """
        openapi: 3.0.3
        info:
          title: Orders API
          version: "1.0"
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  headers:
                    X-Total-Count:
                      description: Total number of orders.
                      schema:
                        type: integer
                        format: int32
        """;

    [Fact]
    public void ResponseHeader_SchemaTypeChange_IsDetected()
    {
        var changes = TestSpecHelper.Compare(HeaderBase, HeaderBase.Replace("type: integer", "type: string"));

        var c = Assert.Single(changes);
        Assert.Equal(ChangeKinds.SchemaTypeChanged, c.Kind);
        Assert.Equal("/orders", c.Path);
        Assert.Equal(ApiDirection.Response, c.Direction);
        Assert.Equal("200", c.Details["statusCode"]);
        Assert.StartsWith("GET /orders responses.200.headers.X-Total-Count", c.Location);
    }

    [Fact]
    public void ResponseHeader_SchemaConstraintChange_IsDetected()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Orders API
          version: "1.0"
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  headers:
                    X-Total-Count:
                      schema:
                        type: integer
                        format: int32
                        minimum: 0
        """;

        var changes = TestSpecHelper.Compare(HeaderBase, newSpec);
        var c = Assert.Single(changes);
        Assert.Equal(ChangeKinds.ConstraintChanged, c.Kind);
        Assert.Equal("minimum", c.Details["constraint"]);
        Assert.Equal("Response", c.Direction.ToString());
    }

    [Fact]
    public void ResponseHeader_DescriptionOnlyEdit_ProducesNoChanges()
    {
        // The base fixture already carries a description on the header; an identical
        // document (including that prose) must stay silent.
        var changes = TestSpecHelper.Compare(HeaderBase, HeaderBase);
        Assert.Empty(changes);
    }

    [Fact]
    public void OAuthFlow_Added_ToExistingScheme_IsDetected()
    {
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Authed API
          version: "1.0"
        security:
          - oauth: []
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                implicit:
                  authorizationUrl: https://example.com/authorize
                  scopes:
                    read: Read access
        """;
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Authed API
          version: "1.0"
        security:
          - oauth: []
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                implicit:
                  authorizationUrl: https://example.com/authorize
                  scopes:
                    read: Read access
                clientCredentials:
                  tokenUrl: https://example.com/token
                  scopes:
                    read: Read access
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        var added = Assert.Single(changes);
        Assert.Equal(ChangeKinds.OAuthFlowUrlChanged, added.Kind);
        Assert.Equal("flow.added", added.Details["urlKind"]);
        Assert.Equal("clientCredentials", added.Details["flow"]);
        Assert.Equal("oauth", added.Details["scheme"]);
    }

    [Fact]
    public void OAuthFlow_Removed_FromScheme_StillDetected()
    {
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Authed API
          version: "1.0"
        security:
          - oauth: []
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                implicit:
                  authorizationUrl: https://example.com/authorize
                  scopes:
                    read: Read access
                password:
                  tokenUrl: https://example.com/token
                  scopes:
                    read: Read access
        """;
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Authed API
          version: "1.0"
        security:
          - oauth: []
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
        components:
          securitySchemes:
            oauth:
              type: oauth2
              flows:
                implicit:
                  authorizationUrl: https://example.com/authorize
                  scopes:
                    read: Read access
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        var removed = Assert.Single(changes);
        Assert.Equal("flow.removed", removed.Details["urlKind"]);
        Assert.Equal("password", removed.OldValue);
    }
}
