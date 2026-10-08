using AdminDesk.Application.Engine;
using AdminDesk.Domain.Engine;
using AdminDesk.SharedKernel.Constants;
using AdminDesk.SharedKernel.Enums;

namespace AdminDesk.Application.Requests;

// Which actions the viewer is offered on a request. Pure: the engine still decides what it accepts,
// and both use the same status gate and the same step-kind pairing.
public static class AllowedActionsCalculator
{
    public static IReadOnlyList<RequestAction> Compute(
        ActorContext viewer,
        RequestStatus status,
        long requesterEmployeeId,
        int? currentStepSeq,
        StepType? currentStepType,
        IReadOnlyList<ActorRow> activeActors,
        bool cancelLocked = false,
        bool currentStepPending = true)
    {
        if (!RequestWorkflowService.ActionableStatuses.Contains(status))
        {
            return Array.Empty<RequestAction>();
        }

        var allowed = new List<RequestAction>();

        var isActor = currentStepSeq is { } seq
            && activeActors.Any(a => a.StepSeq == seq && Matches(a, viewer));

        if (isActor && currentStepType is { } type
            && !TransitionRules.BarredAsRequester(type, viewer.EmployeeId, requesterEmployeeId))
        {
            foreach (var action in new[] { RequestAction.Approve, RequestAction.Reject, RequestAction.Complete })
            {
                if (TransitionRules.CanAct(type, action))
                {
                    allowed.Add(action);
                }
            }
        }

        if (viewer.EmployeeId is { } employeeId && employeeId == requesterEmployeeId && TransitionRules.CanCancel(status) && !cancelLocked)
        {
            allowed.Add(RequestAction.Cancel);
        }

        // The Admin may reject any in-progress request at its current step. A request with no pending
        // step cannot be rejected, which is also what the engine requires. Cancel stays with the requester alone.
        if (viewer.Roles.Overlaps(Roles.RequestOverride) && currentStepSeq is not null && currentStepPending && !allowed.Contains(RequestAction.Reject))
        {
            allowed.Add(RequestAction.Reject);
        }

        return allowed;
    }

    // "Approve" at an approval step, the step's own label (else "Complete") at a task step.
    public static string? PrimaryLabel(RequestStatus status, StepType? stepType, string? actionLabel)
    {
        if (status != RequestStatus.InProgress || stepType is null)
        {
            return null;
        }
        if (stepType == StepType.Approval)
        {
            return "Approve";
        }
        return string.IsNullOrWhiteSpace(actionLabel) ? "Complete" : actionLabel;
    }

    private static bool Matches(ActorRow row, ActorContext viewer) =>
        (row.EmployeeId is { } employeeId && viewer.EmployeeId == employeeId)
        || (row.RoleName is { } role && viewer.Roles.Contains(role));
}
