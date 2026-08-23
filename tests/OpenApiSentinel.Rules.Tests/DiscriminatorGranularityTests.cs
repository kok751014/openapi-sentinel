using OpenApiSentinel.Core;
using OpenApiSentinel.OpenApi;
using OpenApiSentinel.OpenApi.Model;

namespace OpenApiSentinel.Rules.Tests;

/// <summary>
/// Discriminator granularity (CG5011): propertyName changes are Breaking, mapping
/// removals/retargets are PotentiallyBreaking, purely additive mapping extensions
/// are NonBreaking — false-positive control for polymorphic schemas.
/// </summary>
public class DiscriminatorGranularityTests
{
    private const string Base = """
        openapi: 3.0.3
        info:
          title: Pets API
          version: "1.0"
        paths:
          /pets:
            get:
              responses:
                '200':
                  description: ok
                  content:
                    application/json:
                      schema:
                        $ref: '#/components/schemas/Pet'
        components:
          schemas:
            Pet:
              type: object
              discriminator:
                propertyName: petType
                mapping:
                  dog: '#/components/schemas/Dog'
                  cat: '#/components/schemas/Cat'
            Dog:
              type: object
            Cat:
              type: object
        """;

    private static IReadOnlyList<Finding> Evaluate(string oldSpec, string newSpec)
    {
        var oldDoc = Normalize(oldSpec);
        var newDoc = Normalize(newSpec);
        var changes = new ComparisonEngine().Compare(oldDoc, Normalize(newSpec));
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

    [Fact]
    public void Mapping_Extension_IsNonBreaking_Low()
    {
        // NOTE: search strings use post-raw-string-stripping indentation (8-space prefix removed).
        var newSpec = Base.Replace(
            "          cat: '#/components/schemas/Cat'",
            "          cat: '#/components/schemas/Cat'\n          lizard: '#/components/schemas/Lizard'");
        Assert.NotEqual(Base, newSpec);

        var findings = Evaluate(Base, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG5011");
        Assert.Equal("Discriminator mapping extended", f.Title);
        Assert.Equal(Severity.Low, f.Severity);
        Assert.Equal(Compatibility.NonBreaking, f.Compatibility);
        Assert.Contains("lizard", f.Explanation);
    }

    [Fact]
    public void Mapping_Removal_IsPotentiallyBreaking_Medium()
    {
        var newSpec = Base.Replace("          cat: '#/components/schemas/Cat'\n", "");

        var findings = Evaluate(Base, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG5011");
        Assert.Equal("Discriminator mapping changed", f.Title);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
        Assert.Contains("cat", f.Explanation);
    }

    [Fact]
    public void PropertyName_Change_IsBreaking_High()
    {
        var newSpec = Base.Replace("propertyName: petType", "propertyName: kind");

        var findings = Evaluate(Base, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG5011");
        Assert.Equal("Discriminator changed", f.Title);
        Assert.Equal(Severity.High, f.Severity);
        Assert.Equal(Compatibility.Breaking, f.Compatibility);
    }

    [Fact]
    public void Mapping_TargetChanged_IsPotentiallyBreaking_Medium()
    {
        var newSpec = Base.Replace(
            "          dog: '#/components/schemas/Dog'",
            "          dog: '#/components/schemas/Labrador'");

        var findings = Evaluate(Base, newSpec);
        var f = Assert.Single(findings, x => x.RuleId == "CG5011");
        Assert.Equal("Discriminator mapping changed", f.Title);
        Assert.Equal(Severity.Medium, f.Severity);
        Assert.Equal(Compatibility.PotentiallyBreaking, f.Compatibility);
    }

    [Fact]
    public void Identical_Discriminators_ProduceNothing()
    {
        var findings = Evaluate(Base, Base);
        Assert.DoesNotContain(findings, f => f.RuleId == "CG5011");
    }
}
