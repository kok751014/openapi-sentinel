using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;
using OpenApiSentinel.Rules;

namespace OpenApiSentinel.Rules.Tests;

/// <summary>
/// Fixture-driven suites per master spec §55/§56: harmless edits must not cry wolf,
/// subtle breakage must never slip through.
/// </summary>
public class FalsePositiveNegativeSuites
{
    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private static bool HasRisk(IReadOnlyList<Finding> findings) =>
        findings.Any(f => f.Compatibility is Compatibility.Breaking or Compatibility.PotentiallyBreaking);

    // ---------------- §55 false positives ----------------

    [Fact]
    public void DocumentationOnly_Changes_ProduceNoBreakingFindings()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info:
              title: Orders API
              version: "1.0"
            paths:
              /orders:
                get:
                  operationId: listOrders
                  summary: List orders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Order'
            components:
              schemas:
                Order:
                  type: object
                  required: [id]
                  properties:
                    id:
                      type: string
                      description: The identifier.
                    total:
                      type: number
            """;
        const string newSpec = """
            openapi: 3.0.3
            info:
              title: Orders API — internal edition
              version: "1.0"
            paths:
              /orders:
                get:
                  operationId: listOrders
                  summary: List orders (improved wording)
                  description: Returns all orders visible to the caller.
                  responses:
                    '200':
                      description: OK — reworded documentation only
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Order'
            components:
              schemas:
                Order:
                  type: object
                  description: A customer order.
                  required: [id]
                  properties:
                    id:
                      type: string
                      description: Unique identifier of the order.
                    total:
                      type: number
                      description: Grand total including taxes.
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.False(HasRisk(findings), $"Unexpected breaking findings: {string.Join(", ", findings.Select(f => f.RuleId))}");
    }

    [Fact]
    public void PropertyAndKeywordReordering_ProducesNoFindings()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Order'
            components:
              schemas:
                Order:
                  type: object
                  required: [id, total]
                  properties:
                    id: {type: string}
                    total: {type: number}
            """;
        const string reordered = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            $ref: '#/components/schemas/Order'
            components:
              schemas:
                Order:
                  required: [total, id]
                  properties:
                    total: {type: number}
                    id: {type: string}
                  type: object
            """;
        var findings = Evaluate(oldSpec, reordered);
        Assert.Empty(findings);
    }

    [Fact]
    public void TagAndSummaryChanges_AreAtMostInformational()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  tags: [Orders]
                  responses:
                    '200': {description: ok}
            """;
        const string retagged = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  tags: [Commerce, Orders]
                  summary: List every order
                  responses:
                    '200': {description: ok}
            """;
        var findings = Evaluate(oldSpec, retagged);
        Assert.False(HasRisk(findings));
    }

    // ---------------- §56 false negatives ----------------

    [Fact]
    public void RequestEnumContraction_IsDetected()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: status
                      in: query
                      schema: {type: string, enum: [open, closed]}
                  responses:
                    '200': {description: ok}
            """;
        const string narrowed = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: status
                      in: query
                      schema: {type: string, enum: [open]}
                  responses:
                    '200': {description: ok}
            """;
        var findings = Evaluate(oldSpec, narrowed);
        Assert.Contains(findings, f => f.RuleId == "CG2007" && f.Compatibility == Compatibility.Breaking);
    }

    [Fact]
    public void RangeNarrowing_OnRequest_IsDetected()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          required: [amount]
                          properties:
                            amount: {type: number, minimum: 0}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          required: [amount]
                          properties:
                            amount: {type: number, minimum: 100}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.Contains(findings, f => f.Compatibility == Compatibility.Breaking && f.Severity >= Severity.High);
    }

    [Fact]
    public void ResponseNullableRemoval_IsNotBreaking_ForConsumers()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              note: {type: string, nullable: true}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              note: {type: string}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.DoesNotContain(findings, f => f.Compatibility == Compatibility.Breaking);
    }

    [Fact]
    public void RequestNullableRemoval_IsBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          properties:
                            note: {type: string, nullable: true}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          properties:
                            note: {type: string}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.Contains(findings, f => f.RuleId is "CG5007" or "CG2010" && f.Compatibility == Compatibility.Breaking);
    }

    [Fact]
    public void OptionalRequestProperty_BecomesRequired_IsDetected()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          properties:
                            customerId: {type: string}
                            channel: {type: string}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          required: [customerId]
                          properties:
                            customerId: {type: string}
                            channel: {type: string}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.Contains(findings, f => f.Compatibility == Compatibility.Breaking && f.Severity >= Severity.High);
    }

    [Fact]
    public void RequiredEntryWithoutProperty_OnRequest_IsStillDetected()
    {
        // Invalid-but-possible: required references a property that is defined on neither side.
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          required: [customerId]
                          properties:
                            customerId: {type: string}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          required: [customerId, channel]
                          properties:
                            customerId: {type: string}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.Contains(findings, f => f.Compatibility == Compatibility.Breaking);
    }

    [Fact]
    public void ResponseContentTypeRemoval_IsPotentiallyBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: {type: array, items: {type: string}}
                        application/xml:
                          schema: {type: array, items: {type: string}}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: {type: array, items: {type: string}}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.Contains(findings, f => f.RuleId == "CG4003");
    }

    [Fact]
    public void ParameterExplodeChange_IsDetected()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: tags
                      in: query
                      style: form
                      explode: false
                      schema: {type: array, items: {type: string}}
                  responses:
                    '200': {description: ok}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  parameters:
                    - name: tags
                      in: query
                      style: form
                      explode: true
                      schema: {type: array, items: {type: string}}
                  responses:
                    '200': {description: ok}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.Contains(findings, f => f.RuleId is "CG2014" or "CG2013");
    }

    [Fact]
    public void CompositionMemberRemoval_IsFlagged()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            allOf:
                              - type: object
                                properties: {id: {type: string}}
                              - type: object
                                properties: {extra: {type: string}}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            allOf:
                              - type: object
                                properties: {id: {type: string}}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        Assert.Contains(findings, f => f.RuleId == "CG5010");
    }
}

