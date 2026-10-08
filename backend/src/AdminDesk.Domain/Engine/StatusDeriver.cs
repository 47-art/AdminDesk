using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Domain.Engine;

public static class StatusDeriver
{
    // Closed requests are Approved. Otherwise the approval status is Pending while an
    // approval step is Pending, and Approved when none is (including a module with no
    // approval step). An Upcoming step is undecided and is not counted. A rejection is
    // recorded by the caller as Rejected/Rejected and a cancellation keeps the approval status.
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

    // Approve and reject belong to approval steps, complete to task steps. Cancel is not tied to a step.
    public static bool CanAct(StepType stepType, RequestAction action) => action switch
    {
        RequestAction.Approve or RequestAction.Reject => stepType == StepType.Approval,
        RequestAction.Complete => stepType == StepType.Task,
        _ => false
    };
}
