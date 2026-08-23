# Rule catalog

OpenAPI Sentinel ships **78 built-in rules** with stable IDs (`CGxxxx`)
plus **3 engine meta-checks** (`CG9001`, `CG9002`, `CG9003`)
that document engine-emitted process findings.
Rule IDs never change meaning; new rules get fresh IDs. This file is generated from the
rule registry (`openapi-sentinel rules --format json`), so it cannot drift from the binary.

Regenerate with:

```bash
python3 build/gen-rules-doc.py
```

> `openapi-sentinel explain CG2003` shows this exact information for one rule, including
> breaking/non-breaking examples and limitations.

## Endpoint

| Rule | Title | Default severity | Classification |
|------|-------|------------------|----------------|
| `CG1001` | Endpoint removed | critical | Breaking |
| `CG1002` | Operation removed | critical | Breaking |
| `CG1003` | Endpoint added | info | NonBreaking |
| `CG1004` | Operation added | info | NonBreaking |
| `CG1005` | Operation deprecated | medium | PotentiallyBreaking |
| `CG1011` | Callback added | medium | PotentiallyBreaking |
| `CG1012` | Callback removed | low | NonBreaking |
| `CG1013` | Callback target expression changed | high | Breaking |

### `CG1001` — Endpoint removed

A previously published path no longer exists in the new contract. Clients calling it will receive 404 responses.

- **Default severity:** critical  
- **Classification:** Breaking  
- **Change kinds:** `endpoint.removed`

### `CG1002` — Operation removed

An HTTP method on an existing path disappeared while the path itself remains.

- **Default severity:** critical  
- **Classification:** Breaking  
- **Change kinds:** `operation.removed`

### `CG1003` — Endpoint added

A new path was added to the API.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `endpoint.added`

### `CG1004` — Operation added

A new HTTP method was added to an existing path.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `operation.added`

### `CG1005` — Operation deprecated

An operation was marked deprecated, signalling planned future removal.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `operation.deprecated.changed`

### `CG1011` — Callback added

A new callback was added to an operation: the server will start making out-of-band requests to client-provided URLs as part of this operation's flow.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `callback.added`

### `CG1012` — Callback removed

A callback declaration was removed from an operation: the server will no longer make those out-of-band requests.

- **Default severity:** low  
- **Classification:** NonBreaking  
- **Change kinds:** `callback.removed`

### `CG1013` — Callback target expression changed

The runtime expression or URI template that determines where callback requests are delivered changed.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `callback.expression.changed`

## Parameter

| Rule | Title | Default severity | Classification |
|------|-------|------------------|----------------|
| `CG2001` | Parameter removed | high | Breaking |
| `CG2002` | Required parameter added | high | Breaking |
| `CG2003` | Optional parameter became required | high | Breaking |
| `CG2004` | Required parameter became optional | low | NonBreaking |
| `CG2005` | Parameter type changed | high | Breaking |
| `CG2006` | Parameter format changed | medium | PotentiallyBreaking |
| `CG2007` | Parameter enum value removed | high | Breaking |
| `CG2008` | Parameter enum value added | info | NonBreaking |
| `CG2009` | Parameter default value changed | verify | Unknown |
| `CG2010` | Parameter nullability changed | high | Breaking |
| `CG2011` | Parameter constraint changed | high | Breaking |
| `CG2012` | Parameter array item schema changed | medium | PotentiallyBreaking |
| `CG2013` | Parameter serialization style changed | medium | Breaking |
| `CG2014` | Parameter explode behaviour changed | medium | Breaking |
| `CG2015` | Parameter moved to a different location | high | Breaking |
| `CG2016` | Parameter deprecated | medium | PotentiallyBreaking |

### `CG2001` — Parameter removed

A documented input parameter disappeared. The server no longer accepts or documents it.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `parameter.removed`

### `CG2002` — Required parameter added

A new parameter was added. When required, existing calls fail immediately.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `parameter.added`

### `CG2003` — Optional parameter became required

