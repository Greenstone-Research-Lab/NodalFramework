# Architecture quality gates

Nodal treats architecture as executable product behavior rather than a review-time preference.
The repository uses three complementary controls:

1. `Nodal.ArchitectureTests` enforces dependency direction and detects product-layer cycles.
2. `Nodal.ProviderContractTests` verifies that every provider honors the same portable runtime contracts.
3. SonarQube Cloud compares the analyzed structure with the intended architecture below.

## Intended product architecture

```text
Nodal.Core
├── Nodal.Migrations
├── Nodal.Analytics
├── Nodal.Import
│   ├── Nodal.Import.Csv
│   └── Nodal.Import.Relational
├── Nodal.Neo4j
├── Nodal.TigerGraph
└── Nodal.Tool
    ├── Nodal.Migrations
    ├── Nodal.Import
    ├── Nodal.Import.Csv
    └── Nodal.Import.Relational
```

Arrows point from an outer product package toward an allowed dependency. `Nodal.Core` has no
outgoing product dependency. Provider packages are peers and must never reference one another.
Import packages remain provider-neutral. `Nodal.Tool` composes portable workflows and must not
reference concrete graph providers.

## SonarQube Cloud configuration

In the SonarQube Cloud project, open **Architecture > Intended architecture** and model these
top-level containers:

| Container | Allowed outgoing product dependencies |
| --- | --- |
| `Nodal.Core` | none |
| `Nodal.Migrations` | `Nodal.Core` |
| `Nodal.Analytics` | `Nodal.Core` |
| `Nodal.Import` | `Nodal.Core` |
| `Nodal.Import.Csv` | `Nodal.Import` |
| `Nodal.Import.Relational` | `Nodal.Import` |
| `Nodal.Neo4j` | `Nodal.Core` |
| `Nodal.TigerGraph` | `Nodal.Core` |
| `Nodal.Tool` | `Nodal.Core`, `Nodal.Migrations`, `Nodal.Import`, `Nodal.Import.Csv`, `Nodal.Import.Relational` |

Map each container to its matching `src/<container>` directory, define only the relationships in
the table, and save the model. Sonar then reports wrong locations, forbidden dependencies, and
dependency tangles through the existing pull-request analysis.

The executable tests are the repository source of truth. Any accepted boundary change updates
this document, the Sonar intended architecture, and the corresponding architecture tests in the
same pull request.
