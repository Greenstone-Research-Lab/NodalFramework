---
title: Change tracking
description: Understand identity resolution, snapshots, modified properties, and no-tracking queries.
---

# Change tracking

Tracked queries use identity resolution: the same graph identity reuses the same object instance within a context. Original mapped-property snapshots let `SaveChangesAsync` detect ordinary mutable POCO changes without an explicit `Update` call.

For high-volume or read-only work, call `AsNoTracking()`. Explicit controls include:

- `AutoDetectChangesEnabled` and `DetectChanges()`
- `Entry(entity)` and property-level `IsModified`
- `Attach`, `Detach`, and `ReloadAsync`
- state inspection through `ChangeTracker.Entries(...)`

A context represents a bounded unit of work. Avoid using one context as an unbounded cache for long-running graph scans.
