using AdminDesk.SharedKernel.Enums;
using FluentValidation;

namespace AdminDesk.Application.Requests;

public sealed class CreateRequestBodyValidator : AbstractValidator<CreateRequestBody>
{
    public CreateRequestBodyValidator()
    {
        RuleFor(x => x.ModuleCode).NotEmpty().WithMessage("Choose a module.").MaximumLength(60);
        RuleFor(x => x.DefinitionId).GreaterThan(0).WithMessage("This form is out of date. Reload the page and try again.");
        RuleFor(x => x.Payload).NotNull().WithMessage("Fill in the form.");

        // Reported under the same names the engine uses for the common fields.
        When(x => x.Common is not null, () =>
        {
            RuleFor(x => x.Common.Remarks)
                .MaximumLength(1000).WithMessage("Keep the remarks to 1000 characters or fewer.")
                .OverridePropertyName("remarks");
            RuleFor(x => x.Common.Priority)
                .Must(p => string.IsNullOrWhiteSpace(p) || RequestParsing.ParseEnum<Priority>(p) is not null)
                .WithMessage("Choose a valid priority.")
                .OverridePropertyName("priority");
        });
    }
}

public sealed class ActionBodyValidator : AbstractValidator<ActionBody>
{
    public ActionBodyValidator()
    {
        RuleFor(x => x.Action)
            .Must(a => RequestParsing.ParseEnum<RequestAction>(a) is not null)
            .WithMessage("Choose a valid action.");

        RuleFor(x => x.RowVersion).GreaterThan(0).WithMessage("Reload the request and try again.");

        // Only a rejection and a cancellation carry a reason; the other actions ignore the comment.
        When(x => RequestParsing.ParseEnum<RequestAction>(x.Action) == RequestAction.Reject, () =>
        {
            RuleFor(x => x.Comment)
                .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Enter a reason so the requester knows why.")
                .Must(c => c is null || c.Trim().Length <= 1000).WithMessage("Keep the reason to 1000 characters or fewer.");
        });
        When(x => RequestParsing.ParseEnum<RequestAction>(x.Action) == RequestAction.Cancel, () =>
        {
            RuleFor(x => x.Comment)
                .Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Give a reason for cancelling.")
                .Must(c => c is null || c.Trim().Length <= 1000).WithMessage("Keep the reason to 1000 characters or fewer.");
        });

        RuleFor(x => x.Captured!)
            .Must(c => c.Count <= 20).WithMessage("Too many values were sent.")
            .When(x => x.Captured is not null)
            .OverridePropertyName("captured");
    }
}

public sealed class MineQueryValidator : AbstractValidator<MineQuery>
{
    public MineQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, RequestParsing.MaxPageSize);
        RuleFor(x => x.Q).MaximumLength(100);
        RuleFor(x => x.Module).MaximumLength(60);
        RuleFor(x => x.Dir)
            .Must(d => string.IsNullOrEmpty(d) || d.Equals("asc", StringComparison.OrdinalIgnoreCase) || d.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Direction must be asc or desc.");
        RuleForEach(x => x.Status)
            .Must(s => RequestParsing.ParseEnum<RequestStatus>(s) is not null)
            .WithMessage("Choose a valid status.");
        RuleFor(x => x.ApprovalStatus)
            .Must(s => string.IsNullOrEmpty(s) || RequestParsing.ParseEnum<ApprovalStatus>(s) is not null)
            .WithMessage("Choose a valid approval status.");
        RuleFor(x => x.From)
            .Must(d => string.IsNullOrEmpty(d) || RequestParsing.ParseDate(d) is not null)
            .WithMessage("Use the date format yyyy-MM-dd.");
        RuleFor(x => x.To)
            .Must(d => string.IsNullOrEmpty(d) || RequestParsing.ParseDate(d) is not null)
            .WithMessage("Use the date format yyyy-MM-dd.");
    }
}

public sealed class InboxQueryValidator : AbstractValidator<InboxQuery>
{
    public InboxQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, RequestParsing.MaxPageSize);
        RuleFor(x => x.Module).MaximumLength(60);
        RuleFor(x => x.Requester).MaximumLength(100);
        RuleFor(x => x.Priority)
            .Must(p => string.IsNullOrEmpty(p) || RequestParsing.ParseEnum<Priority>(p) is not null)
            .WithMessage("Choose a valid priority.");
    }
}
