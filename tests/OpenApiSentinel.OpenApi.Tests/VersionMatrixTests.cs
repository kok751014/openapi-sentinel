using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Version-matrix coverage per master spec §32: OpenAPI 3.1 and 3.2 semantics
/// (JSON-Schema type arrays, $ref siblings, webhooks) and JSON/YAML equivalence.
/// </summary>
public class VersionMatrixTests
{
    private const string V31Old = """
        openapi: 3.1.0
        info:
          title: Modern API
          version: "1.0"
        paths:
          /widgets:
            get:
              operationId: listWidgets
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Widget'
        webhooks:
          widgetChanged:
            post:
              operationId: onWidgetChanged
              responses:
                '200':
                  description: received
        components:
          schemas:
            Widget:
              type: object
              required: [id]
              properties:
                id:
                  type: string
                note:
                  type: [string, "null"]
        """;

    [Fact]
    public void OpenApi31_NullableViaTypeArray_IsUnderstood()
    {
        const string responseNew = """
            openapi: 3.1.0
            info:
              title: Modern API
              version: "2.0"
            paths:
              /widgets:
                get:
                  operationId: listWidgets
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Widget'
            components:
              schemas:
                Widget:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: string
                    note:
                      type: string
            """;
        // Response-side removal of null from the union: server stops sending null — safe for readers.
        var changes = TestSpecHelper.Compare(V31Old, responseNew);
        var nullable = changes.FirstOrDefault(c => c.Kind == ChangeKinds.NullableChanged);
        if (nullable is not null)
        {
            Assert.Equal(ApiDirection.Response, nullable.Direction);
        }
        Assert.DoesNotContain(changes, c => c.Kind is ChangeKinds.SchemaTypeChanged or ChangeKinds.PropertyRemoved);

        // And the mirrored request-side flip must be flagged as a direction-aware event.
        const string requestOld = """
            openapi: 3.1.0
            info: {title: T, version: "1.0"}
            paths:
              /widgets:
                post:
                  operationId: createWidget
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          properties:
                            label:
                              type: [string, "null"]
                  responses:
                    '201': {description: created}
            """;
        const string requestNew = """
            openapi: 3.1.0
            info: {title: T, version: "2.0"}
            paths:
              /widgets:
                post:
                  operationId: createWidget
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          properties:
                            label:
                              type: string
                  responses:
                    '201': {description: created}
            """;
        var requestChanges = TestSpecHelper.Compare(requestOld, requestNew);
        var change = Assert.Single(requestChanges, c => c.Kind == ChangeKinds.NullableChanged);
        Assert.Equal(ApiDirection.Request, change.Direction);
    }

    [Fact]
    public void OpenApi31_WebhookChanges_AreDetected()
    {
        const string newSpec = """
            openapi: 3.1.0
            info:
              title: Modern API
              version: "2.0"
            paths:
              /widgets:
                get:
                  operationId: listWidgets
                  responses:
                    '200':
                      description: ok
            webhooks:
              widgetDeleted:
                post:
                  operationId: onWidgetDeleted
                  responses:
                    '200':
                      description: received
            """;
        var changes = TestSpecHelper.Compare(V31Old, newSpec);
        var added = changes.Where(c => c.Kind == ChangeKinds.OperationAdded).ToList();
        Assert.Contains(added, c => c.Path == "<webhook:widgetDeleted>");
    }

    [Fact]
    public void OpenApi32_IsDetected_AndCompared()
    {
        const string old32 = """
            openapi: 3.2.0
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200': {description: ok}
            """;
        const string new32 = """
            openapi: 3.2.0
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: verbose
                      in: query
                      required: true
                      schema: {type: boolean}
                  responses:
                    '200': {description: ok}
            """;
        var oldDoc = TestSpecHelper.Normalize(old32);
        Assert.Equal(SpecVersion.OpenApi32, oldDoc.Version);

        var changes = TestSpecHelper.Compare(old32, new32);
        Assert.True(TestSpecHelper.HasKind(changes, ChangeKinds.ParameterAdded));
        var added = changes.First(c => c.Kind == ChangeKinds.ParameterAdded);
        Assert.Equal("true", added.Details["required"]);
    }