// ---------------- additionalProperties directionality ----------------

public class AdditionalPropertiesDirectionTests
{
    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private const string RequestOpen = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            post:
              operationId: createOrder
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
                      properties: {a: {type: string}}
              responses:
                '201': {description: created}
        """;

    private const string ResponseOpen = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        type: object
                        properties: {a: {type: string}}
        """;

    [Fact]
    public void Closing_RequestObject_IsBreakingHigh()
    {
        const string closed = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          additionalProperties: false
                          properties: {a: {type: string}}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(RequestOpen, closed);
        var f = Assert.Single(findings, x => x.RuleId == "CG5009");
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void Opening_RequestObject_IsNonBreaking()
    {
        const string closed = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          additionalProperties: false
                          properties: {a: {type: string}}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(closed, RequestOpen);
        var f = Assert.Single(findings, x => x.RuleId == "CG5009");
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
    }

    [Fact]
    public void Closing_ResponseObject_IsNonBreaking_ForReaders()
    {
        const string responseClosed = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            additionalProperties: false
                            properties: {a: {type: string}}
            """;
        var findings = Evaluate(ResponseOpen, responseClosed);
        var f = Assert.Single(findings, x => x.RuleId == "CG5009");
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
    }

    [Fact]
    public void Opening_ResponseObject_IsPotentiallyBreaking()
    {
        const string responseClosed = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            additionalProperties: false
                            properties: {a: {type: string}}
            """;
        var findings = Evaluate(responseClosed, ResponseOpen);
        var f = Assert.Single(findings, x => x.RuleId == "CG5009");
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
    }

    [Fact]
    public void AbsentVersusExplicitTrue_ProducesNoFinding()
    {
        const string explicitTrue = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          additionalProperties: true
                          properties: {a: {type: string}}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(RequestOpen, explicitTrue);
        Assert.DoesNotContain(findings, f => f.RuleId == "CG5009");
    }

    [Fact]
    public void SchemaValuedTransition_IsVerify()
    {
        const string schemaValued = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema:
                          type: object
                          additionalProperties: {type: string}
                          properties: {a: {type: string}}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(RequestOpen, schemaValued);
        var f = Assert.Single(findings, x => x.RuleId == "CG5009");
        Assert.Equal(Severity.Verify, f.Severity);
        Assert.Equal(Compatibility.Unknown, f.Compatibility);
        Assert.Equal(Confidence.Uncertain, f.Confidence);
    }
}

// ---------------- format-change directionality (CG5002) ----------------

public class FormatDirectionTests
{
    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private static Finding? FormatFinding(string oldSpec, string newSpec) =>
        Evaluate(oldSpec, newSpec).FirstOrDefault(f => f.RuleId == "CG5002");

