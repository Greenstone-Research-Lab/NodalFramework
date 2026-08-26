# Nodal Framework Engineering Rules

This document is the engineering contract for Nodal Framework. It applies to
all product code, provider packages, migration code, tests, samples, tooling,
documentation examples, and GitHub Actions changes.

The purpose is to keep the framework provider-neutral, predictable, testable,
and safe to extend. A contribution that conflicts with these rules must include
an explicit architecture decision record and approval in the pull request.

## 1. Architectural boundaries

- `Nodal.Core` contains provider-neutral contracts, metadata, expressions,
  execution abstractions, tracking, mutations, analytics contracts, and
  migration contracts.
- Provider packages depend inward on `Nodal.Core`; `Nodal.Core` must never
  depend on Neo4j, TigerGraph, HTTP clients, GSQL, Bolt, or provider SDKs.
- `Nodal.Migrations` contains portable migration orchestration. Provider
  dialects and executors remain in their provider packages.
- `Nodal.Analytics` is an optional layer above providers. It must not become a
  hidden dependency of `Nodal.Core` or provider packages.
- Samples, benchmarks, and tests may depend on product packages but product
  code must not depend on samples or tests.
- New providers must implement existing contracts and capability declarations;
  they must not fork the common query or migration model.

## 2. SOLID and design principles

- Each type has one primary responsibility.
- Public extension points use small, cohesive interfaces.
- New providers and transports must be addable without modifying core
  provider-neutral behavior.
- Implementations must honor the semantics of their contracts. Unsupported
  operations fail explicitly before transport execution.
- Dependencies point to abstractions. Concrete transports, drivers, and
  clients are composed at the application boundary.
- Prefer composition, immutable records, and pure compilation functions over
  inheritance and global state.
- Do not introduce an abstraction only to satisfy a metric; introduce it when
  it protects a boundary or makes behavior replaceable and testable.

## 3. Public API rules

- All public types, members, constructors, and extension methods require
  professional XML documentation.
- Public APIs must provide a practical usage example in documentation or an
  executable sample.
- Async APIs return `Task` or `ValueTask` consistently with the surrounding
  contract and accept `CancellationToken` for I/O or potentially long work.
- Do not expose provider payload types from provider-neutral APIs.
- Do not silently change public behavior. Breaking changes require a documented
  migration note and a deliberate pre-release decision.
- Nullable reference types remain enabled. Avoid null-forgiving operators
  unless the invariant is proven and documented.

## 4. Provider behavior

- Provider compilers must parameterize user values and escape identifiers.
- Provider-specific syntax belongs in the provider dialect/compiler, never in
  application-facing query expressions.
- Capability metadata must reflect the installed and verified provider
  features, not merely what the compiler could theoretically generate.
- A missing capability must produce a clear exception before any database call.
- Provider differences must be documented in the compatibility matrix.
- Native pushdown is allowed only when its semantics match the Nodal contract;
  otherwise the operation must remain unsupported or use an explicitly named
  provider extension.
- Connection pools, `HttpClient` lifetimes, authentication, and retry policy
  remain under the host/provider boundary and must not be recreated per query.

### Capability policy

- Every provider feature must be classified as either **portable** or
  **provider-specific**.
- Portable features remain behind provider-neutral contracts. If the selected
  provider or its installed extensions cannot satisfy the request, execution
  must fail before transport with a capability-specific exception.
- Provider-specific features may be exposed through typed provider extensions,
  but their dependency must be explicit. Application code using such an
  extension is intentionally bound to that provider capability.
- Provider changes must never silently emulate, ignore, or downgrade a required
  capability. A provider switch that removes a required feature must fail fast
  at compile time, startup, planning, or pre-transport validation.
- Capability checks must include the provider name, verified provider version,
  requested capability, and a safe reason for unavailability.
- Prefer a framework-specific `NodalCapabilityNotSupportedException` derived
  from `NotSupportedException` so callers can handle unsupported behavior
  without parsing error strings.
- Capability metadata must describe the installed and verified feature set; a
  compiler-supported algorithm is not automatically an available capability.
- Portable analytics and migration APIs must not call provider-specific
  extensions through runtime type checks such as `provider.Name == "Neo4j"`.
- Provider extensions belong in the provider package or a clearly named
  provider-extension package; they must not leak vendor types into
  `Nodal.Core`.

## 5. Migration safety

- Migration planning is side-effect free.
- Migration IDs are stable, unique, ordered, and never reused.
- Applied migrations are checksummed; checksum drift fails before execution.
- Destructive operations require explicit visibility and approval.
- Provider-neutral operations are compiled by a provider dialect; raw provider
  commands are not accepted as the default application API.
- Rollback behavior must be explicit. Irreversible migrations must declare it.
- Migration history must distinguish pending, applying, applied, failed, and
  reverted states when the provider boundary permits it.
- A provider must not claim migration execution support without an explicit
  administrative transport where the database requires one.

## 6. Testing and quality gates

- Product code targets at least 95% line coverage; critical compiler and
  migration paths require branch and negative-case coverage.
- Every provider feature requires compiler tests, unsupported-capability tests,
  and transport contract tests.
- Live provider tests are environment-gated and must state the exact database
  version they verify.
- Architecture tests enforce dependency direction and provider isolation.
- Public examples should compile and run in CI where practical.
- Formatting, build, tests, coverage, package validation, and security checks
  must pass before merge.
- Benchmarks must publish method, dataset, runtime, provider version, and
  allocation information; benchmark claims must not be presented without
  reproducible inputs.

## 7. Performance and reliability

- Avoid unbounded graph extraction, all-pairs materialization, and accidental
  client-side emulation of provider algorithms.
- Support cancellation and bounded limits at every database or network edge.
- Do not allocate per-record reflection or serialization state in hot paths when
  a cached or compiled alternative is available.
- Retry only operations that are safe to retry and document idempotency.
- Preserve identity, relationship direction, parallel edges, and provider
  response ordering where the contract requires it.

## 8. Security and data handling

- Credentials, tokens, and connection strings never appear in source,
  migration plans, benchmark output, logs, or committed documentation.
- Tests use disposable credentials and environment-provided secrets.
- User values are parameterized; identifiers are validated and escaped.
- Diagnostics must avoid logging sensitive graph data by default.
- Dependency and container updates must pass vulnerability scanning.

## 9. Documentation and contribution workflow

- English is the canonical language for source documentation and the public
  documentation site.
- Architecture decisions, provider limitations, and compatibility claims must
  be documented close to the affected feature.
- Every change should state its scope, tests, provider impact, and migration or
  compatibility impact in the pull request.
- Contributors work on feature branches and submit pull requests to
  `developer`; promotion to `staging` and `master` follows the protected branch
  workflow.
- Do not commit local TODO notes, credentials, generated build output, or
  environment-specific configuration.

## 10. Review checklist

Before requesting review, confirm:

- [ ] The change respects the dependency direction.
- [ ] Unsupported provider behavior fails visibly.
- [ ] Public APIs and examples are documented.
- [ ] Tests cover success, failure, cancellation, and capability boundaries.
- [ ] Coverage and formatting gates pass.
- [ ] No credentials, generated artifacts, or local TODO files are included.
- [ ] Compatibility, migration, and documentation impact are described.

When a rule must be intentionally broken, record the reason, alternatives
considered, scope, and removal or review condition in an architecture decision
record.
