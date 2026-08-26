---
title: Analytics boundary
description: Understand the public Nodal analytics contract boundary.
---

# Analytics boundary

`Nodal.Analytics` is an optional public layer above `Nodal.Core` and the
database-provider packages. It provides provider-neutral contracts and
capability-aware integration for analytics that supported graph platforms can
execute.

It is not a provider, does not become a dependency of provider packages, and
does not promise that every provider offers the same analytics capability.
Applications should inspect the compatibility matrix and handle unavailable
capabilities explicitly.

Advanced analytics implementations are outside the public package and
documentation contract. Their availability, licensing, configuration, and
operational behavior are governed separately and must not be inferred from a
public provider capability declaration.