    [Fact]
    public void Response_Widening_Int32ToInt64_IsPotentiallyBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: {type: integer, format: int32}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: {type: integer, format: int64}
            """;
        var f = FormatFinding(oldSpec, newSpec);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void Request_Narrowing_Int64ToInt32_IsBreakingHigh()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema: {type: integer, format: int64}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema: {type: integer, format: int32}
                  responses:
                    '201': {description: created}
            """;
        var f = FormatFinding(oldSpec, newSpec);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
        Assert.Equal(Severity.High, f.Severity);
    }

    [Fact]
    public void Request_Widening_Int32ToInt64_IsNonBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema: {type: integer, format: int32}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema: {type: integer, format: int64}
                  responses:
                    '201': {description: created}
            """;
        var f = FormatFinding(oldSpec, newSpec);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
        Assert.Equal(Severity.Info, f.Severity);
    }

    [Fact]
    public void UnknownFormatPair_StaysConservative()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: {type: string, format: email}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: {type: string, format: hostname}
            """;
        var f = FormatFinding(oldSpec, newSpec);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
    }
}

// ---------------- numeric type-change directionality (CG5001) ----------------

public class NumericTypeDirectionTests
{
    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private static Finding? TypeFinding(string oldSpec, string newSpec) =>
        Evaluate(oldSpec, newSpec).FirstOrDefault(f => f.RuleId == "CG5001");

    private static string Spec(string version, string schema) => $$"""
        openapi: 3.0.3
        info: {title: T, version: "{{version}}"}
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        {{schema}}
        """;

    [Fact]
    public void Response_IntegerToNumber_IsPotentiallyBreaking_NotCritical()
    {
        var f = TypeFinding(
            Spec("1.0", "{type: integer}"),
            Spec("2.0", "{type: number}"));
        Assert.NotNull(f);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void Response_NumberToInteger_IsNonBreaking()
    {
        var f = TypeFinding(
            Spec("1.0", "{type: number}"),
            Spec("2.0", "{type: integer}"));
        Assert.NotNull(f);
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
        Assert.Equal(Severity.Low, f.Severity);
    }

    [Fact]
    public void CrossFamilyChange_StaysCriticalBreaking()
    {
        var f = TypeFinding(
            Spec("1.0", "{type: integer}"),
            Spec("2.0", "{type: string}"));
        Assert.NotNull(f);
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
        Assert.Equal(Severity.Critical, f.Severity);
    }

    [Fact]
    public void Request_IntegerToNumber_IsNonBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema: {type: integer}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema: {type: number}
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        // Request-direction widening must not be flagged as breaking by any rule.
        Assert.DoesNotContain(findings, f => f.Compatibility == Compatibility.Breaking);
    }
}

// ---------------- request body add/remove requiredness (CG3001/CG3002) ----------------

