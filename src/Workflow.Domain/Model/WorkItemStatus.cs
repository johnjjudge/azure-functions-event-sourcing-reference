namespace Workflow.Domain.Model;

/// <summary>
/// Coarse processing status persisted on the Table Storage intake work item.
/// </summary>
public enum WorkItemStatus
{
    /// <summary>
    /// The work item has not yet been claimed by the workflow.
    /// </summary>
    Unprocessed = 0,

    /// <summary>
    /// The work item has been claimed and processing has started.
    /// </summary>
    InProgress = 1,

    /// <summary>
    /// The external service reached a terminal passing status.
    /// </summary>
    Pass = 2,

    /// <summary>
    /// The external service reached a terminal failing status.
    /// </summary>
    Fail = 3,
}
