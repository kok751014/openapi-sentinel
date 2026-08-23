using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.OpenApi.Tests;

/// <summary>
/// Operation-level callbacks (OpenAPI 3.0+) must be diffed like endpoints:
/// declaration add/remove, delivery-expression changes, and every change INSIDE
/// an existing callback's operations are part of the contract consumers implement.
/// </summary>
public class CallbackDiffTests
{
    // Closing delimiters sit at 8 spaces; all YAML top-level keys align there too.
    private const string BaseSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
        """;

    private const string WithCallback = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      summary: Order event delivery
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              properties:
                                eventId:
                                  type: string
                                status:
                                  type: string
                                  enum: [created, shipped]
                                note:
                                  type: string
                      responses:
                        '200':
                          description: received
        """;

    [Fact]
    public void IdenticalDocs_WithCallbacks_ProduceNoChanges()
    {
        var changes = TestSpecHelper.Compare(WithCallback, WithCallback);
        Assert.Empty(changes);
    }

    [Fact]
    public void Normalizer_Captures_CallbackStructure()
    {
        var doc = TestSpecHelper.Normalize(WithCallback);
        var op = doc.Paths["/subscribe"].Operations[HttpMethodKind.Post];

        var cb = Assert.Single(op.Callbacks);
        Assert.Equal("orderEvent", cb.Name);
        var expr = Assert.Single(cb.Expressions);
        Assert.Equal("{$request.body#/callbackUrl}", expr.Key);
        Assert.True(expr.Value.ContainsKey(HttpMethodKind.Post));
        // Inner operations carry a synthetic display path identifying owner endpoint + name.
        Assert.Equal("<callback:POST /subscribe:orderEvent>", expr.Value[HttpMethodKind.Post].Path);
    }

