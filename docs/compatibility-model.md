# The compatibility model

OpenAPI Sentinel's core premise: **the same structural edit can be safe in one direction and
breaking in the other.** A contract describes two one-way promises:

- **Request direction** — what the *server accepts* from clients. Clients depend on the
  server accepting everything they may send.
- **Response direction** — what the *server produces*. Clients depend on the server producing
  something they can consume.

## Direction-aware classification

| Change | Request side | Response side |
|--------|--------------|---------------|
| New optional property | safe (additive) | usually safe; strict deserializers may care → potentially breaking |
| Property becomes required | **breaking** — old payloads rejected | safe for readers; a stronger server commitment |
| Property becomes optional | low impact (clients keep sending it) | **breaking** if clients depended on presence |
| Enum value removed | **breaking** — clients sending it get rejected | non-breaking — that value simply stops appearing |
| Enum value added | safe (more accepted inputs) | **potentially breaking** — clients see unknown values |
| Range/limit tightened (`minimum`, `maxLength`, `maxItems`…) | **breaking** | informational |
| Range widened | safe | **potentially breaking** when it widens accepted output variance |
| `nullable: true` removed | **breaking** — nulls now rejected | safe — nulls stop appearing |

Severity and compatibility are **independent axes**:

- Severity (critical, high, medium, low, info, verify) = how much attention it deserves.
- Compatibility (breaking, potentially breaking, non-breaking, informational, unknown) =
  the contractual direction of the change.

A critical finding can be non-breaking (a documented endpoint removal you already coordinated);
a low-severity finding can still be flagged breaking for audit purposes.

## Format and numeric-type directionality

Precision pairs follow the same model:

- **Request** widening (`int32→int64`, `float→double`, `date→date-time`) is safe — the server
  still accepts everything it used to. Narrowing is **breaking** (`int64→int32` rejects large values).
- **Response** widening is **potentially breaking** (values may exceed client parsers);
  narrowing is safe.
- Within the numeric family, `integer→number` widens and `number→integer` narrows; cross-family
  type changes remain critical.

`additionalProperties` follows the mirror pattern: closing an object breaks request senders
with extension fields; opening a response object may surface fields strict readers do not expect.
Schema-valued transitions are reported as Verify. Absent vs explicit `true` are treated as
identical and never reported.

## `Verify`: honest uncertainty

Some transitions are genuinely undecidable statically:

- `pattern` regex changes (`^a` → `^[ab]` — is that wider or narrower?)
- `multipleOf` changes
- composition algebra changes (`allOf` member edits)
- default-value changes that alter server behaviour

These are emitted with severity `verify` and compatibility `unknown` rather than guessed.
Verify findings never fail policy by themselves but appear prominently ("Needs review").

## Rename detection

Renames are only claimed with high confidence: same primary type AND ≥80% child-property
Jaccard similarity, or an obvious typographic variant. Otherwise the tool reports
remove + add so nothing is hidden behind a speculative rename.

## Wildcard responses

Removing `404` while a `default` or `4XX` response remains is not reported as a status-code
removal: the wildcard still documents the behaviour. Coverage logic accounts for
`default`, `2XX`, `4XX`, `5XX`.

## Version recommendation

| Bump | Trigger |
|------|---------|
| MAJOR | any Breaking finding at High/Critical severity |
| MINOR | additive changes only (new endpoints, operations, content types, status codes) |
| PATCH | only Low/Info findings |
| NONE | no findings, or mixed signals that need human review |

The recommendation is advisory output, never enforced implicitly.

## Worked example

```yaml
# v1
parameters:
  - name: customerId
    in: query
    required: false
```

```yaml
# v2 — same location, now mandatory
parameters:
  - name: customerId
    in: query
    required: true
```

Reported as `CG2003` (Parameter became required), High/Breaking:

> Existing clients that omit customerId may begin receiving validation errors.
> Introduce the field as optional first, migrate consumers, then enforce the requirement
> in a later API version.

The mirrored change on a *response header* would classify entirely differently — that is
the point of the model.