    [Fact]
    public void RefSiblings_ArePreservedPerJsonSchemaDialect()
    {
        const string spec = """
            openapi: 3.1.0
            info: {title: T, version: "1.0"}
            paths:
              /x:
                get:
                  operationId: getX
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Base'
                            maxLength: 10
            components:
              schemas:
                Base:
                  type: string
                  maxLength: 999
            """;
        var doc = TestSpecHelper.Normalize(spec);
        var schema = doc.Paths["/x"].Operations[HttpMethodKind.Get].Responses["200"].Content["application/json"];
        var obj = (System.Text.Json.Nodes.JsonObject)schema.Node;
        // JSON Schema 2020-12: $ref siblings override the resolved target's same-named keywords.
        Assert.Equal("string", obj["type"]?.GetValue<string>());
        Assert.Equal(10, obj["maxLength"]?.GetValue<long>());
    }

    [Theory]
    [InlineData("json")]
    [InlineData("yaml")]
    public void JsonAndYaml_ProduceEquivalentComparisons(string flavor)
    {
        const string jsonOld = """{"openapi":"3.0.3","info":{"title":"T","version":"1.0"},"paths":{"/a":{"get":{"operationId":"getA","responses":{"200":{"description":"ok"}}}}}}""";
        const string jsonNew = """{"openapi":"3.0.3","info":{"title":"T","version":"2.0"},"paths":{"/a":{"get":{"operationId":"getA","parameters":[{"name":"p","in":"query","required":true,"schema":{"type":"string"}}],"responses":{"200":{"description":"ok"}}}}}}""";
        const string yamlOld = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /a:
                get:
                  operationId: getA
                  responses:
                    '200': {description: ok}
            """;
        const string yamlNew = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /a:
                get:
                  operationId: getA
                  parameters:
                    - name: p
                      in: query
                      required: true
                      schema: {type: string}
                  responses:
                    '200': {description: ok}
            """;

        var (oldSpec, newSpec) = flavor == "json" ? (jsonOld, jsonNew) : (yamlOld, yamlNew);
        var changes = TestSpecHelper.Compare(oldSpec, newSpec);

        Assert.True(TestSpecHelper.HasKind(changes, ChangeKinds.InfoVersionChanged));
        var added = Assert.Single(changes, c => c.Kind == ChangeKinds.ParameterAdded);
        Assert.Equal("/a", added.Path);
        Assert.Equal("p", added.ParameterName);
        Assert.Equal("true", added.Details["required"]);
    }
}

public class WebhookContentTests
{
    private const string OldSpec = """
        openapi: 3.1.0
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200': {description: ok}
        webhooks:
          orderPaid:
            post:
              operationId: onOrderPaid
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: object
                        properties:
                          orderId: {type: string}
                          amount: {type: number}
        """;

    [Fact]
    public void SchemaChange_InsideExistingWebhook_IsDetected()
    {
        const string newSpec = """
            openapi: 3.1.0
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200': {description: ok}
            webhooks:
              orderPaid:
                post:
                  operationId: onOrderPaid
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            required: [orderId]
                            properties:
                              orderId: {type: string}
                              amount: {type: number}
            """;
        var changes = TestSpecHelper.Compare(OldSpec, newSpec);
        // The webhook's payload became stricter — must surface as a schema-level event,
        // not be silently ignored because it lives under webhooks.
        Assert.Contains(changes, c => c.Path.StartsWith("<webhook:", StringComparison.Ordinal) &&
            c.Kind is ChangeKinds.PropertyAdded or ChangeKinds.RequiredPropertyChanged);
    }

