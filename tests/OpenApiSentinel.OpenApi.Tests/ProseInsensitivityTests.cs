using OpenApiSentinel.OpenApi;
using Xunit;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Container-level change gates must be driven by SUBSTANTIVE child differences only.
/// Description/title/example drift inside an items subtree or a composition member is
/// documentation, not compatibility (§55 false-positive control). Found on the real
/// Kubernetes v1.30 → v1.31 diff: 2,076 of 3,854 CG5008 events were prose-only noise.
/// </summary>
public class ProseInsensitivityTests
{
    private const string ItemsSpecA = """
        openapi: 3.0.3
        info:
          title: P
          version: "1.0"
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/ThingList'
        components:
          schemas:
            ThingList:
              type: object
              properties:
                data:
                  type: array
                  items:
                    type: object
                    description: Original description.
                    properties:
                      name:
                        type: string
        """;

    private const string ItemsSpecProseOnly = """
        openapi: 3.0.3
        info:
          title: P
          version: "1.0"
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/ThingList'
        components:
          schemas:
            ThingList:
              type: object
              properties:
                data:
                  type: array
                  items:
                    type: object
                    description: Reworded description with more detail.
                    title: Also retitled.
                    properties:
                      name:
                        type: string
        """;

    private const string ItemsSpecExtraProperty = """
        openapi: 3.0.3
        info:
          title: P
          version: "1.0"
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/ThingList'
        components:
          schemas:
            ThingList:
              type: object
              properties:
                data:
                  type: array
                  items:
                    type: object
                    description: Original description.
                    properties:
                      name:
                        type: string
                      size:
                        type: integer
        """;

    private const string ItemsScalarSpec = """
        openapi: 3.0.3
        info:
          title: P
          version: "1.0"
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/ThingList'
        components:
          schemas:
            ThingList:
              type: object
              properties:
                data:
                  type: array
                  items:
                    type: string
        """;

    private const string NoItemsSpec = """
        openapi: 3.0.3
        info:
          title: P
          version: "1.0"
        paths:
          /things:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/ThingList'
        components:
          schemas:
            ThingList:
              type: object
              properties:
                data:
                  type: string
        """;

    [Fact]
    public void Items_Prose_Only_Drift_Produces_No_Findings()
    {
        Assert.Empty(TestSpecHelper.Compare(ItemsSpecA, ItemsSpecProseOnly));
    }

    [Fact]
    public void Items_Substantive_Child_Change_Emits_Exactly_One_Summary_Marker()
    {
        var changes = TestSpecHelper.Compare(ItemsSpecA, ItemsSpecExtraProperty);

        var markers = changes.Where(c => c.Kind == ChangeKinds.ItemsChanged).ToList();
        var children = changes.Where(c => c.Kind == ChangeKinds.PropertyAdded).ToList();

        Assert.Single(markers);
        Assert.Single(children);
    }

    [Fact]
    public void Items_Presence_Change_Emits_Exactly_One_Event()
    {
        var changes = TestSpecHelper.Compare(ItemsScalarSpec, NoItemsSpec);
        Assert.Single(changes, c => c.Kind == ChangeKinds.ItemsChanged);

        var reverse = TestSpecHelper.Compare(NoItemsSpec, ItemsScalarSpec);
        Assert.Single(reverse, c => c.Kind == ChangeKinds.ItemsChanged);
    }

    private const string CompositionNumberMember = """
        openapi: 3.0.3
        info:
          title: P
          version: "1.0"
        paths:
          /pay:
            post:
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Payment'
              responses:
                '200':
                  description: ok
        components:
          schemas:
            Payment:
              allOf:
                - type: object
                  description: Member one.
                  properties:
                    amount:
                      type: number
        """;

    private const string CompositionProseOnlyMember = """
        openapi: 3.0.3
        info:
          title: P
          version: "1.0"
        paths:
          /pay:
            post:
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Payment'
              responses:
                '200':
                  description: ok
        components:
          schemas:
            Payment:
              allOf:
                - type: object
                  description: Member one, reworded at length.
                  properties:
                    amount:
                      type: number
        """;

    private const string CompositionStringMember = """
        openapi: 3.0.3
        info:
          title: P
          version: "1.0"
        paths:
          /pay:
            post:
              requestBody:
                content:
                  application/json:
                    schema:
                      $ref: '#/components/schemas/Payment'
              responses:
                '200':
                  description: ok
        components:
          schemas:
            Payment:
              allOf:
                - type: object
                  description: Member one.
                  properties:
                    amount:
                      type: string
        """;

    [Fact]
    public void Composition_Prose_Only_Member_Change_Produces_No_Findings()
    {
        Assert.Empty(TestSpecHelper.Compare(CompositionNumberMember, CompositionProseOnlyMember));
    }

    [Fact]
    public void Composition_Substantive_Member_Change_Still_Reported()
    {
        var changes = TestSpecHelper.Compare(CompositionNumberMember, CompositionStringMember);
        Assert.Single(changes, c => c.Kind == ChangeKinds.CompositionChanged);
    }
}