A parameter that clients could previously omit is now mandatory.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `parameter.required.changed`

### `CG2004` — Required parameter became optional

A previously mandatory parameter can now be omitted.

- **Default severity:** low  
- **Classification:** NonBreaking  
- **Change kinds:** `parameter.required.changed`

### `CG2005` — Parameter type changed

The data type of a parameter changed (e.g. string → integer).

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `parameter.type.changed`

### `CG2006` — Parameter format changed

The format of a parameter changed (e.g. int32 → int64, date-time → date).

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `parameter.format.changed`

### `CG2007` — Parameter enum value removed

A value the server previously accepted for this parameter is no longer allowed.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `parameter.enum.value.removed`

### `CG2008` — Parameter enum value added

A new value is accepted for this parameter.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `parameter.enum.value.added`

### `CG2009` — Parameter default value changed

The default applied when the parameter is omitted changed.

- **Default severity:** verify  
- **Classification:** Unknown  
- **Change kinds:** `parameter.default.changed`

### `CG2010` — Parameter nullability changed

Whether null is an accepted value for this parameter changed.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `parameter.nullable.changed`

### `CG2011` — Parameter constraint changed

A validation constraint (minimum, maximum, minLength, maxLength, pattern, minItems, maxItems, uniqueItems, multipleOf, minProperties, maxProperties) changed on a request parameter.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `parameter.constraint.changed`

### `CG2012` — Parameter array item schema changed

The schema of array elements in a parameter changed.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `parameter.items.changed`

### `CG2013` — Parameter serialization style changed

The serialization style (form, spaceDelimited, pipeDelimited, deepObject, ...) of a parameter changed.

- **Default severity:** medium  
- **Classification:** Breaking  
- **Change kinds:** `parameter.style.changed`

### `CG2014` — Parameter explode behaviour changed

The explode flag (how arrays/objects are serialized) changed.

- **Default severity:** medium  
- **Classification:** Breaking  
- **Change kinds:** `parameter.explode.changed`

### `CG2015` — Parameter moved to a different location

A parameter moved between query, header, path or cookie.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `parameter.location.changed`

### `CG2016` — Parameter deprecated

A parameter was marked (or unmarked) deprecated.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `parameter.deprecated.changed`

## RequestBody

| Rule | Title | Default severity | Classification |
|------|-------|------------------|----------------|
| `CG3001` | Request body removed | critical | Breaking |
| `CG3002` | Request body added | info | NonBreaking |
| `CG3003` | Optional request body became required | high | Breaking |
| `CG3004` | Required request body became optional | low | NonBreaking |
| `CG3005` | Supported request content type removed | high | Breaking |
| `CG3006` | Request content type added | info | NonBreaking |

### `CG3001` — Request body removed

The operation no longer accepts a request body.

- **Default severity:** critical  
- **Classification:** Breaking  
- **Change kinds:** `requestbody.removed`

### `CG3002` — Request body added

The operation now documents an optional request body.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `requestbody.added`

### `CG3003` — Optional request body became required

The operation now requires a body that was previously optional.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `requestbody.required.changed`

### `CG3004` — Required request body became optional

The previously mandatory request body is now optional.

- **Default severity:** low  
- **Classification:** NonBreaking  
- **Change kinds:** `requestbody.required.changed`

### `CG3005` — Supported request content type removed

A media type the server accepted for this operation was removed.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `contenttype.request.removed`

### `CG3006` — Request content type added

A new media type is accepted for this operation's request body.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `contenttype.request.added`

## Response

| Rule | Title | Default severity | Classification |
|------|-------|------------------|----------------|
| `CG4001` | Response status code removed | high | Breaking |
| `CG4002` | Response status code added | low | NonBreaking |
| `CG4003` | Response content type removed | medium | PotentiallyBreaking |
| `CG4004` | Response content type added | low | NonBreaking |
| `CG4005` | Response header removed | medium | PotentiallyBreaking |
| `CG4006` | Response header added | low | NonBreaking |
| `CG4007` | Response header required-ness changed | medium | PotentiallyBreaking |