public class RequestBodyRequirednessTests
{
    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private const string NoBodyV1 = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            post:
              operationId: createOrder
              responses:
                '201': {description: created}
        """;

    private const string RequiredBodyV2 = """
        openapi: 3.0.3
        info: {title: T, version: "2.0"}
        paths:
          /orders:
            post:
              operationId: createOrder
              requestBody:
                required: true
                content:
                  application/json:
                    schema: {type: object}
              responses:
                '201': {description: created}
        """;

    private const string OptionalBodyV2 = """
        openapi: 3.0.3
        info: {title: T, version: "2.0"}
        paths:
          /orders:
            post:
              operationId: createOrder
              requestBody:
                required: false
                content:
                  application/json:
                    schema: {type: object}
              responses:
                '201': {description: created}
        """;

    [Fact]
    public void Added_RequiredBody_IsBreakingHigh()
    {
        var findings = Evaluate(NoBodyV1, RequiredBodyV2);
        var f = Assert.Single(findings, x => x.RuleId == "CG3002");
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Contains("required", f.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Added_OptionalBody_IsNonBreakingInfo()
    {
        var findings = Evaluate(NoBodyV1, OptionalBodyV2);
        var f = Assert.Single(findings, x => x.RuleId == "CG3002");
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
        Assert.Equal(Severity.Info, f.Severity);
    }

    [Fact]
    public void Removed_RequiredBody_IsBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    required: true
                    content:
                      application/json:
                        schema: {type: object}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG3001");
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
    }

    [Fact]
    public void Removed_OptionalBody_IsNonBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    required: false
                    content:
                      application/json:
                        schema: {type: object}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  responses:
                    '201': {description: created}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG3001");
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
        Assert.Equal(Severity.Low, f.Severity);
    }
}

public class NumericUnionDirectionTests
{
    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static Finding? TypeFinding(string oldSpec, string newSpec) =>
        Evaluate(oldSpec, newSpec).FirstOrDefault(f => f.RuleId == "CG5001");

    [Fact]
    public void Response_31TypeArrayWidening_IsPotentiallyBreaking()
    {
        const string oldSpec = """
            openapi: 3.1.0
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: {type: [integer]}
            """;
        const string newSpec = """
            openapi: 3.1.0
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema: {type: [integer, number]}
            """;
        var f = TypeFinding(oldSpec, newSpec);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Equal(Severity.Medium, f.Severity);
    }

    [Fact]
    public void Request_31TypeArrayNarrowing_IsCritical()
    {
        const string oldSpec = """
            openapi: 3.1.0
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema: {type: [integer, number]}
                  responses:
                    '201': {description: created}
            """;
        const string newSpec = """
            openapi: 3.1.0
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                post:
                  operationId: createOrder
                  requestBody:
                    content:
                      application/json:
                        schema: {type: [integer]}
                  responses:
                    '201': {description: created}
            """;
        var f = TypeFinding(oldSpec, newSpec);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
        Assert.Equal(Severity.Critical, f.Severity);
    }
}

// ---------------- response header requiredness direction (CG4007) ----------------

public class ResponseHeaderRequirednessTests
{
    private static Finding? HeaderFinding(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings.FirstOrDefault(f => f.RuleId == "CG4007");
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private const string RequiredHeaderV1 = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  headers:
                    X-Total-Count:
                      required: true
                      schema: {type: integer}
        """;

    private const string OptionalHeaderV2 = """
        openapi: 3.0.3
        info: {title: T, version: "2.0"}
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  headers:
                    X-Total-Count:
                      required: false
                      schema: {type: integer}
        """;

    private const string OptionalHeaderV1 = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  headers:
                    X-Total-Count:
                      required: false
                      schema: {type: integer}
        """;

    [Fact]
    public void RequiredToOptional_IsPotentiallyBreaking()
    {
        var f = HeaderFinding(RequiredHeaderV1, OptionalHeaderV2);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Contains("optional", f.Title, StringComparison.OrdinalIgnoreCase);
    }

    private const string RequiredHeaderV2 = """
        openapi: 3.0.3
        info: {title: T, version: "2.0"}
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200':
                  description: ok
                  headers:
                    X-Total-Count:
                      required: true
                      schema: {type: integer}
        """;

    [Fact]
    public void OptionalToRequired_IsNonBreaking()
    {
        var f = HeaderFinding(OptionalHeaderV1, RequiredHeaderV2);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
        Assert.Equal(Severity.Low, f.Severity);
    }
}

// ---------------- readOnly/writeOnly directionality (CG5012) ----------------

public class ReadOnlyWriteOnlyDirectionTests
{
    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private const string RequestPlainV1 = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        paths:
          /orders:
            post:
              operationId: createOrder
              requestBody:
                content:
                  application/json:
                    schema:
                      type: object
                      properties: {a: {type: string}}
              responses:
                '201': {description: created}
        """;

