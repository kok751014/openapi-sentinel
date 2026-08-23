namespace OpenApiSentinel.OpenApi;

/// <summary>
/// Stable identifiers for every structural change kind emitted by the comparison engine.
/// These strings are consumed by the rule catalog and must never be renamed.
/// </summary>
public static class ChangeKinds
{
    // ---- Endpoints & operations ----
    public const string EndpointAdded = "endpoint.added";
    public const string EndpointRemoved = "endpoint.removed";
    public const string OperationAdded = "operation.added";
    public const string OperationRemoved = "operation.removed";
    public const string OperationDeprecatedToggled = "operation.deprecated.changed";
    public const string OperationIdChanged = "operation.operationId.changed";
    public const string OperationIdAdded = "operation.operationId.added";
    public const string OperationIdRemoved = "operation.operationId.removed";
    public const string ServerChanged = "servers.changed";

    // ---- Callbacks (out-of-band server→client operations declared per operation) ----
    public const string CallbackAdded = "callback.added";
    public const string CallbackRemoved = "callback.removed";
    public const string CallbackExpressionChanged = "callback.expression.changed";

    // ---- Parameters ----
    public const string ParameterAdded = "parameter.added";
    public const string ParameterRemoved = "parameter.removed";
    public const string ParameterRequiredChanged = "parameter.required.changed";
    public const string ParameterTypeChanged = "parameter.type.changed";
    public const string ParameterFormatChanged = "parameter.format.changed";
    public const string ParameterEnumValueAdded = "parameter.enum.value.added";
    public const string ParameterEnumValueRemoved = "parameter.enum.value.removed";
    public const string ParameterDefaultChanged = "parameter.default.changed";
    public const string ParameterNullableChanged = "parameter.nullable.changed";
    public const string ParameterItemsChanged = "parameter.items.changed";
    public const string ParameterStyleChanged = "parameter.style.changed";
    public const string ParameterExplodeChanged = "parameter.explode.changed";
    public const string ParameterLocationChanged = "parameter.location.changed";
    public const string ParameterDeprecatedChanged = "parameter.deprecated.changed";

    /// <summary>Generic constraint change; Details["constraint"] carries the keyword.</summary>
    public const string ParameterConstraintChanged = "parameter.constraint.changed";

    // ---- Request bodies ----
    public const string RequestBodyAdded = "requestbody.added";
    public const string RequestBodyRemoved = "requestbody.removed";
    public const string RequestBodyRequiredChanged = "requestbody.required.changed";
    public const string RequestContentTypeAdded = "contenttype.request.added";
    public const string RequestContentTypeRemoved = "contenttype.request.removed";

    // ---- Responses ----
    public const string ResponseStatusCodeAdded = "response.statuscode.added";
    public const string ResponseStatusCodeRemoved = "response.statuscode.removed";
    public const string ResponseContentTypeAdded = "contenttype.response.added";
    public const string ResponseContentTypeRemoved = "contenttype.response.removed";
    public const string ResponseHeaderAdded = "header.response.added";
    public const string ResponseHeaderRemoved = "header.response.removed";
    public const string ResponseHeaderRequiredChanged = "header.response.required.changed";

    // ---- Shared schema-level (direction applied via SemanticChange.Direction) ----
    public const string PropertyAdded = "schema.property.added";
    public const string PropertyRemoved = "schema.property.removed";
    public const string PropertyRenamed = "schema.property.renamed";
    public const string RequiredPropertyChanged = "schema.required.changed"; // Details: property, becameRequired
    public const string SchemaTypeChanged = "schema.type.changed";
    public const string SchemaFormatChanged = "schema.format.changed";
    public const string EnumValueAdded = "schema.enum.value.added";
    public const string EnumValueRemoved = "schema.enum.value.removed";
    public const string ConstChanged = "schema.const.changed";
    public const string ConstraintChanged = "schema.constraint.changed";
    public const string NullableChanged = "schema.nullable.changed";
    public const string ItemsChanged = "schema.items.changed";
    public const string AdditionalPropertiesChanged = "schema.additionalProperties.changed";
    public const string CompositionChanged = "schema.composition.changed"; // allOf/oneOf/anyOf/not
    public const string DiscriminatorChanged = "schema.discriminator.changed";
    public const string ReadOnlyWriteOnlyChanged = "schema.readonly.changed"; // Details: kind
    public const string SchemaDeprecatedChanged = "schema.deprecated.changed";
    public const string DefaultChanged = "schema.default.changed";
    public const string ExampleChanged = "schema.example.changed";

    /// <summary>Emitted when a subtree was left un-materialized by a safety budget and
    /// the two sides cannot be proven identical (see RefResolver.UnresolvedMarkerKey).</summary>
    public const string SchemaUnresolvedChanged = "schema.unresolved.changed";

    // ---- Security ----
    public const string SecurityRequirementAdded = "security.requirement.added";
    public const string SecurityRequirementRemoved = "security.requirement.removed";
    public const string OAuthScopeAdded = "security.scope.added";
    public const string OAuthScopeRemoved = "security.scope.removed";
    public const string SecuritySchemeAdded = "security.scheme.added";
    public const string SecuritySchemeRemoved = "security.scheme.removed";
    public const string SecuritySchemeTypeChanged = "security.scheme.type.changed";
    public const string ApiKeyNameChanged = "security.apikey.name.changed";
    public const string ApiKeyLocationChanged = "security.apikey.location.changed";
    public const string HttpSchemeChanged = "security.http.scheme.changed";
    public const string BearerFormatChanged = "security.bearerformat.changed";
    public const string OpenIdConnectUrlChanged = "security.openid.url.changed";
    public const string OAuthFlowUrlChanged = "security.flow.url.changed"; // Details: flow, urlKind
    public const string OAuthFlowScopeDefRemoved = "security.flow.scope.def.removed";
    public const string OAuthFlowScopeDefAdded = "security.flow.scope.def.added";

    // ---- Metadata ----
    public const string InfoVersionChanged = "meta.info.version.changed";
    public const string TitleChanged = "meta.title.changed";
}