### `CG4001` — Response status code removed

A documented response status code disappeared and is not covered by a remaining wildcard (default/2XX).

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `response.statuscode.removed`

### `CG4002` — Response status code added

A new response status code was documented.

- **Default severity:** low  
- **Classification:** NonBreaking  
- **Change kinds:** `response.statuscode.added`

### `CG4003` — Response content type removed

A media type documented in the response was removed.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `contenttype.response.removed`

### `CG4004` — Response content type added

A new media type was added to the documented response.

- **Default severity:** low  
- **Classification:** NonBreaking  
- **Change kinds:** `contenttype.response.added`

### `CG4005` — Response header removed

A documented response header disappeared.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `header.response.removed`

### `CG4006` — Response header added

A new response header was documented.

- **Default severity:** low  
- **Classification:** NonBreaking  
- **Change kinds:** `header.response.added`

### `CG4007` — Response header required-ness changed

The required flag of a response header changed.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `header.response.required.changed`

## Schema

| Rule | Title | Default severity | Classification |
|------|-------|------------------|----------------|
| `CG5001` | Schema type changed | critical | Breaking |
| `CG5002` | Schema format changed | medium | PotentiallyBreaking |
| `CG5003` | Enum value removed | high | Breaking |
| `CG5004` | Enum value added | medium | PotentiallyBreaking |
| `CG5005` | Const value changed | high | Breaking |
| `CG5006` | Validation constraint changed | high | Breaking |
| `CG5007` | Nullability changed | high | Breaking |
| `CG5008` | Array item schema changed | info | NonBreaking |
| `CG5009` | additionalProperties changed | medium | PotentiallyBreaking |
| `CG5010` | Schema composition changed | verify | Unknown |
| `CG5011` | Discriminator changed | high | Breaking |
| `CG5012` | readOnly/writeOnly flag changed | low | PotentiallyBreaking |
| `CG5013` | Schema deprecated | medium | PotentiallyBreaking |
| `CG5014` | Default value changed | verify | Unknown |
| `CG5015` | Required property changed | high | Breaking |
| `CG5017` | Property removed | medium | PotentiallyBreaking |
| `CG5018` | Property renamed | medium | Breaking |
| `CG5020` | Optional property added | info | NonBreaking |
| `CG5022` | Unresolved reference changed | verify | Unknown |

### `CG5001` — Schema type changed

The JSON type of a schema changed (string → number, object → array, ...).

- **Default severity:** critical  
- **Classification:** Breaking  
- **Change kinds:** `schema.type.changed`

### `CG5002` — Schema format changed

The format of a schema changed (date ↔ date-time, int32 ↔ int64, ...).

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `schema.format.changed`

### `CG5003` — Enum value removed

An enum value disappeared from a schema. Direction-aware: request enums contract acceptance; response enums narrow possible outputs.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `schema.enum.value.removed`

### `CG5004` — Enum value added

A new enum value appeared. Direction-aware: accepting more input is safe; producing new outputs can break strict readers.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `schema.enum.value.added`

### `CG5005` — Const value changed

A const-constrained value changed.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `schema.const.changed`

### `CG5006` — Validation constraint changed

A numeric/string/array/object constraint changed (minimum, maximum, minLength, maxLength, pattern, minItems, maxItems, uniqueItems, multipleOf, minProperties, maxProperties). Tightening request constraints rejects old payloads; widening response constraints produces new outputs readers must handle.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `schema.constraint.changed`

### `CG5007` — Nullability changed

Whether null is allowed changed. Request: nullable→non-nullable rejects null-sending clients. Response: non-nullable→nullable forces readers to handle nulls.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `schema.nullable.changed`

### `CG5008` — Array item schema changed

The element schema of an array changed. Element-level details are reported by their own rules (CG50xx), so this is a summary marker.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `schema.items.changed`

### `CG5009` — additionalProperties changed