    [Fact]
    public void MethodRemoved_FromWebhook_IsDetected()
    {
        const string newSpec = """
            openapi: 3.1.0
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200': {description: ok}
            webhooks:
              orderPaid:
                get:
                  operationId: pollOrderPaid
                  responses:
                    '200': {description: ok}
            """;
        var changes = TestSpecHelper.Compare(OldSpec, newSpec);
        Assert.Contains(changes, c => c.Kind == ChangeKinds.OperationRemoved &&
            c.Path == "<webhook:orderPaid>" && c.HttpMethod == HttpMethodKind.Post);
    }
}

public class CrossVersionMigrationTests
{
    /// <summary>
    /// Master spec §4: different spec versions must not be treated as identical by default,
    /// but an API migrated faithfully from Swagger 2.0 to OpenAPI 3.0.3 must compare CLEAN —
    /// proving the normalizer unifies body params, definitions refs, servers synthesis and
    /// consumes/produces into the same semantic model.
    /// </summary>
    [Fact]
    public void Faithful_Swagger20_To_OpenApi30_Migration_ComparesClean()
    {
        const string v2 = """
            swagger: "2.0"
            info:
              title: Migrated API
              version: "1.0.0"
            host: api.example.com
            basePath: /v1
            schemes: [https]
            consumes: [application/json]
            securityDefinitions:
              keyAuth:
                type: apiKey
                name: X-API-Key
                in: header
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: page
                      in: query
                      type: integer
                      format: int32
                  responses:
                    200:
                      description: ok
                      schema:
                        $ref: '#/definitions/OrderList'
                post:
                  operationId: createOrder
                  parameters:
                    - name: body
                      in: body
                      required: true
                      schema:
                        $ref: '#/definitions/CreateOrder'
                  responses:
                    201:
                      description: created
                      schema:
                        $ref: '#/definitions/Order'
            definitions:
              OrderList:
                type: array
                items:
                  $ref: '#/definitions/Order'
              Order:
                type: object
                required: [id]
                properties:
                  id:
                    type: string
              CreateOrder:
                type: object
                required: [customerId]
                properties:
                  customerId:
                    type: string
            """;
        const string v30 = """
            openapi: 3.0.3
            info:
              title: Migrated API
              version: "1.0.0"
            servers:
              - url: https://api.example.com/v1
            components:
              securitySchemes:
                keyAuth:
                  type: apiKey
                  name: X-API-Key
                  in: header
              schemas:
                OrderList:
                  type: array
                  items:
                    $ref: '#/components/schemas/Order'
                Order:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: string
                CreateOrder:
                  type: object
                  required: [customerId]
                  properties:
                    customerId:
                      type: string
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: page
                      in: query
                      schema:
                        type: integer
                        format: int32
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/OrderList'
                post:
                  operationId: createOrder
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema:
                          $ref: '#/components/schemas/CreateOrder'
                  responses:
                    '201':
                      description: created
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Order'
            """;
        var changes = TestSpecHelper.Compare(v2, v30);
        Assert.True(changes.Count == 0,
            "expected zero changes for a faithful 2.0→3.0 migration but got: "
            + string.Join(", ", TestSpecHelper.Kinds(changes)));
    }

    [Fact]
    public void Migration_WithRealChange_StillDetectsBreakage()
    {
        const string v2 = """
            swagger: "2.0"
            info:
              title: Migrated API
              version: "1.0.0"
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: page
                      in: query
                      type: integer
                  responses:
                    200:
                      description: ok
            """;
        // Same migration, but page gains maximum: 50 during the move — still breaking.
        const string v30 = """
            openapi: 3.0.3
            info:
              title: Migrated API
              version: "2.0.0"
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: page
                      in: query
                      required: true
                      schema:
                        type: integer
                        maximum: 50
                  responses:
                    '200':
                      description: ok
            """;
        var changes = TestSpecHelper.Compare(v2, v30);
        Assert.True(TestSpecHelper.HasKind(changes, ChangeKinds.ParameterRequiredChanged) ||
                    TestSpecHelper.HasKind(changes, ChangeKinds.ParameterAdded),
            "expected requiredness/addition event across version migration");
        Assert.True(TestSpecHelper.HasKind(changes, ChangeKinds.ParameterConstraintChanged));
    }
}
