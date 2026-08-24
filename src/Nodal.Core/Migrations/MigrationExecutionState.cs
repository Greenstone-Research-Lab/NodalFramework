using System;
using System.Collections.Generic;
using System.Text;

namespace Nodal.Core.Migrations;

/// <summary>
/// Describes the lifecycle state of one migration execution.
/// </summary>
public enum MigrationExecutionState
{
    /// <summary>
    /// The migration is known but has not started.
    /// </summary>
    Pending,

    /// <summary>
    /// The migration commands are currently being executed.
    /// </summary>
    Applying,

    /// <summary>
    /// The migration completed successfully.
    /// </summary>
    Applied,

    /// <summary>
    /// The migration failed after execution started.
    /// </summary>
    Failed,

    /// <summary>
    /// The migration was explicitly reverted.
    /// </summary>
    Reverted
}

/// <summary>
/// Stores a provider-neutral migration failure without exposing credentials
/// or sensitive provider payloads.
/// </summary>
/// <param name="Message">A safe diagnostic message.</param>
/// <param name="ErrorType">The exception or provider error category.</param>
/// <param name="OccurredAt">The UTC time at which the failure was recorded.</param>
public sealed record MigrationExecutionFailure(
    string Message,
    string ErrorType,
    DateTimeOffset OccurredAt);

/// <summary>
/// Represents the persisted lifecycle information for one migration.
/// </summary>
/// <param name="Id">The stable migration identifier.</param>
/// <param name="Checksum">The canonical migration checksum.</param>
/// <param name="State">The current execution state.</param>
/// <param name="StartedAt">The UTC start time, when execution started.</param>
/// <param name="CompletedAt">The UTC completion time, when execution finished.</param>
/// <param name="Failure">A safe failure description, when execution failed.</param>
public sealed record MigrationHistoryEntry(
    string Id,
    string Checksum,
    MigrationExecutionState State,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? CompletedAt = null,
    MigrationExecutionFailure? Failure = null);