Whether extra properties are allowed (or how they are validated) changed. Closing an object rejects unknown fields sent by clients.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `schema.additionalProperties.changed`

### `CG5010` — Schema composition changed

allOf/oneOf/anyOf/not membership changed structurally.

- **Default severity:** verify  
- **Classification:** Unknown  
- **Change kinds:** `schema.composition.changed`

### `CG5011` — Discriminator changed

The discriminator (propertyName or mapping) of a polymorphic schema changed. Property changes and mapping removals break payload routing; purely additive mapping extensions do not.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `schema.discriminator.changed`

### `CG5012` — readOnly/writeOnly flag changed

The readOnly or writeOnly classification of a property changed.

- **Default severity:** low  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `schema.readonly.changed`

### `CG5013` — Schema deprecated

A schema (or property) was marked deprecated.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `schema.deprecated.changed`

### `CG5014` — Default value changed

The default value of a property changed.

- **Default severity:** verify  
- **Classification:** Unknown  
- **Change kinds:** `schema.default.changed`

### `CG5015` — Required property changed

A property entered or left the 'required' list. Direction decides impact: requiring input properties breaks senders; removing required output properties breaks readers.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `schema.required.changed`

### `CG5017` — Property removed

A property disappeared from a schema.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `schema.property.removed`

### `CG5018` — Property renamed

A property appears to have been renamed (same type and highly similar structure).

- **Default severity:** medium  
- **Classification:** Breaking  
- **Change kinds:** `schema.property.renamed`

### `CG5020` — Optional property added

A new optional property appeared in a schema.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `schema.property.added`

### `CG5022` — Unresolved reference changed

A subtree was left un-resolved because a processing-safety budget (limits.maxNodes / limits.maxResolvedRefs) tripped, so the two sides cannot be compared in detail. This finding reports that the targets are not provably identical.

- **Default severity:** verify  
- **Classification:** Unknown  
- **Change kinds:** `schema.unresolved.changed`

## Security

| Rule | Title | Default severity | Classification |
|------|-------|------------------|----------------|
| `CG6001` | Authentication requirement added | high | Breaking |
| `CG6002` | Authentication requirement removed | medium | PotentiallyBreaking |
| `CG6003` | OAuth scope added to requirement | medium | Breaking |
| `CG6004` | OAuth scope removed from requirement | low | NonBreaking |
| `CG6005` | Security scheme removed | high | Breaking |
| `CG6006` | Security scheme added | info | Informational |
| `CG6007` | Security scheme type changed | high | Breaking |
| `CG6008` | API key location changed | high | Breaking |
| `CG6009` | HTTP security scheme changed | high | Breaking |
| `CG6010` | Bearer format changed | low | PotentiallyBreaking |
| `CG6011` | OpenID Connect URL changed | medium | PotentiallyBreaking |
| `CG6012` | OAuth flow changed | medium | PotentiallyBreaking |
| `CG6013` | OAuth scope definition removed | medium | PotentiallyBreaking |
| `CG6014` | OAuth scope definition added | info | NonBreaking |
| `CG6015` | API key name changed | high | Breaking |

### `CG6001` — Authentication requirement added

An operation now requires authentication where it previously was public (or required fewer credentials).

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `security.requirement.added`

### `CG6002` — Authentication requirement removed

A previously required authentication scheme is no longer documented for this operation.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `security.requirement.removed`

### `CG6003` — OAuth scope added to requirement

An OAuth scope was added to an effective security requirement, so tokens must now carry it.

- **Default severity:** medium  
- **Classification:** Breaking  
- **Change kinds:** `security.scope.added`

### `CG6004` — OAuth scope removed from requirement

An OAuth scope is no longer demanded by this operation's security requirement.

- **Default severity:** low  
- **Classification:** NonBreaking  
- **Change kinds:** `security.scope.removed`

### `CG6005` — Security scheme removed

A named security scheme disappeared from components.securitySchemes / securityDefinitions.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `security.scheme.removed`

### `CG6006` — Security scheme added

A new security scheme definition was added.