    [Fact]
    public void Callback_Added_IsDetected()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderShipped:
                  '{$request.body#/callbackUrl}':
                    post:
                      responses:
                        '200':
                          description: received
        """;

        var changes = TestSpecHelper.Compare(BaseSpec, newSpec);
        var added = Assert.Single(changes);
        Assert.Equal(ChangeKinds.CallbackAdded, added.Kind);
        Assert.Equal("/subscribe", added.Path);
        Assert.Equal(HttpMethodKind.Post, added.HttpMethod);
        Assert.Equal("POST /subscribe callbacks.orderShipped", added.Location);
        Assert.Equal("orderShipped", added.NewValue);
        Assert.Contains("{$request.body#/callbackUrl}", added.Details["expressions"]);
    }

    [Fact]
    public void Callback_Removed_IsDetected()
    {
        var changes = TestSpecHelper.Compare(WithCallback, BaseSpec);

        var removed = Assert.Single(changes);
        Assert.Equal(ChangeKinds.CallbackRemoved, removed.Kind);
        Assert.Equal("POST /subscribe callbacks.orderEvent", removed.Location);
        Assert.Equal("orderEvent", removed.OldValue);
    }

    [Fact]
    public void Expression_Changed_SingleEvent_NotRemoveAddNoise()
    {
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      responses:
                        '200':
                          description: received
        """;
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/notificationUrl}':
                    post:
                      responses:
                        '200':
                          description: received
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        var changed = Assert.Single(changes);
        Assert.Equal(ChangeKinds.CallbackExpressionChanged, changed.Kind);
        Assert.Equal("{$request.body#/callbackUrl}", changed.OldValue);
        Assert.Equal("{$request.body#/notificationUrl}", changed.NewValue);
        Assert.Equal("orderEvent", changed.Details["callback"]);
    }

    [Fact]
    public void Payload_EnumContraction_Inside_Callback_IsDetected()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      summary: Order event delivery
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              properties:
                                eventId:
                                  type: string
                                status:
                                  type: string
                                  enum: [created]
                                note:
                                  type: string
                      responses:
                        '200':
                          description: received
        """;

        var changes = TestSpecHelper.Compare(WithCallback, newSpec);
        var enumRemoved = Assert.Single(changes);
        Assert.Equal(ChangeKinds.EnumValueRemoved, enumRemoved.Kind);
        Assert.Equal("<callback:POST /subscribe:orderEvent>", enumRemoved.Path);
        Assert.Equal(ApiDirection.Request, enumRemoved.Direction);
        Assert.Contains("shipped", enumRemoved.OldValue);
    }

    [Fact]
    public void RequiredProperty_Added_Inside_CallbackPayload_IsDetected()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              required:
                                - eventId
                              properties:
                                eventId:
                                  type: string
                                status:
                                  type: string
                                  enum: [created, shipped]
                                note:
                                  type: string
                      responses:
                        '200':
                          description: received
        """;

        var changes = TestSpecHelper.Compare(WithCallback, newSpec);
        var req = Assert.Single(changes);
        Assert.Equal(ChangeKinds.RequiredPropertyChanged, req.Kind);
        Assert.Equal("<callback:POST /subscribe:orderEvent>", req.Path);
        Assert.Equal("true", req.Details["becameRequired"]);
    }

    [Fact]
    public void Method_Added_Inside_ExistingCallback_IsDetected()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      responses:
                        '200':
                          description: received
                    delete:
                      responses:
                        '200':
                          description: cancelled
        """;
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      responses:
                        '200':
                          description: received
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        var added = Assert.Single(changes);
        Assert.Equal(ChangeKinds.OperationAdded, added.Kind);
        Assert.Equal("<callback:POST /subscribe:orderEvent>", added.Path);
        Assert.Equal(HttpMethodKind.Delete, added.HttpMethod);
    }

    [Fact]
    public void DescriptionOnly_Edit_Inside_Callback_ProducesNoFindings()
    {
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      summary: Delivers order events to consumers
                      description: Retries with backoff.
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              properties:
                                eventId:
                                  type: string
                                status:
                                  type: string
                                  enum: [created, shipped]
                                note:
                                  type: string
                      responses:
                        '200':
                          description: received
        """;

        var changes = TestSpecHelper.Compare(WithCallback, newSpec);
        Assert.Empty(changes);
    }

    [Fact]
    public void OpenApi31_Callbacks_AreSupported()
    {
        const string oldSpec = """
        openapi: 3.1.0
        info:
          title: Hooks API
          version: "1.0"
        paths:
          /register:
            post:
              operationId: register
              responses:
                '200':
                  description: ok
              callbacks:
                onDone:
                  '{$request.body#/url}':
                    post:
                      responses:
                        '200':
                          description: done
        """;
        const string newSpec = """
        openapi: 3.1.0
        info:
          title: Hooks API
          version: "1.0"
        paths:
          /register:
            post:
              operationId: register
              responses:
                '200':
                  description: ok
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.Equal(ChangeKinds.CallbackRemoved, Assert.Single(changes).Kind);
    }

    [Fact]
    public void Expression_Rename_Still_Diffs_Inner_Operations()
    {
        // Regression: a 1:1 expression rename used to skip the inner operation diff,
        // silently hiding payload changes (e.g. a property becoming required).
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/callbackUrl}':
                    post:
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              properties:
                                signature:
                                  type: string
                      responses:
                        '200':
                          description: received
        """;
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              callbacks:
                orderEvent:
                  '{$request.body#/notificationUrl}':
                    post:
                      requestBody:
                        content:
                          application/json:
                            schema:
                              type: object
                              required:
                                - signature
                              properties:
                                signature:
                                  type: string
                      responses:
                        '200':
                          description: received
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        Assert.Equal(2, changes.Count);
        Assert.Contains(changes, c => c.Kind == ChangeKinds.CallbackExpressionChanged);
        var req = changes.Single(c => c.Kind == ChangeKinds.RequiredPropertyChanged);
        Assert.Equal("<callback:POST /subscribe:orderEvent>", req.Path);
        Assert.Equal("true", req.Details["becameRequired"]);
    }

    [Fact]
    public void ExtensionKeys_Malformed_And_EmptyCallbacks_DoNotCrashOrPhantomReport()
    {
        const string oldSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
        """;
        const string newSpec = """
        openapi: 3.0.3
        info:
          title: Subscriptions API
          version: "1.0"
        paths:
          /subscribe:
            post:
              operationId: subscribe
              responses:
                '200':
                  description: ok
              x-not-callbacks:
                weird: true
              callbacks:
                x-internal: {}
                broken: just-a-string
                emptyExpr: {}
        """;

        var changes = TestSpecHelper.Compare(oldSpec, newSpec);
        // No phantom declarations may be reported for extension keys, malformed values,
        // or callbacks that declare no operations.
        Assert.Empty(changes);
    }
}
