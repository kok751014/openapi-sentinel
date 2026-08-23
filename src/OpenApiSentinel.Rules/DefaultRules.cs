namespace OpenApiSentinel.Rules;

/// <summary>Aggregates every built-in rule family into one registration sequence.</summary>
internal static class DefaultRules
{
    public static IEnumerable<RuleDefinition> All() =>
        EndpointRules.All()
        .Concat(ParameterRules.All())
        .Concat(RequestBodyRules.All())
        .Concat(ResponseRules.All())
        .Concat(SchemaRules.All())
        .Concat(SecurityRules.All())
        .Concat(MetadataRules.All())
        .Concat(EngineMetaRules.All());
}