- **Default severity:** info  
- **Classification:** Informational  
- **Change kinds:** `security.scheme.added`

### `CG6007` — Security scheme type changed

The type of a security scheme changed (e.g. apiKey → http, http → oauth2).

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `security.scheme.type.changed`

### `CG6008` — API key location changed

The in (header/query/cookie) of an apiKey scheme changed.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `security.apikey.location.changed`

### `CG6009` — HTTP security scheme changed

The scheme of an HTTP security scheme changed (e.g. basic → bearer).

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `security.http.scheme.changed`

### `CG6010` — Bearer format changed

The bearerFormat hint of an HTTP bearer scheme changed.

- **Default severity:** low  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `security.bearerformat.changed`

### `CG6011` — OpenID Connect URL changed

The openIdConnectUrl of a scheme changed.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `security.openid.url.changed`

### `CG6012` — OAuth flow changed

An OAuth flow was added or removed, or an authorization/token/refresh URL of a flow changed.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `security.flow.url.changed`

### `CG6013` — OAuth scope definition removed

A documented OAuth scope definition was removed from a flow.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `security.flow.scope.def.removed`

### `CG6014` — OAuth scope definition added

A new OAuth scope definition was documented in a flow.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `security.flow.scope.def.added`

### `CG6015` — API key name changed

The documented name of an apiKey (the header/query/cookie parameter carrying the key) changed.

- **Default severity:** high  
- **Classification:** Breaking  
- **Change kinds:** `security.apikey.name.changed`

## Metadata

| Rule | Title | Default severity | Classification |
|------|-------|------------------|----------------|
| `CG1010` | Servers changed | medium | PotentiallyBreaking |
| `CG5019` | Example changed | info | Informational |
| `CG7001` | Info version changed | info | Informational |
| `CG7002` | Title changed | info | Informational |
| `CG7003` | operationId changed | medium | PotentiallyBreaking |
| `CG7004` | operationId removed | medium | PotentiallyBreaking |
| `CG7005` | operationId added | info | NonBreaking |
| `CG9001` | Expired suppression | low | Informational |
| `CG9002` | Stale baseline entries | info | Informational |
| `CG9003` | Changes without an assigned rule | verify | Unknown |

### `CG1010` — Servers changed

The set of advertised server URLs changed between contracts.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `servers.changed`

### `CG5019` — Example changed

Documentation examples changed. No wire-format impact.

- **Default severity:** info  
- **Classification:** Informational  
- **Change kinds:** `schema.example.changed`

### `CG7001` — Info version changed

The info.version field of the document changed.

- **Default severity:** info  
- **Classification:** Informational  
- **Change kinds:** `meta.info.version.changed`

### `CG7002` — Title changed

The info.title of the document changed.

- **Default severity:** info  
- **Classification:** Informational  
- **Change kinds:** `meta.title.changed`

### `CG7003` — operationId changed

The operationId of an operation changed.

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `operation.operationId.changed`

### `CG7004` — operationId removed

An operationId was removed from an operation (now unnamed).

- **Default severity:** medium  
- **Classification:** PotentiallyBreaking  
- **Change kinds:** `operation.operationId.removed`

### `CG7005` — operationId added

A previously unnamed operation received an operationId.

- **Default severity:** info  
- **Classification:** NonBreaking  
- **Change kinds:** `operation.operationId.added`

### `CG9001` — Expired suppression

A configured suppression matched this finding, but its expiry date has passed, so the finding is reported again.

- **Default severity:** low  
- **Classification:** Informational  
- **Change kinds:** (engine-emitted)

### `CG9002` — Stale baseline entries

One or more baseline fingerprints no longer match any current finding.

- **Default severity:** info  
- **Classification:** Informational  
- **Change kinds:** (engine-emitted)

### `CG9003` — Changes without an assigned rule

Structural changes were detected whose change kinds no registered rule evaluates.

- **Default severity:** verify  
- **Classification:** Unknown  
- **Change kinds:** (engine-emitted)
