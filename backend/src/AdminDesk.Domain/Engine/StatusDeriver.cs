using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Domain.Engine;

public static class StatusDeriver
{
    // Closed requests are Approved. Otherwise the approval status is Pending while an
    // approval step is Pending, and Approved when none is (including a module with no
    // approval step). An Upcoming step is undecided and is not counted. A rejection, at an
    // approval step or a task step and by anyone, is recorded by the caller as Rejected/Rejected, so
    // a rejected request never counts as Approved or Pending; a cancellation keeps the approval status.
    public static (RequestStatus Status, ApprovalStatus Approval) Derive(IReadOnlyList<PlannedStep> steps, bool isClosed)
    {
        if (isClosed)
        {
            return (RequestStatus.Closed, ApprovalStatus.Approved);
        }
        var approvalPending = steps.Any(s => s.Type == StepType.Approval && s.State == StepState.Pending);
        return (RequestStatus.InProgress, approvalPending ? ApprovalStatus.Pending : ApprovalStatus.Approved);
    }
}

public static class TransitionRules
{
    public static bool IsTerminal(RequestStatus status) =>
        status is RequestStatus.Rejected or RequestStatus.Closed or RequestStatus.Cancelled;

    // The requester may cancel at any step while the request is in progress.
    public static bool CanCancel(RequestStatus status) => status == RequestStatus.InProgress;

    // True when a step flagged as locking cancellation has been completed.
    public static bool CancelLocked(
        IEnumerable<AdminDesk.Domain.Definitions.StepDefinition> definitionSteps,
        IEnumerable<(string Key, StepState State)> stepStates)
    {
        var locking = definitionSteps.Where(d => d.LocksCancel).Select(d => d.Key).ToHashSet();
        return locking.Count > 0 && stepStates.Any(s => s.State == StepState.Done && locking.Contains(s.Key));
    }

    // Segregation of duties: nobody approves or rejects their own request at an approval step, even
    // when they hold the step's role. Task steps (fulfilment, confirmation) are not affected.
    public static bool BarredAsRequester(StepType stepType, long? actorEmployeeId, long requesterEmployeeId) =>
        stepType == StepType.Approval && actorEmployeeId is { } id && id == requesterEmployeeId;

    // Approve and reject belong to approval steps, complete to task steps. Cancel is not tied to a step.
    public static bool CanAct(StepType stepType, RequestAction action) => action switch
    {
        RequestAction.Approve or RequestAction.Reject => stepType == StepType.Approval,
        RequestAction.Complete => stepType == StepType.Task,
        _ => false
    };
}