    [Fact]
    public void ReadOnlyAdded_OnRequest_IsBreaking()
    {
        var findings = Evaluate(RequestPlainV1, RequestPlainV1.Replace(
            "properties: {a: {type: string}}",
            "properties:\n                      a: {type: string, readOnly: true}"));
        var f = Assert.Single(findings, x => x.RuleId == "CG5012");
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
        Assert.Contains("no longer send", f.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriteOnlyAdded_OnResponse_IsPotentiallyBreaking()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties: {a: {type: string}}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              a: {type: string, writeOnly: true}
            """;
        var findings = Evaluate(oldSpec, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG5012");
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Contains("stop returning", f.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MismatchedFlag_OnRequest_IsInformationalOnly()
    {
        // writeOnly on a REQUEST schema has no wire effect in that position.
        var findings = Evaluate(RequestPlainV1, RequestPlainV1.Replace(
            "properties: {a: {type: string}}",
            "properties:\n                      a: {type: string, writeOnly: true}"));
        var f = Assert.Single(findings, x => x.RuleId == "CG5012");
        Assert.Equal(Compatibility.Informational, f.Compatibility);
    }
}

// ---------------- schema deprecation flag (CG5013) ----------------

public class SchemaDeprecatedTests
{
    private static Finding? DeprecatedFinding(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings.FirstOrDefault(f => f.RuleId == "CG5013");
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    [Fact]
    public void PropertyMarkedDeprecated_IsDetected()
    {
        const string oldSpec = """
            openapi: 3.0.3
            info: {title: T, version: "1.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties: {legacyId: {type: string}}
            """;
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200':
                      description: ok
                      content:
                        application/json:
                          schema:
                            type: object
                            properties:
                              legacyId:
                                type: string
                                deprecated: true
            """;
        var f = DeprecatedFinding(oldSpec, newSpec);
        Assert.NotNull(f); // regression guard: bool.ToString() capitalization previously broke this
        Assert.Contains("deprecated", f.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
    }
}

// ---------------- servers add/remove directionality (CG1010) ----------------

public class ServersDirectionTests
{
    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, newDoc);
        return new RulesEngine(RuleRegistry.CreateDefault(), new FakeClock(FakeClock.Fixed))
            .Evaluate(changes, new EngineOptions { OldContractName = "old", NewContractName = "new" })
            .Result.Findings;
    }

    private static ApiDocument Normalize(string yaml)
    {
        var limits = ProcessingLimits.Default();
        var root = DocumentParser.Parse("test", yaml, limits);
        new RefResolver(limits, RemoteFetchPolicy.Disabled()).Resolve(root, null);
        return OpenApiNormalizer.Normalize(root);
    }

    private static Finding? ServersFinding(string oldSpec, string newSpec) =>
        Evaluate(oldSpec, newSpec).FirstOrDefault(f => f.RuleId == "CG1010");

    private const string OneServer = """
        openapi: 3.0.3
        info: {title: T, version: "1.0"}
        servers:
          - url: https://api.example.com/v1
        paths:
          /orders:
            get:
              operationId: listOrders
              responses:
                '200': {description: ok}
        """;

    [Fact]
    public void AllServersRemoved_IsBreakingHigh()
    {
        const string newSpec = """
            openapi: 3.0.3
            info: {title: T, version: "2.0"}
            paths:
              /orders:
                get:
                  operationId: listOrders
                  responses:
                    '200': {description: ok}
            """;
        var f = ServersFinding(OneServer, newSpec);
        Assert.NotNull(f);
        Assert.Contains("removed", f.Title, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
    }

    [Fact]
    public void AdditiveServer_IsNonBreaking()
    {
        var newSpec = OneServer.Replace(
            "- url: https://api.example.com/v1",
            "- url: https://api.example.com/v1\n  - url: https://api.example.com/v2");
        var f = ServersFinding(OneServer, newSpec);
        Assert.NotNull(f);
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
        Assert.Contains("added", f.Title, StringComparison.OrdinalIgnoreCase);
    }
}
